using CadSpace.Geometry;
using CadSpace.Model;

namespace CadSpace.Engine;

public sealed partial class CommandEngine
{
    private Entity? _dimensionCircle;
    private static bool IsDimensionCommand(string name) => name is "DIMLINEAR" or "DIMROTATED" or "DIMANGULAR" or "DIMANGULAR2" or "DIMORDINATE" or "DIMRADIUS" or "DIMDIAMETER" or "DIMEDIT";
    private bool TrySubmitDimension(string input)
    {
        if (!IsActive)
        {
            var name = input.ToUpperInvariant() switch { "DLI" => "DIMLINEAR", "DROT" => "DIMROTATED", "DAN" => "DIMANGULAR", "DA2" => "DIMANGULAR2", "DOR" => "DIMORDINATE", "DRA" => "DIMRADIUS", "DDI" => "DIMDIAMETER", "DED" => "DIMEDIT", _ => input.ToUpperInvariant() };
            if (!IsDimensionCommand(name)) return false;
            _active = name; _points.Clear(); _text = ""; _dimensionCircle = null; Message?.Invoke("Command: " + name);
            if (name == "DIMEDIT" && Session.Selection.Count > 0) { OpenDimensionEditor(); return true; }
            if (name is "DIMRADIUS" or "DIMDIAMETER" && Session.Selection.Count == 1)
            {
                var selected = Session.SelectedEntities()[0]; if (CircleData(selected) != null) _dimensionCircle = selected;
            }
            DimensionPrompt(); return true;
        }
        if (!IsDimensionCommand(_active)) return false;
        if (input.Length == 0) { Cancel(); return true; }
        if (_active == "DIMROTATED" && _text.Length == 0)
        {
            if (!GeometryMath.Number(input, out _radius)) throw new ArgumentException("Enter a rotation in degrees.");
            _text = "ANGLE"; DimensionPrompt(); return true;
        }
        if (_active == "DIMORDINATE" && _text.Length == 0)
        {
            if (!input.Equals("X", StringComparison.OrdinalIgnoreCase) && !input.Equals("Y", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Choose X or Y ordinate.");
            _text = input.ToUpperInvariant(); DimensionPrompt(); return true;
        }
        if (_active == "DIMLINEAR" && input.ToUpperInvariant() is "H" or "HORIZONTAL" or "V" or "VERTICAL")
        { _text = input.ToUpperInvariant().StartsWith('V') ? "V" : "H"; DimensionPrompt(); return true; }
        if (!GeometryMath.TryParsePoint(input, ReferencePoint ?? default, out var point)) throw new ArgumentException("Specify a point as x,y or x,y,z.");
        TryDimensionPoint(point); return true;
    }
    private void OpenDimensionEditor()
    {
        var selected = Session.EditableSelection();
        if (selected.Length != 1 || DimensionGeometry.Unwrap(selected[0]) == null) throw new ArgumentException("Select one modeled dimension.");
        Cancel(); ViewRequested?.Invoke("DIMENSIONEDITOR");
    }
    private static (Vec3 Center, double Radius, Transform3 Plane)? CircleData(Entity root)
    {
        var t = Transform3.Identity; var e = root; var depth = 0;
        while (e is PlacedEntity p) { if (++depth > 32) return null; t = p.Placement.Then(t); e = p.Geometry; }
        if (e is not (CircleEntity or ArcEntity)) return null;
        if (Math.Abs(t.X.Length - t.Y.Length) > 1e-8 * t.X.Length || Math.Abs(t.X.Dot(t.Y)) > 1e-8 * t.X.Length * t.Y.Length)
            throw new NotSupportedException("A nonuniformly scaled circle has no single native dimension radius.");
        return e is CircleEntity c ? (c.Center, c.Radius, t) : (((ArcEntity)e).Center, ((ArcEntity)e).Radius, t);
    }
    private Entity RadialDimension(Vec3 point)
    {
        if (!Session.Document.Drawing.Entities.Any(e => ReferenceEquals(e, _dimensionCircle))) throw new InvalidOperationException("The measured circle changed; restart the command.");
        var c = CircleData(_dimensionCircle!)!.Value; var local = Coordinates3D.Inverse(c.Plane).Point(point); local = local with { Z = c.Center.Z };
        var v = local - c.Center; if (v.Length < 1e-9) throw new ArgumentException("Place dimension text away from the center.");
        var rim = c.Center + v.Normalized * c.Radius;
        var d = new DimensionEntity(_active == "DIMRADIUS" ? c.Center : c.Center - v.Normalized * c.Radius, rim, local)
            { Type = _active == "DIMRADIUS" ? DimensionKind.Radius : DimensionKind.Diameter };
        return c.Plane == Transform3.Identity ? d : new PlacedEntity(d, c.Plane);
    }
    private bool TryDimensionPoint(Vec3 point)
    {
        if (!IsDimensionCommand(_active)) return false;
        if (_active == "DIMEDIT")
        {
            var hit = Session.HitTest(point, PickTolerance); var entity = Session.Document.Drawing.Entities.FirstOrDefault(e => e.Id == hit);
            if (entity == null || DimensionGeometry.Unwrap(entity) == null) throw new ArgumentException("No modeled dimension at that point.");
            Session.Select(hit); OpenDimensionEditor(); return true;
        }
        if (_active is "DIMRADIUS" or "DIMDIAMETER")
        {
            if (_dimensionCircle == null)
            {
                var id = Session.HitTest(point, PickTolerance); var picked = Session.Document.Drawing.Entities.FirstOrDefault(e => e.Id == id);
                if (picked == null || CircleData(picked) == null) throw new ArgumentException("Pick a circle or circular arc.");
                _dimensionCircle = picked; DimensionPrompt(); return true;
            }
            Session.Add("Dimension", RadialDimension(point)); Cancel(); return true;
        }
        if (_active is "DIMROTATED" or "DIMORDINATE" && _text.Length == 0) { Message?.Invoke(Prompt); return true; }
        _points.Add(point); var count = _active == "DIMANGULAR2" ? 5 : _active == "DIMANGULAR" ? 4 : 3;
        if (_points.Count == count) { Session.Add("Dimension", CreateDimension(_points)); Cancel(); } else DimensionPrompt(); return true;
    }
    private DimensionEntity CreateDimension(IReadOnlyList<Vec3> points) => _active switch
    {
        "DIMLINEAR" or "DIMROTATED" => new(points[0], points[1], points[2]) { Type = DimensionKind.Rotated, Rotation = _active == "DIMROTATED" ? _radius : _text == "V" ? 90 : 0 },
        "DIMANGULAR" => new(points[1], points[2], points[3]) { Type = DimensionKind.Angular3Point, Third = points[0] },
        "DIMANGULAR2" => new(points[0], points[1], points[4]) { Type = DimensionKind.Angular2Line, Third = points[2], Fourth = points[3] },
        _ => new(points[0], points[1], points[2]) { Type = DimensionKind.Ordinate, OrdinateX = _text == "X" }
    };
    private IReadOnlyList<Entity>? DimensionPreview(Vec3 point)
    {
        if (!IsDimensionCommand(_active)) return null;
        try
        {
            if (_active is "DIMRADIUS" or "DIMDIAMETER") return _dimensionCircle == null ? [] : [RadialDimension(point)];
            var count = _active == "DIMANGULAR2" ? 5 : _active == "DIMANGULAR" ? 4 : 3;
            if (_active != "DIMEDIT" && _points.Count == count - 1)
            { var d = CreateDimension(_points.Append(point).ToArray()); DimensionGeometry.Validate(d); return [d]; }
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or NotSupportedException) { }
        return [];
    }
    private void DimensionPrompt()
    {
        Prompt = _active switch {
            "DIMEDIT" => "Pick a dimension to edit",
            "DIMRADIUS" or "DIMDIAMETER" => _dimensionCircle == null ? "Select a circle or circular arc" : "Specify dimension text/leader location",
            "DIMROTATED" when _text.Length == 0 => "Specify dimension rotation angle (degrees)",
            "DIMORDINATE" when _text.Length == 0 => "Choose X or Y ordinate",
            "DIMORDINATE" => _points.Count switch { 0 => "Specify ordinate origin", 1 => "Specify feature location", _ => "Specify leader endpoint" },
            "DIMANGULAR" => _points.Count switch { 0 => "Specify angle vertex", 1 => "Specify first ray point", 2 => "Specify second ray point", _ => "Specify dimension arc location" },
            "DIMANGULAR2" => _points.Count < 4 ? $"Specify line {_points.Count / 2 + 1}, endpoint {_points.Count % 2 + 1}" : "Specify dimension arc location",
            _ => _points.Count < 2 ? $"Specify extension origin {_points.Count + 1}" : "Specify dimension line location (H / V for linear)" };
        Changed?.Invoke();
    }
}
