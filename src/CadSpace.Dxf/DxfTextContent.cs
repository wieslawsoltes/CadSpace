using System.Collections.Immutable;
using System.Text;

namespace CadSpace.Dxf;

/// <summary>MTEXT content transport. Chunk boundaries never split a UTF-16 surrogate pair.</summary>
internal static class DxfTextContent
{
    public static IEnumerable<DxfPair> Chunks(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var chunk = new StringBuilder(256); var count = 0;
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            if (c == '\0') throw new ArgumentException("DXF text cannot contain NUL.");
            if (c is '\r' or '\n')
            {
                if (c == '\r' && i + 1 < value.Length && value[i + 1] == '\n') i++;
                // A paragraph escape may straddle chunks; readers concatenate before parsing it.
                chunk.Append('\\'); if (++count == 250) { yield return new(3, chunk.ToString()); chunk.Clear(); count = 0; }
                chunk.Append('P');
            }
            else if (char.IsHighSurrogate(c))
            {
                if (i + 1 >= value.Length || !char.IsLowSurrogate(value[i + 1])) throw new ArgumentException("DXF text has an unpaired surrogate.");
                chunk.Append(c).Append(value[++i]);
            }
            else
            {
                if (char.IsLowSurrogate(c)) throw new ArgumentException("DXF text has an unpaired surrogate.");
                chunk.Append(c);
            }
            if (++count == 250) { yield return new(3, chunk.ToString()); chunk.Clear(); count = 0; }
        }
        yield return new(1, chunk.ToString());
    }

    public static bool TryReplace(ImmutableArray<DxfPair> record, string value, out ImmutableArray<DxfPair> replaced)
    {
        replaced = record;
        var semantic = DxfRecordEditing.SemanticPairs(record);
        var marked = semantic.Any(p => p.Code == 100);
        if (marked && semantic.Count(p => p.Code == 100 && p.Value == "AcDbMText") != 1) return false;
        var output = ImmutableArray.CreateBuilder<DxfPair>();
        var active = !marked; var extended = false; var depth = 0; var inserted = false; var terminal = 0;
        foreach (var pair in record)
        {
            if (pair.Code == 102 && !extended)
            {
                if (pair.Value.StartsWith('{')) depth++; else if (pair.Value == "}") depth--;
                output.Add(pair); continue;
            }
            if (depth == 0 && pair.Code == 1001) extended = true;
            if (depth == 0 && !extended && pair.Code == 100) active = pair.Value == "AcDbMText";
            if (depth == 0 && !extended && active && pair.Code is 1 or 3)
            {
                if (pair.Code == 1) terminal++;
                if (!inserted) { output.AddRange(Chunks(value)); inserted = true; }
            }
            else output.Add(pair);
        }
        if (!inserted || terminal != 1) return false; // Never guess between multiple independent content fields.
        replaced = output.ToImmutable(); return true;
    }
}
