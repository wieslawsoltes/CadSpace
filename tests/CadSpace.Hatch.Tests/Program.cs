using System.Collections.Immutable;
using CadSpace.Dxf;
using CadSpace.Engine;
using CadSpace.Geometry;
using CadSpace.Model;

var tests = new List<(string, Action)>();
void Test(string name, Action test) => tests.Add((name, test));
void Check(bool value, string reason = "Assertion failed") { if (!value) throw new Exception(reason); }
void Reject(Action a) { try { a(); } catch (Exception e) when (e is ArgumentException or InvalidOperationException or NotSupportedException or FormatException) { return; } throw new Exception("Expected rejection"); }
HatchRegionEntity Square(HatchGradient? gradient = null) => new([[new(new(0,0)),new(new(20,0)),new(new(20,20)),new(new(0,20))]],true,[]) { Gradient = gradient };
CadSession Session(Entity e) { var s = new CadSession(new(Drawing.Empty with {Entities=[e]})); s.Select(e.Id); return s; }
var folder = Environment.GetEnvironmentVariable("CADSPACE_HATCH_OUTPUT");
void Write(string name, byte[] bytes) { if (folder != null) { Directory.CreateDirectory(folder); File.WriteAllBytes(Path.Combine(folder,name),bytes); } }
foreach (var name in HatchGradient.Names)
{
    Test("gradient profile bounded colors " + name, () => {
        var h = Square(new(name,0xff143cc8,0xfff0b428,30,.25));
        var t = HatchGradientGeometry.For(h); Check(t.Length > 0 && t.Length <= HatchGradientGeometry.MaximumTriangles);
        var area = t.Sum(v => (v.B-v.A).Cross(v.C-v.A).Length / 2); Check(Math.Abs(area-400)<1e-7);
        Check(t.All(v => v.A.IsFinite && v.ColorA >> 24 == 255));
        Check(HatchGradientGeometry.For(h) == t, "Retained immutable hatch must reuse tessellation");
        var scene = EntityGeometry.BuildScene(Drawing.Empty with {Entities=[h]});
        Check(scene.Paths.All(p => p.VertexColors.Length == 3) && scene.Triangles.All(t => t.ColorB.HasValue && t.ColorC.HasValue));
        Check(scene.Triangles.Length == t.Length);
    });
}
foreach (var binary in new[]{false,true})
{
    Test("independent nine-gradient native/source-less roundtrip " + binary, () => {
        var input = DxfBinary.Read(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory,"fixtures","independent-hatches.dxf")));
        Check(input.Drawing.Entities.All(e => HatchEditing.Unwrap(e)?.Gradient != null));
        var project = CadProjectCodec.Write(input.Drawing,input.Source); Check(project.Contains("\"version\": 4"));
        var read = CadProjectCodec.Read(project); Check(DxfCodec.Write(read.Drawing,read.DxfSource).Text == input.Source.Text);
        var bytes = DxfBinary.Write(read.Drawing,binary:binary).Bytes; var again = DxfBinary.Read(bytes);
        for(var i=0;i<9;i++) {
            var a=HatchEditing.Unwrap(input.Drawing.Entities[i])!; var b=HatchEditing.Unwrap(again.Drawing.Entities[i])!;
            Check(a.Gradient == b.Gradient && b.Loops.Length == 2 && b.Solid);
        }
        Write(binary?"hatches-binary.dxf":"hatches.dxf",bytes);
    });
    Test("native attribute sequence survives source-less export " + binary, () => {
        var input=DxfBinary.Read(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory,"fixtures","independent-editing.dxf")));
        var root=input.Drawing.Entities.OfType<CompositeEntity>().Single(e=>e.DxfType=="INSERT");
        var d=DxfAttributeEditing.Apply(input.Drawing,root,new Dictionary<int,string>{{0,"Native Ω"}});
        var saved=CadProjectCodec.Read(CadProjectCodec.Write(d));
        var output=DxfBinary.Write(saved.Drawing,binary:binary);var again=DxfBinary.Read(output.Bytes);
        var insert=again.Drawing.Entities.OfType<CompositeEntity>().Single(e=>e.DxfType=="INSERT");
        Check(DxfAttributeEditing.Read(insert)[0].Value=="Native Ω");
        Check(insert.SourceRecord.Contains("retained attribute application note") && insert.SourceRecord.Contains("Hidden A"));
        Check(!output.Warnings.Any(w=>w.Contains("display children")));
        Write(binary?"attributes-native-binary.dxf":"attributes-native.dxf",output.Bytes);
    });
}
Test("new hatch retains circle and polyline bulges",()=>{
    var c=new CircleEntity(default,10);var p=new PolylineEntity([new(new(20,0),1),new(new(40,0),1)],true);
    var h=(HatchRegionEntity)HatchEditing.FromBoundaries([c,p]);
    Check(h.Loops.Length==2 && h.Loops.All(l=>l.Length==2 && l[0].Bulge==1));
});
Test("hatch create and boundary extraction are atomic",()=>{
    var s=Session(new CircleEntity(default,10));s.CreateHatch(solid:true);var h=s.Document.Drawing.Entities[^1];s.Select(h.Id);
    var before=s.Document.Drawing;s.ExtractBoundaries();Check(s.Document.Drawing.Entities[^1] is PolylineEntity{Closed:true});
    s.Document.Undo();Check(ReferenceEquals(before,s.Document.Drawing));
});
Test("tilted hatch creation retains plane and native gradient",()=>{
    var t=Coordinates3D.ObjectCoordinateSystem(new(1,2,3));var circle=new PlacedEntity(new CircleEntity(default,10),t);
    var root=HatchEditing.FromBoundaries([circle],gradient:new("LINEAR",0xff000000,0xffffffff));
    Check(root is PlacedEntity p && p.Placement==t);
    var d=DxfBinary.Read(DxfBinary.Write(Drawing.Empty with{Entities=[root]}).Bytes).Drawing;
    Check(HatchEditing.Unwrap(d.Entities[0])!.Gradient!=null);
});
Test("hatch rejects open wide nonplanar mixed-frame inputs",()=>{
    var c=new CircleEntity(default,10);
    Reject(()=>HatchEditing.FromBoundaries([new LineEntity(default,Vec3.UnitX)]));
    Reject(()=>HatchEditing.FromBoundaries([new PolylineEntity([new(default),new(Vec3.UnitX)],false)]));
    Reject(()=>HatchEditing.FromBoundaries([new PolylineEntity([new(default),new(Vec3.UnitX),new(Vec3.UnitY)],true){ConstantWidth=2}]));
    Reject(()=>HatchEditing.FromBoundaries([c,new CircleEntity(Vec3.UnitZ,5)]));
    Reject(()=>HatchEditing.FromBoundaries([c,new PlacedEntity(c,Transform3.Translation(Vec3.UnitX))]));
});
Test("hatch edit no-op undo redo and stale capture",()=>{
    var h=Square();var s=Session(h);s.SetFill(h,h.Solid,h.Pattern,h.PatternName,h.IslandStyle,h.Gradient);
    Check(!s.Document.CanUndo);s.SetFill(h,false,HatchEditing.Pattern(20,4,default),"USER",1,null);
    Reject(()=>s.SetFill(h,true,[],"SOLID",0,null));s.Document.Undo();Check(s.Document.Drawing.Entities[0]==h);
    s.SetFill(h,h.Solid,h.Pattern,h.PatternName,h.IslandStyle,h.Gradient);Check(s.Document.CanRedo);
});
Test("hatch validation rejects locked fills and invalid pattern",()=>{
    var h=Square();var s=Session(h);Reject(()=>s.SetFill(h,false,[],"USER",0,null));
    Reject(()=>s.SetFill(h,true,[],"SOLID",3,null));
    s.Document.Edit("Lock",d=>d with{Layers=d.Layers.SetItem("0",new("0",Locked:true))});
    Reject(()=>s.SetFill(h,true,[],"SOLID",0,new("LINEAR",0xff000000,0xffffffff)));
});
Test("island holes remain unfilled by gradient triangles",()=>{
    var h=Square(new("LINEAR",0xff000000,0xffffffff));
    h=h with{Loops=h.Loops.Add([new(new(5,5)),new(new(15,5)),new(new(15,15)),new(new(5,15))])};
    var triangles=HatchGradientGeometry.For(h);var area=triangles.Sum(t=>(t.B-t.A).Cross(t.C-t.A).Length/2);
    Check(Math.Abs(area-300)<1e-7);Check(triangles.All(t=>!GeometryMath.PointInPolygon((t.A+t.B+t.C)/3,[new(5,5),new(15,5),new(15,15),new(5,15)])));
});
Test("gradient edit invalidates only the edited root",()=>{
    var h=Square(new("LINEAR",0xff000000,0xffffffff));var second=Square(new("LINEAR",0xffff0000,0xff0000ff));
    var d=Drawing.Empty with{Entities=[h,second]};var cache=new DrawingSceneCache();cache.Build(d);
    cache.Build(d with{Entities=d.Entities.SetItem(0,h with{Gradient=h.Gradient! with{Angle=90}})});
    Check(cache.RebuiltRoots==1 && cache.ReusedRoots==1);
});
Test("gradient rejects invalid stops controls and transformed shear",()=>{
    Reject(()=>new HatchGradient("UNKNOWN",0xff000000,0xffffffff).Validate());
    Reject(()=>new HatchGradient("LINEAR",0x00000000,0xffffffff).Validate());
    Reject(()=>new HatchGradient("LINEAR",0xff000000,0xffffffff,Shift:2).Validate());
    Reject(()=>new HatchGradient("LINEAR",0xff000000,0xffffffff,Angle:double.NaN).Validate());
    var h=Square(new("LINEAR",0xff000000,0xffffffff));
    Reject(()=>DxfCodec.Write(Drawing.Empty with{Entities=[new PlacedEntity(h,Transform3.Scaling(new(2,1,1)))]}));
});
Test("HATCH GRADIENT HATCHEDIT and boundary commands are connected",()=>{
    var s=Session(new CircleEntity(default,10));var c=new CommandEngine(s);c.Start("HATCH");
    Check(HatchEditing.Unwrap(s.Document.Drawing.Entities[^1])!=null);s.Document.Undo();c.Start("GD");
    var h=s.Document.Drawing.Entities[^1];Check(HatchEditing.Unwrap(h)!.Gradient!=null);s.Select(h.Id);
    var request="";c.ViewRequested+=r=>request=r;c.Start("HE");Check(request=="HATCHEDITOR" && !c.IsActive);
    c.Start("HGB");Check(s.Document.Drawing.Entities[^1] is PolylineEntity);
});
Test("invisible-only INSERT remains editable and exportable",()=>{
    const string raw="0\nSECTION\n2\nENTITIES\n0\nINSERT\n5\nAB\n2\nPART\n66\n1\n10\n0\n20\n0\n30\n0\n0\nATTRIB\n5\n2000\n10\n0\n20\n0\n30\n0\n40\n2\n1\nHidden\n2\nSERIAL\n70\n1\n0\nSEQEND\n5\n2001\n0\nENDSEC\n0\nEOF\n";
    var read=DxfCodec.Read(raw);var insert=(CompositeEntity)read.Drawing.Entities.Single();
    Check(insert.Children.Length==1 && DxfAttributeEditing.Read(insert)[0].Invisible);
    var drawing=read.Drawing with{Blocks=read.Drawing.Blocks.Add("PART",new("PART",default,[]))};
    var changed=DxfAttributeEditing.Apply(drawing,insert,new Dictionary<int,string>{{0,"Changed"}});
    var outText=DxfCodec.Write(changed).Text;Check(outText.Contains("Changed") && outText.Contains("0\nATTRIB\n"));
    var handles=DxfCodec.ParsePairs(outText).Where(p=>p.Code is 5 or 105).Select(p=>p.Value).ToArray();
    Check(handles.Distinct(StringComparer.OrdinalIgnoreCase).Count()==handles.Length,"Generated and retained handles must not collide");
});
Test("moved retained INSERT cannot silently restore old attributes",()=>{
    var input=DxfBinary.Read(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory,"fixtures","independent-editing.dxf")));
    var root=input.Drawing.Entities.OfType<CompositeEntity>().Single(e=>e.DxfType=="INSERT");
    var moved=EntityGeometry.Transform(root,Transform3.Translation(new(1,2)));
    var output=DxfCodec.Write(input.Drawing with{Entities=[moved]});Check(output.Warnings.Any(w=>w.Contains("display children")));
});
var failed=0;
foreach(var (name,test) in tests)try{test();Console.WriteLine("PASS "+name);}catch(Exception e){failed++;Console.Error.WriteLine("FAIL "+name+": "+e);}
Console.WriteLine($"{tests.Count-failed}/{tests.Count} hatch and retained-attribute tests passed.");return failed==0?0:1;
