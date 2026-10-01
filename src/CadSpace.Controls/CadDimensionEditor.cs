using System.Globalization;
using CadSpace.Engine;
using CadSpace.Geometry;
using CadSpace.Model;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace CadSpace.Controls;

/// <summary>Reusable staged dimension formatting editor. Apply is a single document transaction.</summary>
public sealed class CadDimensionEditor : UserControl
{
    private readonly CadSession _session;
    private readonly Entity _expected;
    private readonly DimensionEntity _dimension;
    private readonly Dictionary<string, TextBox> _fields = new();
    private readonly TextBlock _status = CadTheme.Text("", 11, CadTheme.Muted);
    private readonly CheckBox _zeros = new() { Content = "Suppress trailing zeroes" };
    public CadDimensionEditor(CadSession session, Entity expected)
    {
        _session = session; _expected = expected;
        _dimension = DimensionGeometry.Unwrap(expected) ?? throw new ArgumentException("Select a modeled dimension.");
        var d = _dimension; var f = d.Format;
        var body = new StackPanel { Spacing = 10, MinWidth = 360, MaxWidth = 600 };
        body.Children.Add(CadTheme.Text($"{d.Type}   •   local measurement {DimensionGeometry.Measure(d):0.######}", 14));
        TextBox Field(string key, string label, string value)
        {
            var box = CadUi.Identify(new TextBox { Header = label, Text = value, FontSize = 12, MinWidth = 105, MinHeight = 34 }, "dimension." + key, label);
            _fields[key] = box; return box;
        }
        body.Children.Add(Field("text", "Text override (<> = measurement; one space hides text)", d.TextOverride));
        body.Children.Add(Field("template", "Measurement template", f.TextTemplate));
        void Row(params (string Key, string Label, double Value)[] values)
        {
            var row = new Grid { ColumnSpacing = 10 };
            foreach (var (key, label, value) in values)
            {
                row.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
                var box = Field(key, label, value.ToString("R", CultureInfo.InvariantCulture));
                Grid.SetColumn(box, row.ColumnDefinitions.Count - 1); row.Children.Add(box);
            }
            body.Children.Add(row);
        }
        Row(("height", "Text height", f.TextHeight), ("arrow", "Arrow size", f.ArrowSize), ("gap", "Text gap", f.Gap));
        Row(("offset", "Extension offset", f.ExtensionOffset), ("beyond", "Extension beyond", f.ExtensionBeyond), ("scale", "Overall scale", f.Scale));
        Row(("precision", "Decimal places", f.Precision), ("angularPrecision", "Angular places", f.AngularPrecision), ("measurementScale", "Measurement factor", f.MeasurementScale));
        body.Children.Add(Field("location", "Local dimension line / leader position", FormattableString.Invariant($"{d.Location.X:R},{d.Location.Y:R},{d.Location.Z:R}")));
        Row(("rotation", "Local axis rotation (degrees)", d.Rotation));
        _fields["rotation"].IsEnabled = d.Type is DimensionKind.Rotated or DimensionKind.Ordinate;
        _zeros.IsChecked = f.SuppressTrailingZeros; CadUi.Identify(_zeros, "dimension.zeros", "Suppress trailing zeroes"); body.Children.Add(_zeros);
        _status.Text = "Apply is one Undo step. Cancel preserves the drawing. Definition changes regenerate the picture using supported decimal formatting; private styles, associations and fields are not rebuilt.";
        _status.TextWrapping = TextWrapping.Wrap; body.Children.Add(_status);
        Content = new ScrollViewer { Content = body, MaxHeight = 610, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
    }
    public void ShowError(string message) => _status.Text = message;
    public void Apply()
    {
        double N(string key) => GeometryMath.Number(_fields[key].Text, out var n) ? n : throw new ArgumentException("Enter a finite value for " + key + ".");
        int Places(string key) { var n = N(key); return n >= 0 && n <= 8 && n == Math.Truncate(n) ? (int)n : throw new ArgumentException("Decimal places must be an integer from 0 to 8."); }
        if (!GeometryMath.TryParsePoint(_fields["location"].Text, default, out var point)) throw new ArgumentException("Enter the local position as x,y,z.");
        var f = _dimension.Format with { TextHeight = N("height"), ArrowSize = N("arrow"), Gap = N("gap"), ExtensionOffset = N("offset"), ExtensionBeyond = N("beyond"),
            Scale = N("scale"), MeasurementScale = N("measurementScale"), Precision = Places("precision"), AngularPrecision = Places("angularPrecision"),
            SuppressTrailingZeros = _zeros.IsChecked == true, TextTemplate = _fields["template"].Text };
        _session.SetDimensionProperties(_expected, f, _fields["text"].Text, point, N("rotation"));
    }
}
