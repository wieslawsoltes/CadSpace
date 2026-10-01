using System.Collections.Immutable;
using System.Globalization;
using CadSpace.Geometry;
using CadSpace.Model;

namespace CadSpace.Dxf;

/// <summary>Native dimension definitions, decimal DIMSTYLE settings and anonymous picture regeneration.</summary>
internal static class DxfDimensions
{
    private static string S(IEnumerable<DxfPair> p, int code, string fallback = "") => p.FirstOrDefault(v => v.Code == code).Value ?? fallback;
    private static double N(IEnumerable<DxfPair> p, int code, double fallback = 0)
    {
        var value = p.FirstOrDefault(v => v.Code == code).Value;
        return value == null ? fallback : GeometryMath.Number(value, out var n) ? n : throw new FormatException($"Invalid dimension group {code}.");
    }
    private static Vec3 P(IEnumerable<DxfPair> p, int code) => new(N(p, code), N(p, code + 10), N(p, code + 20));
    public static Dictionary<string, ImmutableArray<DxfPair>> Styles(ImmutableArray<DxfPair> tables) => DxfEntityReader.Records(tables)
        .Where(r => S(r, 0) == "DIMSTYLE").GroupBy(r => S(r, 2), StringComparer.OrdinalIgnoreCase)
        .ToDictionary(g => g.Key, g => DxfRecordEditing.SemanticPairs(g.Last()), StringComparer.OrdinalIgnoreCase);
    public static Entity Read(ImmutableArray<DxfPair> p, ImmutableArray<DxfPair> raw,
        Dictionary<string, ImmutableArray<DxfPair>> styles, Action<string> warn)
    {
        try
        {
            if (S(p, 8).Equals("*ADSK_CONSTRAINTS", StringComparison.OrdinalIgnoreCase)) throw new NotSupportedException("Dimensional constraints are not ordinary annotations.");
            var flagValue = N(p, 70, -1);
            if (flagValue < 0 || flagValue > short.MaxValue || flagValue != Math.Truncate(flagValue)) throw new FormatException("Invalid dimension flags.");
            var flags = (int)flagValue; var type = flags & 15;
            if (type > 6) throw new NotSupportedException("Unsupported dimension subtype.");
            var normal = new Vec3(N(p, 210), N(p, 220), N(p, 230, 1)); var ocs = Coordinates3D.ObjectCoordinateSystem(normal); var inverse = Coordinates3D.Inverse(ocs);
            Vec3 World(int c) => inverse.Point(P(p, c));
            int[] required = type switch { 0 or 1 => [10,13,14], 2 => [10,13,14,15,16], 3 or 4 => [10,11,15], 5 => [10,13,14,15], _ => [10,13,14] };
            if (required.Any(c => !p.Any(v => v.Code == c))) throw new FormatException("Missing dimension definition point.");
            DimensionEntity d = (DimensionKind)type switch {
                DimensionKind.Aligned or DimensionKind.Rotated => new(World(13), World(14), World(10)),
                DimensionKind.Radius or DimensionKind.Diameter => new(World(10), World(15), P(p, 11)),
                DimensionKind.Angular2Line => new(World(13), World(14), P(p, 16)) { Third = World(15), Fourth = World(10) },
                DimensionKind.Angular3Point => new(World(13), World(14), World(10)) { Third = World(15) },
                _ => new(World(10), World(13), World(14)) };
            var fields = styles.GetValueOrDefault(S(p, 3, "Standard"), []).GroupBy(v => v.Code).ToDictionary(g => g.Key, g => g.Last().Value);
            d = d with { Type = (DimensionKind)type, Rotation = type == 6 ? -N(p, 51) : N(p, 50), OrdinateX = (flags & 64) != 0,
                TextOverride = S(p, 1), TextPosition = (flags & 128) != 0 ? P(p, 11) : null, Format = ReadFormat(raw, fields, warn) };
            DimensionGeometry.Validate(d);
            var name = S(p, 2);
            if (name.Length > 0) d = d with { Picture = new(name, DimensionGeometry.Signature(d), P(p, 12)) };
            warn("DIMENSION definitions are editable; the stored picture is retained until definition/formatting changes. Regeneration supports decimal formatting, not every style, field or association.");
            return normal.Normalized.DistanceTo(Vec3.UnitZ) < 1e-12 ? d : new PlacedEntity(d, ocs);
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or FormatException or NotSupportedException or OverflowException)
        {
            var block = S(p, 2); if (block.Length == 0) throw;
            warn("DIMENSION retained its anonymous display block: " + e.Message);
            var ocs = Coordinates3D.ObjectCoordinateSystem(new(N(p,210), N(p,220), N(p,230,1)));
            return new PlacedEntity(new CompositeEntity("DIMENSION", [new BlockReferenceEntity(block, P(p,12), new(1,1,1))], Encode(raw)), ocs);
        }
    }
    private static DimensionFormat ReadFormat(ImmutableArray<DxfPair> raw, Dictionary<int, string> values, Action<string> warn)
    {
        // ACAD:DSTYLE has typed key/value pairs. Other XDATA and braced application payloads are not style values.
        var acad = false; var active = false; var depth = 0; var applicationDepth = 0;
        for (var i = 0; i < raw.Length; i++)
        {
            var pair = raw[i];
            if (pair.Code == 102) { if (pair.Value.StartsWith('{')) applicationDepth++; else if (pair.Value == "}") applicationDepth--; continue; }
            if (applicationDepth != 0) continue;
            if (pair.Code == 1001) { acad = pair.Value == "ACAD"; active = false; depth = 0; }
            if (!acad) continue;
            if (pair.Code == 1000 && pair.Value == "DSTYLE") { active = true; continue; }
            if (!active) continue;
            if (pair.Code == 1002) { if (pair.Value == "{") depth++; else if (pair.Value == "}") depth--; if (depth == 0) active = false; continue; }
            if (depth == 1 && pair.Code == 1070 && i + 1 < raw.Length && raw[i + 1].Code is 1040 or 1070 or 1000)
                if (int.TryParse(pair.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var code)) values[code] = raw[++i].Value;
        }
        double Number(int c, double fallback) => N(values.Select(v => new DxfPair(v.Key, v.Value)), c, fallback);
        int Integer(int c, int fallback) { var n = Number(c, fallback); return n == Math.Truncate(n) && n >= int.MinValue && n <= int.MaxValue ? (int)n : throw new FormatException("Invalid integral dimension style value."); }
        var f = new DimensionFormat { TextHeight = Number(140,2.5), ArrowSize = Number(41,2.5), Gap = Math.Abs(Number(147,.625)),
            ExtensionOffset = Number(42,.625), ExtensionBeyond = Number(44,1.25), Scale = Number(40,1), MeasurementScale = Math.Abs(Number(144,1)),
            Precision = Integer(271,2), AngularPrecision = Integer(179,2), SuppressTrailingZeros = (Integer(78,0) & 8) != 0,
            TextTemplate = values.GetValueOrDefault(3,"<>") };
        if (f.AngularPrecision < 0) f = f with { AngularPrecision = f.Precision };
        if (f.TextTemplate.Length == 0) f = f with { TextTemplate = "<>" };
        if (f.Scale == 0) { warn("Annotative dimension scale uses 1 for regeneration; the source picture is retained."); f = f with { Scale = 1 }; }
        f.Validate(); return f;
    }
    private static string Encode(IEnumerable<DxfPair> pairs) => string.Concat(pairs.Select(p => $"{p.Code.ToString(CultureInfo.InvariantCulture)}\n{p.Value}\n"));

    internal sealed class Exporter(Drawing drawing, DxfSource? source, Func<string> nextHandle, Action<string> warn)
    {
        private readonly HashSet<string> _names = new(drawing.Blocks.Keys, StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _styleNames = new(Styles(source?.Sections.FirstOrDefault(s => s.Name == "TABLES")?.Pairs ?? []).Keys, StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<DimensionFormat, string> _styleFor = new();
        private int _nextBlock = 1, _nextStyle = 1;
        public Dictionary<string, BlockDefinition> Blocks { get; } = new(StringComparer.OrdinalIgnoreCase);
        public List<ImmutableArray<DxfPair>> StyleRecords { get; } = [];
        public string Write(Entity root)
        {
            if (Blocks.Count >= 20000) throw new NotSupportedException("Native dimension regeneration is bounded to 20,000 pictures per export.");
            var leaf = root; var transform = Transform3.Identity;
            while (leaf is PlacedEntity p) { transform = p.Placement.Then(transform); leaf = p.Geometry; }
            var dim = (DimensionEntity)leaf; DimensionGeometry.Validate(dim);
            var normal = transform.X.Cross(transform.Y).Normalized; var ocs = Coordinates3D.ObjectCoordinateSystem(normal);
            var d = DimensionGeometry.TransformPlanar(dim, transform.Then(Coordinates3D.Inverse(ocs)));
            var layout = DimensionGeometry.For(d); var name = Unique(_names, "*D", ref _nextBlock);
            Blocks.Add(name, new(name, default, layout.Entities.Select(e => e with { ColorIndex = 0 }).ToImmutableArray()));
            if (!_styleFor.TryGetValue(d.Format, out var style))
            {
                style = Unique(_styleNames, "CadSpaceDim", ref _nextStyle); _styleFor.Add(d.Format, style); var f = d.Format;
                StyleRecords.Add(Record(0,"DIMSTYLE",105,nextHandle(),100,"AcDbSymbolTableRecord",100,"AcDbDimStyleTableRecord",2,style,70,0,
                    3,f.TextTemplate,40,f.Scale,41,f.ArrowSize,42,f.ExtensionOffset,44,f.ExtensionBeyond,140,f.TextHeight,144,f.MeasurementScale,147,f.Gap,
                    73,0,74,0,77,1,78,f.SuppressTrailingZeros?8:0,179,f.AngularPrecision,271,f.Precision,275,0,277,2,278,46,280,0));
            }
            var record = Record(0,"DIMENSION",5,root.Handle.Length > 0 ? root.Handle : nextHandle(),100,"AcDbEntity",8,root.Layer,6,root.Linetype,
                48,root.LinetypeScale,62,root.ColorIndex).ToBuilder();
            void Pair(int c, object v) => record.Add(new(c, Convert.ToString(v, CultureInfo.InvariantCulture)!));
            void Point(int c, Vec3 p) { Pair(c,p.X); Pair(c+10,p.Y); Pair(c+20,p.Z); }
            void World(int c, Vec3 p) => Point(c, ocs.Point(p));
            if (root.TrueColor is uint color) Pair(420,color & 0xffffff); if (root.LineWeight >= 0) Pair(370,Math.Round(root.LineWeight*100));
            if (!root.Visible) Pair(60,1); if (root.Layout != "Model") { Pair(67,1); Pair(410,root.Layout); }
            Pair(100,"AcDbDimension"); Pair(280,0); Pair(2,name);
            World(10,d.Type == DimensionKind.Angular2Line ? d.Fourth : d.Type is DimensionKind.Radius or DimensionKind.Diameter or DimensionKind.Ordinate ? d.First : d.Location);
            Point(11,layout.TextCenter); Pair(70,(int)d.Type | 32 | (d.Type == DimensionKind.Ordinate && d.OrdinateX ? 64 : 0) | (d.TextPosition.HasValue ? 128 : 0));
            Pair(71,5); Pair(1,d.TextOverride); Pair(3,style); Point(210,normal);
            Pair(42,d.Type is DimensionKind.Angular2Line or DimensionKind.Angular3Point ? GeometryMath.Radians(layout.Measurement) : layout.Measurement);
            if (d.Type == DimensionKind.Ordinate) Pair(51,-d.Rotation);
            switch (d.Type)
            {
                case DimensionKind.Aligned: case DimensionKind.Rotated:
                    Pair(100,"AcDbAlignedDimension"); World(13,d.First); World(14,d.Second);
                    Pair(50,d.Type == DimensionKind.Aligned ? GeometryMath.Angle(d.Second-d.First) : d.Rotation);
                    if (d.Type == DimensionKind.Rotated) Pair(100,"AcDbRotatedDimension"); break;
                case DimensionKind.Angular2Line:
                    Pair(100,"AcDb2LineAngularDimension"); World(13,d.First); World(14,d.Second); World(15,d.Third); Point(16,d.Location); break;
                case DimensionKind.Angular3Point:
                    Pair(100,"AcDb3PointAngularDimension"); World(13,d.First); World(14,d.Second); World(15,d.Third); break;
                case DimensionKind.Radius: case DimensionKind.Diameter:
                    Pair(100,d.Type == DimensionKind.Radius ? "AcDbRadialDimension" : "AcDbDiametricDimension"); World(15,d.Second); Pair(40,d.Second.DistanceTo(d.Location)); break;
                case DimensionKind.Ordinate:
                    Pair(100,"AcDbOrdinateDimension"); World(13,d.Second); World(14,d.Location); break;
            }
            if (dim.Picture != null) warn("Regenerated DIMENSION: supported definitions and decimal formatting retained; original private styles, associations and fields are not regenerated. Keep the source DXF.");
            return Encode(record);
        }
        private static string Unique(HashSet<string> names, string prefix, ref int sequence)
        {
            // Monotonic counters avoid quadratic name probing for large dimension exports.
            string name; do { name = prefix + sequence++; } while (!names.Add(name)); return name;
        }
    }
    private static ImmutableArray<DxfPair> Record(params object[] values)
    {
        var p = ImmutableArray.CreateBuilder<DxfPair>();
        for (var i = 0; i < values.Length; i += 2) p.Add(new((int)values[i], Convert.ToString(values[i+1], CultureInfo.InvariantCulture)!));
        return p.ToImmutable();
    }
}
