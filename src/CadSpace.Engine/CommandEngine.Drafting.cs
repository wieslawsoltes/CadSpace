using CadSpace.Geometry;
using CadSpace.Model;

namespace CadSpace.Engine;

public sealed partial class CommandEngine
{
    private int _draftStage, _polygonSides;
    private double _innerDiameter, _outerDiameter;
    private bool _circumscribed;
    private Guid[] _matchDestinations = [];
    private Drawing? _matchDrawing;
    private bool TrySubmitDrafting(string input)
    {
        if (!IsActive)
        {
            var command = input.ToUpperInvariant() switch { "POL" or "POLYGON" => "POLYGON", "DO" or "DONUT" => "DONUT", "MA" or "MATCHPROP" => "MATCHPROP", _ => "" };
            if (command.Length == 0) return false;
            _active = command; _draftStage = 0; _points.Clear(); _circumscribed = false;
            if (command == "MATCHPROP") { _matchDestinations = Session.EditableSelection().Select(e => e.Id).ToArray(); _matchDrawing = Session.Document.Drawing; }
            Message?.Invoke("Command: " + command); DraftPrompt(); return true;
        }
        if (_active is not ("POLYGON" or "DONUT" or "MATCHPROP")) return false;
        if (input.Length == 0) { Cancel(); return true; }
        if (_active == "POLYGON" && _draftStage == 2)
        {
            _circumscribed = input.ToUpperInvariant() switch { "I" or "INSCRIBED" => false, "C" or "CIRCUMSCRIBED" => true, _ => throw new ArgumentException("Choose Inscribed (I) or Circumscribed (C).") };
            _draftStage = 3; DraftPrompt(); return true;
        }
        if (GeometryMath.TryParsePoint(input, ReferencePoint ?? default, out var point)) { TryDraftPoint(point); return true; }
        if (!GeometryMath.Number(input, out var value)) throw new ArgumentException("Enter the prompted number or coordinate.");
        if (_active == "POLYGON" && _draftStage == 0)
        {
            if (value < 3 || value > 1024 || value != Math.Truncate(value)) throw new ArgumentException("Enter an integer from 3 to 1024.");
            _polygonSides = (int)value; _draftStage = 1;
        }
        else if (_active == "POLYGON" && _draftStage == 3)
        { Session.Add("Polygon", PolylineTools.Polygon(_polygonSides, _points[0], value, _circumscribed) with { ConstantWidth = Session.CurrentPolylineWidth }); Cancel(); return true; }
        else if (_active == "DONUT" && _draftStage == 0)
        { if (value < 0) throw new ArgumentException("The inner diameter cannot be negative."); _innerDiameter = value; _draftStage = 1; }
        else if (_active == "DONUT" && _draftStage == 1)
        { if (value <= _innerDiameter) throw new ArgumentException("The outer diameter must exceed the inner diameter."); _outerDiameter = value; _draftStage = 2; }
        else throw new ArgumentException("A coordinate is required at this prompt.");
        DraftPrompt(); return true;
    }
    private bool TryDraftPoint(Vec3 point)
    {
        if (_active is not ("POLYGON" or "DONUT" or "MATCHPROP")) return false;
        if (_active == "POLYGON" && _draftStage == 1) { _points.Add(point); _draftStage = 2; }
        else if (_active == "POLYGON" && _draftStage == 3)
        { Session.Add("Polygon", PolylineTools.Polygon(_polygonSides, _points[0], new Vec3(point.X - _points[0].X, point.Y - _points[0].Y).Length, _circumscribed) with { ConstantWidth = Session.CurrentPolylineWidth }); Cancel(); return true; }
        else if (_active == "DONUT" && _draftStage == 2) Session.Add("Donut", PolylineTools.Donut(point, _innerDiameter, _outerDiameter));
        else if (_active == "MATCHPROP")
        {
            if (!ReferenceEquals(_matchDrawing, Session.Document.Drawing)) throw new InvalidOperationException("The drawing changed during Match Properties; select destinations again.");
            var id = Session.HitTest(point, PickTolerance) ?? throw new ArgumentException("No source object at the picked location.");
            Session.MatchProperties(id, _matchDestinations); Cancel(); return true;
        }
        else { Message?.Invoke(Prompt); return true; }
        DraftPrompt(); return true;
    }
    private void DraftPrompt()
    {
        Prompt = _active switch {
            "MATCHPROP" => "Pick source object; common properties will be copied to the preselected destinations",
            "POLYGON" => _draftStage switch { 0 => "Enter number of sides (3–1024)", 1 => "Specify polygon center", 2 => "Choose Inscribed (I) or Circumscribed (C)", _ => "Specify radius or radius point" },
            _ => _draftStage switch { 0 => "Specify inside diameter (0 for a filled disc)", 1 => "Specify outside diameter", _ => "Specify donut center; Enter finishes, each placement is undoable" }
        };
        Changed?.Invoke();
    }
    private IReadOnlyList<Entity>? DraftPreview(Vec3 point)
    {
        if (_active == "POLYGON" && _draftStage == 3)
        {
            var radius = new Vec3(point.X - _points[0].X, point.Y - _points[0].Y).Length;
            return radius > 1e-9 ? [PolylineTools.Polygon(_polygonSides, _points[0], radius, _circumscribed) with { ConstantWidth = Session.CurrentPolylineWidth }] : [];
        }
        if (_active == "DONUT" && _draftStage == 2) return [PolylineTools.Donut(point, _innerDiameter, _outerDiameter)];
        return _active is "POLYGON" or "DONUT" or "MATCHPROP" ? [] : null;
    }
}
