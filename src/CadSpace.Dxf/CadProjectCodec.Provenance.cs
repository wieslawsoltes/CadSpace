using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using CadSpace.Geometry;
using CadSpace.Model;

namespace CadSpace.Dxf;

public static partial class CadProjectCodec
{
    private static Entity VerifySourceEntity(Entity actual, Entity saved, ImmutableArray<DxfPair> originalRecord = default)
    {
        actual=actual with{Id=saved.Id};
        if (saved is OpaqueEntity opaque && actual is not OpaqueEntity)
        {
            // An older interpreter may have retained a now-supported root as opaque data.
            // Preserve that native representation only after validating ALL original group values
            // and common properties, not merely its displayed geometry or a claimed checksum.
            if (opaque.RawRecord.Length > DxfCodec.MaximumCharacters || originalRecord.IsDefaultOrEmpty || originalRecord[0].Code != 0 ||
                originalRecord[0].Value.Trim() != opaque.DxfType ||
                !DxfCodec.ParsePairs(opaque.RawRecord).SequenceEqual(originalRecord) ||
                CommonProperties(actual) != CommonProperties(saved))
                throw new FormatException("Opaque provenance does not match the original DXF record.");
            return saved;
        }
        if(actual is PlacedEntity a && saved is PlacedEntity b) actual=a with{Geometry=VerifySourceEntity(a.Geometry,b.Geometry)};
        if(actual is CompositeEntity x && saved is CompositeEntity y)
        {
            if(x.Children.Length!=y.Children.Length)throw new FormatException("Provenance child counts differ from DXF.");
            actual=x with{Children=x.Children.Zip(y.Children).Select(p=>VerifySourceEntity(p.First,p.Second)).ToImmutableArray()};
        }
        if (!Equivalent(actual, saved))
        {
            if (actual is CompositeEntity { DxfType: "INSERT", Children.Length: 1 } insert && saved is not CompositeEntity &&
                !originalRecord.IsDefaultOrEmpty && originalRecord[0].Value.Trim() == "INSERT")
            {
                var child = insert.Children[0] with { Id = actual.Id, Handle = actual.Handle, Layer = actual.Layer,
                    ColorIndex = actual.ColorIndex, TrueColor = actual.TrueColor, LineWeight = actual.LineWeight, Linetype = actual.Linetype,
                    LinetypeScale = actual.LinetypeScale, Visible = actual.Visible, Layout = actual.Layout };
                return VerifySourceEntity(child, saved);
            }
            if (!originalRecord.IsDefaultOrEmpty && originalRecord[0].Value.Trim() is "DIMENSION" or "LEADER")
            {
                // Older projects used anonymous display wrappers or basic aligned definitions. Validate
                // the entire earlier interpretation, including raw data, instead of trusting a saved picture.
                var legacy = DxfEntityReader.Read(DxfRecordEditing.SemanticPairs(originalRecord), _ => { });
                if (legacy is CompositeEntity c) legacy = c with { SourceRecord = string.Concat(originalRecord.Select(p => $"{p.Code.ToString(System.Globalization.CultureInfo.InvariantCulture)}\n{p.Value}\n")) };
                return VerifySourceEntity(legacy with { Layout = actual.Layout }, saved);
            }
            throw new FormatException("Provenance geometry differs from the original DXF. Refusing unsafe source-record reuse.");
        }
        return saved;
    }
    private static (string Handle, string Layer, int Aci, uint? Color, double Weight,
        string Linetype, double Scale, bool Visible, string Layout) CommonProperties(Entity e) =>
        (e.Handle, e.Layer, e.ColorIndex, e.TrueColor, e.LineWeight, e.Linetype, e.LinetypeScale, e.Visible, e.Layout);
    private static Drawing Intern(Drawing drawing, Drawing original)
    {
        var originals = original.Entities.Concat(original.Blocks.Values.SelectMany(b => b.Entities)).ToDictionary(e => e.Id);
        Entity Canonical(Entity entity) => originals.TryGetValue(entity.Id, out var old) && Equivalent(entity, old) ? old : entity;
        var entities = drawing.Entities.Select(Canonical).ToImmutableArray();
        if (entities.SequenceEqual(original.Entities)) entities = original.Entities;
        var layers = drawing.Layers;
        if (layers.Count == original.Layers.Count && layers.All(p => original.Layers.TryGetValue(p.Key, out var l) && l == p.Value)) layers = original.Layers;
        var blocks = drawing.Blocks;
        foreach (var block in drawing.Blocks.Values)
        {
            var children = block.Entities.Select(Canonical).ToImmutableArray();
            var updated = block with { Entities = children };
            if (original.Blocks.TryGetValue(block.Name, out var old) && old.BasePoint == block.BasePoint && children.SequenceEqual(old.Entities)) updated = old;
            blocks = blocks.SetItem(block.Name, updated);
        }
        if (blocks.Count == original.Blocks.Count && blocks.All(p => original.Blocks.TryGetValue(p.Key, out var b) && b == p.Value)) blocks = original.Blocks;
        var layouts = drawing.LayoutBlockNames;
        if (layouts.Count == original.LayoutBlockNames.Count && layouts.All(p => original.LayoutBlockNames.TryGetValue(p.Key, out var name) && name == p.Value)) layouts = original.LayoutBlockNames;
        var types = drawing.Linetypes;
        if (types.Count == original.Linetypes.Count && types.All(p => original.Linetypes.TryGetValue(p.Key, out var before) && Linetype.Equivalent(p.Value, before))) types = original.Linetypes;
        return drawing with { Entities = entities, Layers = layers, Blocks = blocks, LayoutBlockNames = layouts, Linetypes = types };
    }
    private static bool Equivalent(Entity a, Entity b) => (a, b) switch
    {
        (LeaderEntity x, LeaderEntity y) => x.Vertices.SequenceEqual(y.Vertices) && x with { Vertices = y.Vertices } == y,
        (PolylineEntity x, PolylineEntity y) => x.Vertices.SequenceEqual(y.Vertices) && x with { Vertices = y.Vertices } == y,
        (HatchEntity x, HatchEntity y) => x.Boundary.SequenceEqual(y.Boundary) && x with { Boundary = y.Boundary } == y,
        (MeshEntity x, MeshEntity y) => x.Vertices.SequenceEqual(y.Vertices) && x.Triangles.SequenceEqual(y.Triangles) && x with { Vertices = y.Vertices, Triangles = y.Triangles } == y,
        (Polyline3DEntity x, Polyline3DEntity y) => x.Points.SequenceEqual(y.Points) && x with { Points = y.Points } == y,
        (SplineEntity x, SplineEntity y) => x.ControlPoints.SequenceEqual(y.ControlPoints) && x.Knots.SequenceEqual(y.Knots) && x.Weights.SequenceEqual(y.Weights) && x with { ControlPoints = y.ControlPoints, Knots = y.Knots, Weights = y.Weights } == y,
        (PlacedEntity x, PlacedEntity y) => Equivalent(x.Geometry, y.Geometry) && x with { Geometry = y.Geometry } == y,
        (CompositeEntity x, CompositeEntity y) => x.Children.Length == y.Children.Length && x.Children.Zip(y.Children).All(p => Equivalent(p.First, p.Second)) && x with { Children = y.Children } == y,
        (HatchRegionEntity x, HatchRegionEntity y) => x.Loops.Length == y.Loops.Length && x.Loops.Zip(y.Loops).All(p => p.First.SequenceEqual(p.Second)) && x.Pattern.Length == y.Pattern.Length && x.Pattern.Zip(y.Pattern).All(p => p.First.Dashes.SequenceEqual(p.Second.Dashes) && p.First with { Dashes = p.Second.Dashes } == p.Second) && x with { Loops = y.Loops, Pattern = y.Pattern } == y,
        _ => a == b
    };
}
