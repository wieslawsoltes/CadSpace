using System.Collections.Immutable;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using CadSpace.Geometry;

namespace CadSpace.Model;

/// <summary>The seven standard DIMENSION subtypes, excluding constraints and private extensions.</summary>
public enum DimensionKind { Rotated, Aligned, Angular2Line, Diameter, Radius, Angular3Point, Ordinate }

/// <summary>Portable decimal dimension formatting, in drawing units before applying Scale.</summary>
public sealed record DimensionFormat
{
    public double TextHeight { get; init; } = 10;
    public double ArrowSize { get; init; } = 7;
    public double Gap { get; init; } = 4;
    public double ExtensionOffset { get; init; }
    public double ExtensionBeyond { get; init; } = 4;
    public double Scale { get; init; } = 1;
    public double MeasurementScale { get; init; } = 1;
    public int Precision { get; init; } = 2;
    public int AngularPrecision { get; init; } = 2;
    public bool SuppressTrailingZeros { get; init; } = true;
    public string TextTemplate { get; init; } = "<>";
    public static DimensionFormat Default { get; } = new();
    public void Validate()
    {
        if (!double.IsFinite(Scale) || Scale <= 0 || !double.IsFinite(MeasurementScale) || MeasurementScale <= 0 || MeasurementScale > 1e12 ||
            TextHeight <= 0 || TextHeight * Scale <= 0 || Precision is < 0 or > 8 || AngularPrecision is < 0 or > 8)
            throw new ArgumentException("Invalid dimension size, scale or precision.");
        foreach (var value in new[] { TextHeight, ArrowSize, Gap, ExtensionOffset, ExtensionBeyond })
            if (!double.IsFinite(value) || value < 0 || !double.IsFinite(value * Scale) || value * Scale > 1e12)
                throw new ArgumentException("Dimension sizes must be finite, nonnegative and within 1e12 scaled units.");
        ValidateText(TextTemplate);
    }
    internal static void ValidateText(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.Length > 2048 || value.IndexOfAny(['\0', '\r', '\n']) >= 0)
            throw new ArgumentException("Dimension labels must be one line of at most 2,048 characters without NUL.");
        for (var i = 0; i < value.Length; i++)
            if (char.IsSurrogate(value[i]) && (!char.IsHighSurrogate(value[i]) || ++i == value.Length || !char.IsLowSurrogate(value[i])))
                throw new ArgumentException("Dimension labels must contain valid Unicode.");
    }
}

/// <summary>Original anonymous picture, used only while its definition signature still matches.</summary>
public sealed record DimensionPicture(string BlockName, string Signature, Vec3 Insertion);
public sealed record DimensionLayout(ImmutableArray<Entity> Entities, double Measurement, Vec3 TextCenter, string Label);

/// <summary>Shared bounded dimension geometry for drafting, GPU rendering, picking and native DXF pictures.</summary>
public static class DimensionGeometry
{
    private sealed class Cached(DimensionEntity dimension)
    {
        public Lazy<string> Signature { get; } = new(() => ComputeSignature(dimension));
        public Lazy<DimensionLayout> Layout { get; } = new(() => Build(dimension));
    }
    private static readonly ConditionalWeakTable<DimensionEntity, Cached> Cache = new();
    public static DimensionLayout For(DimensionEntity dimension) => Cache.GetValue(dimension, static d => new(d)).Layout.Value;
    public static string Signature(DimensionEntity dimension) => Cache.GetValue(dimension, static d => new(d)).Signature.Value;
    public static bool UsesPicture(DimensionEntity d) => d.Picture is { } p && p.Signature == Signature(d);
    public static DimensionEntity? Unwrap(Entity root)
    {
        for (var i = 0; root is PlacedEntity p; i++) { if (i >= 32) return null; root = p.Geometry; }
        return root as DimensionEntity;
    }
    public static IEnumerable<Vec3> Points(DimensionEntity d)
    {
        yield return d.First; yield return d.Second; yield return d.Location;
        if (d.Type is DimensionKind.Angular2Line or DimensionKind.Angular3Point) yield return d.Third;
        if (d.Type == DimensionKind.Angular2Line) yield return d.Fourth;
        if (d.TextPosition is { } p) yield return p;
    }
    public static void Validate(DimensionEntity d)
    {
        if (!Enum.IsDefined(d.Type) || !double.IsFinite(d.Rotation)) throw new ArgumentException("Invalid dimension type or angle.");
        ArgumentNullException.ThrowIfNull(d.Format); d.Format.Validate(); DimensionFormat.ValidateText(d.TextOverride);
        if (Points(d).Any(p => !p.IsFinite || Math.Abs(p.Z - d.First.Z) > 1e-7))
            throw new ArgumentException("Dimension points must be finite and share a local XY plane.");
        if (d.Picture is { } pic && (string.IsNullOrWhiteSpace(pic.BlockName) || pic.BlockName.IndexOfAny(['\0','\r','\n']) >= 0 ||
            !pic.Insertion.IsFinite || pic.Signature.Length != 64 || pic.Signature.Any(c => !Uri.IsHexDigit(c))))
            throw new ArgumentException("Invalid retained dimension picture.");
        if (d.Type != DimensionKind.Ordinate && d.First.DistanceTo(d.Second) < 1e-9) throw new ArgumentException("Dimension definition points must differ.");
        var measurement = Measure(d);
        if (!double.IsFinite(measurement) || !double.IsFinite(measurement * d.Format.MeasurementScale)) throw new ArgumentException("Dimension measurement is outside the numeric range.");
    }
    public static double Measure(DimensionEntity d) => d.Type switch
    {
        DimensionKind.Rotated => Math.Abs((d.Second - d.First).Dot(GeometryMath.OnCircle(default, 1, d.Rotation))),
        DimensionKind.Ordinate => Math.Abs((d.Second - d.First).Dot(GeometryMath.OnCircle(default, 1, d.Rotation + (d.OrdinateX ? 0 : 90)))),
        DimensionKind.Angular2Line or DimensionKind.Angular3Point => Angles(d).Sweep,
        _ => d.First.DistanceTo(d.Second)
    };
    private static (Vec3 Center, double Start, double Sweep) Angles(DimensionEntity d)
    {
        Vec3 center; double start, end;
        if (d.Type == DimensionKind.Angular2Line)
        {
            if (!GeometryMath.IntersectLinesXY(d.First, d.Second, d.Third, d.Fourth, out center, false))
                throw new ArgumentException("Angular dimension lines must be nonparallel and nondegenerate.");
            var a = GeometryMath.Angle(d.Second - d.First); var b = GeometryMath.Angle(d.Fourth - d.Third);
            var rays = new[] { a, a + 180, b, b + 180 }.Select(GeometryMath.NormalizeAngle).Order().ToArray();
            var location = GeometryMath.NormalizeAngle(GeometryMath.Angle(d.Location - center));
            var i = Array.FindLastIndex(rays, r => r <= location); if (i < 0) i = 3;
            start = rays[i]; end = rays[(i + 1) % 4];
        }
        else
        {
            center = d.Third;
            if (center.DistanceTo(d.First) < 1e-9 || center.DistanceTo(d.Second) < 1e-9) throw new ArgumentException("Angular ray points must differ from their vertex.");
            start = GeometryMath.Angle(d.First - center); end = GeometryMath.Angle(d.Second - center);
            if (GeometryMath.NormalizeAngle(GeometryMath.Angle(d.Location - center) - start) > GeometryMath.NormalizeAngle(end - start)) (start, end) = (end, start);
        }
        var sweep = GeometryMath.NormalizeAngle(end - start);
        if (sweep < 1e-8 || center.DistanceTo(d.Location) < 1e-9) throw new ArgumentException("Angular dimensions require a nonzero angle and arc radius.");
        return (center, start, sweep);
    }
    public static string Label(DimensionEntity d)
    {
        var angular = d.Type is DimensionKind.Angular2Line or DimensionKind.Angular3Point;
        var n = Measure(d) * (angular ? 1 : d.Format.MeasurementScale);
        var precision = angular ? d.Format.AngularPrecision : d.Format.Precision;
        var format = "0" + (precision == 0 ? "" : "." + new string(d.Format.SuppressTrailingZeros ? '#' : '0', precision));
        var number = n.ToString(format, CultureInfo.InvariantCulture);
        var measure = (d.Type == DimensionKind.Radius ? "R" : d.Type == DimensionKind.Diameter ? "Ø" : "") + number + (angular ? "°" : "");
        var text = d.TextOverride is "" or "<>" ? d.Format.TextTemplate : d.TextOverride;
        return text == " " ? "" : text.Replace("<>", measure, StringComparison.Ordinal);
    }
    private static DimensionLayout Build(DimensionEntity d)
    {
        Validate(d); var result = ImmutableArray.CreateBuilder<Entity>();
        var f = d.Format; var arrow = f.ArrowSize * f.Scale; var height = f.TextHeight * f.Scale; var gap = f.Gap * f.Scale;
        var label = Label(d); var textCenter = d.Location; var rotation = 0.0;
        void Line(Vec3 a, Vec3 b) { if (a.DistanceTo(b) > 1e-10) result.Add(new LineEntity(a, b)); }
        void Arrow(Vec3 point, Vec3 into)
        {
            if (arrow == 0 || into.Length < 1e-12) return;
            var axis = into.Normalized; var normal = new Vec3(-axis.Y, axis.X);
            result.Add(new MeshEntity([point, point + axis * arrow + normal * (arrow * .28), point + axis * arrow - normal * (arrow * .28)], [0,1,2], "Dimension arrow"));
        }
        void Extension(Vec3 from, Vec3 to)
        {
            var delta = to - from; if (delta.Length < 1e-10) return;
            var axis = delta.Normalized;
            Line(from + axis * (f.ExtensionOffset * f.Scale), to + axis * (f.ExtensionBeyond * f.Scale));
        }
        switch (d.Type)
        {
            case DimensionKind.Aligned: case DimensionKind.Rotated:
                var axis = d.Type == DimensionKind.Aligned ? (d.Second - d.First).Normalized : GeometryMath.OnCircle(default, 1, d.Rotation);
                var normal = new Vec3(-axis.Y, axis.X); var offset = (d.Location - d.First).Dot(normal);
                var a = d.First + normal * offset; var b = d.Second + normal * (d.Location - d.Second).Dot(normal);
                Extension(d.First, a); Extension(d.Second, b); Line(a,b);
                var sign = (b-a).Dot(axis) >= 0 ? 1 : -1;
                Arrow(a, axis * sign); Arrow(b, -axis * sign);
                textCenter = (a+b)/2 + normal * (gap + height / 2); rotation = GeometryMath.Angle(axis); break;
            case DimensionKind.Radius: case DimensionKind.Diameter:
                Line(d.First, d.Second); Arrow(d.Second, d.First-d.Second);
                if (d.Type == DimensionKind.Diameter) Arrow(d.First,d.Second-d.First);
                var nearest = d.Location.DistanceTo(d.Second) <= d.Location.DistanceTo(d.First) ? d.Second : d.First;
                Line(nearest,d.Location); textCenter = d.Location; break;
            case DimensionKind.Ordinate:
                var x = GeometryMath.OnCircle(default,1,d.Rotation); var y = new Vec3(-x.Y,x.X); var lead = d.OrdinateX ? y : x;
                var turn = d.Second + lead * (d.Location-d.Second).Dot(lead);
                Line(d.Second,turn); Line(turn,d.Location); textCenter = d.Location + y * (gap+height/2); break;
            case DimensionKind.Angular2Line: case DimensionKind.Angular3Point:
                var angle = Angles(d); var radius = angle.Center.DistanceTo(d.Location);
                var curve = EntityGeometry.Curve(angle.Center,radius,angle.Start,angle.Sweep);
                result.Add(PolylineEntity.FromPoints(curve));
                var p1 = curve[0]; var p2 = curve[^1];
                if (d.Type == DimensionKind.Angular3Point) { Extension(d.First,p1.DistanceTo(d.First) < p2.DistanceTo(d.First) ? p1 : p2); Extension(d.Second,p1.DistanceTo(d.Second) < p2.DistanceTo(d.Second) ? p1 : p2); }
                var r1 = (p1-angle.Center).Normalized; var r2 = (p2-angle.Center).Normalized;
                Arrow(p1,new(-r1.Y,r1.X)); Arrow(p2,new(r2.Y,-r2.X));
                textCenter = GeometryMath.OnCircle(angle.Center,radius+gap+height/2,angle.Start+angle.Sweep/2); break;
        }
        textCenter = d.TextPosition ?? textCenter;
        if (label.Length > 0)
        {
            // Approximate centering; exact font-dependent fitting is outside the portable geometry layer.
            rotation = GeometryMath.NormalizeAngle(rotation);
            if (rotation > 90 && rotation <= 270) rotation = GeometryMath.NormalizeAngle(rotation+180);
            var axis = GeometryMath.OnCircle(default,1,rotation); var up = new Vec3(-axis.Y,axis.X);
            result.Add(new TextEntity(textCenter-axis*(height*label.Length*.3)-up*(height*.5),label,height,rotation));
        }
        return new(result.ToImmutable(),Measure(d),textCenter,label);
    }
    /// <summary>Apply an orientation-preserving XY similarity in the dimension's local plane.</summary>
    public static DimensionEntity TransformPlanar(DimensionEntity d, Transform3 t)
    {
        if (t == Transform3.Identity) return d;
        var scale = t.X.Length;
        if (scale < 1e-12 || Math.Abs(t.Y.Length-scale)>scale*1e-8 || Math.Abs(t.X.Dot(t.Y))>scale*scale*1e-8 ||
            Math.Abs(t.X.Z)+Math.Abs(t.Y.Z)>scale*1e-8 || GeometryMath.Cross2(t.X,t.Y)<=0)
            throw new NotSupportedException("Native dimensions require an orientation-preserving planar similarity; retain general affine placements in a native project.");
        return d with { First=t.Point(d.First), Second=t.Point(d.Second), Location=t.Point(d.Location), Third=d.Type is DimensionKind.Angular2Line or DimensionKind.Angular3Point ? t.Point(d.Third) : d.Third, Fourth=d.Type == DimensionKind.Angular2Line ? t.Point(d.Fourth) : d.Fourth,
            Rotation=GeometryMath.Angle(t.Vector(GeometryMath.OnCircle(default,1,d.Rotation))),
            TextPosition=d.TextPosition is { } p ? t.Point(p) : null, Format=d.Format with { Scale=d.Format.Scale*scale }, Picture=null };
    }
    private static string ComputeSignature(DimensionEntity d)
    {
        using var data=new MemoryStream(); using var w=new BinaryWriter(data,Encoding.UTF8,true);
        void Point(Vec3 p) { w.Write(p.X);w.Write(p.Y);w.Write(p.Z); }
        w.Write((int)d.Type); Point(d.First);Point(d.Second);Point(d.Location);Point(d.Third);Point(d.Fourth);
        w.Write(d.Rotation);w.Write(d.OrdinateX);w.Write(d.TextOverride);w.Write(d.TextPosition.HasValue);if(d.TextPosition is { } p)Point(p);
        var f=d.Format;w.Write(f.TextHeight);w.Write(f.ArrowSize);w.Write(f.Gap);w.Write(f.ExtensionOffset);w.Write(f.ExtensionBeyond);
        w.Write(f.Scale);w.Write(f.MeasurementScale);w.Write(f.Precision);w.Write(f.AngularPrecision);w.Write(f.SuppressTrailingZeros);w.Write(f.TextTemplate);
        w.Flush();return Convert.ToHexString(SHA256.HashData(data.GetBuffer().AsSpan(0,(int)data.Length)));
    }
}
