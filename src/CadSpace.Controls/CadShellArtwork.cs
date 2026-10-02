using SkiaSharp;

namespace CadSpace.Controls;

/// <summary>Original vector artwork for workspace, file, navigation and editing controls.</summary>
internal static class CadShellArtwork
{
    public static bool Draw(string kind, SKCanvas canvas, SKPaint p)
    {
        void L(float x, float y, float a, float b) => canvas.DrawLine(x, y, a, b, p);
        void R(float x, float y, float w, float h) => canvas.DrawRect(x, y, w, h, p);
        void Sheet() { L(7, 3, 21, 3); L(21, 3, 27, 9); L(27, 9, 27, 29); L(27, 29, 7, 29); L(7, 29, 7, 3); L(21, 3, 21, 9); L(21, 9, 27, 9); }
        void Arrow(float x, float y) { L(x - 5, y, x + 5, y); L(x + 5, y, x, y - 4); L(x + 5, y, x, y + 4); }
        switch (kind)
        {
            case "GRADIENT":
                for (var i = 0; i < 7; i++) { p.Color = new SKColor((byte)(45+i*26), (byte)(100+i*12), (byte)(210-i*22)); L(5+i*3.5f,5,5+i*3.5f,27); } break;
            case "HATCHEDIT": R(3,4,22,24); L(4,14,14,4); L(4,23,23,4); L(10,28,26,12); break;
            case "HATCHGENERATEBOUNDARY": R(4,4,24,24); R(12,12,8,8); break;
            case "DIMLINEAR": case "DIMROTATED": case "DIMORDINATE":
                L(4, 3, 4, 27); L(28, 3, 28, 27); L(4, 16, 28, 16); L(4, 16, 10, 12); L(4, 16, 10, 20); L(28, 16, 22, 12); L(28, 16, 22, 20); break;
            case "DIMRADIUS": case "DIMDIAMETER":
                canvas.DrawCircle(16, 16, 11, p); L(kind == "DIMRADIUS" ? 16 : 5, 16, 27, 16); L(27, 16, 22, 12); L(27, 16, 22, 20); break;
            case "DIMANGULAR": case "DIMANGULAR2":
                L(4, 28, 28, 28); L(4, 28, 4, 4); using (var path = new SKPath()) { path.AddArc(new SKRect(-16, 8, 24, 48), 270, 90); canvas.DrawPath(path, p); } break;
            case "DIMEDIT": R(3, 4, 22, 24); L(7, 10, 21, 10); L(7, 16, 17, 16); L(15, 29, 30, 14); break;
            case "LEADER": case "LEADEREDIT": L(3,27,16,8); L(16,8,29,8); L(3,27,4,20); L(3,27,10,24); break;
            case "MTEXT": L(4, 4, 18, 4); L(11, 4, 11, 26); L(20, 9, 29, 9); L(20, 16, 29, 16); L(20, 23, 29, 23); break;
            case "DDEDIT": case "EATTEDIT": R(3, 4, 19, 21); L(7, 9, 18, 9); L(7, 14, 16, 14); L(15, 28, 29, 14); L(15, 28, 19, 27); break;
            case "POLYGON":
                using (var path = new SKPath()) { for (var i = 0; i < 6; i++) { var a = MathF.PI * i / 3; var x = 16 + 12 * MathF.Cos(a); var y = 16 + 12 * MathF.Sin(a); if (i == 0) path.MoveTo(x, y); else path.LineTo(x, y); } path.Close(); canvas.DrawPath(path, p); } break;
            case "DONUT": canvas.DrawCircle(16, 16, 12, p); canvas.DrawCircle(16, 16, 6, p); break;
            case "MATCHPROP": R(3, 3, 12, 10); L(9, 13, 9, 20); L(9, 20, 20, 20); Arrow(23, 20); break;
            case "NEW": Sheet(); L(12, 18, 22, 18); L(17, 13, 17, 23); break;
            case "OPEN": p.Color = new SKColor(232, 198, 110); L(3, 10, 12, 10); L(12, 10, 15, 13); L(15, 13, 29, 13); L(29, 13, 25, 27); L(25, 27, 3, 27); L(3, 27, 3, 7); L(3, 7, 12, 7); break;
            case "SAVE": R(5, 3, 23, 26); R(10, 3, 12, 9); R(10, 19, 12, 10); break;
            case "UNDO": case "REDO": using (var path = new SKPath()) { var flip = kind == "REDO"; canvas.Save(); if (flip) { canvas.Translate(32, 0); canvas.Scale(-1, 1); } path.MoveTo(5, 11); path.CubicTo(27, 6, 31, 16, 22, 26); canvas.DrawPath(path, p); L(5, 11, 11, 5); L(5, 11, 12, 16); canvas.Restore(); } break;
            case "EXPORT": case "EXPORT_BINARY": Sheet(); p.Color = new SKColor(229, 194, 114); Arrow(19, 19); break;
            case "RECOVER": Sheet(); canvas.DrawCircle(12, 21, 7, p); L(12, 16, 12, 21); L(12, 21, 16, 23); break;
            case "HELP": canvas.DrawCircle(16, 16, 12, p); L(16, 22, 16, 24); using (var path = new SKPath()) { path.MoveTo(11, 11); path.CubicTo(12, 4, 25, 7, 19, 14); path.LineTo(16, 17); canvas.DrawPath(path, p); } break;
            case "OPTIONS": canvas.DrawCircle(16, 16, 8, p); canvas.DrawCircle(16, 16, 3, p); for (var i = 0; i < 8; i++) { var a = MathF.PI * i / 4; L(16 + 9 * MathF.Cos(a), 16 + 9 * MathF.Sin(a), 16 + 13 * MathF.Cos(a), 16 + 13 * MathF.Sin(a)); } break;
            case "PROPERTIES": case "PROPERTIESCLOSE": case "UISTATS": R(3, 4, 26, 24); L(3, 11, 29, 11); L(12, 11, 12, 28); L(3, 18, 29, 18); L(3, 23, 29, 23); break;
            case "TOOLPALETTES": case "TOOLPALETTESCLOSE": case "ARRAY": for (var x = 4; x < 29; x += 9) for (var y = 4; y < 29; y += 9) R(x, y, 6, 6); break;
            case "RIBBON": case "RIBBONCLOSE": R(2, 5, 28, 23); L(2, 11, 30, 11); for (var x = 6; x < 28; x += 8) R(x, 15, 5, 9); break;
            case "CLEANSCREENON": case "CLEANSCREENOFF": case "FIT": L(3, 12, 3, 3); L(3, 3, 12, 3); L(20, 3, 29, 3); L(29, 3, 29, 12); L(29, 20, 29, 29); L(29, 29, 20, 29); L(12, 29, 3, 29); L(3, 29, 3, 20); break;
            case "LAYOUT_NEW": case "LAYOUT_RENAME": case "LAYOUT_DELETE": Sheet(); if (kind == "LAYOUT_NEW") { L(11, 17, 23, 17); L(17, 11, 17, 23); } else if (kind == "LAYOUT_DELETE") { L(12, 12, 22, 23); L(12, 23, 22, 12); } else L(10, 23, 23, 12); break;
            case "LAYER": case "LINETYPE": using (var path = new SKPath()) { path.MoveTo(3, 10); path.LineTo(16, 3); path.LineTo(29, 10); path.LineTo(16, 17); path.Close(); canvas.DrawPath(path, p); L(3, 17, 16, 24); L(16, 24, 29, 17); L(3, 23, 16, 30); L(16, 30, 29, 23); } break;
            case "LIGHT": canvas.DrawCircle(16, 11, 7, p); R(12, 21, 8, 6); L(12, 18, 12, 21); L(20, 18, 20, 21); break;
            case "LOCK": R(7, 14, 18, 14); using (var path = new SKPath()) { path.AddArc(new SKRect(10, 3, 22, 23), 180, 180); canvas.DrawPath(path, p); } L(16, 19, 16, 23); break;
            case "GRID": for (var n = 5; n < 30; n += 7) { L(5, n, 26, n); L(n, 5, n, 26); } break;
            case "SNAP": for (var x = 5; x < 30; x += 8) for (var y = 5; y < 30; y += 8) canvas.DrawCircle(x, y, 1, p); break;
            case "ORTHO": L(6, 4, 6, 26); L(6, 26, 28, 26); R(6, 17, 9, 9); break;
            case "POLAR": canvas.DrawCircle(15, 16, 11, p); L(15, 16, 29, 4); L(15, 16, 27, 16); break;
            case "OSNAP": R(6, 6, 20, 20); L(1, 16, 11, 16); L(21, 16, 31, 16); L(16, 1, 16, 11); L(16, 21, 16, 31); break;
            case "DYN": R(3, 7, 26, 19); L(8, 13, 13, 17); L(13, 17, 8, 21); L(17, 21, 24, 21); break;
            case "SC": case "SELECTALL": R(3, 3, 20, 20); R(12, 12, 17, 17); break;
            case "PAN": using (var path = new SKPath()) { path.MoveTo(7, 27); path.LineTo(3, 15); path.LineTo(7, 14); path.LineTo(10, 20); path.LineTo(10, 6); path.LineTo(14, 6); path.LineTo(14, 17); path.LineTo(17, 3); path.LineTo(20, 4); path.LineTo(20, 17); path.LineTo(24, 8); path.LineTo(27, 10); path.LineTo(26, 24); path.LineTo(23, 29); path.Close(); canvas.DrawPath(path, p); } break;
            case "ZOOMOUT": canvas.DrawCircle(13, 13, 8, p); L(19, 19, 28, 28); L(8, 13, 18, 13); break;
            case "HOME": L(3, 15, 16, 3); L(16, 3, 29, 15); L(7, 12, 7, 29); L(7, 29, 25, 29); L(25, 29, 25, 12); R(13, 20, 6, 9); break;
            case "PERSPECTIVE": L(4, 6, 28, 11); L(28, 11, 28, 23); L(28, 23, 4, 28); L(4, 28, 4, 6); L(16, 9, 16, 26); break;
            case "CLIP3D": R(6, 7, 20, 20); p.Color = new SKColor(227, 189, 109); L(2, 23, 30, 9); break;
            case "ELLIPSE": canvas.DrawOval(new SKRect(3, 8, 29, 24), p); break;
            case "SPLINEDIT": case "SPLINE": using (var path = new SKPath()) { path.MoveTo(2, 27); path.CubicTo(10, -3, 22, 37, 30, 4); canvas.DrawPath(path, p); } break;
            case "3DPOLY": case "PEDIT": case "PLINEWID": L(3, 27, 10, 7); L(10, 7, 21, 19); L(21, 19, 28, 4); R(7, 4, 6, 6); R(18, 16, 6, 6); break;
            case "TRIM": L(9, 2, 9, 29); L(23, 2, 23, 29); L(2, 16, 9, 16); L(23, 16, 30, 16); break;
            case "EXTEND": L(26, 2, 26, 30); L(3, 17, 25, 17); Arrow(15, 17); break;
            case "FILLET": L(4, 3, 4, 15); L(16, 27, 29, 27); using (var path = new SKPath()) { path.MoveTo(4, 15); path.QuadTo(4, 27, 16, 27); canvas.DrawPath(path, p); } break;
            case "CHAMFER": L(4, 3, 4, 16); L(4, 16, 15, 27); L(15, 27, 29, 27); break;
            case "JOIN": L(2, 24, 16, 16); L(16, 16, 29, 6); canvas.DrawCircle(16, 16, 3, p); break;
            case "BREAK": L(3, 26, 12, 17); L(21, 8, 29, 1); L(9, 12, 17, 20); L(17, 4, 25, 12); break;
            case "EXPLODE": R(10, 10, 12, 12); L(5, 5, 10, 10); L(22, 10, 29, 3); L(22, 22, 29, 29); L(10, 22, 3, 29); break;
            case "UNION": case "SUBTRACT": case "INTERSECT": R(3, 5, 16, 17); p.Color = new SKColor(233, 193, 109); R(13, 13, 16, 17); break;
            case "ROTATE3D": case "MIRROR3D": case "ALIGN3D": L(16, 16, 3, 27); L(16, 16, 28, 27); L(16, 16, 16, 2); Arrow(19, 10); break;
            case "LOFT": case "SWEEP": case "REVOLVE": canvas.DrawOval(new SKRect(3, 3, 21, 11), p); canvas.DrawOval(new SKRect(10, 22, 29, 30), p); L(3, 7, 10, 26); L(21, 7, 29, 26); break;
            case "AREA": R(4, 4, 24, 24); L(4, 4, 28, 28); L(4, 28, 28, 4); break;
            default: return false;
        }
        return true;
    }
}
