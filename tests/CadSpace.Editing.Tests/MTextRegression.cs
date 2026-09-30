using System.Collections.Immutable;
using System.Text;
using CadSpace.Dxf;
using CadSpace.Engine;
using CadSpace.Geometry;
using CadSpace.Model;

internal static class MTextRegression
{
    public static void Register(Action<string, Action> test)
    {
        static void Check(bool value) { if (!value) throw new Exception("MTEXT assertion failed."); }
        const string raw = "0\nSECTION\n2\nENTITIES\n0\nMTEXT\n5\nAB\n100\nAcDbEntity\n8\n0\n100\nAcDbMText\n10\n10\n20\n20\n30\n30\n40\n3\n41\n45\n71\n5\n1\nBefore\n11\n1\n21\n0\n31\n0\n210\n0\n220\n1\n230\n0\n90\n1\n63\n2\n1001\nCUSTOM\n1000\nretained\n0\nENDSEC\n0\nEOF\n";
        test("source MTEXT content and height edits retain metadata and orientation", () => {
            var read = DxfCodec.Read(raw); var p = (PlacedEntity)read.Drawing.Entities.Single();
            var edit = p with { Geometry = ((TextEntity)p.Geometry) with { Text = "After\\PΩ", Height = 5 } };
            var output = DxfCodec.Write(read.Drawing with { Entities = [edit] }, read.Source);
            Check(output.Text.Contains("41\n45\n") && output.Text.Contains("71\n5\n") && output.Text.Contains("1000\nretained\n"));
            var next = (PlacedEntity)DxfCodec.Read(output.Text).Drawing.Entities.Single();
            Check(next.Placement == p.Placement && TextEditing.TextOf(next)!.Text == "After\\PΩ" && TextEditing.TextOf(next)!.Height == 5);
        });
        test("new long MTEXT uses 250-character chunks with final group 1", () => {
            var text = new string('a', 600); var output = DxfCodec.Write(Drawing.Empty with { Entities = [new TextEntity(default, text, 5, 0, true)] });
            var record = DxfCodec.Read(output.Text).Source.Records.Values.Single();
            Check(record.Count(p => p.Code == 3) == 2 && record.Single(p => p.Code == 1).Value.Length == 100);
        });
        test("source-less tilted MTEXT remains native MTEXT", () => {
            var read = DxfCodec.Read(raw);
            var output = DxfCodec.Write(read.Drawing);
            var next = (PlacedEntity)DxfCodec.Read(output.Text).Drawing.Entities.Single();
            Check(next.Placement == ((PlacedEntity)read.Drawing.Entities.Single()).Placement);
        });

        static void Reject(Action action) { try { action(); } catch (Exception e) when (e is ArgumentException or InvalidOperationException or NotSupportedException) { return; } throw new Exception("Expected rejection."); }
        static CadSession Session(Entity e) { var s = new CadSession(new(Drawing.Empty with { Entities = [e] })); s.Select(e.Id); return s; }
        static void Inputs(CommandEngine c, params string[] values) { foreach (var v in values) c.Submit(v); }
        foreach (var binary in new[] { false, true }) foreach (var count in new[] { 0, 249, 250, 251, 500, 1000 })
            test($"Unicode MTEXT chunk boundary {count}, binary={binary}", () => {
                var value = string.Concat(Enumerable.Repeat("😀", count));
                var d = Drawing.Empty with { Entities = [new TextEntity(default, value, Multiline: true)] };
                var read = DxfBinary.Read(DxfBinary.Write(d, binary: binary).Bytes);
                var record = read.Source.Records.Values.Single();
                Check(record.Where(p => p.Code == 3).All(p => p.Value.EnumerateRunes().Count() == 250));
                Check(record.Single(p => p.Code == 1).Value.EnumerateRunes().Count() < 250);
                Check(TextEditing.TextOf(read.Drawing.Entities.Single())!.Text == value);
            });
        test("MTEXT newline forms normalize to paragraph escapes", () => {
            var value = new string('a', 249) + "\r\n" + "Ω\rB\nC";
            var read = DxfCodec.Read(DxfCodec.Write(Drawing.Empty with { Entities = [new TextEntity(default, value, Multiline: true)] }).Text);
            Check(TextEditing.TextOf(read.Drawing.Entities.Single())!.Text == new string('a', 249) + "\\PΩ\\PB\\PC");
        });
        test("MTEXT transport rejects NUL and unpaired surrogates", () => {
            foreach (var value in new[] { "a\0b", "a\ud800", "\udc00b" })
                Reject(() => DxfCodec.Write(Drawing.Empty with { Entities = [new TextEntity(default, value, Multiline: true)] }));
        });
        foreach (var binary in new[] { false, true })
            test($"independent MTEXT retained edit, placement and creation binary={binary}", () => {
                var read = DxfBinary.Read(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "fixtures", "independent-mtext.dxf")));
                var root = read.Drawing.Entities.Single(); var s = new CadSession(new(read.Drawing)); s.Select(root.Id);
                var value = "Unicode Ω 😀 " + new string('x', 700) + "\\PEnd";
                s.SetTextProperties(root, value, 5, 0);
                var native = CadProjectCodec.Read(CadProjectCodec.Write(s.Document.Drawing, read.Source));
                var edited = DxfBinary.Write(native.Drawing, native.DxfSource, binary);
                var parsed = DxfBinary.Read(edited.Bytes);
                Check(TextEditing.TextOf(parsed.Drawing.Entities.Single())!.Text == value);
                Check(parsed.Source.Text.Contains("keep application text") && parsed.Source.Text.Contains("keep application chunks") && parsed.Source.Text.Contains("keep extended text"));
                Check(edited.Warnings.All(w => !w.Contains("not retained")));
                var t = new Transform3(new(2,0,0),new(0,0,-2),new(0,1,0),new(10,20,30));
                var placed = DxfBinary.Write(Drawing.Empty with { Entities = [new PlacedEntity(new TextEntity(default,value,3,Multiline:true),t)] }, binary:binary);
                var creation = new CadSession(); Inputs(new(creation), "MTEXT", "0,0", value);
                var created = DxfBinary.Write(creation.Document.Drawing,binary:binary);
                var folder=Environment.GetEnvironmentVariable("CADSPACE_EDITING_OUTPUT");
                if (folder != null) {
                    Directory.CreateDirectory(folder);
                    foreach (var (kind, bytes) in new[]{("edited",edited.Bytes),("placed",placed.Bytes),("created",created.Bytes)})
                        File.WriteAllBytes(Path.Combine(folder,"mtext-"+kind+(binary?"-binary":"")+".dxf"),bytes);
                }
            });
        test("rotation edits preserve the actual tilted MTEXT world basis", () => {
            var read = DxfCodec.Read(raw); var s = new CadSession(new(read.Drawing)); var root = read.Drawing.Entities.Single(); s.Select(root.Id);
            s.SetTextProperties(root, "Rotated", 5, 30);
            var result = DxfCodec.Write(s.Document.Drawing, read.Source); var expected = EntityGeometry.BuildScene(s.Document.Drawing).Texts.Single();
            var actual = EntityGeometry.BuildScene(DxfCodec.Read(result.Text).Drawing).Texts.Single();
            Check(expected.Position.DistanceTo(actual.Position)<1e-10 && expected.AxisX.DistanceTo(actual.AxisX)<1e-10 && expected.AxisY.DistanceTo(actual.AxisY)<1e-10);
            Check(result.Warnings.Any(w=>w.Contains("metadata")));
        });
        test("MTEXT refuses nonuniform scale and shear rather than changing glyph geometry", () => {
            foreach (var transform in new[]{Transform3.Scaling(new(2,1,1)),new Transform3(Vec3.UnitX,new(1,1,0),Vec3.UnitZ,default)})
                Reject(()=>DxfCodec.Write(Drawing.Empty with{Entities=[new PlacedEntity(new TextEntity(default,"A",Multiline:true),transform)]}));
        });
        test("mirrored MTEXT remains on its oriented plane", () => {
            var d = Drawing.Empty with{Entities=[new PlacedEntity(new TextEntity(default,"A",Multiline:true),Transform3.Scaling(new(-2,2,1)))]};
            var actual = EntityGeometry.BuildScene(DxfCodec.Read(DxfCodec.Write(d).Text).Drawing).Texts.Single();
            var expected = EntityGeometry.BuildScene(d).Texts.Single();
            Check(expected.AxisX.DistanceTo(actual.AxisX) < 1e-10 && expected.AxisY.DistanceTo(actual.AxisY) < 1e-10);
        });
        test("MTEXT command creates content through the shared session and one Undo", () => {
            var s = new CadSession(); var c = new CommandEngine(s); s.CurrentColorIndex=3;
            Inputs(c,"MT","1,2,3","A\\PB"); var text = (TextEntity)s.Document.Drawing.Entities.Single();
            Check(text.Multiline && text.Position==new Vec3(1,2,3) && text.ColorIndex==3 && !c.IsActive);
            s.Document.Undo();Check(s.Document.Drawing.Entities.IsEmpty);
        });
        test("MTEXT command cancellation and repeated point clicks are harmless", () => {
            var s = new CadSession(); var c = new CommandEngine(s); Inputs(c,"MT","1,2");c.Point(new(99,99));c.Cancel();Check(!s.Document.CanUndo);
            Inputs(c,"MT","0,0","100,200");Check(((TextEntity)s.Document.Drawing.Entities.Single()).Text=="100,200");
        });
        test("formatting edit combines content height and angle in one transaction", () => {
            var old=new TextEntity(new(1,2),"A",5,10);var s=Session(old);var before=s.Document.Drawing;
            s.SetTextProperties(old,"B",12,30);var next=(TextEntity)s.Document.Drawing.Entities.Single();
            Check(next.Position==old.Position && next.Id==old.Id && next.Text=="B" && next.Height==12 && next.Rotation==30);
            s.Document.Undo();Check(s.Document.Drawing==before);
        });
        test("formatting no-op retains snapshot and redo history", () => {
            var old=new TextEntity(default,"A",5);var s=Session(old);s.SetText(old,"B");s.Document.Undo();var before=s.Document.Drawing;
            s.SetTextProperties(old,"A",5,0);Check(ReferenceEquals(before,s.Document.Drawing) && s.Document.CanRedo);
        });
        test("invalid and stale formatting fail atomically", () => {
            var old=new TextEntity(default,"A",5);var s=Session(old);var before=s.Document.Drawing;
            foreach(var h in new[]{0d,-1,double.NaN,double.PositiveInfinity,1e13})Reject(()=>s.SetTextProperties(old,"B",h,0));
            Reject(()=>s.SetTextProperties(old,"B",5,double.NaN));Check(s.Document.Drawing==before);
            s.SetTextProperties(old,"B",6,1);Reject(()=>s.SetTextProperties(old,"C",7,2));
        });
        test("styled TEXT rotation does not overwrite its original placement angle", () => {
            var textRaw="0\nSECTION\n2\nENTITIES\n0\nTEXT\n5\nAB\n10\n5\n20\n6\n40\n3\n1\nA\n50\n45\n41\n2\n0\nENDSEC\n0\nEOF\n";
            var read=DxfCodec.Read(textRaw);var s=new CadSession(new(read.Drawing));var root=read.Drawing.Entities.Single();s.Select(root.Id);s.SetTextProperties(root,"B",4,20);
            var expected=EntityGeometry.BuildScene(s.Document.Drawing).Texts.Single();var actual=EntityGeometry.BuildScene(DxfCodec.Read(DxfCodec.Write(s.Document.Drawing,read.Source).Text).Drawing).Texts.Single();
            Check(expected.Position.DistanceTo(actual.Position)<1e-10 && expected.AxisX.DistanceTo(actual.AxisX)<1e-10 && expected.AxisY.DistanceTo(actual.AxisY)<1e-10);
        });
        test("MATCHPROP no-op preserves clean state and redo", () => {
            var a=new LineEntity(default,Vec3.UnitX);var b=new LineEntity(Vec3.UnitY,new(1,1));var s=Session(a);s.Add("B",b);s.Document.Undo();s.Document.Redo();s.Document.MarkSaved();
            var before=s.Document.Drawing;s.MatchProperties(a.Id,[b.Id]);Check(ReferenceEquals(before,s.Document.Drawing) && !s.Document.IsDirty);
            s.Add("Point",new PointEntity(default));s.Document.Undo();s.MatchProperties(a.Id,[b.Id]);Check(s.Document.CanRedo);
        });
    }
}
