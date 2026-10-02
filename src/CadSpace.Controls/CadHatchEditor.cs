using System.Collections.Immutable;
using System.Globalization;
using CadSpace.Engine;
using CadSpace.Geometry;
using CadSpace.Model;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace CadSpace.Controls;

/// <summary>Staged, reusable hatch editor. Retains a custom pattern unless explicitly replaced.</summary>
public sealed class CadHatchEditor : UserControl
{
    private readonly CadSession _session;
    private readonly Entity _expected;
    private readonly HatchRegionEntity _original;
    private readonly ComboBox _mode, _islands, _gradient;
    private readonly Dictionary<string, TextBox> _fields = new();
    private readonly TextBlock _status = CadTheme.Text("", 11, CadTheme.Muted);
    private readonly int _initialMode;
    public CadHatchEditor(CadSession session, Entity expected)
    {
        _session = session; _expected = expected; _original = HatchEditing.Unwrap(expected) ?? throw new ArgumentException("Select one hatch.");
        var h = _original;
        _initialMode = h.Gradient != null ? 4 : h.Solid ? 1 : 0;
        var panel = new StackPanel { Spacing = 10, MinWidth = 340, MaxWidth = 600 };
        ComboBox Combo(string name, string title, string[] items, int index)
        {
            var box = CadUi.Identify(new ComboBox { Header = title, ItemsSource = items, SelectedIndex = index, HorizontalAlignment = HorizontalAlignment.Stretch }, "hatch." + name, title);
            panel.Children.Add(box); return box;
        }
        TextBox Field(string name, string title, string value)
        {
            var box = CadUi.Identify(new TextBox { Header = title, Text = value, FontSize = 12 }, "hatch." + name, title);
            panel.Children.Add(box); _fields[name] = box; return box;
        }
        string N(double n) => n.ToString("R", CultureInfo.InvariantCulture);
        panel.Children.Add(CadTheme.Text($"{h.Loops.Length} boundary loops • {h.PatternName}", 14));
        _mode = Combo("mode", "Fill", ["Retain existing pattern", "Solid", "Single-line pattern", "Crosshatch", "Gradient"], _initialMode);
        _islands = Combo("islands", "Island detection", ["Normal", "Outer", "Ignore"], h.IslandStyle);
        Field("angle", "Pattern / gradient angle (degrees)", N(h.Gradient?.Angle ?? h.Pattern.FirstOrDefault()?.Angle ?? 45));
        Field("spacing", "Replacement pattern spacing", "10");
        var g = h.Gradient ?? new HatchGradient("LINEAR", 0xff387bc4, 0xffe6ba67);
        _gradient = Combo("gradient", "Gradient type", HatchGradient.Names.ToArray(), HatchGradient.Names.IndexOf(g.Name));
        Field("first", "First color (RGB)", (g.FirstColor & 0xffffff).ToString("X6"));
        Field("second", "Second color (RGB)", (g.SecondColor & 0xffffff).ToString("X6"));
        Field("shift", "Gradient shift (0–1)", N(g.Shift));
        _status.Text = "Apply commits one Undo step. Custom patterns and analytic boundary bulges remain intact until explicitly replaced. Nonlinear gradient display is approximate.";
        _status.TextWrapping = TextWrapping.Wrap; panel.Children.Add(_status);
        void EnableFields()
        {
            var mode = _mode.SelectedIndex;
            _fields["angle"].IsEnabled = mode is 2 or 3 or 4;
            _fields["spacing"].IsEnabled = mode is 2 or 3;
            _gradient.IsEnabled = mode == 4;
            foreach (var key in new[] { "first", "second", "shift" }) _fields[key].IsEnabled = mode == 4;
        }
        _mode.SelectionChanged += (_, _) => EnableFields(); EnableFields();
        Content = new ScrollViewer { Content = panel, MaxHeight = 600, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
    }
    public void ShowError(string message) => _status.Text = message;
    public void Apply()
    {
        double N(string key) => GeometryMath.Number(_fields[key].Text, out var n) ? n : throw new ArgumentException("Enter a finite value for " + key + ".");
        uint Color(string key)
        {
            var s = _fields[key].Text.Trim().TrimStart('#');
            return s.Length == 6 && uint.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var c) ? 0xff000000 | c : throw new ArgumentException("Use six hexadecimal RGB digits.");
        }
        var mode = _mode.SelectedIndex; var h = _original;
        HatchGradient? gradient = mode == 4 ? new((string)_gradient.SelectedItem, Color("first"), Color("second"), N("angle"), N("shift"), h.Gradient?.SingleColor ?? false, h.Gradient?.Tint ?? 0) : mode == 0 ? h.Gradient : null;
        var solid = mode is 1 or 4 || mode == 0 && h.Solid;
        var retain = mode == 0 || mode == _initialMode && mode is 1 or 4;
        var pattern = mode is 2 or 3 ? HatchEditing.Pattern(N("angle"), N("spacing"), default, mode == 3) : retain ? h.Pattern : ImmutableArray<HatchPatternLine>.Empty;
        _session.SetFill(_expected, solid, pattern, retain ? h.PatternName : solid ? "SOLID" : "USER", _islands.SelectedIndex, gradient);
    }
}
