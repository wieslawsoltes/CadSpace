using CadSpace.Geometry;
using CadSpace.Model;

namespace CadSpace.Engine;

public sealed partial class CommandEngine
{
    private bool TrySubmitSpline(string input)
    {
        if (!IsActive)
        {
            if (!input.Equals("SPLINEDIT", StringComparison.OrdinalIgnoreCase) && !input.Equals("SPE", StringComparison.OrdinalIgnoreCase)) return false;
            _active = "SPLINEDIT"; Message?.Invoke("Command: SPLINEDIT");
            if (Session.Selection.Count > 0) OpenSplineEditor();
            else { Prompt = "Select a spline to edit; Enter cancels"; Changed?.Invoke(); }
            return true;
        }
        if (_active != "SPLINEDIT") return false;
        if (input.Length == 0) { Cancel(); return true; }
        if (!GeometryMath.TryParsePoint(input, default, out var point)) throw new ArgumentException("Pick a spline or enter x,y,z.");
        TrySplinePoint(point); return true;
    }
    private bool TrySplinePoint(Vec3 point)
    {
        if (_active != "SPLINEDIT") return false;
        var id = Session.HitTest(point, PickTolerance);
        var entity = Session.Document.Drawing.Entities.FirstOrDefault(e => e.Id == id);
        if (entity == null || SplineEditing.Unwrap(entity) == null) throw new ArgumentException("No modeled spline at that point.");
        Session.Select(id); OpenSplineEditor(); return true;
    }
    private void OpenSplineEditor()
    {
        var selected = Session.EditableSelection();
        if (selected.Length != 1 || SplineEditing.Unwrap(selected[0]) == null) throw new ArgumentException("Select exactly one spline.");
        Cancel(); ViewRequested?.Invoke("SPLINEEDITOR");
    }
}
