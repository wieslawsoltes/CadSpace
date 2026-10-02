using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using CadSpace.Geometry;
using CadSpace.Model;

namespace CadSpace.Dxf;

internal static class DxfLeaderCodec
{
    private static readonly CultureInfo Culture = CultureInfo.InvariantCulture;
    private static string S(IEnumerable<DxfPair> p, int c, string fallback = "") => p.FirstOrDefault(v => v.Code == c).Value ?? fallback;
    private static double N(IEnumerable<DxfPair> p, int c, double fallback = 0) => GeometryMath.Number(S(p,c,fallback.ToString(Culture)), out var n) ? n : throw new FormatException("Invalid leader numeric value.");
    private static int I(IEnumerable<DxfPair> p, int c, int fallback = 0) => int.Parse(S(p,c,fallback.ToString(Culture)), Culture);
    private static Vec3 P(IEnumerable<DxfPair> p, int c, Vec3 fallback = default) => new(N(p,c,fallback.X),N(p,c+10,fallback.Y),N(p,c+20,fallback.Z));
    public static bool Contains(Drawing d)
    {
        static bool Has(Entity e) => LeaderGeometry.Unwrap(e) != null || e is CompositeEntity c && c.Children.Any(Has);
        return d.Entities.Any(Has) || d.Blocks.Values.Any(b => b.Entities.Any(Has));
    }
    public static Entity Read(ImmutableArray<DxfPair> raw, Dictionary<string, ImmutableArray<DxfPair>> styles, Action<string> warn)
    {
        var p = DxfRecordEditing.SemanticPairs(raw);
        // Do not turn stored spline fit points into a claimed editable straight path.
        if (I(p,72) != 0) return DxfEntityReader.Read(p, warn);
        var points = DxfEntityReader.Points(p,10);
        if (I(p,76,points.Length) != points.Length) throw new FormatException("LEADER vertex count mismatch.");
        foreach (var c in new[] { 71,74,75 }) if (I(p,c,1) is not (0 or 1)) throw new FormatException("Invalid LEADER flag.");
        var styleName = S(p,3,"Standard"); var style = styles.GetValueOrDefault(styleName, []);
        var values = style.GroupBy(v => v.Code).ToDictionary(g => g.Key,g => g.Last().Value);
        // Parse only ACAD DSTYLE key/value pairs, never application data that resembles XDATA.
        var depth = 0; var appDepth = 0; var acad = false; var dstyle = false;
        for (var i = 0; i < raw.Length; i++)
        {
            var q = raw[i];
            if (q.Code == 102) { if (q.Value.StartsWith('{')) appDepth++; else if (q.Value == "}") appDepth--; continue; }
            if (appDepth != 0) continue;
            if (q.Code == 1001) { acad = q.Value == "ACAD"; dstyle = false; depth = 0; }
            if (!acad) continue;
            if (q.Code == 1000 && q.Value == "DSTYLE") { dstyle = true; continue; }
            if (!dstyle) continue;
            if (q.Code == 1002) { depth += q.Value == "{" ? 1 : -1; if (depth == 0) dstyle = false; continue; }
            if (depth == 1 && q.Code == 1070 && i+1 < raw.Length && raw[i+1].Code is 1040 or 1070 or 1000 or 1005)
                if (int.TryParse(q.Value, NumberStyles.Integer, Culture, out var code)) values[code] = raw[++i].Value;
        }
        double V(int code, double fallback) => N(values.Select(v => new DxfPair(v.Key,v.Value)), code, fallback);
        var scale = V(40,1); if (scale == 0) { scale = 1; warn("Annotative LEADER scale uses 1 for display; no annotation-scale regeneration."); }
        if (values.TryGetValue(341,out var arrowBlock) && arrowBlock != "0")
            warn("Custom leader arrow block is retained in source; displayed as a closed filled arrow.");
        var e = new LeaderEntity(points) { ArrowEnabled = I(p,71,1) != 0, ArrowSize = V(41,2.5)*scale,
            Normal = P(p,210,Vec3.UnitZ), Horizontal = P(p,211,Vec3.UnitX), Hookline = I(p,75,1) != 0, HooklineReversed = I(p,74,1) != 0,
            TextAbove = V(77,1) != 0, TextWidth = N(p,41,1), TextHeight = N(p,40,1), Gap = Math.Abs(V(147,.625))*scale,
            AnnotationType = I(p,73,3), AnnotationHandle = S(p,340,"0"), BlockOffset = P(p,212), AnnotationOffset = P(p,213),
            BlockColor = I(p,77,7), DimensionStyle = styleName };
        LeaderGeometry.Validate(e);
        if (e.AnnotationHandle != "0" || e.Hookline) warn("LEADER annotation references are retained, not associatively regenerated; hookline display uses stored dimensions.");
        return e;
    }
    public static bool TryPatch(Entity after, Entity before, ImmutableArray<DxfPair> raw, Action<string> warn, out string text)
    {
        text = "";
        if (after is not LeaderEntity a || before is not LeaderEntity b || a.Id != b.Id || a.Handle != b.Handle) return false;
        if (a.Vertices == b.Vertices && a.ArrowEnabled == b.ArrowEnabled) return false;
        // Common-property patch validates that there are no other unaccounted-for definition changes.
        if (!DxfRecordEditing.TryWrite(a with { Vertices = b.Vertices, ArrowEnabled = b.ArrowEnabled }, b, raw, warn, out var common)) return false;
        var record = DxfCodec.ParsePairs(common); var semantic = DxfRecordEditing.SemanticPairs(record);
        if (S(semantic,0) != "LEADER" || I(semantic,72) != 0 || semantic.Count(p => p.Code == 76) > 1) return false;
        var output = ImmutableArray.CreateBuilder<DxfPair>(); var depth = 0; var extended = false;
        var marked = semantic.Any(p => p.Code == 100); var active = !marked; var inserted = false; var arrow = false; var count = false;
        void AddVertices() { foreach (var p in a.Vertices) { output.Add(new(10,p.X.ToString("R",Culture))); output.Add(new(20,p.Y.ToString("R",Culture))); output.Add(new(30,p.Z.ToString("R",Culture))); } }
        foreach (var p in record)
        {
            if (p.Code == 102 && !extended) { if (p.Value.StartsWith('{')) depth++; else if (p.Value == "}") depth--; output.Add(p); continue; }
            if (depth == 0 && p.Code == 1001) extended = true;
            if (depth == 0 && !extended && p.Code == 100) active = p.Value == "AcDbLeader";
            if (depth == 0 && !extended && active)
            {
                if (p.Code == 10) { if (!inserted) {
                    if (!arrow) { output.Add(new(71,a.ArrowEnabled ? "1" : "0")); arrow = true; }
                    if (!count) { output.Add(new(76,a.Vertices.Length.ToString(Culture))); count = true; }
                    AddVertices(); inserted = true; } continue; }
                if (p.Code is 20 or 30) continue;
                if (p.Code == 71) { if (!arrow) output.Add(new(71,a.ArrowEnabled ? "1" : "0")); arrow = true; continue; }
                if (p.Code == 76) { if (!count) output.Add(new(76,a.Vertices.Length.ToString(Culture))); count = true; continue; }
            }
            output.Add(p);
        }
        // Missing required markers use the canonical writer rather than append data after XDATA.
        if (!inserted || !count || a.ArrowEnabled != b.ArrowEnabled && !arrow) return false;
        text = Encode(output); warn("Edited LEADER vertices retain source style/annotation data; linked annotations and application caches are not regenerated."); return true;
    }
    public static string Write(Entity root, Drawing drawing, Func<string> next, Action<string> warn)
    {
        var e = root; var transform = Transform3.Identity;
        while (e is PlacedEntity p) { transform = p.Placement.Then(transform); e = p.Geometry; }
        var leader = (LeaderEntity)e;
        if (transform != Transform3.Identity) leader = LeaderGeometry.Transform(leader,transform);
        LeaderGeometry.Validate(leader);
        var b = new StringBuilder();
        void Pair(int code, object value) => b.Append(code.ToString(Culture)).Append('\n').Append(Convert.ToString(value,Culture)).Append('\n');
        void Point(int code, Vec3 p) { Pair(code,p.X); Pair(code+10,p.Y); Pair(code+20,p.Z); }
        Pair(0,"LEADER"); Pair(5,root.Handle.Length == 0 ? next() : root.Handle); Pair(100,"AcDbEntity"); Pair(8,root.Layer);
        Pair(6,root.Linetype); Pair(48,root.LinetypeScale); Pair(62,root.ColorIndex); if (root.TrueColor is uint c) Pair(420,c & 0xffffff);
        if (root.LineWeight >= 0) Pair(370,Math.Round(root.LineWeight*100)); if (!root.Visible) Pair(60,1);
        if (root.Layout != "Model") { Pair(67,1); Pair(410,root.Layout); }
        Pair(100,"AcDbLeader"); Pair(3,"Standard"); Pair(71,leader.ArrowEnabled ? 1 : 0); Pair(72,0);
        var reference = leader.AnnotationHandle; var detached = false;
        if (reference != "0" && !drawing.Entities.Concat(drawing.Blocks.Values.SelectMany(v => v.Entities)).Any(v => v.Handle.Equals(reference,StringComparison.OrdinalIgnoreCase)))
        { warn("LEADER annotation target is missing; canonical export detaches its dangling reference."); reference = "0"; detached = true; }
        Pair(73,detached ? 3 : leader.AnnotationType); Pair(74,leader.HooklineReversed ? 1 : 0); Pair(75,leader.Hookline ? 1 : 0);
        Pair(40,leader.TextHeight); Pair(41,leader.TextWidth); Pair(76,leader.Vertices.Length);
        foreach (var p in leader.Vertices) Point(10,p);
        Pair(77,leader.BlockColor); Pair(340,reference); Point(210,leader.Normal); Point(211,leader.Horizontal); Point(212,leader.BlockOffset); Point(213,leader.AnnotationOffset);
        Pair(1001,"ACAD"); Pair(1000,"DSTYLE"); Pair(1002,"{");
        Pair(1070,40); Pair(1040,1); Pair(1070,41); Pair(1040,leader.ArrowSize); Pair(1070,147); Pair(1040,leader.Gap); Pair(1070,77); Pair(1070,leader.TextAbove ? 1 : 0);
        Pair(1002,"}"); return b.ToString();
    }
    private static string Encode(IEnumerable<DxfPair> pairs) => string.Concat(pairs.Select(p => $"{p.Code.ToString(Culture)}\n{p.Value}\n"));
}
