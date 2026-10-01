using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using CadSpace.Geometry;
using CadSpace.Model;

namespace CadSpace.Dxf;

public readonly record struct DxfPair(int Code, string Value);
public sealed record DxfSection(string Name, ImmutableArray<DxfPair> Pairs);
public sealed record DxfSource(string Text, Drawing Original, ImmutableArray<DxfSection> Sections, ImmutableDictionary<Guid, ImmutableArray<DxfPair>> Records)
{
    public byte[]? OriginalBytes { get; init; }
}
public sealed record DxfReadResult(Drawing Drawing, DxfSource Source, ImmutableArray<string> Warnings);
public sealed record DxfWriteResult(string Text, ImmutableArray<string> Warnings);

/// <summary>Bounded ASCII DXF exchange. Unsupported records remain opaque and untouched records retain their group data.</summary>
public static class DxfCodec
{
    private static readonly CultureInfo Culture = CultureInfo.InvariantCulture;
    public const int MaximumCharacters = 64 * 1024 * 1024;
    public static DxfReadResult Read(string text, string name = "Drawing.dxf")
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.Length > MaximumCharacters) throw new FormatException("DXF exceeds the 64 Mi-character import limit.");
        if (text.StartsWith("AutoCAD Binary DXF", StringComparison.Ordinal)) throw new NotSupportedException("Use DxfBinary.Read with bytes for binary DXF transport.");
        var pairs = ParsePairs(text); var sections = ImmutableArray.CreateBuilder<DxfSection>();
        for (var i = 0; i < pairs.Length; i++)
        {
            if (pairs[i].Code != 0 || pairs[i].Value.Trim() != "SECTION") continue;
            if (++i >= pairs.Length || pairs[i].Code != 2) throw new FormatException("SECTION must be followed by a section name.");
            var sectionName = pairs[i].Value.Trim(); var content = ImmutableArray.CreateBuilder<DxfPair>();
            while (++i < pairs.Length && !(pairs[i].Code == 0 && pairs[i].Value.Trim() == "ENDSEC")) content.Add(pairs[i]);
            if (i == pairs.Length) throw new FormatException($"Unterminated {sectionName} section.");
            sections.Add(new(sectionName, content.ToImmutable()));
        }
        if (!pairs.Any(p => p.Code == 0 && p.Value.Trim() == "EOF")) throw new FormatException("DXF EOF marker is missing.");
        if (sections.GroupBy(s => s.Name).Any(g => g.Count() > 1)) throw new FormatException("Duplicate DXF sections are not supported.");
        var state = Drawing.Empty with { Name = name };
        var warnings = ImmutableArray.CreateBuilder<string>();
        var dimensionStyles = DxfDimensions.Styles(sections.FirstOrDefault(s => s.Name == "TABLES")?.Pairs ?? []);
        var sourceRecords = ImmutableDictionary.CreateBuilder<Guid, ImmutableArray<DxfPair>>();
        state = DxfLinetypes.Read(state, sections.FirstOrDefault(s => s.Name == "TABLES")?.Pairs ?? [], sections.FirstOrDefault(s => s.Name == "HEADER")?.Pairs ?? [], warnings.Add);
        foreach (var record in Records(sections.FirstOrDefault(s => s.Name == "TABLES")?.Pairs ?? []))
        {
            if (Type(record) != "LAYER") continue;
            var layerName = String(record, 2, "0"); var aci = Integer(record, 62, 7); var flags = Integer(record, 70);
            var color = Has(record, 420) ? 0xFF000000u | (uint)Integer(record, 420) : EntityGeometry.AciColor(Math.Abs(aci));
            var layer = new Layer(layerName, color, aci >= 0 && (flags & 1) == 0, (flags & 4) != 0, Math.Max(0, Number(record, 370, 25)) / 100) { Linetype = String(record, 6, "CONTINUOUS") };
            state = state with { Layers = state.Layers.SetItem(layerName, layer) };
        }
        var blockNamesByHandle = Records(sections.FirstOrDefault(s => s.Name == "TABLES")?.Pairs ?? []).Where(r => Type(r) == "BLOCK_RECORD").ToDictionary(r => String(r, 5), r => String(r, 2), StringComparer.OrdinalIgnoreCase);
        var layoutsByOwner = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (Records(sections.FirstOrDefault(s => s.Name == "OBJECTS")?.Pairs ?? []).Any(r => Type(r) == "LAYOUT"))
            state = state with { LayoutBlockNames = ImmutableDictionary.Create<string, string>(StringComparer.OrdinalIgnoreCase) };
        foreach (var record in Records(sections.FirstOrDefault(s => s.Name == "OBJECTS")?.Pairs ?? []).Where(r => Type(r) == "LAYOUT"))
        {
            var layout = record.SkipWhile(p => p.Code != 100 || p.Value != "AcDbLayout").ToImmutableArray();
            var layoutTitle = String(layout, 1); var owner = String(layout, 330);
            if (layoutTitle.Length == 0 || !blockNamesByHandle.TryGetValue(owner, out var blockName)) continue;
            layoutsByOwner[owner] = layoutTitle; state = state with { LayoutBlockNames = state.LayoutBlockNames.SetItem(layoutTitle, blockName) };
        }
        string LayoutFor(ImmutableArray<DxfPair> record) => Has(record, 410) ? String(record, 410) : layoutsByOwner.TryGetValue(String(record, 330), out var name) ? name : Integer(record, 67) == 0 ? "Model" : "Layout1";
        var layoutEntities = ImmutableArray.CreateBuilder<Entity>();
        Entity Parse(ImmutableArray<DxfPair> record)
        {
            var type = Type(record); Entity entity;
            try
            {
                entity = type == "DIMENSION"
                    ? DxfDimensions.Read(DxfRecordEditing.SemanticPairs(record), record, dimensionStyles, warnings.Add)
                    : DxfEntityReader.Read(DxfRecordEditing.SemanticPairs(record), warnings.Add);
                if (entity is OpaqueEntity opaqueSource) entity = opaqueSource with { RawRecord = Encode(record) };
                if (entity is CompositeEntity compoundSource && compoundSource.SourceRecord.Length > 0)
                    entity = compoundSource with { SourceRecord = Encode(record) };
                // Validate before admitting a typed record; bad geometry is retained opaque.
                var layers = state.Layers;
                void CollectLayers(Entity e) { if (!layers.ContainsKey(e.Layer)) layers = layers.Add(e.Layer, new(e.Layer)); if (e is PlacedEntity placed) CollectLayers(placed.Geometry); if (e is CompositeEntity compound) foreach (var child in compound.Children) CollectLayers(child); }
                CollectLayers(entity); CadDocument.ValidateEntity(entity, state with { Layers = layers });
            }
            catch (Exception ex) when (ex is ArgumentException or FormatException or OverflowException or NotSupportedException)
            {
                warnings.Add($"{type} was retained as opaque: {ex.Message}"); entity = Opaque(type, record);
            }
            var fields = DxfRecordEditing.SemanticPairs(DxfEntityReader.Records(record).First());
            var layerName = String(fields, 8, "0");
            if (!state.Layers.ContainsKey(layerName)) state = state with { Layers = state.Layers.Add(layerName, new(layerName)) };
            entity = entity with { Handle = String(fields, 5), Layer = layerName, ColorIndex = Integer(fields, 62, 256), TrueColor = Has(fields, 420) ? 0xFF000000u | (uint)Integer(fields, 420) : null, LineWeight = Number(fields, 370, -100) / 100, Visible = Integer(fields, 60) == 0, Layout = LayoutFor(fields), Linetype = String(fields, 6, "BYLAYER"), LinetypeScale = Number(fields, 48, 1) };
            sourceRecords[entity.Id] = record; return entity;
        }
        var blockRecords = DxfEntityReader.LogicalRecords(sections.FirstOrDefault(s => s.Name == "BLOCKS")?.Pairs ?? []).ToArray();
        for (var i = 0; i < blockRecords.Length; i++)
        {
            if (Type(blockRecords[i]) != "BLOCK") continue;
            var header = blockRecords[i]; var blockName = String(header, 2); var entities = ImmutableArray.CreateBuilder<Entity>();
            while (++i < blockRecords.Length && Type(blockRecords[i]) != "ENDBLK") entities.Add(Parse(blockRecords[i]));
            var layoutName = state.LayoutBlockNames.FirstOrDefault(p => p.Value.Equals(blockName, StringComparison.OrdinalIgnoreCase)).Key;
            if (layoutName != null) { layoutEntities.AddRange(entities.Select(e => e with { Layout = layoutName })); entities.Clear(); }
            state = state with { Blocks = state.Blocks.SetItem(blockName, new(blockName, Point(header, 10), entities.ToImmutable())) };
        }
        var model = DxfEntityReader.LogicalRecords(sections.FirstOrDefault(s => s.Name == "ENTITIES")?.Pairs ?? []).Select(Parse).Concat(layoutEntities).ToImmutableArray();
        state = state with { Entities = model };
        var headerPairs = sections.FirstOrDefault(s => s.Name == "HEADER")?.Pairs ?? [];
        for (var i = 0; i + 1 < headerPairs.Length; i++) if (headerPairs[i].Code == 9 && headerPairs[i].Value == "$INSUNITS") state = state with { Units = int.TryParse(headerPairs[i + 1].Value, out var units) ? units : 4 };
        var opaque = model.Count(e => e is OpaqueEntity) + state.Blocks.Values.Sum(b => b.Entities.Count(e => e is OpaqueEntity));
        if (opaque > 0) warnings.Add($"{opaque} unsupported records were preserved but are not rendered or editable.");
        if (model.Any(e => e is TextEntity { Multiline: true })) warnings.Add("MTEXT display uses plain text; full formatting and attachment semantics are not implemented.");
        // Compound children can have independent layers (notably INSERT attributes).
        void EnsureLayers(Entity e)
        {
            if (!state.Layers.ContainsKey(e.Layer)) state = state with { Layers = state.Layers.Add(e.Layer, new(e.Layer)) };
            if (e is CompositeEntity composite) foreach (var child in composite.Children) EnsureLayers(child);
            if (e is PlacedEntity placed) EnsureLayers(placed.Geometry);
        }
        foreach (var e in state.Entities.Concat(state.Blocks.Values.SelectMany(b => b.Entities))) EnsureLayers(e);
        CadDocument.Validate(state);
        var source = new DxfSource(text, state, sections.ToImmutable(), sourceRecords.ToImmutable());
        return new(state, source, warnings.Distinct().ToImmutableArray());
    }
    public static DxfWriteResult Write(Drawing drawing, DxfSource? source = null)
    {
        CadDocument.Validate(drawing);
        if (source != null && drawing == source.Original) return new(source.Text, []);
        var warnings = ImmutableArray.CreateBuilder<string>();
        ulong handle = 0x100;
        if (source != null) foreach (var pair in source.Sections.SelectMany(s => s.Pairs)) if (pair.Code is 5 or 105 && ulong.TryParse(pair.Value, NumberStyles.HexNumber, Culture, out var value)) handle = Math.Max(handle, value);
        foreach (var entity in drawing.Entities.Concat(drawing.Blocks.Values.SelectMany(b => b.Entities)))
            if (ulong.TryParse(entity.Handle, NumberStyles.HexNumber, Culture, out var value)) handle = Math.Max(handle, value);
        void ReserveChildren(Entity e)
        {
            if (e is CompositeEntity c)
            {
                if (c.SourceRecord.Length > MaximumCharacters) throw new ArgumentException("Retained compound exceeds the DXF size limit.");
                if (c.SourceRecord.Length > 0) foreach (var pair in ParsePairs(c.SourceRecord))
                    if (pair.Code is 5 or 105 && ulong.TryParse(pair.Value, NumberStyles.HexNumber, Culture, out var child)) handle = Math.Max(handle, child);
                foreach (var child in c.Children) ReserveChildren(child);
            }
            else if (e is PlacedEntity p) ReserveChildren(p.Geometry);
        }
        foreach (var entity in drawing.Entities.Concat(drawing.Blocks.Values.SelectMany(b => b.Entities))) ReserveChildren(entity);
        string NewHandle() => checked(++handle).ToString("X", Culture);
        var originals = source?.Original.Entities.Concat(source.Original.Blocks.Values.SelectMany(b => b.Entities)).ToDictionary(e => e.Id) ?? new();
        var dimensions = new DxfDimensions.Exporter(drawing, source, NewHandle, warnings.Add);
        var emitted = new Dictionary<Entity, string>(ReferenceEqualityComparer.Instance);
        string Emit(Entity entity)
        {
            if (!emitted.TryGetValue(entity, out var text)) emitted[entity] = text = EmitCore(entity);
            return text;
        }
        string EmitCore(Entity entity)
        {
            if (source != null && originals.TryGetValue(entity.Id, out var original) && entity == original && source.Records.TryGetValue(entity.Id, out var raw)) return Encode(raw);
            if (source != null && originals.TryGetValue(entity.Id, out var prior) && source.Records.TryGetValue(entity.Id, out var retained)
                && (DxfAttributeEditing.TryWrite(entity, prior, retained, warnings.Add, out var patched)
                    || DxfRecordEditing.TryWrite(entity, prior, retained, warnings.Add, out patched))) return patched;
            if (entity is OpaqueEntity opaque) return opaque.RawRecord;
            if (DimensionGeometry.Unwrap(entity) != null) return dimensions.Write(entity);
            var buffer = new StringBuilder();
            void Pair(int code, object value) => buffer.Append(code.ToString(Culture)).Append('\n').Append(Convert.ToString(value, Culture)).Append('\n');
            void Position(int code, Vec3 p) { Pair(code, p.X); Pair(code + 10, p.Y); Pair(code + 20, p.Z); }
            void Start(string type, string subclass, string? forcedHandle = null)
            {
                Pair(0, type); Pair(5, forcedHandle ?? (string.IsNullOrEmpty(entity.Handle) ? NewHandle() : entity.Handle)); Pair(100, "AcDbEntity"); Pair(8, entity.Layer);
                Pair(6, entity.Linetype); Pair(48, entity.LinetypeScale);
                if (!entity.Visible) Pair(60, 1);
                if (entity.Layout != "Model") { Pair(67, 1); Pair(410, entity.Layout); }
                Pair(62, entity.ColorIndex); if (entity.TrueColor is uint c) Pair(420, c & 0xFFFFFF);
                if (entity.LineWeight >= 0) Pair(370, Math.Round(entity.LineWeight * 100)); Pair(100, subclass);
            }
            if (DxfEntityWriter.TryWrite(entity, drawing, Emit, NewHandle, warnings.Add, out var advancedText))
            {
                if (source != null && source.Records.ContainsKey(entity.Id)) warnings.Add($"Edited {entity.Kind}: unmodeled metadata may not be retained.");
                return advancedText;
            }
            switch (entity)
            {
                case LineEntity line: Start("LINE", "AcDbLine"); Position(10, line.Start); Position(11, line.End); break;
                case PointEntity point: Start("POINT", "AcDbPoint"); Position(10, point.Position); break;
                case CircleEntity circle: Start("CIRCLE", "AcDbCircle"); Position(10, circle.Center); Pair(40, circle.Radius); break;
                case ArcEntity arc: Start("ARC", "AcDbCircle"); Position(10, arc.Center); Pair(40, arc.Radius); Pair(100, "AcDbArc"); Pair(50, arc.StartAngle); Pair(51, arc.EndAngle); break;
                case PolylineEntity poly:
                    if (poly.Vertices.Any(v => Math.Abs(v.Position.Z - poly.Vertices[0].Position.Z) > 1e-8)) throw new NotSupportedException("LWPOLYLINE requires a constant elevation.");
                    Start("LWPOLYLINE", "AcDbPolyline"); Pair(90, poly.Vertices.Length); Pair(70, (poly.Closed ? 1 : 0) | (poly.ContinuousLinetype ? 128 : 0)); Pair(38, poly.Vertices[0].Position.Z); if (poly.ConstantWidth != 0) Pair(43, poly.ConstantWidth);
                    foreach (var v in poly.Vertices) { Pair(10, v.Position.X); Pair(20, v.Position.Y); if (v.Bulge != 0) Pair(42, v.Bulge); if (v.StartWidth != 0) Pair(40, v.StartWidth); if (v.EndWidth != 0) Pair(41, v.EndWidth); } break;
                case EllipseEntity ellipse: Start("ELLIPSE", "AcDbEllipse"); Position(10, ellipse.Center); Position(11, ellipse.MajorAxis); Pair(40, ellipse.Ratio); Pair(41, ellipse.StartParameter); Pair(42, ellipse.EndParameter); break;
                case TextEntity text:
                    Start(text.Multiline ? "MTEXT" : "TEXT", text.Multiline ? "AcDbMText" : "AcDbText"); Position(10, text.Position); Pair(40, text.Height);
                    Pair(1, text.Text.Replace("\r", "").Replace("\n", text.Multiline ? "\\P" : " ")); Pair(50, text.Multiline ? GeometryMath.Radians(text.Rotation) : text.Rotation);
                    if (text.Multiline) { Pair(41, 0); Pair(71, 1); } else Pair(100, "AcDbText"); break;
                case BlockReferenceEntity insert: Start("INSERT", "AcDbBlockReference"); Pair(2, insert.Name); Position(10, insert.Position); Pair(41, insert.Scale.X); Pair(42, insert.Scale.Y); Pair(43, insert.Scale.Z); Pair(50, insert.Rotation); break;
                default: throw new NotSupportedException($"Export of {entity.Kind} is not implemented.");
            }
            if (source != null && source.Records.ContainsKey(entity.Id)) warnings.Add($"Edited {entity.Kind} {entity.Handle}: unmodeled per-entity metadata is not retained. Keep the original DXF.");
            return buffer.ToString();
        }
        // Discover generated dimension pictures/styles before the document writer allocates ownership tables.
        foreach (var entity in drawing.Entities.Concat(drawing.Blocks.Values.SelectMany(b => b.Entities))) Emit(entity);
        var exportDrawing = dimensions.Blocks.Count == 0 ? drawing : drawing with { Blocks = drawing.Blocks.SetItems(dimensions.Blocks) };
        var text = DxfDocumentWriter.Write(exportDrawing, source, Emit, NewHandle, dimensions.StyleRecords);
        return new(text, warnings.Distinct().ToImmutableArray());
    }
    public static ImmutableArray<DxfPair> ParsePairs(string text)
    {
        using var reader = new StringReader(text.TrimStart('\uFEFF'));
        var result = ImmutableArray.CreateBuilder<DxfPair>(); string? line; var number = 0;
        while ((line = reader.ReadLine()) != null)
        {
            number++;
            if (string.IsNullOrWhiteSpace(line) && reader.Peek() == -1) break;
            if (!int.TryParse(line.Trim(), NumberStyles.Integer, Culture, out var code) || code < 0 || code > 1071) throw new FormatException($"Invalid group code at line {number}.");
            var value = reader.ReadLine() ?? throw new FormatException($"Missing group value at line {number + 1}."); number++;
            result.Add(new(code, value));
        }
        return result.ToImmutable();
    }
    private static IEnumerable<ImmutableArray<DxfPair>> Records(ImmutableArray<DxfPair> pairs)
    {
        var record = ImmutableArray.CreateBuilder<DxfPair>();
        foreach (var pair in pairs)
        {
            if (pair.Code == 0 && record.Count != 0) { yield return record.ToImmutable(); record.Clear(); }
            record.Add(pair);
        }
        if (record.Count != 0) yield return record.ToImmutable();
    }
    private static OpaqueEntity Opaque(string type, ImmutableArray<DxfPair> pairs) => new(type, Encode(pairs));
    private static bool Has(ImmutableArray<DxfPair> p, int c) => p.Any(v => v.Code == c);
    private static string Type(ImmutableArray<DxfPair> p) => String(p, 0).Trim();
    private static string String(ImmutableArray<DxfPair> p, int c, string fallback = "") => p.FirstOrDefault(v => v.Code == c).Value ?? fallback;
    private static double ParseNumber(string s) => GeometryMath.Number(s, out var value) ? value : throw new FormatException($"Invalid numeric value: {s}");
    private static double Number(ImmutableArray<DxfPair> p, int c, double fallback = 0) => Has(p, c) ? ParseNumber(String(p, c)) : fallback;
    private static double Positive(ImmutableArray<DxfPair> p, int c) { var value = Number(p, c); return value > 0 ? value : throw new FormatException("Value must be positive."); }
    private static int Integer(ImmutableArray<DxfPair> p, int c, int fallback = 0) => Has(p, c) ? int.Parse(String(p, c), Culture) : fallback;
    private static Vec3 Point(ImmutableArray<DxfPair> p, int c, Vec3 fallback = default) => new(Number(p, c, fallback.X), Number(p, c + 10, fallback.Y), Number(p, c + 20, fallback.Z));
    private static string Encode(IEnumerable<DxfPair> p) => string.Concat(p.Select(v => $"{v.Code.ToString(Culture)}\n{v.Value}\n"));
    private static string F(double value) => value.ToString("R", Culture);
}
