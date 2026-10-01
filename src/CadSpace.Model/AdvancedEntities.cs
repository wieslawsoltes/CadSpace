using System.Collections.Immutable;
using CadSpace.Geometry;

namespace CadSpace.Model;

/// <summary>A nonplanar polyline. Vertices are WCS; no implicit flattening to XY.</summary>
public sealed record Polyline3DEntity(ImmutableArray<Vec3> Points, bool Closed = false) : Entity
{
    public override string Kind => "POLYLINE";
    public bool ContinuousLinetype { get; init; }
}

/// <summary>Rational B-spline with an explicit, nondecreasing knot vector.</summary>
public sealed record SplineEntity(int Degree, ImmutableArray<Vec3> ControlPoints, ImmutableArray<double> Knots, ImmutableArray<double> Weights, bool Closed = false, bool Periodic = false) : Entity
{
    public override string Kind => "SPLINE";
}

/// <summary>Local geometry plus an affine placement. Used for OCS and arbitrary 3D transforms.</summary>
public sealed record PlacedEntity(Entity Geometry, Transform3 Placement) : Entity
{
    public override string Kind => Geometry.Kind;
}

/// <summary>Explicit display children of a compound DXF record, such as an attributed INSERT.</summary>
public sealed record CompositeEntity(string DxfType, ImmutableArray<Entity> Children, string SourceRecord = "") : Entity
{
    public override string Kind => DxfType;
}

/// <summary>One family of hatch lines, expressed in the hatch plane; angle is degrees.</summary>
public sealed record HatchPatternLine(double Angle, Vec3 Origin, Vec3 Offset, ImmutableArray<double> Dashes);

/// <summary>Odd-even polygon loops, with bulges retained for native DXF polyline boundaries.</summary>
public sealed record HatchRegionEntity(ImmutableArray<ImmutableArray<PolyVertex>> Loops, bool Solid, ImmutableArray<HatchPatternLine> Pattern, string PatternName = "SOLID", bool SampledBoundary = false) : Entity
{
    public override string Kind => "HATCH";
    public int IslandStyle { get; init; }
    public HatchGradient? Gradient { get; init; }
}

public static class AdvancedGeometry
{
    public const int MaximumCurvePoints = 32768;
    public static bool IsAdvanced(Entity entity) => entity is Polyline3DEntity or SplineEntity or PlacedEntity or CompositeEntity or HatchRegionEntity;
    public static IEnumerable<Vec3> Anchors(Entity e) => e switch
    {
        Polyline3DEntity p => p.Points,
        SplineEntity s => s.ControlPoints,
        PlacedEntity p => EntityGeometry.Anchors(p.Geometry).Select(p.Placement.Point),
        CompositeEntity c => c.Children.SelectMany(EntityGeometry.Anchors),
        HatchRegionEntity h => h.Loops.SelectMany(l => l.Select(v => v.Position)),
        _ => []
    };
    public static void Validate(Entity entity, int depth = 0)
    {
        if (depth > 32) throw new ArgumentException("Compound geometry exceeds 32 levels.");
        switch (entity)
        {
            case SplineEntity spline: ValidateSpline(spline); break;
            case Polyline3DEntity poly when poly.Points.Length < 2 || poly.Points.Length > 1000000:
                throw new ArgumentException("A 3D polyline needs between 2 and 1,000,000 vertices.");
            case PlacedEntity placed:
                var t = placed.Placement;
                if (!t.X.IsFinite || !t.Y.IsFinite || !t.Z.IsFinite || !t.Origin.IsFinite || !double.IsFinite(t.Determinant) || Math.Abs(t.Determinant) < 1e-18) throw new ArgumentException("Invalid affine placement.");
                Validate(placed.Geometry, depth + 1); break;
            case CompositeEntity composite:
                if (composite.Children.Length > 100000) throw new ArgumentException("Too many compound children.");
                foreach (var child in composite.Children) Validate(child, depth + 1); break;
            case HatchRegionEntity hatch:
                hatch.Gradient?.Validate();
                if (hatch.Gradient != null && !hatch.Solid) throw new ArgumentException("A DXF gradient requires solid-fill mode.");
                if (hatch.IslandStyle is < 0 or > 2) throw new ArgumentException("Invalid hatch island style.");
                if (hatch.Loops.IsEmpty || hatch.Loops.Length > 1024 || hatch.Loops.Any(l => l.Length < 2 || l.Length > 100000 || l.Any(v => !v.Position.IsFinite || !double.IsFinite(v.Bulge)))) throw new ArgumentException("Invalid hatch boundary.");
                if (hatch.Pattern.Length > 256 || hatch.Pattern.Any(p => !double.IsFinite(p.Angle) || !p.Origin.IsFinite || !p.Offset.IsFinite || p.Dashes.Length > 256 || p.Dashes.Any(d => !double.IsFinite(d)))) throw new ArgumentException("Invalid hatch pattern.");
                break;
        }
        if (Anchors(entity).Any(p => !p.IsFinite)) throw new ArgumentException("Nonfinite advanced geometry.");
    }
    public static void ValidateSpline(SplineEntity s)
    {
        if (s.Degree is < 1 or > 16 || s.ControlPoints.Length <= s.Degree || s.ControlPoints.Length > 100000 || s.Knots.Length != s.ControlPoints.Length + s.Degree + 1) throw new ArgumentException("Invalid spline degree/control-point/knot counts.");
        if (s.ControlPoints.Any(p => !p.IsFinite) || s.Knots.Any(k => !double.IsFinite(k)) || s.Knots.Zip(s.Knots.Skip(1)).Any(p => p.First > p.Second)) throw new ArgumentException("Spline knots must be finite and nondecreasing.");
        if (s.Knots[s.Degree] >= s.Knots[s.ControlPoints.Length]) throw new ArgumentException("The spline parameter domain is empty.");
        if (!s.Weights.IsEmpty && (s.Weights.Length != s.ControlPoints.Length || s.Weights.Any(w => !double.IsFinite(w) || w <= 0))) throw new ArgumentException("Spline weights must be positive and match the control points.");
    }
    /// <summary>Validated homogeneous de Boor evaluation in double precision.</summary>
    public static Vec3 Evaluate(SplineEntity s, double parameter)
    {
        ValidateSpline(s);
        if (!double.IsFinite(parameter)) throw new ArgumentOutOfRangeException(nameof(parameter));
        return EvaluateValidated(s, parameter);
    }
    private static Vec3 EvaluateValidated(SplineEntity s, double parameter)
    {
        var n = s.ControlPoints.Length - 1; var p = s.Degree;
        var t = Math.Clamp(parameter, s.Knots[p], s.Knots[n + 1]);
        var span = n;
        if (t < s.Knots[n + 1])
        {
            int low = p, high = n + 1;
            while (high - low > 1) { var middle = (low + high) / 2; if (t < s.Knots[middle]) high = middle; else low = middle; }
            span = low;
        }
        Span<Vec3> positions = stackalloc Vec3[17]; Span<double> weights = stackalloc double[17];
        for (var j = 0; j <= p; j++) { var index = span - p + j; var w = s.Weights.IsEmpty ? 1 : s.Weights[index]; positions[j] = s.ControlPoints[index] * w; weights[j] = w; }
        for (var level = 1; level <= p; level++) for (var j = p; j >= level; j--)
        {
            var i = span - p + j; var divisor = s.Knots[i + p - level + 1] - s.Knots[i];
            var alpha = divisor <= 0 ? 0 : (t - s.Knots[i]) / divisor;
            positions[j] = Vec3.Lerp(positions[j - 1], positions[j], alpha); weights[j] = weights[j - 1] * (1 - alpha) + weights[j] * alpha;
        }
        if (weights[p] <= 0) throw new ArgumentException("The spline has an invalid homogeneous denominator.");
        return positions[p] / weights[p];
    }
    public static ImmutableArray<Vec3> Tessellate(SplineEntity spline, double relativeTolerance = 1e-5)
    {
        ValidateSpline(spline);
        if (!double.IsFinite(relativeTolerance) || relativeTolerance <= 0) throw new ArgumentOutOfRangeException(nameof(relativeTolerance));
        var tolerance = Math.Max(1e-8, Bounds3.From(spline.ControlPoints).Size.Length * relativeTolerance);
        var result = ImmutableArray.CreateBuilder<Vec3>();
        void Segment(double a, Vec3 pa, double b, Vec3 pb, int depth)
        {
            var middle = (a + b) / 2; var pm = EvaluateValidated(spline, middle);
            var q1 = EvaluateValidated(spline, a * .75 + b * .25); var q3 = EvaluateValidated(spline, a * .25 + b * .75);
            var error = Math.Max(pm.DistanceTo(GeometryMath.NearestOnSegment(pm, pa, pb)), Math.Max(q1.DistanceTo(GeometryMath.NearestOnSegment(q1, pa, pb)), q3.DistanceTo(GeometryMath.NearestOnSegment(q3, pa, pb))));
            if (error > tolerance && depth < 14) { Segment(a, pa, middle, pm, depth + 1); Segment(middle, pm, b, pb, depth + 1); }
            else { if (result.Count >= MaximumCurvePoints) throw new ArgumentException("Spline tessellation exceeds the point budget."); result.Add(pb); }
        }
        var start = spline.Knots[spline.Degree]; result.Add(EvaluateValidated(spline, start));
        for (var i = spline.Degree; i < spline.ControlPoints.Length; i++)
            if (spline.Knots[i + 1] > spline.Knots[i]) Segment(spline.Knots[i], EvaluateValidated(spline, spline.Knots[i]), spline.Knots[i + 1], EvaluateValidated(spline, spline.Knots[i + 1]), 0);
        return result.ToImmutable();
    }
    public static ImmutableArray<ImmutableArray<Vec3>> HatchLoops(HatchRegionEntity hatch) => hatch.Loops.Select(l => EntityGeometry.PolylinePoints(new PolylineEntity(l, true))).ToImmutableArray();
    public static IEnumerable<Entity> Expand(Entity entity)
    {
        switch (entity)
        {
            case Polyline3DEntity p: yield return PolylineEntity.FromPoints(p.Points, p.Closed) with { ContinuousLinetype = p.ContinuousLinetype }; break;
            case SplineEntity s: yield return PolylineEntity.FromPoints(Tessellate(s), s.Closed) with { ContinuousLinetype = true }; break;
            case CompositeEntity c: foreach (var child in c.Children) yield return child; break;
            case HatchRegionEntity h:
                var loops = HatchLoops(h);
                if (h.IslandStyle != 0)
                {
                    var all = loops;
                    loops = all.Where((loop, index) => all.Where((other, j) => j != index && GeometryMath.PointInPolygon(loop[0], other)).Count() <= (h.IslandStyle == 1 ? 1 : 0)).ToImmutableArray();
                }
                if (h.Solid)
                {
                    foreach (var face in PolygonBands.Fill(loops)) yield return face;
                }
                else foreach (var line in PatternSegments(loops, h.Pattern)) yield return line;
                break;
        }
    }
    private static IEnumerable<LineEntity> PatternSegments(ImmutableArray<ImmutableArray<Vec3>> loops, ImmutableArray<HatchPatternLine> patterns)
    {
        var budget = 0;
        foreach (var pattern in patterns)
        {
            var inverse = Transform3.RotationZ(-pattern.Angle); var forward = Transform3.RotationZ(pattern.Angle);
            var polygons = loops.Select(l => l.Select(inverse.Point).ToImmutableArray()).ToImmutableArray();
            var bounds = Bounds3.From(polygons.SelectMany(p => p));
            var origin = inverse.Point(pattern.Origin); var offset = inverse.Vector(pattern.Offset);
            var spacing = offset.Y;
            if (Math.Abs(spacing) < 1e-12) throw new ArgumentException("Hatch pattern lines require a nonzero perpendicular offset.");
            var min = Math.Min((bounds.Min.Y - origin.Y) / spacing, (bounds.Max.Y - origin.Y) / spacing);
            var max = Math.Max((bounds.Min.Y - origin.Y) / spacing, (bounds.Max.Y - origin.Y) / spacing);
            if (!double.IsFinite(min) || !double.IsFinite(max) || Math.Abs(min) > 4503599627370495 || Math.Abs(max) > 4503599627370495)
                throw new ArgumentException("Hatch pattern origin/spacing exceeds the supported numerical range.");
            if ((Math.Floor(max) - Math.Ceiling(min) + 1) * polygons.Sum(p => (double)p.Length) > 10000000)
                throw new ArgumentException("Hatch intersection work budget exceeded.");
            if (max - min > 20000) throw new ArgumentException("Hatch line count exceeds 20,000; increase pattern scale.");
            for (var k = (long)Math.Ceiling(min); k <= (long)Math.Floor(max); k++)
            {
                var y = origin.Y + k * spacing; var xs = PolygonBands.Intersections(polygons, y); var phase = origin.X + k * offset.X;
                for (var i = 0; i + 1 < xs.Length; i += 2)
                {
                    foreach (var (a, b) in Dash(xs[i], xs[i + 1], phase, pattern.Dashes))
                    {
                        if (++budget > 100000) throw new ArgumentException("Hatch segment count exceeds 100,000.");
                        yield return new(forward.Point(new(a, y, polygons[0][0].Z)), forward.Point(new(b, y, polygons[0][0].Z)));
                    }
                }
            }
        }
    }
    private static IEnumerable<(double A, double B)> Dash(double min, double max, double phase, ImmutableArray<double> dashes)
    {
        if (dashes.IsEmpty) { yield return (min, max); yield break; }
        var period = dashes.Sum(Math.Abs);
        if (!double.IsFinite(period) || period < 1e-12) throw new ArgumentException("All-zero hatch dash patterns are not supported.");
        var first = Math.Floor((min - phase) / period); var last = Math.Floor((max - phase) / period);
        if (!double.IsFinite(first) || !double.IsFinite(last) || Math.Abs(first) > 4503599627370495 || Math.Abs(last) > 4503599627370495)
            throw new ArgumentException("Hatch dash phase exceeds the supported numerical range.");
        if (last - first > 100000) throw new ArgumentException("Hatch dash count exceeds budget.");
        for (var cycle = first; cycle <= last; cycle++)
        {
            var x = phase + cycle * period;
            foreach (var dash in dashes)
            {
                var end = x + Math.Abs(dash);
                if (dash > 0 && end > min && x < max) yield return (Math.Max(min, x), Math.Min(max, end));
                else if (dash == 0 && x >= min && x <= max) yield return (x, Math.Min(max, x + period * .001));
                x = end;
            }
        }
    }
}

/// <summary>Planar odd-even tessellation. Assumes simple nonintersecting loops.</summary>
public static class PolygonBands
{
    private readonly record struct Edge(Vec3 A, Vec3 B)
    {
        public double At(double y) => A.X + (B.X - A.X) * ((y - A.Y) / (B.Y - A.Y));
    }
    private static IEnumerable<Edge> Edges(ImmutableArray<ImmutableArray<Vec3>> loops)
    {
        foreach (var loop in loops) for (var i = 0; i < loop.Length; i++) if (Math.Abs(loop[i].Y - loop[(i + 1) % loop.Length].Y) > 1e-12) yield return new(loop[i], loop[(i + 1) % loop.Length]);
    }
    public static double[] Intersections(ImmutableArray<ImmutableArray<Vec3>> loops, double y) => Edges(loops).Where(e => Math.Min(e.A.Y, e.B.Y) <= y && y < Math.Max(e.A.Y, e.B.Y)).Select(e => e.At(y)).OrderBy(x => x).ToArray();
    public static IEnumerable<MeshEntity> Fill(ImmutableArray<ImmutableArray<Vec3>> loops)
    {
        var ys = loops.SelectMany(l => l).Select(p => p.Y).Distinct().OrderBy(y => y).ToArray(); var edges = Edges(loops).ToArray();
        if ((long)ys.Length * edges.Length > 10000000) throw new ArgumentException("Hatch tessellation work budget exceeded.");
        var vertices = ImmutableArray.CreateBuilder<Vec3>(); var triangles = ImmutableArray.CreateBuilder<int>();
        var z = loops[0][0].Z;
        for (var i = 0; i + 1 < ys.Length; i++)
        {
            var y0 = ys[i]; var y1 = ys[i + 1]; if (y1 - y0 < 1e-12) continue;
            var mid = (y0 + y1) / 2; var active = edges.Where(e => Math.Min(e.A.Y, e.B.Y) < mid && mid < Math.Max(e.A.Y, e.B.Y)).OrderBy(e => e.At(mid)).ToArray();
            if (active.Length % 2 != 0) throw new ArgumentException("The hatch loops do not form a valid odd-even region.");
            for (var j = 0; j < active.Length; j += 2)
            {
                var a = new Vec3(active[j].At(y0), y0, z); var b = new Vec3(active[j + 1].At(y0), y0, z); var c = new Vec3(active[j + 1].At(y1), y1, z); var d = new Vec3(active[j].At(y1), y1, z);
                var n = vertices.Count; vertices.AddRange(new Vec3[] { a, b, c, d });
                if ((b - a).Cross(c - a).Length > 1e-12) triangles.AddRange(new int[] { n, n + 1, n + 2 });
                if ((c - a).Cross(d - a).Length > 1e-12) triangles.AddRange(new int[] { n, n + 2, n + 3 });
            }
        }
        yield return new MeshEntity(vertices.ToImmutable(), triangles.ToImmutable(), "Hatch fill");
    }
}
