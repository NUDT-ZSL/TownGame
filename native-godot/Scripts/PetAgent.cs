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
    readonly System.Collections.Generic.List<Vector3> _path=new();
    FarmNavigation? _navigation;
    int _index;
    double _stuck;
    string _intent="探索";
    public string Activity {get;private set;}="观察周围";
    double _wait;
    double _reaction;
    string _pose="idle";
    public void React(string pose){_pose=pose;_reaction=2.5;}
    public override void _Ready()
    {
        CollisionLayer=2;CollisionMask=7;SafeMargin=.03f;
        uint seed=2166136261;foreach(char c in Ident){seed^=c;seed*=16777619;}_random=new(unchecked((int)seed));
        float scale=Main.PetWorldScale(Species);float height=Species is "羊驼" or "梅花鹿"?1.9f:Species=="兔子"?.85f:Math.Max(.45f,scale*6);
        AddChild(new CollisionShape3D{Shape=new CapsuleShape3D{Radius=Math.Min(.36f,height*.3f),Height=height},Position=new(0,height/2,0)});
        _visual.Scale=Vector3.One*.5f;AddChild(_visual);_target=Position;
    }
    void ShowPose(System.Text.Json.Nodes.JsonNode state,string pose)
    {
        int frame=pose is "walk" or "eat" or "pet" or "bath"?(int)(Time.GetTicksMsec()/160)%4:0;
        if(pose=="idle"&&Time.GetTicksMsec()%4300<160)pose="idle_blink";
        _visual.Mesh=Town.PetMesh(state,pose,frame);
    }
    void Decide(System.Text.Json.Nodes.JsonNode state)
    {
        _navigation??=new FarmNavigation(Town);_path.Clear();_index=0;_intent="探索";
        if(NativeGame.D(state,"hunger")<45){_target=Town.PetPropPoint("bowl")+new Vector3(0,0,2.8f);_intent="等待喂食";}
        else if(NativeGame.D(state,"happy")<45||_random.NextDouble()<.2){_target=Town.PetPropPoint("toy")+new Vector3(0,0,2.5f);_intent="玩玩具";}
        else if(_random.NextDouble()<.3){var friend=Town.GetChildren().OfType<PetAgent>().Where(p=>p!=this&&!p.IsQueuedForDeletion()).OrderBy(p=>Position.DistanceSquaredTo(p.Position)).FirstOrDefault();_target=friend==null?Position:friend.Position+new Vector3(2.2f,0,1.2f);_intent="与伙伴玩耍";}
        else _target=WorldData.Expand(new(104+(float)_random.NextDouble()*91,0,99+(float)_random.NextDouble()*14));
        var route=_navigation.Route(Position,_target,1.6f);if(route.Count>0)_path.AddRange(route);else{_target=Position;_wait=2;Activity="等待通道畅通";}
    }
    public override void _PhysicsProcess(double delta)
    {
        if(Town.Paused)return;
        var state=Town.Data.Pets.FirstOrDefault(p=>WorldData.S(p!,"pet_id")==Ident);if(state==null)return;
        string stage=Town.Game.Stage(state);_visual.Scale=Vector3.One*Main.PetWorldScale(Species)*(stage=="幼崽"?.72f:stage=="少年"?.86f:stage=="老年"?.94f:1);
        if(_reaction>0){_reaction-=delta;ShowPose(state,_pose);Activity=_pose switch{"eat"=>"进食中","bath"=>"洗澡中","pet"=>"互动中",_=>"等待照护"};Velocity=Vector3.Zero;return;}
        double hour=Town.Game.Now%86400/3600;
        if(hour<6||hour>=22||NativeGame.D(state,"health")<25){ShowPose(state,"sleep");Velocity=Vector3.Zero;Activity="休息";return;}
        if(_index>=_path.Count){Velocity=Vector3.Zero;_wait-=delta;Activity=_intent;ShowPose(state,_intent is "玩玩具" or "与伙伴玩耍"?"pet":"idle");if(_wait<=0){Decide(state);_wait=4+_random.NextDouble()*7;}return;}
        var d=_path[_index]-Position;d.Y=0;if(d.Length()<.3f){_index++;return;}
        var direction=d.Normalized();Velocity=direction*Math.Min(2.4f,d.Length()/(float)Math.Max(delta,.001))+Vector3.Down*2;
        var before=Position;MoveAndSlide();Position=new(Position.X,Math.Max(0,Position.Y),Position.Z);Rotation=new(0,Mathf.Atan2(direction.X,direction.Z),0);ShowPose(state,"walk");Activity="前往"+_intent;
        _stuck=Position.DistanceTo(before)<.005?_stuck+delta:0;if(_stuck>1.5){_navigation?.Invalidate();_path.Clear();_wait=.4;_stuck=0;}
    }
    public override void _ExitTree()=>_navigation?.Dispose();
}
