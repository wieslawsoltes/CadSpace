using System.Collections.Immutable;
using CadSpace.Model;

namespace CadSpace.Engine;

/// <summary>Direct text-content edits, preserving all geometry, placement, identity and common properties.</summary>
public static class TextEditing
{
    public const int MaximumCharacters = 32768;
    public static TextEntity? TextOf(Entity entity)
    {
        for (var depth = 0; entity is PlacedEntity p; depth++)
        {
            if (depth >= 32) return null;
            entity = p.Geometry;
        }
        return entity as TextEntity;
    }
    public static bool IsEditable(Entity entity) => TextOf(entity) != null || entity is CompositeEntity { DxfType: "INSERT", SourceRecord.Length: > 0 };
    public static void ValidateValue(string value, bool multiline)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.Length > MaximumCharacters || value.Contains('\0') || !multiline && value.IndexOfAny(['\r', '\n']) >= 0)
            throw new ArgumentException("Text must fit 32,768 characters, contain no NUL, and use one line for TEXT.");
    }
    public static void SetText(this CadSession session, Entity expected, string value)
    {
        var text = TextOf(expected) ?? throw new NotSupportedException("Select an editable TEXT or MTEXT object.");
        ValidateValue(value, text.Multiline);
        if (!session.EditableSelection().Any(e => ReferenceEquals(e, expected)))
            throw new InvalidOperationException("The object or selection changed. Reopen the text editor.");
        if (text.Text == value) return;
        Entity Replace(Entity e) => e switch {
            TextEntity t => t with { Text = value },
            PlacedEntity p => p with { Geometry = Replace(p.Geometry) },
            _ => throw new NotSupportedException("The selected object is not text.")
        };
        var next = Replace(expected);
        session.Document.Edit("Edit text", d => d with { Entities = d.Entities.Select(e => e.Id == expected.Id ? next : e).ToImmutableArray() });
    }
}
