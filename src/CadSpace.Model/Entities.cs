using System.Collections.Immutable;
using CadSpace.Geometry;

namespace CadSpace.Model;

public abstract record Entity
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Handle { get; init; } = "";
    public string Layer { get; init; } = "0";
    public int ColorIndex { get; init; } = 256;
    public uint? TrueColor { get; init; }
    public double LineWeight { get; init; } = -1;
    public string Linetype { get; init; } = "BYLAYER";
    public double LinetypeScale { get; init; } = 1;
    public bool Visible { get; init; } = true;
    public string Layout { get; init; } = "Model";
    public abstract string Kind { get; }
}
public sealed record LineEntity(Vec3 Start, Vec3 End) : Entity { public override string Kind => "LINE"; }
public sealed record PointEntity(Vec3 Position) : Entity { public override string Kind => "POINT"; }
public sealed record CircleEntity(Vec3 Center, double Radius) : Entity { public override string Kind => "CIRCLE"; }
public sealed record ArcEntity(Vec3 Center, double Radius, double StartAngle, double EndAngle) : Entity { public override string Kind => "ARC"; }
public readonly record struct PolyVertex(Vec3 Position, double Bulge = 0)
{
    public double StartWidth { get; init; }
    public double EndWidth { get; init; }
}
public sealed record PolylineEntity(ImmutableArray<PolyVertex> Vertices, bool Closed = false) : Entity
{
    public override string Kind => "LWPOLYLINE";
    public double ConstantWidth { get; init; }
    public bool HasWidth => ConstantWidth > 0 || Vertices.Any(v => v.StartWidth > 0 || v.EndWidth > 0);
    public bool ContinuousLinetype { get; init; }
    public static PolylineEntity FromPoints(IEnumerable<Vec3> points, bool closed = false) => new(points.Select(p => new PolyVertex(p)).ToImmutableArray(), closed);
}
public sealed record EllipseEntity(Vec3 Center, Vec3 MajorAxis, double Ratio, double StartParameter = 0, double EndParameter = Math.PI * 2) : Entity { public override string Kind => "ELLIPSE"; }
public sealed record TextEntity(Vec3 Position, string Text, double Height = 12, double Rotation = 0, bool Multiline = false) : Entity { public override string Kind => Multiline ? "MTEXT" : "TEXT"; }
/// <summary>Local-plane definitions; placed wrappers supply arbitrary OCS planes.</summary>
public sealed record DimensionEntity(Vec3 First, Vec3 Second, Vec3 Location) : Entity
{
    public override string Kind => "DIMENSION";
    public DimensionKind Type { get; init; } = DimensionKind.Aligned;
    public Vec3 Third { get; init; }
    public Vec3 Fourth { get; init; }
    public double Rotation { get; init; }
    public bool OrdinateX { get; init; } = true;
    public string TextOverride { get; init; } = "";
    public Vec3? TextPosition { get; init; }
    public DimensionFormat Format { get; init; } = DimensionFormat.Default;
    public DimensionPicture? Picture { get; init; }
}
public sealed record HatchEntity(ImmutableArray<Vec3> Boundary, double Spacing = 10, double Angle = 45, bool Solid = false) : Entity { public override string Kind => "HATCH"; }
public sealed record MeshEntity(ImmutableArray<Vec3> Vertices, ImmutableArray<int> Triangles, string Operation = "Mesh") : Entity { public override string Kind => "MESH"; }
public sealed record BlockReferenceEntity(string Name, Vec3 Position, Vec3 Scale, double Rotation = 0) : Entity { public override string Kind => "INSERT"; }
/// <summary>Unsupported DXF records remain opaque. They are never presented as editable geometry.</summary>
public sealed record OpaqueEntity(string DxfType, string RawRecord) : Entity { public override string Kind => DxfType; }
public sealed record Layer(string Name, uint Color = 0xFFD8DFE8, bool Visible = true, bool Locked = false, double LineWeight = 0.25)
{
    public string Linetype { get; init; } = "CONTINUOUS";
}
public sealed record BlockDefinition(string Name, Vec3 BasePoint, ImmutableArray<Entity> Entities);

public sealed record Drawing(ImmutableArray<Entity> Entities, ImmutableDictionary<string, Layer> Layers, ImmutableDictionary<string, BlockDefinition> Blocks)
{
    public string Name { get; init; } = "Drawing1.dxf";
    public int Units { get; init; } = 4;
    public ImmutableDictionary<string, Linetype> Linetypes { get; init; } = Linetype.Defaults;
    public double LinetypeScale { get; init; } = 1;
    public ImmutableDictionary<string, string> LayoutBlockNames { get; init; } = ImmutableDictionary.Create<string, string>(StringComparer.OrdinalIgnoreCase).Add("Model", "*Model_Space").Add("Layout1", "*Paper_Space");
    public static Drawing Empty => new([], ImmutableDictionary.Create<string, Layer>(StringComparer.OrdinalIgnoreCase).Add("0", new("0")), ImmutableDictionary.Create<string, BlockDefinition>(StringComparer.OrdinalIgnoreCase));
    public Layer LayerFor(Entity entity) => Layers.TryGetValue(entity.Layer, out var layer) ? layer : Layers["0"];
}
