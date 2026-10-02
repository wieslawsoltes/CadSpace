using System.Collections.Immutable;
using CadSpace.Geometry;
using CadSpace.Model;

namespace CadSpace.Engine;

/// <summary>Staged leader edits with capture/lock checks and one document transaction.</summary>
public static class LeaderEditing
{
    public static void SetLeader(this CadSession session, Entity expected, ImmutableArray<Vec3> vertices, bool arrow, double size)
    {
        if (!session.EditableSelection().Any(e => ReferenceEquals(e, expected))) throw new InvalidOperationException("The leader or selection changed. Reopen the editor.");
        if (vertices.IsDefault) throw new ArgumentException("Leader vertices cannot be default.");
        var old = LeaderGeometry.Unwrap(expected) ?? throw new ArgumentException("Select one straight leader.");
        var next = old with { Vertices = vertices.SequenceEqual(old.Vertices) ? old.Vertices : vertices, ArrowEnabled = arrow, ArrowSize = size };
        LeaderGeometry.Validate(next); if (next == old) return;
        Entity Replace(Entity e) => e is PlacedEntity p ? p with { Geometry = Replace(p.Geometry) } : next;
        var replacement = Replace(expected);
        session.Document.Edit("Edit leader", d => d with { Entities = d.Entities.Select(e => e.Id == expected.Id ? replacement : e).ToImmutableArray() });
    }
}
