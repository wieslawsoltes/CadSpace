using System.Collections.Immutable;
using System.Globalization;
using CadSpace.Geometry;
using CadSpace.Model;

namespace CadSpace.Dxf;

/// <summary>Typed interpreters for logical entity records. Unknown/unsupported semantics are never silently discarded.</summary>
internal static class DxfEntityReader
{
    public static Entity Read(ImmutableArray<DxfPair> record, Action<string> warn)
    {
        var pieces = Records(record).ToArray(); var header = pieces[0];
        var type = S(header, 0).Trim(); var normal = P(header, 210, Vec3.UnitZ);
        var ocs = Coordinates3D.ObjectCoordinateSystem(normal);
        Entity InPlane(Entity entity) => normal.DistanceTo(Vec3.UnitZ) < 1e-12 ? entity : new PlacedEntity(entity, ocs);
        Entity entity;
        switch (type)
        {
            case "LINE": entity = new LineEntity(P(header, 10), P(header, 11)); break;
            case "POINT": entity = new PointEntity(P(header, 10)); break;
            case "CIRCLE": entity = InPlane(new CircleEntity(P(header, 10), Positive(header, 40))); break;
            case "ARC": entity = InPlane(new ArcEntity(P(header, 10), Positive(header, 40), N(header, 50), N(header, 51))); break;
            case "LWPOLYLINE":
                var points = DxfVertexData.Polyline(header.AsSpan(), N(header, 38));
                if (I(header, 90, points.Length) != points.Length) throw new FormatException("LWPOLYLINE vertex count mismatch.");
                entity = InPlane(new PolylineEntity(points, (I(header, 70) & 1) != 0) { ConstantWidth = N(header, 43), ContinuousLinetype = (I(header, 70) & 128) != 0 }); break;
            case "POLYLINE": entity = LegacyPolyline(header, pieces.Skip(1).ToArray(), InPlane, warn); break;
            case "SPLINE": entity = Spline(header); break;
            case "ELLIPSE":
                var center = P(header, 10); var major = P(header, 11);
                var local = Coordinates3D.Inverse(ocs);
                if (Math.Abs(major.Dot(normal.Normalized)) > Math.Max(1, major.Length) * 1e-8) throw new FormatException("Ellipse axis is not perpendicular to its normal.");
                entity = new PlacedEntity(new EllipseEntity(local.Point(center), local.Vector(major), Positive(header, 40), N(header, 41), N(header, 42, 2 * Math.PI)), ocs); break;
            case "3DFACE": entity = Face(header, false); break;
            case "SOLID": case "TRACE": entity = InPlane(Face(header, true)); break;
            case "MESH": entity = Mesh(header, warn); break;
            case "HATCH": entity = InPlane(Hatch(header, warn)); break;
            case "TEXT": case "ATTRIB": entity = InPlane(StyledText(header, warn)); break;
            case "ATTDEF":
                if ((I(header, 70) & 2) == 0) return Opaque(type, record);
                entity = InPlane(StyledText(header, warn)); break;
            case "MTEXT":
                var text = Text(header, true, warn);
                // MTEXT rotation is radians; the last direction/rotation field wins (Autodesk DXF reference).
                var direction = ocs.X; var index50=-1; var index11=-1;
                for(var i=0;i<header.Length;i++){if(header[i].Code==75)break;if(header[i].Code==50)index50=i;if(header[i].Code==11)index11=i;}
                if(index50>index11)direction=ocs.Vector(GeometryMath.OnCircle(default,1,GeometryMath.Degrees(Number(header[index50].Value))));
                else if(index11>=0)direction=P(header.Skip(index11).ToImmutableArray(),11,ocs.X);
                var up = normal.Normalized.Cross(direction).Normalized;
                var frame = new Transform3(direction.Normalized, up, normal.Normalized, P(header, 10));
                entity = new PlacedEntity(text with { Position = default, Rotation = 0 }, frame); break;
            case "INSERT":
                var insertion = P(header, 10); var rotation = N(header, 50);
                var columns = I(header, 70, 1); var rows = I(header, 71, 1);
                if (columns < 1 || rows < 1 || (long)columns * rows > 10000) throw new FormatException("INSERT array exceeds 10,000 instances.");
                var children = ImmutableArray.CreateBuilder<Entity>();
                for (var row = 0; row < rows; row++) for (var column = 0; column < columns; column++)
                {
                    // Array offsets rotate with the INSERT but are not scaled by its scale factors.
                    var offset = Transform3.RotationZ(rotation).Vector(new(column * N(header, 44), row * N(header, 45)));
                    children.Add(InPlane(new BlockReferenceEntity(S(header, 2), insertion + offset, new(N(header, 41, 1), N(header, 42, 1), N(header, 43, 1)), rotation)));
                }
                foreach (var attribute in pieces.Skip(1).Where(p => S(p, 0) == "ATTRIB"))
                {
                    if ((I(attribute, 70) & 1) != 0) continue;
                    children.Add(Read(attribute, warn));
                }
                entity = children.Count == 1 && !pieces.Skip(1).Any(r => S(r, 0) == "ATTRIB") ? children[0] : new CompositeEntity("INSERT", children.ToImmutable(), Encode(record)); break;
            case "DIMENSION":
                var block = S(header, 2);
                if (block.Length > 0)
                {
                    warn("DIMENSION uses its anonymous display block; associative dimension styles/constraints remain source data.");
                    entity = new CompositeEntity("DIMENSION", [new BlockReferenceEntity(block, ocs.Point(P(header, 12)), new(1, 1, 1))], Encode(record));
                }
                else if ((I(header, 70) & 7) == 1) entity = new DimensionEntity(P(header, 13), P(header, 14), P(header, 10));
                else throw new NotSupportedException("This dimension requires an anonymous display block.");
                break;
            case "LEADER":
                var vertices = Points(header, 10);
                if (vertices.Length < 2) throw new FormatException("LEADER has too few vertices.");
                warn("LEADER renders its vertex path; annotation/arrow style semantics remain in the source record.");
                entity = new CompositeEntity("LEADER", [new Polyline3DEntity(vertices)], Encode(record)); break;
            default: return Opaque(type, record);
        }
        var thickness = N(header, 39);
        if (thickness != 0)
        {
            if (type is "CIRCLE" or "ARC" or "LWPOLYLINE" or "POLYLINE" or "SOLID" or "TRACE" or "LINE")
                entity = Thickened(entity, normal.Normalized * thickness);
            else warn($"{type} thickness is retained in source data but not interpreted for display.");
        }
        
        var layer = S(header, 8, "0");
        entity = entity with { Layer = layer, Handle = S(header, 5), ColorIndex = I(header, 62, 256), TrueColor = Has(header, 420) ? 0xFF000000u | (uint)I(header, 420) : null, LineWeight = N(header, 370, -100) / 100, Linetype = S(header, 6, "BYLAYER"), LinetypeScale = N(header, 48, 1), Visible = I(header, 60) == 0, Layout = S(header, 410, I(header, 67) == 0 ? "Model" : "Layout1") };
        return entity;
    }

    private static Entity Thickened(Entity entity, Vec3 extrusion)
    {
        // Surface extrusion, not a solid/B-rep. Retains the exact original entity plus side geometry.
        var drawing = Drawing.Empty with { Entities = [entity] }; var scene = EntityGeometry.BuildScene(drawing);
        var children = ImmutableArray.CreateBuilder<Entity>(); children.Add(entity); children.Add(EntityGeometry.Transform(entity, Transform3.Translation(extrusion), true));
        foreach (var path in scene.Paths.Where(p => p.Points.Length > 1))
        {
            var verts = path.Points.Concat(path.Points.Select(p => p + extrusion)).ToImmutableArray(); var n = path.Points.Length; var indices = ImmutableArray.CreateBuilder<int>();
            for (var i = 0; i < n - (path.Closed ? 0 : 1); i++) { var j = (i + 1) % n; indices.AddRange(new[] { i, j, j + n, i, j + n, i + n }); }
            children.Add(new MeshEntity(verts, indices.ToImmutable(), "DXF thickness surface"));
        }
        return new CompositeEntity(entity.Kind, children.ToImmutable());
    }

    private static Entity LegacyPolyline(ImmutableArray<DxfPair> header, ImmutableArray<DxfPair>[] records, Func<Entity, Entity> inPlane, Action<string> warn)
    {
        if (records.Length == 0 || S(records[^1], 0) != "SEQEND") throw new FormatException("POLYLINE has no SEQEND.");
        var vertices = records.Where(r => S(r, 0) == "VERTEX").ToArray(); var flags = I(header, 70);
        if ((flags & 64) != 0)
        {
            var coordinates = vertices.Where(v => (I(v, 70) & 64) != 0).Select(v => P(v, 10)).ToImmutableArray();
            var triangles = ImmutableArray.CreateBuilder<int>();
            foreach (var face in vertices.Where(v => (I(v, 70) & 64) == 0))
            {
                if (Enumerable.Range(71, 4).Any(c => I(face, c) < 0)) warn("Polyface invisible-edge flags remain in source data; feature-edge display is geometric.");
                var faceIndices = Enumerable.Range(71, 4).Select(c => I(face, c)).TakeWhile(i => i != 0).Select(i => checked(Math.Abs(i) - 1)).ToArray();
                AddFace(coordinates, faceIndices, triangles);
            }
            return new MeshEntity(coordinates, triangles.ToImmutable(), "DXF polyface");
        }
        if ((flags & 16) != 0)
        {
            var m = I(header, 71); var n = I(header, 72);
            if (m < 2 || n < 2 || (long)m * n != vertices.Length) throw new FormatException("Polygon mesh dimensions do not match its vertices.");
            if (I(header, 75) != 0) warn("Spline-fit polygon mesh displays its stored vertex grid, not a regenerated smooth surface.");
            var indices = ImmutableArray.CreateBuilder<int>();
            for (var i = 0; i < m - ((flags & 1) != 0 ? 0 : 1); i++) for (var j = 0; j < n - ((flags & 32) != 0 ? 0 : 1); j++)
            {
                var a = i * n + j; var b = ((i + 1) % m) * n + j; var c = ((i + 1) % m) * n + (j + 1) % n; var d = i * n + (j + 1) % n;
                indices.AddRange(new[] { a, b, c, a, c, d });
            }
            return new MeshEntity(vertices.Select(v => P(v, 10)).ToImmutableArray(), indices.ToImmutable(), "DXF polygon mesh");
        }
        if ((flags & 6) != 0) throw new NotSupportedException("Curve-fit legacy POLYLINE requires its fitted-curve interpreter.");
        if ((flags & 8) != 0) return new Polyline3DEntity(vertices.Select(v => P(v, 10)).ToImmutableArray(), (flags & 1) != 0) { ContinuousLinetype = (flags & 128) != 0 };
        var elevation = N(header, 30);
        return inPlane(new PolylineEntity(vertices.Select(v => new PolyVertex(new(P(v, 10).X, P(v, 10).Y, elevation), N(v, 42)) { StartWidth = N(v, 40, N(header, 40)), EndWidth = N(v, 41, N(header, 41)) }).ToImmutableArray(), (flags & 1) != 0) { ContinuousLinetype = (flags & 128) != 0 });
    }

    private static SplineEntity Spline(ImmutableArray<DxfPair> p)
    {
        var degree = I(p, 71); var points = Points(p, 10); var knots = p.Where(v => v.Code == 40).Select(v => Number(v.Value)).ToImmutableArray(); var weights = p.Where(v => v.Code == 41).Select(v => Number(v.Value)).ToImmutableArray();
        if (points.Length != I(p, 73, points.Length) || knots.Length != I(p, 72, knots.Length)) throw new FormatException("SPLINE declared counts do not match its data.");
        var spline = new SplineEntity(degree, points, knots, weights, (I(p, 70) & 1) != 0, (I(p, 70) & 2) != 0);
        AdvancedGeometry.ValidateSpline(spline); return spline;
    }

    private static MeshEntity Mesh(ImmutableArray<DxfPair> p, Action<string> warn)
    {
        var positions = Points(p, 10); var count = Index(p, 92);
        if (count < 0 || I(p, 92) != positions.Length) throw new FormatException("MESH vertex count mismatch.");
        var list = Index(p, 93); var size = I(p, 93);
        if (list < 0 || size < 0 || size > 4000000) throw new FormatException("Invalid MESH face list.");
        var values = p.Skip(list + 1).Take(size).ToArray();
        if (values.Length != size || values.Any(v => v.Code != 90)) throw new FormatException("Truncated MESH face list.");
        var result = ImmutableArray.CreateBuilder<int>();
        for (var i = 0; i < values.Length;)
        {
            var n = int.Parse(values[i++].Value, CultureInfo.InvariantCulture);
            if (n < 3 || n > 100000 || i + n > values.Length) throw new FormatException("Invalid face vertex count.");
            var face = values.Skip(i).Take(n).Select(v => int.Parse(v.Value, CultureInfo.InvariantCulture)).ToArray(); i += n;
            AddFace(positions, face, result);
        }
        if (I(p, 91) != 0) warn("MESH subdivision levels are retained in source data; its control mesh is displayed.");
        return new(positions, result.ToImmutable(), "DXF MESH");
    }

    private static void AddFace(ImmutableArray<Vec3> points, int[] face, ImmutableArray<int>.Builder triangles)
    {
        if (face.Length < 3 || face.Any(i => i < 0 || i >= points.Length)) throw new FormatException("Mesh face index is out of range.");
        if (face.Length == 3) { triangles.AddRange(face); return; }
        if (face.Length == 4) { triangles.AddRange(new[] { face[0], face[1], face[2], face[0], face[2], face[3] }); return; }
        triangles.AddRange(Triangulation.Polygon3D(face.Select(i => points[i]).ToArray()).Select(i => face[i]));
    }

    private static MeshEntity Face(ImmutableArray<DxfPair> p, bool solid)
    {
        var a = P(p, 10); var b = P(p, 11); var c = P(p, 12); var d = P(p, 13, c);
        if (c.DistanceTo(d) < 1e-12) return new([a, b, c], [0, 1, 2], solid ? "DXF solid fill" : "3DFACE");
        return solid ? new([a, b, d, c], [0, 1, 2, 0, 2, 3], "DXF solid fill") : new([a, b, c, d], [0, 1, 2, 0, 2, 3], "3DFACE");
    }

    private static Entity StyledText(ImmutableArray<DxfPair> p, Action<string> warn)
    {
        var text = Text(p, false, warn); var width = N(p, 41, 1); if (width <= 0) throw new FormatException("Text width must be positive."); var oblique = N(p, 51); var flags = I(p, 71);
        if (Math.Abs(oblique) >= 85) throw new NotSupportedException("Text oblique angle must be within (-85, 85) degrees.");
        if (width == 1 && oblique == 0 && flags == 0) return text;
        var shape = new Transform3(new((flags & 2) == 0 ? width : -width, 0), new(Math.Tan(GeometryMath.Radians(oblique)), (flags & 4) == 0 ? 1 : -1), Vec3.UnitZ, default);
        return new PlacedEntity(text with { Position = default, Rotation = 0 }, shape.Then(Transform3.RotationZ(text.Rotation)).Then(Transform3.Translation(text.Position)));
    }

    private static TextEntity Text(ImmutableArray<DxfPair> p, bool multiline, Action<string> warn)
    {
        var value = multiline ? string.Concat(p.Where(v => v.Code == 3).Select(v => v.Value)) + S(p, 1) : S(p, 1);
        if (multiline) warn("MTEXT currently uses plain text display; complex inline formatting, columns and attachment styles are retained as source data.");
        else if (I(p, 72) != 0 || I(p, S(p, 0) == "TEXT" ? 73 : 74) != 0)
            throw new NotSupportedException("Non-baseline text alignment requires style metrics and is retained as source data.");
        return new(P(p, 10), value, Positive(p, 40), N(p, 50), multiline);
    }

    private static HatchRegionEntity Hatch(ImmutableArray<DxfPair> p, Action<string> warn)
    {
        var n = I(p, 91); if (n < 1 || n > 1024) throw new FormatException("Invalid hatch loop count.");
        if (I(p, 75) is < 0 or > 2) throw new FormatException("Invalid hatch island style.");
        var gradient = DxfHatchGradient.Read(p);
        if (gradient != null) warn("Gradient HATCH data is retained natively; nonlinear color profiles are approximated for display.");
        var elevation = N(p, 30); var loops = ImmutableArray.CreateBuilder<ImmutableArray<PolyVertex>>();
        var index = Index(p, 91) + 1; var sampled = false;
        for (var path = 0; path < n; path++)
        {
            while (index < p.Length && p[index].Code != 92) index++;
            if (index == p.Length) throw new FormatException("Missing hatch path.");
            var start = index; var flags = int.Parse(p[index++].Value, CultureInfo.InvariantCulture);
            while (index < p.Length && p[index].Code != 92 && p[index].Code != 75) index++;
            var boundary = p.Skip(start).Take(index - start).ToImmutableArray();
            if ((flags & 2) != 0)
            {
                if (I(boundary, 73) != 1) throw new NotSupportedException("An open hatch polyline boundary is not a closed region.");
                var vertices = Vertices(boundary, elevation);
                if (vertices.Length != I(boundary, 93)) throw new FormatException("Hatch polyline count mismatch.");
                loops.Add(vertices);
            }
            else
            {
                var edges = ImmutableArray.CreateBuilder<Vec3>(); var expected = I(boundary, 93); var seen = 0;
                for (var i = Index(boundary, 93) + 1; i < boundary.Length; i++)
                {
                    if (boundary[i].Code != 72) continue;
                    var edgeStart = i++; var edgeType = int.Parse(boundary[edgeStart].Value, CultureInfo.InvariantCulture);
                    while (i < boundary.Length && boundary[i].Code != 72) i++;
                    var edge = boundary.Skip(edgeStart).Take(i - edgeStart).ToImmutableArray(); i--; seen++;
                    IEnumerable<Vec3> samples;
                    if (edgeType == 1) samples = [P(edge, 10) with { Z = elevation }, P(edge, 11) with { Z = elevation }];
                    else if (edgeType == 2)
                    {
                        var startAngle = N(edge, 50); var endAngle = N(edge, 51); var ccw = I(edge, 73) != 0;
                        var sweep = ccw ? GeometryMath.NormalizeAngle(endAngle - startAngle) : -GeometryMath.NormalizeAngle(startAngle - endAngle);
                        if (Math.Abs(sweep) < 1e-9) sweep = ccw ? 360 : -360;
                        samples = EntityGeometry.Curve(P(edge, 10) with { Z = elevation }, Positive(edge, 40), startAngle, sweep); sampled = true;
                    }
                    else if (edgeType == 3)
                    {
                        var c = P(edge, 10) with { Z = elevation }; var major = P(edge, 11); var minor = new Vec3(-major.Y, major.X) * Positive(edge, 40);
                        var startAngle = N(edge, 50); var endAngle = N(edge, 51); var ccw = I(edge, 73) != 0;
                        var sweep = ccw ? GeometryMath.NormalizeAngle(endAngle - startAngle) : -GeometryMath.NormalizeAngle(startAngle - endAngle); if (Math.Abs(sweep) < 1e-9) sweep = ccw ? 360 : -360;
                        samples = Enumerable.Range(0, 257).Select(k => c + major * Math.Cos(GeometryMath.Radians(startAngle + sweep * k / 256)) + minor * Math.Sin(GeometryMath.Radians(startAngle + sweep * k / 256))); sampled = true;
                    }
                    else if (edgeType == 4)
                    {
                        var spline = new SplineEntity(I(edge, 94), Points(edge, 10).Select(v => v with { Z = elevation }).ToImmutableArray(), edge.Where(v => v.Code == 40).Select(v => Number(v.Value)).ToImmutableArray(), edge.Where(v => v.Code == 42).Select(v => Number(v.Value)).ToImmutableArray(), false, I(edge, 74) != 0);
                        samples = AdvancedGeometry.Tessellate(spline); sampled = true;
                    }
                    else throw new NotSupportedException($"Unknown hatch edge type {edgeType}.");
                    var list = samples.ToArray();
                    if (edges.Count > 0 && edges[^1].DistanceTo(list[0]) > 1e-6) throw new FormatException("Hatch edges are disconnected or not ordered.");
                    edges.AddRange(edges.Count == 0 ? list : list.Skip(1));
                }
                if (seen != expected || edges.Count < 4 || edges[0].DistanceTo(edges[^1]) > 1e-6) throw new FormatException("Hatch path is incomplete.");
                edges.RemoveAt(edges.Count - 1); loops.Add(edges.Select(v => new PolyVertex(v)).ToImmutableArray());
            }
        }
        var patterns = ImmutableArray.CreateBuilder<HatchPatternLine>();
        for (var i = Math.Max(index, 0); i < p.Length; i++)
        {
            if (p[i].Code != 53) continue;
            var start = i++; while (i < p.Length && p[i].Code != 53 && p[i].Code != 98) i++;
            var line = p.Skip(start).Take(i - start).ToImmutableArray(); i--;
            var dashes = line.Where(v => v.Code == 49).Select(v => Number(v.Value)).ToImmutableArray();
            if (dashes.Length != I(line, 79)) throw new FormatException("Hatch pattern dash count mismatch.");
            patterns.Add(new(N(line, 53), new(N(line, 43), N(line, 44), elevation), new(N(line, 45), N(line, 46)), dashes));
        }
        var solid = I(p, 70) != 0;
        if (!solid && (patterns.Count == 0 || patterns.Count != I(p, 78))) throw new FormatException("Hatch pattern definitions are incomplete.");
        if (sampled) warn("Curved edge-list HATCH boundaries are tessellated for editing; unchanged DXF retains the original analytic boundary.");
        return new(loops.ToImmutable(), solid, patterns.ToImmutable(), S(p, 2, "SOLID"), sampled) { IslandStyle = I(p, 75), Gradient = gradient };
    }

    internal static IEnumerable<ImmutableArray<DxfPair>> LogicalRecords(ImmutableArray<DxfPair> pairs)
    {
        using var it = Records(pairs).GetEnumerator();
        while (it.MoveNext())
        {
            var record = it.Current; var type = S(record, 0);
            if (type == "POLYLINE" || type == "INSERT" && I(record, 66) != 0)
            {
                var full = record.ToBuilder(); var ended = false;
                while (it.MoveNext()) { full.AddRange(it.Current); if (S(it.Current, 0) == "SEQEND") { ended = true; break; } }
                if (!ended) throw new FormatException($"{type} sequence has no SEQEND.");
                record = full.ToImmutable();
            }
            yield return record;
        }
    }
    internal static IEnumerable<ImmutableArray<DxfPair>> Records(ImmutableArray<DxfPair> pairs)
    {
        var begin = 0;
        for (var i = 1; i < pairs.Length; i++) if (pairs[i].Code == 0) { yield return pairs.Skip(begin).Take(i - begin).ToImmutableArray(); begin = i; }
        if (begin < pairs.Length) yield return pairs.Skip(begin).ToImmutableArray();
    }
    private static ImmutableArray<PolyVertex> Vertices(ImmutableArray<DxfPair> pairs, double elevation)
    {
        var vertices = ImmutableArray.CreateBuilder<PolyVertex>();
        for (var i = 0; i < pairs.Length; i++) if (pairs[i].Code == 10)
        {
            var x = Number(pairs[i].Value); double? y = null; double bulge = 0;
            for (var j = i + 1; j < pairs.Length && pairs[j].Code != 10; j++) { if (pairs[j].Code == 20) y = Number(pairs[j].Value); if (pairs[j].Code == 42) bulge = Number(pairs[j].Value); }
            if (y == null) throw new FormatException("A polyline vertex has no Y coordinate.");
            vertices.Add(new(new(x, y.Value, elevation), bulge));
        }
        return vertices.ToImmutable();
    }
    internal static ImmutableArray<Vec3> Points(ImmutableArray<DxfPair> pairs, int code)
    {
        var result = ImmutableArray.CreateBuilder<Vec3>();
        for (var i = 0; i < pairs.Length; i++) if (pairs[i].Code == code)
        {
            var x = Number(pairs[i].Value); double y = 0, z = 0;
            for (var j = i + 1; j < pairs.Length && pairs[j].Code != code; j++) { if (pairs[j].Code == code + 10) y = Number(pairs[j].Value); if (pairs[j].Code == code + 20) z = Number(pairs[j].Value); }
            result.Add(new(x, y, z));
        }
        return result.ToImmutable();
    }
    private static int Index(ImmutableArray<DxfPair> p, int code) { for (var i = 0; i < p.Length; i++) if (p[i].Code == code) return i; return -1; }
    private static bool Has(ImmutableArray<DxfPair> p, int code) => Index(p, code) >= 0;
    private static string S(ImmutableArray<DxfPair> p, int c, string fallback = "") => p.FirstOrDefault(v => v.Code == c).Value ?? fallback;
    private static int I(ImmutableArray<DxfPair> p, int c, int fallback = 0) => Has(p, c) ? int.Parse(S(p, c), CultureInfo.InvariantCulture) : fallback;
    private static double N(ImmutableArray<DxfPair> p, int c, double fallback = 0) => Has(p, c) ? Number(S(p, c)) : fallback;
    private static double Number(string text) => GeometryMath.Number(text, out var n) ? n : throw new FormatException($"Invalid DXF number: {text}");
    private static double Positive(ImmutableArray<DxfPair> p, int code) { var v = N(p, code); return v > 0 ? v : throw new FormatException("A radius/height/ratio must be positive."); }
    private static Vec3 P(ImmutableArray<DxfPair> p, int c, Vec3 fallback = default) => new(N(p, c, fallback.X), N(p, c + 10, fallback.Y), N(p, c + 20, fallback.Z));
    internal static string Encode(IEnumerable<DxfPair> pairs) => string.Concat(pairs.Select(p => $"{p.Code}\n{p.Value}\n"));
    private static OpaqueEntity Opaque(string type, ImmutableArray<DxfPair> record) => new(type, Encode(record));
}
