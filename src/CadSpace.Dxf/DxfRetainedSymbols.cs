using CadSpace.Model;

namespace CadSpace.Dxf;

internal static class DxfRetainedSymbols
{
    public static IEnumerable<string> ApplicationIds(Drawing drawing)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "ACAD" };
        void Visit(Entity e, int depth)
        {
            if (depth > 32) throw new ArgumentException("Retained symbol nesting exceeds 32 levels.");
            var raw = e is CompositeEntity c ? c.SourceRecord : e is OpaqueEntity o ? o.RawRecord : "";
            if (raw.Length > DxfCodec.MaximumCharacters) throw new ArgumentException("Retained record exceeds the DXF size limit.");
            if (raw.Length > 0)
            {
                var appDepth = 0;
                foreach (var pair in DxfCodec.ParsePairs(raw))
                {
                    if (pair.Code == 102) { if (pair.Value.StartsWith('{')) appDepth++; else if (pair.Value == "}") appDepth--; }
                    if (pair.Code == 1001 && appDepth == 0)
                    {
                        if (string.IsNullOrWhiteSpace(pair.Value) || pair.Value.Length > 255 || pair.Value.Contains('\0')) throw new ArgumentException("Invalid retained APPID name.");
                        names.Add(pair.Value);
                    }
                }
            }
            if (e is PlacedEntity p) Visit(p.Geometry, depth + 1);
            if (e is CompositeEntity compound) foreach (var child in compound.Children) Visit(child, depth + 1);
        }
        foreach (var e in drawing.Entities.Concat(drawing.Blocks.Values.SelectMany(b => b.Entities))) Visit(e, 0);
        return names.Order(StringComparer.OrdinalIgnoreCase);
    }
}
