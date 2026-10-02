using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace CadSpace.Controls;

/// <summary>Shared compact control construction and stable accessibility identifiers.</summary>
public static class CadUi
{
    public static T Identify<T>(T element, string id, string name) where T : DependencyObject
    { AutomationProperties.SetAutomationId(element, id); AutomationProperties.SetName(element, name); return element; }
    public static Button IconButton(string icon, string title, Action action, string? id = null, double size = 28)
    {
        var button = CadTheme.Button(title, action, size); button.Height = size; button.MinHeight = size;
        button.Padding = new Thickness(4); button.Background = CadTheme.Brush(0x00252B34);
        button.Content = new CadIcon { Kind = icon, Width = size - 8, Height = size - 8, IsHitTestVisible = false };
        ToolTipService.SetToolTip(button, title); return Identify(button, id ?? "ui." + icon, title);
    }
    public static Button TextButton(string text, Action action, string id)
    {
        var button = CadTheme.Button(text, action); button.FontSize = 11; button.Height = 26; button.MinHeight = 24;
        button.Padding = new Thickness(7, 2, 7, 2); button.CornerRadius = new CornerRadius(0);
        return Identify(button, id, text);
    }
    /// <summary>Compose chrome symbols as vectors, including labels with a trailing arrow.</summary>
    public static void SetButtonLabel(Button button, string text)
    {
        if (CadChromeGlyph.Supports(text)) { SetGlyph(button, text); return; }
        if (text.Length > 2 && text[^2] == ' ' && CadChromeGlyph.Supports(text[^1].ToString()))
        {
            // Leave FontSize unset so the caption follows its owning button's size.
            var label = new TextBlock { Text = text[..^2], FontFamily = CadTheme.UiFont, VerticalAlignment = VerticalAlignment.Center };
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, IsHitTestVisible = false };
            row.Children.Add(label);
            row.Children.Add(new CadChromeGlyph { Symbol = text[^1].ToString(), Width = 10, Height = 10, VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false });
            button.Content = row;
        }
        else button.Content = text;
    }
    public static void SetGlyph(Button button, string symbol, uint color = CadTheme.TextColor)
    {
        if (!CadChromeGlyph.Supports(symbol)) throw new ArgumentException("Unknown workspace glyph.");
        button.Content = new CadChromeGlyph { Symbol = symbol, Color = color, Width = 13, Height = 13, IsHitTestVisible = false };
    }
    public static void DescribeDialog(ContentDialog dialog, string id)
    {
        Identify(dialog, id, dialog.Title?.ToString() ?? "Dialog");
        dialog.Loaded += (_, _) => {
            void Visit(DependencyObject node)
            {
                if (node is Button button && button.Name is "PrimaryButton" or "SecondaryButton" or "CloseButton")
                    Identify(button, id + "." + button.Name, button.Content?.ToString() ?? button.Name);
                for (var i = 0; i < Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(node); i++) Visit(Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(node, i));
            }
            Visit(dialog);
        };
    }
    public static string Label(string command) => command switch {
        "GRADIENT" => "Gradient", "HATCHEDIT" => "Edit Hatch", "HATCHGENERATEBOUNDARY" => "Boundary",
        "SPLINEDIT" => "Edit Spline",
        "DIMLINEAR" => "Linear", "DIMROTATED" => "Rotated", "DIMANGULAR" => "Angular", "DIMANGULAR2" => "2-Line Angular",
        "DIMRADIUS" => "Radius", "DIMDIAMETER" => "Diameter", "DIMORDINATE" => "Ordinate", "DIMEDIT" => "Edit Dimension",
        "LEADER" => "Leader", "LEADEREDIT" => "Edit Leader",
        "MTEXT" => "Multiline Text", "DDEDIT" => "Edit Text", "EATTEDIT" => "Edit Attributes",
        "MATCHPROP" => "Match Properties", "DONUT" => "Donut", "POLYGON" => "Polygon",
        "LAYOUT_NEW" => "New Layout", "LAYOUT_RENAME" => "Rename", "LAYOUT_DELETE" => "Delete Layout", "CLEANSCREENON" => "Clean Screen",
        "PLINE" => "Polyline", "RECTANG" => "Rectangle", "DIMALIGNED" => "Dimension", "3DORBIT" => "Orbit",
        "QSELECT" => "Quick Select", "SELECTSIMILAR" => "Select Similar", "PEDIT" => "Edit Polyline", "PLINEWID" => "Width",
        "LAYER" => "Layer Properties", "LINETYPE" => "Linetypes", "TOOLPALETTES" => "Tool Palettes", "PROPERTIES" => "Properties",
        "RENDERSTATS" => "Render Statistics", "UISTATS" => "UI Diagnostics", "VSCURRENT" => "Visual Style",
        "EXPORT_BINARY" => "Binary DXF", "EXPORT" => "Export DXF", "NEW" => "New", "OPEN" => "Open", "SAVE" => "Save",
        _ => command.Length > 1 ? char.ToUpper(command[0]) + command[1..].ToLowerInvariant() : command
    };
}
