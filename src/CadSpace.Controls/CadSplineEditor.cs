using System.Globalization;
using CadSpace.Engine;
using CadSpace.Geometry;
using CadSpace.Model;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace CadSpace.Controls;

/// <summary>Indexed spline editor with bounded UI size; each successful action is one Undo step.</summary>
public sealed class CadSplineEditor : UserControl
{
    private readonly CadSession _session;
    private Entity _expected;
    private SplineEntity _spline;
    private readonly TextBox _index, _point, _weight, _knot;
    private readonly TextBlock _status = CadTheme.Text("", 11, CadTheme.Muted);
    private int _loadedIndex;
    public Func<bool>? IsCurrentSession { get; init; }
    public CadSplineEditor(CadSession session, Entity expected)
    {
        _session = session; _expected = expected; _spline = SplineEditing.Unwrap(expected) ?? throw new ArgumentException("A spline is required.");
        var body = new StackPanel { Spacing = 10, MinWidth = 340, MaxWidth = 540 };
        TextBox Field(string name, string title, string value) => CadUi.Identify(new TextBox { Header = title, Text = value, FontSize = 12, MinHeight = 34 }, "spline." + name, title);
        _index = Field("index", "Control point (1-based)", "1");
        _point = Field("point", "Local position (x,y,z)", "0,0,0");
        _weight = Field("weight", "Rational weight (> 0)", "1");
        _knot = Field("knot", "Interior knot parameter", (_spline.Knots[_spline.Degree] / 2 + _spline.Knots[_spline.ControlPoints.Length] / 2).ToString("R", CultureInfo.InvariantCulture));
        body.Children.Add(_index);
        var navigation = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5 };
        navigation.Children.Add(CadUi.TextButton("Previous", () => Step(-1), "spline.previous"));
        navigation.Children.Add(CadUi.TextButton("Read point", ReadPoint, "spline.read"));
        navigation.Children.Add(CadUi.TextButton("Next", () => Step(1), "spline.next")); body.Children.Add(navigation);
        body.Children.Add(_point); body.Children.Add(_weight);
        var apply = CadUi.TextButton("Apply control point", ApplyPoint, "spline.apply"); body.Children.Add(apply);
        body.Children.Add(_knot);
        var insert = CadUi.TextButton("Insert knot (preserve curve)", () => Execute(p => SplineEditing.InsertKnot(p, Number(_knot))), "spline.insert"); body.Children.Add(insert);
        _status.TextWrapping = TextWrapping.Wrap; body.Children.Add(_status);
        var help = CadTheme.Text("Each action commits one Undo step. Close does not revert edits. Knot insertion refines the control structure without intentionally changing the curve; moving a control point or changing its weight changes the curve.", 11, CadTheme.Muted);
        help.TextWrapping = TextWrapping.Wrap; body.Children.Add(help);
        apply.IsEnabled = insert.IsEnabled = !_spline.Periodic;
        _point.IsReadOnly = _weight.IsReadOnly = _spline.Periodic;
        Content = new ScrollViewer { Content = body, MaxHeight = 580, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        ReadPoint();
    }
    private static double Number(TextBox box) => GeometryMath.Number(box.Text, out var n) ? n : throw new ArgumentException("Enter a finite numeric value.");
    private int Index()
    {
        if (!int.TryParse(_index.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) || n < 1 || n > _spline.ControlPoints.Length)
            throw new ArgumentException($"Choose a control point from 1 to {_spline.ControlPoints.Length}.");
        return n - 1;
    }
    private void Step(int direction)
    {
        try { _index.Text = (Math.Clamp(Index() + direction, 0, _spline.ControlPoints.Length - 1) + 1).ToString(CultureInfo.InvariantCulture); ReadPoint(); }
        catch (ArgumentException e) { _status.Text = e.Message; }
    }
    private void ReadPoint()
    {
        try
        {
            _loadedIndex = Index(); var p = _spline.ControlPoints[_loadedIndex];
            _point.Text = FormattableString.Invariant($"{p.X:R},{p.Y:R},{p.Z:R}");
            _weight.Text = (_spline.Weights.IsEmpty ? 1 : _spline.Weights[_loadedIndex]).ToString("R", CultureInfo.InvariantCulture);
            _status.Text = FormattableString.Invariant($"Degree {_spline.Degree}; {_spline.ControlPoints.Length} control points; {_spline.Knots.Length} knots. Domain: {_spline.Knots[_spline.Degree]:R} to {_spline.Knots[_spline.ControlPoints.Length]:R}.") +
                (_spline.Periodic ? " Periodic seam editing is read only." : "");
        }
        catch (ArgumentException e) { _status.Text = e.Message; }
    }
    private void ApplyPoint() => Execute(p => {
        var index = Index(); if (index != _loadedIndex) throw new ArgumentException("Read the chosen control point before applying its fields.");
        if (!GeometryMath.TryParsePoint(_point.Text, default, out var point)) throw new ArgumentException("Enter a local position as x,y,z.");
        return SplineEditing.SetControlPoint(p, index, point, Number(_weight));
    });
    private void Execute(Func<SplineEntity, SplineEntity> edit)
    {
        try
        {
            if (IsCurrentSession?.Invoke() == false) throw new InvalidOperationException("The active drawing changed. Reopen the editor.");
            _session.EditSpline(_expected, edit);
            _expected = _session.Document.Drawing.Entities.First(e => e.Id == _expected.Id);
            _spline = SplineEditing.Unwrap(_expected)!; ReadPoint();
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or NotSupportedException) { _status.Text = e.Message; }
    }
}
