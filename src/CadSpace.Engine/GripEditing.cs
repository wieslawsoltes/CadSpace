using System.Collections.Immutable;
using CadSpace.Geometry;
using CadSpace.Model;

namespace CadSpace.Engine;

public readonly record struct EntityGrip(int Index, Vec3 Position, string Label);

/// <summary>Immutable grip edits. Placement is inverted in double precision; IDs and DXF provenance identity are retained.</summary>
public static class GripEditing
{
    public static ImmutableArray<EntityGrip> Grips(Entity entity)
    {
        IEnumerable<(Vec3 Position, string Label)> points = entity switch
        {
            LineEntity l => [(l.Start, "Start"), (l.End, "End"), (Vec3.Lerp(l.Start, l.End, .5), "Move")],
            CircleEntity c => new[] { (c.Center, "Center") }.Concat(Enumerable.Range(0, 4).Select(i => (GeometryMath.OnCircle(c.Center, c.Radius, i * 90), "Radius"))),
            ArcEntity a => [(a.Center, "Center")],
            EllipseEntity e => [(e.Center, "Center")],
            PointEntity p => [(p.Position, "Position")],
            TextEntity t => [(t.Position, "Insertion")],
            BlockReferenceEntity b => [(b.Position, "Insertion")],
            PolylineEntity p => p.Vertices.Select(v => (v.Position, "Vertex")),
            Polyline3DEntity p => p.Points.Select(v => (v, "Vertex")),
            SplineEntity s => s.ControlPoints.Select(v => (v, "Control point")),
            DimensionEntity d => DimensionGeometry.Points(d).Select((p, i) => (p, i == 2 ? "Dimension line / leader" : "Definition / text point")),
            PlacedEntity p => Grips(p.Geometry).Select(g => (p.Placement.Point(g.Position), g.Label)),
            _ => []
        };
        return points.Select((p, i) => new EntityGrip(i, p.Position, p.Label)).ToImmutableArray();
    }
    public static Entity Move(Entity entity, int index, Vec3 target)
    {
        if (!target.IsFinite) throw new ArgumentException("Grip position must be finite.");
        var grips = Grips(entity);
        if (index < 0 || index >= grips.Length) throw new ArgumentOutOfRangeException(nameof(index));
        if (target == grips[index].Position) return entity;
        if (entity is PolylineEntity && Math.Abs(target.Z - grips[index].Position.Z) > 1e-8) throw new ArgumentException("An XY polyline vertex grip must stay in its plane.");
        var delta = target - grips[index].Position;
        if (entity is PlacedEntity placed) return placed with { Geometry = Move(placed.Geometry, index, InverseVector(placed.Placement, target - placed.Placement.Origin)) };
        return entity switch
        {
            LineEntity l when index == 0 => l with { Start = target },
            LineEntity l when index == 1 => l with { End = target },
            CircleEntity c when index > 0 => ResizeCircle(c, target),
            PolylineEntity p => p with { Vertices = p.Vertices.SetItem(index, p.Vertices[index] with { Position = target }) },
            Polyline3DEntity p => p with { Points = p.Points.SetItem(index, target) },
            SplineEntity s => s with { ControlPoints = s.ControlPoints.SetItem(index, target) },
            DimensionEntity d => DimensionEditing.MovePoint(d, index, target),
            _ => EntityGeometry.Transform(entity, Transform3.Translation(delta))
        };
    }
    private static CircleEntity ResizeCircle(CircleEntity circle, Vec3 target)
    {
        if (Math.Abs(target.Z - circle.Center.Z) > 1e-8) throw new ArgumentException("A radius grip must stay in the circle plane.");
        var radius = target.DistanceTo(circle.Center);
        if (radius <= 1e-9) throw new ArgumentException("Circle radius must be positive.");
        return circle with { Radius = radius };
    }
    private static Vec3 InverseVector(Transform3 t, Vec3 v)
    {
        var det = t.Determinant;
        if (!double.IsFinite(det) || Math.Abs(det) < 1e-15) throw new ArgumentException("The placement is singular.");
        return new(v.Dot(t.Y.Cross(t.Z)) / det, v.Dot(t.Z.Cross(t.X)) / det, v.Dot(t.X.Cross(t.Y)) / det);
    }
    internal static Entity Stretch(Entity entity, Func<Vec3, bool> inside, Vec3 delta)
    {
        if (entity is PolylineEntity poly && Math.Abs(delta.Z) > 1e-8 && poly.Vertices.Any(v => inside(v.Position)) && !poly.Vertices.All(v => inside(v.Position)))
            throw new ArgumentException("A partial XY polyline stretch must stay in its plane.");
        Vec3 Point(Vec3 p) => inside(p) ? p + delta : p;
        return entity switch
        {
            LineEntity l => l with { Start = Point(l.Start), End = Point(l.End) },
            PolylineEntity p => p with { Vertices = p.Vertices.Select(v => v with { Position = Point(v.Position) }).ToImmutableArray() },
            Polyline3DEntity p => p with { Points = p.Points.Select(Point).ToImmutableArray() },
            SplineEntity s => s with { ControlPoints = s.ControlPoints.Select(Point).ToImmutableArray() },
            DimensionEntity d => DimensionEditing.Stretch(d, inside, delta),
            PointEntity p => p with { Position = Point(p.Position) },
            TextEntity t => t with { Position = Point(t.Position) },
            BlockReferenceEntity b => b with { Position = Point(b.Position) },
            PlacedEntity p => p with { Geometry = Stretch(p.Geometry, v => inside(p.Placement.Point(v)), InverseVector(p.Placement, delta)) },
            _ => throw new NotSupportedException($"Partial STRETCH of {entity.Kind} is not supported. Enclose the whole object to move it.")
        };
    }
}

public sealed partial class CadSession
{
    public Entity PreviewGrip(Entity expected, int index, Vec3 target)
    {
        if (!ReferenceEquals(FindEntity(expected.Id), expected)) throw new InvalidOperationException("The object changed during grip editing. Start again.");
        if (!IsVisible(expected) || Document.Drawing.LayerFor(expected).Locked) throw new InvalidOperationException("The object's layer is hidden or locked.");
        var next = GripEditing.Move(expected, index, target); CadDocument.ValidateEntity(next, Document.Drawing); return next;
    }
    public void MoveGrip(Entity expected, int index, Vec3 target)
    {
        var next = PreviewGrip(expected, index, target); EnsureEntityLookup();
        if (next == expected) return;
        var order = _entityLookup[expected.Id].Order;
        Document.Edit("Grip edit", d => d with { Entities = d.Entities.SetItem(order, next) });
    }
    public Entity[] PreviewStretch(Vec3 first, Vec3 second, Vec3 delta)
    {
        if (!first.IsFinite || !second.IsFinite || !delta.IsFinite) throw new ArgumentException("Stretch corners and displacement must be finite.");
        if (delta == Vec3.Zero) return [];
        var query = SelectionQueries.For(Scene); var window = Bounds3.From([first, second]);
        var whole = query.Window(first, second, false).EntityIds.ToHashSet();
        var changes = new List<Entity>();
        foreach (var id in query.Window(first, second, true).EntityIds)
        {
            var entity = FindEntity(id)!;
            if (Document.Drawing.LayerFor(entity).Locked) throw new InvalidOperationException("Stretch contains an object on a locked layer.");
            if (!whole.Contains(id) && !GripEditing.Grips(entity).Any(g => window.ContainsXY(g.Position))) continue;
            var next = whole.Contains(id) ? EntityGeometry.Transform(entity, Transform3.Translation(delta)) : GripEditing.Stretch(entity, window.ContainsXY, delta);
            CadDocument.ValidateEntity(next, Document.Drawing); if (next != entity) changes.Add(next);
        }
        return changes.ToArray();
    }
    public void Stretch(Vec3 first, Vec3 second, Vec3 delta)
    {
        var changes = PreviewStretch(first, second, delta); if (changes.Length == 0) return;
        EnsureEntityLookup(); var entities = Document.Drawing.Entities.ToBuilder();
        foreach (var e in changes) entities[_entityLookup[e.Id].Order] = e;
        Document.Edit("Stretch", d => d with { Entities = entities.ToImmutable() });
    }
}
