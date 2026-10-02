using System.Collections.Immutable;
using System.Globalization;
using CadSpace.Engine;
using CadSpace.Geometry;
using CadSpace.Model;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace CadSpace.Controls;

/// <summary>Indexed staged leader vertex/arrow editing. Apply creates one Undo step; Cancel changes nothing.</summary>
public sealed class CadLeaderEditor : UserControl
{
    private readonly CadSession _session;
    private readonly Entity _expected;
    private ImmutableArray<Vec3> _vertices;
    private int _index;
    private readonly TextBox _vertex = new() { Header = "Local vertex (x,y,z)", FontSize = 12 };
    private readonly TextBox _size = new() { Header = "Arrow size (local drawing units)", FontSize = 12 };
    private readonly CheckBox _arrow = new() { Content = "Show arrowhead" };
    private readonly TextBlock _position = CadTheme.Text("", 12);
    private readonly TextBlock _status = CadTheme.Text("", 11, CadTheme.Muted);
    public CadLeaderEditor(CadSession session, Entity expected)
    {
        _session = session; _expected = expected;
        var leader = LeaderGeometry.Unwrap(expected) ?? throw new ArgumentException("Select a straight leader.");
        _vertices = leader.Vertices; _arrow.IsChecked = leader.ArrowEnabled; _size.Text = leader.ArrowSize.ToString("R", CultureInfo.InvariantCulture);
        CadUi.Identify(_vertex, "leader.vertex", "Leader vertex"); CadUi.Identify(_size, "leader.size", "Leader arrow size"); CadUi.Identify(_arrow, "leader.arrow", "Show leader arrow");
        var panel = new StackPanel { Spacing = 12, MinWidth = 350, MaxWidth = 580 };
        panel.Children.Add(_position); panel.Children.Add(_vertex);
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        void Button(string label, string id, Action action) => row.Children.Add(CadUi.TextButton(label, () => {
            try { StageVertex(); action(); ShowVertex(); }
            catch (Exception e) when (e is ArgumentException or InvalidOperationException) { ShowError(e.Message); }
        }, "leader." + id));
        Button("Previous", "previous", () => _index = Math.Max(0, _index-1));
        Button("Next", "next", () => _index = Math.Min(_vertices.Length-1, _index+1));
        Button("Split segment", "insert", () => {
            if (_index == _vertices.Length-1) throw new ArgumentException("The last vertex has no outgoing segment.");
            if (_vertices.Length >= LeaderGeometry.MaximumVertices) throw new ArgumentException("Leader vertex budget exceeded.");
            var mid = Vec3.Lerp(_vertices[_index], _vertices[_index+1], .5);
            _vertices = _vertices.Insert(++_index, mid);
        });
        Button("Delete vertex", "delete", () => {
            if (_vertices.Length <= 2) throw new ArgumentException("A leader requires at least two vertices.");
            _vertices = _vertices.RemoveAt(_index); _index = Math.Min(_index, _vertices.Length-1);
        });
        panel.Children.Add(row); panel.Children.Add(_arrow); panel.Children.Add(_size);
        panel.Children.Add(CadTheme.Text($"Source style: {leader.DimensionStyle} • annotation: {leader.AnnotationHandle}", 11, CadTheme.Muted));
        _status.Text = "Changes are staged until Apply. Linked annotation placement and private fields are not regenerated. Arrow-size changes use canonical DXF style overrides.";
        _status.TextWrapping = TextWrapping.Wrap; panel.Children.Add(_status); Content = panel; ShowVertex();
    }
    private void StageVertex()
    {
        if (!GeometryMath.TryParsePoint(_vertex.Text, default, out var p)) throw new ArgumentException("Enter a finite x,y,z vertex.");
        if (_vertices[_index] != p) _vertices = _vertices.SetItem(_index,p);
    }
    private void ShowVertex()
    {
        var p = _vertices[_index]; _vertex.Text = FormattableString.Invariant($"{p.X:R},{p.Y:R},{p.Z:R}");
        _position.Text = $"Vertex {_index+1} of {_vertices.Length}";
    }
    public void ShowError(string message) => _status.Text = message;
    public void Apply()
    {
        StageVertex(); if (!GeometryMath.Number(_size.Text, out var size)) throw new ArgumentException("Enter a finite arrow size.");
        _session.SetLeader(_expected, _vertices, _arrow.IsChecked == true, size);
    }
}
