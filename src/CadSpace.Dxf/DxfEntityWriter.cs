using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using CadSpace.Geometry;
using CadSpace.Model;

namespace CadSpace.Dxf;

internal static class DxfEntityWriter
{
    public static bool TryWrite(Entity entity, Drawing drawing, Func<Entity, string> emit, Func<string> nextHandle, Action<string> warn, out string text)
    {
        text = "";
        if (entity is not (Polyline3DEntity or SplineEntity or PlacedEntity or CompositeEntity or HatchRegionEntity or HatchEntity or MeshEntity or TextEntity { Multiline: true })) return false;
        var buffer = new StringBuilder();
        void Pair(int code, object value) => buffer.Append(code).Append('\n').Append(Convert.ToString(value, CultureInfo.InvariantCulture)).Append('\n');
        void Point(int code, Vec3 p) { Pair(code, p.X); Pair(code + 10, p.Y); Pair(code + 20, p.Z); }
        void Start(string type, string subclass, string? handle = null)
        {
            Pair(0, type); Pair(5, handle ?? (entity.Handle.Length == 0 ? nextHandle() : entity.Handle)); Pair(100, "AcDbEntity"); Pair(8, entity.Layer);
            Pair(6, entity.Linetype); Pair(48, entity.LinetypeScale);
            if (!entity.Visible) Pair(60, 1);
            if (entity.Layout != "Model") { Pair(67, 1); Pair(410, entity.Layout); }
            Pair(62, entity.ColorIndex); if (entity.TrueColor is uint color) Pair(420, color & 0xFFFFFF);
            if (entity.LineWeight >= 0) Pair(370, Math.Round(entity.LineWeight * 100)); Pair(100, subclass);
        }
        Entity Style(Entity child) => child with { Id = Guid.NewGuid(), Handle = "", Layer = entity.Layer, ColorIndex = entity.ColorIndex, TrueColor = entity.TrueColor, LineWeight = entity.LineWeight, Linetype = entity.Linetype, LinetypeScale = entity.LinetypeScale, Visible = entity.Visible, Layout = entity.Layout };
        void Hatch(HatchRegionEntity hatch, Vec3 normal = default)
        {
            if (normal == default) normal = Vec3.UnitZ;
            var z = hatch.Loops[0][0].Position.Z;
            if (hatch.Loops.SelectMany(l => l).Any(v => Math.Abs(v.Position.Z - z) > 1e-7)) throw new NotSupportedException("A hatch must be coplanar in its OCS.");
            Start("HATCH", "AcDbHatch"); Point(10, new(0, 0, z)); Point(210, normal); Pair(2, hatch.PatternName); Pair(70, hatch.Solid ? 1 : 0); Pair(71, 0); Pair(91, hatch.Loops.Length);
            foreach (var loop in hatch.Loops)
            {
                Pair(92, 2); Pair(72, loop.Any(v => v.Bulge != 0) ? 1 : 0); Pair(73, 1); Pair(93, loop.Length);
                foreach (var vertex in loop) { Pair(10, vertex.Position.X); Pair(20, vertex.Position.Y); if (loop.Any(v => v.Bulge != 0)) Pair(42, vertex.Bulge); }
                Pair(97, 0);
            }
            Pair(75, hatch.IslandStyle); Pair(76, 1);
            if (!hatch.Solid)
            {
                Pair(52, 0); Pair(41, 1); Pair(77, 0); Pair(78, hatch.Pattern.Length);
                foreach (var line in hatch.Pattern)
                {
                    Pair(53, line.Angle); Pair(43, line.Origin.X); Pair(44, line.Origin.Y); Pair(45, line.Offset.X); Pair(46, line.Offset.Y); Pair(79, line.Dashes.Length);
                    foreach (var dash in line.Dashes) Pair(49, dash);
                }
            }
            Pair(98, 0);
            if (hatch.Gradient is { } gradient) DxfHatchGradient.Write(gradient, Pair);
            if (hatch.SampledBoundary) warn("Edited HATCH edge-list boundaries are exported as sampled polyline boundaries.");
        }
        void Ellipse(Vec3 center, Vec3 u, Vec3 v, double start, double sweep)
        {
            // Principal axes of the affine image of a unit circle, without flattening Z.
            var angle = .5 * Math.Atan2(2 * u.Dot(v), u.Dot(u) - v.Dot(v));
            var major = u * Math.Cos(angle) + v * Math.Sin(angle); var minor = -u * Math.Sin(angle) + v * Math.Cos(angle);
            var normal = u.Cross(v).Normalized; var ratio = minor.Length / major.Length;
            if (!double.IsFinite(ratio) || ratio <= 0 || ratio > 1 + 1e-8) throw new ArgumentException("The transformed ellipse is degenerate.");
            double Normalize(double a) => (a % (2 * Math.PI) + 2 * Math.PI) % (2 * Math.PI);
            var full = Math.Abs(Math.Abs(sweep) - 2 * Math.PI) < 1e-8;
            Start("ELLIPSE", "AcDbEllipse"); Point(10, center); Point(11, major); Point(210, normal); Pair(40, Math.Min(1, ratio)); Pair(41, full ? 0 : Normalize(start - angle)); Pair(42, full ? 2 * Math.PI : Normalize(start + sweep - angle));
        }
        void Placed(PlacedEntity placed)
        {
            var t = placed.Placement; var geometry = placed.Geometry;
            while (geometry is PlacedEntity nested) { t = nested.Placement.Then(t); geometry = nested.Geometry; }
            switch (geometry)
            {
                case LineEntity or PointEntity or MeshEntity or Polyline3DEntity or SplineEntity:
                    buffer.Append(emit(Style(EntityGeometry.Transform(geometry, t)))); break;
                case CircleEntity circle:
                    Ellipse(t.Point(circle.Center), t.Vector(Vec3.UnitX * circle.Radius), t.Vector(Vec3.UnitY * circle.Radius), 0, 2 * Math.PI); break;
                case ArcEntity arc:
                    Ellipse(t.Point(arc.Center), t.Vector(Vec3.UnitX * arc.Radius), t.Vector(Vec3.UnitY * arc.Radius), GeometryMath.Radians(arc.StartAngle), GeometryMath.Radians(GeometryMath.NormalizeAngle(arc.EndAngle - arc.StartAngle))); break;
                case EllipseEntity ellipse:
                    var sweep = ellipse.EndParameter - ellipse.StartParameter; if (sweep <= 0) sweep += 2 * Math.PI;
                    Ellipse(t.Point(ellipse.Center), t.Vector(ellipse.MajorAxis), t.Vector(new Vec3(-ellipse.MajorAxis.Y, ellipse.MajorAxis.X) * ellipse.Ratio), ellipse.StartParameter, sweep); break;
                case PolylineEntity poly:
                    var frame = Coordinates3D.ObjectCoordinateSystem(t.X.Cross(t.Y)); var inverse = Coordinates3D.Inverse(frame);
                    var similarity = Math.Abs(t.X.Dot(t.Y)) <= 1e-8 * t.X.Length * t.Y.Length && Math.Abs(t.X.Length - t.Y.Length) <= 1e-8 * Math.Max(t.X.Length, t.Y.Length);
                    if (similarity)
                    {
                        var vertices = poly.Vertices.Select(v => v with { Position = inverse.Point(t.Point(v.Position)), StartWidth = v.StartWidth * t.X.Length, EndWidth = v.EndWidth * t.X.Length }).ToArray();
                        Start("LWPOLYLINE", "AcDbPolyline"); Pair(90, vertices.Length); Pair(70, (poly.Closed ? 1 : 0) | (poly.ContinuousLinetype ? 128 : 0)); Pair(38, vertices[0].Position.Z); Point(210, frame.Z); if (poly.ConstantWidth != 0) Pair(43, poly.ConstantWidth * t.X.Length);
                        foreach (var vertex in vertices) { Pair(10, vertex.Position.X); Pair(20, vertex.Position.Y); if (vertex.Bulge != 0) Pair(42, vertex.Bulge); if (vertex.StartWidth != 0) Pair(40, vertex.StartWidth); if (vertex.EndWidth != 0) Pair(41, vertex.EndWidth); }
                    }
                    else
                    {
                        if (poly.HasWidth)
                        {
                            warn("An affinely deformed wide polyline is exported as sampled mesh faces, not editable polyline widths. Save a native project to retain the analytic source.");
                            foreach (var strip in PolylineWidths.Build(poly))
                            {
                                Entity display = strip.Triangles.IsEmpty ? new Polyline3DEntity(strip.Vertices) : new MeshEntity(strip.Vertices, strip.Triangles, "Polyline width");
                                buffer.Append(emit(Style(EntityGeometry.Transform(display, t))));
                            }
                            break;
                        }
                        warn("An affinely deformed bulged polyline is exported as a sampled 3D polyline.");
                        buffer.Append(emit(Style(new Polyline3DEntity(EntityGeometry.PolylinePoints(poly).Select(t.Point).ToImmutableArray(), poly.Closed) { ContinuousLinetype = poly.ContinuousLinetype })));
                    }
                    break;
                case TextEntity { Multiline: true } multiline:
                    var textBasis = Transform3.RotationZ(multiline.Rotation).Then(t);
                    var sx = textBasis.X.Length; var sy = textBasis.Y.Length;
                    if (!double.IsFinite(sx) || !double.IsFinite(sy) || sx <= 1e-12 || sy <= 1e-12 ||
                        Math.Abs(sx - sy) > 1e-9 * Math.Max(sx, sy) || Math.Abs((textBasis.X / sx).Dot(textBasis.Y / sy)) > 1e-9)
                        throw new NotSupportedException("MTEXT export requires orthogonal, equally scaled text-plane axes. Save a native project for shear/nonuniform scaling.");
                    if (!double.IsFinite(multiline.Height * sy) || multiline.Height * sy <= 0) throw new NotSupportedException("The transformed MTEXT height is outside the numeric range.");
                    Start("MTEXT", "AcDbMText"); Point(10, t.Point(multiline.Position)); Pair(40, multiline.Height * sy);
                    Pair(41, 0); Pair(71, 1);
                    foreach (var chunk in DxfTextContent.Chunks(multiline.Text)) Pair(chunk.Code, chunk.Value);
                    Point(210, (textBasis.X / sx).Cross(textBasis.Y / sy).Normalized); Point(11, textBasis.X / sx);
                    break;
                case TextEntity textEntity when !textEntity.Multiline:
                    var basis = Transform3.RotationZ(textEntity.Rotation).Then(t); var x = basis.X; var y = basis.Y; var n = x.Cross(y).Normalized;
                    var ocs = Coordinates3D.ObjectCoordinateSystem(n); var local = Coordinates3D.Inverse(ocs); var perpendicular = y.Dot(n.Cross(x.Normalized));
                    Start("TEXT", "AcDbText"); Point(10, local.Point(t.Point(textEntity.Position))); Pair(40, textEntity.Height * perpendicular); Pair(1, textEntity.Text.Replace('\n', ' ').Replace('\r', ' ')); Pair(50, GeometryMath.Angle(local.Vector(x))); Pair(41, x.Length / perpendicular); Pair(51, GeometryMath.Degrees(Math.Atan2(y.Dot(x.Normalized), perpendicular))); Point(210, n); Pair(100, "AcDbText"); break;
                case HatchRegionEntity region:
                    var normal = t.X.Cross(t.Y).Normalized; var ocsH = Coordinates3D.ObjectCoordinateSystem(normal); var toLocal = t.Then(Coordinates3D.Inverse(ocsH));
                    var sameScale = Math.Abs(t.X.Dot(t.Y)) <= 1e-8 * t.X.Length * t.Y.Length && Math.Abs(t.X.Length - t.Y.Length) <= 1e-8 * t.X.Length;
                    var loops = sameScale ? region.Loops.Select(l => l.Select(v => new PolyVertex(toLocal.Point(v.Position), v.Bulge)).ToImmutableArray()).ToImmutableArray()
                        : AdvancedGeometry.HatchLoops(region).Select(l => l.Select(v => new PolyVertex(toLocal.Point(v))).ToImmutableArray()).ToImmutableArray();
                    var patterns = region.Pattern.Select(p =>
                    {
                        var direction = toLocal.Vector(GeometryMath.OnCircle(default, 1, p.Angle));
                        return new HatchPatternLine(GeometryMath.Angle(direction), toLocal.Point(p.Origin), toLocal.Vector(p.Offset), p.Dashes.Select(d => d * direction.Length).ToImmutableArray());
                    }).ToImmutableArray();
                    if (region.Gradient != null && !sameScale) throw new NotSupportedException("Nonuniformly transformed gradient export needs a native project to preserve the color field.");
                    var gradient = region.Gradient;
                    if (gradient != null) gradient = gradient with { Angle = GeometryMath.Angle(toLocal.Vector(GeometryMath.OnCircle(default, 1, gradient.Angle))) };
                    Hatch(region with { Loops = loops, Pattern = patterns, Gradient = gradient, SampledBoundary = region.SampledBoundary || !sameScale }, normal); break;
                case HatchEntity simple:
                    Placed(new PlacedEntity(Region(simple), t)); break;
                case BlockReferenceEntity block when drawing.Blocks.TryGetValue(block.Name, out var definition):
                    warn("A general affine block placement is expanded to geometry in DXF; save a native project to retain its reference semantics.");
                    var blockTransform = Transform3.Translation(-definition.BasePoint).Then(Transform3.Scaling(block.Scale)).Then(Transform3.RotationZ(block.Rotation)).Then(Transform3.Translation(block.Position)).Then(t);
                    foreach (var child in definition.Entities) buffer.Append(emit(Style(EntityGeometry.Transform(child, blockTransform)))); break;
                case CompositeEntity composite:
                    warn($"Modified compound {composite.DxfType} is exported as display geometry.");
                    foreach (var child in composite.Children) buffer.Append(emit(Style(EntityGeometry.Transform(child, t)))); break;
                default:
                    throw new NotSupportedException($"DXF cannot represent the placed {geometry.Kind} without losing unsupported semantics. Save a native project.");
            }
        }
        switch (entity)
        {
            case TextEntity multiline: Placed(new PlacedEntity(multiline, Transform3.Identity)); break;
            case Polyline3DEntity poly:
                Start("POLYLINE", "AcDb3dPolyline"); Pair(66, 1); Point(10, default); Pair(70, 8 | (poly.Closed ? 1 : 0) | (poly.ContinuousLinetype ? 128 : 0));
                foreach (var p in poly.Points) { Start("VERTEX", "AcDbVertex", nextHandle()); Pair(100, "AcDb3dPolylineVertex"); Point(10, p); Pair(70, 32); }
                Pair(0, "SEQEND"); Pair(5, nextHandle()); Pair(100, "AcDbEntity"); Pair(8, entity.Layer); break;
            case SplineEntity spline:
                Start("SPLINE", "AcDbSpline"); Pair(70, (spline.Closed ? 1 : 0) | (spline.Periodic ? 2 : 0) | (spline.Weights.IsEmpty ? 0 : 4)); Pair(71, spline.Degree); Pair(72, spline.Knots.Length); Pair(73, spline.ControlPoints.Length); Pair(74, 0);
                foreach (var k in spline.Knots) Pair(40, k); foreach (var w in spline.Weights) Pair(41, w); foreach (var p in spline.ControlPoints) Point(10, p); break;
            case MeshEntity { Operation: "Dimension arrow", Vertices.Length: 3, Triangles.Length: 3 } arrow:
                Start("SOLID", "AcDbTrace"); Point(10, arrow.Vertices[0]); Point(11, arrow.Vertices[1]);
                Point(12, arrow.Vertices[2]); Point(13, arrow.Vertices[2]); break;
            case MeshEntity mesh:
                Start("MESH", "AcDbSubDMesh"); Pair(71, 2); Pair(72, 0); Pair(91, 0); Pair(92, mesh.Vertices.Length);
                foreach (var vertex in mesh.Vertices) Point(10, vertex);
                Pair(93, mesh.Triangles.Length / 3 * 4);
                for (var i = 0; i < mesh.Triangles.Length; i += 3) { Pair(90, 3); Pair(90, mesh.Triangles[i]); Pair(90, mesh.Triangles[i + 1]); Pair(90, mesh.Triangles[i + 2]); }
                Pair(94, 0); Pair(95, 0); Pair(90, 0); break;
            case HatchRegionEntity region: Hatch(region); break;
            case HatchEntity simple: Hatch(Region(simple)); break;
            case PlacedEntity placed: Placed(placed); break;
            case CompositeEntity composite:
                if (DxfAttributeEditing.TryWriteRetained(composite, warn, out var retainedAttributes)) { buffer.Append(retainedAttributes); break; }
                warn($"Modified {composite.DxfType} is exported as its explicit display children, not the original compound semantics.");
                foreach (var child in composite.Children)
                {
                    var styled = child with { Handle = "", Id = Guid.NewGuid(), Layout = entity.Layout, Visible = child.Visible && entity.Visible, Layer = child.Layer == "0" ? entity.Layer : child.Layer };
                    if (styled.ColorIndex == 0) styled = styled with { TrueColor = entity.TrueColor, ColorIndex = entity.ColorIndex };
                    buffer.Append(emit(styled));
                }
                break;
        }
        text = buffer.ToString(); return true;
    }

    private static HatchRegionEntity Region(HatchEntity hatch) => new([hatch.Boundary.Select(p => new PolyVertex(p)).ToImmutableArray()], hatch.Solid,
        hatch.Solid ? [] : [new(hatch.Angle, default, Transform3.RotationZ(hatch.Angle).Vector(new(0, hatch.Spacing)), [])], hatch.Solid ? "SOLID" : "USER");
}
