using CadSpace.Geometry;
using CadSpace.Model;

namespace CadSpace.Engine;

public sealed partial class CommandEngine
{
    private bool TrySubmitAnnotation(string input)
    {
        if (!IsActive)
        {
            var name = input.ToUpperInvariant() switch { "DDEDIT" or "ED" or "TEXTEDIT" => "DDEDIT", "EATTEDIT" or "ATE" => "EATTEDIT", _ => "" };
            if (name.Length == 0) return false;
            _active = name; Message?.Invoke("Command: " + name);
            var selected = Session.SelectedEntities();
            if (selected.Length == 1) { OpenAnnotation(selected[0]); return true; }
            if (selected.Length > 1) throw new ArgumentException("Select one text object or attributed block, not multiple objects.");
            Prompt = name == "EATTEDIT" ? "Select an attributed block" : "Select text or an attributed block; Enter cancels";
            Changed?.Invoke(); return true;
        }
        if (_active is not ("DDEDIT" or "EATTEDIT")) return false;
        if (input.Length == 0) { Cancel(); return true; }
        if (!GeometryMath.TryParsePoint(input, default, out var point)) throw new ArgumentException("Pick an object or enter x,y coordinates.");
        TryAnnotationPoint(point); return true;
    }
    private bool TryAnnotationPoint(Vec3 point)
    {
        if (_active is not ("DDEDIT" or "EATTEDIT")) return false;
        var id = Session.HitTest(point, PickTolerance) ?? throw new ArgumentException("No annotation at this location.");
        var target = Session.Document.Drawing.Entities.First(e => e.Id == id);
        // Validate before modifying selection, so unrelated geometry is not silently selected for editing.
        if (!TextEditing.IsEditable(target) || _active == "EATTEDIT" && target is not CompositeEntity { DxfType: "INSERT" })
            throw new ArgumentException("Choose supported text or an attributed block.");
        Session.Select(id); OpenAnnotation(target); return true;
    }
    private void OpenAnnotation(Entity entity)
    {
        if (!TextEditing.IsEditable(entity) || _active == "EATTEDIT" && entity is not CompositeEntity { DxfType: "INSERT" })
            throw new ArgumentException("Choose supported text or an attributed block.");
        Session.EditableSelection();
        var request = entity is CompositeEntity ? "ATTRIBUTEEDITOR" : "TEXTEDITOR";
        // Finish the command before opening a modal editor; it must not retain a point-input state.
        Cancel(); ViewRequested?.Invoke(request);
    }
}
