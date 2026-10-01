using System.Collections.Immutable;
using CadSpace.Geometry;

namespace CadSpace.Model;

public sealed record ScenePath(Guid EntityId, string Layer, uint Color, ImmutableArray<Vec3> Points, bool Closed, bool Filled = false, double Weight = 0.25)
{
    public StrokePattern? Pattern { get; init; }
    public ImmutableArray<uint> VertexColors { get; init; } = [];
}
public sealed record SceneText(Guid EntityId, string Layer, uint Color, Vec3 Position, string Text, double Height, double Rotation)
{
    public Vec3 AxisX { get; init; } = Vec3.UnitX;
    public Vec3 AxisY { get; init; } = Vec3.UnitY;
}
public sealed record SceneTriangle(Guid EntityId, uint Color, Vec3 A, Vec3 B, Vec3 C)
{
    public uint? ColorB { get; init; }
    public uint? ColorC { get; init; }
}
public sealed record DrawingScene(ImmutableArray<ScenePath> Paths, ImmutableArray<SceneText> Texts, ImmutableArray<SceneTriangle> Triangles)
{
    public Bounds3 Bounds => SceneBounds.For(this);
}

public static class EntityGeometry
{
    public static IEnumerable<Vec3> Anchors(Entity e) => e switch
    {
        LineEntity l => [l.Start, l.End], PointEntity p => [p.Position], CircleEntity c => [c.Center],
        ArcEntity a => [a.Center], EllipseEntity l => [l.Center, l.MajorAxis],
        PolylineEntity p => p.Vertices.Select(v => v.Position), TextEntity t => [t.Position],
        DimensionEntity d => DimensionGeometry.Points(d), HatchEntity h => h.Boundary,
        MeshEntity m => m.Vertices, BlockReferenceEntity b => [b.Position], _ => AdvancedGeometry.Anchors(e)
    };
    public static uint AciColor(int index)
    {
        if (index < 10) return index switch { 1 => 0xFFFF0000, 2 => 0xFFFFFF00, 3 => 0xFF00FF00, 4 => 0xFF00FFFF, 5 => 0xFF0000FF, 6 => 0xFFFF00FF, 7 => 0xFFE5E9EF, 8 => 0xFF808080, 9 => 0xFFC0C0C0, _ => 0xFFD8DFE8 };
        if (index >= 250) { var grey = new byte[] { 51, 80, 105, 130, 190, 255 }[Math.Clamp(index - 250, 0, 5)]; return 0xFF000000u | (uint)(grey << 16 | grey << 8 | grey); }
        var hue = (index - 10) / 10 * 15.0; var shade = (index - 10) % 10;
        var value = new[] { 1.0, 1.0, .8, .8, .6, .6, .5, .5, .3, .3 }[shade]; var saturation = shade % 2 == 0 ? 1 : .5;
        var chroma = value * saturation; var x = chroma * (1 - Math.Abs(hue / 60 % 2 - 1)); var m = value - chroma;
        var rgb = hue switch { < 60 => (chroma, x, 0.0), < 120 => (x, chroma, 0.0), < 180 => (0.0, chroma, x), < 240 => (0.0, x, chroma), < 300 => (x, 0.0, chroma), _ => (chroma, 0.0, x) };
        return 0xFF000000u | (uint)((int)Math.Round((rgb.Item1 + m) * 255) << 16 | (int)Math.Round((rgb.Item2 + m) * 255) << 8 | (int)Math.Round((rgb.Item3 + m) * 255));
    }
    public static ImmutableArray<Vec3> Curve(Vec3 center, double radius, double start, double sweep, int segments = 192)
    {
        var count = Math.Clamp((int)Math.Ceiling(Math.Abs(sweep) / 360 * segments), 2, 8192);
        return Enumerable.Range(0, count + 1).Select(i => GeometryMath.OnCircle(center, radius, start + sweep * i / count)).ToImmutableArray();
    }
    public static ImmutableArray<Vec3> PolylinePoints(PolylineEntity polyline)
    {
        var result = ImmutableArray.CreateBuilder<Vec3>();
        for (var i = 0; i < polyline.Vertices.Length; i++)
        {
            var a = polyline.Vertices[i]; result.Add(a.Position);
            if (i == polyline.Vertices.Length - 1 && !polyline.Closed) break;
            var b = polyline.Vertices[(i + 1) % polyline.Vertices.Length].Position;
            if (Math.Abs(a.Bulge) < 1e-12 || a.Position.DistanceTo(b) < 1e-12) continue;
            var chord = b - a.Position; var length = chord.Length;
            var normal = new Vec3(-chord.Y, chord.X) / length;
            var center = (a.Position + b) / 2 + normal * (length * (1 - a.Bulge * a.Bulge) / (4 * a.Bulge));
            var points = Curve(center, center.DistanceTo(a.Position), GeometryMath.Angle(a.Position - center), GeometryMath.Degrees(4 * Math.Atan(a.Bulge)));
            for (var j = 1; j < points.Length - 1; j++) result.Add(points[j]);
            if (result.Count > 1000000) throw new ArgumentException("Polyline tessellation exceeds one million points.");
        }
        return result.ToImmutable();
    }
    public static DrawingScene BuildScene(Drawing drawing, string layout = "Model")
    {
        var paths = ImmutableArray.CreateBuilder<ScenePath>(); var texts = ImmutableArray.CreateBuilder<SceneText>(); var triangles = ImmutableArray.CreateBuilder<SceneTriangle>();
        var nodes = 0; var vertices = 0;
        void Add(Entity e, Transform3 transform, Guid root, string? inheritedLayer, uint? inheritedColor, int depth, string? inheritedLinetype = null)
        {
            if (depth > 32 || ++nodes > 200000) throw new ArgumentException("Expanded scene exceeds the nesting/entity budget.");
            var layerName = e.Layer == "0" && inheritedLayer != null ? inheritedLayer : e.Layer;
            var layer = drawing.Layers.TryGetValue(layerName, out var l) ? l : drawing.Layers["0"];
            if (!layer.Visible || !e.Visible) return;
            var color = e.TrueColor ?? (e.ColorIndex == 0 ? inheritedColor ?? layer.Color : e.ColorIndex == 256 ? layer.Color : AciColor(e.ColorIndex));
            var weight = e.LineWeight < 0 ? layer.LineWeight : e.LineWeight;
            var lineName = Linetype.ResolveName(e.Linetype, layer, inheritedLinetype);
            StrokePattern? pattern = null;
            if (drawing.Linetypes.TryGetValue(lineName, out var definition) && !definition.IsComplex && !definition.Elements.IsEmpty)
            {
                var scale = drawing.LinetypeScale * e.LinetypeScale;
                if (!double.IsFinite(scale * definition.Length) || scale * definition.Length < 1e-9 || scale * definition.Length > 1e15) throw new ArgumentException("Resolved linetype scale exceeds the rendering range.");
                pattern = new(definition, scale);
            }
            void Path(IEnumerable<Vec3> points, bool closed = false, bool fill = false)
            {
                var p = points.Select(transform.Point).ToImmutableArray(); vertices += p.Length;
                if (vertices > 2000000) throw new ArgumentException("Expanded scene exceeds two million vertices.");
                paths.Add(new(root, layerName, color, p, closed, fill, weight) { Pattern = fill ? null : pattern });
            }
            void Text(Vec3 p, string value, double height, double rotation = 0)
            {
                var orientation = Transform3.RotationZ(rotation).Then(transform);
                var yScale = orientation.Y.Length; var h = height * yScale;
                texts.Add(new(root, layerName, color, transform.Point(p), value, h, GeometryMath.Angle(orientation.X)) { AxisX = orientation.X / Math.Max(1e-15, yScale), AxisY = orientation.Y / Math.Max(1e-15, yScale) });
            }
            void Child(Entity child, Transform3 placement, bool forceStyle = true) => Add(forceStyle ? child with { Layer = layerName, TrueColor = color, ColorIndex = 256, LineWeight = weight, Linetype = lineName, LinetypeScale = e.LinetypeScale } : child, placement, root, layerName, color, depth + 1, lineName);
            switch (e)
            {
                case PlacedEntity placed: Child(placed.Geometry, placed.Placement.Then(transform)); break;
                case CompositeEntity composite: foreach (var child in composite.Children) Child(child, transform, false); break;
                case HatchRegionEntity { Gradient: not null } gradient:
                    foreach (var face in HatchGradientGeometry.For(gradient))
                    {
                        var a = transform.Point(face.A); var b = transform.Point(face.B); var c = transform.Point(face.C);
                        if (triangles.Count >= 1000000 || (vertices += 3) > 2000000) throw new ArgumentException("Expanded gradient exceeds the scene budget.");
                        triangles.Add(new(root, face.ColorA, a, b, c) { ColorB = face.ColorB, ColorC = face.ColorC });
                        paths.Add(new(root, layerName, face.ColorA, [a,b,c], true, true) { VertexColors = [face.ColorA, face.ColorB, face.ColorC] });
                    }
                    break;
                case SplineEntity or Polyline3DEntity or HatchRegionEntity:
                    if (e is HatchRegionEntity) { pattern = null; lineName = "CONTINUOUS"; }
                    foreach (var child in AdvancedGeometry.Expand(e)) Child(child, transform); break;
                case LineEntity line: Path([line.Start, line.End]); break;
                case PointEntity point: Path([point.Position]); break;
                case CircleEntity circle: Path(Curve(circle.Center, circle.Radius, 0, 360), true); break;
                case ArcEntity arc: Path(Curve(arc.Center, arc.Radius, arc.StartAngle, GeometryMath.NormalizeAngle(arc.EndAngle - arc.StartAngle))); break;
                case PolylineEntity polyline when polyline.HasWidth:
                    // Keep a filled outline per segment, not one Skia path per triangle.
                    // Faces are explicit geometry and remain pickable away from the centerline.
                    foreach (var strip in PolylineWidths.Build(polyline))
                    {
                        if (strip.Triangles.IsEmpty) { Path(strip.Vertices); continue; }
                        Path(strip.Outline, true, true);
                        for (var i = 0; i < strip.Triangles.Length; i += 3)
                        {
                            var a = transform.Point(strip.Vertices[strip.Triangles[i]]);
                            var b = transform.Point(strip.Vertices[strip.Triangles[i + 1]]);
                            var c = transform.Point(strip.Vertices[strip.Triangles[i + 2]]);
                            if (transform.Determinant < 0) (b, c) = (c, b);
                            if (triangles.Count >= 1000000) throw new ArgumentException("Expanded scene exceeds one million triangles.");
                            triangles.Add(new(root, color, a, b, c));
                        }
                    }
                    break;
                case PolylineEntity polyline:
                    if (pattern != null && !polyline.ContinuousLinetype)
                    {
                        for (var i = 0; i < polyline.Vertices.Length - (polyline.Closed ? 0 : 1); i++)
                            Path(PolylinePoints(new([polyline.Vertices[i], polyline.Vertices[(i + 1) % polyline.Vertices.Length]], false)));
                    }
                    else Path(PolylinePoints(polyline), polyline.Closed);
                    break;
                case EllipseEntity ellipse:
                    var minor = new Vec3(-ellipse.MajorAxis.Y, ellipse.MajorAxis.X) * ellipse.Ratio;
                    var sweep = ellipse.EndParameter - ellipse.StartParameter; if (sweep <= 0) sweep += 2 * Math.PI;
                    Path(Enumerable.Range(0, 257).Select(i => ellipse.Center + ellipse.MajorAxis * Math.Cos(ellipse.StartParameter + sweep * i / 256) + minor * Math.Sin(ellipse.StartParameter + sweep * i / 256)), Math.Abs(sweep - Math.PI * 2) < 1e-8); break;
                case TextEntity text: Text(text.Position, text.Text.Replace("\\P", "\n"), text.Height, text.Rotation); break;
                case DimensionEntity dim:
                    if (DimensionGeometry.UsesPicture(dim) && drawing.Blocks.ContainsKey(dim.Picture!.BlockName))
                        Child(new BlockReferenceEntity(dim.Picture.BlockName, dim.Picture.Insertion, new(1,1,1)), transform);
                    else foreach (var child in DimensionGeometry.For(dim).Entities) Child(child, transform);
                    break;
                case HatchEntity hatch:
                    pattern = null; lineName = "CONTINUOUS";
                    var hatchPattern = ImmutableArray.Create(new HatchPatternLine(hatch.Angle, default, Transform3.RotationZ(hatch.Angle).Vector(new(0, hatch.Spacing)), []));
                    var region = new HatchRegionEntity([hatch.Boundary.Select(p => new PolyVertex(p)).ToImmutableArray()], hatch.Solid, hatchPattern, hatch.Solid ? "SOLID" : "ANSI31");
                    foreach (var child in AdvancedGeometry.Expand(region)) Child(child, transform); break;
                case MeshEntity mesh:
                    var edges = new Dictionary<(int, int), (Vec3 Normal, int Count, bool Crease)>();
                    void Edge(int a, int b, Vec3 normal)
                    {
                        var key = a < b ? (a, b) : (b, a);
                        if (edges.TryGetValue(key, out var old)) edges[key] = (old.Normal, old.Count + 1, old.Crease || Math.Abs(old.Normal.Dot(normal)) < .99999);
                        else edges.Add(key, (normal, 1, false));
                    }
                    for (var i = 0; i < mesh.Triangles.Length; i += 3)
                    {
                        var ia = mesh.Triangles[i]; var ib = mesh.Triangles[i + 1]; var ic = mesh.Triangles[i + 2];
                        var a = transform.Point(mesh.Vertices[ia]); var b = transform.Point(mesh.Vertices[ib]); var c = transform.Point(mesh.Vertices[ic]);
                        var n = (b - a).Cross(c - a); if (n.Length < 1e-12) continue;
                        triangles.Add(new(root, color, a, b, c));
                        if (triangles.Count > 1000000) throw new ArgumentException("Expanded scene exceeds one million triangles.");
                        if (mesh.Operation is not ("Hatch fill" or "Dimension arrow")) { Edge(ia, ib, n.Normalized); Edge(ib, ic, n.Normalized); Edge(ic, ia, n.Normalized); }
                    }
                    foreach (var edge in edges.Where(p => p.Value.Count == 1 || p.Value.Crease)) Path([mesh.Vertices[edge.Key.Item1], mesh.Vertices[edge.Key.Item2]]);
                    // Filled regions have an explicit 2D fill, not their internal tessellation edges.
                    if (mesh.Operation is "Hatch fill" or "Dimension arrow") for (var i = 0; i < mesh.Triangles.Length; i += 3) Path([mesh.Vertices[mesh.Triangles[i]], mesh.Vertices[mesh.Triangles[i + 1]], mesh.Vertices[mesh.Triangles[i + 2]]], true, true);
                    break;
                case BlockReferenceEntity insert when drawing.Blocks.TryGetValue(insert.Name, out var block):
                    var local = Transform3.Translation(-block.BasePoint).Then(Transform3.Scaling(insert.Scale)).Then(Transform3.RotationZ(insert.Rotation)).Then(Transform3.Translation(insert.Position)).Then(transform);
                    foreach (var child in block.Entities) Add(child, local, root, layerName, color, depth + 1, lineName); break;
            }
        }
        foreach (var entity in drawing.Entities.Where(e => e.Layout.Equals(layout, StringComparison.OrdinalIgnoreCase))) Add(entity, Transform3.Identity, entity.Id, null, null, 0);
        return new(paths.ToImmutable(), texts.ToImmutable(), triangles.ToImmutable());
    }
    public static Entity Transform(Entity entity, Transform3 transform, bool copy = false)
    {
        if (!transform.X.IsFinite || !transform.Y.IsFinite || !transform.Z.IsFinite || !transform.Origin.IsFinite || Math.Abs(transform.Determinant) < 1e-18) throw new ArgumentException("Transform must be finite and nonsingular.");
        var scale = transform.X.Length; var mirror = GeometryMath.Cross2(transform.X, transform.Y) < 0;
        var xySimilarity = Math.Abs(transform.X.Dot(transform.Y)) <= 1e-7 * Math.Max(1, scale * scale) && Math.Abs(scale - transform.Y.Length) <= 1e-7 * Math.Max(1, scale) && Math.Abs(transform.X.Z) + Math.Abs(transform.Y.Z) <= 1e-7;
        double Angle(double angle) => GeometryMath.Angle(transform.Vector(GeometryMath.OnCircle(default, 1, angle)));
        Entity Place() => new PlacedEntity(entity, transform) { Id = entity.Id, Handle = entity.Handle, Layer = entity.Layer, ColorIndex = entity.ColorIndex, TrueColor = entity.TrueColor, LineWeight = entity.LineWeight, Visible = entity.Visible, Layout = entity.Layout, Linetype = entity.Linetype, LinetypeScale = entity.LinetypeScale };
        Entity result = entity switch
        {
            OpaqueEntity => throw new NotSupportedException("Opaque DXF records cannot be transformed without a geometry interpreter."),
            PlacedEntity p => p with { Placement = p.Placement.Then(transform) },
            SplineEntity s => s with { ControlPoints = s.ControlPoints.Select(transform.Point).ToImmutableArray() },
            Polyline3DEntity p => p with { Points = p.Points.Select(transform.Point).ToImmutableArray() },
            CompositeEntity c => c with { Children = c.Children.Select(e => Transform(e, transform, copy)).ToImmutableArray(), SourceRecord = "" },
            LineEntity l => l with { Start = transform.Point(l.Start), End = transform.Point(l.End) },
            PointEntity p => p with { Position = transform.Point(p.Position) },
            MeshEntity m => m with { Vertices = m.Vertices.Select(transform.Point).ToImmutableArray(), Triangles = transform.Determinant < 0 ? m.Triangles.Chunk(3).SelectMany(t => new[] { t[0], t[2], t[1] }).ToImmutableArray() : m.Triangles },
            CircleEntity c when xySimilarity => c with { Center = transform.Point(c.Center), Radius = c.Radius * scale },
            ArcEntity a when xySimilarity => a with { Center = transform.Point(a.Center), Radius = a.Radius * scale, StartAngle = Angle(mirror ? a.EndAngle : a.StartAngle), EndAngle = Angle(mirror ? a.StartAngle : a.EndAngle) },
            PolylineEntity p when xySimilarity => p with { ConstantWidth = p.ConstantWidth * scale, Vertices = p.Vertices.Select(v => v with { Position = transform.Point(v.Position), Bulge = mirror ? -v.Bulge : v.Bulge, StartWidth = v.StartWidth * scale, EndWidth = v.EndWidth * scale }).ToImmutableArray() },
            EllipseEntity e when xySimilarity && !mirror => e with { Center = transform.Point(e.Center), MajorAxis = transform.Vector(e.MajorAxis) },
            TextEntity t when xySimilarity && !mirror => t with { Position = transform.Point(t.Position), Height = t.Height * scale, Rotation = Angle(t.Rotation) },
            DimensionEntity d when xySimilarity && !mirror && d.Picture == null => DimensionGeometry.TransformPlanar(d, transform),
            HatchEntity h when xySimilarity => h with { Boundary = h.Boundary.Select(transform.Point).ToImmutableArray(), Spacing = h.Spacing * scale, Angle = Angle(h.Angle) },
            BlockReferenceEntity b when xySimilarity && !mirror && Math.Abs(transform.Z.Length - scale) < 1e-7 => b with { Position = transform.Point(b.Position), Scale = b.Scale * scale, Rotation = Angle(b.Rotation) },
            _ => Place()
        };
        return copy ? result with { Id = Guid.NewGuid(), Handle = "" } : result;
    }
}
