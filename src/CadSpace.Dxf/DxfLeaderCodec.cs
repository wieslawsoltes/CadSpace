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
        static bool Has(Entity e) => e is LeaderEntity || e is PlacedEntity p && Has(p.Geometry) || e is CompositeEntity c && c.Children.Any(Has);
        return d.Entities.Any(Has) || d.Blocks.Values.Any(b => b.Entities.Any(Has));
    }
    public static Entity Read(ImmutableArray<DxfPair> raw, Dictionary<string, ImmutableArray<DxfPair>> styles, Action<string> warn)
    {
        var p = DxfRecordEditing.SemanticPairs(raw);
        // Do not turn stored spline fit points into a claimed editable straight path.
        if (I(p,72) != 0) {
            if (I(p,72) != 1) throw new FormatException("Invalid LEADER path type.");
            warn("Spline-path LEADER remains a source-backed display fallback; its fit points are not an editable straight leader.");
            return DxfEntityReader.Read(p, warn);
        }
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
        if (!double.IsFinite(scale) || scale <= 0) throw new FormatException("Invalid leader dimension scale.");
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
    private static string Write(Entity root, string canonicalStyle, string? annotationHandle, Func<string> next, Action<string> warn)
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
        Pair(100,"AcDbLeader"); Pair(3,canonicalStyle); Pair(71,leader.ArrowEnabled ? 1 : 0); Pair(72,0);
        var reference = annotationHandle ?? "0"; var detached = annotationHandle == null;
        if (detached) warn("LEADER annotation target is missing, ambiguous or incompatible; canonical export detaches its reference.");
        Pair(73,detached ? 3 : leader.AnnotationType); Pair(74,leader.HooklineReversed ? 1 : 0); Pair(75,leader.Hookline ? 1 : 0);
        Pair(40,leader.TextHeight); Pair(41,leader.TextWidth); Pair(76,leader.Vertices.Length);
        foreach (var p in leader.Vertices) Point(10,p);
        Pair(77,leader.BlockColor); Pair(340,reference); Point(210,leader.Normal); Point(211,leader.Horizontal); Point(212,leader.BlockOffset); Point(213,leader.AnnotationOffset);
        Pair(1001,"ACAD"); Pair(1000,"DSTYLE"); Pair(1002,"{");
        Pair(1070,40); Pair(1040,1); Pair(1070,41); Pair(1040,leader.ArrowSize); Pair(1070,147); Pair(1040,leader.Gap); Pair(1070,77); Pair(1070,leader.TextAbove ? 1 : 0);
        Pair(1002,"}"); return b.ToString();
    }
    /// <summary>One export-scoped reference index and canonical style; never inherit unrelated Standard overrides.</summary>
    internal sealed class Exporter
    {
        private readonly Lazy<Dictionary<ulong, Entity?>> _targets;
        private readonly HashSet<string> _styles;
        private readonly Func<string> _next;
        private readonly Action<string> _warn;
        private string? _style;
        public List<ImmutableArray<DxfPair>> StyleRecords { get; } = [];
        public Exporter(Drawing drawing, DxfSource? source, Func<string> next, Action<string> warn)
        {
            _next = next; _warn = warn;
            _styles = new(DxfDimensions.Styles(source?.Sections.FirstOrDefault(s => s.Name == "TABLES")?.Pairs ?? []).Keys, StringComparer.OrdinalIgnoreCase);
            _targets = new(() => {
                var index = new Dictionary<ulong, Entity?>();
                foreach (var e in drawing.Entities.Concat(drawing.Blocks.Values.SelectMany(b => b.Entities)))
                    if (ulong.TryParse(e.Handle, NumberStyles.HexNumber, Culture, out var id) && id != 0)
                        if (!index.TryAdd(id,e)) index[id] = null;
                return index;
            });
        }
        private string? Target(LeaderEntity leader, string layout)
        {
            if (!ulong.TryParse(leader.AnnotationHandle, NumberStyles.HexNumber, Culture, out var id)) return null;
            if (id == 0) return "0";
            if (!_targets.Value.TryGetValue(id,out var target) || target == null || !target.Layout.Equals(layout,StringComparison.OrdinalIgnoreCase)) return null;
            var valid = leader.AnnotationType switch {
                0 => target.Kind == "MTEXT", 1 => target.Kind == "TOLERANCE", 2 => target.Kind == "INSERT",
                _ => target.Kind is "MTEXT" or "TOLERANCE" or "INSERT"
            };
            return valid ? target.Handle : null;
        }
        public bool CanRetain(Entity root)
        {
            var leader = LeaderGeometry.Unwrap(root);
            if (leader == null) return true;
            var reference = Target(leader, root.Layout);
            return reference != null && (reference == "0" || reference.Equals(leader.AnnotationHandle,StringComparison.OrdinalIgnoreCase));
        }
        public string Write(Entity root)
        {
            if (_style == null)
            {
                var i = 1; _style = "CadSpaceLeader";
                while (!_styles.Add(_style)) _style = "CadSpaceLeader" + i++;
                StyleRecords.Add([
                    new(0,"DIMSTYLE"), new(105,_next()), new(100,"AcDbSymbolTableRecord"), new(100,"AcDbDimStyleTableRecord"),
                    new(2,_style), new(70,"0"), new(40,"1"), new(41,"2.5"), new(140,"2.5"), new(147,"0.625"), new(77,"1")
                ]);
            }
            return DxfLeaderCodec.Write(root, _style, Target(LeaderGeometry.Unwrap(root)!,root.Layout), _next, _warn);
        }
    }
    private static string Encode(IEnumerable<DxfPair> pairs) => string.Concat(pairs.Select(p => $"{p.Code.ToString(Culture)}\n{p.Value}\n"));
}
