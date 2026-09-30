using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using CadSpace.Geometry;
using CadSpace.Model;

namespace CadSpace.Dxf;

/// <summary>Bounded source-record edits. Application groups and XDATA are not treated as entity geometry.</summary>
internal static class DxfRecordEditing
{
    // This view is for interpretation only. The original pairs remain the provenance/export source.
    public static ImmutableArray<DxfPair> SemanticPairs(ImmutableArray<DxfPair> pairs)
    {
        var result = ImmutableArray.CreateBuilder<DxfPair>(); var depth = 0; var extended = false;
        foreach (var pair in pairs)
        {
            if (pair.Code == 0) { if (depth != 0) throw new FormatException("Unbalanced application data."); extended = false; }
            if (pair.Code == 1001 && depth == 0) extended = true;
            if (extended) continue;
            if (pair.Code == 102)
            {
                if (pair.Value.StartsWith('{')) depth++;
                else if (pair.Value == "}") depth--;
                if (depth < 0 || depth > 32) throw new FormatException("Invalid application-data nesting.");
                continue;
            }
            if (depth == 0) result.Add(pair);
        }
        if (depth != 0) throw new FormatException("Unbalanced application data.");
        return result.ToImmutable();
    }
    private static string F(double value) => value.ToString("R", CultureInfo.InvariantCulture);
    private static Entity WithoutStyleChanges(Entity after, Entity before) => after with {
        Layer = before.Layer, ColorIndex = before.ColorIndex, TrueColor = before.TrueColor,
        LineWeight = before.LineWeight, Linetype = before.Linetype, LinetypeScale = before.LinetypeScale,
        Visible = before.Visible, Layout = before.Layout };
    public static bool TryWrite(Entity after, Entity before, ImmutableArray<DxfPair> raw, Action<string> warn, out string text)
    {
        text = "";
        if (after.Id != before.Id || after.Handle != before.Handle || after is OpaqueEntity || raw.IsDefaultOrEmpty) return false;
        var styleOnly = WithoutStyleChanges(after, before) == before;
        var records = DxfEntityReader.Records(raw).ToArray();
        if (records.Length == 0) return false;
        var record = records[0]; var semantic = SemanticPairs(record);
        var kind = semantic.FirstOrDefault(p => p.Code == 0).Value?.Trim();
        var common = new Dictionary<int, string?>();
        if (after.Layer != before.Layer) common[8] = after.Layer;
        if (after.Linetype != before.Linetype) common[6] = after.Linetype;
        if (after.LinetypeScale != before.LinetypeScale) common[48] = F(after.LinetypeScale);
        if (after.LineWeight != before.LineWeight) common[370] = after.LineWeight < 0 ? null : F(Math.Round(after.LineWeight * 100));
        if (after.Visible != before.Visible) common[60] = after.Visible ? null : "1";
        if (after.ColorIndex != before.ColorIndex) common[62] = after.ColorIndex.ToString(CultureInfo.InvariantCulture);
        if (after.TrueColor != before.TrueColor) { common[420] = after.TrueColor is uint color ? (color & 0xffffff).ToString(CultureInfo.InvariantCulture) : null; common[430] = null; }
        if (after.Layout != before.Layout) { common[67] = after.Layout == "Model" ? null : "1"; common[410] = after.Layout == "Model" ? null : after.Layout; }
        var geometry = new Dictionary<int, string?>(); var subclass = "";
        PolylineEntity? newPoly = null, oldPoly = null;
        var a = WithoutStyleChanges(after, before); var b = before;
        while (!styleOnly && a is PlacedEntity pa && b is PlacedEntity pb && pa.Placement == pb.Placement)
        {
            // Only the unchanged OCS/affine frame is reusable; arbitrary changed placements need the regular conversion writer.
            if (pa with { Geometry = pb.Geometry } != pb) return false;
            a = pa.Geometry; b = pb.Geometry;
        }
        void Position(int code, Vec3 value) { geometry[code] = F(value.X); geometry[code + 10] = F(value.Y); geometry[code + 20] = F(value.Z); }
        if (!styleOnly)
        {
            // Logical compounds cannot be partially regenerated while claiming to preserve attribute or array semantics.
            if (records.Length != 1) return false;
            // Every change must be accounted for by the selected patch path, including nested common fields.
            Entity? restored = (a, b) switch {
                (LineEntity x, LineEntity y) => x with { Start = y.Start, End = y.End },
                (PointEntity x, PointEntity y) => x with { Position = y.Position },
                (CircleEntity x, CircleEntity y) => x with { Center = y.Center, Radius = y.Radius },
                (ArcEntity x, ArcEntity y) => x with { Center = y.Center, Radius = y.Radius, StartAngle = y.StartAngle, EndAngle = y.EndAngle },
                (TextEntity x, TextEntity y) => x with { Position = y.Position, Text = y.Text, Height = y.Height, Rotation = y.Rotation },
                (PolylineEntity x, PolylineEntity y) => x with { Vertices = y.Vertices, Closed = y.Closed, ConstantWidth = y.ConstantWidth, ContinuousLinetype = y.ContinuousLinetype },
                _ => null
            };
            if (restored != b) return false;
            switch (a, b)
            {
                case (LineEntity x, LineEntity y) when kind == "LINE":
                    subclass = "AcDbLine"; if (x.Start != y.Start) Position(10, x.Start); if (x.End != y.End) Position(11, x.End); break;
                case (PointEntity x, PointEntity y) when kind == "POINT":
                    subclass = "AcDbPoint"; if (x.Position != y.Position) Position(10, x.Position); break;
                case (CircleEntity x, CircleEntity y) when kind == "CIRCLE":
                    subclass = "AcDbCircle"; if (x.Center != y.Center) Position(10, x.Center); if (x.Radius != y.Radius) geometry[40] = F(x.Radius); break;
                case (ArcEntity x, ArcEntity y) when kind == "ARC":
                    subclass = "AcDbCircle"; if (x.Center != y.Center) Position(10, x.Center); if (x.Radius != y.Radius) geometry[40] = F(x.Radius);
                    record = Patch(record, new() { [50] = F(x.StartAngle), [51] = F(x.EndAngle) }, "AcDbArc"); break;
                case (TextEntity { Multiline: false } x, TextEntity { Multiline: false } y) when kind == "TEXT":
                    if (x.Text.IndexOfAny(['\r', '\n', '\0']) >= 0) return false;
                    subclass = "AcDbText"; if (x.Position != y.Position) Position(10, x.Position);
                    if (x.Text != y.Text) geometry[1] = x.Text; if (x.Height != y.Height) geometry[40] = F(x.Height); if (x.Rotation != y.Rotation) geometry[50] = F(x.Rotation); break;
                case (PolylineEntity x, PolylineEntity y) when kind == "LWPOLYLINE" && x.Vertices.Length == y.Vertices.Length:
                    subclass = "AcDbPolyline"; newPoly = x; oldPoly = y;
                    // Do not guess which old vertex identifier belongs to a reordered point.
                    if (semantic.Any(p => p.Code == 91))
                    {
                        var previousPoints = y.Vertices.Select(v => v.Position).ToHashSet();
                        for (var i = 0; i < x.Vertices.Length; i++)
                            if (x.Vertices[i].Position != y.Vertices[i].Position && previousPoints.Contains(x.Vertices[i].Position)) return false;
                    }
                    var flags = int.Parse(semantic.FirstOrDefault(p => p.Code == 70).Value ?? "0", CultureInfo.InvariantCulture);
                    geometry[70] = ((flags & ~129) | (x.Closed ? 1 : 0) | (x.ContinuousLinetype ? 128 : 0)).ToString(CultureInfo.InvariantCulture);
                    geometry[43] = x.ConstantWidth == 0 ? null : F(x.ConstantWidth); geometry[38] = F(x.Vertices[0].Position.Z); break;
                default: return false;
            }
        }
        record = Patch(record, common, "AcDbEntity");
        if (geometry.Count > 0) record = Patch(record, geometry, subclass);
        if (newPoly != null && oldPoly != null)
        {
            var starts = new List<int>(); var depth = 0; var end = record.Length;
            for (var i = 0; i < record.Length; i++)
            {
                var pair = record[i];
                if (pair.Code == 102) { if (pair.Value.StartsWith('{')) depth++; else if (pair.Value == "}") depth--; }
                if (depth != 0) continue;
                if (pair.Code == 1001 || pair.Code == 210) { end = i; break; }
                if (pair.Code == 10) starts.Add(i);
            }
            if (starts.Count != newPoly.Vertices.Length) return false;
            var result = ImmutableArray.CreateBuilder<DxfPair>(); result.AddRange(record.Take(starts[0]));
            for (var i = 0; i < starts.Count; i++)
            {
                var stop = i + 1 < starts.Count ? starts[i + 1] : end;
                var vertex = newPoly.Vertices[i];
                result.AddRange(Patch(record.Skip(starts[i]).Take(stop - starts[i]).ToImmutableArray(), new() {
                    [10] = F(vertex.Position.X), [20] = F(vertex.Position.Y), [42] = vertex.Bulge == 0 ? null : F(vertex.Bulge),
                    [40] = vertex.StartWidth == 0 ? null : F(vertex.StartWidth), [41] = vertex.EndWidth == 0 ? null : F(vertex.EndWidth) }, ""));
            }
            result.AddRange(record.Skip(end)); record = result.ToImmutable();
        }
        var output = new StringBuilder();
        void Emit(IEnumerable<DxfPair> pairs) { foreach (var pair in pairs) output.Append(pair.Code.ToString(CultureInfo.InvariantCulture)).Append('\n').Append(pair.Value).Append('\n'); }
        Emit(record); foreach (var child in records.Skip(1)) Emit(child);
        text = output.ToString();
        if (!styleOnly) warn($"Edited {kind}: original application/extended data and unedited fields retained; external associations and caches are not regenerated.");
        return true;
    }
    // Subclass-aware, linear patch; braced application data and extended data are never edited.
    private static ImmutableArray<DxfPair> Patch(ImmutableArray<DxfPair> record, Dictionary<int, string?> changes, string subclass)
    {
        if (changes.Count == 0) return record;
        var output = ImmutableArray.CreateBuilder<DxfPair>(); var seen = new HashSet<int>();
        var hasMarkers = SemanticPairs(record).Any(p => p.Code == 100); var active = !hasMarkers || subclass.Length == 0;
        var finished = false; var depth = 0;
        void Missing()
        {
            foreach (var (code, value) in changes) if (!seen.Contains(code) && value != null) output.Add(new(code, value));
            finished = true;
        }
        foreach (var pair in record)
        {
            if (pair.Code == 102)
            {
                if (pair.Value.StartsWith('{')) depth++; else if (pair.Value == "}") depth--;
                if (depth < 0 || depth > 32) throw new FormatException("Invalid application data nesting.");
                output.Add(pair); continue;
            }
            if (depth == 0 && !finished && (pair.Code == 1001 || active && subclass == "AcDbPolyline" && pair.Code == 10)) { Missing(); active = false; }
            if (depth == 0 && pair.Code == 100)
            {
                if (active && !finished) Missing();
                active = !finished && pair.Value == subclass;
            }
            if (depth == 0 && active && changes.TryGetValue(pair.Code, out var replacement))
            { if (seen.Add(pair.Code) && replacement != null) output.Add(new(pair.Code, replacement)); }
            else output.Add(pair);
        }
        if (!finished) Missing();
        return output.ToImmutable();
    }
}
