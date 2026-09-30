using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using CadSpace.Engine;
using CadSpace.Model;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
using Windows.System;

namespace CadSpace.Controls;

/// <summary>Composable CAD workspace. All UI entry points share command/document/selection state.</summary>
public sealed partial class CadWorkspace : UserControl
{
    public CadApplicationBar ApplicationBar { get; } = new();
    public CadRibbon Ribbon { get; } = new();
    public CadMenuBar MenuBar { get; } = new() { Visibility = Visibility.Collapsed };
    public CadViewport Viewport { get; } = new();
    public CadCommandLine CommandLine { get; } = new();
    public CadPalette Palette { get; } = new();
    public CadToolPalette ToolPalette { get; } = new();
    public CadDocumentTabs DocumentTabs { get; } = new();
    public CadStatusBar StatusBar { get; } = new();
    public CadLayoutTabs LayoutTabs { get; } = new();
    public CadDockHost DockHost { get; }
    public event Action<string>? FileRequested;
    public event Action<WorkspaceLayout>? PreferencesChanged;
    private CommandEngine? _commands; private CadSession? _session;
    private readonly Grid _root = CadTheme.Grid(34, 120, 30, -1, 78, 27);
    private string _workspaceName = WorkspaceLayout.Presets[0];
    private bool _cleanScreen, _menuBarVisible, _restoring, _optionsDialog;
    private Action? _cancelConsoleResize;
    private Drawing? _selectionDrawing; private long _selectionRevision = -1;
    public CadWorkspace()
    {
        CadUi.Identify(Viewport, "viewport.surface", "CAD drawing viewport");
        RequestedTheme = ElementTheme.Dark; _root.Background = CadTheme.Brush(CadTheme.Background);
        DockHost = new(Viewport); DockHost.AddPane("properties", "Properties", Palette, new("properties")); DockHost.AddPane("tools", "Tool Palettes", ToolPalette, new("tools", PaletteDock.Left, false));
        var header = new StackPanel(); header.Children.Add(ApplicationBar); header.Children.Add(MenuBar);
        CadTheme.At(_root, header, 0); CadTheme.At(_root, Ribbon, 1); CadTheme.At(_root, DocumentTabs, 2); CadTheme.At(_root, DockHost, 3);
        var console = CadTheme.Grid(4, -1); var resize = new Border { Background = CadTheme.Brush(CadTheme.Edge) }; CadUi.Identify(resize, "command.resize", "Resize command window");
        CadTheme.At(console, resize, 0); CadTheme.At(console, CommandLine, 1); CadTheme.At(_root, console, 4);
        bool dragging = false; double start = 0, height = 0;
        resize.PointerPressed += (_, e) => { if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return; CancelShellInteraction(); dragging = true; start = e.GetCurrentPoint(this).Position.Y; height = CommandLine.PanelHeight;
            _cancelConsoleResize = () => { dragging = false; CommandLine.ResizeTo(height); resize.ReleasePointerCaptures(); }; resize.CapturePointer(e.Pointer); e.Handled = true; };
        resize.PointerMoved += (_, e) => { if (dragging) CommandLine.ResizeTo(Math.Clamp(height + start - e.GetCurrentPoint(this).Position.Y, 74, 350)); };
        resize.PointerReleased += (_, e) => { if (!dragging) return; dragging = false; _cancelConsoleResize = null; resize.ReleasePointerCapture(e.Pointer); NotifyPreferences(); e.Handled = true; };
        resize.PointerCaptureLost += (_, _) => { if (!dragging) return; dragging = false; _cancelConsoleResize = null; CommandLine.ResizeTo(height); };
        CommandLine.HeightChanged += h => { _root.RowDefinitions[4].Height = new GridLength(h + 4); if (!dragging) NotifyPreferences(); };
        var bottom = new Grid(); bottom.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) }); bottom.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        bottom.Children.Add(LayoutTabs); Grid.SetColumn(StatusBar, 1); bottom.Children.Add(StatusBar); CadTheme.At(_root, bottom, 5); Content = _root;
        CadUi.Identify(CommandLine.Input, "command.input", "CAD command input");
        MenuBar.CommandRequested += Invoke; ApplicationBar.CommandRequested += Invoke; Ribbon.CommandRequested += Invoke; ToolPalette.CommandRequested += Invoke;
        Ribbon.Message += CommandLine.AddMessage; ToolPalette.InsertRequested += Insert; Palette.InsertRequested += Insert;
        Palette.AnnotationEditRequested += () => Invoke("DDEDIT");
        Palette.Message += CommandLine.AddMessage; Viewport.Message += CommandLine.AddMessage; Viewport.CoordinatesChanged += StatusBar.SetCoordinates;
        DocumentTabs.NewRequested += () => Invoke("NEW");
        DocumentTabs.DocumentsChanged += () => ApplicationBar.ApplicationMenu.SetDocuments(DocumentTabs.Documents);
        ApplicationBar.ApplicationMenu.ActivateRequested += key => DocumentTabs.RequestActivation(key);
        LayoutTabs.LayoutActivated += () => { Viewport.CancelInteraction(); _commands?.Cancel(); Viewport.Set3D(false); Viewport.Fit(); };
        LayoutTabs.RenameRequested += RenameLayout; LayoutTabs.Message += CommandLine.AddMessage;
        StatusBar.WorkspaceRequested += SetWorkspace; StatusBar.OptionsRequested += ShowOptions; StatusBar.CustomizationChanged += NotifyPreferences;
        DockHost.LayoutChanged += NotifyPreferences;
        Ribbon.MinimizedChanged += _ => { ApplyChrome(); NotifyPreferences(); };
        CommandLine.CancelRequested += CancelShellInteraction;
        AddShortcut(VirtualKey.N, VirtualKeyModifiers.Control, () => Invoke("NEW")); AddShortcut(VirtualKey.O, VirtualKeyModifiers.Control, () => Invoke("OPEN")); AddShortcut(VirtualKey.S, VirtualKeyModifiers.Control, () => Invoke("SAVE"));
        AddShortcut(VirtualKey.E, VirtualKeyModifiers.Control | VirtualKeyModifiers.Shift, () => Invoke("EXPORT")); AddShortcut(VirtualKey.R, VirtualKeyModifiers.Control | VirtualKeyModifiers.Shift, () => Invoke("RECOVER"));
        AddShortcut(VirtualKey.Z, VirtualKeyModifiers.Control, () => Invoke("UNDO"), true); AddShortcut(VirtualKey.Y, VirtualKeyModifiers.Control, () => Invoke("REDO"), true); AddShortcut(VirtualKey.A, VirtualKeyModifiers.Control, () => Invoke("SELECTALL"), true);
        AddShortcut(VirtualKey.Delete, VirtualKeyModifiers.None, () => { if (_session?.Selection.Count > 0) Invoke("ERASE"); }, true);
        AddShortcut(VirtualKey.Number1, VirtualKeyModifiers.Control, () => DockHost.Toggle("properties")); AddShortcut(VirtualKey.Number3, VirtualKeyModifiers.Control, () => DockHost.Toggle("tools"));
        AddShortcut(VirtualKey.Number0, VirtualKeyModifiers.Control, () => { _cleanScreen = !_cleanScreen; ApplyChrome(); NotifyPreferences(); });
        AddShortcut(VirtualKey.F2, VirtualKeyModifiers.None, CommandLine.ToggleHistory); AddShortcut(VirtualKey.F6, VirtualKeyModifiers.None, CommandLine.FocusInput);
        AddShortcut(VirtualKey.K, VirtualKeyModifiers.Control, () => ApplicationBar.Search.Focus(FocusState.Programmatic));
        AddShortcut(VirtualKey.F3, VirtualKeyModifiers.None, () => StatusBar.Toggle("OSNAP")); AddShortcut(VirtualKey.F7, VirtualKeyModifiers.None, () => StatusBar.Toggle("GRID")); AddShortcut(VirtualKey.F8, VirtualKeyModifiers.None, () => StatusBar.Toggle("ORTHO"));
        AddShortcut(VirtualKey.F9, VirtualKeyModifiers.None, () => StatusBar.Toggle("SNAP")); AddShortcut(VirtualKey.F10, VirtualKeyModifiers.None, () => StatusBar.Toggle("POLAR")); AddShortcut(VirtualKey.F12, VirtualKeyModifiers.None, () => StatusBar.Toggle("DYN"));
        AddShortcut(VirtualKey.W, VirtualKeyModifiers.Control, () => StatusBar.Toggle("SC"), true);
        AddShortcut(VirtualKey.F12, VirtualKeyModifiers.Control | VirtualKeyModifiers.Shift, () => DispatcherQueue.TryEnqueue(ReportUiBounds));
        KeyDown += (_, e) => { if (e.Key == VirtualKey.Escape) { CancelShellInteraction(); _commands?.Cancel(); CommandLine.Input.Text = ""; e.Handled = true; } };
    }
    public void CancelShellInteraction()
    {
        var cancel = _cancelConsoleResize; _cancelConsoleResize = null; cancel?.Invoke();
        DockHost.CancelInteraction(); DockHost.DismissPeek(); Viewport.CancelInteraction();
    }
    public void Invoke(string command)
    {
        if (_commands == null) return;
        try
        {
            if (command is "NEW" or "OPEN" or "SAVE" or "EXPORT" or "EXPORT_BINARY" or "RECOVER" or "ABOUT" or "STUDIO" or "MODEL") { FileRequested?.Invoke(command); return; }
            if (command == "LAYOUT_NEW") { LayoutTabs.NewLayout(); return; }
            if (command == "LAYOUT_RENAME") { RenameLayout(_session!.ActiveLayout); return; }
            if (command == "LAYOUT_DELETE") { LayoutTabs.DeleteLayout(); return; }
            if (command == "QSELECT") { ShowQuickSelect(); return; }
            Viewport.CancelInteraction(); _commands.Start(command); CommandLine.FocusInput();
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or NotSupportedException) { CommandLine.AddMessage(e.Message); }
    }
    private void Insert(string name) { Viewport.CancelInteraction(); _commands?.Start("INSERT"); _commands?.Submit(name); CommandLine.FocusInput(); }
    private void OnShellRequested(string command)
    {
        if (command is "MENUBAR:0" or "MENUBAR:1") { _menuBarVisible = command.EndsWith('1'); ApplyChrome(); NotifyPreferences(); return; }
        switch (command)
        {
            case "PROPERTIES": DockHost.SetVisible("properties", true); break;
            case "PROPERTIESCLOSE": DockHost.SetVisible("properties", false); break;
            case "TOOLPALETTES": DockHost.SetVisible("tools", true); break;
            case "TOOLPALETTESCLOSE": DockHost.SetVisible("tools", false); break;
            case "RIBBON": Ribbon.SetMinimized(false); break;
            case "RIBBONCLOSE": Ribbon.SetMinimized(true); break;
            case "CLEANSCREENON": _cleanScreen = true; ApplyChrome(); NotifyPreferences(); break;
            case "CLEANSCREENOFF": _cleanScreen = false; ApplyChrome(); NotifyPreferences(); break;
            case "OPTIONS": ShowOptions(); break;
            case "UISTATS": DispatcherQueue.TryEnqueue(ReportUiBounds); break;
        }
    }
    private void ApplyChrome()
    {
        MenuBar.Visibility = _menuBarVisible && !_cleanScreen ? Visibility.Visible : Visibility.Collapsed;
        _root.RowDefinitions[0].Height = new GridLength(MenuBar.Visibility == Visibility.Visible ? 59 : 34);
        Ribbon.Visibility = _cleanScreen ? Visibility.Collapsed : Visibility.Visible;
        _root.RowDefinitions[1].Height = new GridLength(_cleanScreen ? 0 : Ribbon.IsMinimized ? 28 : 120); DockHost.Suspend(_cleanScreen);
    }
    public WorkspaceLayout CapturePreferences() => new() { Workspace = _workspaceName, RibbonMinimized = Ribbon.IsMinimized, CleanScreen = _cleanScreen,
        MenuBarVisible = _menuBarVisible, ViewCubeVisible = Viewport.ViewCubeVisible, NavigationBarVisible = Viewport.NavigationBarVisible,
        CommandHeight = CommandLine.PanelHeight, HiddenStatusItems = StatusBar.HiddenItems, Palettes = DockHost.Capture() };
    public void RestorePreferences(WorkspaceLayout layout, bool restoreView = true)
    {
        WorkspaceLayout.Validate(layout); _restoring = true;
        try { CancelShellInteraction(); _menuBarVisible = layout.MenuBarVisible; Viewport.SetNavigationVisibility(layout.ViewCubeVisible, layout.NavigationBarVisible);
            CommandLine.ResizeTo(layout.CommandHeight); StatusBar.SetHiddenItems(layout.HiddenStatusItems); _workspaceName = layout.Workspace; StatusBar.SetWorkspace(_workspaceName); Ribbon.Show(_workspaceName == WorkspaceLayout.Presets[0] ? "Home" : "3D Modeling"); Ribbon.SetMinimized(layout.RibbonMinimized); DockHost.Restore(layout.Palettes); if (restoreView) Viewport.Set3D(_workspaceName != WorkspaceLayout.Presets[0]); _cleanScreen = layout.CleanScreen; ApplyChrome(); }
        finally { _restoring = false; }
    }
    private void NotifyPreferences() { if (!_restoring) PreferencesChanged?.Invoke(CapturePreferences()); }
    private void SetWorkspace(string name)
    {
        if (!WorkspaceLayout.Presets.Contains(name)) return;
        _workspaceName = name; StatusBar.SetWorkspace(name); Ribbon.Show(name == WorkspaceLayout.Presets[0] ? "Home" : "3D Modeling");
        Viewport.CancelInteraction(); _commands?.Cancel(); Viewport.Set3D(name != WorkspaceLayout.Presets[0]);
        if (name == WorkspaceLayout.Presets[2]) DockHost.SetVisible("tools", true);
        NotifyPreferences();
    }
    private async void ShowOptions()
    {
        if (_optionsDialog || _styleDialog || _selectionDialog || XamlRoot == null) return; _optionsDialog = true;
        try
        {
            var options = new CadWorkspaceOptions(CapturePreferences()); WorkspaceLayout? chosen = null;
            var dialog = new ContentDialog { XamlRoot = XamlRoot, Title = "Options", Content = options, PrimaryButtonText = "Apply", SecondaryButtonText = "Reset workspace", CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Primary };
            dialog.Resources["ContentDialogMaxWidth"] = 700.0; CadUi.DescribeDialog(dialog, "options.dialog");
            dialog.PrimaryButtonClick += (_, e) => { try { chosen = options.Capture(); } catch (ArgumentException error) { options.ShowError(error.Message); e.Cancel = true; } };
            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Secondary) { RestorePreferences(WorkspaceLayout.Default); NotifyPreferences(); }
            else if (result == ContentDialogResult.Primary && chosen != null) { RestorePreferences(chosen, chosen.Workspace != _workspaceName); NotifyPreferences(); }
        }
        catch (Exception e) { CommandLine.AddMessage(e.Message); }
        finally { _optionsDialog = false; }
    }
    private async void RenameLayout(string name)
    {
        if (_optionsDialog || _styleDialog || _selectionDialog || _session == null || XamlRoot == null) return;
        var session = _session; if (name == "Model") { CommandLine.AddMessage("Model cannot be renamed."); return; } _optionsDialog = true;
        try
        {
            var input = new TextBox { Header = "Layout name", Text = name, MinWidth = 320 }; CadUi.Identify(input, "layout.renameInput", "Layout name");
            var dialog = new ContentDialog { XamlRoot = XamlRoot, Title = "Rename layout", Content = input, PrimaryButtonText = "Rename", CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Primary };
            if (await dialog.ShowAsync() == ContentDialogResult.Primary && ReferenceEquals(session, _session)) session.RenameLayout(name, input.Text.Trim());
        }
        catch (Exception e) { CommandLine.AddMessage(e.Message); }
        finally { _optionsDialog = false; }
    }
    private void AddShortcut(VirtualKey key, VirtualKeyModifiers modifiers, Action action, bool preserveTextEditing = false)
    {
        var accelerator = new KeyboardAccelerator { Key = key, Modifiers = modifiers };
        accelerator.Invoked += (_, e) => {
            if (preserveTextEditing && XamlRoot != null && FocusManager.GetFocusedElement(XamlRoot) is TextBox text && (text != CommandLine.Input || text.Text.Length != 0)) return;
            action(); e.Handled = true;
        }; KeyboardAccelerators.Add(accelerator);
    }
    private void SelectionChanged()
    {
        if (_session == null || ReferenceEquals(_selectionDrawing, _session.Document.Drawing) && _selectionRevision == _session.SelectionRevision) return;
        _selectionDrawing = _session.Document.Drawing; _selectionRevision = _session.SelectionRevision; var selected = _session.SelectedEntities();
        Ribbon.SetContext(selected.Length == 0 ? "" : selected.All(e => e is PolylineEntity or PlacedEntity { Geometry: PolylineEntity }) ? "Polyline" : selected.All(e => e is MeshEntity) ? "Mesh" : "Selection");
    }
    public void SetTitle(string name) => ApplicationBar.SetTitle(name + (_session?.Document.IsDirty == true ? " *" : ""));
    public void Bind(CadSession session, CommandEngine commands)
    {
        if (_session != null) _session.Changed -= SelectionChanged;
        if (_commands != null) { _commands.ViewRequested -= OnStyleRequested; _commands.ViewRequested -= OnShellRequested; }
        _session = session; _commands = commands; commands.ViewRequested += OnStyleRequested; commands.ViewRequested += OnShellRequested;
        Viewport.Bind(session, commands); Palette.Bind(session); ToolPalette.Bind(session); Ribbon.Bind(session); LayoutTabs.Bind(session); CommandLine.Bind(commands); StatusBar.Bind(session);
        _selectionDrawing = null; session.Changed += SelectionChanged; SelectionChanged(); SetTitle(session.Document.Drawing.Name);
    }
    /// <summary>Read-only accessibility/layout diagnostics used to locate the actual rendered controls.</summary>
    public void ReportUiBounds()
    {
        using var buffer = new MemoryStream();
        using (var w = new Utf8JsonWriter(buffer))
        {
            w.WriteStartObject(); var count = 0; var visited = new HashSet<DependencyObject>(); var ids = new HashSet<string>();
            void Visit(DependencyObject node)
            {
                if (++count > 20000 || !visited.Add(node) || node is UIElement { Visibility: Visibility.Collapsed }) return;
                if (node is FrameworkElement element && element.ActualWidth > 0 && element.ActualHeight > 0)
                {
                    var id = AutomationProperties.GetAutomationId(element);
                    if (!string.IsNullOrEmpty(id) && !ids.Contains(id))
                    {
                        var p = element.TransformToVisual(this).TransformPoint(new Point(0, 0));
                        if (double.IsFinite(p.X) && double.IsFinite(p.Y) && p.X >= 0 && p.Y >= 0 && p.X < ActualWidth && p.Y < ActualHeight)
                        { ids.Add(id); w.WriteStartArray(id); w.WriteNumberValue(p.X); w.WriteNumberValue(p.Y); w.WriteNumberValue(element.ActualWidth); w.WriteNumberValue(element.ActualHeight); w.WriteEndArray(); }
                    }
                }
                for (var i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++) Visit(VisualTreeHelper.GetChild(node, i));
            }
            Visit(this); if (XamlRoot != null) foreach (var popup in VisualTreeHelper.GetOpenPopupsForXamlRoot(XamlRoot)) if (popup.Child != null) Visit(popup.Child); w.WriteEndObject();
        }
        Console.WriteLine("CADSPACE_UI_BOUNDS:" + Encoding.UTF8.GetString(buffer.ToArray()));
        Console.WriteLine(FormattableString.Invariant($"CADSPACE_UI_STATE: workspace={_workspaceName}; model={Viewport.Is3D}; yaw={Viewport.ModelCamera.Yaw}; pitch={Viewport.ModelCamera.Pitch}; clean={_cleanScreen}; snapModes={(int)(_session?.SnapModes ?? ObjectSnapModes.None)}; grid={_session?.GridVisible}; cx={Viewport.Camera.Center.X:R}; cy={Viewport.Camera.Center.Y:R}; ppu={Viewport.Camera.PixelsPerUnit:R}"));
    }
}
