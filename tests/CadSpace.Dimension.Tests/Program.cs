using System.Collections.Immutable;
using System.Diagnostics;
using System.Text.Json.Nodes;
using CadSpace.Dxf;
using CadSpace.Engine;
using CadSpace.Geometry;
using CadSpace.Model;

var tests = new List<(string Name, Action Run)>();
void Test(string name, Action run) => tests.Add((name, run));
void Check(bool condition, string message = "Assertion failed") { if (!condition) throw new Exception(message); }
void Near(double a, double b) => Check(Math.Abs(a-b)<1e-6,$"Expected {b}, got {a}");
void Reject(Action a) { try { a(); } catch(Exception e) when(e is ArgumentException or InvalidOperationException or NotSupportedException or FormatException) { return; } throw new Exception("Expected rejection"); }
DimensionEntity[] Definitions() => [
    new(default,new(80,10),new(30,20)){Type=DimensionKind.Rotated},
    new(default,new(60,80),new(-16,12)){Type=DimensionKind.Aligned},
    new(default,new(60,0),new(30,30)){Type=DimensionKind.Angular2Line,Third=default,Fourth=new(0,60)},
    new(new(10,20),new(30,20),new(40,20)){Type=DimensionKind.Diameter},
    new(new(20,20),new(30,20),new(40,20)){Type=DimensionKind.Radius},
    new(new(60,0),new(0,60),new(30,30)){Type=DimensionKind.Angular3Point,Third=default},
    new(default,new(30,40),new(30,60)){Type=DimensionKind.Ordinate,OrdinateX=true},
    new(default,new(30,40),new(60,40)){Type=DimensionKind.Ordinate,OrdinateX=false}
];
var measurements = new[]{80d,100,90,20,10,90,30,40};
CadSession Session(Entity d) { var s=new CadSession(new(Drawing.Empty with{Entities=[d]}));s.Select(d.Id);return s; }
void Inputs(CommandEngine c,params string[] inputs){foreach(var i in inputs)c.Submit(i);}
var output=Environment.GetEnvironmentVariable("CADSPACE_DIMENSION_OUTPUT");
void Export(string name,Drawing d,DxfSource? source,bool binary) {var bytes=DxfBinary.Write(d,source,binary).Bytes;if(output!=null){Directory.CreateDirectory(output);File.WriteAllBytes(Path.Combine(output,name+(binary?"-binary":"")+".dxf"),bytes);}}
for(var i=0;i<8;i++)
{
    var index=i;
    Test($"definition measurement and drawing geometry {i}",()=>{
        var d=Definitions()[index];DimensionGeometry.Validate(d);Near(DimensionGeometry.Measure(d),measurements[index]);
        var scene=EntityGeometry.BuildScene(Drawing.Empty with{Entities=[d]});Check(!scene.Paths.IsEmpty && scene.Texts.Length==1);Check(scene.Paths.All(p=>p.EntityId==d.Id));
        if(d.Type!=DimensionKind.Ordinate)Check(scene.Triangles.Length>=1);
    });
    Test($"native v3 identity formatting and geometry {i}",()=>{
        var d=Definitions()[index] with{TextOverride="Size <>",Format=DimensionFormat.Default with{Precision=3,TextHeight=4}};
        var text=CadProjectCodec.Write(Drawing.Empty with{Entities=[d]});Check(text.Contains("\"version\": 3"));
        var read=CadProjectCodec.Read(text);Check(read.Drawing.Entities.Single()==d);Check(CadProjectCodec.Write(read.Drawing)==text);
    });
    foreach(var binary in new[]{false,true}) Test($"native dimension roundtrip {i} binary={binary}",()=>{
        var d=Definitions()[index];var drawing=Drawing.Empty with{Entities=[d]};var bytes=DxfBinary.Write(drawing,binary:binary);
        Check(bytes.Warnings.IsEmpty,string.Join(";",bytes.Warnings));var read=DxfBinary.Read(bytes.Bytes);var next=DimensionGeometry.Unwrap(read.Drawing.Entities.Single());
        Check(next!=null);Check(next!.Type==d.Type);Near(DimensionGeometry.Measure(next),DimensionGeometry.Measure(d));
        Check(DimensionGeometry.UsesPicture(next));Check(read.Drawing.Blocks.ContainsKey(next.Picture!.BlockName));
        var project=CadProjectCodec.Read(CadProjectCodec.Write(read.Drawing,read.Source));Check(DxfBinary.Write(project.Drawing,project.DxfSource,binary).Bytes.SequenceEqual(bytes.Bytes));
        var session=new CadSession(new(read.Drawing));var root=read.Drawing.Entities[0];session.Select(root.Id);
        session.SetDimensionProperties(root,next.Format,"Edited <>",next.Location,next.Rotation);Check(!DimensionGeometry.UsesPicture(DimensionGeometry.Unwrap(session.Document.Drawing.Entities[0])!));
        var edited=DxfBinary.Read(DxfBinary.Write(session.Document.Drawing,read.Source,binary).Bytes);Check(DimensionGeometry.Unwrap(edited.Drawing.Entities[0])!.TextOverride=="Edited <>");
    });
}
foreach(var binary in new[]{false,true})
{
    Test($"all subtypes external audit output binary={binary}",()=>{
        var drawing=Drawing.Empty with{Entities=Definitions().Cast<Entity>().ToImmutableArray()};Export("dimensions-new",drawing,null,binary);
        var nested=Drawing.Empty with{Blocks=Drawing.Empty.Blocks.Add("Dimensions",new("Dimensions",default,drawing.Entities)),Entities=[new BlockReferenceEntity("Dimensions",new(200,100),new(1,1,1))]};
        Export("dimensions-nested",nested,null,binary);
    });
    foreach(var tilted in new[]{false,true})Test($"independent imported definitions and edited output tilted={tilted} binary={binary}",()=>{
        var read=DxfBinary.Read(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory,"fixtures",tilted?"independent-dimensions-tilted.dxf":"independent-dimensions.dxf")));
        Check(read.Drawing.Entities.Length==8,string.Join(";",read.Warnings));
        for(var i=0;i<8;i++){
            var d=DimensionGeometry.Unwrap(read.Drawing.Entities[i]);Check(d!=null,$"Dimension {i} not typed: {string.Join(";",read.Warnings)}");
            Near(DimensionGeometry.Measure(d!),measurements[i]);Check(DimensionGeometry.UsesPicture(d!));Near(d!.Format.TextHeight,3);Check(d.Format.Precision==3);
        }
        var originalScene=EntityGeometry.BuildScene(read.Drawing);Check(originalScene.Paths.Length>20 && originalScene.Texts.Length>=8);
        var s=new CadSession(new(read.Drawing));var first=read.Drawing.Entities[0];var dim=DimensionGeometry.Unwrap(first)!;s.Select(first.Id);
        s.SetDimensionProperties(first,dim.Format with{TextHeight=4},"Edited <>",dim.Location,dim.Rotation);
        var project=CadProjectCodec.Read(CadProjectCodec.Write(s.Document.Drawing,read.Source));
        Export(tilted?"dimensions-tilted":"dimensions-imported",project.Drawing,project.DxfSource,binary);
        s.Document.Undo();Check(s.Document.Drawing==read.Drawing);
    });
}
Test("dimension picture cache invalidates on definition edit and reuses on Undo",()=>{
    var d=Definitions()[0];var picture=new BlockDefinition("*D1",default,[new LineEntity(default,new(999,0))]);
    d=d with{Picture=new("*D1",DimensionGeometry.Signature(d),default)};
    var drawing=Drawing.Empty with{Entities=[d],Blocks=Drawing.Empty.Blocks.Add(picture.Name,picture)};
    Check(EntityGeometry.BuildScene(drawing).Paths.Single().Points[^1].X==999);
    var changed=d with{Second=new(150,10)};Check(!DimensionGeometry.UsesPicture(changed));
    Check(EntityGeometry.BuildScene(drawing with{Entities=[changed]}).Paths.Length>1);
    Check(DimensionGeometry.UsesPicture(d));
});
Test("scene cache tracks retained dimension picture block dependencies",()=>{
    var d=Definitions()[0];d=d with{Picture=new("*D1",DimensionGeometry.Signature(d),default)};
    var drawing=Drawing.Empty with{Entities=[d],Blocks=Drawing.Empty.Blocks.Add("*D1",new("*D1",default,[new LineEntity(default,new(50,0))]))};
    var cache=new DrawingSceneCache();cache.Build(drawing);
    var scene=cache.Build(drawing with{Blocks=drawing.Blocks.SetItem("*D1",new("*D1",default,[new LineEntity(default,new(90,0))]))});
    Check(cache.RebuiltRoots==1 && scene.Paths.Single().Points[^1].X==90);
});
Test("cyclic dimension picture references are rejected",()=>{
    var d=Definitions()[0];d=d with{Picture=new("Loop",DimensionGeometry.Signature(d),default)};
    Reject(()=>new CadDocument(Drawing.Empty with{Blocks=Drawing.Empty.Blocks.Add("Loop",new("Loop",default,[d]))}));
});
Test("missing retained picture uses definition geometry",()=>{
    var d=Definitions()[0];d=d with{Picture=new("missing",DimensionGeometry.Signature(d),default)};
    Check(EntityGeometry.BuildScene(Drawing.Empty with{Entities=[d]}).Texts.Length==1);
});
Test("formatting measurement templates labels and suppression",()=>{
    var d=Definitions()[1] with{Format=DimensionFormat.Default with{Precision=3,SuppressTrailingZeros=false,MeasurementScale=2,TextTemplate="Length <> mm"}};
    Check(DimensionGeometry.Label(d)=="Length 200.000 mm");Check(DimensionGeometry.Label(d with{TextOverride="Custom <>"})=="Custom 200.000");
    Check(DimensionGeometry.For(d with{TextOverride=" "}).Entities.All(e=>e is not TextEntity));
    Check(DimensionGeometry.Label(Definitions()[4]).StartsWith("R"));Check(DimensionGeometry.Label(Definitions()[3]).StartsWith("Ø"));
    Check(DimensionGeometry.Label(Definitions()[5] with{Format=d.Format}).Contains("90.00°"));
});
Test("negative ordinate coordinates measure a positive length",()=>Near(DimensionGeometry.Measure(Definitions()[6] with{Second=new(-30,-40)}),30));
Test("two-line angular sectors and endpoint reversal",()=>{
    var d=new DimensionEntity(default,new(10,0),new(4,2)){Type=DimensionKind.Angular2Line,Third=default,Fourth=new(10,10)};
    foreach(var (location,angle) in new[]{(new Vec3(4,2),45d),(new Vec3(-2,4),135d),(new Vec3(-4,-2),45d),(new Vec3(2,-4),135d)}){
        var a=d with{Location=location};Near(DimensionGeometry.Measure(a),angle);Near(DimensionGeometry.Measure(a with{First=a.Second,Second=a.First}),angle);
    }
});
Test("three-point reflex angle follows arc location",()=>{
    var d=Definitions()[5];Near(DimensionGeometry.Measure(d),90);Near(DimensionGeometry.Measure(d with{Location=new(-20,-20)}),270);
});
Test("degenerate angular definitions fail atomically",()=>{
    var d=Definitions()[2];Reject(()=>DimensionGeometry.For(d with{Third=new(0,10),Fourth=new(60,10)}));
    Reject(()=>DimensionGeometry.For(Definitions()[5] with{Third=new(60,0)}));
    Reject(()=>DimensionGeometry.For(d with{Location=default}));
});
Test("invalid dimension formats rejected",()=>{
    var d=Definitions()[0];foreach(var f in new[]{d.Format with{TextHeight=0},d.Format with{Scale=double.NaN},d.Format with{ArrowSize=-1},d.Format with{Precision=9},d.Format with{TextTemplate="a\n"},d.Format with{TextTemplate="\ud800"}})Reject(()=>new CadDocument(Drawing.Empty with{Entities=[d with{Format=f}]}));
    Reject(()=>new CadDocument(Drawing.Empty with{Entities=[d with{Location=new(0,0,1)}]}));
});
Test("grips include angular definitions and custom text points",()=>{
    var d=Definitions()[2] with{TextPosition=new(40,40)};Check(GripEditing.Grips(d).Length==6);
    var next=(DimensionEntity)GripEditing.Move(d,3,new(-20,0));Check(next.Third==new Vec3(-20,0));
    next=(DimensionEntity)GripEditing.Move(d,5,new(50,50));Check(next.TextPosition==new Vec3(50,50));
    Reject(()=>GripEditing.Move(d,3,new(0,0,10)));
});
Test("placed grips and stale-safe formatting retain OCS",()=>{
    var t=Coordinates3D.ObjectCoordinateSystem(new(1,2,3));var d=Definitions()[0];var root=new PlacedEntity(d,t);var s=Session(root);
    var target=t.Point(new(100,10));s.MoveGrip(root,1,target);var next=(PlacedEntity)s.Document.Drawing.Entities[0];Near(((DimensionEntity)next.Geometry).Second.DistanceTo(new(100,10)),0);Check(next.Placement==t);
    Reject(()=>s.SetDimensionProperties(root,d.Format,"Stale",d.Location,0));s.Document.Undo();Check(s.Document.Drawing.Entities[0]==root);
});
Test("no-op dimension formatting preserves clean snapshot and redo",()=>{
    var d=Definitions()[0];var s=Session(d);s.SetDimensionProperties(d,d.Format,"Edited",d.Location,d.Rotation);s.Document.Undo();var before=s.Document.Drawing;
    s.SetDimensionProperties(d,d.Format,d.TextOverride,d.Location,d.Rotation);Check(ReferenceEquals(before,s.Document.Drawing)&&s.Document.CanRedo&&!s.Document.IsDirty);
});
Test("locked dimension edits do not change document",()=>{
    var d=Definitions()[0];var s=Session(d);s.Document.Edit("Lock",x=>x with{Layers=x.Layers.SetItem("0",new("0",Locked:true))});var before=s.Document.Drawing;
    Reject(()=>s.SetDimensionProperties(d,d.Format,"No",d.Location,0));Check(s.Document.Drawing==before);
});
Test("similarity transform scales measurements and text sizes",()=>{
    var d=Definitions()[0];var t=Transform3.Scaling(new(2,2,2)).Then(Transform3.RotationZ(30)).Then(Transform3.Translation(new(3,4)));
    var moved=(DimensionEntity)EntityGeometry.Transform(d,t);Near(DimensionGeometry.Measure(moved),160);Near(moved.Format.Scale,2);Near(moved.Rotation,30);Check(moved.Id==d.Id);
});
Test("tilted and reflected dimensions remain exportable; shear rejects explicitly",()=>{
    var d=Definitions()[1];foreach(var t in new[]{Coordinates3D.ObjectCoordinateSystem(new(1,2,3)),Transform3.Scaling(new(-1,1,1))}){
        var moved=EntityGeometry.Transform(d,t);var read=DxfCodec.Read(DxfCodec.Write(Drawing.Empty with{Entities=[moved]}).Text);var next=DimensionGeometry.Unwrap(read.Drawing.Entities[0]);Check(next!=null);Near(DimensionGeometry.Measure(next!),100);
    }
    Reject(()=>DxfCodec.Write(Drawing.Empty with{Entities=[new PlacedEntity(d,Transform3.Scaling(new(2,1,1)))]}));
});
Test("native source provenance refuses forged definition and picture",()=>{
    var read=DxfCodec.Read(DxfCodec.Write(Drawing.Empty with{Entities=[Definitions()[0]]}).Text);
    var json=JsonNode.Parse(CadProjectCodec.Write(read.Drawing,read.Source))!;
    json["sourceGraph"]!["entities"]![0]!["dimensionFormat"]!["textHeight"]=99;
    Reject(()=>CadProjectCodec.Read(json.ToJsonString()));
});
Test("v2 plain aligned dimensions remain readable",()=>{
    var json=JsonNode.Parse(CadProjectCodec.Write(Drawing.Empty with{Entities=[Definitions()[1]]}))!;json["version"]=2;
    var e=json["drawing"]!["entities"]![0]!.AsObject();foreach(var field in new[]{"dimensionType","dimensionFormat","textOverride","third","fourth","ordinateX","rotation"})e.Remove(field);
    Check(((DimensionEntity)CadProjectCodec.Read(json.ToJsonString()).Drawing.Entities[0]).Type==DimensionKind.Aligned);
});
Test("dimension editor command dispatches without pending point state",()=>{
    var d=Definitions()[0];var s=Session(d);var c=new CommandEngine(s);var request="";c.ViewRequested+=r=>{Check(!c.IsActive);request=r;};c.Start("DIMEDIT");Check(request=="DIMENSIONEDITOR"&&!s.Document.CanUndo);
});
Test("all new dimension command workflows create real modeled objects",()=>{
    string[][] inputs=[ ["DLI","0,0","80,10","30,20"],["DROT","45","0,0","60,80","30,20"],["DAN","0,0","60,0","0,60","30,30"],["DA2","0,0","60,0","0,0","0,60","30,30"],["DOR","X","0,0","30,40","30,60"], ["DRA","10,0","30,0"],["DDI","10,0","30,0"] ];
    foreach(var input in inputs){var s=new CadSession();if(input[0] is "DRA" or "DDI")s.Add("Circle",new CircleEntity(default,10));var c=new CommandEngine(s);Inputs(c,input);Check(s.Document.Drawing.Entities.Last() is DimensionEntity,string.Join(" ",input));s.Document.Undo();Check(!s.Document.Drawing.Entities.Any(e=>e is DimensionEntity));}
});
Test("dimension command previews are transient; numeric pointer clicks do not advance",()=>{
    var s=new CadSession();var c=new CommandEngine(s);c.Start("DIMROTATED");c.Point(new(100,100));Inputs(c,"30","0,0","50,0");var preview=c.Preview(new(25,20));Check(preview.Single() is DimensionEntity&&!s.Document.CanUndo);c.Cancel();Check(!s.Document.CanUndo);
});
Test("radial stale source and invalid definition commands fail without partial edits",()=>{
    var circle=new CircleEntity(default,10);var s=Session(circle);var c=new CommandEngine(s);c.Start("DIMRADIUS");s.Document.Edit("Resize",d=>d with{Entities=[circle with{Radius=20}]});var before=s.Document.Drawing;c.Point(new(30,0));Check(s.Document.Drawing==before&&!c.IsActive);
    Inputs(c,"DAN","0,0","0,0","10,0","5,5");Check(s.Document.Drawing==before);
});
Test("cached dimension layout publishes once and avoids stale record-copy data",()=>{
    var d=Definitions()[0];var first=DimensionGeometry.For(d);var layouts=new DimensionLayout[100];Parallel.For(0,100,i=>layouts[i]=DimensionGeometry.For(d));Check(layouts.All(l=>ReferenceEquals(l,first)));
    Check(!ReferenceEquals(first,DimensionGeometry.For(d with{TextOverride="Changed"})));Check(DimensionGeometry.Signature(d)!=DimensionGeometry.Signature(d with{Second=new(90,10)}));
});
Test("bounded dimension batch export shares styles and unique picture names",()=>{
    var d=Definitions()[0];var drawing=Drawing.Empty with{Entities=Enumerable.Range(0,1000).Select(i=>(Entity)(d with{Id=Guid.NewGuid()})).ToImmutableArray()};
    var timer=Stopwatch.StartNew();var exported=DxfCodec.Write(drawing);timer.Stop();var parsed=DxfCodec.Read(exported.Text);
    Check(parsed.Drawing.Entities.Length==1000);var names=parsed.Drawing.Entities.Select(e=>DimensionGeometry.Unwrap(e)!.Picture!.BlockName).ToHashSet();Check(names.Count==1000);
    var styles=parsed.Source.Sections.Single(s=>s.Name=="TABLES").Pairs.Count(p=>p.Code==0&&p.Value=="DIMSTYLE");Check(styles==1);
    Console.WriteLine($"BENCH 1000 native dimension exports: {timer.Elapsed.TotalMilliseconds:0.###} ms; 1000 unique pictures / 1 shared style; no timing gate.");
});
Test("legacy dimension picture provenance remains readable without accepting a forged block",()=>{
    var read=DxfCodec.Read(DxfCodec.Write(Drawing.Empty with{Entities=[Definitions()[0]]}).Text);
    var root=read.Drawing.Entities[0];var dim=DimensionGeometry.Unwrap(root)!;
    var raw=string.Concat(read.Source.Records[root.Id].Select(p=>$"{p.Code}\n{p.Value}\n"));
    var legacy=new CompositeEntity("DIMENSION",[new BlockReferenceEntity(dim.Picture!.BlockName,default,new(1,1,1))],raw)
        {Id=root.Id,Handle=root.Handle,Layer=root.Layer,ColorIndex=root.ColorIndex,TrueColor=root.TrueColor,LineWeight=root.LineWeight,Linetype=root.Linetype,LinetypeScale=root.LinetypeScale,Layout=root.Layout};
    var oldDrawing=read.Drawing with{Entities=[legacy]};var oldSource=read.Source with{Original=oldDrawing};
    var saved=CadProjectCodec.Write(oldDrawing,oldSource);Check(saved.Contains("\"version\": 2"));
    var restored=CadProjectCodec.Read(saved);Check(restored.Drawing.Entities[0] is CompositeEntity);
    Check(DxfCodec.Write(restored.Drawing,restored.DxfSource).Text==read.Source.Text);
    CadProjectCodec.Read(CadProjectCodec.Write(restored.Drawing,restored.DxfSource));
    var json=JsonNode.Parse(saved)!;json["sourceGraph"]!["entities"]![0]!["children"]![0]!["name"]="Forged";
    Reject(()=>CadProjectCodec.Read(json.ToJsonString()));
});
Test("legacy aligned definition without picture retains validated provenance",()=>{
    const string raw="0\nSECTION\n2\nENTITIES\n0\nDIMENSION\n5\nAB\n70\n1\n10\n0\n20\n20\n13\n0\n23\n0\n14\n100\n24\n0\n0\nENDSEC\n0\nEOF\n";
    var read=DxfCodec.Read(raw);var current=(DimensionEntity)read.Drawing.Entities[0];
    var legacy=current with{Format=DimensionFormat.Default};var drawing=read.Drawing with{Entities=[legacy]};
    var source=read.Source with{Original=drawing};var json=JsonNode.Parse(CadProjectCodec.Write(drawing,source))!;json["version"]=2;
    foreach(var key in new[]{"drawing","sourceGraph"}){var e=json[key]!["entities"]![0]!.AsObject();foreach(var name in new[]{"dimensionType","dimensionFormat","textOverride","third","fourth","ordinateX","rotation"})e.Remove(name);}
    var restored=CadProjectCodec.Read(json.ToJsonString());Check(DxfCodec.Write(restored.Drawing,restored.DxfSource).Text==raw);
});
var failed=0;foreach(var (name,run) in tests)try{run();Console.WriteLine("PASS "+name);}catch(Exception e){failed++;Console.Error.WriteLine("FAIL "+name+": "+e);}
Console.WriteLine($"{tests.Count-failed}/{tests.Count} dimension tests passed.");return failed==0?0:1;
