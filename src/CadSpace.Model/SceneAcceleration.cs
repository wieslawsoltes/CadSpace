using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using CadSpace.Geometry;

namespace CadSpace.Model;

/// <summary>Identity-scoped lazy indexes; record copies cannot accidentally reuse stale bounds.</summary>
public sealed class SceneAcceleration
{
    private static readonly ConditionalWeakTable<DrawingScene, SceneAcceleration> Cache = new();
    public static SceneAcceleration For(DrawingScene scene) => Cache.GetValue(scene, s => new(s));
    private readonly Lazy<SpatialIndex> _paths, _texts, _triangles;
    public SpatialIndex Paths => _paths.Value;
    public SpatialIndex Texts => _texts.Value;
    public SpatialIndex Triangles => _triangles.Value;
    public Bounds3 Bounds { get; }
    private SceneAcceleration(DrawingScene scene)
    {
        // Empty paths are legal. Give them point bounds; the narrow phase simply skips them.
        var paths = scene.Paths.Select(p => p.Points.IsEmpty ? new Bounds3(default, default) : Bounds3.From(p.Points)).ToArray();
        var texts = scene.Texts.Select(TextBounds).ToArray();
        _paths = new(() => new(paths)); _texts = new(() => new(texts));
        _triangles = new(() => new(scene.Triangles.Select(t => Bounds3.Empty.Include(t.A).Include(t.B).Include(t.C))));
        var b = Bounds3.Empty;
        for (var i = 0; i < paths.Length; i++) if (!scene.Paths[i].Points.IsEmpty) b = b.Union(paths[i]);
        foreach (var box in texts) b = b.Union(box);
        foreach (var t in scene.Triangles) b = b.Include(t.A).Include(t.B).Include(t.C);
        Bounds = b;
    }
    public static Bounds3 TextBounds(SceneText text)
    {
        var layout = SceneTextLayout.For(text);
        var x = text.AxisX * (text.Height * Math.Max(1, layout.MaximumLineLength) * 1.5);
        var top = text.AxisY * text.Height;
        var bottom = text.AxisY * (-text.Height * (.3 + (layout.Lines.Length - 1) * 1.3));
        return Bounds3.Empty.Include(text.Position + top).Include(text.Position + top + x).Include(text.Position + bottom).Include(text.Position + bottom + x);
    }
    public static bool NearScreen(Bounds3 b, Func<Vec3, Vec3> project, Vec3 screen, double tolerance)
    {
        var projected = Bounds3.Empty;
        for (var i = 0; i < 8; i++)
        {
            var p = project(new((i & 1) == 0 ? b.Min.X : b.Max.X, (i & 2) == 0 ? b.Min.Y : b.Max.Y, (i & 4) == 0 ? b.Min.Z : b.Max.Z));
            // Boxes crossing the eye plane require conservative traversal.
            if (!p.IsFinite || p.Z <= 0) return true;
            projected = projected.Include(p);
        }
        return screen.X + tolerance >= projected.Min.X && screen.X - tolerance <= projected.Max.X && screen.Y + tolerance >= projected.Min.Y && screen.Y - tolerance <= projected.Max.Y;
    }
}

/// <summary>Dependency-aware incremental tessellation. Changes invalidate affected roots, not unrelated layers or blocks.</summary>
public sealed class DrawingSceneCache
{
    private sealed record Entry(Entity Entity, DrawingScene Scene, string[] Layers, string[] Blocks, string[] Types, long Vertices);
    private Dictionary<Guid, Entry> _roots = new();
    private Drawing? _drawing;
    private string _layout = "";
    private DrawingScene? _scene;
    private static readonly HashSet<string> NoChanges = new(StringComparer.OrdinalIgnoreCase);
    public int RebuiltRoots { get; private set; }
    public int ReusedRoots { get; private set; }

    private static HashSet<string> Changed<T>(ImmutableDictionary<string, T>? before, ImmutableDictionary<string, T> after)
    {
        if (ReferenceEquals(before, after)) return NoChanges;
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (before != null) foreach (var item in before) if (!after.TryGetValue(item.Key, out var value) || !EqualityComparer<T>.Default.Equals(item.Value, value)) result.Add(item.Key);
        foreach (var key in after.Keys) if (before?.ContainsKey(key) != true) result.Add(key);
        return result;
    }
    public DrawingScene Build(Drawing drawing, string layout = "Model")
    {
        if (_drawing != null && _drawing.Entities == drawing.Entities && ReferenceEquals(_drawing.Blocks, drawing.Blocks) && ReferenceEquals(_drawing.Layers, drawing.Layers) && ReferenceEquals(_drawing.Linetypes, drawing.Linetypes) && _drawing.LinetypeScale == drawing.LinetypeScale && layout == _layout && _scene != null) return _scene;
        var layers = Changed(_drawing?.Layers, drawing.Layers); var blocks = Changed(_drawing?.Blocks, drawing.Blocks); var types = Changed(_drawing?.Linetypes, drawing.Linetypes);
        var scaleChanged = _drawing?.LinetypeScale != drawing.LinetypeScale;
        var next = new Dictionary<Guid, Entry>(_roots.Count);
        var paths = ImmutableArray.CreateBuilder<ScenePath>(); var text = ImmutableArray.CreateBuilder<SceneText>(); var triangles = ImmutableArray.CreateBuilder<SceneTriangle>();
        RebuiltRoots = ReusedRoots = 0; long vertices = 0;
        foreach (var e in drawing.Entities)
        {
            if (!e.Visible || !e.Layout.Equals(layout, StringComparison.OrdinalIgnoreCase) || !drawing.LayerFor(e).Visible) continue;
            Entry entry;
            if (layout == _layout && _roots.TryGetValue(e.Id, out var previous) && ReferenceEquals(previous.Entity, e) &&
                !layers.Overlaps(previous.Layers) && !blocks.Overlaps(previous.Blocks) && !types.Overlaps(previous.Types) &&
                (!scaleChanged || previous.Scene.Paths.All(p => p.Pattern == null)))
            { entry = previous; ReusedRoots++; }
            else
            {
                var part = EntityGeometry.BuildScene(drawing with { Entities = [e] }, layout);
                var dependency = Dependencies(e, drawing);
                entry = new(e, part, dependency.Layers, dependency.Blocks, dependency.Types, part.Paths.Sum(p => (long)p.Points.Length)); RebuiltRoots++;
            }
            vertices += entry.Vertices;
            if (vertices > 2_000_000 || triangles.Count + (long)entry.Scene.Triangles.Length > 1_000_000) throw new ArgumentException("Expanded scene exceeds the geometry budget.");
            next[e.Id] = entry; paths.AddRange(entry.Scene.Paths); text.AddRange(entry.Scene.Texts); triangles.AddRange(entry.Scene.Triangles);
        }
        // Swap, rather than clear and copy a second dictionary; failures above leave the old cache intact.
        _roots = next; _drawing = drawing; _layout = layout;
        return _scene = new(paths.ToImmutable(), text.ToImmutable(), triangles.ToImmutable());
    }
    private static (string[] Layers, string[] Blocks, string[] Types) Dependencies(Entity root, Drawing drawing)
    {
        if (root is not (BlockReferenceEntity or PlacedEntity or CompositeEntity))
            return ([root.Layer], [], [Linetype.ResolveName(root.Linetype, drawing.LayerFor(root))]);
        var layers = new HashSet<string>(StringComparer.OrdinalIgnoreCase); var blocks = new HashSet<string>(StringComparer.OrdinalIgnoreCase); var types = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Visit(Entity e, string? inheritedLayer, string? inheritedType, int depth)
        {
            if (depth > 32) throw new ArgumentException("Scene dependency depth exceeds its budget.");
            var layerName = e.Layer == "0" && inheritedLayer != null ? inheritedLayer : e.Layer;
            layers.Add(e.Layer); layers.Add(layerName);
            var layer = drawing.Layers.GetValueOrDefault(layerName) ?? drawing.Layers["0"];
            var type = Linetype.ResolveName(e.Linetype, layer, inheritedType); types.Add(type);
            if (e is PlacedEntity placed) Visit(placed.Geometry, layerName, type, depth + 1);
            else if (e is CompositeEntity composite) foreach (var child in composite.Children) Visit(child, layerName, type, depth + 1);
            else if (e is BlockReferenceEntity insert)
            {
                blocks.Add(insert.Name);
                if (drawing.Blocks.TryGetValue(insert.Name, out var block)) foreach (var child in block.Entities) Visit(child, layerName, type, depth + 1);
            }
        }
        Visit(root, null, null, 0); return (layers.ToArray(), blocks.ToArray(), types.ToArray());
    }
}
