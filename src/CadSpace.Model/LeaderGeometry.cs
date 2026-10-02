using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using CadSpace.Geometry;

namespace CadSpace.Model;

/// <summary>Straight LEADER definition in WCS. Annotation handles are retained references, not constraints.</summary>
public sealed record LeaderEntity(ImmutableArray<Vec3> Vertices) : Entity
{
    public override string Kind => "LEADER";
    public bool ArrowEnabled { get; init; } = true;
    public double ArrowSize { get; init; } = 2.5;
    public Vec3 Normal { get; init; } = Vec3.UnitZ;
    public Vec3 Horizontal { get; init; } = Vec3.UnitX;
    public bool Hookline { get; init; }
    public bool HooklineReversed { get; init; }
    public bool TextAbove { get; init; } = true;
    public double TextHeight { get; init; }
    public double TextWidth { get; init; }
    public double Gap { get; init; } = .625;
    public int AnnotationType { get; init; } = 3;
    public string AnnotationHandle { get; init; } = "0";
    public Vec3 BlockOffset { get; init; }
    public Vec3 AnnotationOffset { get; init; }
    public int BlockColor { get; init; } = 7;
    public string DimensionStyle { get; init; } = "Standard";
}

/// <summary>Shared identity-cached leader paths/arrows for 2D, 3D and picking.</summary>
public static class LeaderGeometry
{
    // Group 76 is a signed 16-bit DXF integer in binary transport.
    public const int MaximumVertices = 32767;
    private static readonly ConditionalWeakTable<LeaderEntity, Lazy<ImmutableArray<Entity>>> Cache = new();
    public static LeaderEntity? Unwrap(Entity e)
    {
        for (var depth = 0; e is PlacedEntity p; depth++) { if (depth >= 32) return null; e = p.Geometry; }
        return e as LeaderEntity;
    }
    public static void Validate(LeaderEntity e)
    {
        if (e.Vertices.IsDefault || e.Vertices.Length is < 2 or > MaximumVertices || e.Vertices.Any(p => !p.IsFinite))
            throw new ArgumentException("A leader needs 2–32,767 finite vertices.");
        for (var i = 1; i < e.Vertices.Length; i++)
            if (!double.IsFinite(e.Vertices[i].DistanceTo(e.Vertices[i-1])) || e.Vertices[i].DistanceTo(e.Vertices[i-1]) < 1e-12)
                throw new ArgumentException("Consecutive leader vertices must differ and have a finite distance.");
        if (!e.Normal.IsFinite || !double.IsFinite(e.Normal.Length) || e.Normal.Length < 1e-12 ||
            !e.Horizontal.IsFinite || !double.IsFinite(e.Horizontal.Length) || e.Horizontal.Length < 1e-12 ||
            !e.BlockOffset.IsFinite || !e.AnnotationOffset.IsFinite || e.AnnotationType is < 0 or > 3 || e.BlockColor is < 0 or > 256)
            throw new ArgumentException("Invalid leader normal, horizontal direction or annotation data.");
        foreach (var n in new[] { e.ArrowSize, e.TextWidth, e.TextHeight, e.Gap })
            if (!double.IsFinite(n) || n < 0 || n > 1e12) throw new ArgumentException("Leader sizes must be between 0 and 1e12.");
        if (string.IsNullOrEmpty(e.DimensionStyle) || e.DimensionStyle.Length > 255 || e.DimensionStyle.IndexOfAny(['\0','\r','\n']) >= 0 ||
            string.IsNullOrEmpty(e.AnnotationHandle) || e.AnnotationHandle.Length > 16 || e.AnnotationHandle.Any(c => !Uri.IsHexDigit(c)))
            throw new ArgumentException("Invalid leader style or annotation handle.");
    }
    public static ImmutableArray<Entity> For(LeaderEntity e) => Cache.GetValue(e, static v => new(() => Build(v))).Value;
    private static ImmutableArray<Entity> Build(LeaderEntity e)
    {
        Validate(e); var items = ImmutableArray.CreateBuilder<Entity>();
        var vertices = e.Vertices;
        if (e.Hookline && e.AnnotationType == 0 && e.TextAbove && e.TextWidth > 0)
            vertices = vertices.Add(vertices[^1] + e.Horizontal * ((e.HooklineReversed ? -1 : 1) * (e.TextWidth + e.Gap)));
        if (vertices.Any(p => !p.IsFinite)) throw new ArgumentException("Leader hookline exceeds the coordinate range.");
        items.Add(new Polyline3DEntity(vertices));
        if (e.ArrowEnabled && e.ArrowSize > 0)
        {
            var axis = (vertices[1] - vertices[0]).Normalized;
            var side = e.Normal.Normalized.Cross(axis);
            if (side.Length < 1e-10) side = (Math.Abs(axis.Z) < .9 ? Vec3.UnitZ : Vec3.UnitY).Cross(axis);
            side = side.Normalized * (e.ArrowSize / 6);
            var back = vertices[0] + axis * e.ArrowSize;
            if (!(back + side).IsFinite || !(back - side).IsFinite) throw new ArgumentException("Leader arrow exceeds the coordinate range.");
            items.Add(new MeshEntity([vertices[0], back + side, back - side], [0,1,2], "Dimension arrow"));
        }
        return items.ToImmutable();
    }
    /// <summary>Similarity transforms preserve native leader sizes; other transforms need a placed native project.</summary>
    public static LeaderEntity Transform(LeaderEntity e, Transform3 t)
    {
        Validate(e); var s = t.X.Length;
        if (!double.IsFinite(s) || s < 1e-12 || Math.Abs(t.Y.Length-s) > s*1e-8 || Math.Abs(t.Z.Length-s) > s*1e-8 ||
            Math.Abs(t.X.Dot(t.Y)) > s*s*1e-8 || Math.Abs(t.X.Dot(t.Z)) > s*s*1e-8 || Math.Abs(t.Y.Dot(t.Z)) > s*s*1e-8)
            throw new NotSupportedException("Native LEADER export requires a uniform orthogonal transform. Save a native project to retain other placements.");
        var result = e with { Vertices = e.Vertices.Select(t.Point).ToImmutableArray(), Normal = t.Vector(e.Normal).Normalized,
            Horizontal = t.Vector(e.Horizontal) / s, ArrowSize = e.ArrowSize*s, Gap = e.Gap*s, TextWidth = e.TextWidth*s, TextHeight = e.TextHeight*s,
            BlockOffset = t.Vector(e.BlockOffset), AnnotationOffset = t.Vector(e.AnnotationOffset) };
        Validate(result); return result;
    }
}
