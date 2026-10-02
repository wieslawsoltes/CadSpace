using CadSpace.Geometry;
using CadSpace.Model;

namespace CadSpace.Engine;

public sealed partial class CommandEngine
{
    private bool TrySubmitHatch(string input)
    {
        if (!IsActive)
        {
            var name = input.ToUpperInvariant() switch {
                "H" => "HATCH", "GD" => "GRADIENT", "HE" => "HATCHEDIT", "HGB" => "HATCHGENERATEBOUNDARY", _ => input.ToUpperInvariant()
            };
            if (name is not ("HATCH" or "GRADIENT" or "HATCHEDIT" or "HATCHGENERATEBOUNDARY")) return false;
            _active = name; Message?.Invoke("Command: " + name);
            switch (name)
            {
                case "HATCH": Session.CreateHatch(); Cancel(); break;
                case "GRADIENT": Session.CreateHatch(gradient: new("LINEAR", 0xff387bc4, 0xffe6ba67)); Cancel(); break;
                case "HATCHGENERATEBOUNDARY": Session.ExtractBoundaries(); Cancel(); break;
                default:
                    if (Session.Selection.Count > 0) OpenHatchEditor();
                    else { Prompt = "Select a hatch to edit; Enter cancels"; Changed?.Invoke(); }
                    break;
            }
            return true;
        }
        if (_active != "HATCHEDIT") return false;
        if (input.Length == 0) { Cancel(); return true; }
        if (!GeometryMath.TryParsePoint(input, default, out var point)) throw new ArgumentException("Pick a hatch or enter x,y,z.");
        TryHatchPoint(point); return true;
    }
    private bool TryHatchPoint(Vec3 point)
    {
        if (_active != "HATCHEDIT") return false;
        var id = Session.HitTest(point, PickTolerance);
        var entity = Session.Document.Drawing.Entities.FirstOrDefault(e => e.Id == id);
        if (entity == null || HatchEditing.Unwrap(entity) == null) throw new ArgumentException("No modeled hatch at that point.");
        Session.Select(id); OpenHatchEditor(); return true;
    }
    private void OpenHatchEditor()
    {
        var selected = Session.EditableSelection();
        if (selected.Length != 1 || HatchEditing.Unwrap(selected[0]) == null) throw new ArgumentException("Select exactly one modeled hatch.");
        Cancel(); ViewRequested?.Invoke("HATCHEDITOR");
    }
}
