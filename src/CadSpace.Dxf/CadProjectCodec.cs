using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using CadSpace.Geometry;
using CadSpace.Model;

namespace CadSpace.Dxf;

public sealed record CadProjectReadResult(Drawing Drawing, DxfSource? DxfSource);

/// <summary>Versioned, lossless native persistence for the implemented document model. No reflection or executable payloads.</summary>
public static partial class CadProjectCodec
{
    public const int MaximumCharacters = 128 * 1024 * 1024;
    public static string Write(Drawing drawing, DxfSource? source = null)
    {
        CadDocument.Validate(drawing);
        using var memory = new MemoryStream();
        using (var writer = new Utf8JsonWriter(memory, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject(); writer.WriteString("format", "CadSpace"); writer.WriteNumber("version", HasNativeDimensions(drawing) || source != null && HasNativeDimensions(source.Original) ? 3 : 2);
            writer.WritePropertyName("drawing"); WriteDrawing(writer, drawing);
            if (source != null)
            {
                writer.WriteString("dxfOriginal", source.Text);
                if (source.OriginalBytes is { } bytes) writer.WriteBase64String("dxfBytes", bytes);
                writer.WritePropertyName("sourceGraph"); WriteDrawing(writer, source.Original);
                writer.WritePropertyName("sourceIds"); writer.WriteStartObject();
                writer.WritePropertyName("model"); WriteIds(writer, source.Original.Entities);
                writer.WritePropertyName("blocks"); writer.WriteStartObject();
                foreach (var block in source.Original.Blocks.Values) { writer.WritePropertyName(block.Name); WriteIds(writer, block.Entities); }
                writer.WriteEndObject(); writer.WriteEndObject();
            }
            writer.WriteEndObject();
        }
        if (memory.Length > MaximumCharacters) throw new InvalidOperationException("Native project exceeds the 128 MiB save limit.");
        return Encoding.UTF8.GetString(memory.ToArray());
    }
    public static CadProjectReadResult Read(string text)
    {
        if (text.Length > MaximumCharacters) throw new FormatException("Native project exceeds the 128 Mi-character read limit.");
        using var document = JsonDocument.Parse(text, new JsonDocumentOptions { MaxDepth = 128 });
        var root = document.RootElement;
        if (root.GetProperty("format").GetString() != "CadSpace" || root.GetProperty("version").GetInt32() is not (1 or 2 or 3)) throw new FormatException("Unsupported CadSpace project format/version.");
        var drawing = ReadDrawing(root.GetProperty("drawing"));
        DxfSource? source = null;
        if (root.TryGetProperty("dxfOriginal", out var raw))
        {
            var parsed = root.TryGetProperty("dxfBytes", out var originalBytes)
                ? DxfBinary.Read(originalBytes.GetBytesFromBase64(), drawing.Name)
                : DxfCodec.Read(raw.GetString() ?? throw new FormatException("Missing original DXF."), drawing.Name);
            var ids = root.GetProperty("sourceIds"); var map = new Dictionary<Guid, Guid>();
            ImmutableArray<Entity> Remap(ImmutableArray<Entity> entities, JsonElement identifiers)
            {
                var saved = identifiers.EnumerateArray().Select(i => i.GetGuid()).ToArray();
                if (saved.Length != entities.Length) throw new FormatException("The original DXF entity map is inconsistent.");
                return entities.Select((entity, i) => { if (!map.TryAdd(entity.Id, saved[i])) throw new FormatException("Duplicate source entity identifier."); return entity with { Id = saved[i] }; }).ToImmutableArray();
            }
            var original = parsed.Drawing with { Entities = Remap(parsed.Drawing.Entities, ids.GetProperty("model")) };
            var blocks = original.Blocks;
            foreach (var block in parsed.Drawing.Blocks.Values) blocks = blocks.SetItem(block.Name, block with { Entities = Remap(block.Entities, ids.GetProperty("blocks").GetProperty(block.Name)) });
            original = original with { Blocks = blocks };
            if (map.Values.Distinct().Count() != map.Count) throw new FormatException("Duplicate persistent source entity identifiers.");
            var recordMap = parsed.Source.Records.ToImmutableDictionary(p => map[p.Key], p => p.Value);
            if (root.TryGetProperty("sourceGraph", out var sourceGraph))
            {
                var savedGraph = ReadDrawing(sourceGraph);
                if (!savedGraph.Entities.Select(e => e.Id).SequenceEqual(original.Entities.Select(e => e.Id)) ||
                    savedGraph.Blocks.Count != original.Blocks.Count || savedGraph.Blocks.Any(b => !original.Blocks.TryGetValue(b.Key, out var old) || !b.Value.Entities.Select(e => e.Id).SequenceEqual(old.Entities.Select(e => e.Id))))
                    throw new FormatException("Inconsistent source provenance graph.");
                // Never trust serialized provenance as evidence that edited geometry matches raw DXF.
                if(savedGraph.Units!=original.Units || savedGraph.Layers.Count!=original.Layers.Count || savedGraph.Layers.Any(p=>!original.Layers.TryGetValue(p.Key,out var layer)||layer!=p.Value) ||
                   savedGraph.LayoutBlockNames.Count!=original.LayoutBlockNames.Count || savedGraph.LayoutBlockNames.Any(p=>!original.LayoutBlockNames.TryGetValue(p.Key,out var value)||value!=p.Value))
                    throw new FormatException("Provenance tables do not match original DXF.");
                for(var i=0;i<original.Entities.Length;i++) VerifySourceEntity(original.Entities[i],savedGraph.Entities[i],recordMap.GetValueOrDefault(original.Entities[i].Id));
                foreach(var (name,block) in original.Blocks)
                {
                    var saved=savedGraph.Blocks[name]; if(block.BasePoint!=saved.BasePoint)throw new FormatException("Provenance block base point differs from DXF.");
                    for(var i=0;i<block.Entities.Length;i++)VerifySourceEntity(block.Entities[i],saved.Entities[i],recordMap.GetValueOrDefault(block.Entities[i].Id));
                }
                if (savedGraph.LinetypeScale != original.LinetypeScale || savedGraph.Linetypes.Count != original.Linetypes.Count || savedGraph.Linetypes.Any(p => !original.Linetypes.TryGetValue(p.Key, out var t) || !Linetype.Equivalent(p.Value, t))) throw new FormatException("Provenance linetypes differ from the original DXF.");
                CadDocument.Validate(savedGraph); original = savedGraph;
            }
            source = new(parsed.Source.Text, original, parsed.Source.Sections, recordMap) { OriginalBytes = parsed.Source.OriginalBytes };
            // Reuse equal imported instances so untouched group data and exact original-text pass-through remain available.
            drawing = Intern(drawing, original);
        }
        CadDocument.Validate(drawing); return new(drawing, source);
    }
    private static Entity VerifySourceEntity(Entity actual, Entity saved, ImmutableArray<DxfPair> originalRecord = default)
    {
        actual=actual with{Id=saved.Id};
        if (saved is OpaqueEntity opaque && actual is not OpaqueEntity)
        {
            // An older interpreter may have retained a now-supported root as opaque data.
            // Preserve that native representation only after validating ALL original group values
            // and common properties, not merely its displayed geometry or a claimed checksum.
            if (opaque.RawRecord.Length > DxfCodec.MaximumCharacters || originalRecord.IsDefaultOrEmpty || originalRecord[0].Code != 0 ||
                originalRecord[0].Value.Trim() != opaque.DxfType ||
                !DxfCodec.ParsePairs(opaque.RawRecord).SequenceEqual(originalRecord) ||
                CommonProperties(actual) != CommonProperties(saved))
                throw new FormatException("Opaque provenance does not match the original DXF record.");
            return saved;
        }
        if(actual is PlacedEntity a && saved is PlacedEntity b) actual=a with{Geometry=VerifySourceEntity(a.Geometry,b.Geometry)};
        if(actual is CompositeEntity x && saved is CompositeEntity y)
        {
            if(x.Children.Length!=y.Children.Length)throw new FormatException("Provenance child counts differ from DXF.");
            actual=x with{Children=x.Children.Zip(y.Children).Select(p=>VerifySourceEntity(p.First,p.Second)).ToImmutableArray()};
        }
        if (!Equivalent(actual, saved))
        {
            if (!originalRecord.IsDefaultOrEmpty && originalRecord[0].Value.Trim() == "DIMENSION")
            {
                // Older projects used anonymous display wrappers or basic aligned definitions. Validate
                // the entire earlier interpretation, including raw data, instead of trusting a saved picture.
                var legacy = DxfEntityReader.Read(DxfRecordEditing.SemanticPairs(originalRecord), _ => { });
                if (legacy is CompositeEntity c) legacy = c with { SourceRecord = string.Concat(originalRecord.Select(p => $"{p.Code.ToString(System.Globalization.CultureInfo.InvariantCulture)}\n{p.Value}\n")) };
                return VerifySourceEntity(legacy with { Layout = actual.Layout }, saved);
            }
            throw new FormatException("Provenance geometry differs from the original DXF. Refusing unsafe source-record reuse.");
        }
        return saved;
    }
    private static (string Handle, string Layer, int Aci, uint? Color, double Weight,
        string Linetype, double Scale, bool Visible, string Layout) CommonProperties(Entity e) =>
        (e.Handle, e.Layer, e.ColorIndex, e.TrueColor, e.LineWeight, e.Linetype, e.LinetypeScale, e.Visible, e.Layout);
    private static Drawing Intern(Drawing drawing, Drawing original)
    {
        var originals = original.Entities.Concat(original.Blocks.Values.SelectMany(b => b.Entities)).ToDictionary(e => e.Id);
        Entity Canonical(Entity entity) => originals.TryGetValue(entity.Id, out var old) && Equivalent(entity, old) ? old : entity;
        var entities = drawing.Entities.Select(Canonical).ToImmutableArray();
        if (entities.SequenceEqual(original.Entities)) entities = original.Entities;
        var layers = drawing.Layers;
        if (layers.Count == original.Layers.Count && layers.All(p => original.Layers.TryGetValue(p.Key, out var l) && l == p.Value)) layers = original.Layers;
        var blocks = drawing.Blocks;
        foreach (var block in drawing.Blocks.Values)
        {
            var children = block.Entities.Select(Canonical).ToImmutableArray();
            var updated = block with { Entities = children };
            if (original.Blocks.TryGetValue(block.Name, out var old) && old.BasePoint == block.BasePoint && children.SequenceEqual(old.Entities)) updated = old;
            blocks = blocks.SetItem(block.Name, updated);
        }
        if (blocks.Count == original.Blocks.Count && blocks.All(p => original.Blocks.TryGetValue(p.Key, out var b) && b == p.Value)) blocks = original.Blocks;
        var layouts = drawing.LayoutBlockNames;
        if (layouts.Count == original.LayoutBlockNames.Count && layouts.All(p => original.LayoutBlockNames.TryGetValue(p.Key, out var name) && name == p.Value)) layouts = original.LayoutBlockNames;
        var types = drawing.Linetypes;
        if (types.Count == original.Linetypes.Count && types.All(p => original.Linetypes.TryGetValue(p.Key, out var before) && Linetype.Equivalent(p.Value, before))) types = original.Linetypes;
        return drawing with { Entities = entities, Layers = layers, Blocks = blocks, LayoutBlockNames = layouts, Linetypes = types };
    }
    private static bool Equivalent(Entity a, Entity b) => (a, b) switch
    {
        (PolylineEntity x, PolylineEntity y) => x.Vertices.SequenceEqual(y.Vertices) && x with { Vertices = y.Vertices } == y,
        (HatchEntity x, HatchEntity y) => x.Boundary.SequenceEqual(y.Boundary) && x with { Boundary = y.Boundary } == y,
        (MeshEntity x, MeshEntity y) => x.Vertices.SequenceEqual(y.Vertices) && x.Triangles.SequenceEqual(y.Triangles) && x with { Vertices = y.Vertices, Triangles = y.Triangles } == y,
        (Polyline3DEntity x, Polyline3DEntity y) => x.Points.SequenceEqual(y.Points) && x with { Points = y.Points } == y,
        (SplineEntity x, SplineEntity y) => x.ControlPoints.SequenceEqual(y.ControlPoints) && x.Knots.SequenceEqual(y.Knots) && x.Weights.SequenceEqual(y.Weights) && x with { ControlPoints = y.ControlPoints, Knots = y.Knots, Weights = y.Weights } == y,
        (PlacedEntity x, PlacedEntity y) => Equivalent(x.Geometry, y.Geometry) && x with { Geometry = y.Geometry } == y,
        (CompositeEntity x, CompositeEntity y) => x.Children.Length == y.Children.Length && x.Children.Zip(y.Children).All(p => Equivalent(p.First, p.Second)) && x with { Children = y.Children } == y,
        (HatchRegionEntity x, HatchRegionEntity y) => x.Loops.Length == y.Loops.Length && x.Loops.Zip(y.Loops).All(p => p.First.SequenceEqual(p.Second)) && x.Pattern.Length == y.Pattern.Length && x.Pattern.Zip(y.Pattern).All(p => p.First.Dashes.SequenceEqual(p.Second.Dashes) && p.First with { Dashes = p.Second.Dashes } == p.Second) && x with { Loops = y.Loops, Pattern = y.Pattern } == y,
        _ => a == b
    };
    private static void WriteIds(Utf8JsonWriter writer, IEnumerable<Entity> entities)
    {
        writer.WriteStartArray(); foreach (var entity in entities) writer.WriteStringValue(entity.Id); writer.WriteEndArray();
    }
    private static void WriteDrawing(Utf8JsonWriter w, Drawing drawing)
    {
        w.WriteStartObject(); w.WriteString("name", drawing.Name); w.WriteNumber("units", drawing.Units); w.WriteNumber("linetypeScale", drawing.LinetypeScale);
        w.WritePropertyName("linetypes"); w.WriteStartArray();
        foreach (var type in drawing.Linetypes.Values.OrderBy(l => l.Name, StringComparer.Ordinal))
        { w.WriteStartObject(); w.WriteString("name", type.Name); w.WriteString("description", type.Description); Numbers(w, "elements", type.Elements); w.WriteBoolean("complex", type.IsComplex); w.WriteEndObject(); }
        w.WriteEndArray();
        w.WritePropertyName("layouts"); w.WriteStartObject(); foreach (var layout in drawing.LayoutBlockNames.OrderBy(p => p.Key, StringComparer.Ordinal)) w.WriteString(layout.Key, layout.Value); w.WriteEndObject();
        w.WritePropertyName("layers"); w.WriteStartArray();
        foreach (var layer in drawing.Layers.Values.OrderBy(l => l.Name, StringComparer.Ordinal))
        {
            w.WriteStartObject(); w.WriteString("name", layer.Name); w.WriteNumber("color", layer.Color); w.WriteBoolean("visible", layer.Visible); w.WriteBoolean("locked", layer.Locked); w.WriteNumber("weight", layer.LineWeight); w.WriteString("linetype", layer.Linetype); w.WriteEndObject();
        }
        w.WriteEndArray(); w.WritePropertyName("blocks"); w.WriteStartArray();
        foreach (var block in drawing.Blocks.Values.OrderBy(b => b.Name, StringComparer.Ordinal))
        {
            w.WriteStartObject(); w.WriteString("name", block.Name); Point(w, "base", block.BasePoint); w.WritePropertyName("entities"); Entities(w, block.Entities); w.WriteEndObject();
        }
        w.WriteEndArray(); w.WritePropertyName("entities"); Entities(w, drawing.Entities); w.WriteEndObject();
    }
    private static Drawing ReadDrawing(JsonElement root)
    {
        var layers = ImmutableDictionary.Create<string, Layer>(StringComparer.OrdinalIgnoreCase);
        foreach (var l in root.GetProperty("layers").EnumerateArray())
        {
            var layer = new Layer(S(l, "name"), l.GetProperty("color").GetUInt32(), l.GetProperty("visible").GetBoolean(), l.GetProperty("locked").GetBoolean(), N(l, "weight")) { Linetype = l.TryGetProperty("linetype", out var lt) ? lt.GetString()! : "CONTINUOUS" }; layers = layers.Add(layer.Name, layer);
        }
        var blocks = ImmutableDictionary.Create<string, BlockDefinition>(StringComparer.OrdinalIgnoreCase);
        foreach (var b in root.GetProperty("blocks").EnumerateArray()) { var block = new BlockDefinition(S(b, "name"), P(b, "base"), ReadEntities(b.GetProperty("entities"))); blocks = blocks.Add(block.Name, block); }
        return new(ReadEntities(root.GetProperty("entities")), layers, blocks) { Name = S(root, "name"), Units = root.GetProperty("units").GetInt32(), LinetypeScale = root.TryGetProperty("linetypeScale", out var scale) ? scale.GetDouble() : 1, Linetypes = root.TryGetProperty("linetypes", out var types) ? types.EnumerateArray().Select(t => new Linetype(S(t, "name"), S(t, "description"), Numbers(t, "elements"), t.GetProperty("complex").GetBoolean())).ToImmutableDictionary(t => t.Name, StringComparer.OrdinalIgnoreCase) : Linetype.Defaults, LayoutBlockNames = root.TryGetProperty("layouts", out var layouts) ? layouts.EnumerateObject().ToImmutableDictionary(p => p.Name, p => p.Value.GetString()!, StringComparer.OrdinalIgnoreCase) : Drawing.Empty.LayoutBlockNames };
    }
    private static void Point(Utf8JsonWriter w, string name, Vec3 p) { w.WritePropertyName(name); Point(w, p); }
    private static void Point(Utf8JsonWriter w, Vec3 p) { w.WriteStartArray(); w.WriteNumberValue(p.X); w.WriteNumberValue(p.Y); w.WriteNumberValue(p.Z); w.WriteEndArray(); }
    private static void Points(Utf8JsonWriter w, string name, IEnumerable<Vec3> points) { w.WritePropertyName(name); w.WriteStartArray(); foreach (var p in points) Point(w, p); w.WriteEndArray(); }
    private static void Entities(Utf8JsonWriter w, IEnumerable<Entity> entities)
    {
        w.WriteStartArray();
        foreach (var entity in entities)
        {
            w.WriteStartObject(); w.WriteString("type", entity switch { OpaqueEntity => "OPAQUE", PlacedEntity => "PLACED", CompositeEntity => "COMPOSITE", HatchRegionEntity => "HATCH_REGION", _ => entity.Kind });
            w.WriteString("id", entity.Id); w.WriteString("handle", entity.Handle); w.WriteString("layer", entity.Layer); w.WriteNumber("aci", entity.ColorIndex); w.WriteNumber("weight", entity.LineWeight); w.WriteString("linetype", entity.Linetype); w.WriteNumber("linetypeScale", entity.LinetypeScale); w.WriteBoolean("visible", entity.Visible); w.WriteString("layout", entity.Layout);
            if (entity.TrueColor is uint color) w.WriteNumber("color", color);
            switch (entity)
            {
                case LineEntity e: Point(w, "a", e.Start); Point(w, "b", e.End); break;
                case PointEntity e: Point(w, "point", e.Position); break;
                case CircleEntity e: Point(w, "center", e.Center); w.WriteNumber("radius", e.Radius); break;
                case ArcEntity e: Point(w, "center", e.Center); w.WriteNumber("radius", e.Radius); w.WriteNumber("start", e.StartAngle); w.WriteNumber("end", e.EndAngle); break;
                case PolylineEntity e:
                    w.WriteBoolean("closed", e.Closed); w.WriteNumber("constantWidth", e.ConstantWidth); w.WriteBoolean("continuousLinetype", e.ContinuousLinetype); w.WritePropertyName("vertices"); w.WriteStartArray();
                    foreach (var v in e.Vertices) { w.WriteStartObject(); Point(w, "point", v.Position); w.WriteNumber("bulge", v.Bulge); w.WriteNumber("startWidth", v.StartWidth); w.WriteNumber("endWidth", v.EndWidth); w.WriteEndObject(); } w.WriteEndArray(); break;
                case EllipseEntity e: Point(w, "center", e.Center); Point(w, "major", e.MajorAxis); w.WriteNumber("ratio", e.Ratio); w.WriteNumber("start", e.StartParameter); w.WriteNumber("end", e.EndParameter); break;
                case TextEntity e: Point(w, "point", e.Position); w.WriteString("text", e.Text); w.WriteNumber("height", e.Height); w.WriteNumber("rotation", e.Rotation); break;
                case DimensionEntity e: WriteDimension(w, e); break;
                case HatchEntity e: Points(w, "boundary", e.Boundary); w.WriteNumber("spacing", e.Spacing); w.WriteNumber("angle", e.Angle); w.WriteBoolean("solid", e.Solid); break;
                case MeshEntity e: Points(w, "vertices", e.Vertices); w.WritePropertyName("triangles"); w.WriteStartArray(); foreach (var index in e.Triangles) w.WriteNumberValue(index); w.WriteEndArray(); w.WriteString("operation", e.Operation); break;
                case BlockReferenceEntity e: w.WriteString("name", e.Name); Point(w, "point", e.Position); Point(w, "scale", e.Scale); w.WriteNumber("rotation", e.Rotation); break;
                case Polyline3DEntity e: Points(w, "points", e.Points); w.WriteBoolean("closed", e.Closed); w.WriteBoolean("continuousLinetype", e.ContinuousLinetype); break;
                case SplineEntity e:
                    w.WriteNumber("degree", e.Degree); Points(w, "controls", e.ControlPoints); Numbers(w, "knots", e.Knots); Numbers(w, "weights", e.Weights); w.WriteBoolean("closed", e.Closed); w.WriteBoolean("periodic", e.Periodic); break;
                case PlacedEntity e:
                    Points(w, "matrix", [e.Placement.X, e.Placement.Y, e.Placement.Z, e.Placement.Origin]); w.WritePropertyName("geometry"); Entities(w, [e.Geometry]); break;
                case CompositeEntity e:
                    w.WriteString("dxfType", e.DxfType); w.WriteString("source", e.SourceRecord); w.WritePropertyName("children"); Entities(w, e.Children); break;
                case HatchRegionEntity e:
                    w.WriteString("patternName", e.PatternName); w.WriteNumber("islandStyle", e.IslandStyle); w.WriteBoolean("solid", e.Solid); w.WriteBoolean("sampled", e.SampledBoundary);
                    w.WritePropertyName("loops"); w.WriteStartArray();
                    foreach (var loop in e.Loops) { w.WriteStartArray(); foreach (var v in loop) { w.WriteStartObject(); Point(w, "point", v.Position); w.WriteNumber("bulge", v.Bulge); w.WriteEndObject(); } w.WriteEndArray(); } w.WriteEndArray();
                    w.WritePropertyName("pattern"); w.WriteStartArray();
                    foreach (var line in e.Pattern) { w.WriteStartObject(); w.WriteNumber("angle", line.Angle); Point(w, "origin", line.Origin); Point(w, "offset", line.Offset); Numbers(w, "dashes", line.Dashes); w.WriteEndObject(); } w.WriteEndArray(); break;
                case OpaqueEntity e: w.WriteString("dxfType", e.DxfType); w.WriteString("raw", e.RawRecord); break;
                default: throw new NotSupportedException($"Native persistence does not know entity {entity.Kind}.");
            }
            w.WriteEndObject();
        }
        w.WriteEndArray();
    }
    private static ImmutableArray<Entity> ReadEntities(JsonElement array)
    {
        return array.EnumerateArray().Select(e =>
        {
            var type = S(e, "type");
            Entity entity = type switch
            {
                "LINE" => new LineEntity(P(e, "a"), P(e, "b")),
                "POINT" => new PointEntity(P(e, "point")),
                "CIRCLE" => new CircleEntity(P(e, "center"), N(e, "radius")),
                "ARC" => new ArcEntity(P(e, "center"), N(e, "radius"), N(e, "start"), N(e, "end")),
                "LWPOLYLINE" => new PolylineEntity(e.GetProperty("vertices").EnumerateArray().Select(v => new PolyVertex(P(v, "point"), N(v, "bulge")) { StartWidth = v.TryGetProperty("startWidth", out var sw) ? sw.GetDouble() : 0, EndWidth = v.TryGetProperty("endWidth", out var ew) ? ew.GetDouble() : 0 }).ToImmutableArray(), e.GetProperty("closed").GetBoolean()) { ConstantWidth = e.TryGetProperty("constantWidth", out var cw) ? cw.GetDouble() : 0, ContinuousLinetype = e.TryGetProperty("continuousLinetype", out var generated) && generated.GetBoolean() },
                "ELLIPSE" => new EllipseEntity(P(e, "center"), P(e, "major"), N(e, "ratio"), N(e, "start"), N(e, "end")),
                "TEXT" or "MTEXT" => new TextEntity(P(e, "point"), S(e, "text"), N(e, "height"), N(e, "rotation"), type == "MTEXT"),
                "DIMENSION" => ReadDimension(e),
                "HATCH" => new HatchEntity(Points(e.GetProperty("boundary")), N(e, "spacing"), N(e, "angle"), e.GetProperty("solid").GetBoolean()),
                "MESH" => new MeshEntity(Points(e.GetProperty("vertices")), e.GetProperty("triangles").EnumerateArray().Select(i => i.GetInt32()).ToImmutableArray(), S(e, "operation")),
                "INSERT" => new BlockReferenceEntity(S(e, "name"), P(e, "point"), P(e, "scale"), N(e, "rotation")),
                "POLYLINE" => new Polyline3DEntity(Points(e.GetProperty("points")), e.GetProperty("closed").GetBoolean()) { ContinuousLinetype = e.TryGetProperty("continuousLinetype", out var generated3d) && generated3d.GetBoolean() },
                "SPLINE" => new SplineEntity(e.GetProperty("degree").GetInt32(), Points(e.GetProperty("controls")), Numbers(e, "knots"), Numbers(e, "weights"), e.GetProperty("closed").GetBoolean(), e.GetProperty("periodic").GetBoolean()),
                "PLACED" => new PlacedEntity(ReadEntities(e.GetProperty("geometry")).Single(), Matrix(e.GetProperty("matrix"))),
                "COMPOSITE" => new CompositeEntity(S(e, "dxfType"), ReadEntities(e.GetProperty("children")), S(e, "source")),
                "HATCH_REGION" => new HatchRegionEntity(e.GetProperty("loops").EnumerateArray().Select(l => l.EnumerateArray().Select(v => new PolyVertex(P(v, "point"), N(v, "bulge"))).ToImmutableArray()).ToImmutableArray(), e.GetProperty("solid").GetBoolean(), e.GetProperty("pattern").EnumerateArray().Select(l => new HatchPatternLine(N(l, "angle"), P(l, "origin"), P(l, "offset"), Numbers(l, "dashes"))).ToImmutableArray(), S(e, "patternName"), e.GetProperty("sampled").GetBoolean()) { IslandStyle = e.TryGetProperty("islandStyle", out var style) ? style.GetInt32() : 0 },
                "OPAQUE" => new OpaqueEntity(S(e, "dxfType"), S(e, "raw")),
                _ => throw new FormatException($"Unknown native entity type: {type}")
            };
            return entity with { Id = e.GetProperty("id").GetGuid(), Handle = S(e, "handle"), Layer = S(e, "layer"), ColorIndex = e.GetProperty("aci").GetInt32(), LineWeight = N(e, "weight"), Linetype = e.TryGetProperty("linetype", out var lt) ? lt.GetString()! : "BYLAYER", LinetypeScale = e.TryGetProperty("linetypeScale", out var ls) ? ls.GetDouble() : 1, Visible = !e.TryGetProperty("visible", out var visible) || visible.GetBoolean(), Layout = e.TryGetProperty("layout", out var layout) ? layout.GetString() ?? "Model" : "Model", TrueColor = e.TryGetProperty("color", out var c) ? c.GetUInt32() : null };
        }).ToImmutableArray();
    }
    private static Transform3 Matrix(JsonElement e)
    {
        var columns = Points(e); if (columns.Length != 4) throw new FormatException("Expected four affine matrix columns.");
        return new(columns[0], columns[1], columns[2], columns[3]);
    }
    private static void Numbers(Utf8JsonWriter w, string name, IEnumerable<double> values) { w.WritePropertyName(name); w.WriteStartArray(); foreach (var value in values) w.WriteNumberValue(value); w.WriteEndArray(); }
    private static ImmutableArray<double> Numbers(JsonElement e, string name) => e.GetProperty(name).EnumerateArray().Select(v => v.GetDouble()).ToImmutableArray();
    private static string S(JsonElement e, string key) => e.GetProperty(key).GetString() ?? throw new FormatException($"Missing string: {key}");
    private static double N(JsonElement e, string key) { var value = e.GetProperty(key).GetDouble(); return double.IsFinite(value) ? value : throw new FormatException("Nonfinite value."); }
    private static Vec3 P(JsonElement e, string key) => P(e.GetProperty(key));
    private static Vec3 P(JsonElement e)
    {
        var values = e.EnumerateArray().Select(v => v.GetDouble()).ToArray();
        if (values.Length != 3 || values.Any(v => !double.IsFinite(v))) throw new FormatException("Invalid 3D point.");
        return new(values[0], values[1], values[2]);
    }
    private static ImmutableArray<Vec3> Points(JsonElement array) => array.EnumerateArray().Select(P).ToImmutableArray();
}
