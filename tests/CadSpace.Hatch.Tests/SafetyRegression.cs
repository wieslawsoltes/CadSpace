using System.Collections.Immutable;
using System.Text.Json.Nodes;
using CadSpace.Dxf;
using CadSpace.Engine;
using CadSpace.Geometry;
using CadSpace.Model;

internal static class SafetyRegression
{
    public static void Register(Action<string, Action> test)
    {
        static void Check(bool value) { if (!value) throw new Exception("Hatch safety assertion failed."); }
        static void Reject(Action a) { try { a(); } catch (Exception e) when (e is ArgumentException or InvalidOperationException or FormatException or NotSupportedException) { return; } throw new Exception("Expected rejection."); }
        static CadSession Session(Entity e) { var s = new CadSession(new(Drawing.Empty with { Entities = [e] })); s.Select(e.Id); return s; }
        static DxfReadResult AttributeFile() => DxfBinary.Read(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory,"fixtures","independent-editing.dxf")));
        static DxfReadResult HatchFile() => DxfBinary.Read(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory,"fixtures","independent-hatches.dxf")));
        test("legacy hatch equivalent pattern edits preserve clean snapshot", () => {
            var e = new HatchEntity([default,new(20,0),new(20,20),new(0,20)]);
            var s = Session(e); var h = HatchEditing.Unwrap(e)!; var before = s.Document.Drawing;
            s.SetFill(e,h.Solid,h.Pattern,h.PatternName,h.IslandStyle,h.Gradient);
            Check(ReferenceEquals(before,s.Document.Drawing) && !s.Document.IsDirty && !s.Document.CanUndo);
        });
        test("legacy hatch conversion preserves all root properties", () => {
            var e = new HatchEntity([default,new(20,0),new(20,20),new(0,20)]) { Handle="ABC",ColorIndex=3,LineWeight=.5,LinetypeScale=2 };
            var s = Session(e); s.SetFill(e,true,[],"SOLID",0,new("LINEAR",0xff000000,0xffffffff));
            var after = (HatchRegionEntity)s.Document.Drawing.Entities.Single();
            Check(after.Id==e.Id && after.Handle==e.Handle && after.ColorIndex==3 && after.LineWeight==.5 && after.LinetypeScale==2);
            s.Document.Undo(); Check(s.Document.Drawing.Entities.Single()==e);
        });
        test("invalid colors or nonfinite shifts reject without document edits", () => {
            var read = HatchFile(); var e = read.Drawing.Entities[0]; var h=HatchEditing.Unwrap(e)!; var s=new CadSession(new(read.Drawing));s.Select(e.Id);
            var before=s.Document.Drawing;
            Reject(()=>s.SetFill(e,true,[],"SOLID",0,h.Gradient! with{Shift=double.PositiveInfinity}));
            Reject(()=>s.SetFill(e,true,[],"SOLID",0,h.Gradient! with{SecondColor=0}));
            Check(ReferenceEquals(before,s.Document.Drawing));
        });
        test("hatch command uses selected layer locks and does not partly add", () => {
            var e=new CircleEntity(default,20);var s=Session(e);s.Document.Edit("Lock",d=>d with{Layers=d.Layers.SetItem("0",new("0",Locked:true))});
            var before=s.Document.Drawing;new CommandEngine(s).Start("GRADIENT");Check(ReferenceEquals(before,s.Document.Drawing));
        });
        test("hatch editor command picking rejects unrelated geometry without selecting it", () => {
            var e=new LineEntity(default,new(20,0));var s=new CadSession(new(Drawing.Empty with{Entities=[e]}));var c=new CommandEngine(s);
            c.Start("HE");c.Point(new(10,0));Check(s.Selection.Count==0 && !c.IsActive);
        });
        test("changed retained INSERT handle cannot reintroduce old native handle", () => {
            var read=AttributeFile();var e=read.Drawing.Entities.OfType<CompositeEntity>().Single(x=>x.DxfType=="INSERT");
            var changed=e with{Handle="FFFF00"};
            var output=DxfCodec.Write(read.Drawing with{Entities=[changed]});
            Check(output.Warnings.Any(w=>w.Contains("display children")) && !output.Text.Contains("0\nATTRIB\n"));
        });
        test("retained native attribute export patches root common properties", () => {
            var read=AttributeFile();var e=read.Drawing.Entities.OfType<CompositeEntity>().Single(x=>x.DxfType=="INSERT");
            var changed=e with{ColorIndex=3,LinetypeScale=2};
            var output=DxfCodec.Read(DxfCodec.Write(read.Drawing with{Entities=[changed]}).Text);
            var after=output.Drawing.Entities.OfType<CompositeEntity>().Single();
            Check(after.ColorIndex==3 && after.LinetypeScale==2 && DxfAttributeEditing.Read(after)[0].Value=="Part A");
        });
        test("invisible-only pre-upgrade native source graphs remain verifiable", () => {
            // Use an independently authored minimal invisible-only sequence.
            const string raw="0\nSECTION\n2\nENTITIES\n0\nINSERT\n5\nAB\n2\nPART\n66\n1\n10\n0\n20\n0\n0\nATTRIB\n5\nAC\n10\n0\n20\n0\n40\n2\n1\nHidden\n2\nTAG\n70\n1\n0\nSEQEND\n5\nAD\n0\nENDSEC\n0\nEOF\n";
            var imported=DxfCodec.Read(raw);var node=JsonNode.Parse(CadProjectCodec.Write(imported.Drawing,imported.Source))!;
            foreach(var field in new[]{"drawing","sourceGraph"}) {
                var entities=node[field]!["entities"]!.AsArray();var original=entities[0]!;
                var simple=original["children"]![0]!.DeepClone();
                foreach(var key in new[]{"id","handle","layer","aci","weight","linetype","linetypeScale","visible","layout"})simple[key]=original[key]!.DeepClone();
                entities[0]=simple;
            }
            var reopened=CadProjectCodec.Read(node.ToJsonString());
            Check(reopened.Drawing.Entities[0] is BlockReferenceEntity && DxfCodec.Write(reopened.Drawing,reopened.DxfSource).Text==raw);
            node["sourceGraph"]!["entities"]![0]!["name"]="FORGED";
            Reject(()=>CadProjectCodec.Read(node.ToJsonString()));
        });
        test("gradient native provenance rejects altered stop colors", () => {
            var read=HatchFile();var node=JsonNode.Parse(CadProjectCodec.Write(read.Drawing,read.Source))!;
            node["sourceGraph"]!["entities"]![0]!["gradient"]!["first"]=0xff00ff00u;
            Reject(()=>CadProjectCodec.Read(node.ToJsonString()));
        });
        test("malformed gradient records remain opaque with the complete payload", () => {
            var read=HatchFile();var h=(HatchRegionEntity)read.Drawing.Entities[0];
            var raw=DxfCodec.Write(Drawing.Empty with{Entities=[h]}).Text;
            foreach(var bad in new[]{raw.Replace("453\n2\n","453\n3\n"),raw.Replace("470\nLINEAR\n","470\nUNKNOWN\n"),raw.Replace("461\n0.25\n","461\nNaN\n")}) {
                var failed=DxfCodec.Read(bad);Check(failed.Drawing.Entities[0] is OpaqueEntity);
                Check(DxfCodec.Write(failed.Drawing,failed.Source).Text==bad);
            }
        });
        test("one-color gradient dialog metadata roundtrips without changing stored stops", () => {
            var read=HatchFile();var h=(HatchRegionEntity)read.Drawing.Entities[0];h=h with{Gradient=h.Gradient! with{SingleColor=true,Tint=.7}};
            var d=Drawing.Empty with{Entities=[h]};var next=DxfBinary.Read(DxfBinary.Write(d,binary:true).Bytes);
            Check(((HatchRegionEntity)next.Drawing.Entities[0]).Gradient==h.Gradient);
        });
        test("gradient mesh cache never crosses edited record identities", () => {
            var read=HatchFile();var h=(HatchRegionEntity)read.Drawing.Entities[0];var a=HatchGradientGeometry.For(h);
            var b=HatchGradientGeometry.For(h with{Gradient=h.Gradient! with{FirstColor=0xffff0000}});
            Check(a!=b && a[0].ColorA!=b[0].ColorA);
            Parallel.For(0,20,_=>Check(HatchGradientGeometry.For(h)==a));
        });
    }
}
