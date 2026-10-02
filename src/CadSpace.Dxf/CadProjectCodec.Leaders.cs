using System.Text.Json;
using CadSpace.Model;

namespace CadSpace.Dxf;

public static partial class CadProjectCodec
{
    private static void WriteLeader(Utf8JsonWriter w, LeaderEntity e)
    {
        Points(w,"vertices",e.Vertices); w.WriteBoolean("arrow",e.ArrowEnabled); w.WriteNumber("arrowSize",e.ArrowSize);
        Point(w,"normal",e.Normal); Point(w,"horizontal",e.Horizontal); w.WriteBoolean("hookline",e.Hookline); w.WriteBoolean("reversed",e.HooklineReversed);
        w.WriteBoolean("textAbove",e.TextAbove); w.WriteNumber("textHeight",e.TextHeight); w.WriteNumber("textWidth",e.TextWidth); w.WriteNumber("gap",e.Gap);
        w.WriteNumber("annotationType",e.AnnotationType); w.WriteString("annotationHandle",e.AnnotationHandle);
        Point(w,"blockOffset",e.BlockOffset); Point(w,"annotationOffset",e.AnnotationOffset); w.WriteNumber("blockColor",e.BlockColor); w.WriteString("dimensionStyle",e.DimensionStyle);
    }
    private static LeaderEntity ReadLeader(JsonElement e) => new(Points(e.GetProperty("vertices"))) {
        ArrowEnabled=e.GetProperty("arrow").GetBoolean(), ArrowSize=N(e,"arrowSize"), Normal=P(e,"normal"), Horizontal=P(e,"horizontal"),
        Hookline=e.GetProperty("hookline").GetBoolean(), HooklineReversed=e.GetProperty("reversed").GetBoolean(), TextAbove=e.GetProperty("textAbove").GetBoolean(),
        TextHeight=N(e,"textHeight"), TextWidth=N(e,"textWidth"), Gap=N(e,"gap"), AnnotationType=e.GetProperty("annotationType").GetInt32(), AnnotationHandle=S(e,"annotationHandle"),
        BlockOffset=P(e,"blockOffset"), AnnotationOffset=P(e,"annotationOffset"), BlockColor=e.GetProperty("blockColor").GetInt32(), DimensionStyle=S(e,"dimensionStyle") };
}
