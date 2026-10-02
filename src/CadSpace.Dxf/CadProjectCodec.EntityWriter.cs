using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using CadSpace.Geometry;
using CadSpace.Model;

namespace CadSpace.Dxf;

public static partial class CadProjectCodec
{
    private static void Entities(Utf8JsonWriter w, IEnumerable<Entity> entities)
    {
        w.WriteStartArray();
        foreach (var entity in entities)
        {
            w.WriteStartObject(); w.WriteString("type", entity switch { OpaqueEntity => "OPAQUE", PlacedEntity => "PLACED", CompositeEntity => "COMPOSITE", HatchRegionEntity => "HATCH_REGION", _ => entity.Kind });
            w.WriteString("id", entity.Id); w.WriteString("handle", entity.Handle); w.WriteString("layer", entity.Layer); w.WriteNumber("aci", entity.ColorIndex); w.WriteNumber("weight", entity.LineWeight); w.WriteString("linetype", entity.Linetype); w.WriteNumber("linetypeScale", entity.LinetypeScale); w.WriteBoolean("visible", entity.Visible); w.WriteString("layout", entity.Layout);
            if (entity.TrueColor is uint color) w.WriteNumber("color", color);
            switch (entity)
            {
                case LineEntity e: Point(w, "a", e.Start); Point(w, "b", e.End); break;
                case PointEntity e: Point(w, "point", e.Position); break;
                case CircleEntity e: Point(w, "center", e.Center); w.WriteNumber("radius", e.Radius); break;
                case ArcEntity e: Point(w, "center", e.Center); w.WriteNumber("radius", e.Radius); w.WriteNumber("start", e.StartAngle); w.WriteNumber("end", e.EndAngle); break;
                case PolylineEntity e:
                    w.WriteBoolean("closed", e.Closed); w.WriteNumber("constantWidth", e.ConstantWidth); w.WriteBoolean("continuousLinetype", e.ContinuousLinetype); w.WritePropertyName("vertices"); w.WriteStartArray();
                    foreach (var v in e.Vertices) { w.WriteStartObject(); Point(w, "point", v.Position); w.WriteNumber("bulge", v.Bulge); w.WriteNumber("startWidth", v.StartWidth); w.WriteNumber("endWidth", v.EndWidth); w.WriteEndObject(); } w.WriteEndArray(); break;
                case EllipseEntity e: Point(w, "center", e.Center); Point(w, "major", e.MajorAxis); w.WriteNumber("ratio", e.Ratio); w.WriteNumber("start", e.StartParameter); w.WriteNumber("end", e.EndParameter); break;
                case TextEntity e: Point(w, "point", e.Position); w.WriteString("text", e.Text); w.WriteNumber("height", e.Height); w.WriteNumber("rotation", e.Rotation); break;
                case LeaderEntity e: WriteLeader(w, e); break;
                case DimensionEntity e: WriteDimension(w, e); break;
                case HatchEntity e: Points(w, "boundary", e.Boundary); w.WriteNumber("spacing", e.Spacing); w.WriteNumber("angle", e.Angle); w.WriteBoolean("solid", e.Solid); break;
                case MeshEntity e: Points(w, "vertices", e.Vertices); w.WritePropertyName("triangles"); w.WriteStartArray(); foreach (var index in e.Triangles) w.WriteNumberValue(index); w.WriteEndArray(); w.WriteString("operation", e.Operation); break;
                case BlockReferenceEntity e: w.WriteString("name", e.Name); Point(w, "point", e.Position); Point(w, "scale", e.Scale); w.WriteNumber("rotation", e.Rotation); break;
                case Polyline3DEntity e: Points(w, "points", e.Points); w.WriteBoolean("closed", e.Closed); w.WriteBoolean("continuousLinetype", e.ContinuousLinetype); break;
                case SplineEntity e:
                    w.WriteNumber("degree", e.Degree); Points(w, "controls", e.ControlPoints); Numbers(w, "knots", e.Knots); Numbers(w, "weights", e.Weights); w.WriteBoolean("closed", e.Closed); w.WriteBoolean("periodic", e.Periodic); break;
                case PlacedEntity e:
                    Points(w, "matrix", [e.Placement.X, e.Placement.Y, e.Placement.Z, e.Placement.Origin]); w.WritePropertyName("geometry"); Entities(w, [e.Geometry]); break;
                case CompositeEntity e:
                    w.WriteString("dxfType", e.DxfType); w.WriteString("source", e.SourceRecord); w.WritePropertyName("children"); Entities(w, e.Children); break;
                case HatchRegionEntity e:
                    WriteGradient(w, e.Gradient);
                    w.WriteString("patternName", e.PatternName); w.WriteNumber("islandStyle", e.IslandStyle); w.WriteBoolean("solid", e.Solid); w.WriteBoolean("sampled", e.SampledBoundary);
                    w.WritePropertyName("loops"); w.WriteStartArray();
                    foreach (var loop in e.Loops) { w.WriteStartArray(); foreach (var v in loop) { w.WriteStartObject(); Point(w, "point", v.Position); w.WriteNumber("bulge", v.Bulge); w.WriteEndObject(); } w.WriteEndArray(); } w.WriteEndArray();
                    w.WritePropertyName("pattern"); w.WriteStartArray();
                    foreach (var line in e.Pattern) { w.WriteStartObject(); w.WriteNumber("angle", line.Angle); Point(w, "origin", line.Origin); Point(w, "offset", line.Offset); Numbers(w, "dashes", line.Dashes); w.WriteEndObject(); } w.WriteEndArray(); break;
                case OpaqueEntity e: w.WriteString("dxfType", e.DxfType); w.WriteString("raw", e.RawRecord); break;
                default: throw new NotSupportedException($"Native persistence does not know entity {entity.Kind}.");
            }
            w.WriteEndObject();
        }
        w.WriteEndArray();
    }
}
