using System.Collections.Immutable;
using CadSpace.Dxf;
using CadSpace.Engine;
using CadSpace.Geometry;
using CadSpace.Model;

var tests = new List<(string, Action)>();
void Test(string name, Action run) => tests.Add((name, run));
void Check(bool value, string reason = "Assertion failed") { if (!value) throw new Exception(reason); }
void Near(double a, double b, double tolerance = 1e-7) => Check(Math.Abs(a - b) <= tolerance, $"Expected {b}, got {a}.");
void Reject(Action action) { try { action(); } catch (Exception e) when (e is ArgumentException or InvalidOperationException or NotSupportedException) { return; } throw new Exception("Expected rejection."); }
CadSession Session(params Entity[] e) { var s = new CadSession(new(Drawing.Empty with { Entities = e.ToImmutableArray() })); s.SelectAll(); return s; }
PolylineEntity P(params Vec3[] points) => PolylineEntity.FromPoints(points);
void Inputs(CommandEngine c, params string[] values) { foreach (var value in values) c.Submit(value); }
double Area(PolylineEntity p) => EntityGeometry.BuildScene(Drawing.Empty with { Entities = [p] }).Triangles.Sum(t => (t.B - t.A).Cross(t.C - t.A).Length / 2);

Test("join unordered and reversed lines without losing primary identity/style", () => {
    var first = new LineEntity(new(10,0), new(20,0)) { Handle = "AB", ColorIndex = 4, LinetypeScale = 7 };
    var result = (PolylineEntity)PolylineTools.Join([first, new LineEntity(new(30,0),new(20,0)), new LineEntity(default,new(10,0))]);
    Check(result.Id == first.Id && result.Handle == "AB" && result.ColorIndex == 4 && result.LinetypeScale == 7 && result.Vertices.Length == 4);
    Near(result.Vertices[0].Position.DistanceTo(result.Vertices[^1].Position), 30);
});
Test("join analytic line arc line chain", () => {
    var p = (PolylineEntity)PolylineTools.Join([new LineEntity(new(-10,0),default), new ArcEntity(new(0,10),10,270,360), new LineEntity(new(20,10),new(10,10))]);
    Check(p.Vertices.Length == 4); Near(p.Vertices[1].Bulge, Math.Tan(Math.PI/8));
    Check(!p.Closed); Near(p.Vertices[2].Position.DistanceTo(new(10,10)),0);
});
Test("reversed arc retains a negative analytic bulge", () => {
    var p = (PolylineEntity)PolylineTools.Join([new LineEntity(new(20,0),new(10,0)), new ArcEntity(default,10,90,360)]);
    Check(p.Vertices.Length == 3 && p.Vertices[1].Bulge < -1);
});
Test("join materializes global widths and reverses tapers", () => {
    var wide = P(new(20,0),new(10,0)) with { ConstantWidth = 5 };
    var p = (PolylineEntity)PolylineTools.Join([new LineEntity(default,new(10,0)),wide]);
    Near(p.ConstantWidth,0); Near(p.Vertices[1].StartWidth,5); Near(p.Vertices[1].EndWidth,5);
    wide = wide with { ConstantWidth = 0, Vertices = [new(new(20,0)){StartWidth=2,EndWidth=8},new(new(10,0))] };
    p = (PolylineEntity)PolylineTools.Join([new LineEntity(default,new(10,0)),wide]);
    Near(p.Vertices[1].StartWidth,8); Near(p.Vertices[1].EndWidth,2);
});
Test("join square produces closed polyline with no duplicate closing vertex", () => {
    var p = (PolylineEntity)PolylineTools.Join([new LineEntity(default,new(10,0)),new LineEntity(new(10,10),new(0,10)),new LineEntity(new(10,0),new(10,10)),new LineEntity(new(0,10),default)]);
    Check(p.Closed && p.Vertices.Length == 4);
});
Test("join two semicircles keeps a two-bulge ring", () => {
    var p = (PolylineEntity)PolylineTools.Join([new ArcEntity(default,10,0,180), new ArcEntity(default,10,180,360)]);
    Check(p.Closed && p.Vertices.Length == 2); Near(Math.Abs(p.Vertices[0].Bulge),1);
});
Test("join common tilted OCS preserves the plane", () => {
    var t = Coordinates3D.ObjectCoordinateSystem(new(1,2,3));
    var result = PolylineTools.Join([new PlacedEntity(new LineEntity(default,new(10,0)),t),new PlacedEntity(new LineEntity(new(10,0),new(10,10)),t)]);
    Check(result is PlacedEntity { Geometry: PolylineEntity }); Near(((PlacedEntity)result).Placement.Z.DistanceTo(t.Z),0);
});
Test("join rejects differing frames, elevations, branching and disconnection", () => {
    var line = new LineEntity(default,new(10,0));
    Reject(() => PolylineTools.Join([line,new PlacedEntity(new LineEntity(new(10,0),new(20,0)),Transform3.Translation(Vec3.UnitZ))]));
    Reject(() => PolylineTools.Join([line,new LineEntity(new(10,0,1),new(20,0,1))]));
    Reject(() => PolylineTools.Join([line,new LineEntity(new(10,0),new(20,0)),new LineEntity(new(10,0),new(10,10))]));
    Reject(() => PolylineTools.Join([line,new LineEntity(new(30,0),new(40,0))]));
});
Test("join rejects a nonplanar intermediate polyline vertex", () => Reject(() => PolylineTools.Join([P(default,new(5,0,5),new(10,0)),new LineEntity(new(10,0),new(20,0))])));
Test("join rejects closed inputs and zero-sweep arcs", () => {
    Reject(() => PolylineTools.Join([P(default,new(10,0),new(10,10)) with {Closed=true},new LineEntity(default,new(-5,0))]));
    Reject(() => PolylineTools.Join([new ArcEntity(default,10,0,360),new LineEntity(new(10,0),new(20,0))]));
});
Test("join command and PEDIT Join use one atomic undo", () => {
    foreach (var inputs in new[]{new[]{"JOIN"},new[]{"PEDIT","JOIN"}}) {
        var s=Session(new LineEntity(default,new(10,0)),new LineEntity(new(10,0),new(20,0))); var before=s.Document.Drawing;
        Inputs(new(s),inputs); Check(s.Document.Drawing.Entities.Single() is PolylineEntity && s.Selection.Count == 1);
        s.Document.Undo(); Check(s.Document.Drawing==before);
    }
});
Test("join locked selection rejects without edits", () => {
    var s=Session(new LineEntity(default,new(10,0)),new LineEntity(new(10,0),new(20,0)));
    s.Document.Edit("lock",d=>d with{Layers=d.Layers.SetItem("0",new("0",Locked:true))});var before=s.Document.Drawing;
    Reject(s.JoinCurves); Check(s.Document.Drawing==before);
});
Test("split straight taper preserves both widths", () => {
    var p = new PolylineEntity([new(default){StartWidth=2,EndWidth=10},new(new(100,0))]);
    var q=PolylineTools.SplitSegment(p,0,.25); Check(q.Id==p.Id && q.Vertices.Length==3); Near(q.Vertices[1].Position.X,25);
    Near(q.Vertices[0].EndWidth,4);Near(q.Vertices[1].StartWidth,4);Near(q.Vertices[1].EndWidth,10);Near(Area(p),Area(q));
});
foreach (var bulge in new[]{1d,-1d,.25,-.5,2d}) Test($"split arc bulge={bulge} remains on original circle", () => {
    var p=new PolylineEntity([new(default,bulge){StartWidth=2,EndWidth=6},new(new(100,0))]);
    var q=PolylineTools.SplitSegment(p,0,.35);
    Near(4*Math.Atan(q.Vertices[0].Bulge)+4*Math.Atan(q.Vertices[1].Bulge),4*Math.Atan(bulge));
    var center=new Vec3(50,100*(1-bulge*bulge)/(4*bulge));var radius=center.Length;
    foreach(var point in EntityGeometry.PolylinePoints(q)) Near(point.DistanceTo(center),radius,1e-6);
});
Test("split closing segment and preserve constant width",()=>{
    var p=PolylineTools.Donut(default,10,20);var q=PolylineTools.SplitSegment(p,1);Check(q.Closed && q.Vertices.Length==3 && q.ConstantWidth==p.ConstantWidth);
    Near(Area(p),Area(q),.05);
});
Test("split rejects dormant last vertex and invalid fractions",()=>{
    var p=P(default,new(10,0));Reject(()=>PolylineTools.SplitSegment(p,1));
    foreach(var t in new[]{0d,1d,double.NaN,double.PositiveInfinity})Reject(()=>PolylineTools.SplitSegment(p,0,t));
});
Test("delete vertex reconnects straight and retains other data",()=>{
    var p=new PolylineEntity([new(default,1){StartWidth=2,EndWidth=4},new(new(5,5),.5){StartWidth=4,EndWidth=8},new(new(10,0)),new(new(20,0))]);
    var q=PolylineTools.DeleteVertex(p,1);Check(q.Vertices.Length==3 && q.Id==p.Id);Near(q.Vertices[0].Bulge,0);Near(q.Vertices[0].StartWidth,2);Near(q.Vertices[0].EndWidth,8);
    Reject(()=>PolylineTools.DeleteVertex(P(default,new(1,0)),0));
});
Test("vertex edits are atomic, stale-safe and OCS-preserving",()=>{
    var p=P(default,new(10,0));var root=new PlacedEntity(p,Coordinates3D.ObjectCoordinateSystem(new(1,2,3)));var s=Session(root);var before=s.Document.Drawing;
    s.EditVertex(root,q=>PolylineTools.SplitSegment(q,0));Check(((PolylineEntity)((PlacedEntity)s.Document.Drawing.Entities[0]).Geometry).Vertices.Length==3);
    Reject(()=>s.EditVertex(root,q=>q));s.Document.Undo();Check(s.Document.Drawing==before);
});
Test("polygon inscribed radius and circumscribed apothem",()=>{
    foreach(var sides in new[]{3,4,6,32,1024}){
        var p=PolylineTools.Polygon(sides,new(2,3,4),10);Check(p.Closed && p.Vertices.Length==sides);
        foreach(var v in p.Vertices)Near(v.Position.DistanceTo(new(2,3,4)),10);
        var q=PolylineTools.Polygon(sides,default,10,true);var middle=(q.Vertices[0].Position+q.Vertices[1].Position)/2;Near(middle.Length,10);
    }
});
Test("polygon rejects invalid numeric inputs",()=>{
    Reject(()=>PolylineTools.Polygon(2,default,1));Reject(()=>PolylineTools.Polygon(1025,default,1));Reject(()=>PolylineTools.Polygon(4,default,0));Reject(()=>PolylineTools.Polygon(4,default,double.NaN));
});
Test("donut ring and filled disc areas",()=>{
    foreach(var inner in new[]{0d,10d}){var p=PolylineTools.Donut(default,inner,20);Check(p.Closed && p.Vertices.Length==2);Near(Area(p),Math.PI*(100-inner*inner/4),.04);}
    Reject(()=>PolylineTools.Donut(default,10,10));Reject(()=>PolylineTools.Donut(default,-1,10));
});
Test("polygon and repeated donut command workflows",()=>{
    var s=new CadSession();var c=new CommandEngine(s);Inputs(c,"POLYGON","6","0,0","I","10","DONUT","4","8","30,0","50,0","");
    Check(s.Document.Drawing.Entities.Length==3 && !c.IsActive);Check(((PolylineEntity)s.Document.Drawing.Entities[0]).Vertices.Length==6);
    s.Document.Undo();Check(s.Document.Drawing.Entities.Length==2);
});
Test("numeric prompt pointer clicks do not corrupt drafting state",()=>{
    var s=new CadSession();var c=new CommandEngine(s);c.Start("POLYGON");c.Point(new(1,2));Inputs(c,"5","0,0","C","10");Check(s.Document.Drawing.Entities.Length==1);
});
Test("PEDIT Edit dispatches the reusable editor without document changes",()=>{
    var s=Session(P(default,new(10,0)));var c=new CommandEngine(s);var request="";c.ViewRequested+=v=>request=v;Inputs(c,"PE","E");Check(request=="POLYLINEEDITOR" && !s.Document.CanUndo);
});
Test("match properties copies styles not geometry, identity, visibility or layout",()=>{
    var source=new CircleEntity(default,5){ColorIndex=1,TrueColor=0xff123456,LinetypeScale=3,LineWeight=.5};var target=new LineEntity(new(50,0),new(100,0)){Handle="AC",Visible=false};var s=Session(source,target);var before=s.Document.Drawing;
    s.MatchProperties(source.Id,[target.Id]);var next=(LineEntity)s.Document.Drawing.Entities[1];
    Check(next.Start==target.Start && next.Id==target.Id && next.Handle==target.Handle && !next.Visible && next.TrueColor==source.TrueColor && next.LinetypeScale==3);
    s.Document.Undo();Check(s.Document.Drawing==before);
});
Test("match properties supports field masks without partial locked edits",()=>{
    var source=new CircleEntity(default,5){ColorIndex=1,LinetypeScale=3};var target=new LineEntity(default,new(100,0)){LinetypeScale=7};var s=Session(source,target);
    s.MatchProperties(source.Id,[target.Id],PropertyMatchFields.Color);Near(s.Document.Drawing.Entities[1].LinetypeScale,7);
    var before=s.Document.Drawing;Reject(()=>s.MatchProperties(source.Id,[target.Id,Guid.NewGuid()]));Check(s.Document.Drawing==before);
    Reject(()=>s.MatchProperties(source.Id,[target.Id],(PropertyMatchFields)128));
});
Test("MATCHPROP uses preselected destinations and picked source",()=>{
    var source=new CircleEntity(default,10){ColorIndex=3};var target=new LineEntity(new(50,0),new(100,0));var s=Session(source,target);s.Select(target.Id);var c=new CommandEngine(s);Inputs(c,"MA","10,0");Check(s.Document.Drawing.Entities[1].ColorIndex==3);
});
Test("MATCHPROP rejects a stale command snapshot",()=>{
    var source=new CircleEntity(default,10){ColorIndex=3};var target=new LineEntity(new(50,0),new(100,0));var s=Session(source,target);s.Select(target.Id);var c=new CommandEngine(s);c.Start("MA");s.Document.Add("Point",new PointEntity(new(5,5)));var before=s.Document.Drawing;c.Point(new(10,0));Check(s.Document.Drawing==before && !c.IsActive);
});

var fixture=Path.Combine(AppContext.BaseDirectory,"fixtures","independent-editing.dxf");
foreach(var binary in new[]{false,true})
{
    Test($"source-preserving geometry and property editing binary={binary}",()=>{
        var imported=DxfBinary.Read(File.ReadAllBytes(fixture));
        var line=imported.Drawing.Entities.OfType<LineEntity>().Single();Check(line.Start==new Vec3(1,2,3));
        var poly=imported.Drawing.Entities.OfType<PolylineEntity>().Single();
        var session=new CadSession(new(imported.Drawing));session.Select(poly.Id);session.SetWidth(8);
        session.Document.Edit("Move line end",d=>d with{Entities=d.Entities.Select(e=>e.Id==line.Id?line with{End=new(60,70,80),ColorIndex=4}:e).ToImmutableArray()});
        var native=CadProjectCodec.Read(CadProjectCodec.Write(session.Document.Drawing,imported.Source));
        var output=DxfBinary.Write(native.Drawing,native.DxfSource,binary);var read=DxfBinary.Read(output.Bytes);
        Check(read.Drawing.Entities.OfType<LineEntity>().Single().End==new Vec3(60,70,80));Near(read.Drawing.Entities.OfType<PolylineEntity>().Single().ConstantWidth,8);
        var ascii=read.Source.Text;Check(ascii.Contains("CS_EDIT") && ascii.Contains("retained application note") && ascii.Contains("retained extended note"));
        Check(ascii.Contains("91\n301\n") && ascii.Contains("91\n302\n"));
        var folder=Environment.GetEnvironmentVariable("CADSPACE_EDITING_OUTPUT");if(folder!=null){Directory.CreateDirectory(folder);File.WriteAllBytes(Path.Combine(folder,binary?"edited-binary.dxf":"edited.dxf"),output.Bytes);}
    });
    Test($"new polygon/donut/join DXF roundtrip binary={binary}",()=>{
        var joined=PolylineTools.Join([new LineEntity(new(-10,0),default),new ArcEntity(new(0,10),10,270,360)]);
        var drawing=Drawing.Empty with{Entities=[joined,PolylineTools.Polygon(6,new(50,50),20),PolylineTools.Donut(new(100,50),10,20)]};
        var output=DxfBinary.Write(drawing,binary:binary);var result=DxfBinary.Read(output.Bytes);Check(result.Drawing.Entities.All(e=>e is PolylineEntity));
        var folder=Environment.GetEnvironmentVariable("CADSPACE_EDITING_OUTPUT");if(folder!=null){Directory.CreateDirectory(folder);File.WriteAllBytes(Path.Combine(folder,binary?"tools-binary.dxf":"tools.dxf"),output.Bytes);}
    });
}
Test("source property edit preserves compound ATTRIB instead of exploding it",()=>{
    var imported=DxfBinary.Read(File.ReadAllBytes(fixture));var insert=imported.Drawing.Entities.OfType<CompositeEntity>().First(e=>e.Kind=="INSERT");
    var next=imported.Drawing with{Entities=imported.Drawing.Entities.Select(e=>e.Id==insert.Id?e with{ColorIndex=4}:e).ToImmutableArray()};
    var output=DxfCodec.Write(next,imported.Source);Check(output.Text.Contains("0\nATTRIB\n") && output.Text.Contains("Part A"));
    Check(!output.Warnings.Any(w=>w.Contains("display geometry")));
});
Test("APPDATA cannot masquerade as geometry or common fields",()=>{
    var text="0\nSECTION\n2\nENTITIES\n0\nLINE\n5\nAB\n102\n{CUSTOM\n8\nFAKE\n10\n999\n20\n999\n30\n999\n102\n}\n8\n0\n10\n1\n20\n2\n30\n3\n11\n4\n21\n5\n31\n6\n0\nENDSEC\n0\nEOF\n";
    var read=DxfCodec.Read(text);var line=(LineEntity)read.Drawing.Entities.Single();Check(line.Layer=="0" && line.Start==new Vec3(1,2,3));
    var output=DxfCodec.Write(read.Drawing with{Entities=[line with{End=new(7,8,9),ColorIndex=2}]},read.Source);
    Check(output.Text.Contains("8\nFAKE\n10\n999\n"));Check(((LineEntity)DxfCodec.Read(output.Text).Drawing.Entities.Single()).End==new Vec3(7,8,9));
});
Test("changed topology reports fallback instead of reattaching vertex identifiers",()=>{
    var imported=DxfBinary.Read(File.ReadAllBytes(fixture));var p=imported.Drawing.Entities.OfType<PolylineEntity>().Single();
    var drawing=imported.Drawing with{Entities=imported.Drawing.Entities.Select(e=>e.Id==p.Id?PolylineTools.SplitSegment(p,0):e).ToImmutableArray()};
    var result=DxfCodec.Write(drawing,imported.Source);Check(result.Warnings.Any(w=>w.Contains("metadata")));
});
Test("indexed join builds 10,000 unordered segments",()=>{
    var lines=Enumerable.Range(0,10000).Select(i=>(Entity)new LineEntity(new(i,0),new(i+1,0))).Reverse().ToArray();
    var watch=System.Diagnostics.Stopwatch.StartNew();var p=(PolylineEntity)PolylineTools.Join(lines);watch.Stop();Check(p.Vertices.Length==10001);
    Console.WriteLine($"BENCH join 10000 segments: {watch.Elapsed.TotalMilliseconds:0.###} ms; geometry count verified; no timing gate.");
});
var failed=0;
foreach(var (name,run) in tests)try{run();Console.WriteLine("PASS "+name);}catch(Exception e){failed++;Console.Error.WriteLine($"FAIL {name}: {e}");}
Console.WriteLine($"{tests.Count-failed}/{tests.Count} drafting/editing regressions passed.");return failed==0?0:1;
