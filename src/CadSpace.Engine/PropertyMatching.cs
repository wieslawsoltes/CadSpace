using System.Collections.Immutable;
using CadSpace.Model;

namespace CadSpace.Engine;

[Flags]
public enum PropertyMatchFields { None = 0, Layer = 1, Color = 2, Linetype = 4, LinetypeScale = 8, LineWeight = 16, All = 31 }

/// <summary>Copies common drafting properties only; never identity, layout, visibility or geometry.</summary>
public static class PropertyMatching
{
    public static void MatchProperties(this CadSession session, Guid sourceId, IEnumerable<Guid> targetIds, PropertyMatchFields fields = PropertyMatchFields.All)
    {
        if ((fields & ~PropertyMatchFields.All) != 0 || fields == PropertyMatchFields.None) throw new ArgumentException("Choose supported properties to match.");
        var drawing = session.Document.Drawing; var byId = drawing.Entities.ToDictionary(e => e.Id);
        if (!byId.TryGetValue(sourceId, out var source) || source is OpaqueEntity || source.Layout != session.ActiveLayout) throw new ArgumentException("Choose a modeled source in the active layout.");
        var targets = targetIds.Distinct().Where(id => id != sourceId).ToArray();
        if (targets.Length is < 1 or > 100000) throw new ArgumentException("Choose 1–100,000 destination objects besides the source.");
        if (fields.HasFlag(PropertyMatchFields.Layer) && drawing.LayerFor(source).Locked) throw new InvalidOperationException("The destination layer is locked.");
        var replacements = new Dictionary<Guid, Entity>();
        foreach (var id in targets)
        {
            if (!byId.TryGetValue(id, out var target) || target.Layout != session.ActiveLayout || target is OpaqueEntity) throw new ArgumentException("A destination is missing, unsupported or outside the active layout.");
            if (drawing.LayerFor(target).Locked) throw new InvalidOperationException("A destination layer is locked.");
            replacements.Add(id, target with {
                Layer = fields.HasFlag(PropertyMatchFields.Layer) ? source.Layer : target.Layer,
                ColorIndex = fields.HasFlag(PropertyMatchFields.Color) ? source.ColorIndex : target.ColorIndex,
                TrueColor = fields.HasFlag(PropertyMatchFields.Color) ? source.TrueColor : target.TrueColor,
                Linetype = fields.HasFlag(PropertyMatchFields.Linetype) ? source.Linetype : target.Linetype,
                LinetypeScale = fields.HasFlag(PropertyMatchFields.LinetypeScale) ? source.LinetypeScale : target.LinetypeScale,
                LineWeight = fields.HasFlag(PropertyMatchFields.LineWeight) ? source.LineWeight : target.LineWeight });
        }
        session.Document.Edit("Match properties", d => d with { Entities = d.Entities.Select(e => replacements.GetValueOrDefault(e.Id) ?? e).ToImmutableArray() });
    }
}
