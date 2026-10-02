using System.Text.Json;
using CadSpace.Model;

namespace CadSpace.Dxf;

public static partial class CadProjectCodec
{
    private static bool HasGradients(Drawing drawing)
    {
        static bool Has(Entity e) => e is HatchRegionEntity { Gradient: not null } ||
            e is PlacedEntity p && Has(p.Geometry) || e is CompositeEntity c && c.Children.Any(Has);
        return drawing.Entities.Any(Has) || drawing.Blocks.Values.Any(b => b.Entities.Any(Has));
    }
    private static void WriteGradient(Utf8JsonWriter w, HatchGradient? g)
    {
        if (g == null) return;
        w.WritePropertyName("gradient"); w.WriteStartObject(); w.WriteString("name",g.Name);
        w.WriteNumber("first",g.FirstColor); w.WriteNumber("second",g.SecondColor); w.WriteNumber("angle",g.Angle);
        w.WriteNumber("shift",g.Shift); w.WriteBoolean("single",g.SingleColor); w.WriteNumber("tint",g.Tint); w.WriteEndObject();
    }
    private static HatchGradient? ReadGradient(JsonElement entity)
    {
        if (!entity.TryGetProperty("gradient", out var g)) return null;
        var value = new HatchGradient(S(g,"name"), g.GetProperty("first").GetUInt32(), g.GetProperty("second").GetUInt32(),
            N(g,"angle"), N(g,"shift"), g.GetProperty("single").GetBoolean(), N(g,"tint"));
        value.Validate(); return value;
    }
}
