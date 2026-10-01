using System.Collections.Immutable;
using CadSpace.Geometry;
using CadSpace.Model;

namespace CadSpace.Engine;

/// <summary>Bounded control-point edits and shape-preserving rational knot insertion.</summary>
public static class SplineEditing
{
    public static SplineEntity? Unwrap(Entity root)
    {
        for (var depth = 0; root is PlacedEntity p; depth++)
        { if (depth >= 32) return null; root = p.Geometry; }
        return root as SplineEntity;
    }
    private static void ValidateEditable(SplineEntity spline)
    {
        AdvancedGeometry.ValidateSpline(spline);
        if (spline.Periodic) throw new NotSupportedException("Periodic splines require coordinated seam editing; this editor retains them without changing their control structure.");
    }
    public static SplineEntity SetControlPoint(SplineEntity spline, int index, Vec3 point, double weight)
    {
        ValidateEditable(spline);
        if (index < 0 || index >= spline.ControlPoints.Length) throw new ArgumentOutOfRangeException(nameof(index));
        if (!point.IsFinite || !double.IsFinite(weight) || weight <= 0) throw new ArgumentException("Use finite coordinates and a positive finite weight.");
        var oldWeight = spline.Weights.IsEmpty ? 1 : spline.Weights[index];
        if (spline.ControlPoints[index] == point && oldWeight == weight) return spline;
        // Closed clamped curves share their end position; do not silently turn a closed curve into an open one.
        if (spline.Closed && point != spline.ControlPoints[index] && (index == 0 || index == spline.ControlPoints.Length - 1))
            throw new NotSupportedException("Move a closed spline's shared endpoints with a coordinated geometry operation, not a single control-point edit.");
        var weights = spline.Weights;
        if (oldWeight != weight)
        {
            if (weights.IsEmpty) weights = Enumerable.Repeat(1d, spline.ControlPoints.Length).ToImmutableArray();
            weights = weights.SetItem(index, weight);
        }
        return spline with { ControlPoints = spline.ControlPoints.SetItem(index, point), Weights = weights };
    }
    /// <summary>Insert one interior knot without changing the rational curve, up to floating-point rounding.</summary>
    public static SplineEntity InsertKnot(SplineEntity spline, double parameter)
    {
        ValidateEditable(spline);
        var p = spline.Degree; var count = spline.ControlPoints.Length; var knots = spline.Knots;
        if (count >= 100000) throw new ArgumentException("Knot insertion would exceed 100,000 control points.");
        if (!double.IsFinite(parameter) || parameter <= knots[p] || parameter >= knots[count])
            throw new ArgumentOutOfRangeException(nameof(parameter), "Choose a knot strictly inside the active parameter domain.");
        var low = p; var high = count;
        while (high - low > 1) { var middle = (low + high) / 2; if (parameter < knots[middle]) high = middle; else low = middle; }
        var span = low; var multiplicity = 0;
        for (var i = span; i >= 0 && knots[i] == parameter; i--) multiplicity++;
        if (multiplicity >= p) throw new ArgumentException("The interior knot already has degree-many repetitions.");
        var points = ImmutableArray.CreateBuilder<Vec3>(count + 1); points.Count = count + 1;
        var weighted = !spline.Weights.IsEmpty;
        var weights = weighted ? ImmutableArray.CreateBuilder<double>(count + 1) : null;
        if (weights != null) weights.Count = count + 1;
        void Copy(int from, int to) { points[to] = spline.ControlPoints[from]; if (weights != null) weights[to] = spline.Weights[from]; }
        for (var i = 0; i <= span - p; i++) Copy(i, i);
        for (var i = span - multiplicity; i < count; i++) Copy(i, i + 1);
        for (var i = span - p + 1; i <= span - multiplicity; i++)
        {
            var denominator = knots[i + p] - knots[i];
            var alpha = (parameter - knots[i]) / denominator;
            if (!double.IsFinite(denominator))
            {
                // Rescale only when subtraction overflows; nearby large knots keep their original precision.
                var scale = Math.Max(Math.Abs(knots[i]), Math.Abs(knots[i + p]));
                alpha = (parameter / scale - knots[i] / scale) / (knots[i + p] / scale - knots[i] / scale);
            }
            if (!double.IsFinite(alpha) || alpha < 0 || alpha > 1) throw new ArgumentException("The knot interval cannot be refined at this numeric precision.");
            var fraction = alpha;
            if (weights != null)
            {
                var a = spline.Weights[i - 1]; var b = spline.Weights[i]; var scale = Math.Max(a, b);
                var left = (a / scale) * (1 - alpha); var right = (b / scale) * alpha; var sum = left + right;
                weights[i] = scale * sum; fraction = right / sum;
                if (!double.IsFinite(weights[i]) || weights[i] <= 0 || !double.IsFinite(fraction))
                    throw new ArgumentException("Refined homogeneous weights are outside the numeric range.");
            }
            // Convex interpolation avoids forming the potentially overflowing difference b-a.
            points[i] = spline.ControlPoints[i - 1] * (1 - fraction) + spline.ControlPoints[i] * fraction;
            if (!points[i].IsFinite) throw new ArgumentException("Refined coordinates are outside the numeric range.");
        }
        var result = spline with { ControlPoints = points.MoveToImmutable(), Knots = knots.Insert(span + 1, parameter),
            Weights = weights?.MoveToImmutable() ?? ImmutableArray<double>.Empty };
        AdvancedGeometry.ValidateSpline(result); return result;
    }
    /// <summary>Commit one validated local spline edit. A stale capture never overwrites a newer object.</summary>
    public static void EditSpline(this CadSession session, Entity expected, Func<SplineEntity, SplineEntity> edit)
    {
        ArgumentNullException.ThrowIfNull(edit);
        if (!session.EditableSelection().Any(e => ReferenceEquals(e, expected))) throw new InvalidOperationException("The spline or selection changed. Reopen the editor.");
        var before = Unwrap(expected) ?? throw new ArgumentException("Select one spline.");
        ValidateEditable(before); var after = edit(before) ?? throw new ArgumentException("The spline edit returned no geometry.");
        if (after.Id != before.Id || after.Handle != before.Handle) throw new ArgumentException("A spline edit must retain its identity and handle.");
        AdvancedGeometry.ValidateSpline(after); if (after == before) return;
        Entity Replace(Entity root) => root is PlacedEntity p ? p with { Geometry = Replace(p.Geometry) } : after;
        var replacement = Replace(expected);
        session.Document.Edit("Edit spline", drawing => drawing with { Entities = drawing.Entities.Select(e => e.Id == expected.Id ? replacement : e).ToImmutableArray() });
    }
}
