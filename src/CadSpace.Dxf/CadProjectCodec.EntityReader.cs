using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using CadSpace.Geometry;
using CadSpace.Model;

namespace CadSpace.Dxf;

public static partial class CadProjectCodec
{
    private static ImmutableArray<Entity> ReadEntities(JsonElement array)
    {
        return array.EnumerateArray().Select(e =>
        {
            var type = S(e, "type");
            Entity entity = type switch
            {
                "LINE" => new LineEntity(P(e, "a"), P(e, "b")),
                "POINT" => new PointEntity(P(e, "point")),
                "CIRCLE" => new CircleEntity(P(e, "center"), N(e, "radius")),
                "ARC" => new ArcEntity(P(e, "center"), N(e, "radius"), N(e, "start"), N(e, "end")),
                "LWPOLYLINE" => new PolylineEntity(e.GetProperty("vertices").EnumerateArray().Select(v => new PolyVertex(P(v, "point"), N(v, "bulge")) { StartWidth = v.TryGetProperty("startWidth", out var sw) ? sw.GetDouble() : 0, EndWidth = v.TryGetProperty("endWidth", out var ew) ? ew.GetDouble() : 0 }).ToImmutableArray(), e.GetProperty("closed").GetBoolean()) { ConstantWidth = e.TryGetProperty("constantWidth", out var cw) ? cw.GetDouble() : 0, ContinuousLinetype = e.TryGetProperty("continuousLinetype", out var generated) && generated.GetBoolean() },
                "ELLIPSE" => new EllipseEntity(P(e, "center"), P(e, "major"), N(e, "ratio"), N(e, "start"), N(e, "end")),
                "TEXT" or "MTEXT" => new TextEntity(P(e, "point"), S(e, "text"), N(e, "height"), N(e, "rotation"), type == "MTEXT"),
                "LEADER" => ReadLeader(e),
                "DIMENSION" => ReadDimension(e),
                "HATCH" => new HatchEntity(Points(e.GetProperty("boundary")), N(e, "spacing"), N(e, "angle"), e.GetProperty("solid").GetBoolean()),
                "MESH" => new MeshEntity(Points(e.GetProperty("vertices")), e.GetProperty("triangles").EnumerateArray().Select(i => i.GetInt32()).ToImmutableArray(), S(e, "operation")),
                "INSERT" => new BlockReferenceEntity(S(e, "name"), P(e, "point"), P(e, "scale"), N(e, "rotation")),
                "POLYLINE" => new Polyline3DEntity(Points(e.GetProperty("points")), e.GetProperty("closed").GetBoolean()) { ContinuousLinetype = e.TryGetProperty("continuousLinetype", out var generated3d) && generated3d.GetBoolean() },
                "SPLINE" => new SplineEntity(e.GetProperty("degree").GetInt32(), Points(e.GetProperty("controls")), Numbers(e, "knots"), Numbers(e, "weights"), e.GetProperty("closed").GetBoolean(), e.GetProperty("periodic").GetBoolean()),
                "PLACED" => new PlacedEntity(ReadEntities(e.GetProperty("geometry")).Single(), Matrix(e.GetProperty("matrix"))),
                "COMPOSITE" => new CompositeEntity(S(e, "dxfType"), ReadEntities(e.GetProperty("children")), S(e, "source")),
                "HATCH_REGION" => new HatchRegionEntity(e.GetProperty("loops").EnumerateArray().Select(l => l.EnumerateArray().Select(v => new PolyVertex(P(v, "point"), N(v, "bulge"))).ToImmutableArray()).ToImmutableArray(), e.GetProperty("solid").GetBoolean(), e.GetProperty("pattern").EnumerateArray().Select(l => new HatchPatternLine(N(l, "angle"), P(l, "origin"), P(l, "offset"), Numbers(l, "dashes"))).ToImmutableArray(), S(e, "patternName"), e.GetProperty("sampled").GetBoolean()) { IslandStyle = e.TryGetProperty("islandStyle", out var style) ? style.GetInt32() : 0, Gradient = ReadGradient(e) },
                "OPAQUE" => new OpaqueEntity(S(e, "dxfType"), S(e, "raw")),
                _ => throw new FormatException($"Unknown native entity type: {type}")
            };
            return entity with { Id = e.GetProperty("id").GetGuid(), Handle = S(e, "handle"), Layer = S(e, "layer"), ColorIndex = e.GetProperty("aci").GetInt32(), LineWeight = N(e, "weight"), Linetype = e.TryGetProperty("linetype", out var lt) ? lt.GetString()! : "BYLAYER", LinetypeScale = e.TryGetProperty("linetypeScale", out var ls) ? ls.GetDouble() : 1, Visible = !e.TryGetProperty("visible", out var visible) || visible.GetBoolean(), Layout = e.TryGetProperty("layout", out var layout) ? layout.GetString() ?? "Model" : "Model", TrueColor = e.TryGetProperty("color", out var c) ? c.GetUInt32() : null };
        }).ToImmutableArray();
    }
}
