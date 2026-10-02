using CadSpace.Geometry;
using CadSpace.Model;

namespace CadSpace.Engine;

[Flags]
public enum ObjectSnapModes { None = 0, Endpoint = 1, Midpoint = 2, Center = 4, Quadrant = 8, Intersection = 16, Perpendicular = 32, Tangent = 64, Nearest = 128, Default = Endpoint | Midpoint | Center | Quadrant | Intersection | Nearest }

/// <summary>Indexed root-identity snaps, including nested block and OCS placements. Exact fixed anchors precede nearest-curve fallback.</summary>
public sealed class SnapIndex
{
    private readonly record struct Anchor(Vec3 Point, SnapKind Kind, Guid Id, ObjectSnapModes Mode);
    private readonly record struct Segment(Vec3 A, Vec3 B, Guid Id);
    private readonly record struct Circle(Vec3 Center,double Radius,double Start,double Sweep,Transform3 Placement,Guid Id);
    private readonly Circle[] _circles;
    private readonly SpatialIndex _curveBounds;
    private readonly Anchor[] _anchors;
    private readonly Segment[] _segments;
    private readonly SpatialIndex _points, _lines;
    private readonly DrawingScene _scene;
    public SnapIndex(Drawing drawing, DrawingScene scene, string layout)
    {
        _scene = scene; var anchors = new List<Anchor>(); var segments = new List<Segment>(); var circles=new List<Circle>(); var visited = 0;
        void Add(Entity e, Guid root, Transform3 transform, string inheritedLayer, int depth)
        {
            if (++visited > 200000 || depth > 32) throw new ArgumentException("Snap expansion budget exceeded.");
            var layer = e.Layer == "0" ? inheritedLayer : e.Layer;
            if (!e.Visible || !drawing.Layers.GetValueOrDefault(layer, drawing.Layers["0"]).Visible) return;
            void Point(Vec3 p, SnapKind kind, ObjectSnapModes mode) => anchors.Add(new(transform.Point(p), kind, root, mode));
            void Line(Vec3 a, Vec3 b) { Point(a, SnapKind.Endpoint, ObjectSnapModes.Endpoint); Point(b, SnapKind.Endpoint, ObjectSnapModes.Endpoint); Point((a+b)/2, SnapKind.Midpoint, ObjectSnapModes.Midpoint); segments.Add(new(transform.Point(a),transform.Point(b),root)); }
            switch (e)
            {
                case PlacedEntity p: Add(p.Geometry,root,p.Placement.Then(transform),layer,depth+1); break;
                case CompositeEntity c: foreach(var child in c.Children) Add(child,root,transform,layer,depth+1); break;
                case BlockReferenceEntity b when drawing.Blocks.TryGetValue(b.Name,out var block):
                    Point(b.Position,SnapKind.Endpoint,ObjectSnapModes.Endpoint);
                    var t = Transform3.Translation(-block.BasePoint).Then(Transform3.Scaling(b.Scale)).Then(Transform3.RotationZ(b.Rotation)).Then(Transform3.Translation(b.Position)).Then(transform);
                    foreach(var child in block.Entities) Add(child,root,t,layer,depth+1); break;
                case LineEntity l: Line(l.Start,l.End); break;
                case LeaderEntity leader:
                    for (var i = 0; i + 1 < leader.Vertices.Length; i++) Line(leader.Vertices[i], leader.Vertices[i+1]);
                    break;
                case PointEntity p: Point(p.Position,SnapKind.Endpoint,ObjectSnapModes.Endpoint); break;
                case CircleEntity c:
                    circles.Add(new(c.Center,c.Radius,0,360,transform,root));
                    Point(c.Center,SnapKind.Center,ObjectSnapModes.Center);
                    for(var i=0;i<4;i++) Point(GeometryMath.OnCircle(c.Center,c.Radius,i*90),SnapKind.Quadrant,ObjectSnapModes.Quadrant); break;
                case ArcEntity a:
                    circles.Add(new(a.Center,a.Radius,a.StartAngle,GeometryMath.NormalizeAngle(a.EndAngle-a.StartAngle),transform,root));
                    Point(a.Center,SnapKind.Center,ObjectSnapModes.Center);
                    Point(GeometryMath.OnCircle(a.Center,a.Radius,a.StartAngle),SnapKind.Endpoint,ObjectSnapModes.Endpoint);
                    Point(GeometryMath.OnCircle(a.Center,a.Radius,a.EndAngle),SnapKind.Endpoint,ObjectSnapModes.Endpoint);
                    Point(GeometryMath.OnCircle(a.Center,a.Radius,a.StartAngle+GeometryMath.NormalizeAngle(a.EndAngle-a.StartAngle)/2),SnapKind.Midpoint,ObjectSnapModes.Midpoint); break;
                case PolylineEntity p:
                    for(var i=0;i<p.Vertices.Length-(p.Closed?0:1);i++)
                    {
                        var a=p.Vertices[i]; var b=p.Vertices[(i+1)%p.Vertices.Length];
                        if(Math.Abs(a.Bulge)<1e-12) Line(a.Position,b.Position);
                        else { Point(a.Position,SnapKind.Endpoint,ObjectSnapModes.Endpoint); Point(b.Position,SnapKind.Endpoint,ObjectSnapModes.Endpoint); }
                    } break;
                case Polyline3DEntity p: for(var i=0;i<p.Points.Length-(p.Closed?0:1);i++) Line(p.Points[i],p.Points[(i+1)%p.Points.Length]); break;
                case SplineEntity s:
                    Point(AdvancedGeometry.Evaluate(s,s.Knots[s.Degree]),SnapKind.Endpoint,ObjectSnapModes.Endpoint);
                    Point(AdvancedGeometry.Evaluate(s,s.Knots[s.ControlPoints.Length]),SnapKind.Endpoint,ObjectSnapModes.Endpoint); break;
                case EllipseEntity e2: Point(e2.Center,SnapKind.Center,ObjectSnapModes.Center); break;
            }
            if(anchors.Count>2000000) throw new ArgumentException("Snap anchor budget exceeded.");
        }
        foreach(var e in drawing.Entities) if(e.Layout.Equals(layout,StringComparison.OrdinalIgnoreCase)) Add(e,e.Id,Transform3.Identity,e.Layer,0);
        _circles=circles.ToArray();
        _curveBounds=new(_circles.Select(c=>
        {
            var center=c.Placement.Point(c.Center);var x=c.Placement.X*c.Radius;var y=c.Placement.Y*c.Radius;
            var extent=new Vec3(Math.Abs(x.X)+Math.Abs(y.X),Math.Abs(x.Y)+Math.Abs(y.Y),Math.Abs(x.Z)+Math.Abs(y.Z));
            return new Bounds3(center-extent,center+extent);
        }));
        _anchors=anchors.ToArray(); _segments=segments.ToArray();
        _points=new(_anchors.Select(a=>new Bounds3(a.Point,a.Point))); _lines=new(_segments.Select(s=>Bounds3.Empty.Include(s.A).Include(s.B)));
    }
    public SnapResult Find(Vec3 point,double tolerance,Vec3? reference,ObjectSnapModes modes)
    {
        var box=new Bounds3(point-new Vec3(tolerance,tolerance,tolerance),point+new Vec3(tolerance,tolerance,tolerance));
        var result=new SnapResult(point,SnapKind.None); var best=tolerance*tolerance;
        void Candidate(Vec3 p,SnapKind kind,Guid id)
        {
            var d=p-point; var distance=d.Dot(d);
            if(distance<best) {best=distance;result=new(p,kind,id);}
        }
        var candidates=new List<int>(); _points.Query(box,candidates); candidates.Sort();
        foreach(var i in candidates) {var a=_anchors[i];if((modes&a.Mode)!=0) Candidate(a.Point,a.Kind,a.Id);}
        candidates.Clear(); _lines.Query(box,candidates); candidates.Sort();
        if((modes&ObjectSnapModes.Perpendicular)!=0 && reference is Vec3 from)
            foreach(var i in candidates) {var l=_segments[i];var d=l.B-l.A;var length=d.Dot(d);if(length<1e-20)continue;var t=(from-l.A).Dot(d)/length;if(t>=0 && t<=1)Candidate(l.A+d*t,SnapKind.Perpendicular,l.Id);}
        if((modes&ObjectSnapModes.Intersection)!=0 && candidates.Count<=256)
            for(var a=0;a<candidates.Count;a++) for(var b=a+1;b<candidates.Count;b++)
            {
                var x=_segments[candidates[a]];var y=_segments[candidates[b]];
                if(GeometryMath.IntersectLinesXY(x.A,x.B,y.A,y.B,out var hit) && GeometryMath.NearestOnSegment(hit,y.A,y.B).DistanceTo(hit)<1e-7)
                    Candidate(hit,SnapKind.Intersection,x.Id);
            }
        if((modes&ObjectSnapModes.Tangent)!=0 && reference is Vec3 origin)
        {
            var curves=new List<int>(); _curveBounds.Query(box,curves);
            foreach(var i in curves)
            {
                var c=_circles[i];var fromLocal=Coordinates3D.Inverse(c.Placement).Point(origin)-c.Center;
                if(Math.Abs(fromLocal.Z)>1e-7)continue;
                var length2=fromLocal.Dot(fromLocal);if(length2<=c.Radius*c.Radius)continue;
                var a=fromLocal*(c.Radius*c.Radius/length2);var b=new Vec3(-fromLocal.Y,fromLocal.X)*(c.Radius*Math.Sqrt(length2-c.Radius*c.Radius)/length2);
                foreach(var v in new[]{a+b,a-b})
                    if(c.Sweep>=360-1e-8 || GeometryMath.NormalizeAngle(GeometryMath.Angle(v)-c.Start)<=c.Sweep+1e-8)Candidate(c.Placement.Point(c.Center+v),SnapKind.Tangent,c.Id);
            }
        }
        if(result.Kind!=SnapKind.None || (modes&ObjectSnapModes.Nearest)==0) return result;
        candidates.Clear(); SceneAcceleration.For(_scene).Paths.Query(box,candidates); candidates.Sort();
        foreach(var index in candidates)
        {
            var path=_scene.Paths[index];
            for(var i=0;i<path.Points.Length-(path.Closed?0:1);i++) Candidate(GeometryMath.NearestOnSegment(point,path.Points[i],path.Points[(i+1)%path.Points.Length]),SnapKind.Nearest,path.EntityId);
        }
        return result;
    }
}
