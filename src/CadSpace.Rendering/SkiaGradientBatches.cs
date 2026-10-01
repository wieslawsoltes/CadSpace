using CadSpace.Geometry;
using CadSpace.Model;
using SkiaSharp;

namespace CadSpace.Rendering;

/// <summary>Retains color meshes across camera-only redraws; adjacent triangles are one draw call.</summary>
internal sealed class SkiaGradientBatches : IDisposable
{
    private DrawingScene? _scene;
    private readonly Dictionary<int, Batch> _batches = new();
    private readonly Dictionary<int, int> _rangeStarts = new();
    private readonly HashSet<int> _drawn = new();

    public void Begin(DrawingScene scene)
    {
        _drawn.Clear();
        if (ReferenceEquals(scene, _scene)) return;
        Dispose(); _scene = scene;
        for (var index = 0; index < scene.Paths.Length;)
        {
            var first = scene.Paths[index];
            if (first.VertexColors.Length != 3 || first.Points.Length != 3) { index++; continue; }
            var start = index++;
            while (index < scene.Paths.Length && scene.Paths[index].EntityId == first.EntityId &&
                   scene.Paths[index].VertexColors.Length == 3 && scene.Paths[index].Points.Length == 3) index++;
            var count = index - start;
            _batches.Add(start, new Batch(scene, start, count));
            for (var i = start; i < index; i++) _rangeStarts[i] = start;
        }
    }

    public bool Draw(SKCanvas canvas, Camera2D camera, int pathIndex, bool selected, bool preview, SKPaint paint)
    {
        if (!_rangeStarts.TryGetValue(pathIndex, out var start)) return false;
        if (!_drawn.Add(start)) return true;
        var batch = _batches[start];
        var origin = camera.WorldToScreen(batch.Origin);
        var scale = (float)camera.PixelsPerUnit;
        var matrix = new SKMatrix(scale, 0, (float)origin.X, 0, -scale, (float)origin.Y, 0, 0, 1);
        canvas.Save();
        try
        {
            canvas.Concat(in matrix); paint.Color = SKColors.White;
            canvas.DrawVertices(batch.Get(selected, preview), SKBlendMode.Modulate, paint);
        }
        finally { canvas.Restore(); }
        return true;
    }

    private sealed class Batch : IDisposable
    {
        private readonly DrawingScene _scene;
        private readonly int _start, _count;
        private SKPoint[]? _positions;
        private SKVertices? _normal, _selected, _preview;
        public Vec3 Origin { get; }
        public Batch(DrawingScene scene, int start, int count)
        {
            _scene = scene; _start = start; _count = count;
            // Rebasing prevents loss of survey-coordinate precision in the float vertex buffer.
            Origin = scene.Paths[start].Points[0];
        }
        public SKVertices Get(bool selected, bool preview)
        {
            var cached = preview ? _preview : selected ? _selected : _normal;
            if (cached != null) return cached;
            _positions ??= Positions();
            var colors = new SKColor[_positions.Length];
            for (var i = 0; i < _count; i++) for (var j = 0; j < 3; j++)
                colors[i * 3 + j] = preview ? new(151, 208, 252, 180) : selected ? new(86, 172, 255) : SkiaDraftRenderer.Color(_scene.Paths[_start+i].VertexColors[j]);
            var vertices = SKVertices.CreateCopy(SKVertexMode.Triangles, _positions, colors);
            if (preview) _preview = vertices; else if (selected) _selected = vertices; else _normal = vertices;
            return vertices;
        }
        private SKPoint[] Positions()
        {
            var positions = new SKPoint[checked(_count * 3)];
            for (var i = 0; i < _count; i++) for (var j = 0; j < 3; j++)
            {
                var delta = _scene.Paths[_start+i].Points[j] - Origin;
                if (Math.Abs(delta.X) > float.MaxValue || Math.Abs(delta.Y) > float.MaxValue)
                    throw new ArgumentException("Gradient mesh exceeds the Skia coordinate range.");
                positions[i*3+j] = new((float)delta.X, (float)delta.Y);
            }
            return positions;
        }
        public void Dispose() { _normal?.Dispose(); _selected?.Dispose(); _preview?.Dispose(); }
    }
    public void Dispose()
    {
        foreach (var batch in _batches.Values) batch.Dispose();
        _batches.Clear(); _rangeStarts.Clear(); _drawn.Clear(); _scene = null;
    }
}
