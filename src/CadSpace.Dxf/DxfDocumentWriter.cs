using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using CadSpace.Geometry;
using CadSpace.Model;

namespace CadSpace.Dxf;

/// <summary>Rebuilds ownership/layout links while retaining unrelated source sections and symbol-table records.</summary>
internal static class DxfDocumentWriter
{
    private static readonly CultureInfo Culture = CultureInfo.InvariantCulture;
    private static ImmutableArray<DxfPair> Record(params object[] values)
    {
        var p = ImmutableArray.CreateBuilder<DxfPair>();
        for (var i = 0; i < values.Length; i += 2) p.Add(new((int)values[i], Convert.ToString(values[i + 1], Culture)!));
        return p.ToImmutable();
    }
    private static string Value(IEnumerable<DxfPair> p, int code, string fallback = "") => p.FirstOrDefault(p => p.Code == code).Value?.Trim() ?? fallback;
    private static ImmutableArray<DxfPair> Set(ImmutableArray<DxfPair> record, int code, object value)
    {
        var pair = new DxfPair(code, Convert.ToString(value, Culture)!); var index = record.FindIndex(p => p.Code == code);
        return index < 0 ? record.Add(pair) : record.SetItem(index, pair);
    }
    private static string Encode(IEnumerable<DxfPair> pairs) => string.Concat(pairs.Select(p => $"{p.Code.ToString(Culture)}\n{p.Value}\n"));
    private static ImmutableArray<DxfPair> Owner(ImmutableArray<DxfPair> record, string owner)
    {
        // Reactor dictionaries also use code 330. Only change the unbraced entity owner.
        var pairs = record.ToBuilder(); var nesting = 0;
        for (var i = 1; i < pairs.Count; i++)
        {
            if (pairs[i].Code == 102) { if (pairs[i].Value.StartsWith('{')) nesting++; else if (pairs[i].Value == "}") nesting--; }
            if (nesting == 0 && pairs[i].Code == 330) { pairs[i] = new(330, owner); return pairs.ToImmutable(); }
            if (nesting == 0 && pairs[i].Code == 100) break;
        }
        var handle = pairs.FindIndex(p => p.Code is 5 or 105);
        pairs.Insert(handle < 0 ? 1 : handle + 1, new(330, owner)); return pairs.ToImmutable();
    }
    private static string Owned(string text, string owner)
    {
        string? parent = null; var output = new StringBuilder();
        foreach (var record in DxfEntityReader.Records(DxfCodec.ParsePairs(text)))
        {
            var type = Value(record, 0); var child = type is "VERTEX" or "ATTRIB" or "SEQEND";
            output.Append(Encode(Owner(record, child ? parent ?? owner : owner)));
            if (type == "POLYLINE" || type == "INSERT" && Value(record, 66) == "1") parent = Value(record, 5, owner);
            else if (type == "SEQEND" || !child) parent = null;
        }
        return output.ToString();
    }
    public static string Write(Drawing drawing, DxfSource? source, Func<Entity, string> emit, Func<string> next)
    {
        var sections = source?.Sections ?? [];
        ImmutableArray<DxfPair> Section(string name) => sections.FirstOrDefault(s => s.Name == name)?.Pairs ?? [];
        var oldTables = new Dictionary<string, (ImmutableArray<DxfPair> Header, List<ImmutableArray<DxfPair>> Entries)>(StringComparer.OrdinalIgnoreCase);
        string currentTable = "";
        foreach (var record in DxfEntityReader.Records(Section("TABLES")))
        {
            var type = Value(record, 0);
            if (type == "TABLE") { currentTable = Value(record, 2); oldTables[currentTable] = (record, new()); }
            else if (type == "ENDTAB") currentTable = "";
            else if (currentTable.Length != 0) oldTables[currentTable].Entries.Add(record);
        }
        List<ImmutableArray<DxfPair>> Entries(string name) => oldTables.TryGetValue(name, out var table) ? table.Entries : [];
        var tableIds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string TableId(string name)
        {
            if (!tableIds.TryGetValue(name, out var id)) { id = oldTables.TryGetValue(name, out var t) ? Value(t.Header, 5) : ""; if (id.Length == 0) id = next(); tableIds[name] = id; }
            return id;
        }
        var layouts = drawing.LayoutBlockNames.ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase);
        if (!layouts.ContainsKey("Model")) layouts["Model"] = "*Model_Space";
        foreach (var name in drawing.Entities.Select(e => e.Layout).Distinct(StringComparer.OrdinalIgnoreCase))
            if (!layouts.ContainsKey(name)) { var i = 0; string candidate; do { candidate = $"*Paper_Space{i++}"; } while (layouts.Values.Contains(candidate) || drawing.Blocks.ContainsKey(candidate)); layouts[name] = candidate; }
        var names = drawing.Blocks.Keys.Concat(layouts.Values).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var oldBlockRecords = Entries("BLOCK_RECORD").ToDictionary(p => Value(p, 2), p => p, StringComparer.OrdinalIgnoreCase);
        var blockIds = names.ToDictionary(n => n, n => oldBlockRecords.TryGetValue(n, out var r) && Value(r, 5).Length > 0 ? Value(r, 5) : next(), StringComparer.OrdinalIgnoreCase);
        var oldObjects = DxfEntityReader.Records(Section("OBJECTS")).ToList();
        var oldLayouts = new Dictionary<string, ImmutableArray<DxfPair>>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in oldObjects.Where(r => Value(r, 0) == "LAYOUT"))
        {
            var name = Value(r.SkipWhile(p => p.Code != 100 || p.Value != "AcDbLayout"), 1); if (name.Length > 0) oldLayouts[name] = r;
        }
        var root = oldObjects.FirstOrDefault(r => Value(r, 0) == "DICTIONARY" && Value(r, 330, "0") == "0");
        if (root.IsDefaultOrEmpty) root = Record(0, "DICTIONARY", 5, next(), 330, "0", 100, "AcDbDictionary", 281, 1);
        var rootId = Value(root, 5); var layoutDictId = "";
        for (var i = 0; i + 1 < root.Length; i++) if (root[i].Code == 3 && root[i].Value == "ACAD_LAYOUT") layoutDictId = root[i + 1].Value;
        if (layoutDictId.Length == 0) { layoutDictId = next(); root = root.AddRange(Record(3, "ACAD_LAYOUT", 350, layoutDictId)); }
        var layoutIds = layouts.Keys.ToDictionary(n => n, n => oldLayouts.TryGetValue(n, out var r) ? Value(r, 5) : next(), StringComparer.OrdinalIgnoreCase);
        var objectText = new StringBuilder(Encode(root));
        var layoutDictionary = Record(0, "DICTIONARY", 5, layoutDictId, 330, rootId, 100, "AcDbDictionary", 281, 1).ToBuilder();
        foreach (var (name, id) in layoutIds) layoutDictionary.AddRange(Record(3, name, 350, id));
        objectText.Append(Encode(layoutDictionary));
        foreach (var record in oldObjects)
            if (Value(record, 0) != "LAYOUT" && Value(record, 5) != rootId && Value(record, 5) != layoutDictId) objectText.Append(Encode(record));
        var tab = 0;
        foreach (var (name, blockName) in layouts.OrderBy(l => l.Key.Equals("Model", StringComparison.OrdinalIgnoreCase) ? 0 : 1))
        {
            if (oldLayouts.TryGetValue(name, out var old))
            {
                var split = old.FindIndex(p => p.Code == 100 && p.Value == "AcDbLayout");
                var tail = Set(old.Skip(split).ToImmutableArray(), 330, blockIds[blockName]);
                objectText.Append(Encode(Owner(old.Take(split).Concat(tail).ToImmutableArray(), layoutDictId)));
            }
            else
            {
                objectText.Append(Encode(Record(0, "LAYOUT", 5, layoutIds[name], 330, layoutDictId, 100, "AcDbPlotSettings", 1, "", 70, 0,
                    100, "AcDbLayout", 1, name, 70, 1, 71, tab, 10, 0, 20, 0, 11, 420, 21, 297, 12, 0, 22, 0, 32, 0,
                    14, 0, 24, 0, 34, 0, 15, 420, 25, 297, 35, 0, 146, 0, 13, 0, 23, 0, 33, 0,
                    16, 1, 26, 0, 36, 0, 17, 0, 27, 1, 37, 0, 76, 0, 330, blockIds[blockName])));
            }
            tab++;
        }
        var tables = new StringBuilder();
        void Table(string name, IEnumerable<ImmutableArray<DxfPair>> entries)
        {
            var values = entries.ToList(); var id = TableId(name);
            var header = oldTables.TryGetValue(name, out var old) ? Set(Set(old.Header, 5, id), 70, values.Count) : Record(0, "TABLE", 2, name, 5, id, 330, "0", 100, "AcDbSymbolTable", 70, values.Count);
            tables.Append(Encode(header)); foreach (var record in values) tables.Append(Encode(Owner(record, id))); tables.Append("0\nENDTAB\n");
        }
        foreach (var (name, table) in oldTables.Where(t => t.Key is not ("LTYPE" or "LAYER" or "BLOCK_RECORD"))) Table(name, table.Entries);
        Table("LTYPE", DxfLinetypes.Write(drawing, source, Entries("LTYPE"), next));
        if (!oldTables.ContainsKey("STYLE")) Table("STYLE", [Record(0, "STYLE", 5, next(), 100, "AcDbSymbolTableRecord", 100, "AcDbTextStyleTableRecord", 2, "Standard", 70, 0, 40, 0, 41, 1, 50, 0, 71, 0, 42, 2.5, 3, "txt", 4, "")]);
        if (!oldTables.ContainsKey("APPID")) Table("APPID", [Record(0, "APPID", 5, next(), 100, "AcDbSymbolTableRecord", 100, "AcDbRegAppTableRecord", 2, "ACAD", 70, 0)]);
        var oldLayers = Entries("LAYER").ToDictionary(p => Value(p, 2), p => p, StringComparer.OrdinalIgnoreCase);
        Table("LAYER", drawing.Layers.Values.Select(layer =>
        {
            var r = oldLayers.TryGetValue(layer.Name, out var old) ? old : Record(0, "LAYER", 5, next(), 100, "AcDbSymbolTableRecord", 100, "AcDbLayerTableRecord", 2, layer.Name, 70, 0, 6, "CONTINUOUS");
            if (source?.Original.Layers.TryGetValue(layer.Name, out var original) == true && original == layer) return r;
            var flags = int.TryParse(Value(r, 70), out var f) ? f : 0; flags &= ~5; if (layer.Locked) flags |= 4;
            r = Set(r, 6, layer.Linetype); r = Set(r, 70, flags); r = Set(r, 62, layer.Visible ? 7 : -7); r = Set(r, 420, layer.Color & 0xFFFFFF); return Set(r, 370, Math.Round(layer.LineWeight * 100));
        }));
        Table("BLOCK_RECORD", names.Select(name =>
        {
            var r = oldBlockRecords.TryGetValue(name, out var old) ? old : Record(0, "BLOCK_RECORD", 5, blockIds[name], 100, "AcDbSymbolTableRecord", 100, "AcDbBlockTableRecord", 2, name, 70, 0, 280, 1, 281, 0);
            var layout = layouts.FirstOrDefault(p => p.Value.Equals(name, StringComparison.OrdinalIgnoreCase)).Key;
            return layout == null ? r : Set(r, 340, layoutIds[layout]);
        }));
        var oldBlocks = new Dictionary<string, (ImmutableArray<DxfPair> Header, ImmutableArray<DxfPair> End)>(StringComparer.OrdinalIgnoreCase);
        string block = ""; ImmutableArray<DxfPair> blockHeader = [];
        foreach (var record in DxfEntityReader.Records(Section("BLOCKS")))
        {
            if (Value(record, 0) == "BLOCK") { block = Value(record, 2); blockHeader = record; }
            else if (Value(record, 0) == "ENDBLK" && block.Length > 0) { oldBlocks[block] = (blockHeader, record); block = ""; }
        }
        var blocks = new StringBuilder();
        foreach (var name in names)
        {
            var definition = drawing.Blocks.GetValueOrDefault(name) ?? new BlockDefinition(name, default, []); var origin = definition.BasePoint; var id = blockIds[name];
            var header = oldBlocks.TryGetValue(name, out var old) ? old.Header : Record(0, "BLOCK", 5, next(), 100, "AcDbEntity", 8, "0", 100, "AcDbBlockBegin", 2, name, 70, 0, 10, 0, 20, 0, 30, 0, 3, name, 1, "");
            header = Set(Set(Set(header, 10, origin.X), 20, origin.Y), 30, origin.Z);
            blocks.Append(Encode(Owner(header, id)));
            foreach (var entity in definition.Entities) blocks.Append(Owned(emit(entity), id));
            var layout = layouts.FirstOrDefault(p => p.Value.Equals(name, StringComparison.OrdinalIgnoreCase)).Key;
            if (layout != null && !layout.Equals("Model", StringComparison.OrdinalIgnoreCase))
                foreach (var entity in drawing.Entities.Where(e => e.Layout.Equals(layout, StringComparison.OrdinalIgnoreCase))) blocks.Append(Owned(emit(entity), id));
            var end = oldBlocks.TryGetValue(name, out old) ? old.End : Record(0, "ENDBLK", 5, next(), 100, "AcDbEntity", 8, "0", 100, "AcDbBlockEnd");
            blocks.Append(Encode(Owner(end, id)));
        }
        var entities = string.Concat(drawing.Entities.Where(e => e.Layout.Equals("Model", StringComparison.OrdinalIgnoreCase)).Select(e => Owned(emit(e), blockIds[layouts["Model"]])));
        // Keep the version marker first: binary readers may determine code width from a bounded header prefix.
        var headerPairs = new List<DxfPair>(Record(9, "$ACADVER", 1, "AC1027")); var skip = false;
        foreach (var pair in Section("HEADER")) { if (pair.Code == 9) skip = pair.Value is "$ACADVER" or "$HANDSEED" or "$INSUNITS" or "$LTSCALE"; if (!skip) headerPairs.Add(pair); }
        headerPairs.AddRange(Record(9, "$INSUNITS", 70, drawing.Units, 9, "$LTSCALE", 40, drawing.LinetypeScale, 9, "$HANDSEED", 5, next()));
        var output = new StringBuilder();
        void Append(string name, string content) => output.Append("0\nSECTION\n2\n").Append(name).Append('\n').Append(content).Append("0\nENDSEC\n");
        Append("HEADER", Encode(headerPairs)); if (!Section("CLASSES").IsEmpty) Append("CLASSES", Encode(Section("CLASSES")));
        Append("TABLES", tables.ToString()); Append("BLOCKS", blocks.ToString()); Append("ENTITIES", entities); Append("OBJECTS", objectText.ToString());
        foreach (var section in sections.Where(s => s.Name is not ("HEADER" or "CLASSES" or "TABLES" or "BLOCKS" or "ENTITIES" or "OBJECTS"))) Append(section.Name, Encode(section.Pairs));
        return output.Append("0\nEOF\n").ToString();
    }
    private static int FindIndex(this IEnumerable<DxfPair> source, Func<DxfPair, bool> predicate)
    {
        var i = 0; foreach (var pair in source) { if (predicate(pair)) return i; i++; } return -1;
    }
}
