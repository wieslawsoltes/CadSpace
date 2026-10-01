using System.Text.Json;
using CadSpace.Model;

namespace CadSpace.Dxf;

public static partial class CadProjectCodec
{
    private static bool HasNativeDimensions(Drawing drawing)
    {
        static bool Contains(Entity e) => e is DimensionEntity || e is PlacedEntity p && Contains(p.Geometry) || e is CompositeEntity c && c.Children.Any(Contains);
        return drawing.Entities.Any(Contains) || drawing.Blocks.Values.Any(b => b.Entities.Any(Contains));
    }
    private static void WriteDimension(Utf8JsonWriter w, DimensionEntity e)
    {
        Point(w,"a",e.First);Point(w,"b",e.Second);Point(w,"location",e.Location);
        w.WriteNumber("dimensionType",(int)e.Type);Point(w,"third",e.Third);Point(w,"fourth",e.Fourth);
        w.WriteNumber("rotation",e.Rotation);w.WriteBoolean("ordinateX",e.OrdinateX);w.WriteString("textOverride",e.TextOverride);
        if(e.TextPosition is { } p)Point(w,"textPosition",p);
        var f=e.Format;w.WritePropertyName("dimensionFormat");w.WriteStartObject();
        w.WriteNumber("textHeight",f.TextHeight);w.WriteNumber("arrowSize",f.ArrowSize);w.WriteNumber("gap",f.Gap);
        w.WriteNumber("extensionOffset",f.ExtensionOffset);w.WriteNumber("extensionBeyond",f.ExtensionBeyond);
        w.WriteNumber("scale",f.Scale);w.WriteNumber("measurementScale",f.MeasurementScale);
        w.WriteNumber("precision",f.Precision);w.WriteNumber("angularPrecision",f.AngularPrecision);
        w.WriteBoolean("suppressTrailingZeros",f.SuppressTrailingZeros);w.WriteString("textTemplate",f.TextTemplate);w.WriteEndObject();
        if(e.Picture is { } picture)
        {
            w.WritePropertyName("dimensionPicture");w.WriteStartObject();w.WriteString("block",picture.BlockName);
            w.WriteString("signature",picture.Signature);Point(w,"insertion",picture.Insertion);w.WriteEndObject();
        }
    }
    private static DimensionEntity ReadDimension(JsonElement e)
    {
        var d=new DimensionEntity(P(e,"a"),P(e,"b"),P(e,"location"));
        if(!e.TryGetProperty("dimensionType",out var type))return d; // Existing v1/v2 aligned dimensions.
        var f=e.GetProperty("dimensionFormat");
        d=d with { Type=(DimensionKind)type.GetInt32(),Third=P(e,"third"),Fourth=P(e,"fourth"),Rotation=N(e,"rotation"),
            OrdinateX=e.GetProperty("ordinateX").GetBoolean(),TextOverride=S(e,"textOverride"),
            TextPosition=e.TryGetProperty("textPosition",out var text)?P(text):null,
            Format=new DimensionFormat {TextHeight=N(f,"textHeight"),ArrowSize=N(f,"arrowSize"),Gap=N(f,"gap"),ExtensionOffset=N(f,"extensionOffset"),
                ExtensionBeyond=N(f,"extensionBeyond"),Scale=N(f,"scale"),MeasurementScale=N(f,"measurementScale"),
                Precision=f.GetProperty("precision").GetInt32(),AngularPrecision=f.GetProperty("angularPrecision").GetInt32(),
                SuppressTrailingZeros=f.GetProperty("suppressTrailingZeros").GetBoolean(),TextTemplate=S(f,"textTemplate")}};
        if(e.TryGetProperty("dimensionPicture",out var pic))d=d with {Picture=new(S(pic,"block"),S(pic,"signature"),P(pic,"insertion"))};
        return d;
    }
}
