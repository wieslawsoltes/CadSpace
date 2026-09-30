using System.Diagnostics;
using System.Collections.Immutable;
using CadSpace.Engine;
using CadSpace.Geometry;
using CadSpace.Model;
using CadSpace.Rendering;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Silk.NET.OpenGL;
using SkiaSharp;
using Uno.WinUI.Graphics2DSK;
using Uno.WinUI.Graphics3DGL;
using Windows.Foundation;
using Windows.System;

namespace CadSpace.Controls;

/// <summary>Reusable two-dimensional drafting and three-dimensional GPU viewport with no application dependency.</summary>
public sealed partial class CadViewport : Grid
{
    private readonly DraftSurface _draft;
    private readonly CadDynamicInput _dynamic=new();
    private ModelSurface? _model;
    private readonly CadViewportControls _viewControls = new();
    private readonly CadViewCube _cube = new();
    private readonly CadNavigationBar _navigation = new();
    private string _navigationTool = "orbit";
    private readonly TextBlock _metrics = CadTheme.Text("", 10, CadTheme.Muted);
    private CadSession? _session;
    private CommandEngine? _commands;
    private bool _inside, _pan, _orbit, _fit = true;
    private Vec3 _cursor, _pressWorld;
    private Point _previous, _pressScreen;
    private bool _selecting, _dragged;
    private SnapResult _snap;
    private double _lastCpuDrawMilliseconds;
    private long _lastMetricUpdate;
    public Camera2D Camera { get; } = new();
    public Camera3D ModelCamera { get; } = new();
    public bool Is3D { get; private set; }
    public ModelVisualStyle VisualStyle { get; set; } = ModelVisualStyle.ShadedEdges;
    public Plane3? ClippingPlane { get; set; }
    public event Action<Vec3>? CoordinatesChanged;
    public event Action<string>? Message;
    public event Action<bool>? ModeChanged;
    public CadViewport()
    {
        Background = CadTheme.Brush(0xFF1D242C); _draft = new(this) { IsHitTestVisible = false }; Children.Add(_draft); _overlay = new(this) { IsHitTestVisible = false }; Children.Add(_overlay);
        _viewControls.Margin = new Thickness(7, 7, 0, 0); _viewControls.HorizontalAlignment = HorizontalAlignment.Left; _viewControls.VerticalAlignment = VerticalAlignment.Top; Children.Add(_viewControls);
        _metrics.Margin = new Thickness(0, 0, 16, 12); _metrics.HorizontalAlignment = HorizontalAlignment.Right; _metrics.VerticalAlignment = VerticalAlignment.Bottom; _metrics.IsHitTestVisible = false; Children.Add(_metrics);
        _cube.HorizontalAlignment = HorizontalAlignment.Right; _cube.VerticalAlignment = VerticalAlignment.Top; _cube.Margin = new Thickness(0, 12, 14, 0); Children.Add(_cube);
        _navigation.HorizontalAlignment = HorizontalAlignment.Right; _navigation.VerticalAlignment = VerticalAlignment.Top; _navigation.Margin = new Thickness(0, 180, 14, 0); Children.Add(_navigation);
        _cube.OrientationRequested += SetOrientation; _cube.OrbitRequested += (dx, dy) => { Set3D(true); ModelCamera.Orbit(dx, dy); Redraw(); };
        _cube.NavigationRequested += Navigate; _navigation.NavigationRequested += Navigate; _viewControls.NavigationRequested += Navigate;
        Children.Add(_dynamic);
        PointerPressed += Pressed; PointerMoved += Moved; PointerReleased += Released; PointerWheelChanged += Wheel;
        PointerEntered += (_, _) => { _inside = true; Redraw(); };
        PointerExited += (_, _) => { _inside = false; Redraw(); };
        PointerCaptureLost += (_, _) => { _pan = _orbit = _selecting = false; _gripEntity = null; Redraw(); };
        DoubleTapped += (_, e) => {
            if (_commands?.IsActive == true || IsChrome(e.OriginalSource)) return;
            if (!Is3D && _session != null && _commands != null)
            {
                var hit = _session.HitTest(World(e.GetPosition(this)), 7 / Camera.PixelsPerUnit);
                var entity = hit is Guid id ? _session.Document.Drawing.Entities.FirstOrDefault(x => x.Id == id) : null;
                if (entity != null && TextEditing.IsEditable(entity))
                { CancelInteraction(); _session.Select(entity.Id); _commands.Start("DDEDIT"); e.Handled = true; return; }
            }
            Fit(); e.Handled = true;
        };
        SizeChanged += (_, _) => { Camera.Width = ActualWidth; Camera.Height = ActualHeight; Redraw(); };
    }
    public void Bind(CadSession session, CommandEngine commands)
    {
        if (_session != null) _session.Changed -= Redraw;
        if (_commands != null) { _commands.Changed -= Redraw; _commands.ViewRequested -= OnView; }
        _session = session; _commands = commands; session.Changed += Redraw; commands.Changed += Redraw; commands.ViewRequested += OnView;
        _dynamic.Bind(commands); CancelInteraction(); _lastRender = null; _gripDocument = null; _fit = true; ClippingPlane = null; Set3D(false); Redraw();
    }
    public void Fit()
    {
        if (_session == null) return;
        Camera.Width = ActualWidth; Camera.Height = ActualHeight; Camera.Fit(_session.Scene.Bounds); ModelCamera.Fit(_session.Scene.Bounds); _fit = false; Redraw();
    }
    public void Set3D(bool enabled)
    {
        var changed = Is3D != enabled;
        Is3D = enabled;
        if (enabled && _model == null)
        {
            _model = new(this) { IsHitTestVisible = false }; Children.Insert(1, _model);
            _model.RegisterPropertyChangedCallback(GLCanvasElement.IsGLInitializedProperty, (_, _) =>
            {
                if (_model.IsGLInitialized == false) Fault("The 3D GPU context could not be initialized. The 2D drafting view remains available.");
            });
        }
        _draft.Visibility = enabled ? Visibility.Collapsed : Visibility.Visible; _overlay.Visibility = _draft.Visibility;
        if (_model != null) _model.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
        if (enabled && changed && _session != null) ModelCamera.Fit(_session.Scene.Bounds);
        UpdateViewLabel();
        ModeChanged?.Invoke(enabled); Redraw();
    }
    private void UpdateViewLabel()
    {
        _viewControls.Synchronize(Is3D, ModelCamera.Yaw, ModelCamera.Pitch, VisualStyle);
        _cube.Synchronize(Is3D ? ModelCamera.Yaw : -90, Is3D ? ModelCamera.Pitch : 90);
        _navigation.SetMode(_navigationTool);
    }
    public void SetOrientation(ViewOrientation orientation)
    {
        CancelInteraction(); _commands?.Cancel(); Set3D(true); ModelCamera.Yaw = orientation.Yaw; ModelCamera.Pitch = orientation.Pitch;
        ModelCamera.Orthographic = true; Redraw();
    }
    public void Navigate(string action)
    {
        if (action.StartsWith("view:")) { SetOrientation(ViewCubeGeometry.Named(action[5..])); return; }
        if (action.StartsWith("style:"))
        {
            if (action[6..] == "2D Wireframe") Set3D(false);
            else { VisualStyle = Enum.Parse<ModelVisualStyle>(action[6..]); Set3D(true); }
        }
        else switch (action)
        {
            case "ZOOM": Fit(); break;
            case "home": SetOrientation(ViewCubeGeometry.Named("SW Isometric")); Fit(); break;
            case "zoomIn": case "zoomOut": var factor = action == "zoomIn" ? 1.35 : 1 / 1.35; if (Is3D) ModelCamera.Zoom(factor); else Camera.Zoom(factor, ActualWidth / 2, ActualHeight / 2); break;
            case "projection": ModelCamera.Orthographic = !ModelCamera.Orthographic; Set3D(true); break;
            case "clip": ClippingPlane = ClippingPlane == null ? Plane3.Through(_session?.Scene.Bounds.Center ?? default, Vec3.UnitZ) : null; Set3D(true); break;
            case "pan": case "orbit": case "select": CancelInteraction(); _commands?.Cancel(); _navigationTool = action; if (action == "orbit") Set3D(true); break;
        }
        UpdateViewLabel(); Redraw();
    }
    private bool IsChrome(object source)
    {
        for (var node = source as DependencyObject; node != null && node != this; node = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(node))
            if (node is Button or ComboBox or TextBox or CadViewCube or CadViewportControls or CadNavigationBar) return true;
        return false;
    }
    private void OnView(string view)
    {
        if (view is "LAYER" or "LINETYPE") return;
        if (view == "RENDERSTATS") { ReportRenderStatistics(); Console.WriteLine($"CADSPACE_HOST_CALLBACKS: scene={_sceneCallbacks}; model={_modelCallbacks}"); return; }
        if (view == "ZOOM") { Fit(); return; }
        if (view.StartsWith("STYLE:")) { VisualStyle = Enum.Parse<ModelVisualStyle>(view[6..], true); Set3D(true); }
        else if (view.StartsWith("PROJECTION:")) { ModelCamera.Orthographic = view[11..] == "0"; Set3D(true); }
        else if (view.StartsWith("CLIP:"))
        {
            if (view[5..] == "OFF") ClippingPlane = null;
            else { var values = view[5..].Split(',').Select(v => double.Parse(v, System.Globalization.CultureInfo.InvariantCulture)).ToArray(); ClippingPlane = Plane3.Through(new(values[0], values[1], values[2]), new(values[3], values[4], values[5])); }
            Set3D(true);
        }
        else if (view is "TOP" or "3DORBIT") { _navigationTool = view == "TOP" ? "select" : "orbit"; Set3D(view == "3DORBIT"); }
        else return;
        UpdateViewLabel(); Redraw(); Console.WriteLine($"CADSPACE_VIEW: {view}");
    }
    private bool _redrawPending;
    public void Redraw()
    {
        if(_redrawPending)return;_redrawPending=true;
        if(!DispatcherQueue.TryEnqueue(()=>{_redrawPending=false;DrawPending();}))_redrawPending=false;
    }
    private void DrawPending()
    {
        _dynamic.Refresh(); UpdateViewLabel();
        if (_session == null || ActualWidth <= 0 || ActualHeight <= 0) return;
        Camera.Width = ActualWidth; Camera.Height = ActualHeight;
        if (_fit && ActualWidth > 100 && ActualHeight > 100) { Camera.Fit(_session.Scene.Bounds); _fit = false; }
        var stamp = CaptureRenderStamp(ActualWidth, ActualHeight);
        if (_lastRender != stamp)
        {
            _lastRender = stamp;
            if (Is3D) _model?.Invalidate(); else _draft.Invalidate();
        }
        if (!Is3D) _overlay.Invalidate();
        if (!Is3D && Stopwatch.GetElapsedTime(_lastMetricUpdate).TotalMilliseconds > 250)
        {
            _lastMetricUpdate = Stopwatch.GetTimestamp();
            _metrics.Text = $"{_session.Document.Drawing.Entities.Length} objects   •   scene {_sceneDraws}   •   overlay {_overlayDraws}   •   previous CPU recording {_lastCpuDrawMilliseconds:0.0} ms";
        }
    }
    private void Fault(string message) => DispatcherQueue.TryEnqueue(() => { Message?.Invoke(message); Set3D(false); });
    private Vec3 World(Point screen)
    {
        if (!Is3D) return Camera.ScreenToWorld(screen.X, screen.Y);
        return ModelCamera.Ray(screen.X, screen.Y, ActualWidth, ActualHeight).IntersectPlane(new(0, 0, _commands?.ReferencePoint?.Z ?? 0), Vec3.UnitZ, out var p) ? p : _cursor;
    }
    private void Pressed(object sender, PointerRoutedEventArgs e)
    {
        if (_session == null || _commands == null || IsChrome(e.OriginalSource)) return;
        var current = e.GetCurrentPoint(this); _previous = _pressScreen = current.Position; _pressWorld = World(current.Position); _dragged = false;
        if (current.Properties.IsRightButtonPressed)
        {
            if(_commands.IsActive) _commands.Submit("");
            else
            {
                var menu=new MenuFlyout();
                foreach(var name in new[]{"MOVE","COPY","STRETCH","ERASE","SELECTSIMILAR","SELECTALL","ZOOM","UNDO","REDO"})
                {var item=new MenuFlyoutItem{Text=name};item.Click+=(_,_)=>_commands.Start(name);menu.Items.Add(item);}
                menu.ShowAt(this,new Microsoft.UI.Xaml.Controls.Primitives.FlyoutShowOptions{Position=current.Position});
            }
            e.Handled=true;return;
        }
        _pan = current.Properties.IsMiddleButtonPressed || _navigationTool == "pan" && !_commands.IsActive && current.Properties.IsLeftButtonPressed; _orbit = Is3D && !_pan && !_commands.IsActive && current.Properties.IsLeftButtonPressed;
        if (_pan || _orbit) { CapturePointer(e.Pointer); e.Handled = true; return; }
        if (!current.Properties.IsLeftButtonPressed) return;
        if (!_commands.IsActive && !e.KeyModifiers.HasFlag(VirtualKeyModifiers.Shift) && TryBeginGrip(current.Position))
        { CapturePointer(e.Pointer); e.Handled = true; return; }
        if (_commands.IsActive)
        {
            _snap = _session.Snap(_pressWorld, 9 / Camera.PixelsPerUnit, _commands.ReferencePoint); _cursor = _snap.Point; _commands.PickTolerance = 7 / Camera.PixelsPerUnit; _commands.Point(_snap.Point);
        }
        else { _selecting = true; CapturePointer(e.Pointer); }
        Redraw(); e.Handled = true;
    }
    private void Moved(object sender, PointerRoutedEventArgs e)
    {
        if (_session == null || _commands == null) return;
        var current = e.GetCurrentPoint(this); var p = current.Position;
        var dx = p.X - _previous.X; var dy = p.Y - _previous.Y; _previous = p;
        if (_pan) { if (Is3D) ModelCamera.Pan(dx, dy, ActualHeight); else Camera.Pan(dx, dy); }
        if (_orbit && _navigationTool != "select" && (Math.Abs(p.X - _pressScreen.X) + Math.Abs(p.Y - _pressScreen.Y) > 5 || _dragged)) { _dragged = true; ModelCamera.Orbit(dx, dy); }
        if (_selecting && Math.Abs(p.X - _pressScreen.X) + Math.Abs(p.Y - _pressScreen.Y) > 5) _dragged = true;
        if (_gripEntity != null)
        {
            MoveGripPointer(p); CoordinatesChanged?.Invoke(_cursor); Redraw(); return;
        }
        _hotGrip = !Is3D && !_commands.IsActive && !_selecting && !_pan ? FindGrip(p) : -1;
        var world = World(p); _snap = _commands.IsActive ? _session.Snap(world, 9 / Camera.PixelsPerUnit, _commands.ReferencePoint) : new(world, SnapKind.None);
        _cursor = _snap.Point; _dynamic.Position(p.X,p.Y,ActualWidth,ActualHeight,_cursor); CoordinatesChanged?.Invoke(_cursor); Redraw();
    }
    private void Released(object sender, PointerRoutedEventArgs e)
    {
        if (_gripEntity != null)
        {
            try { MoveGripPointer(e.GetCurrentPoint(this).Position); if (_dragged) _session?.MoveGrip(_gripEntity, _gripIndex, _cursor); }
            catch (Exception error) when (error is ArgumentException or InvalidOperationException or NotSupportedException) { Message?.Invoke(error.Message); }
            finally { _gripEntity = null; _dragged = false; ReleasePointerCapture(e.Pointer); Redraw(); }
            e.Handled = true; return;
        }
        if (_session != null && _orbit && !_dragged)
        {
            var point = e.GetCurrentPoint(this).Position;
            var pick = ScenePicking.Pick(_session.Scene, ModelCamera.Ray(point.X, point.Y, ActualWidth, ActualHeight), p => ModelCamera.Project(p, ActualWidth, ActualHeight), new(point.X, point.Y), 7, ClippingPlane, VisualStyle == ModelVisualStyle.Wireframe);
            _session.Select(pick?.EntityId, e.KeyModifiers.HasFlag(VirtualKeyModifiers.Shift) || e.KeyModifiers.HasFlag(VirtualKeyModifiers.Control));
            Console.WriteLine($"CADSPACE_PICK: count={_session.Selection.Count}");
        }
        if (_session != null && _selecting)
        {
            var point = e.GetCurrentPoint(this).Position; var world = World(point);
            var mode = e.KeyModifiers.HasFlag(VirtualKeyModifiers.Shift) ? SelectionMode.Remove : e.KeyModifiers.HasFlag(VirtualKeyModifiers.Control) ? SelectionMode.Toggle : SelectionMode.Add;
            if (_dragged) _session.SelectWindow(_pressWorld, world, point.X < _pressScreen.X, mode);
            else SelectAt(world, point, mode);
        }
        _pan = _orbit = _selecting = _dragged = false; ReleasePointerCapture(e.Pointer); Redraw(); e.Handled = true;
    }
    private void Wheel(object sender, PointerRoutedEventArgs e)
    {
        if (IsChrome(e.OriginalSource)) return;
        var p = e.GetCurrentPoint(this); var factor = Math.Pow(1.18, p.Properties.MouseWheelDelta / 120.0);
        if (Is3D) ModelCamera.ZoomAt(factor, p.Position.X, p.Position.Y, ActualWidth, ActualHeight); else Camera.Zoom(factor, p.Position.X, p.Position.Y);
        Redraw(); e.Handled = true;
    }
}
