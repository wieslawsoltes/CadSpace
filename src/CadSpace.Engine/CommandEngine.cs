using System.Collections.Immutable;
using CadSpace.Geometry;
using CadSpace.Model;

namespace CadSpace.Engine;

public sealed record CommandInfo(string Name, string Alias, string Description, string Category);

/// <summary>Stateful CAD prompts shared by pointer input, command line, and scripts.</summary>
public sealed partial class CommandEngine(CadSession session)
{
    private readonly List<Vec3> _points = new();
    private string _active = "";
    private string _text = "";
    private double _radius;
    public CadSession Session { get; } = session;
    public string ActiveCommand => _active;
    public bool IsActive => _active.Length != 0;
    public Vec3? ReferencePoint => _points.Count > 0 ? _points[^1] : null;
    public double PickTolerance { get; set; } = 5;
    public string Prompt { get; private set; } = "Type a command";
    public event Action<string>? Message;
    public event Action? Changed;
    public event Action<string>? ViewRequested;
    public static IReadOnlyList<CommandInfo> Commands { get; } = new CommandInfo[]
    {
        new("DDEDIT", "ED", "Edit selected or picked text and block attribute values", "Annotate"),
        new("EATTEDIT", "ATE", "Edit retained attribute values on one block reference", "Blocks"),
        new("POLYGON", "POL", "Regular inscribed or circumscribed polygon", "Draw"),
        new("DONUT", "DO", "Native two-arc wide polyline rings or filled discs", "Draw"),
        new("MATCHPROP", "MA", "Preselect destinations, then pick a source for common properties", "Modify"),
        new("UISTATS", "UISTATS", "Report rendered control bounds and workspace state", "Workspace"),
        new("PROPERTIES", "PR", "Show the Properties palette", "Workspace"),
        new("PROPERTIESCLOSE", "PROPERTIESCLOSE", "Hide the Properties palette", "Workspace"),
        new("TOOLPALETTES", "TP", "Show searchable tool and block palettes", "Workspace"),
        new("TOOLPALETTESCLOSE", "TOOLPALETTESCLOSE", "Hide Tool Palettes", "Workspace"),
        new("RIBBON", "RIBBON", "Show the expanded ribbon", "Workspace"),
        new("RIBBONCLOSE", "RIBBONCLOSE", "Minimize the ribbon to tabs", "Workspace"),
        new("CLEANSCREENON", "CLEANSCREENON", "Hide ribbon and palettes without changing the drawing", "Workspace"),
        new("CLEANSCREENOFF", "CLEANSCREENOFF", "Restore the saved workspace controls", "Workspace"),
        new("OPTIONS", "OP", "Workspace and display preferences", "Workspace"),
        new("RENDERSTATS", "RS", "Report actual scene, overlay and GPU draw counts", "View"),
        new("STRETCH", "S", "Crossing corners, base point and displacement point; supported vertices", "Modify"),
        new("QSELECT", "QS", "Filter kind,layer,mode,scope; * wildcard, Replace/Add/Remove, All/Selection", "Edit"),
        new("SELECTSIMILAR", "SE", "Select visible objects with the selected kind and layer", "Edit"),
        new("3DPOLY", "3P", "WCS polyline; Enter finishes, C closes", "Draw"),
        new("SPLINE", "SPL", "Clamped cubic control-point spline; Enter finishes", "Draw"),
        new("ROTATE3D", "3R", "Selected objects, two axis points and angle", "Modify"),
        new("MIRROR3D", "3M", "Selected objects and three mirror-plane points", "Modify"),
        new("ALIGN3D", "3A", "Three source points followed by three target points; rigid alignment", "Modify"),
        new("LOFT", "LOFT", "Capped polygonal loft through selected matching profiles", "Model"),
        new("SWEEP", "SW", "Parallel-transport mesh sweep of a profile along an open polyline", "Model"),
        new("LAYER", "LA", "Layer Properties Manager", "Manage"), new("LINETYPE", "LT", "Linetype Manager", "Manage"),
        new("LTSCALE", "LTSCALE", "Global drawing linetype scale", "Manage"), new("CELTSCALE", "CELTSCALE", "Linetype scale for new objects", "Manage"), new("CELTYPE", "CELTYPE", "Current linetype; built-in patterns load by name", "Manage"),
        new("MENUBAR", "MENUBAR", "Display classic menu bar: 1 on, 0 off", "Manage"),
        new("PEDIT", "PE", "Polylines: Width/Open/Close/Reverse/Join/Edit vertices", "Modify"),
        new("PLINEWID", "PLINEWID", "Default width of new polylines and rectangles", "Draw"),
        new("LINE", "L", "Connected line segments", "Draw"), new("PLINE", "PL", "Polyline; Enter finishes, C closes", "Draw"), new("RECTANG", "REC", "Rectangle from two corners", "Draw"),
        new("CIRCLE", "C", "Center and radius", "Draw"), new("ARC", "A", "Arc through three points", "Draw"), new("POINT", "PO", "Model-space point", "Draw"), new("ELLIPSE", "EL", "Center, major-axis point, minor radius", "Draw"),
        new("TEXT", "T", "Single-line text", "Annotate"), new("DIMALIGNED", "DAL", "Aligned dimension", "Annotate"), new("HATCH", "H", "Hatch selected closed polyline", "Annotate"),
        new("MOVE", "M", "Move selected objects", "Modify"), new("COPY", "CO", "Copy selected objects", "Modify"), new("ROTATE", "RO", "Rotate about base point", "Modify"),
        new("SCALE", "SC", "Uniform scaling about base point", "Modify"), new("MIRROR", "MI", "Mirror about two-point XY axis", "Modify"), new("OFFSET", "O", "Signed line/arc/circle offset", "Modify"),
        new("ERASE", "E", "Erase selected objects", "Modify"), new("EXPLODE", "X", "Explode blocks or straight polylines", "Modify"), new("ARRAY", "AR", "Rectangular array", "Modify"),
        new("TRIM", "TR", "Select line boundaries, then pick a segment to remove", "Modify"), new("EXTEND", "EX", "Select line boundaries, then pick a line end to extend", "Modify"),
        new("FILLET", "F", "Round the corner between two selected lines", "Modify"), new("CHAMFER", "CHA", "Equal-distance bevel between two selected lines", "Modify"), new("JOIN", "J", "Join a connected coplanar line, arc and open polyline chain", "Modify"), new("BREAK", "BR", "Remove a portion of one selected line", "Modify"),
        new("BLOCK", "B", "Create block from selection", "Blocks"), new("INSERT", "I", "Insert block definition", "Blocks"),
        new("UNION", "UNI", "Boolean union of closed triangle meshes", "Model"), new("SUBTRACT", "SU", "Subtract closed meshes from the first selected mesh in drawing order", "Model"), new("INTERSECT", "IN", "Intersect closed triangle meshes", "Model"),
        new("BOX", "BOX", "Triangle-mesh box", "Model"), new("CYLINDER", "CYL", "Triangle-mesh cylinder", "Model"), new("SPHERE", "SPH", "Triangle-mesh sphere", "Model"), new("CONE", "CONE", "Triangle-mesh cone", "Model"),
        new("EXTRUDE", "EXT", "Extrude closed XY profiles", "Model"), new("REVOLVE", "REV", "Revolve polyline surface about an axis", "Model"),
        new("DIST", "DI", "Distance between points", "Measure"), new("AREA", "AA", "Selected closed polyline area", "Measure"),
        new("UNDO", "U", "Undo transaction", "Edit"), new("REDO", "REDO", "Redo transaction", "Edit"), new("SELECTALL", "ALL", "Select visible objects", "Edit"),
        new("ZOOM", "Z", "Zoom extents", "View"), new("TOP", "TOP", "Top drafting view", "View"), new("3DORBIT", "3DO", "3D model viewport", "View"), new("VSCURRENT", "VS", "Set Wireframe, HiddenLine, Shaded or ShadedEdges", "View"),
        new("PERSPECTIVE", "PERSPECTIVE", "Set 1 for perspective or 0 for orthographic", "View"),
        new("CLIP3D", "CLIP3D", "Clip at x,y,z,nx,ny,nz; OFF clears the plane", "View"),
        new("HELP", "?", "List supported commands", "Help")
    };
    public void Cancel() { _matchDrawing = null; _matchDestinations = []; _active = ""; _points.Clear(); _text = ""; Prompt = "Type a command"; Changed?.Invoke(); }
    public void Start(string command) { Cancel(); Submit(command); }
    public void Submit(string input)
    {
        input = input.Trim();
        try
        {
            if (TrySubmitAnnotation(input) || TrySubmitDrafting(input)) return;
            if (!IsActive)
            {
                if (input.Length == 0) return;
                var definition = Commands.FirstOrDefault(c => c.Name.Equals(input, StringComparison.OrdinalIgnoreCase) || c.Alias.Equals(input, StringComparison.OrdinalIgnoreCase));
                if (definition == null) throw new ArgumentException($"Unknown command: {input}. Type HELP for implemented commands.");
                _active = definition.Name; Message?.Invoke($"Command: {_active}");
                switch (_active)
                {
                    case "LOFT": Session.LoftSelection(); ViewRequested?.Invoke("3DORBIT"); Cancel(); return;
                    case "SWEEP": Session.SweepSelection(); ViewRequested?.Invoke("3DORBIT"); Cancel(); return;
                    case "UNION": Session.BooleanSelection(MeshBooleanOperation.Union); ViewRequested?.Invoke("3DORBIT"); Cancel(); return;
                    case "SUBTRACT": Session.BooleanSelection(MeshBooleanOperation.Subtract); ViewRequested?.Invoke("3DORBIT"); Cancel(); return;
                    case "INTERSECT": Session.BooleanSelection(MeshBooleanOperation.Intersect); ViewRequested?.Invoke("3DORBIT"); Cancel(); return;
                    case "UNDO": Session.Document.Undo(); Cancel(); return;
                    case "REDO": Session.Document.Redo(); Cancel(); return;
                    case "ERASE": Session.Erase(); Cancel(); return;
                    case "EXPLODE": Session.Explode(); Cancel(); return;
                    case "JOIN": Session.JoinCurves(); Cancel(); return;
                    case "SELECTALL": Session.SelectAll(); Cancel(); return;
                    case "SELECTSIMILAR": Session.SelectSimilar(); Cancel(); return;
                    case "HELP": Message?.Invoke(string.Join("  ·  ", Commands.Select(c => $"{c.Name} ({c.Alias})"))); Cancel(); return;
                    case "UISTATS":
                    case "PROPERTIES":
                    case "PROPERTIESCLOSE":
                    case "TOOLPALETTES":
                    case "TOOLPALETTESCLOSE":
                    case "RIBBON":
                    case "RIBBONCLOSE":
                    case "CLEANSCREENON":
                    case "CLEANSCREENOFF":
                    case "OPTIONS":
                        ViewRequested?.Invoke(_active); Cancel(); return;
                    case "RENDERSTATS": case "LAYER": case "LINETYPE": case "ZOOM": case "TOP": case "3DORBIT": ViewRequested?.Invoke(_active); Cancel(); return;
                    case "AREA":
                        var polygons = Session.EditableSelection().OfType<PolylineEntity>().Where(p => p.Closed).ToArray();
                        if (polygons.Length == 0) throw new ArgumentException("Select closed polylines for area measurement.");
                        foreach (var poly in polygons) Message?.Invoke($"Area = {Math.Abs(GeometryMath.SignedArea(EntityGeometry.PolylinePoints(poly))):0.###} square units"); Cancel(); return;
                    case "HATCH":
                        var hatches = Session.EditableSelection().Select(e => e is PolylineEntity { Closed: true } p ? new HatchEntity(EntityGeometry.PolylinePoints(p)) { Layer = p.Layer, Layout = p.Layout } : throw new ArgumentException("Hatch requires closed polylines.")).ToArray();
                        Session.Document.Add("Hatch", hatches); Cancel(); return;
                    case "MOVE": case "COPY": case "ROTATE": case "SCALE": case "MIRROR": case "OFFSET": case "ARRAY": case "EXTRUDE": case "REVOLVE": case "BLOCK": case "TRIM": case "EXTEND": case "FILLET": case "CHAMFER": case "BREAK": case "ROTATE3D": case "MIRROR3D": case "ALIGN3D": case "PEDIT": Session.EditableSelection(); break;
                }
                UpdatePrompt(); return;
            }
            if (input.Length == 0)
            {
                if (_active == "3DPOLY" && _points.Count >= 2) Session.Add("3D polyline", new Polyline3DEntity(_points.ToImmutableArray()));
                if (_active == "SPLINE" && _points.Count >= 2) Session.Add("Spline", AdvancedEditing.ControlSpline(_points));
                if (_active == "PLINE" && _points.Count >= 2) Session.Add("Polyline", PolylineEntity.FromPoints(_points) with { ConstantWidth = Session.CurrentPolylineWidth });
                Cancel(); return;
            }
            if (_active is "PLINE" or "3DPOLY" && input.Equals("C", StringComparison.OrdinalIgnoreCase))
            {
                if (_points.Count < 3) throw new ArgumentException("At least three vertices are required to close the polyline.");
                Session.Add("Polyline", _active == "3DPOLY" ? new Polyline3DEntity(_points.ToImmutableArray(), true) : PolylineEntity.FromPoints(_points, true) with { ConstantWidth = Session.CurrentPolylineWidth }); Cancel(); return;
            }
            if (_active is "BLOCK" or "INSERT" && _text.Length == 0) { _text = input; if (_active == "INSERT" && !Session.Document.Drawing.Blocks.ContainsKey(_text)) throw new ArgumentException("Block not found."); UpdatePrompt(); return; }
            if (_active == "QSELECT")
            {
                var parts = input.Split(',', StringSplitOptions.TrimEntries);
                if (parts.Length < 2 || parts.Length > 4) throw new ArgumentException("Use kind,layer[,Replace|Add|Remove[,All|Selection]]. Use * for any kind/layer.");
                var mode = SelectionMode.Replace;
                if (parts.Length >= 3 && (!Enum.TryParse(parts[2], true, out mode) || !Enum.IsDefined(mode))) throw new ArgumentException("Invalid selection mode.");
                if (parts.Length == 4 && !parts[3].Equals("All", StringComparison.OrdinalIgnoreCase) && !parts[3].Equals("Selection", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Scope must be All or Selection.");
                Session.QuickSelect(parts[0], parts[1], mode, parts.Length == 4 && parts[3].Equals("Selection", StringComparison.OrdinalIgnoreCase));
                Message?.Invoke($"Selected {Session.Selection.Count} objects."); Cancel(); return;
            }
            if (_active == "TEXT" && _points.Count == 1) { Session.Add("Text", new TextEntity(_points[0], input, 12)); Cancel(); return; }
            if (_active == "VSCURRENT")
            {
                var styles = new[] { "Wireframe", "HiddenLine", "Shaded", "ShadedEdges" };
                var style = styles.FirstOrDefault(s => s.Equals(input, StringComparison.OrdinalIgnoreCase)) ?? throw new ArgumentException("Use Wireframe, HiddenLine, Shaded or ShadedEdges.");
                ViewRequested?.Invoke("STYLE:" + style); Cancel(); return;
            }
            if (_active == "PERSPECTIVE") { if (input is not ("0" or "1")) throw new ArgumentException("Use 0 (orthographic) or 1 (perspective)."); ViewRequested?.Invoke("PROJECTION:" + input); Cancel(); return; }
            if (_active == "CLIP3D")
            {
                if (input.Equals("OFF", StringComparison.OrdinalIgnoreCase)) { ViewRequested?.Invoke("CLIP:OFF"); Cancel(); return; }
                var values = input.Split(',');
                if (values.Length != 6 || values.Any(v => !GeometryMath.Number(v, out _))) throw new ArgumentException("Enter x,y,z,nx,ny,nz or OFF.");
                var numbers = values.Select(v => double.Parse(v, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
                if (new Vec3(numbers[3], numbers[4], numbers[5]).Length < 1e-12) throw new ArgumentException("A clipping normal cannot be zero.");
                ViewRequested?.Invoke("CLIP:" + input); Cancel(); return;
            }
            if (_active == "PEDIT" && _text.Length == 0)
            {
                switch (input.ToUpperInvariant())
                {
                    case "J": case "JOIN": Session.JoinCurves(); break;
                    case "E": case "EDIT": ViewRequested?.Invoke("POLYLINEEDITOR"); break;
                    case "W": case "WIDTH": _text = "WIDTH"; UpdatePrompt(); return;
                    case "O": case "OPEN": Session.SetClosed(false); break;
                    case "C": case "CLOSE": Session.SetClosed(true); break;
                    case "R": case "REVERSE": Session.Reverse(); break;
                    default: throw new ArgumentException("Use Width, Open, Close, Reverse, Join or Edit.");
                }
                Cancel(); return;
            }
            if (_active == "CELTYPE") { Session.SetCurrentLinetype(input); Cancel(); return; }
            if (_active == "ARRAY") { Array(input); Cancel(); return; }
            if (GeometryMath.TryParsePoint(input, ReferencePoint ?? default, out var p)) { Point(p); return; }
            if (GeometryMath.Number(input, out var number)) { Number(number); return; }
            throw new ArgumentException("Enter a coordinate (x,y or @dx,dy), number, or the prompted keyword.");
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or NotSupportedException) { Message?.Invoke(ex.Message); Cancel(); }
    }
    private bool RequiresNumber => _active is "MENUBAR" or "PEDIT" or "PLINEWID" or "LTSCALE" or "CELTSCALE" or "CELTYPE" or "VSCURRENT" or "PERSPECTIVE" or "CLIP3D" or "OFFSET" or "EXTRUDE" or "FILLET" or "CHAMFER" || (_active is "ROTATE" or "SCALE" && _points.Count == 1) || (_active is "BOX" or "CYLINDER" or "CONE" or "REVOLVE" or "ELLIPSE" or "ROTATE3D" && _points.Count == 2);
    public void Point(Vec3 point)
    {
        if (!IsActive || !point.IsFinite) return;
        try
        {
            if (TryAnnotationPoint(point) || TryDraftPoint(point)) return;
            if (RequiresNumber || _active is "ARRAY" or "QSELECT" || (_active is "BLOCK" or "INSERT" && _text.Length == 0)) { Message?.Invoke(Prompt); return; }
            if (_active == "TEXT" && _points.Count == 1) { Message?.Invoke("Enter the text in the command line."); return; }
            if (_active is "TRIM" or "EXTEND") { Session.TrimOrExtend(point, PickTolerance, _active == "EXTEND"); Cancel(); return; }
            if (_active == "PLINE" && _points.Count > 0 && _points[^1].DistanceTo(point) <= 1e-9) { Message?.Invoke("Specify a different vertex."); return; }
            _points.Add(point);
            switch (_active)
            {
                case "STRETCH" when _points.Count == 4: Session.Stretch(_points[0], _points[1], point - _points[2]); Cancel(); return;
                case "MIRROR3D" when _points.Count == 3: Session.TransformSelection("Mirror 3D", AdvancedEditing.MirrorPlane(_points[0],_points[1],point)); Cancel(); return;
                case "ALIGN3D" when _points.Count == 6: Session.TransformSelection("Align 3D", AdvancedEditing.Align(_points[0],_points[1],_points[2],_points[3],_points[4],point)); Cancel(); return;
                case "POINT": Session.Add("Point", new PointEntity(point)); Cancel(); return;
                case "LINE" when _points.Count == 2:
                    if (_points[0].DistanceTo(point) > 1e-9) Session.Add("Line", new LineEntity(_points[0], point)); _points.RemoveAt(0); break;
                case "RECTANG" when _points.Count == 2:
                    var a = _points[0]; var b = point;
                    if (Math.Abs(a.X - b.X) < 1e-9 || Math.Abs(a.Y - b.Y) < 1e-9) throw new ArgumentException("Rectangle width and height must be nonzero.");
                    Session.Add("Rectangle", PolylineEntity.FromPoints(new Vec3[] { a, new(b.X, a.Y, a.Z), new(b.X, b.Y, a.Z), new(a.X, b.Y, a.Z) }, true) with { ConstantWidth = Session.CurrentPolylineWidth }); Cancel(); return;
                case "CIRCLE" when _points.Count == 2: Session.Add("Circle", new CircleEntity(_points[0], _points[0].DistanceTo(point))); Cancel(); return;
                case "ARC" when _points.Count == 3:
                    var circle = GeometryMath.CircleThrough(_points[0], _points[1], _points[2]); var start = GeometryMath.Angle(_points[0] - circle.Center); var end = GeometryMath.Angle(point - circle.Center); var middle = GeometryMath.Angle(_points[1] - circle.Center);
                    if (GeometryMath.NormalizeAngle(middle - start) > GeometryMath.NormalizeAngle(end - start)) (start, end) = (end, start);
                    Session.Add("Arc", new ArcEntity(circle.Center, circle.Radius, start, end)); Cancel(); return;
                case "DIMALIGNED" when _points.Count == 3: Session.Add("Dimension", new DimensionEntity(_points[0], _points[1], point)); Cancel(); return;
                case "MOVE" or "COPY" when _points.Count == 2: Session.TransformSelection(_active, Transform3.Translation(point - _points[0]), _active == "COPY"); Cancel(); return;
                case "MIRROR" when _points.Count == 2: Session.TransformSelection("Mirror", Transform3.MirrorXY(_points[0], point)); Cancel(); return;
                case "BREAK" when _points.Count == 2: Session.BreakLine(_points[0], point); Cancel(); return;
                case "BLOCK": Session.CreateBlock(_text, point); Cancel(); return;
                case "INSERT": Session.Add("Insert", new BlockReferenceEntity(_text, point, new(1, 1, 1))); Cancel(); return;
                case "SPHERE" when _points.Count == 2: Session.Add("Sphere", MeshFactory.Sphere(_points[0], _points[0].DistanceTo(point))); ViewRequested?.Invoke("3DORBIT"); Cancel(); return;
                case "CYLINDER" or "CONE" when _points.Count == 2: _radius = _points[0].DistanceTo(point); break;
                case "DIST" when _points.Count == 2: Message?.Invoke($"Distance = {_points[0].DistanceTo(point):0.######}; delta = {point - _points[0]}"); Cancel(); return;
            }
            UpdatePrompt();
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or NotSupportedException) { Message?.Invoke(ex.Message); Cancel(); }
    }
    private void Number(double value)
    {
        switch (_active)
        {
            case "MENUBAR":
                if (value is not (0 or 1)) throw new ArgumentException("MENUBAR accepts 0 or 1.");
                ViewRequested?.Invoke(value == 0 ? "MENUBAR:0" : "MENUBAR:1"); break;
            case "PEDIT" when _text == "WIDTH": Session.SetWidth(value); break;
            case "PLINEWID":
                if (value < 0 || value > 1e12) throw new ArgumentException("Polyline width must be between 0 and 1e12.");
                Session.CurrentPolylineWidth = value; Session.Invalidate(); break;
            case "LTSCALE":
                if (value <= 0 || value > 1e9) throw new ArgumentException("Global linetype scale must be positive and no larger than 1e9.");
                Session.Document.Edit("Global linetype scale", d => d with { LinetypeScale = value }); break;
            case "CELTSCALE":
                if (value <= 0 || value > 1e9) throw new ArgumentException("Current linetype scale must be positive and no larger than 1e9.");
                Session.CurrentLinetypeScale = value; Session.Invalidate(); break;

            case "CIRCLE" when _points.Count == 1: Session.Add("Circle", new CircleEntity(_points[0], value)); break;
            case "ELLIPSE" when _points.Count == 2:
                var major = _points[1] - _points[0];
                if (Math.Abs(major.Z) > 1e-9 || major.Length <= 1e-9 || value <= 0 || value > major.Length) throw new ArgumentException("Use an XY major axis and a positive minor radius no larger than the major radius.");
                Session.Add("Ellipse", new EllipseEntity(_points[0], major, value / major.Length)); break;
            case "ROTATE3D" when _points.Count == 2: Session.TransformSelection("Rotate 3D", Transform3.RotationAxis(_points[1]-_points[0],value,_points[0])); break;
            case "ROTATE" when _points.Count == 1: Session.TransformSelection("Rotate", Transform3.RotationZ(value, _points[0])); break;
            case "SCALE" when _points.Count == 1:
                if (value <= 0) throw new ArgumentException("Scale must be positive."); Session.TransformSelection("Scale", Transform3.Scaling(new(value, value, value), _points[0])); break;
            case "OFFSET": Session.Offset(value); break;
            case "FILLET": Session.Fillet(value); break;
            case "CHAMFER": Session.Chamfer(value); break;
            case "EXTRUDE": Session.Extrude(value); ViewRequested?.Invoke("3DORBIT"); break;
            case "BOX" when _points.Count == 2: Session.Add("Box", MeshFactory.Box(_points[0], new(_points[1].X, _points[1].Y, _points[0].Z + value))); ViewRequested?.Invoke("3DORBIT"); break;
            case "SPHERE" when _points.Count == 1: Session.Add("Sphere", MeshFactory.Sphere(_points[0], value)); ViewRequested?.Invoke("3DORBIT"); break;
            case "CYLINDER" or "CONE" when _points.Count == 1:
                if (value <= 0) throw new ArgumentException("Radius must be positive."); _radius = value; _points.Add(_points[0] + Vec3.UnitX * value); UpdatePrompt(); return;
            case "CYLINDER" when _points.Count == 2: Session.Add("Cylinder", MeshFactory.Cylinder(_points[0], _radius, value)); ViewRequested?.Invoke("3DORBIT"); break;
            case "CONE" when _points.Count == 2: Session.Add("Cone", MeshFactory.Cone(_points[0], _radius, value)); ViewRequested?.Invoke("3DORBIT"); break;
            case "REVOLVE" when _points.Count == 2:
                var meshes = Session.EditableSelection().Select(e => e is PolylineEntity p ? MeshFactory.Revolve(EntityGeometry.PolylinePoints(p), _points[0], _points[1], value) with { Layer = p.Layer, Layout = p.Layout } : throw new ArgumentException("Revolve requires polylines.")).ToArray();
                Session.Document.Add("Revolve", meshes); ViewRequested?.Invoke("3DORBIT"); break;
            default: throw new ArgumentException("A point is required before the numeric parameter.");
        }
        Cancel();
    }
    private void Array(string input)
    {
        var values = input.Split(',', StringSplitOptions.TrimEntries);
        if (values.Length != 4 || !int.TryParse(values[0], out var columns) || !int.TryParse(values[1], out var rows) || !GeometryMath.Number(values[2], out var dx) || !GeometryMath.Number(values[3], out var dy) || columns < 1 || rows < 1 || (long)columns * rows > 10000) throw new ArgumentException("Use columns,rows,x-spacing,y-spacing; maximum 10,000 instances.");
        var selection = Session.EditableSelection();
        if ((long)selection.Length * rows * columns > 100000) throw new ArgumentException("The array exceeds 100,000 objects.");
        var copies = new List<Entity>();
        for (var y = 0; y < rows; y++) for (var x = 0; x < columns; x++) if (x != 0 || y != 0) copies.AddRange(selection.Select(e => EntityGeometry.Transform(e, Transform3.Translation(new(x * dx, y * dy)), true)));
        Session.Document.Add("Array", copies.ToArray());
    }
    private void UpdatePrompt()
    {
        Prompt = _active switch
        {
            "MENUBAR" => "Enter 1 to show the classic menu bar or 0 to hide it",
            "PEDIT" => _text == "WIDTH" ? "Specify uniform polyline width (0 for a centerline)" : "Enter Width / Open / Close / Reverse / Join / Edit",
            "PLINEWID" => "Specify default polyline width (drawing units)",
            "LTSCALE" => "Specify global linetype scale",
            "CELTSCALE" => "Specify linetype scale for new objects",
            "CELTYPE" => "Enter BYLAYER / BYBLOCK or a loaded/built-in linetype name",
            "STRETCH" => _points.Count switch { 0 => "Specify first crossing corner", 1 => "Specify opposite crossing corner", 2 => "Specify stretch base point", _ => "Specify displacement point" },
            "QSELECT" => "Enter kind,layer[,Replace|Add|Remove[,All|Selection]]; * matches any",
            "VSCURRENT" => "Enter Wireframe / HiddenLine / Shaded / ShadedEdges",
            "PERSPECTIVE" => "Enter 1 for perspective or 0 for orthographic",
            "CLIP3D" => "Enter point and normal: x,y,z,nx,ny,nz; or OFF",
            "3DPOLY" => _points.Count == 0 ? "Specify first WCS point" : "Specify next WCS point or Close; Enter to finish",
            "SPLINE" => "Specify spline control point; Enter to finish",
            "ROTATE3D" => _points.Count < 2 ? $"Specify axis point {_points.Count + 1}" : "Specify angle in degrees",
            "ALIGN3D" => $"Specify {(_points.Count < 3 ? "source" : "target")} frame point {_points.Count % 3 + 1}",
            "PLINE" => _points.Count == 0 ? "Specify start point" : "Specify next point or [Close]; Enter to finish",
            "LINE" => _points.Count == 0 ? "Specify first point" : "Specify next point; Enter to finish",
            "CIRCLE" or "SPHERE" or "CYLINDER" or "CONE" => _points.Count == 0 ? "Specify center point" : _points.Count == 1 ? "Specify radius or radius point" : "Specify height",
            "ELLIPSE" => _points.Count == 0 ? "Specify ellipse center" : _points.Count == 1 ? "Specify major-axis endpoint" : "Specify minor-axis radius",
            "TEXT" => _points.Count == 0 ? "Specify insertion point" : "Enter text",
            "ROTATE" => _points.Count == 0 ? "Specify base point" : "Specify rotation angle in degrees",
            "SCALE" => _points.Count == 0 ? "Specify base point" : "Specify scale factor",
            "OFFSET" => "Specify signed offset distance (positive is left / outward)",
            "FILLET" => "Specify fillet radius for the selected two lines",
            "CHAMFER" => "Specify equal chamfer distance for the selected two lines",
            "TRIM" => "Pick the unselected line portion to remove between selected boundaries",
            "EXTEND" => "Pick an unselected line near the end to extend to a selected boundary",
            "EXTRUDE" => "Specify extrusion height",
            "ARRAY" => "Enter columns,rows,x-spacing,y-spacing",
            "BLOCK" or "INSERT" => _text.Length == 0 ? "Enter block name" : "Specify insertion/base point",
            "BOX" => _points.Count == 0 ? "Specify first base corner" : _points.Count == 1 ? "Specify opposite base corner" : "Specify height",
            "REVOLVE" => _points.Count == 0 ? "Specify axis start point" : _points.Count == 1 ? "Specify axis end point" : "Specify revolution angle in degrees",
            "DIMALIGNED" => _points.Count < 2 ? $"Specify extension point {_points.Count + 1}" : "Specify dimension-line location",
            _ => $"Specify point {_points.Count + 1}"
        };
        Changed?.Invoke();
    }
    public IReadOnlyList<Entity> Preview(Vec3 cursor)
    {
        if (DraftPreview(cursor) is { } drafting) return drafting;
        if (_points.Count == 0 || RequiresNumber) return [];
        var a = _points[0];
        return _active switch
        {
            "STRETCH" when _points.Count == 1 => [PolylineEntity.FromPoints(new Vec3[] { a, new(cursor.X, a.Y, a.Z), cursor, new(a.X, cursor.Y, a.Z) }, true)],
            "STRETCH" when _points.Count == 3 => Session.PreviewStretch(_points[0], _points[1], cursor - _points[2]),
            "STRETCH" => [],
            "LINE" => [new LineEntity(_points[^1], cursor)],
            "PLINE" => [PolylineEntity.FromPoints(_points.Append(cursor)) with { ConstantWidth = Session.CurrentPolylineWidth }],
            "3DPOLY" or "SPLINE" => [PolylineEntity.FromPoints(_points.Append(cursor))],
            "RECTANG" when _points.Count == 1 => [PolylineEntity.FromPoints(new Vec3[] { a, new(cursor.X, a.Y, a.Z), cursor, new(a.X, cursor.Y, a.Z) }, true) with { ConstantWidth = Session.CurrentPolylineWidth }],
            "BOX" when _points.Count == 1 => [PolylineEntity.FromPoints(new Vec3[] { a, new(cursor.X, a.Y, a.Z), cursor, new(a.X, cursor.Y, a.Z) }, true)],
            "CIRCLE" or "CYLINDER" or "CONE" or "SPHERE" when _points.Count == 1 => [new CircleEntity(a, Math.Max(1e-8, a.DistanceTo(cursor)))],
            "MOVE" or "COPY" => Session.SelectedEntities().Select(e => EntityGeometry.Transform(e, Transform3.Translation(cursor - a))).ToArray(),
            "DIMALIGNED" when _points.Count == 2 => [new DimensionEntity(a, _points[1], cursor)],
            _ => [new LineEntity(_points[^1], cursor)]
        };
    }
}
