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
            writer.WriteStartObject(); writer.WriteString("format", "CadSpace"); writer.WriteNumber("version", HasGradients(drawing) || source != null && HasGradients(source.Original) ? 4 : HasNativeDimensions(drawing) || source != null && HasNativeDimensions(source.Original) ? 3 : 2);
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
        if (root.GetProperty("format").GetString() != "CadSpace" || root.GetProperty("version").GetInt32() is not (1 or 2 or 3 or 4)) throw new FormatException("Unsupported CadSpace project format/version.");
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
}
