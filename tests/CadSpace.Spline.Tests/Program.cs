using System.Collections.Immutable;
using System.Diagnostics;
using CadSpace.Dxf;
using CadSpace.Engine;
using CadSpace.Geometry;
using CadSpace.Model;

var tests = new List<(string Name, Action Run)>();
void Test(string name, Action run) => tests.Add((name, run));
void Check(bool condition, string message = "Assertion failed") { if (!condition) throw new Exception(message); }
void Reject(Action run) { try { run(); } catch (Exception e) when (e is ArgumentException or InvalidOperationException or NotSupportedException) { return; } throw new Exception("Expected rejection"); }
SplineEntity Arc() => new(2, [new(10,0),new(10,10),new(0,10)], [0,0,0,1,1,1], [1,Math.Sqrt(.5),1]);
SplineEntity Curve(int degree, bool rational)
{
    var count = degree + 5;
    var points = Enumerable.Range(0,count).Select(i => new Vec3(i * 4, Math.Sin(i) * 8, Math.Cos(i) * 2)).ToImmutableArray();
    var knots = Enumerable.Range(0,count+degree+1).Select(i => i <= degree ? 0d : i >= count ? 1d : (i-degree)/(double)(count-degree)).ToImmutableArray();
    return new(degree, points, knots, rational ? Enumerable.Range(0,count).Select(i => .2 + i*.13).ToImmutableArray() : []);
}
void Equivalent(SplineEntity a, SplineEntity b)
{
    var lo=a.Knots[a.Degree];var hi=a.Knots[a.ControlPoints.Length];
    for(var i=0;i<=200;i++)
    {
        var u=lo+(hi-lo)*i/200;
        var x=AdvancedGeometry.Evaluate(a,u);var y=AdvancedGeometry.Evaluate(b,u);
        Check(x.DistanceTo(y)<1e-9,$"Curve changed at {u}: {x} vs {y}");
    }
}
CadSession Session(Entity e) { var s=new CadSession(new(Drawing.Empty with{Entities=[e]}));s.Select(e.Id);return s; }
var directory=Environment.GetEnvironmentVariable("CADSPACE_SPLINE_OUTPUT");
void Export(string name,Drawing drawing,DxfSource source,bool binary)
{
    var result=DxfBinary.Write(drawing,source,binary);
    if(directory!=null){Directory.CreateDirectory(directory);File.WriteAllBytes(Path.Combine(directory,name+(binary?"-binary":"")+".dxf"),result.Bytes);}
}
foreach(var degree in new[]{1,2,3,5,8,16})foreach(var rational in new[]{false,true})
    Test($"shape-preserving knot insertion degree={degree}, rational={rational}",()=>{
        var s=Curve(degree,rational);var result=SplineEditing.InsertKnot(s,.37);
        Check(result.Id==s.Id && result.ControlPoints.Length==s.ControlPoints.Length+1 && result.Knots.Length==s.Knots.Length+1);
        Check(result.Weights.IsEmpty==s.Weights.IsEmpty);Equivalent(s,result);
    });
Test("repeated interior knots retain the curve through degree multiplicity",()=>{
    var before=Curve(3,true);var s=SplineEditing.InsertKnot(before,.4);s=SplineEditing.InsertKnot(s,.4);
    Check(s.Knots.Count(x=>x==.4)==3);Equivalent(before,s);Reject(()=>SplineEditing.InsertKnot(s,.4));
});
Test("rational circle remains circular after several different refinements",()=>{
    var original=Arc();var s=original;foreach(var t in new[]{.1,.5,.75,.9})s=SplineEditing.InsertKnot(s,t);
    Equivalent(original,s);for(var i=0;i<=100;i++)Check(Math.Abs(AdvancedGeometry.Evaluate(s,i/100d).Length-10)<1e-10);
});
Test("nonuniform and unclamped parameter domains retain shape",()=>{
    var s=new SplineEntity(2,[new(0,0),new(3,4),new(7,8),new(10,2),new(12,0)],[-5,-3,-1,.2,1.7,4,8,9],[1,2,.5,3,1]);
    Equivalent(s,SplineEditing.InsertKnot(s,1.5));
});
Test("near-endpoint interior refinement retains shape",()=>{
    var s=Arc();foreach(var t in new[]{Math.BitIncrement(0d),1e-12,Math.BitDecrement(1d)})Equivalent(s,SplineEditing.InsertKnot(s,t));
});
Test("insertion rejects endpoints, outside range, NaN and infinity",()=>{
    var s=Arc();foreach(var u in new[]{0d,1d,-1d,2d,double.NaN,double.PositiveInfinity})Reject(()=>SplineEditing.InsertKnot(s,u));
});
Test("periodic curves are not edited without coordinated seam handling",()=>{
    var s=Arc() with{Periodic=true};Reject(()=>SplineEditing.InsertKnot(s,.5));Reject(()=>SplineEditing.SetControlPoint(s,1,new(3,3),2));
});
Test("closed clamped endpoints cannot be detached by a single point edit",()=>{
    var s=Curve(3,false);s=s with{Closed=true,ControlPoints=s.ControlPoints.SetItem(s.ControlPoints.Length-1,s.ControlPoints[0])};
    Reject(()=>SplineEditing.SetControlPoint(s,0,new(99,0),1));Reject(()=>SplineEditing.SetControlPoint(s,s.ControlPoints.Length-1,new(99,0),1));
    Equivalent(s,SplineEditing.InsertKnot(s,.5));
});
Test("homogeneous insertion avoids weighted-position and knot-difference overflow",()=>{
    var s=new SplineEntity(1,[new(-1e200,0),new(1e200,0)],[-1e308,-1e308,1e308,1e308],[1e200,1e200]);
    var q=SplineEditing.InsertKnot(s,0);Check(q.ControlPoints[1].IsFinite && q.ControlPoints[1].X==0 && double.IsFinite(q.Weights[1]));
});
Test("setting a weight materializes defaults and retains other weights",()=>{
    var s=Curve(3,false);var q=SplineEditing.SetControlPoint(s,2,new(2,9),3);
    Check(q.ControlPoints[2]==new Vec3(2,9) && q.Weights.Length==s.ControlPoints.Length && q.Weights[2]==3 && q.Weights[1]==1);
    Check(q.Id==s.Id && q.Knots==s.Knots && q.Degree==s.Degree);
});
Test("unchanged point and weight preserve exact reference identity",()=>{
    var s=Arc();Check(ReferenceEquals(s,SplineEditing.SetControlPoint(s,1,s.ControlPoints[1],s.Weights[1])));
});
Test("invalid point weights and indices reject atomically",()=>{
    var s=Arc();foreach(var w in new[]{0d,-1,double.NaN,double.PositiveInfinity})Reject(()=>SplineEditing.SetControlPoint(s,1,default,w));
    Reject(()=>SplineEditing.SetControlPoint(s,-1,default,1));Reject(()=>SplineEditing.SetControlPoint(s,3,default,1));
    Reject(()=>SplineEditing.SetControlPoint(s,1,new(double.NaN,0),1));
});
Test("placed spline edits retain full root and leaf identities and frame",()=>{
    var s=Arc();var root=new PlacedEntity(s,Transform3.Scaling(new(2,3,1)).Then(Transform3.Translation(new(5,6,7)))){Handle="AB",ColorIndex=4};
    var session=Session(root);var before=session.Document.Drawing;
    session.EditSpline(root,p=>SplineEditing.InsertKnot(p,.5));var after=(PlacedEntity)session.Document.Drawing.Entities[0];
    Check(after.Placement==root.Placement && after.Id==root.Id && after.Geometry.Id==s.Id && after.Handle==root.Handle && after.ColorIndex==4);
    session.Document.Undo();Check(ReferenceEquals(before,session.Document.Drawing));session.Document.Redo();Check(SplineEditing.Unwrap(session.Document.Drawing.Entities[0])!.ControlPoints.Length==4);
});
Test("stale, wrong selection, lock and changed identity reject edits",()=>{
    var s=Arc();var session=Session(s);session.EditSpline(s,p=>SplineEditing.InsertKnot(p,.5));
    Reject(()=>session.EditSpline(s,p=>p));var current=session.Document.Drawing.Entities[0];
    Reject(()=>session.EditSpline(current,p=>p with{Id=Guid.NewGuid()}));session.Select(null);Reject(()=>session.EditSpline(current,p=>p));
    session.Select(current.Id);session.Document.Edit("Lock",d=>d with{Layers=d.Layers.SetItem("0",new("0",Locked:true))});Reject(()=>session.EditSpline(current,p=>p));
});
Test("no-op spline edit preserves clean state and redo",()=>{
    var s=Arc();var session=Session(s);session.EditSpline(s,p=>SplineEditing.InsertKnot(p,.5));session.Document.Undo();
    var before=session.Document.Drawing;session.EditSpline(s,p=>SplineEditing.SetControlPoint(p,1,p.ControlPoints[1],p.Weights[1]));
    Check(ReferenceEquals(before,session.Document.Drawing) && session.Document.CanRedo && !session.Document.IsDirty);
});
Test("SPLINEDIT preselection and alias finish command before editor dispatch",()=>{
    foreach(var name in new[]{"SPLINEDIT","SPE"}){
        var session=Session(Arc());var c=new CommandEngine(session);var request="";
        c.ViewRequested+=s=>{Check(!c.IsActive);request=s;};c.Start(name);Check(request=="SPLINEEDITOR" && !session.Document.CanUndo);
    }
});
Test("SPLINEDIT point selection and cancellation",()=>{
    var s=Arc();var session=Session(s);session.Select(null);var c=new CommandEngine(session);var request="";c.ViewRequested+=s=>request=s;
    c.Start("SPE");Check(c.IsActive);c.Submit("10,0");Check(request=="SPLINEEDITOR" && session.Selection.Contains(s.Id));
    session.Select(null);c.Start("SPE");c.Cancel();Check(!c.IsActive && !session.Document.CanUndo);
});
Test("SPLINEDIT rejects unrelated and multiple selected objects",()=>{
    var session=Session(new LineEntity(default,Vec3.UnitX));var c=new CommandEngine(session);var request="";c.ViewRequested+=s=>request=s;
    c.Start("SPE");Check(!c.IsActive && request=="");session.Document.Add("Curve",Arc());session.SelectAll();c.Start("SPE");Check(!c.IsActive && request=="");
});
var path=Path.Combine(AppContext.BaseDirectory,"fixtures","independent-spline.dxf");
foreach(var binary in new[]{false,true})
{
    Test($"source-backed knot and weight edits preserve native data binary={binary}",()=>{
        var input=DxfBinary.Read(File.ReadAllBytes(path));var before=(SplineEntity)input.Drawing.Entities.Single();
        var refined=SplineEditing.InsertKnot(before,.5);var d=input.Drawing with{Entities=[refined]};Export("spline-refined",d,input.Source,binary);
        var edited=SplineEditing.SetControlPoint(refined,1,new(12,6),2) with{ColorIndex=4};d=d with{Entities=[edited]};
        var native=CadProjectCodec.Read(CadProjectCodec.Write(d,input.Source));var result=DxfBinary.Write(native.Drawing,native.DxfSource,binary);
        var next=DxfBinary.Read(result.Bytes);var s=(SplineEntity)next.Drawing.Entities.Single();
        Check(s.ControlPoints[1]==new Vec3(12,6) && s.Weights[1]==2 && s.ColorIndex==4);
        Check(next.Source.Text.Contains("keep spline application data") && next.Source.Text.Contains("keep spline extended data"));
        Check(result.Warnings.Any(w=>w.Contains("unedited fields retained")) && !result.Warnings.Any(w=>w.Contains("metadata may")));
        Export("spline-edited",d,input.Source,binary);
    });
}
Test("source planar metadata is not reused after a nonplanar deformation",()=>{
    var input=DxfBinary.Read(File.ReadAllBytes(path));var s=(SplineEntity)input.Drawing.Entities.Single();
    var changed=SplineEditing.SetControlPoint(s,1,new(10,10,8),s.Weights[1]);var result=DxfCodec.Write(input.Drawing with{Entities=[changed]},input.Source);
    Check(result.Warnings.Any(w=>w.Contains("metadata")));var next=(SplineEntity)DxfCodec.Read(result.Text).Drawing.Entities.Single();Check(next.ControlPoints[1].Z==8);
});
foreach(var code in new[]{11,12}) Test($"fit-point/tangent group {code} does not retain stale geometric constraints",()=>{
    var input=DxfBinary.Read(File.ReadAllBytes(path));var source=input.Source.Text;
    var pairs=DxfCodec.ParsePairs(source).ToBuilder();
    var i=Enumerable.Range(0,pairs.Count).First(n=>pairs[n].Code==100 && pairs[n].Value=="AcDbSpline");
    pairs.Insert(i+1,new(code,"1"));pairs.Insert(i+2,new(code+10,"0"));pairs.Insert(i+3,new(code+20,"0"));
    input=DxfCodec.Read(string.Concat(pairs.Select(p=>$"{p.Code}\n{p.Value}\n")));var s=(SplineEntity)input.Drawing.Entities.Single();
    var result=DxfCodec.Write(input.Drawing with{Entities=[SplineEditing.InsertKnot(s,.5)]},input.Source);
    Check(result.Warnings.Any(w=>w.Contains("metadata")));
    Check(!DxfCodec.Read(result.Text).Source.Records.Values.Single().Any(p=>p.Code==code));
});
Test("overflowing source-plane checks take explicit conversion fallback",()=>{
    var input=DxfBinary.Read(File.ReadAllBytes(path));var spline=(SplineEntity)input.Drawing.Entities.Single();
    var edited=SplineEditing.SetControlPoint(spline,1,new(1e200,0,1),1);
    var output=DxfCodec.Write(input.Drawing with{Entities=[edited]},input.Source);
    Check(output.Warnings.Any(w=>w.Contains("metadata")));
    Check(((SplineEntity)DxfCodec.Read(output.Text).Drawing.Entities.Single()).ControlPoints[1].Z==1);
});
Test("marker-free spline geometry patch preserves application groups",()=>{
    var input=DxfBinary.Read(File.ReadAllBytes(path));var pairs=DxfCodec.ParsePairs(input.Source.Text).Where(p=>p.Code!=100);
    input=DxfCodec.Read(string.Concat(pairs.Select(p=>$"{p.Code}\n{p.Value}\n")));var s=(SplineEntity)input.Drawing.Entities.Single();
    var result=DxfCodec.Write(input.Drawing with{Entities=[SplineEditing.InsertKnot(s,.5)]},input.Source);
    Check(result.Text.Contains("keep spline application data"));Check(((SplineEntity)DxfCodec.Read(result.Text).Drawing.Entities.Single()).ControlPoints.Length==4);
});
Test("100,000 point budget is enforced before refinement allocation",()=>{
    var count=100000;var s=new SplineEntity(1,Enumerable.Range(0,count).Select(i=>new Vec3(i,0)).ToImmutableArray(),Enumerable.Range(0,count+2).Select(i=>(double)i).ToImmutableArray(),[]);
    Reject(()=>SplineEditing.InsertKnot(s,100.5));
});
Test("large insertion only adds one point and one knot",()=>{
    var count=20000;var s=new SplineEntity(3,Enumerable.Range(0,count).Select(i=>new Vec3(i,Math.Sin(i*.01))).ToImmutableArray(),Enumerable.Range(0,count+4).Select(i=>(double)i).ToImmutableArray(),[]);
    var watch=Stopwatch.StartNew();var result=SplineEditing.InsertKnot(s,10000.5);watch.Stop();Check(result.ControlPoints.Length==count+1 && result.Knots.Length==count+5);
    for(var i=0;i<100;i++)Check(s.ControlPoints[i]==result.ControlPoints[i]);
    Console.WriteLine($"BENCH 20000-control-point knot insertion: {watch.Elapsed.TotalMilliseconds:0.###} ms; count verified, no timing gate.");
});
var failed=0;foreach(var (name,run) in tests)try{run();Console.WriteLine("PASS "+name);}catch(Exception e){failed++;Console.Error.WriteLine($"FAIL {name}: {e}");}
Console.WriteLine($"{tests.Count-failed}/{tests.Count} spline regressions passed.");return failed==0?0:1;
