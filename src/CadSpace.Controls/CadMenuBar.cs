using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace CadSpace.Controls;

/// <summary>Optional classic CAD menu bar. Items dispatch the same commands as ribbon, search and tool palettes.</summary>
public sealed class CadMenuBar : StackPanel
{
    public event Action<string>? CommandRequested;
    public CadMenuBar()
    {
        Orientation = Orientation.Horizontal; Height = 25; Background = CadTheme.Brush(0xFF273342);
        Add("File", ["NEW", "OPEN", "SAVE", "-", "EXPORT", "EXPORT_BINARY", "RECOVER", "-", "ABOUT"]);
        Add("Edit", ["UNDO", "REDO", "-", "ERASE", "SELECTALL", "QSELECT", "SELECTSIMILAR"]);
        Add("View", ["TOP", "3DORBIT", "ZOOM", "-", "PROPERTIES", "TOOLPALETTES", "RIBBON", "RIBBONCLOSE", "CLEANSCREENON", "CLEANSCREENOFF"]);
        Add("Draw", ["LINE", "PLINE", "CIRCLE", "ARC", "RECTANG", "POLYGON", "DONUT", "ELLIPSE", "SPLINE", "3DPOLY", "POINT", "HATCH", "TEXT"]);
        Add("Modify", ["MOVE", "COPY", "ROTATE", "SCALE", "MIRROR", "OFFSET", "-", "TRIM", "EXTEND", "FILLET", "CHAMFER", "STRETCH", "PEDIT", "JOIN", "MATCHPROP", "DDEDIT", "EATTEDIT", "EXPLODE"]);
        Add("Tools", ["LAYER", "LINETYPE", "BLOCK", "INSERT", "-", "DIST", "AREA", "OPTIONS"]);
        Add("Help", ["HELP", "ABOUT"]);
    }
    private void Add(string label, IEnumerable<string> commands)
    {
        var button = CadUi.TextButton(label, () => { }, "menubar." + label); button.Height = 25;
        button.Background = CadTheme.Brush(0x00273342); var menu = new MenuFlyout();
        foreach (var command in commands)
        {
            if (command == "-") { menu.Items.Add(new MenuFlyoutSeparator()); continue; }
            var text = command switch { "EXPORT_BINARY" => "Export binary DXF…", "RECOVER" => "Drawing recovery…", "CLEANSCREENON" => "Clean screen", "CLEANSCREENOFF" => "Restore screen", "RIBBONCLOSE" => "Minimize ribbon", "RIBBON" => "Expand ribbon", _ => CadUi.Label(command) };
            var item = CadUi.Identify(new MenuFlyoutItem { Text = text }, "menu." + label + "." + command, text);
            item.Click += (_, _) => CommandRequested?.Invoke(command); menu.Items.Add(item);
        }
        button.Flyout = menu; Children.Add(button);
    }
}
