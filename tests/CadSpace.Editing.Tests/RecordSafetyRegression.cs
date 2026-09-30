using System.Collections.Immutable;
using System.Text;
using CadSpace.Dxf;
using CadSpace.Engine;
using CadSpace.Geometry;
using CadSpace.Model;

internal static class RecordSafetyRegression
{
    public static void Register(Action<string, Action> test)
    {
        static void Check(bool value) { if (!value) throw new Exception("Record-safety assertion failed."); }
        static string File(string entity) => "0\nSECTION\n2\nENTITIES\n" + entity + "0\nENDSEC\n0\nEOF\n";
        test("opaque records retain APPDATA and XDATA without source provenance", () => {
            const string raw = "0\nACAD_PROXY_ENTITY\n5\nAB\n102\n{CUSTOM\n1\napplication payload\n102\n}\n1001\nCUSTOM\n1000\nextended payload\n";
            var read = DxfCodec.Read(File(raw));
            Check(((OpaqueEntity)read.Drawing.Entities.Single()).RawRecord == raw);
            var written = DxfCodec.Write(read.Drawing).Text;
            Check(written.Contains("application payload") && written.Contains("extended payload"));
            var native = CadProjectCodec.Read(CadProjectCodec.Write(read.Drawing, read.Source));
            Check(((OpaqueEntity)native.Drawing.Entities.Single()).RawRecord == raw);
        });
        test("edited large-header binary exports put ACADVER first", () => {
            var read = DxfBinary.Read(System.IO.File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "fixtures", "independent-editing.dxf")));
            var drawing = read.Drawing with { Entities = read.Drawing.Entities.Add(new PointEntity(new(100, 100))) };
            var bytes = DxfBinary.Write(drawing, read.Source, binary: true).Bytes;
            var first = Encoding.Latin1.GetString(bytes, 0, Math.Min(bytes.Length, 256));
            Check(first.IndexOf("$ACADVER", StringComparison.Ordinal) is >= 0 and < 64);
            var header = DxfBinary.ReadPairs(bytes);
            Check(header[2] == new DxfPair(9, "$ACADVER") && header[3] == new DxfPair(1, "AC1027"));
        });
        test("binary extended coordinate scalar types remain double precision", () => {
            DxfPair[] pairs = [new(0,"SECTION"),new(2,"HEADER"),new(9,"$ACADVER"),new(1,"AC1027"),new(0,"ENDSEC"),
                new(0,"SECTION"),new(2,"ENTITIES"),new(0,"POINT"),new(10,"1"),new(20,"2"),new(30,"3"),new(1001,"CUSTOM"),
                new(1010,"9.25"),new(1020,"-8.5"),new(1030,"7.125"),new(1070,"12"),new(0,"ENDSEC"),new(0,"EOF")];
            var output = DxfBinary.ReadPairs(DxfBinary.Encode(pairs));
            Check(output.SequenceEqual(pairs));
        });
        test("same-count polyline vertex edits retain vertex IDs and XDATA", () => {
            const string raw = "0\nLWPOLYLINE\n5\nAB\n100\nAcDbEntity\n8\n0\n100\nAcDbPolyline\n90\n2\n70\n0\n10\n0\n20\n0\n91\n301\n10\n100\n20\n0\n91\n302\n1001\nCUSTOM\n1000\nnote\n";
            var read = DxfCodec.Read(File(raw)); var poly = (PolylineEntity)read.Drawing.Entities.Single();
            var edited = poly with { Vertices = poly.Vertices.SetItem(1, poly.Vertices[1] with { Position = new(120, 5) }) };
            var result = DxfCodec.Write(read.Drawing with { Entities = [edited] }, read.Source);
            var reparsed = DxfCodec.Read(result.Text); var next = (PolylineEntity)reparsed.Drawing.Entities.Single();
            Check(next.Vertices[1].Position == new Vec3(120,5));
            Check(result.Text.Contains("91\n301\n") && result.Text.Contains("91\n302\n") && result.Text.Contains("1000\nnote\n"));
        });
        test("reordered polyline vertices report metadata fallback instead of guessing IDs", () => {
            const string raw = "0\nLWPOLYLINE\n5\nAB\n90\n2\n10\n0\n20\n0\n91\n301\n10\n100\n20\n0\n91\n302\n";
            var read = DxfCodec.Read(File(raw)); var p = (PolylineEntity)read.Drawing.Entities.Single();
            var output = DxfCodec.Write(read.Drawing with { Entities = [PolylineEditing.Reverse(p)] }, read.Source);
            Check(output.Warnings.Any(w => w.Contains("metadata")) && !output.Text.Contains("91\n301\n"));
        });
        test("nested common-property changes are not ignored by source patching", () => {
            var raw = File("0\nCIRCLE\n5\nAB\n10\n0\n20\n0\n30\n0\n40\n10\n210\n0\n220\n1\n230\n0\n");
            var read = DxfCodec.Read(raw); var p = (PlacedEntity)read.Drawing.Entities.Single();
            var edit = p with { Geometry = ((CircleEntity)p.Geometry) with { Radius = 15, ColorIndex = 1 } };
            var output = DxfCodec.Write(read.Drawing with { Entities = [edit] }, read.Source);
            Check(output.Warnings.Any(w => w.Contains("metadata")));
        });
    }
}
