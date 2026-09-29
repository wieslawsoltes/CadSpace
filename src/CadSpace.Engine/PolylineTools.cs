using System.Collections.Immutable;
using CadSpace.Geometry;
using CadSpace.Model;

namespace CadSpace.Engine;

/// <summary>Analytic polyline construction and topology edits. Curved joins never sample arcs into line chords.</summary>
public static class PolylineTools
{
    public const int MaximumJoinSegments = 100000;
    public static PolylineEntity Polygon(int sides, Vec3 center, double radius, bool circumscribed = false)
    {
        if (sides is < 3 or > 1024 || !center.IsFinite || !double.IsFinite(radius) || radius <= 0)
            throw new ArgumentException("Use 3–1024 sides, a finite center and a positive radius.");
        var r = circumscribed ? radius / Math.Cos(Math.PI / sides) : radius;
        var angle = circumscribed ? Math.PI / sides : 0;
        return PolylineEntity.FromPoints(Enumerable.Range(0, sides).Select(i => center + new Vec3(Math.Cos(angle + i * 2 * Math.PI / sides), Math.Sin(angle + i * 2 * Math.PI / sides)) * r), true);
    }
    public static PolylineEntity Donut(Vec3 center, double innerDiameter, double outerDiameter)
    {
        if (!center.IsFinite || !double.IsFinite(innerDiameter) || !double.IsFinite(outerDiameter) || innerDiameter < 0 || outerDiameter <= innerDiameter)
            throw new ArgumentException("The outer diameter must exceed the nonnegative inner diameter.");
        var radius = innerDiameter / 4 + outerDiameter / 4;
        return new([new(center - Vec3.UnitX * radius, 1), new(center + Vec3.UnitX * radius, 1)], true)
            { ConstantWidth = (outerDiameter - innerDiameter) / 2 };
    }
    public static PolylineEntity SplitSegment(PolylineEntity polyline, int index, double fraction = .5)
    {
        var count = polyline.Vertices.Length;
        if (index < 0 || index >= count - (polyline.Closed ? 0 : 1)) throw new ArgumentOutOfRangeException(nameof(index), "Choose an outgoing segment.");
        if (!double.IsFinite(fraction) || fraction <= 0 || fraction >= 1 || count >= MaximumJoinSegments)
            throw new ArgumentException("Split fraction must be between zero and one; at most 100,000 vertices are editable.");
        var a = polyline.Vertices[index]; var b = polyline.Vertices[(index + 1) % count];
        var delta = b.Position - a.Position; var point = a.Position + delta * fraction;
        var leftBulge = 0d; var rightBulge = 0d;
        if (Math.Abs(a.Bulge) >= 1e-12)
        {
            var normal = new Vec3(-delta.Y, delta.X);
            var center = a.Position + delta / 2 + normal * ((1 - a.Bulge * a.Bulge) / (4 * a.Bulge));
            var sweep = 4 * Math.Atan(a.Bulge);
            point = Transform3.RotationZ(GeometryMath.Degrees(sweep * fraction), center).Point(a.Position);
            leftBulge = Math.Tan(sweep * fraction / 4); rightBulge = Math.Tan(sweep * (1 - fraction) / 4);
        }
        if (!point.IsFinite || point.DistanceTo(a.Position) <= 1e-9 || point.DistanceTo(b.Position) <= 1e-9)
            throw new ArgumentException("Splitting would create a degenerate segment.");
        var width = a.StartWidth + (a.EndWidth - a.StartWidth) * fraction;
        var next = polyline.Vertices.SetItem(index, a with { Bulge = leftBulge, EndWidth = width })
            .Insert(index + 1, new(point, rightBulge) { StartWidth = width, EndWidth = a.EndWidth });
        return polyline with { Vertices = next };
    }
    /// <summary>Deletes a vertex; the new connecting edge is explicitly straight, not an invented replacement arc.</summary>
    public static PolylineEntity DeleteVertex(PolylineEntity polyline, int index)
    {
        if (index < 0 || index >= polyline.Vertices.Length) throw new ArgumentOutOfRangeException(nameof(index));
        if (polyline.Vertices.Length <= (polyline.Closed ? 3 : 2)) throw new ArgumentException("Retain at least three closed or two open vertices.");
        var next = polyline.Vertices;
        if (polyline.Closed || index > 0 && index < next.Length - 1)
        {
            var previous = (index + next.Length - 1) % next.Length;
            next = next.SetItem(previous, next[previous] with { Bulge = 0, EndWidth = next[index].EndWidth });
        }
        return polyline with { Vertices = next.RemoveAt(index) };
    }
    public static (Entity Geometry, Transform3 Placement) Unwrap(Entity root)
    {
        var placement = Transform3.Identity;
        for (var depth = 0; root is PlacedEntity p; depth++)
        {
            if (depth >= 32) throw new ArgumentException("Excessive placement nesting.");
            placement = p.Placement.Then(placement); root = p.Geometry;
        }
        return (root, placement);
    }
    public static void EditVertex(this CadSession session, Entity expected, Func<PolylineEntity, PolylineEntity> edit)
    {
        ArgumentNullException.ThrowIfNull(edit);
        if (!session.EditableSelection().Any(e => ReferenceEquals(e, expected))) throw new InvalidOperationException("The selection changed; reselect before editing.");
        Entity Apply(Entity e) => e switch {
            PolylineEntity p => edit(p), PlacedEntity p => p with { Geometry = Apply(p.Geometry) },
            _ => throw new ArgumentException("Select a 2D polyline, including an OCS placement.")
        };
        var replacement = Apply(expected);
        session.Document.Edit("Edit polyline vertex", d => d with { Entities = d.Entities.Select(e => e.Id == expected.Id ? replacement : e).ToImmutableArray() });
    }
    private sealed class Node(Vec3 position) { public Vec3 Position { get; } = position; public List<int> Edges { get; } = []; }
    private sealed record Piece(PolylineEntity Curve, int Start, int End);
    /// <summary>One nonbranching coplanar chain, with endpoint welding within tolerance; first object supplies root properties.</summary>
    public static Entity Join(IReadOnlyList<Entity> entities, double tolerance = 1e-7)
    {
        if (entities.Count is < 2 or > 10000 || !double.IsFinite(tolerance) || tolerance <= 0 || tolerance > 1)
            throw new ArgumentException("Select 2–10,000 connected lines, arcs or open 2D polylines; tolerance must be in (0,1].");
        var first = entities[0]; var frame = Unwrap(first).Placement;
        var nodes = new List<Node>(); var pieces = new List<Piece>();
        var buckets = new Dictionary<(long, long), List<int>>();
        Vec3 origin = default; var haveOrigin = false; var segments = 0;
        int NodeFor(Vec3 point)
        {
            if (!haveOrigin) { origin = point; haveOrigin = true; }
            if (!point.IsFinite || Math.Abs(point.Z - origin.Z) > tolerance) throw new ArgumentException("Join requires coplanar XY geometry in one shared placement.");
            var x = (point.X - origin.X) / tolerance; var y = (point.Y - origin.Y) / tolerance;
            if (!double.IsFinite(x) || !double.IsFinite(y) || Math.Abs(x) > 4e15 || Math.Abs(y) > 4e15) throw new ArgumentException("Coordinates exceed the join tolerance's numeric range.");
            var cx = (long)Math.Floor(x); var cy = (long)Math.Floor(y); var found = -1;
            for (var dx = -1; dx <= 1; dx++) for (var dy = -1; dy <= 1; dy++)
                if (buckets.TryGetValue((cx + dx, cy + dy), out var bucket)) foreach (var id in bucket)
                    if (nodes[id].Position.DistanceTo(point) <= tolerance)
                    { if (found >= 0 && found != id) throw new ArgumentException("Ambiguous endpoints within the join tolerance."); found = id; }
            if (found >= 0) return found;
            var next = nodes.Count; nodes.Add(new(point));
            if (!buckets.TryGetValue((cx, cy), out var cell)) buckets.Add((cx, cy), cell = []);
            cell.Add(next); return next;
        }
        foreach (var root in entities)
        {
            var (geometry, placement) = Unwrap(root);
            if (placement != frame || root.Layout != first.Layout) throw new ArgumentException("Joined objects must share a layout and OCS/affine placement.");
            PolylineEntity curve = geometry switch {
                LineEntity l => PolylineEntity.FromPoints([l.Start, l.End]),
                ArcEntity a when GeometryMath.NormalizeAngle(a.EndAngle - a.StartAngle) > 1e-9 => new([
                    new(GeometryMath.OnCircle(a.Center, a.Radius, a.StartAngle), Math.Tan(GeometryMath.Radians(GeometryMath.NormalizeAngle(a.EndAngle - a.StartAngle)) / 4)),
                    new(GeometryMath.OnCircle(a.Center, a.Radius, a.EndAngle))]),
                PolylineEntity { Closed: false } p => p,
                _ => throw new ArgumentException("JOIN accepts lines, nonzero arcs and open 2D polylines, not closed curves or meshes.")
            };
            segments += curve.Vertices.Length - 1;
            if (segments > MaximumJoinSegments) throw new ArgumentException("Joined geometry exceeds 100,000 segments.");
            // Explicit widths allow adjoining pieces with different global widths to retain their geometry.
            if (curve.ConstantWidth > 0) curve = curve with { ConstantWidth = 0, Vertices = curve.Vertices.Select(v => v with { StartWidth = curve.ConstantWidth, EndWidth = curve.ConstantWidth }).ToImmutableArray() };
            foreach (var vertex in curve.Vertices)
                if (!vertex.Position.IsFinite || Math.Abs(vertex.Position.Z - curve.Vertices[0].Position.Z) > tolerance) throw new ArgumentException("Nonplanar join geometry.");
            var start = NodeFor(curve.Vertices[0].Position); var end = NodeFor(curve.Vertices[^1].Position);
            if (start == end) throw new ArgumentException("An input curve closes within the join tolerance.");
            var index = pieces.Count; pieces.Add(new(curve, start, end)); nodes[start].Edges.Add(index); nodes[end].Edges.Add(index);
            if (nodes[start].Edges.Count > 2 || nodes[end].Edges.Count > 2) throw new ArgumentException("Branching chains cannot form a single polyline.");
        }
        var ends = Enumerable.Range(0, nodes.Count).Where(i => nodes[i].Edges.Count == 1).ToArray();
        if (ends.Length != 0 && ends.Length != 2) throw new ArgumentException("Selected curves do not form one connected chain.");
        var closed = ends.Length == 0; var node = closed ? pieces[0].Start : ends[0]; var used = new bool[pieces.Count];
        var vertices = ImmutableArray.CreateBuilder<PolyVertex>(segments + (closed ? 0 : 1));
        PolyVertex last = default;
        for (var step = 0; step < pieces.Count; step++)
        {
            var edge = nodes[node].Edges.FirstOrDefault(i => !used[i], -1);
            if (edge < 0) throw new ArgumentException("Selected curves contain disconnected chains.");
            used[edge] = true; var piece = pieces[edge]; var forward = piece.Start == node;
            var curve = forward ? piece.Curve : PolylineEditing.Reverse(piece.Curve);
            for (var j = 0; j < curve.Vertices.Length - 1; j++) vertices.Add(j == 0 ? curve.Vertices[j] with { Position = nodes[node].Position } : curve.Vertices[j]);
            node = forward ? piece.End : piece.Start; last = curve.Vertices[^1] with { Position = nodes[node].Position };
        }
        if (!closed) vertices.Add(last);
        var poly = new PolylineEntity(vertices.ToImmutable(), closed) { ContinuousLinetype = Unwrap(first).Geometry is PolylineEntity { ContinuousLinetype: true } };
        Entity result = frame == Transform3.Identity ? poly : new PlacedEntity(poly, frame);
        return result with { Id = first.Id, Handle = first.Handle, Layer = first.Layer, ColorIndex = first.ColorIndex, TrueColor = first.TrueColor,
            LineWeight = first.LineWeight, Linetype = first.Linetype, LinetypeScale = first.LinetypeScale, Visible = first.Visible, Layout = first.Layout };
    }
    public static void JoinCurves(this CadSession session)
    {
        var selected = session.EditableSelection(); var joined = Join(selected); var ids = selected.Select(e => e.Id).ToHashSet();
        session.Document.Edit("Join curves", d => d with { Entities = d.Entities.Where(e => e.Id == joined.Id || !ids.Contains(e.Id)).Select(e => e.Id == joined.Id ? joined : e).ToImmutableArray() });
        session.Select(joined.Id);
    }
}
