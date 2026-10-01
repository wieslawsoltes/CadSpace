using System.Collections.Immutable;
using CadSpace.Geometry;
using CadSpace.Model;

namespace CadSpace.Engine;

/// <summary>Atomic dimension edits retain root identity and the existing local plane.</summary>
public static class DimensionEditing
{
    public static void SetDimensionProperties(this CadSession session, Entity expected, DimensionFormat format, string textOverride, Vec3 location, double rotation)
    {
        if (!session.EditableSelection().Any(e => ReferenceEquals(e, expected))) throw new InvalidOperationException("The dimension or selection changed. Reopen the editor.");
        var d = DimensionGeometry.Unwrap(expected) ?? throw new NotSupportedException("Select one modeled dimension.");
        var next = d with { Format = format, TextOverride = textOverride, Location = location, Rotation = rotation,
            TextPosition = d.Type is DimensionKind.Radius or DimensionKind.Diameter && d.TextPosition.HasValue ? location : d.TextPosition };
        DimensionGeometry.Validate(next); if (next == d) return;
        Entity Replace(Entity e) => e is PlacedEntity p ? p with { Geometry = Replace(p.Geometry) } : next;
        var changed = Replace(expected);
        session.Document.Edit("Edit dimension", drawing => drawing with { Entities = drawing.Entities.Select(e => e.Id == expected.Id ? changed : e).ToImmutableArray() });
    }
    public static DimensionEntity MovePoint(DimensionEntity d, int index, Vec3 point)
    {
        var points = DimensionGeometry.Points(d).ToArray();
        if (index < 0 || index >= points.Length) throw new ArgumentOutOfRangeException(nameof(index));
        if (!point.IsFinite || Math.Abs(point.Z - d.First.Z) > 1e-7) throw new ArgumentException("A dimension grip must stay in its local plane.");
        return index switch {
            0 => d with { First = point }, 1 => d with { Second = point },
            2 => d with { Location = point, TextPosition = d.Type is DimensionKind.Radius or DimensionKind.Diameter && d.TextPosition.HasValue ? point : d.TextPosition },
            3 when d.Type is DimensionKind.Angular2Line or DimensionKind.Angular3Point => d with { Third = point },
            4 when d.Type == DimensionKind.Angular2Line => d with { Fourth = point },
            _ when d.TextPosition.HasValue => d with { TextPosition = point },
            _ => throw new ArgumentOutOfRangeException(nameof(index)) };
    }
    internal static DimensionEntity Stretch(DimensionEntity d, Func<Vec3, bool> inside, Vec3 delta)
    {
        Vec3 Point(Vec3 p) => inside(p) ? p + delta : p;
        var result = d with { First = Point(d.First), Second = Point(d.Second), Location = Point(d.Location),
            Third = d.Type is DimensionKind.Angular2Line or DimensionKind.Angular3Point ? Point(d.Third) : d.Third,
            Fourth = d.Type == DimensionKind.Angular2Line ? Point(d.Fourth) : d.Fourth,
            TextPosition = d.TextPosition is { } t ? Point(t) : null };
        DimensionGeometry.Validate(result); return result;
    }
}
