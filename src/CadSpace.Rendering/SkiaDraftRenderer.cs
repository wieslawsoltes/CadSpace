using CadSpace.Geometry;
using CadSpace.Model;
using SkiaSharp;

namespace CadSpace.Rendering;

/// <summary>Host-agnostic Skia renderer. The host owns the hardware-accelerated SKCanvas and its lifetime.</summary>
public sealed class SkiaDraftRenderer : IDisposable
{
    private readonly SKPaint _stroke = new() { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1 };
    private readonly SKPaint _fill = new() { IsAntialias = true, Style = SKPaintStyle.Fill };
    private readonly List<int> _visiblePaths = new(), _visibleTexts = new();
    private readonly SKFont _font = new(SKTypeface.Default, 12);
    public static SKColor Color(uint c) => new((byte)(c >> 16), (byte)(c >> 8), (byte)c, (byte)(c >> 24));
    private static SKPoint Pixel(Camera2D camera, Vec3 p) { var s = camera.WorldToScreen(p); return new((float)s.X, (float)s.Y); }
    public void Render(SKCanvas canvas, Camera2D camera, DrawingScene scene, IReadOnlySet<Guid> selected, bool grid = true, double spacing = 10)
    {
        canvas.Clear(new SKColor(29, 36, 44));
        if (grid) Grid(canvas, camera, spacing);
        DrawScene(canvas, camera, scene, selected);
        Axes(canvas, camera);
    }
    public void DrawScene(SKCanvas canvas, Camera2D camera, DrawingScene scene, IReadOnlySet<Guid> selected, bool preview = false)
    {
        var bounds = camera.VisibleBounds;
        var acceleration = SceneAcceleration.For(scene);
        _visiblePaths.Clear(); acceleration.Paths.Query(bounds, _visiblePaths, xyOnly:true); _visiblePaths.Sort();
        foreach (var index in _visiblePaths)
        {
            var path = scene.Paths[index];
            if (path.Points.IsEmpty) continue;
            var highlight = selected.Contains(path.EntityId);
            var color = preview ? new SKColor(151, 208, 252, 180) : highlight ? new SKColor(86, 172, 255) : Color(path.Color);
            _stroke.Color = color; _stroke.StrokeWidth = highlight ? 2 : Math.Clamp((float)path.Weight * 3, 0.85f, 4);
            if (path.Points.Length == 1)
            {
                var point = Pixel(camera, path.Points[0]); canvas.DrawLine(point.X - 3, point.Y, point.X + 3, point.Y, _stroke); canvas.DrawLine(point.X, point.Y - 3, point.X, point.Y + 3, _stroke); continue;
            }
            if (!path.Filled && path.Pattern is { } pattern && pattern.Length * camera.PixelsPerUnit >= 2)
            {
                _fill.Color = color;
                foreach (var dash in pattern.VisibleStrokes(path, bounds))
                {
                    var a = Pixel(camera, dash.Start); var b = Pixel(camera, dash.End);
                    if (dash.Dot) canvas.DrawCircle(a.X, a.Y, Math.Max(.75f, _stroke.StrokeWidth / 2), _fill);
                    else canvas.DrawLine(a, b, _stroke);
                }
                continue;
            }
            using var outline = new SKPath(); outline.MoveTo(Pixel(camera, path.Points[0]));
            for (var i = 1; i < path.Points.Length; i++) outline.LineTo(Pixel(camera, path.Points[i]));
            if (path.Closed) outline.Close();
            if (path.Filled) { _fill.Color = color; canvas.DrawPath(outline, _fill); }
            else canvas.DrawPath(outline, _stroke);

        }
        _visibleTexts.Clear(); acceleration.Texts.Query(bounds, _visibleTexts, xyOnly:true); _visibleTexts.Sort();
        foreach (var index in _visibleTexts)
        {
            var label = scene.Texts[index];
            var point = Pixel(camera, label.Position); var height = label.Height * camera.PixelsPerUnit;
            if (height < 2 || height > 10000) continue;
            _font.Size = (float)height;
            _fill.Color = preview ? new SKColor(151, 208, 252) : selected.Contains(label.EntityId) ? new SKColor(86, 172, 255) : Color(label.Color);
            canvas.Save();
            var textMatrix = new SKMatrix((float)label.AxisX.X, (float)-label.AxisY.X, point.X, (float)-label.AxisX.Y, (float)label.AxisY.Y, point.Y, 0, 0, 1);
            canvas.Concat(in textMatrix);
            var lines = SceneTextLayout.For(label).Lines;
            for (var i = 0; i < lines.Length; i++) canvas.DrawText(lines[i], 0, (float)(i * height * 1.3), _font, _fill);
            canvas.Restore();
        }
    }
    public void DrawInteraction(SKCanvas canvas, Camera2D camera, Vec3 cursor, bool showCursor, string? snapKind, Vec3? windowStart)
    {
        if (!showCursor) return;
        var p = Pixel(camera, cursor);
        _stroke.Color = new SKColor(213, 225, 231); _stroke.StrokeWidth = 1;
        canvas.DrawLine(p.X - 28, p.Y, p.X - 5, p.Y, _stroke); canvas.DrawLine(p.X + 5, p.Y, p.X + 28, p.Y, _stroke);
        canvas.DrawLine(p.X, p.Y - 28, p.X, p.Y - 5, _stroke); canvas.DrawLine(p.X, p.Y + 5, p.X, p.Y + 28, _stroke);
        canvas.DrawRect(p.X - 4, p.Y - 4, 8, 8, _stroke);
        if (snapKind is not null)
        {
            _stroke.Color = new SKColor(114, 245, 139); _stroke.StrokeWidth = 1.5f; canvas.DrawRect(p.X - 7, p.Y - 7, 14, 14, _stroke);
            _font.Size = 11; _fill.Color = _stroke.Color; canvas.DrawText(snapKind, p.X + 13, p.Y - 12, _font, _fill);
        }
        if (windowStart is Vec3 start)
        {
            var a = Pixel(camera, start); var crossing = cursor.X < start.X;
            var rect = new SKRect(Math.Min(a.X, p.X), Math.Min(a.Y, p.Y), Math.Max(a.X, p.X), Math.Max(a.Y, p.Y));
            var color = crossing ? new SKColor(76, 210, 132) : new SKColor(78, 153, 246);
            _fill.Color = color.WithAlpha(30); canvas.DrawRect(rect, _fill);
            _stroke.Color = color; _stroke.StrokeWidth = 1; canvas.DrawRect(rect, _stroke);
        }
    }
    private void Grid(SKCanvas canvas, Camera2D camera, double nominal)
    {
        var spacing = nominal > 0 && double.IsFinite(nominal) ? nominal : 10;
        while (spacing * camera.PixelsPerUnit < 12) spacing *= 10;
        while (spacing * camera.PixelsPerUnit > 140) spacing /= 10;
        var bounds = camera.VisibleBounds;
        var firstX = Math.Floor(bounds.Min.X / spacing); var firstY = Math.Floor(bounds.Min.Y / spacing);
        var countX = Math.Min(1000, (int)(camera.Width / (spacing * camera.PixelsPerUnit)) + 3);
        var countY = Math.Min(1000, (int)(camera.Height / (spacing * camera.PixelsPerUnit)) + 3);
        _stroke.StrokeWidth = 1; _stroke.IsAntialias = false;
        for (var i = 0; i < countX; i++)
        {
            var n = firstX + i; var x = Pixel(camera, new(n * spacing, 0)).X;
            _stroke.Color = (long)n % 5 == 0 ? new(43, 53, 64) : new(35, 44, 53); canvas.DrawLine(x, 0, x, (float)camera.Height, _stroke);
        }
        for (var i = 0; i < countY; i++)
        {
            var n = firstY + i; var y = Pixel(camera, new(0, n * spacing)).Y;
            _stroke.Color = (long)n % 5 == 0 ? new(43, 53, 64) : new(35, 44, 53); canvas.DrawLine(0, y, (float)camera.Width, y, _stroke);
        }
        _stroke.IsAntialias = true;
    }
    private void Axes(SKCanvas canvas, Camera2D camera)
    {
        var x = 38f; var y = (float)camera.Height - 42; _stroke.StrokeWidth = 2;
        _stroke.Color = new SKColor(229, 111, 101); canvas.DrawLine(x, y, x + 42, y, _stroke);
        _stroke.Color = new SKColor(133, 193, 104); canvas.DrawLine(x, y, x, y - 42, _stroke);
        _font.Size = 11; _fill.Color = new SKColor(198, 207, 220); canvas.DrawText("X", x + 47, y + 4, _font, _fill); canvas.DrawText("Y", x - 4, y - 48, _font, _fill); canvas.DrawText("WCS", x - 5, y + 20, _font, _fill);
    }
    public void Dispose() { _stroke.Dispose(); _fill.Dispose(); _font.Dispose(); }
}
