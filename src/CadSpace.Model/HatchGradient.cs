using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using CadSpace.Geometry;

namespace CadSpace.Model;

/// <summary>Native two-stop DXF gradient metadata. Angles use degrees in the model, radians in DXF.</summary>
public sealed record HatchGradient(string Name, uint FirstColor, uint SecondColor, double Angle = 0,
    double Shift = 0, bool SingleColor = false, double Tint = 0)
{
    public static ImmutableArray<string> Names { get; } = ["LINEAR", "CYLINDER", "INVCYLINDER", "SPHERICAL", "INVSPHERICAL", "HEMISPHERICAL", "INVHEMISPHERICAL", "CURVED", "INVCURVED"];
    public void Validate()
    {
        if (!Names.Contains(Name) || !double.IsFinite(Angle) || !double.IsFinite(Shift) || Shift is < 0 or > 1 ||
            !double.IsFinite(Tint) || Tint is < 0 or > 1 || FirstColor >> 24 != 255 || SecondColor >> 24 != 255)
            throw new ArgumentException("Invalid gradient name, opaque RGB stops, rotation, shift or tint.");
    }
}

public readonly record struct HatchColorTriangle(Vec3 A, Vec3 B, Vec3 C, uint ColorA, uint ColorB, uint ColorC);

/// <summary>Bounded, cached color tessellation. Nonlinear native gradient profiles are a display approximation.</summary>
public static class HatchGradientGeometry
{
    public const int MaximumTriangles = 32768;
    private static readonly ConditionalWeakTable<HatchRegionEntity, Lazy<ImmutableArray<HatchColorTriangle>>> Cache = new();
    public static ImmutableArray<HatchColorTriangle> For(HatchRegionEntity hatch) =>
        Cache.GetValue(hatch, h => new(() => Build(h))).Value;

    private static ImmutableArray<HatchColorTriangle> Build(HatchRegionEntity hatch)
    {
        AdvancedGeometry.Validate(hatch);
        var g = hatch.Gradient ?? throw new ArgumentException("A gradient is required.");
        var loops = AdvancedGeometry.HatchLoops(hatch);
        if (hatch.IslandStyle != 0)
        {
            var original = loops;
            loops = original.Where((loop, i) => original.Where((other, j) => i != j && GeometryMath.PointInPolygon(loop[0], other)).Count() <= (hatch.IslandStyle == 1 ? 1 : 0)).ToImmutableArray();
        }
        var axis = GeometryMath.OnCircle(default, 1, g.Angle); var up = new Vec3(-axis.Y, axis.X);
        var center = Bounds3.From(loops.SelectMany(p => p)).Center;
        var projected = loops.SelectMany(p => p).Select(p => new Vec3((p - center).Dot(axis), (p - center).Dot(up))).ToArray();
        var box = Bounds3.From(projected); var width = Math.Max(1e-12, box.Size.X); var height = Math.Max(1e-12, box.Size.Y);
        double Value(Vec3 point)
        {
            var delta = point - center;
            var x = Math.Clamp(((delta.Dot(axis) - box.Min.X) / width), 0, 1);
            var y = Math.Clamp(((delta.Dot(up) - box.Min.Y) / height), 0, 1);
            var u = 2 * x - 1; var v = 2 * y - 1;
            var t = g.Name switch {
                "LINEAR" => x,
                "CYLINDER" or "INVCYLINDER" => Math.Sqrt(Math.Max(0, 1 - u * u)),
                "SPHERICAL" or "INVSPHERICAL" => Math.Max(0, 1 - Math.Sqrt(u * u + v * v)),
                "HEMISPHERICAL" or "INVHEMISPHERICAL" => Math.Sqrt(Math.Max(0, 1 - u * u - v * v)),
                _ => (x + y) / 2
            };
            if (g.Name.StartsWith("INV", StringComparison.Ordinal)) t = 1 - t;
            return Math.Clamp(t * (1 - g.Shift) + Math.Abs(2 * t - 1) * g.Shift, 0, 1);
        }
        uint Color(double t)
        {
            uint Channel(int shift) => (uint)Math.Round(((g.FirstColor >> shift) & 255) * (1 - t) + ((g.SecondColor >> shift) & 255) * t);
            return 0xff000000 | Channel(16) << 16 | Channel(8) << 8 | Channel(0);
        }
        var triangles = ImmutableArray.CreateBuilder<HatchColorTriangle>();
        void Add(Vec3 a, Vec3 b, Vec3 c, int depth)
        {
            var ta = Value(a); var tb = Value(b); var tc = Value(c);
            var ab = (a + b) / 2; var bc = (b + c) / 2; var ca = (c + a) / 2;
            var error = Math.Max(Math.Abs(Value(ab) - (ta + tb) / 2), Math.Max(Math.Abs(Value(bc) - (tb + tc) / 2), Math.Abs(Value(ca) - (tc + ta) / 2)));
            error = Math.Max(error, Math.Abs(Value((a + b + c) / 3) - (ta + tb + tc) / 3));
            if (depth < 7 && error > 1.0 / 128)
            { Add(a, ab, ca, depth + 1); Add(ab, b, bc, depth + 1); Add(ca, bc, c, depth + 1); Add(ab, bc, ca, depth + 1); return; }
            if (triangles.Count >= MaximumTriangles) throw new ArgumentException("Gradient exceeds the 32,768-triangle display budget.");
            triangles.Add(new(a, b, c, Color(ta), Color(tb), Color(tc)));
        }
        foreach (var mesh in PolygonBands.Fill(loops))
            for (var i = 0; i < mesh.Triangles.Length; i += 3)
                Add(mesh.Vertices[mesh.Triangles[i]], mesh.Vertices[mesh.Triangles[i + 1]], mesh.Vertices[mesh.Triangles[i + 2]], 0);
        return triangles.ToImmutable();
    }
}
