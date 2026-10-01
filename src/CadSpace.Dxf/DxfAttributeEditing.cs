using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using CadSpace.Model;

namespace CadSpace.Dxf;

public sealed record DxfAttributeValue(int Index, string Tag, string Value, bool Invisible, bool Editable);

/// <summary>Value-only edits of source-backed INSERT/ATTRIB sequences. Tags, geometry, handles and application data stay unchanged.</summary>
public static class DxfAttributeEditing
{
    public const int MaximumAttributes = 1024;
    public const int MaximumValueCharacters = 2048;

    public static ImmutableArray<DxfAttributeValue> Read(CompositeEntity insert)
    {
        var records = ValidatedRecords(insert);
        return Describe(records);
    }

    /// <summary>Return one edited drawing snapshot; the host wraps it in a document transaction.</summary>
    public static Drawing Apply(Drawing drawing, CompositeEntity expected, IReadOnlyDictionary<int, string> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (!drawing.Entities.Any(e => ReferenceEquals(e, expected)))
            throw new InvalidOperationException("The attributed block changed. Reopen the editor.");
        if (drawing.LayerFor(expected).Locked) throw new InvalidOperationException("The block layer is locked.");
        var records = ValidatedRecords(expected);
        var descriptions = Describe(records);
        var changed = false;
        foreach (var (index, value) in values)
        {
            ValidateValue(value);
            if (index < 0 || index >= descriptions.Length || !descriptions[index].Editable)
                throw new NotSupportedException("The selected attribute is constant, field-backed or has unsupported multiline content.");
            var number = AttributeRecordIndex(records, index);
            var fields = DxfRecordEditing.SemanticPairs(records[number]);
            var layer = S(fields, 8, "0");
            if (layer == "0") layer = expected.Layer;
            if (drawing.Layers.TryGetValue(layer, out var attributeLayer) && attributeLayer.Locked)
                throw new InvalidOperationException("An attribute layer is locked.");
            if (descriptions[index].Value == value) continue;
            records[number] = ReplaceValue(records[number], value);
            changed = true;
        }
        if (!changed) return drawing;
        var raw = Encode(records.SelectMany(r => r));
        var replacement = Reinterpret(raw, expected);
        // The parser reconstructs the original geometry; only the validated attribute values differ.
        var next = drawing with { Entities = drawing.Entities.Select(e => e.Id == expected.Id ? replacement : e).ToImmutableArray() };
        CadDocument.Validate(next);
        return next;
    }

    public static void ValidateValue(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.Length > MaximumValueCharacters || value.IndexOfAny(['\0', '\r', '\n']) >= 0 || value.Contains("%<", StringComparison.Ordinal))
            throw new ArgumentException("An attribute value must be a single line of at most 2,048 characters, without NUL or field expressions.");
    }

    private static ImmutableArray<DxfPair>[] ValidatedRecords(CompositeEntity insert)
    {
        if (insert.DxfType != "INSERT" || insert.SourceRecord.Length == 0 || insert.SourceRecord.Length > DxfCodec.MaximumCharacters)
            throw new NotSupportedException("This block has no retained editable INSERT/ATTRIB sequence.");
        var pairs = DxfCodec.ParsePairs(insert.SourceRecord);
        var records = DxfEntityReader.Records(pairs).ToArray();
        if (records.Length < 3 || records.Length > MaximumAttributes + 2 || S(records[0], 0) != "INSERT" || S(records[^1], 0) != "SEQEND" ||
            records.Skip(1).SkipLast(1).Any(r => S(r, 0) != "ATTRIB"))
            throw new NotSupportedException("Expected a bounded INSERT, ATTRIB and SEQEND sequence.");
        // Never reuse a stale retained sequence after block movement, scaling, child replacement or private geometry edits.
        var baseline = Reinterpret(insert.SourceRecord, insert);
        if (!SameGraph(baseline, insert))
            throw new NotSupportedException("The block display no longer matches its retained DXF sequence. Export and reimport before editing attributes.");
        return records;
    }

    private static CompositeEntity Reinterpret(string raw, CompositeEntity identity)
    {
        var read = DxfEntityReader.Read(DxfRecordEditing.SemanticPairs(DxfCodec.ParsePairs(raw)), _ => { });
        if (read is not CompositeEntity { DxfType: "INSERT" } parsed)
            throw new NotSupportedException("This attribute sequence cannot be edited with the current interpreter.");
        parsed = (CompositeEntity)CopyRootStyle(parsed, identity);
        parsed = parsed with { SourceRecord = raw };
        return (CompositeEntity)ReuseIdentifiers(parsed, identity);
    }

    private static Entity ReuseIdentifiers(Entity parsed, Entity identity)
    {
        parsed = parsed with { Id = identity.Id };
        if (parsed is PlacedEntity p && identity is PlacedEntity old)
            return p with { Geometry = ReuseIdentifiers(p.Geometry, old.Geometry) };
        if (parsed is CompositeEntity c && identity is CompositeEntity before)
        {
            if (c.Children.Length != before.Children.Length) throw new NotSupportedException("Attribute changes cannot replace block display topology.");
            return c with { Children = c.Children.Zip(before.Children).Select(pair => ReuseIdentifiers(pair.First, pair.Second)).ToImmutableArray() };
        }
        return parsed;
    }

    private static bool SameGraph(Entity a, Entity b) => (a, b) switch {
        (PlacedEntity x, PlacedEntity y) => SameGraph(x.Geometry, y.Geometry) && x with { Geometry = y.Geometry } == y,
        (CompositeEntity x, CompositeEntity y) => x.Children.Length == y.Children.Length && x.Children.Zip(y.Children).All(p => SameGraph(p.First, p.Second)) && x with { Children = y.Children } == y,
        _ => a == b
    };

    private static Entity CopyRootStyle(Entity target, Entity source) => target with {
        Id = source.Id, Handle = source.Handle, Layer = source.Layer, ColorIndex = source.ColorIndex, TrueColor = source.TrueColor,
        LineWeight = source.LineWeight, Linetype = source.Linetype, LinetypeScale = source.LinetypeScale, Visible = source.Visible, Layout = source.Layout
    };

    private static ImmutableArray<DxfAttributeValue> Describe(ImmutableArray<DxfPair>[] records)
    {
        var result = ImmutableArray.CreateBuilder<DxfAttributeValue>();
        foreach (var record in records.Skip(1).SkipLast(1))
        {
            var fields = DxfRecordEditing.SemanticPairs(record); var flags = I(fields, 70);
            var text = S(fields, 1);
            var editable = (flags & 2) == 0 && !text.Contains("%<", StringComparison.Ordinal) &&
                !fields.Any(p => p.Code == 101 || p.Code == 100 && p.Value == "AcDbXrecord");
            result.Add(new(result.Count, S(fields, 2), text, (flags & 1) != 0, editable));
        }
        return result.ToImmutable();
    }

    private static int AttributeRecordIndex(ImmutableArray<DxfPair>[] records, int index) => index + 1;

    // ATTRIB value is group 1 in AcDbText (or a marker-free legacy record), never an application-group payload.
    private static ImmutableArray<DxfPair> ReplaceValue(ImmutableArray<DxfPair> record, string value)
    {
        var result = record.ToBuilder(); var depth = 0; var subclass = ""; var found = -1;
        var markers = DxfRecordEditing.SemanticPairs(record).Any(p => p.Code == 100);
        for (var i = 0; i < result.Count; i++)
        {
            var p = result[i];
            if (p.Code == 102) { if (p.Value.StartsWith('{')) depth++; else if (p.Value == "}") depth--; continue; }
            if (depth != 0) continue;
            if (p.Code == 1001) break;
            if (p.Code == 100) subclass = p.Value;
            if (p.Code == 1 && (!markers || subclass == "AcDbText"))
            {
                if (found >= 0) throw new NotSupportedException("Duplicate attribute values are ambiguous.");
                found = i;
            }
        }
        if (found < 0) throw new NotSupportedException("The attribute has no editable text value.");
        result[found] = new(1, value); return result.ToImmutable();
    }

    /// <summary>Reuse a complete native attribute sequence even when the original whole-file source is absent.</summary>
    internal static bool TryWriteRetained(CompositeEntity insert, Action<string> warn, out string text)
    {
        text = "";
        if (insert.DxfType != "INSERT" || insert.SourceRecord.Length == 0) return false;
        try
        {
            var records = ValidatedRecords(insert);
            var raw = records.SelectMany(r => r).ToImmutableArray();
            // A copied/renamed root must not reintroduce the former root handle.
            if (!S(DxfRecordEditing.SemanticPairs(records[0]), 5).Equals(insert.Handle, StringComparison.OrdinalIgnoreCase)) return false;
            var original = DxfEntityReader.Read(DxfRecordEditing.SemanticPairs(raw), _ => { });
            var baseline = (CompositeEntity)CopyRootStyle(insert, original);
            baseline = baseline with { Id = insert.Id, Handle = insert.Handle };
            if (!DxfRecordEditing.TryWrite(insert, baseline, raw, warn, out text)) return false;
            warn("Retained INSERT/ATTRIB sequence: application payloads and external references are preserved, not remapped or regenerated.");
            return true;
        }
        catch (Exception e) when (e is ArgumentException or FormatException or NotSupportedException or OverflowException)
        { return false; }
    }

    // Called before the generic source-record patcher. All differences must be supported value edits, not arbitrary SourceRecord changes.
    internal static bool TryWrite(Entity after, Entity before, ImmutableArray<DxfPair> source, Action<string> warn, out string text)
    {
        text = "";
        if (after is not CompositeEntity a || before is not CompositeEntity b || a.DxfType != "INSERT" || b.DxfType != "INSERT" ||
            a.Id != b.Id || a.Handle != b.Handle || a.SourceRecord == b.SourceRecord) return false;
        try
        {
            var oldRecords = ValidatedRecords(b); var newRecords = ValidatedRecords(a);
            if (!DxfCodec.ParsePairs(b.SourceRecord).SequenceEqual(source) || oldRecords.Length != newRecords.Length) return false;
            var oldValues = Describe(oldRecords); var newValues = Describe(newRecords);
            for (var i = 0; i < oldValues.Length; i++)
            {
                if (oldValues[i].Value == newValues[i].Value) continue;
                if (!oldValues[i].Editable) return false;
                ValidateValue(newValues[i].Value);
                oldRecords[i + 1] = ReplaceValue(oldRecords[i + 1], newValues[i].Value);
            }
            if (!oldRecords.SelectMany(r => r).SequenceEqual(newRecords.SelectMany(r => r))) return false;
            // Restore only the geometry/source part for the common-property patcher's comparison.
            var styleChange = a with { Children = b.Children, SourceRecord = b.SourceRecord };
            if (!DxfRecordEditing.TryWrite(styleChange, b, newRecords.SelectMany(r => r).ToImmutableArray(), warn, out text)) return false;
            warn("Edited INSERT attribute values retain their native ATTRIB sequence; external fields and application associations are not regenerated.");
            return true;
        }
        catch (Exception e) when (e is ArgumentException or FormatException or NotSupportedException or OverflowException)
        { return false; }
    }
    private static string S(ImmutableArray<DxfPair> pairs, int code, string fallback = "") => pairs.FirstOrDefault(p => p.Code == code).Value ?? fallback;
    private static int I(ImmutableArray<DxfPair> pairs, int code) => int.Parse(S(pairs, code, "0"), CultureInfo.InvariantCulture);
    private static string Encode(IEnumerable<DxfPair> pairs)
    {
        var b = new StringBuilder();
        foreach (var p in pairs) b.Append(p.Code.ToString(CultureInfo.InvariantCulture)).Append('\n').Append(p.Value).Append('\n');
        return b.ToString();
    }
}
