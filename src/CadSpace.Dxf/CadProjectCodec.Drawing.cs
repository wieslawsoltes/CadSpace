using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using CadSpace.Geometry;
using CadSpace.Model;

namespace CadSpace.Dxf;

public static partial class CadProjectCodec
{
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
