using System.Collections.Immutable;
using CadSpace.Geometry;

namespace CadSpace.Model;

/// <summary>Atomic immutable revisions with bounded undo history.</summary>
public sealed class CadDocument
{
    private readonly Stack<(string Name, Drawing State)> _undo = new();
    private readonly Stack<(string Name, Drawing State)> _redo = new();
    private Drawing? _saved;
    public CadDocument(Drawing? drawing = null) { Drawing = drawing ?? Drawing.Empty; _saved = Drawing; Validate(Drawing); }
    public Drawing Drawing { get; private set; }
    public long Revision { get; private set; }
    public bool IsDirty => Drawing != _saved;
    public bool CanUndo => _undo.Count != 0;
    public bool CanRedo => _redo.Count != 0;
    public event Action? Changed;
    public void Edit(string name, Func<Drawing, Drawing> edit)
    {
        var next = edit(Drawing); if (next == Drawing) return; Validate(next);
        _undo.Push((name, Drawing)); _redo.Clear(); Drawing = next;
        if (_undo.Count > 256) { var retained = _undo.Take(256).Reverse().ToArray(); _undo.Clear(); foreach (var item in retained) _undo.Push(item); }
        Notify();
    }
    public void Add(string name, params Entity[] entities) => Edit(name, s => s with { Entities = s.Entities.AddRange(entities) });
    public void Undo() { if (_undo.TryPop(out var p)) { _redo.Push((p.Name, Drawing)); Drawing = p.State; Notify(); } }
    public void Redo() { if (_redo.TryPop(out var p)) { _undo.Push((p.Name, Drawing)); Drawing = p.State; Notify(); } }
    public void MarkUnsaved() { _saved = null; Changed?.Invoke(); }
    public void MarkSaved() { _saved = Drawing; Changed?.Invoke(); }
    public void Load(Drawing drawing) { Validate(drawing); Drawing = drawing; _saved = drawing; _undo.Clear(); _redo.Clear(); Notify(); }
    private void Notify() { Revision++; Changed?.Invoke(); }
    public static void ValidateEntity(Entity entity, Drawing drawing, int depth = 0)
    {
        if (!Linetype.ValidName(entity.Linetype) || !double.IsFinite(entity.LinetypeScale) || entity.LinetypeScale <= 0 || entity.LinetypeScale > 1e9) throw new ArgumentException("Invalid entity linetype or scale.");
        if (depth > 32) throw new ArgumentException("Geometry nesting exceeds 32 levels.");
        if (string.IsNullOrWhiteSpace(entity.Layout) || entity.Layout.IndexOfAny(['\0', '\n', '\r']) >= 0) throw new ArgumentException("Invalid entity layout name.");
        if (!drawing.Layers.ContainsKey(entity.Layer)) throw new ArgumentException($"Missing layer: {entity.Layer}");
        if (EntityGeometry.Anchors(entity).Any(p => !p.IsFinite)) throw new ArgumentException("Coordinates must be finite.");
        if (!double.IsFinite(entity.LineWeight)) throw new ArgumentException("Nonfinite line weight.");
        if (entity is CircleEntity c && (!double.IsFinite(c.Radius) || c.Radius <= 0)) throw new ArgumentException("Circle radius must be positive.");
        if (entity is ArcEntity a && (!double.IsFinite(a.Radius) || a.Radius <= 0 || !double.IsFinite(a.StartAngle) || !double.IsFinite(a.EndAngle))) throw new ArgumentException("Invalid arc.");
        if (entity is EllipseEntity ell && (ell.MajorAxis.Length <= 1e-9 || !double.IsFinite(ell.Ratio) || ell.Ratio <= 0 || !double.IsFinite(ell.StartParameter) || !double.IsFinite(ell.EndParameter))) throw new ArgumentException("Invalid ellipse.");
        if (entity is TextEntity t && (!double.IsFinite(t.Height) || t.Height <= 0 || !double.IsFinite(t.Rotation))) throw new ArgumentException("Invalid text geometry.");
        if (entity is DimensionEntity dim) DimensionGeometry.Validate(dim);
        if (entity is PolylineEntity wide) PolylineWidths.Validate(wide);
        if (entity is PolylineEntity p && (p.Vertices.Length < 2 || p.Vertices.Any(v => !double.IsFinite(v.Bulge)))) throw new ArgumentException("Invalid polyline.");
        if (entity is MeshEntity m && (m.Triangles.Length % 3 != 0 || m.Triangles.Length > 3000000 || m.Triangles.Any(i => i < 0 || i >= m.Vertices.Length))) throw new ArgumentException("Invalid mesh indices.");
        if (entity is HatchEntity h && (h.Boundary.Length < 3 || !double.IsFinite(h.Spacing) || h.Spacing <= 0 || !double.IsFinite(h.Angle))) throw new ArgumentException("Invalid hatch.");
        if (entity is BlockReferenceEntity b && (!b.Scale.IsFinite || Math.Abs(b.Scale.X * b.Scale.Y * b.Scale.Z) < 1e-15 || !double.IsFinite(b.Rotation))) throw new ArgumentException("Invalid block transform.");
        if (AdvancedGeometry.IsAdvanced(entity)) AdvancedGeometry.Validate(entity, depth);
        if (entity is PlacedEntity placed) ValidateEntity(placed.Geometry, drawing, depth + 1);
        if (entity is CompositeEntity composite) foreach (var child in composite.Children) ValidateEntity(child, drawing, depth + 1);
    }
    public static void Validate(Drawing drawing)
    {
        if (drawing.LayoutBlockNames.Any(p => string.IsNullOrWhiteSpace(p.Key) || string.IsNullOrWhiteSpace(p.Value) || p.Key.IndexOfAny(['\0', '\r', '\n']) >= 0 || p.Value.IndexOfAny(['\0', '\r', '\n']) >= 0) || drawing.LayoutBlockNames.Values.Distinct(StringComparer.OrdinalIgnoreCase).Count() != drawing.LayoutBlockNames.Count) throw new ArgumentException("Invalid layout/block mapping.");
        if (!drawing.Linetypes.ContainsKey("CONTINUOUS") || !double.IsFinite(drawing.LinetypeScale) || drawing.LinetypeScale <= 0 || drawing.LinetypeScale > 1e9) throw new ArgumentException("Invalid drawing linetypes or global scale.");
        foreach (var pair in drawing.Linetypes) { Linetype.Validate(pair.Value); if (!pair.Key.Equals(pair.Value.Name, StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Linetype key/name mismatch."); }
        foreach (var layer in drawing.Layers.Values) if (!Linetype.ValidName(layer.Linetype)) throw new ArgumentException("Invalid layer linetype.");
        if (!drawing.Layers.ContainsKey("0")) throw new ArgumentException("Layer 0 is required.");
        if (drawing.Entities.Length > 1000000 || drawing.Entities.Select(e => e.Id).Distinct().Count() != drawing.Entities.Length) throw new ArgumentException("Too many entities or duplicate entity IDs.");
        foreach (var layer in drawing.Layers.Values)
            if (string.IsNullOrWhiteSpace(layer.Name) || layer.Name.Contains('\n') || layer.Name.Contains('\r') || !double.IsFinite(layer.LineWeight) || layer.LineWeight < 0) throw new ArgumentException("Invalid layer.");
        foreach (var entity in drawing.Entities.Concat(drawing.Blocks.Values.SelectMany(b => b.Entities))) ValidateEntity(entity, drawing);
        IEnumerable<string> References(Entity e) => e switch { BlockReferenceEntity b => [b.Name], DimensionEntity d when DimensionGeometry.UsesPicture(d) => [d.Picture!.BlockName], PlacedEntity p => References(p.Geometry), CompositeEntity c => c.Children.SelectMany(References), _ => [] };
        void Visit(string name, HashSet<string> ancestors)
        {
            if (!drawing.Blocks.TryGetValue(name, out var block)) return;
            if (!block.BasePoint.IsFinite || block.Name.Contains('\n') || block.Name.Contains('\r')) throw new ArgumentException("Invalid block definition.");
            if (!ancestors.Add(name)) throw new ArgumentException($"Cyclic block reference: {name}");
            if (ancestors.Count > 32) throw new ArgumentException("Block nesting exceeds 32 levels.");
            foreach (var reference in block.Entities.SelectMany(References)) Visit(reference, ancestors);
            ancestors.Remove(name);
        }
        foreach (var name in drawing.Blocks.Keys) Visit(name, new(StringComparer.OrdinalIgnoreCase));
    }
}
