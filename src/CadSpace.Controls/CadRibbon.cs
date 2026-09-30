using CadSpace.Engine;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace CadSpace.Controls;

/// <summary>Reusable compact ribbon panel, with a caption and optional dialog launcher.</summary>
public sealed class CadRibbonPanel : UserControl
{
    public StackPanel Items { get; } = new() { Orientation = Orientation.Horizontal, Spacing = 2 };
    public CadRibbonPanel(string title, Action? launch = null)
    {
        var grid = CadTheme.Grid(73, 17); CadTheme.At(grid, Items, 0);
        var footer = new Grid(); var caption = CadTheme.Text(title, 10, CadTheme.Muted); caption.HorizontalAlignment = HorizontalAlignment.Center; footer.Children.Add(caption);
        if (launch != null) { var button = CadUi.TextButton("↗", launch, "panel." + title); button.MinHeight = button.Height = 17; button.Width = 19; button.Padding = new Thickness(0); button.HorizontalAlignment = HorizontalAlignment.Right; footer.Children.Add(button); }
        CadTheme.At(grid, footer, 1); Content = new Border { Child = grid, Padding = new Thickness(6, 2, 6, 0), BorderBrush = CadTheme.Brush(CadTheme.Edge), BorderThickness = new Thickness(0, 0, 1, 0) };
    }
}

/// <summary>Large command button with a separate, accessible alternatives dropdown.</summary>
public sealed class CadRibbonButton : UserControl
{
    public CadRibbonButton(string command, Action<string> invoke, params string[] alternatives)
    {
        var title = CadUi.Label(command);
        // Keep long command captions legible; overflow is handled by the ribbon, not by clipping names.
        var width = Math.Clamp(title.Length * 5.3 + (alternatives.Length > 0 ? 18 : 10), 54, 112);
        var body = CadTheme.Grid(54, 18); body.Width = width;
        var button = CadUi.IconButton(command, title, () => invoke(command), "command." + command, 52);
        button.Width = width - 2;
        button.Content = new CadIcon { Kind = command, Width = 33, Height = 33, IsHitTestVisible = false }; CadTheme.At(body, button, 0);
        var label = CadUi.TextButton(title + (alternatives.Length > 0 ? " ▾" : ""), () => { if (alternatives.Length == 0) invoke(command); }, "label." + command);
        ToolTipService.SetToolTip(label, title);
        label.MinHeight = label.Height = 18; label.FontSize = 10; label.Padding = new Thickness(0); label.HorizontalAlignment = HorizontalAlignment.Stretch;
        if (alternatives.Length > 0)
        {
            var menu = new MenuFlyout();
            foreach (var name in new[] { command }.Concat(alternatives)) { var item = new MenuFlyoutItem { Text = CadUi.Label(name) }; item.Click += (_, _) => invoke(name); menu.Items.Add(item); }
            label.Flyout = menu;
        }
        CadTheme.At(body, label, 1); Content = body;
    }
}

/// <summary>AutoCAD-style tab/panel organization; every displayed command routes through one dispatcher.</summary>
public sealed class CadRibbon : UserControl
{
    private readonly Grid _root = CadTheme.Grid(28, 92);
    private readonly StackPanel _groups = new() { Orientation = Orientation.Horizontal };
    private readonly StackPanel _tabStrip = new() { Orientation = Orientation.Horizontal };
    private readonly Dictionary<string, Button> _tabs = new();
    private readonly ScrollViewer _scroll;
    private readonly CadPropertySelectors _layer = new(true), _properties = new();
    private string _tab = "Home", _context = "";
    public event Action<string>? CommandRequested;
    public event Action<bool>? MinimizedChanged;
    public event Action<string>? Message;
    public bool IsMinimized { get; private set; }
    public string SelectedTab => _tab;
    public CadRibbon()
    {
        foreach (var name in new[] { "Home", "Insert", "Annotate", "Layout", "Modify", "3D Modeling", "View", "Manage", "Output" }) AddTab(name);
        var tabScroll = new ScrollViewer { Content = _tabStrip, HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, HorizontalScrollMode = ScrollMode.Enabled };
        var tabs = new Grid(); tabs.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) }); tabs.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        tabs.Children.Add(tabScroll); var minimize = CadUi.TextButton("⌃", ToggleMinimized, "ribbon.minimize"); Grid.SetColumn(minimize, 1); tabs.Children.Add(minimize); CadTheme.At(_root, tabs, 0);
        _scroll = new ScrollViewer { Content = _groups, HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, HorizontalScrollMode = ScrollMode.Enabled, Background = CadTheme.Brush(CadTheme.Panel) };
        var content = new Grid(); content.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) }); content.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); content.Children.Add(_scroll);
        var arrows = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        arrows.Children.Add(CadUi.TextButton("‹", () => _scroll.ChangeView(Math.Max(0, _scroll.HorizontalOffset - 220), null, null), "ribbon.previous"));
        arrows.Children.Add(CadUi.TextButton("›", () => _scroll.ChangeView(_scroll.HorizontalOffset + 220, null, null), "ribbon.next")); Grid.SetColumn(arrows, 1); content.Children.Add(arrows);
        void Overflow() => arrows.Visibility = _scroll.ExtentWidth > _scroll.ViewportWidth + 2 ? Visibility.Visible : Visibility.Collapsed;
        _scroll.SizeChanged += (_, _) => Overflow(); _scroll.ViewChanged += (_, _) => Overflow(); _groups.SizeChanged += (_, _) => Overflow();
        CadTheme.At(_root, content, 1); Content = _root;
        _layer.CommandRequested += Invoke; _properties.CommandRequested += Invoke; _layer.Message += m => Message?.Invoke(m); _properties.Message += m => Message?.Invoke(m);
        Show("Home");
    }
    private void AddTab(string name)
    {
        var button = CadUi.TextButton(name, () => Show(name), "tab." + name); button.Padding = new Thickness(12, 2, 12, 2);
        _tabs.Add(name, button); _tabStrip.Children.Add(button);
    }
    private void Invoke(string command) => CommandRequested?.Invoke(command);
    public void Bind(CadSession session) { _layer.Bind(session); _properties.Bind(session); }
    public void ToggleMinimized() => SetMinimized(!IsMinimized);
    public void SetMinimized(bool minimized)
    {
        if (IsMinimized == minimized) return; IsMinimized = minimized;
        _root.RowDefinitions[1].Height = new GridLength(minimized ? 0 : 92); _groups.Visibility = minimized ? Visibility.Collapsed : Visibility.Visible; MinimizedChanged?.Invoke(minimized);
    }
    public void SetContext(string context)
    {
        if (_context == context) return;
        if (_context.Length > 0 && _tabs.Remove(_context, out var old)) _tabStrip.Children.Remove(old);
        var wasContext = _tab == _context; _context = context;
        if (context.Length > 0) { AddTab(context); _tabs[context].Foreground = CadTheme.Brush(0xFF8ACAC1); }
        if (wasContext) Show(context.Length > 0 ? context : "Home");
    }
    public void Show(string tab)
    {
        SetMinimized(false); _tab = tab;
        foreach (var (name, button) in _tabs) button.Background = CadTheme.Brush(name == tab ? CadTheme.Panel : CadTheme.Background);
        // Selectors are retained; detach them before their former panel goes out of scope.
        foreach (var p in _groups.Children.OfType<CadRibbonPanel>()) p.Items.Children.Clear(); _groups.Children.Clear();
        void Group(string title, params string[] commands)
        {
            var panel = new CadRibbonPanel(title); foreach (var cmd in commands) panel.Items.Children.Add(new CadRibbonButton(cmd, Invoke)); _groups.Children.Add(panel);
        }
        void Small(CadRibbonPanel panel, params string[] commands)
        {
            foreach (var column in commands.Chunk(3))
            {
                var stack = new StackPanel { Spacing = 1 };
                foreach (var cmd in column) stack.Children.Add(CadUi.IconButton(cmd, CadUi.Label(cmd), () => Invoke(cmd), "command." + cmd, 23));
                panel.Items.Children.Add(stack);
            }
        }
        switch (tab)
        {
            case "Home":
                var draw = new CadRibbonPanel("Draw");
                draw.Items.Children.Add(new CadRibbonButton("LINE", Invoke, "3DPOLY")); draw.Items.Children.Add(new CadRibbonButton("PLINE", Invoke, "SPLINE"));
                draw.Items.Children.Add(new CadRibbonButton("CIRCLE", Invoke, "ELLIPSE", "DONUT")); draw.Items.Children.Add(new CadRibbonButton("ARC", Invoke));
                draw.Items.Children.Add(new CadRibbonButton("RECTANG", Invoke, "POLYGON")); Small(draw, "HATCH", "POINT"); _groups.Children.Add(draw);
                var modify = new CadRibbonPanel("Modify", () => Show("Modify")); Small(modify, "MOVE", "COPY", "STRETCH", "ROTATE", "MIRROR", "SCALE", "TRIM", "EXTEND", "OFFSET", "FILLET", "CHAMFER", "ERASE"); _groups.Children.Add(modify);
                Group("Annotation", "TEXT", "DIMALIGNED");
                var layers = new CadRibbonPanel("Layers", () => Invoke("LAYER")); layers.Items.Children.Add(_layer); _groups.Children.Add(layers);
                var blocks = new CadRibbonPanel("Block"); blocks.Items.Children.Add(new CadRibbonButton("INSERT", Invoke, "BLOCK", "EXPLODE")); _groups.Children.Add(blocks);
                var properties = new CadRibbonPanel("Properties", () => Invoke("PROPERTIES")); properties.Items.Children.Add(_properties); _groups.Children.Add(properties);
                var utilities = new CadRibbonPanel("Utilities"); utilities.Items.Children.Add(new CadRibbonButton("DIST", Invoke, "AREA")); Small(utilities, "QSELECT", "SELECTSIMILAR", "SELECTALL"); _groups.Children.Add(utilities); break;
            case "Modify": Group("Transform", "MOVE", "COPY", "ROTATE", "SCALE", "MIRROR", "STRETCH"); Group("Edit", "TRIM", "EXTEND", "FILLET", "CHAMFER", "BREAK", "JOIN"); Group("Polyline", "PEDIT", "PLINEWID"); Group("Properties", "MATCHPROP"); Group("Pattern", "OFFSET", "ARRAY", "EXPLODE", "ERASE"); break;
            case "Insert": Group("Blocks", "INSERT", "BLOCK", "EATTEDIT", "EXPLODE"); Group("Pattern", "ARRAY"); Group("Content", "TOOLPALETTES", "OPEN"); break;
            case "Annotate": Group("Text", "TEXT", "DDEDIT"); Group("Dimensions", "DIMALIGNED", "DIST", "AREA"); Group("Hatching", "HATCH"); break;
            case "Layout": Group("Layouts", "LAYOUT_NEW", "LAYOUT_RENAME", "LAYOUT_DELETE"); Group("View", "TOP", "ZOOM"); break;
            case "3D Modeling": Group("Mesh Primitives", "BOX", "CYLINDER", "SPHERE", "CONE"); Group("Mesh Surfaces", "EXTRUDE", "REVOLVE", "SWEEP", "LOFT"); Group("Mesh Booleans", "UNION", "SUBTRACT", "INTERSECT"); Group("Transform", "ROTATE3D", "MIRROR3D", "ALIGN3D"); break;
            case "View": Group("Views", "TOP", "3DORBIT", "ZOOM"); Group("Palettes", "PROPERTIES", "TOOLPALETTES", "LAYER", "LINETYPE"); Group("Workspace", "OPTIONS", "CLEANSCREENON"); break;
            case "Manage": Group("Drawing", "LAYER", "LINETYPE", "QSELECT"); Group("Workspace", "OPTIONS", "RENDERSTATS", "UISTATS"); Group("Recovery", "RECOVER"); break;
            case "Output": Group("Native Project", "SAVE"); Group("Interchange", "EXPORT", "EXPORT_BINARY"); break;
            case "Polyline": Group("Polyline", "PEDIT", "JOIN", "PLINEWID", "STRETCH", "EXPLODE"); Group("Modify", "MOVE", "COPY", "SCALE", "ERASE"); break;
            case "Mesh": Group("Mesh", "UNION", "SUBTRACT", "INTERSECT", "ROTATE3D", "MIRROR3D"); Group("View", "3DORBIT", "ZOOM"); break;
            default: Group("Selection", "MOVE", "COPY", "ERASE", "SELECTSIMILAR", "PROPERTIES"); break;
        }
    }
}
