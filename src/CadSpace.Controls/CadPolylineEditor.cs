using System.Collections.Immutable;
using System.Globalization;
using CadSpace.Engine;
using CadSpace.Geometry;
using CadSpace.Model;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace CadSpace.Controls;

/// <summary>Bounded vertex editor, shared by Properties and PEDIT. Coordinates are in the polyline's own plane.</summary>
public sealed class CadPolylineEditor : UserControl
{
    private readonly TextBox _index;
    private readonly TextBox _point;
    private readonly TextBox _start, _end, _bulge;
    private readonly TextBlock _status = CadTheme.Text("", 10, CadTheme.Muted);
    private CadSession? _session;
    private Entity? _expected;
    private PolylineEntity? _polyline;
    private int _selected;
    public CadPolylineEditor() : this("polyline") { }
    public CadPolylineEditor(string automationPrefix)
    {
        if (string.IsNullOrWhiteSpace(automationPrefix)) throw new ArgumentException("An automation prefix is required.");
        string Id(string name) => automationPrefix + "." + name;
        _index = Input("Vertex (1-based)", "1", Id("index"));
        _point = Input("Position (local x,y,z)", "0,0,0", Id("point"));
        _start = Input("Start width", "0", Id("start")); _end = Input("End width", "0", Id("end")); _bulge = Input("Bulge", "0", Id("bulge"));
        var body = new StackPanel { Spacing = 6 };
        body.Children.Add(CadTheme.Text("VERTEX AND SEGMENT EDITOR", 10, CadTheme.Muted));
        body.Children.Add(_index);
        var navigation = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 3 };
        navigation.Children.Add(CadUi.TextButton("Previous", () => Step(-1), Id("previous")));
        navigation.Children.Add(CadUi.TextButton("Read", Read, Id("read")));
        navigation.Children.Add(CadUi.TextButton("Next", () => Step(1), Id("next"))); body.Children.Add(navigation);
        body.Children.Add(_point); body.Children.Add(_start); body.Children.Add(_end); body.Children.Add(_bulge);
        body.Children.Add(CadUi.TextButton("Apply vertex", Apply, Id("apply")));
        body.Children.Add(CadUi.TextButton("Insert segment midpoint", () => Execute(p => PolylineTools.SplitSegment(p, _selected)), Id("insert")));
        body.Children.Add(CadUi.TextButton("Delete vertex / reconnect straight", () => Execute(p => PolylineTools.DeleteVertex(p, _selected)), Id("delete")));
        body.Children.Add(CadUi.TextButton("Straighten outgoing segment", () => Execute(p => {
            if (!p.Closed && _selected == p.Vertices.Length - 1) throw new ArgumentException("The last open vertex has no outgoing segment.");
            return p with { Vertices = p.Vertices.SetItem(_selected, p.Vertices[_selected] with { Bulge = 0 }) };
        }), Id("straighten")));
        _status.TextWrapping = TextWrapping.Wrap; body.Children.Add(_status); Content = body;
    }
    private static TextBox Input(string header, string value, string id) => CadUi.Identify(new TextBox { Header = header, Text = value, FontSize = 11, MinHeight = 28, Padding = new Thickness(5, 2, 5, 2) }, id, header);
    public void Bind(CadSession session, PolylineEntity polyline) => Bind(session, (Entity)polyline);
    public void Bind(CadSession session, Entity root)
    {
        _session = session; _expected = root; _polyline = PolylineTools.Unwrap(root).Geometry as PolylineEntity ?? throw new ArgumentException("A 2D polyline is required."); Read();
    }
    private bool SelectIndex()
    {
        if (_polyline == null) return false;
        if (!int.TryParse(_index.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) || n < 1 || n > _polyline.Vertices.Length)
        { _status.Text = $"Enter a vertex from 1 to {_polyline.Vertices.Length}."; return false; }
        _selected = n - 1; return true;
    }
    private void Step(int delta)
    {
        if (!SelectIndex()) return;
        _index.Text = (Math.Clamp(_selected + delta, 0, _polyline!.Vertices.Length - 1) + 1).ToString(CultureInfo.InvariantCulture); Read();
    }
    private void Read()
    {
        if (!SelectIndex()) return;
        var p = _polyline!; var v = p.Vertices[_selected];
        _point.Text = FormattableString.Invariant($"{v.Position.X:R},{v.Position.Y:R},{v.Position.Z:R}");
        _start.Text = (p.ConstantWidth > 0 ? p.ConstantWidth : v.StartWidth).ToString("R", CultureInfo.InvariantCulture);
        _end.Text = (p.ConstantWidth > 0 ? p.ConstantWidth : v.EndWidth).ToString("R", CultureInfo.InvariantCulture); _bulge.Text = v.Bulge.ToString("R", CultureInfo.InvariantCulture);
        _status.Text = $"{p.Vertices.Length} vertices. Splitting preserves the arc and taper; deletion reconnects with a straight segment. Each edit is one Undo.";
    }
    private void Execute(Func<PolylineEntity, PolylineEntity> edit)
    {
        if (_session == null || _expected == null || !SelectIndex()) return;
        try
        {
            _session.EditVertex(_expected, edit);
            var next = _session.SelectedEntities().FirstOrDefault(e => e.Id == _expected.Id);
            if (next != null) { _expected = next; _polyline = (PolylineEntity)PolylineTools.Unwrap(next).Geometry; _index.Text = (Math.Min(_selected, _polyline.Vertices.Length - 1) + 1).ToString(CultureInfo.InvariantCulture); Read(); }
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or NotSupportedException) { _status.Text = error.Message; }
    }
    private void Apply() => Execute(p => {
        double Parse(TextBox box) => GeometryMath.Number(box.Text, out var n) ? n : throw new ArgumentException("Enter finite numeric values.");
        if (!GeometryMath.TryParsePoint(_point.Text, default, out var position)) throw new ArgumentException("Enter local x,y,z coordinates.");
        var vertices = p.ConstantWidth > 0 ? p.Vertices.Select(v => v with { StartWidth = p.ConstantWidth, EndWidth = p.ConstantWidth }).ToImmutableArray() : p.Vertices;
        return p with { ConstantWidth = 0, Vertices = vertices.SetItem(_selected, vertices[_selected] with { Position = position, StartWidth = Parse(_start), EndWidth = Parse(_end), Bulge = Parse(_bulge) }) };
    });
}
