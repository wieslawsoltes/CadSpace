using System.Collections.Immutable;
using CadSpace.Geometry;
using CadSpace.Model;

namespace CadSpace.Engine;

/// <summary>Analytic-boundary hatch authoring, staged fill edits and boundary extraction.</summary>
public static class HatchEditing
{
    public static HatchRegionEntity? Unwrap(Entity e)
    {
        var (geometry, _) = PolylineTools.Unwrap(e);
        if (geometry is HatchRegionEntity h) return h;
        if (geometry is not HatchEntity old) return null;
        return new([old.Boundary.Select(p => new PolyVertex(p)).ToImmutableArray()], old.Solid,
            Pattern(old.Angle, old.Spacing, default), old.Solid ? "SOLID" : "USER") { Id = old.Id };
    }
    public static ImmutableArray<HatchPatternLine> Pattern(double angle, double spacing, Vec3 origin, bool cross = false)
    {
        if (!double.IsFinite(angle) || !double.IsFinite(spacing) || spacing < 1e-8 || spacing > 1e12 || !origin.IsFinite)
            throw new ArgumentException("Use a finite angle/origin and spacing from 1e-8 to 1e12.");
        var a = new HatchPatternLine(angle, origin, Transform3.RotationZ(angle).Vector(new(0, spacing)), []);
        return cross ? [a, new(angle + 90, origin, Transform3.RotationZ(angle + 90).Vector(new(0, spacing)), [])] : [a];
    }
    public static Entity FromBoundaries(IReadOnlyList<Entity> roots, bool solid = false, HatchGradient? gradient = null)
    {
        if (roots.Count is < 1 or > 1024) throw new ArgumentException("Select 1–1,024 closed polylines or circles.");
        var frame = PolylineTools.Unwrap(roots[0]).Placement;
        var loops = ImmutableArray.CreateBuilder<ImmutableArray<PolyVertex>>();
        foreach (var root in roots)
        {
            var (geometry, placement) = PolylineTools.Unwrap(root);
            if (placement != frame || root.Layout != roots[0].Layout) throw new ArgumentException("Hatch boundaries must share a layout and local plane.");
            loops.Add(geometry switch {
                PolylineEntity { Closed: true } p when !p.HasWidth => p.Vertices,
                CircleEntity c => [new(c.Center - Vec3.UnitX * c.Radius, 1), new(c.Center + Vec3.UnitX * c.Radius, 1)],
                _ => throw new NotSupportedException("Use closed centerline polylines or circles; wide outlines require explicit boundaries.")
            });
        }
        var elevation = loops[0][0].Position.Z;
        if (loops.SelectMany(p => p).Any(p => Math.Abs(p.Position.Z - elevation) > 1e-7)) throw new ArgumentException("Hatch boundaries must be coplanar.");
        var hatch = new HatchRegionEntity(loops.ToImmutable(), solid || gradient != null, solid || gradient != null ? [] : Pattern(45, 10, default), solid || gradient != null ? "SOLID" : "USER") { Gradient = gradient };
        AdvancedGeometry.Validate(hatch);
        return frame == Transform3.Identity ? hatch : new PlacedEntity(hatch, frame);
    }
    public static void CreateHatch(this CadSession session, bool solid = false, HatchGradient? gradient = null) =>
        session.Add("Hatch", FromBoundaries(session.EditableSelection(), solid, gradient));

    public static void SetFill(this CadSession session, Entity expected, bool solid, ImmutableArray<HatchPatternLine> pattern, string patternName, int islands, HatchGradient? gradient)
    {
        if (!session.EditableSelection().Any(e => ReferenceEquals(e, expected))) throw new InvalidOperationException("The hatch or selection changed. Reopen the editor.");
        var h = Unwrap(expected) ?? throw new ArgumentException("Select a hatch.");
        if (string.IsNullOrWhiteSpace(patternName) || patternName.Length > 255 || patternName.IndexOfAny(['\r','\n','\0']) >= 0) throw new ArgumentException("Invalid pattern name.");
        if (!solid && pattern.IsEmpty) throw new ArgumentException("A patterned hatch needs at least one line family.");
        var next = h with { Solid = solid, Pattern = pattern, PatternName = patternName, IslandStyle = islands, Gradient = gradient };
        AdvancedGeometry.Validate(next);
        var samePattern = pattern.Length == h.Pattern.Length && pattern.Zip(h.Pattern).All(p =>
            p.First with { Dashes = p.Second.Dashes } == p.Second && p.First.Dashes.SequenceEqual(p.Second.Dashes));
        if (samePattern && next with { Pattern = h.Pattern } == h) return;
        Entity Replace(Entity e) => e is PlacedEntity p ? p with { Geometry = Replace(p.Geometry) } : next with {
            Id = e.Id, Handle = e.Handle, Layer = e.Layer, Layout = e.Layout, Visible = e.Visible, ColorIndex = e.ColorIndex,
            TrueColor = e.TrueColor, LineWeight = e.LineWeight, Linetype = e.Linetype, LinetypeScale = e.LinetypeScale };
        var changed = Replace(expected);
        session.Document.Edit("Hatch properties", d => d with { Entities = d.Entities.Select(e => e.Id == expected.Id ? changed : e).ToImmutableArray() });
    }
    public static void ExtractBoundaries(this CadSession session)
    {
        var output = new List<Entity>();
        foreach (var root in session.EditableSelection())
        {
            var h = Unwrap(root) ?? throw new ArgumentException("Select hatches only.");
            var frame = PolylineTools.Unwrap(root).Placement;
            foreach (var loop in h.Loops)
            {
                Entity p = new PolylineEntity(loop, true);
                if (frame != Transform3.Identity) p = new PlacedEntity(p, frame);
                output.Add(p with { Layer = root.Layer, Layout = root.Layout, ColorIndex = root.ColorIndex, TrueColor = root.TrueColor });
                if (output.Count > 10000) throw new ArgumentException("Boundary extraction exceeds 10,000 loops.");
            }
        }
        session.Document.Add("Extract hatch boundaries", output.ToArray());
    }
}
