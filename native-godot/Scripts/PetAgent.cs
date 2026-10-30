using Godot;
using System;
using System.Linq;

public partial class PetAgent : CharacterBody3D
{
    public Main Town=null!;
    public string Ident="",Species="兔子";
    readonly MeshInstance3D _visual=new();
    Random _random=null!;
    Vector3 _target;
    double _wait;
    double _reaction;
    string _pose="idle";
    public void React(string pose){_pose=pose;_reaction=2.5;}
    public override void _Ready()
    {
        CollisionLayer=2;CollisionMask=7;SafeMargin=.03f;
        uint seed=2166136261;foreach(char c in Ident){seed^=c;seed*=16777619;}_random=new(unchecked((int)seed));
        AddChild(new CollisionShape3D{Shape=new CapsuleShape3D{Radius=.65f,Height=1.5f},Position=new(0,.75f,0)});
        _visual.Mesh=Town.Prototype(Species+":idle:0");_visual.Scale=Vector3.One*.5f;AddChild(_visual);_target=Position;
    }
    public override void _PhysicsProcess(double delta)
    {
        if(Town.Paused)return;
        var state=Town.Data.Pets.FirstOrDefault(p=>WorldData.S(p!,"pet_id")==Ident);if(state==null)return;
        string stage=Town.Game.Stage(state);float scale=stage=="幼崽"?.3f:stage=="少年"?.4f:.5f;_visual.Scale=Vector3.One*scale;
        if(_reaction>0){_reaction-=delta;_visual.Mesh=Town.Prototype(Species+":"+_pose+":"+((int)(Time.GetTicksMsec()/160)%4));return;}
        double hour=Town.Game.Now%86400/3600;if(hour<6||hour>=22){_visual.Mesh=Town.Prototype(Species+":sleep:0");return;}
        var d=_target-Position;d.Y=0;
        if(d.Length()<.3f) {
            _wait-=delta;
            if(_wait<=0){_target=WorldData.Expand(new(104+(float)_random.NextDouble()*91,0,101+(float)_random.NextDouble()*9));_wait=2+_random.NextDouble()*4;}
            _visual.Mesh=Town.Prototype(Species+":idle:0");Velocity=Vector3.Down*2;
        } else {
            var direction=d.Normalized();Velocity=direction*3+Vector3.Down*2;
            Rotation=new(0,Mathf.Atan2(direction.X,direction.Z),0);_visual.Mesh=Town.Prototype(Species+":walk:"+((int)(Time.GetTicksMsec()/160)%4));
        }
        MoveAndSlide();Position=new(Position.X,Math.Max(0,Position.Y),Position.Z);
        if(IsOnWall()){_target=Position;_wait=.2;}
    }
}
