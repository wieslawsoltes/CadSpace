using System.Collections.Immutable;
using System.Globalization;
using CadSpace.Geometry;
using CadSpace.Model;

namespace CadSpace.Dxf;

internal static partial class DxfRecordEditing
{
    private static bool SplineGeometryEqual(SplineEntity a, SplineEntity b) => a.Degree == b.Degree && a.Closed == b.Closed && a.Periodic == b.Periodic &&
        a.ControlPoints == b.ControlPoints && a.Knots == b.Knots && a.Weights == b.Weights;

    private static bool TryWriteSpline(SplineEntity after, SplineEntity before, ImmutableArray<DxfPair> raw, Action<string> warn, out string text)
    {
        text = "";
        var records = DxfEntityReader.Records(raw).ToArray();
        if (records.Length != 1) return false;
        var semantic = SemanticPairs(raw);
        string S(int code, string fallback = "") => semantic.FirstOrDefault(p => p.Code == code).Value ?? fallback;
        if (S(0).Trim() != "SPLINE") return false;
        var marked = semantic.Any(p => p.Code == 100);
        if (marked && semantic.Count(p => p.Code == 100 && p.Value == "AcDbSpline") != 1) return false;
        // A control edit must not leave contradictory fit-point/tangent constraints in the native record.
        if (S(74, "0").Trim() != "0" || semantic.Any(p => p.Code is 11 or 12 or 13)) return false;
        if (!int.TryParse(S(70, "0"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var flags)) return false;
        if ((flags & 16) != 0 && after.Degree != 1) return false;
        if ((flags & 8) != 0 || semantic.Any(p => p.Code == 210))
        {
            var hasNormal = semantic.Any(p => p.Code == 210);
            if (!hasNormal && before.ControlPoints.Any(p => Math.Abs(p.Z - before.ControlPoints[0].Z) > 1e-9)) return false;
            double N(int c) => double.Parse(S(c, "0"), CultureInfo.InvariantCulture);
            var normal = hasNormal ? new Vec3(N(210), N(220), N(230)) : Vec3.UnitZ;
            if (!normal.IsFinite || !double.IsFinite(normal.Length) || normal.Length < 1e-12) return false;
            normal = normal.Normalized;
            var origin = before.ControlPoints[0];
            foreach (var point in after.ControlPoints)
            {
                var delta = point - origin;
                if (!delta.IsFinite || !double.IsFinite(delta.Length) || Math.Abs(delta.Dot(normal)) > 1e-8 * Math.Max(1, delta.Length)) return false;
            }
        }
        var geometry = ImmutableArray.CreateBuilder<DxfPair>();
        void Pair(int c, object v) => geometry.Add(new(c, Convert.ToString(v, CultureInfo.InvariantCulture)!));
        Pair(70, (flags & ~7) | (after.Closed ? 1 : 0) | (after.Periodic ? 2 : 0) | (after.Weights.IsEmpty ? 0 : 4));
        Pair(71, after.Degree); Pair(72, after.Knots.Length); Pair(73, after.ControlPoints.Length); Pair(74, 0);
        foreach (var knot in after.Knots) Pair(40, knot);
        foreach (var weight in after.Weights) Pair(41, weight);
        foreach (var point in after.ControlPoints) { Pair(10, point.X); Pair(20, point.Y); Pair(30, point.Z); }
        var output = ImmutableArray.CreateBuilder<DxfPair>(); var active = !marked; var extended = false; var depth = 0; var inserted = false;
        foreach (var pair in raw)
        {
            if (!extended && pair.Code == 102)
            { if (pair.Value.StartsWith('{')) depth++; else if (pair.Value == "}") depth--; output.Add(pair); continue; }
            if (depth == 0 && pair.Code == 1001) extended = true;
            if (depth == 0 && !extended && pair.Code == 100) active = pair.Value == "AcDbSpline";
            if (depth == 0 && !extended && active && pair.Code is 70 or 71 or 72 or 73 or 74 or 40 or 41 or 10 or 20 or 30)
            { if (!inserted) { output.AddRange(geometry); inserted = true; } }
            else output.Add(pair);
        }
        if (!inserted) return false;
        // Only common fields now differ, allowing the regular patcher to preserve root metadata as well.
        var styleOnly = after with { Degree = before.Degree, ControlPoints = before.ControlPoints, Knots = before.Knots,
            Weights = before.Weights, Closed = before.Closed, Periodic = before.Periodic };
        if (!TryWrite(styleOnly, before, output.ToImmutable(), warn, out text)) return false;
        warn("Edited SPLINE: native control points, knots and weights updated; application data and unedited fields retained. External associations and caches are not regenerated.");
        return true;
    }
}
