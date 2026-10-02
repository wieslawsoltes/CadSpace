using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json.Nodes;
using CadSpace.Dxf;
using CadSpace.Engine;
using CadSpace.Geometry;
using CadSpace.Model;

var tests = new List<(string Name, Action Run)>();
void Test(string name, Action run) => tests.Add((name, run));
void Check(bool value, string message = "Assertion failed") { if (!value) throw new Exception(message); }
void Near(double a, double b, double eps = 1e-8) => Check(double.IsFinite(a) && Math.Abs(a-b) <= eps, $"{a} differs from {b}");
void Reject(Action run) { try { run(); } catch (Exception e) when (e is ArgumentException or InvalidOperationException or NotSupportedException or FormatException) { return; } throw new Exception("Expected rejection"); }
LeaderEntity Leader() => new([new(0,0),new(10,0),new(10,10)]) { ArrowSize = 5 };
CadSession Session(Entity e) { var s = new CadSession(new(Drawing.Empty with { Entities = [e] })); s.Select(e.Id); return s; }
DxfReadResult Input() => DxfBinary.Read(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory,"fixtures","independent-leaders.dxf")));
void Inputs(CommandEngine c, params string[] values) { foreach (var v in values) c.Submit(v); }
void Export(string name, DxfBytesResult result)
{
    var directory = Environment.GetEnvironmentVariable("CADSPACE_LEADER_OUTPUT");
    if (directory == null) return;
    Directory.CreateDirectory(directory); File.WriteAllBytes(Path.Combine(directory,name),result.Bytes);
}

Test("leader paths, filled arrows and root picking identity", () => {
    var l=Leader(); var s=Session(l); var scene=s.Scene;
    Check(scene.Paths.Length==2 && scene.Triangles.Length==1 && scene.Paths.All(p=>p.EntityId==l.Id));
    Check(s.HitTest(new(2,.1),.5)==l.Id); Check(s.HitTest(new(4,.5),.2)==l.Id);
    Check(LeaderGeometry.For(l)==LeaderGeometry.For(l));
});
Test("disabled and zero-size arrows retain line geometry", () => {
    foreach(var l in new[]{Leader() with{ArrowEnabled=false},Leader() with{ArrowSize=0}})
    { var scene=Session(l).Scene; Check(scene.Triangles.IsEmpty && scene.Paths.Length==1); }
});
Test("three-dimensional arrow basis remains finite", () => {
    var l=new LeaderEntity([default,new(0,0,10),new(10,0,10)]);
    var scene=Session(l).Scene; Check(scene.Triangles.Length==1);
    foreach(var t in scene.Triangles)Check(t.A.IsFinite && t.B.IsFinite && t.C.IsFinite);
});
Test("leader hookline follows stored dimensions and direction flag", () => {
    var l=Input().Drawing.Entities.OfType<LeaderEntity>().Single();
    var path=LeaderGeometry.For(l).OfType<Polyline3DEntity>().Single();
    Near(path.Points[^1].X,19); Near(path.Points[^1].Y,10); Near(l.ArrowSize,6); Near(l.Gap,1);
    var other=LeaderGeometry.For(l with{HooklineReversed=false}).OfType<Polyline3DEntity>().Single(); Near(other.Points[^1].X,61);
});
Test("hookline requires text annotation and above-text setting", () => {
    foreach(var l in new[]{Leader() with{Hookline=true,TextWidth=20},Leader() with{Hookline=true,TextWidth=20,AnnotationType=0,TextAbove=false}})
        Check(LeaderGeometry.For(l).OfType<Polyline3DEntity>().Single().Points.Length==3);
});
Test("invalid counts, duplicate vertices and numeric values rejected", () => {
    Reject(()=>LeaderGeometry.Validate(new LeaderEntity([])));
    Reject(()=>LeaderGeometry.Validate(new LeaderEntity([default,default])));
    Reject(()=>LeaderGeometry.Validate(new LeaderEntity([default,new(double.PositiveInfinity,0)])));
    Reject(()=>LeaderGeometry.Validate(Leader() with{ArrowSize=-1}));
    Reject(()=>LeaderGeometry.Validate(Leader() with{ArrowSize=double.NaN}));
    Reject(()=>LeaderGeometry.Validate(Leader() with{Normal=default}));
    Reject(()=>LeaderGeometry.Validate(Leader() with{Horizontal=default}));
    Reject(()=>LeaderGeometry.Validate(Leader() with{AnnotationHandle="not-hex"}));
    Reject(()=>LeaderGeometry.Validate(Leader() with{DimensionStyle="bad\nstyle"}));
    Reject(()=>LeaderGeometry.Validate(Leader() with{AnnotationType=4}));
});
Test("scene cache rebuilds only changed leader", () => {
    var a=Leader();var b=Leader();var drawing=Drawing.Empty with{Entities=[a,b]};var cache=new DrawingSceneCache();cache.Build(drawing);
    cache.Build(drawing with{Entities=drawing.Entities.SetItem(0,a with{ArrowSize=9})});Check(cache.RebuiltRoots==1 && cache.ReusedRoots==1);
});
Test("geometry cache respects record identity and concurrent reads", () => {
    var a=Leader();var layout=LeaderGeometry.For(a);var copy=a with{ArrowSize=8};Check(LeaderGeometry.For(copy)!=layout);
    Parallel.For(0,100,i=>Check(LeaderGeometry.For(a)==layout));
});
Test("warmed leader geometry lookup allocation observation", () => {
    var a=Leader();LeaderGeometry.For(a);var before=GC.GetAllocatedBytesForCurrentThread();var watch=Stopwatch.GetTimestamp();
    var count=0;for(var i=0;i<100000;i++) count+=LeaderGeometry.For(a).Length;
    var elapsed=Stopwatch.GetElapsedTime(watch);var allocated=GC.GetAllocatedBytesForCurrentThread()-before;
    Check(count==200000);Check(allocated<=256,$"Unexpected warmed allocation {allocated}");
    Console.WriteLine($"BENCH 100000 warmed LEADER layout reads: {elapsed.TotalMilliseconds:0.###} ms, {allocated} bytes, {count} child references; no timing gate");
});
Test("LEADER creation preview undo-last and transaction", () => {
    var s=new CadSession();var c=new CommandEngine(s);Inputs(c,"LE","0,0","10,0");
    Check(c.Preview(new(10,10)).Single() is LeaderEntity && !s.Document.CanUndo);
    Inputs(c,"20,20","U","10,10","");Check(!c.IsActive && s.Document.Drawing.Entities.Single() is LeaderEntity);
    Check(((LeaderEntity)s.Document.Drawing.Entities[0]).Vertices[^1]==new Vec3(10,10));s.Document.Undo();Check(s.Document.Drawing.Entities.IsEmpty);
});
Test("leader cancellation duplicate clicks and editor request", () => {
    var s=new CadSession();var c=new CommandEngine(s);Inputs(c,"LE","0,0");c.Point(default);c.Cancel();Check(!s.Document.CanUndo);
    Inputs(c,"LE","0,0","10,0","");s.SelectAll();var request="";c.ViewRequested+=v=>request=v;c.Start("LED");Check(request=="LEADEREDITOR" && !c.IsActive);
});
Test("staged leader change preserves identity with one Undo", () => {
    var old=Leader();var s=Session(old);var before=s.Document.Drawing;s.SetLeader(old,old.Vertices.SetItem(1,new(11,1)),false,7);
    var next=(LeaderEntity)s.Document.Drawing.Entities.Single();Check(next.Id==old.Id && next.Vertices[1]==new Vec3(11,1) && !next.ArrowEnabled && next.ArrowSize==7);
    s.Document.Undo();Check(ReferenceEquals(before,s.Document.Drawing));s.Document.Redo();Check(s.Document.Drawing.Entities.Single()==next);
});
Test("leader no-op preserves clean state and redo", () => {
    var old=Leader();var s=Session(old);var before=s.Document.Drawing;s.SetLeader(old,old.Vertices.ToArray().ToImmutableArray(),true,5);
    Check(ReferenceEquals(before,s.Document.Drawing) && !s.Document.IsDirty);s.SetLeader(old,old.Vertices,false,5);s.Document.Undo();
    s.SetLeader(old,old.Vertices,true,5);Check(s.Document.CanRedo);
});
Test("stale locked and invalid leader edits fail atomically", () => {
    var old=Leader();var s=Session(old);var before=s.Document.Drawing;Reject(()=>s.SetLeader(old,[default,default],true,5));Check(ReferenceEquals(before,s.Document.Drawing));
    s.SetLeader(old,old.Vertices,false,5);Reject(()=>s.SetLeader(old,old.Vertices,true,5));s.Document.Undo();
    s.Document.Edit("Lock",d=>d with{Layers=d.Layers.SetItem("0",new("0",Locked:true))});Reject(()=>s.SetLeader(old,old.Vertices,false,5));
});
Test("leader grip transform and partial stretch retain native definition", () => {
    var old=Leader();var s=Session(old);s.MoveGrip(old,1,new(15,0));Check(((LeaderEntity)s.Document.Drawing.Entities[0]).Vertices[1]==new Vec3(15,0));s.Document.Undo();
    s.Stretch(new(9,-1),new(11,1),new(5,0));Check(((LeaderEntity)s.Document.Drawing.Entities[0]).Vertices[1]==new Vec3(15,0));
});
Test("placed leader vertex editing and snapping", () => {
    var old=Leader();var t=Transform3.Translation(new(100,200,30));var p=new PlacedEntity(old,t);var s=Session(p);
    s.SnapModes=ObjectSnapModes.Endpoint;var endpoint=s.Snap(new(100.1,200,30),.5);Check(endpoint.EntityId==p.Id && endpoint.Kind==SnapKind.Endpoint);
    s.SnapModes=ObjectSnapModes.Midpoint;var midpoint=s.Snap(new(105.1,200,30),.5);Check(midpoint.Point==new Vec3(105,200,30) && midpoint.EntityId==p.Id);
    s.MoveGrip(p,1,new(115,200,30));Check(LeaderGeometry.Unwrap(s.Document.Drawing.Entities[0])!.Vertices[1]==new Vec3(15,0));
});

foreach(var binary in new[]{false,true})
{
    Test("retained LEADER record edits and native project roundtrip " + binary, () => {
        var read=Input();var root=read.Drawing.Entities.OfType<LeaderEntity>().Single();var s=new CadSession(new(read.Drawing));s.Select(root.Id);
        s.SetLeader(root,root.Vertices.SetItem(1,new(25,15)),false,root.ArrowSize);
        s.Document.Edit("Color",d=>d with{Entities=d.Entities.Select(e=>e.Id==root.Id?e with{ColorIndex=4}:e).ToImmutableArray()});
        var native=CadProjectCodec.Read(CadProjectCodec.Write(s.Document.Drawing,read.Source));
        var result=DxfBinary.Write(native.Drawing,native.DxfSource,binary);var next=DxfBinary.Read(result.Bytes).Drawing.Entities.OfType<LeaderEntity>().Single();
        Check(next.Vertices[1]==new Vec3(25,15) && !next.ArrowEnabled && next.DimensionStyle==root.DimensionStyle);Near(next.ArrowSize,6);
        var bytesText=DxfBinary.Read(result.Bytes).Source.Text;Check(bytesText.Contains("LEADER_APPDATA") && bytesText.Contains("LEADER_XDATA"));
        Check(!result.Warnings.Any(w=>w.Contains("Regenerated LEADER")));Export("leaders-edited"+(binary?"-binary":"")+".dxf",result);
    });
    Test("canonical created tilted paper and block leaders " + binary, () => {
        var a=Leader();var transform=new Transform3(new(2,0,0),new(0,0,2),new(0,-2,0),new(10,20,30));
        var d=Drawing.Empty with{Entities=[a,new PlacedEntity(Leader(),transform),Leader() with{Layout="Layout1"}],Blocks=Drawing.Empty.Blocks.Add("LEADERS",new("LEADERS",default,[Leader()]))};
        var result=DxfBinary.Write(d,binary:binary);var next=DxfBinary.Read(result.Bytes).Drawing;
        Check(next.Entities.Count(e=>e is LeaderEntity)==3);Check(next.Blocks["LEADERS"].Entities.Single() is LeaderEntity);
        var tilted=(LeaderEntity)next.Entities[1];Near(tilted.ArrowSize,10);Near(tilted.Vertices[2].DistanceTo(new(30,20,50)),0);
        Export("leaders-created"+(binary?"-binary":"")+".dxf",result);
    });
    Test("deleted annotation is detached even when leader unchanged " + binary, () => {
        var input=Input();var l=input.Drawing.Entities.OfType<LeaderEntity>().Single();var d=input.Drawing with{Entities=[l]};
        var result=DxfBinary.Write(d,input.Source,binary);var next=DxfBinary.Read(result.Bytes).Drawing.Entities.OfType<LeaderEntity>().Single();
        Check(next.AnnotationHandle=="0" && next.AnnotationType==3 && result.Warnings.Any(w=>w.Contains("detaches")));
        Export("leaders-detached"+(binary?"-binary":"")+".dxf",result);
    });
    Test("leader topology insertion retains source payload " + binary, () => {
        var input=Input();var l=input.Drawing.Entities.OfType<LeaderEntity>().Single();
        var edited=l with{Vertices=l.Vertices.Insert(1,new(10,5))};var d=input.Drawing with{Entities=input.Drawing.Entities.Select(e=>e.Id==l.Id?edited:e).ToImmutableArray()};
        var next=DxfBinary.Read(DxfBinary.Write(d,input.Source,binary).Bytes);Check(next.Drawing.Entities.OfType<LeaderEntity>().Single().Vertices.Length==4);
        Check(next.Source.Text.Contains("LEADER_APPDATA") && next.Source.Text.Contains("LEADER_XDATA"));
    });
}
Test("canonical sizing edit cannot inherit conflicting source style", () => {
    var input=Input();var l=input.Drawing.Entities.OfType<LeaderEntity>().Single();var d=input.Drawing with{Entities=input.Drawing.Entities.Select(e=>e.Id==l.Id?l with{ArrowSize=7}:e).ToImmutableArray()};
    var output=DxfCodec.Write(d,input.Source);var next=DxfCodec.Read(output.Text).Drawing.Entities.OfType<LeaderEntity>().Single();
    Near(next.ArrowSize,7);Check(next.DimensionStyle=="CadSpaceLeader1" && output.Warnings.Any(w=>w.Contains("Regenerated LEADER")));
});
Test("unchanged native file retains exact original DXF", () => {
    var read=Input();var project=CadProjectCodec.Write(read.Drawing,read.Source);Check(project.Contains("\"version\": 5"));
    var loaded=CadProjectCodec.Read(project);Check(DxfCodec.Write(loaded.Drawing,loaded.DxfSource).Text==read.Source.Text);
});
Test("nested leader activates native project version gate", () => {
    var c=new CompositeEntity("CUSTOM",[Leader()]);var d=Drawing.Empty with{Entities=[new PlacedEntity(c,Transform3.Identity)]};
    var project=CadProjectCodec.Write(d);Check(project.Contains("\"version\": 5"));Check(CadProjectCodec.Read(project).Drawing.Entities.Single() is PlacedEntity);
});
Test("forged native leader provenance is rejected", () => {
    var input=Input();var data=JsonNode.Parse(CadProjectCodec.Write(input.Drawing,input.Source))!;
    var saved=data["sourceGraph"]!["entities"]!.AsArray().Single(n=>n!["type"]!.GetValue<string>()=="LEADER")!;
    saved["arrowSize"]=99;Reject(()=>CadProjectCodec.Read(data.ToJsonString()));
});
Test("invalid stored leader path type retained opaque", () => {
    const string raw="0\nSECTION\n2\nENTITIES\n0\nLEADER\n5\nAB\n72\n2\n76\n2\n10\n0\n20\n0\n30\n0\n10\n10\n20\n0\n30\n0\n0\nENDSEC\n0\nEOF\n";
    Check(DxfCodec.Read(raw).Drawing.Entities.Single() is OpaqueEntity);
    Check(DxfCodec.Read(raw.Replace("72\n2","72\n1")).Drawing.Entities.Single() is CompositeEntity);
    Check(DxfCodec.Read(raw.Replace("72\n2","72\n0").Replace("76\n2","76\n4")).Drawing.Entities.Single() is OpaqueEntity);
});
Test("native vertex budget matches signed DXF count transport", () => {
    var vertices=Enumerable.Range(0,LeaderGeometry.MaximumVertices).Select(i=>new Vec3(i,0)).ToImmutableArray();var l=new LeaderEntity(vertices);
    var output=DxfBinary.Write(Drawing.Empty with{Entities=[l]},binary:true);var read=DxfBinary.Read(output.Bytes);
    Check(((LeaderEntity)read.Drawing.Entities.Single()).Vertices.Length==32767);
    Reject(()=>LeaderGeometry.Validate(l with{Vertices=vertices.Add(new(32767,0))}));
});
Test("nonuniform leader export rejects without changing document", () => {
    var d=Drawing.Empty with{Entities=[new PlacedEntity(Leader(),Transform3.Scaling(new(2,1,1)))]};
    var session=new CadSession(new(d));Reject(()=>DxfCodec.Write(d));Check(ReferenceEquals(session.Document.Drawing,d));
});
Test("canonical annotation must resolve to a compatible type and layout", () => {
    foreach(var target in new Entity[]{new LineEntity(default,Vec3.UnitX){Handle="AB"},new TextEntity(default,"T",Multiline:true){Handle="AB",Layout="Layout1"}})
    {
        var l=Leader() with{AnnotationType=0,AnnotationHandle="AB"};var d=Drawing.Empty with{Entities=[l,target]};
        var next=DxfCodec.Read(DxfCodec.Write(d).Text).Drawing.Entities.OfType<LeaderEntity>().Single();Check(next.AnnotationHandle=="0");
    }
});
Test("canonical annotation handles compare numerically without per-leader scans", () => {
    var target=new TextEntity(default,"T",Multiline:true){Handle="0abc"};var l=Leader() with{AnnotationType=0,AnnotationHandle="ABC"};
    var d=Drawing.Empty with{Entities=[l,target]};var next=DxfCodec.Read(DxfCodec.Write(d).Text).Drawing.Entities.OfType<LeaderEntity>().Single();
    Check(next.AnnotationHandle.Equals(target.Handle,StringComparison.OrdinalIgnoreCase));
});
Test("single canonical style reused by many leaders", () => {
    var d=Drawing.Empty with{Entities=Enumerable.Range(0,1000).Select(i=>(Entity)(Leader() with{ArrowSize=1+i%10})).ToImmutableArray()};
    var output=DxfCodec.Write(d);var next=DxfCodec.Read(output.Text).Drawing;
    Check(next.Entities.OfType<LeaderEntity>().Select(e=>e.DimensionStyle).Distinct().Count()==1 && next.Entities.Length==1000);
});
var failed=0;
foreach(var (name,run) in tests)try{run();Console.WriteLine("PASS "+name);}catch(Exception e){failed++;Console.Error.WriteLine("FAIL "+name+": "+e);}
Console.WriteLine($"{tests.Count-failed}/{tests.Count} native LEADER regressions passed.");return failed==0?0:1;
