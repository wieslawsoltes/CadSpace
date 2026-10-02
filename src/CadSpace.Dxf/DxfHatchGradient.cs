using System.Collections.Immutable;
using System.Globalization;
using CadSpace.Model;

namespace CadSpace.Dxf;

internal static class DxfHatchGradient
{
    public static HatchGradient? Read(ImmutableArray<DxfPair> pairs)
    {
        var start = -1;
        for (var i = 0; i < pairs.Length; i++) if (pairs[i].Code == 450) { start = i; break; }
        if (start < 0) return null;
        var p = pairs.Skip(start).ToArray();
        string S(int c, string fallback = "") => p.FirstOrDefault(v => v.Code == c).Value ?? fallback;
        double N(int c) => double.Parse(S(c), CultureInfo.InvariantCulture);
        foreach (var code in new[] { 450,451,452,453,460,461,462,470 })
            if (p.Count(v => v.Code == code) != 1) throw new FormatException("Incomplete or duplicate HATCH gradient fields.");
        if (S(450) == "0") return null;
        if (S(450) != "1" || S(451) != "0" || S(453) != "2" || S(452) is not ("0" or "1"))
            throw new NotSupportedException("Only the native two-stop gradient representation is supported.");
        var colors = p.Where(v => v.Code == 421).Select(v => uint.Parse(v.Value, CultureInfo.InvariantCulture)).ToArray();
        var positions = p.Where(v => v.Code == 463).Select(v => double.Parse(v.Value, CultureInfo.InvariantCulture)).ToArray();
        if (colors.Length != 2 || colors.Any(c => c > 0xffffff) || positions.Length != 2 || positions[0] != 0 || positions[1] != 1)
            throw new FormatException("Gradient requires exactly two RGB colors with stops 0 and 1.");
        var g = new HatchGradient(S(470), colors[0] | 0xff000000, colors[1] | 0xff000000,
            N(460) * 180 / Math.PI, N(461), S(452) == "1", N(462));
        g.Validate(); return g;
    }
    public static void Write(HatchGradient gradient, Action<int, object> pair)
    {
        gradient.Validate();
        pair(450,1); pair(451,0); pair(460,gradient.Angle * Math.PI / 180); pair(461,gradient.Shift);
        pair(452,gradient.SingleColor ? 1 : 0); pair(462,gradient.Tint); pair(453,2);
        pair(463,0); pair(421,gradient.FirstColor & 0xffffff); pair(463,1); pair(421,gradient.SecondColor & 0xffffff); pair(470,gradient.Name);
    }
}
