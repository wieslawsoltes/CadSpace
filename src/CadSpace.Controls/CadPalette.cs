using System.Collections.Immutable;
using System.Globalization;
using CadSpace.Engine;
using CadSpace.Geometry;
using CadSpace.Model;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace CadSpace.Controls;

/// <summary>Standalone property, layer and block palettes. Edits participate in document undo.</summary>
public sealed class CadPalette : UserControl
{
    private readonly StackPanel _body = new() { Spacing = 8, Margin = new Thickness(12) };
    private CadSession? _session;
    private string _tab = "Properties";
    private bool _building;
    private Drawing? _builtDrawing;
    private long _builtSelection = -1;
    private string _builtTab = "", _builtLayer = "";
    public event Action<string>? Message;
    public event Action<string>? InsertRequested;
    public event Action? AnnotationEditRequested;
    public event Action? DimensionEditRequested;
    public event Action? SplineEditRequested;
    public event Action? HatchEditRequested;
    public event Action? LeaderEditRequested;
    public CadPalette()
    {
        var root = CadTheme.Grid(33, -1); root.Background = CadTheme.Brush(CadTheme.Background);
        var tabs = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 1 };
        foreach (var name in new[] { "Properties", "Layers", "Blocks" }) tabs.Children.Add(CadTheme.Button(name, () => { _tab = name; Refresh(); }));
        CadTheme.At(root, tabs, 0); CadTheme.At(root, new ScrollViewer { Content = _body, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }, 1); Content = root;
    }
    public void Bind(CadSession session)
    {
        if (_session != null) _session.Changed -= Refresh; _session = session; _builtDrawing = null; session.Changed += Refresh; Refresh();
    }
    public void Refresh()
    {
        if (_session == null || _building) return;
        if (ReferenceEquals(_builtDrawing, _session.Document.Drawing) && _builtSelection == _session.SelectionRevision && _builtTab == _tab && _builtLayer == _session.CurrentLayer) return;
        _builtDrawing = _session.Document.Drawing; _builtSelection = _session.SelectionRevision; _builtTab = _tab; _builtLayer = _session.CurrentLayer;
        _building = true;
        try { _body.Children.Clear(); if (_tab == "Layers") Layers(); else if (_tab == "Blocks") Blocks(); else Properties(); }
        finally { _building = false; }
    }
    private void Heading(string title) => _body.Children.Add(new Border { Child = CadTheme.Text(title.ToUpperInvariant(), 10, CadTheme.Muted), Padding = new Thickness(0, 12, 0, 5), BorderBrush = CadTheme.Brush(CadTheme.Edge), BorderThickness = new Thickness(0, 0, 0, 1) });
    private void Field(string name, string value, Action<string>? edit = null)
    {
        var root = new Grid(); root.ColumnDefinitions.Add(new() { Width = new GridLength(92) }); root.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        root.Children.Add(CadTheme.Text(name, 11, CadTheme.Muted));
        if (edit == null) { var label = CadTheme.Text(value, 11); label.TextWrapping = TextWrapping.Wrap; Grid.SetColumn(label, 1); root.Children.Add(label); }
        else
        {
            var box = new TextBox { Text = value, FontSize = 11, MinHeight = 28, Padding = new Thickness(5, 3, 5, 3), Background = CadTheme.Brush(0xFF20262E), BorderThickness = new Thickness(0), Foreground = CadTheme.Brush(CadTheme.TextColor) };
            Grid.SetColumn(box, 1); root.Children.Add(box);
            box.LostFocus += (_, _) => { if (!_building && box.Text != value) Try(() => edit(box.Text)); };
        }
        _body.Children.Add(root);
    }
    private void Try(Action edit)
    {
        try { edit(); } catch (Exception error) when (error is ArgumentException or InvalidOperationException or NotSupportedException) { Message?.Invoke(error.Message); _builtDrawing = null; Refresh(); }
    }
    private void Update(Entity entity, Func<Entity, Entity> update)
    {
        var session = _session!;
        if (session.Document.Drawing.LayerFor(entity).Locked) throw new InvalidOperationException("The object's layer is locked.");
        session.Document.Edit("Properties", s => s with { Entities = s.Entities.Select(e => e.Id == entity.Id ? update(e) : e).ToImmutableArray() });
    }
    private static double Number(string value) => GeometryMath.Number(value, out var n) ? n : throw new ArgumentException("Enter a finite number.");
    private static Vec3 Point(string value) => GeometryMath.TryParsePoint(value, default, out var p) ? p : throw new ArgumentException("Enter x,y or x,y,z.");
    private void Properties()
    {
        var session = _session!; var selected = session.SelectedEntities();
        _body.Children.Add(CadTheme.Text(selected.Length == 0 ? "No selection" : selected.Length == 1 ? selected[0].Kind : $"{selected.Length} objects selected", 14));
        Heading("General");
        if (selected.Length == 0)
        {
            Field("Drawing", session.Document.Drawing.Name); Field("Objects", session.Document.Drawing.Entities.Length.ToString()); Field("Layers", session.Document.Drawing.Layers.Count.ToString()); Field("Blocks", session.Document.Drawing.Blocks.Count.ToString());
            Field("Current layer", session.CurrentLayer); Heading("Drafting"); Field("Grid spacing", session.GridSpacing.ToString(CultureInfo.InvariantCulture), value => { var n = Number(value); if (n <= 0) throw new ArgumentException("Spacing must be positive."); session.GridSpacing = n; session.Invalidate(); });
            _body.Children.Add(new TextBlock { Text = "Click an object to inspect it.\n\nDrag left to right for a window selection. Drag right to left for a crossing selection. Click adds; Shift removes; Ctrl toggles. Drag a blue grip to edit.\n\nMiddle-drag pans. The mouse wheel zooms about the pointer.", TextWrapping = TextWrapping.Wrap, FontSize = 11, Foreground = CadTheme.Brush(CadTheme.Muted), Margin = new Thickness(0, 20, 0, 0) }); return;
        }
        var editable = !selected.Any(e => e is OpaqueEntity || session.Document.Drawing.LayerFor(e).Locked);
        var layer = new ComboBox { ItemsSource = session.Document.Drawing.Layers.Keys.OrderBy(x => x).ToArray(), SelectedItem = selected[0].Layer, FontSize = 11, MinHeight = 28, HorizontalAlignment = HorizontalAlignment.Stretch, IsEnabled = editable };
        layer.SelectionChanged += (_, _) => { if (_building || layer.SelectedItem is not string name || selected.All(e => e.Layer == name)) return; Try(() => { session.EditableSelection(); if (session.Document.Drawing.Layers[name].Locked) throw new InvalidOperationException("Target layer is locked."); var ids = selected.Select(e => e.Id).ToHashSet(); session.Document.Edit("Change layer", s => s with { Entities = s.Entities.Select(e => ids.Contains(e.Id) ? e with { Layer = name } : e).ToImmutableArray() }); }); };
        _body.Children.Add(CadTheme.Text("Layer", 11, CadTheme.Muted)); _body.Children.Add(layer);
        var lineTypes = new ComboBox { ItemsSource = new[] { "BYLAYER", "BYBLOCK" }.Concat(session.Document.Drawing.Linetypes.Keys.Order()).ToArray(), SelectedItem = selected[0].Linetype, MinHeight = 28, FontSize = 11, HorizontalAlignment = HorizontalAlignment.Stretch, IsEnabled = editable };
        lineTypes.SelectionChanged += (_, _) => { if (!_building && lineTypes.SelectedItem is string name && selected.Any(e => e.Linetype != name)) Try(() => session.SetSelectedLinetype(name)); };
        _body.Children.Add(CadTheme.Text("Linetype", 11, CadTheme.Muted)); _body.Children.Add(lineTypes);
        if (selected.Length != 1) return;
        var entity = selected[0];
        if (LeaderGeometry.Unwrap(entity) is { } leader)
        {
            Field("Leader vertices", leader.Vertices.Length.ToString());
            if (editable) _body.Children.Add(CadUi.TextButton("Leader Properties…", () => LeaderEditRequested?.Invoke(), "properties.leader"));
        }
        if (HatchEditing.Unwrap(entity) is { } hatch)
        {
            Field("Fill", hatch.Gradient?.Name ?? (hatch.Solid ? "Solid" : hatch.PatternName));
            Field("Boundaries", hatch.Loops.Length.ToString(CultureInfo.InvariantCulture));
            if (editable) _body.Children.Add(CadUi.TextButton("Hatch and Gradient…", () => HatchEditRequested?.Invoke(), "properties.hatch"));
        }
        if (SplineEditing.Unwrap(entity) is { } spline)
        {
            Field("Degree", spline.Degree.ToString(CultureInfo.InvariantCulture));
            Field("Control points", spline.ControlPoints.Length.ToString(CultureInfo.InvariantCulture));
            if (editable) _body.Children.Add(CadUi.TextButton("Spline Editor…", () => SplineEditRequested?.Invoke(), "properties.spline"));
        }
        if (DimensionGeometry.Unwrap(entity) is { } dimension)
        {
            Field("Dimension", dimension.Type.ToString());
            Field("Measurement", DimensionGeometry.Measure(dimension).ToString("0.######", CultureInfo.InvariantCulture));
            if (editable) _body.Children.Add(CadUi.TextButton("Dimension Properties…", () => DimensionEditRequested?.Invoke(), "properties.dimension"));
        }
        if (editable && TextEditing.IsEditable(entity))
            _body.Children.Add(CadUi.TextButton(entity is CompositeEntity ? "Edit block attributes…" : "Edit text…", () => AnnotationEditRequested?.Invoke(), "properties.annotation"));
        Field("Handle", entity.Handle.Length == 0 ? "New object" : entity.Handle);
        Action<string>? editColor = editable ? value => { if (!uint.TryParse(value.TrimStart('#'), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var color) || value.TrimStart('#').Length != 6) throw new ArgumentException("Use a six-digit RGB color."); Update(entity, e => e with { TrueColor = 0xFF000000u | color }); } : null;
        Field("RGB color", ((entity.TrueColor ?? session.Document.Drawing.LayerFor(entity).Color) & 0xFFFFFF).ToString("X6"), editColor);
        if (!editable) { Heading("Read only"); Field("Reason", entity is OpaqueEntity ? "Unsupported DXF record" : "Locked layer"); return; }
        Field("Line scale", entity.LinetypeScale.ToString(CultureInfo.InvariantCulture), value => Update(entity, e => e with { LinetypeScale = Number(value) }));
        Heading("Geometry");
        switch (entity)
        {
            case LineEntity line: Field("Start", line.Start.ToString(), s => Update(entity, e => ((LineEntity)e) with { Start = Point(s) })); Field("End", line.End.ToString(), s => Update(entity, e => ((LineEntity)e) with { End = Point(s) })); Field("Length", line.Start.DistanceTo(line.End).ToString("0.###")); break;
            case CircleEntity circle: Field("Center", circle.Center.ToString(), s => Update(entity, e => ((CircleEntity)e) with { Center = Point(s) })); Field("Radius", circle.Radius.ToString(CultureInfo.InvariantCulture), s => Update(entity, e => ((CircleEntity)e) with { Radius = Number(s) })); Field("Area", (Math.PI * circle.Radius * circle.Radius).ToString("0.###")); break;
            case ArcEntity arc: Field("Center", arc.Center.ToString(), s => Update(entity, e => ((ArcEntity)e) with { Center = Point(s) })); Field("Radius", arc.Radius.ToString(CultureInfo.InvariantCulture), s => Update(entity, e => ((ArcEntity)e) with { Radius = Number(s) })); Field("Start angle", arc.StartAngle.ToString(CultureInfo.InvariantCulture), s => Update(entity, e => ((ArcEntity)e) with { StartAngle = Number(s) })); Field("End angle", arc.EndAngle.ToString(CultureInfo.InvariantCulture), s => Update(entity, e => ((ArcEntity)e) with { EndAngle = Number(s) })); break;
            case TextEntity text:
                Field("Position", text.Position.ToString(), s => Update(entity, e => ((TextEntity)e) with { Position = Point(s) }));
                if (text.Multiline)
                {
                    // The inline single-line TextBox truncates multiline values on assignment and can
                    // commit the truncation on LostFocus. Use the staged editor for MTEXT content.
                    var content = SceneTextLayout.NormalizeLineEndings(text.Text).Replace("\n", "\\P");
                    var length = Math.Min(160, content.Length);
                    if (length < content.Length && char.IsHighSurrogate(content[length - 1])) length--;
                    Field("Text", length < content.Length ? content[..length] + "…" : content);
                }
                else Field("Text", text.Text, s => session.SetText(entity, s));
                Field("Height", text.Height.ToString(CultureInfo.InvariantCulture), s => Update(entity, e => ((TextEntity)e) with { Height = Number(s) }));
                Field("Rotation", text.Rotation.ToString(CultureInfo.InvariantCulture), s => Update(entity, e => ((TextEntity)e) with { Rotation = Number(s) }));
                break;
            case PolylineEntity poly:
                Field("Vertices", poly.Vertices.Length.ToString());
                Field("Closed", poly.Closed ? "Yes" : "No");
                Field("Global width", poly.ConstantWidth.ToString(CultureInfo.InvariantCulture), s => Update(entity, e => ((PolylineEntity)e) with { ConstantWidth = Number(s), Vertices = ((PolylineEntity)e).Vertices.Select(v => v with { StartWidth = 0, EndWidth = 0 }).ToImmutableArray() }));
                Field("Area", Math.Abs(GeometryMath.SignedArea(EntityGeometry.PolylinePoints(poly))).ToString("0.###"));
                var editor = new CadPolylineEditor(); editor.Bind(session, poly); _body.Children.Add(editor);
                break;
            case MeshEntity mesh: Field("Operation", mesh.Operation); Field("Vertices", mesh.Vertices.Length.ToString()); Field("Triangles", (mesh.Triangles.Length / 3).ToString()); Field("Signed volume", MeshFactory.SignedVolume(mesh).ToString("0.###")); break;
            case BlockReferenceEntity block: Field("Definition", block.Name); Field("Position", block.Position.ToString(), s => Update(entity, e => ((BlockReferenceEntity)e) with { Position = Point(s) })); Field("Scale", block.Scale.ToString(), s => Update(entity, e => ((BlockReferenceEntity)e) with { Scale = Point(s) })); Field("Rotation", block.Rotation.ToString(CultureInfo.InvariantCulture), s => Update(entity, e => ((BlockReferenceEntity)e) with { Rotation = Number(s) })); break;
            case PointEntity point: Field("Position", point.Position.ToString(), s => Update(entity, e => ((PointEntity)e) with { Position = Point(s) })); break;
        }
    }
    private void Layers()
    {
        var session = _session!; _body.Children.Add(CadTheme.Text("Layer properties", 14));
        _body.Children.Add(CadTheme.Text("Click a layer name to make it current.", 10, CadTheme.Muted));
        foreach (var layer in session.Document.Drawing.Layers.Values.OrderBy(l => l.Name))
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 3 };
            var swatch = new Border { Width = 7, Background = CadTheme.Brush(layer.Color), Margin = new Thickness(0, 5, 2, 5) }; row.Children.Add(swatch);
            var name = CadTheme.Button(layer.Name, () => { session.CurrentLayer = layer.Name; session.Invalidate(); }, 112); name.Background = CadTheme.Brush(session.CurrentLayer == layer.Name ? 0xFF385775 : CadTheme.Panel); row.Children.Add(name);
            row.Children.Add(CadTheme.Button(layer.Visible ? "On" : "Off", () => session.Document.Edit("Layer visibility", s => s with { Layers = s.Layers.SetItem(layer.Name, layer with { Visible = !layer.Visible }) }), 38));
            var lockButton = CadTheme.Button(layer.Locked ? "Lock" : "Free", () => session.Document.Edit("Layer lock", s => s with { Layers = s.Layers.SetItem(layer.Name, layer with { Locked = !layer.Locked }) }), 44); row.Children.Add(lockButton); _body.Children.Add(row);
        }
        Heading("New layer"); var input = new TextBox { PlaceholderText = "Layer name", FontSize = 12, MinHeight = 30 }; _body.Children.Add(input);
        _body.Children.Add(CadTheme.Button("Add layer", () => Try(() => { var name = input.Text.Trim(); if (string.IsNullOrWhiteSpace(name) || name.Contains('\n') || name.Contains('\r') || name.IndexOfAny(['<', '>', '/', '\\', ':', ';', '?', '*', '|', '=']) >= 0) throw new ArgumentException("Enter a valid layer name."); if (session.Document.Drawing.Layers.ContainsKey(name)) throw new ArgumentException("Layer already exists."); session.Document.Edit("New layer", s => s with { Layers = s.Layers.Add(name, new(name)) }); })));
    }
    private void Blocks()
    {
        var session = _session!; _body.Children.Add(CadTheme.Text("Block library", 14));
        foreach (var block in session.Document.Drawing.Blocks.Values.Where(b => !b.Name.StartsWith('*')).OrderBy(b => b.Name))
        {
            var card = new StackPanel { Spacing = 7 }; card.Children.Add(CadTheme.Text(block.Name, 13)); card.Children.Add(CadTheme.Text($"{block.Entities.Length} entities  •  base {block.BasePoint}", 10, CadTheme.Muted)); card.Children.Add(CadTheme.Button("Insert into drawing", () => InsertRequested?.Invoke(block.Name)));
            _body.Children.Add(CadTheme.Box(card, CadTheme.Panel, 10));
        }
        if (session.Document.Drawing.Blocks.IsEmpty) _body.Children.Add(CadTheme.Text("Select objects and use BLOCK to create a definition.", 11, CadTheme.Muted));
    }
}
