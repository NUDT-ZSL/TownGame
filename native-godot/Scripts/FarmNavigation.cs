using Godot;
using System;
using System.Collections.Generic;

// One-metre local A*: check the actual wall colliders above ground rather than
// sending a worker in a straight line through the animal houses.
public sealed class FarmNavigation
{
    readonly Main _town;
    readonly Dictionary<Vector2I,bool> _clear=new();
    readonly BoxShape3D _body=new(){Size=new(.72f,1.65f,.72f)};
    static readonly Vector2I[] Steps={Vector2I.Up,Vector2I.Right,Vector2I.Down,Vector2I.Left,new(1,1),new(1,-1),new(-1,1),new(-1,-1)};
    int _minX,_maxX,_minZ,_maxZ;
    string _layout="";
    public FarmNavigation(Main town){_town=town;}
    public void Dispose()=>_body.Dispose();
    public void Invalidate(){_clear.Clear();}
    public bool CanInteract(Vector3 from,Vector3 target)
    {
        var ray=PhysicsRayQueryParameters3D.Create(from+Vector3.Up*.75f,target+Vector3.Up*.75f,1);
        bool visible=_town.GetWorld3D().DirectSpaceState.IntersectRay(ray).Count==0;ray.Dispose();return visible;
    }
    public bool Clear(Vector3 point)
    {
        var query=new PhysicsShapeQueryParameters3D{Shape=_body,Transform=new Transform3D(Basis.Identity,point+Vector3.Up*.95f),CollisionMask=1,CollideWithAreas=false};
        bool clear=_town.GetWorld3D().DirectSpaceState.IntersectShape(query,1).Count==0;query.Dispose();return clear;
    }
    bool Open(Vector2I c)
    {
        if(c.X<_minX||c.X>_maxX||c.Y<_minZ||c.Y>_maxZ)return false;
        if(!_clear.TryGetValue(c,out bool clear))_clear[c]=clear=Clear(new(c.X,0,c.Y));return clear;
    }
    public List<Vector3> Route(Vector3 from,Vector3 target,float reach)
    {
        string layout=_town.FarmLayoutSignature;
        if(layout!=_layout){_layout=layout;Invalidate();}
        _minX=Math.Max(1,(int)Math.Min(from.X,target.X)-24);_maxX=Math.Min((int)WorldData.WorldWidth-1,(int)Math.Max(from.X,target.X)+24);
        _minZ=Math.Max(1,(int)Math.Min(from.Z,target.Z)-24);_maxZ=Math.Min((int)WorldData.WorldDepth-1,(int)Math.Max(from.Z,target.Z)+24);
        var start=new Vector2I(Mathf.RoundToInt(from.X),Mathf.RoundToInt(from.Z));
        var queue=new PriorityQueue<Vector2I,float>();var cost=new Dictionary<Vector2I,float>{{start,0}};var parents=new Dictionary<Vector2I,Vector2I>{{start,start}};
        float Distance(Vector2I c)=>new Vector2(c.X-target.X,c.Y-target.Z).Length();
        queue.Enqueue(start,Distance(start));Vector2I? goal=null;
        while(queue.Count>0&&parents.Count<8000){
            var c=queue.Dequeue();if(Distance(c)<=reach&&Open(c)&&CanInteract(new(c.X,0,c.Y),target)){goal=c;break;}
            foreach(var step in Steps){
                var next=c+step;if(!Open(next))continue;
                if(step.X!=0&&step.Y!=0&&(!Open(c+new Vector2I(step.X,0))||!Open(c+new Vector2I(0,step.Y))))continue;
                float candidate=cost[c]+(step.X!=0&&step.Y!=0?1.4143f:1);
                if(cost.TryGetValue(next,out float previous)&&candidate>=previous)continue;
                cost[next]=candidate;parents[next]=c;queue.Enqueue(next,candidate+Distance(next));
            }
        }
        if(goal==null)return new();
        var route=new List<Vector3>();for(var c=goal.Value;c!=start;c=parents[c])route.Add(new(c.X,0,c.Y));route.Reverse();
        if(route.Count==0)route.Add(new(goal.Value.X,0,goal.Value.Y));return route;
    }
}
