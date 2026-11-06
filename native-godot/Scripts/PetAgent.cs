using Godot;
using System;

public partial class PetAgent : CharacterBody3D
{
    public Main Town=null!;
    public string Ident="",Species="兔子";
    readonly MeshInstance3D _visual=new();
    internal float NameplateHeight=>_visual.Mesh is Mesh mesh?Math.Max(.25f,mesh.GetAabb().End.Y*_visual.Scale.Y)+.14f:.8f;
    internal void SetPhotoFacing(Vector3? point)
    {
        var direction=point is Vector3 world?ToLocal(world):Vector3.Forward;
        _visual.Rotation=point==null?Vector3.Zero:new(0,Mathf.Atan2(direction.X,direction.Z),0);
    }
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
    int _homePlot=-1;
    Vector3 _homePoint;
    bool _atHospital,_returningHome,_movingHome;
    PetAmbientMotion.Profile? _ambientMotion;
    int _ambientOffset;
    public void React(string pose){EndCompanionship();_pose=pose;_reaction=2.5;}
    public override void _Ready()
    {
        CollisionLayer=2;CollisionMask=7;SafeMargin=.03f;
        uint seed=2166136261;foreach(char c in Ident){seed^=c;seed*=16777619;}_random=new(unchecked((int)seed));
        _ambientMotion=PetAmbientMotion.ForSpecies(Species);
        _ambientOffset=(int)(seed%PetAmbientMotion.CycleMs);
        _wait=(seed%1200)/1000d;
        float scale=Main.PetWorldScale(Species);float height=Species is "羊驼" or "梅花鹿"?1.9f:Species=="兔子"?.85f:Math.Max(.45f,scale*6);
        AddChild(new CollisionShape3D{Shape=new CapsuleShape3D{Radius=Math.Min(.36f,height*.3f),Height=height},Position=new(0,height/2,0)});
        _visual.Scale=Vector3.One*.5f;AddChild(_visual);_target=Position;
        if(Town.Data.FindPet(Ident) is { } state)SetHome(state);
    }
    void ShowPose(System.Text.Json.Nodes.JsonNode state,string pose)
    {
        int frame=pose is "walk" or "eat" or "pet" or "bath" or "soothe"?(int)(Time.GetTicksMsec()/160)%4:0;
        if(pose=="idle"&&Time.GetTicksMsec()%4300<160)pose="idle_blink";
        _visual.Mesh=Town.PetMesh(state,pose,frame);
    }
    void ShowIdle(System.Text.Json.Nodes.JsonNode state)
    {
        if(UsePetFacility(state))return;
        Activity=_intent;
        if(_intent=="玩玩具"){
            ShowPose(state,"pet");
            return;
        }
        int phase=(int)((Time.GetTicksMsec()+(ulong)_ambientOffset)%PetAmbientMotion.CycleMs);
        if(_intent=="探索"&&_ambientMotion is PetAmbientMotion.Profile motion&&phase<PetAmbientMotion.ActiveMs){
            int frame=PetAmbientMotion.FrameAt(phase);
            if(frame!=0){
                _visual.Mesh=Town.PetMesh(state,motion.Pose,frame);
                Activity=motion.Activity;
                return;
            }
        }
        ShowPose(state,"idle");
    }
    void Decide(System.Text.Json.Nodes.JsonNode state)
    {
        _navigation??=new FarmNavigation(Town);_path.Clear();_index=0;_intent="探索";_approachingCompanion="";
        _facilityIntent=null;_facilityVisited=false;
        if(NativeGame.D(state,"hunger")<45){_target=Town.PetPropPoint("bowl")+new Vector3(0,0,2.8f);_intent="等待喂食";}
        else if((NativeGame.D(state,"happy")<52||_random.NextDouble()<.36)&&ChoosePetFacility(state))return;
        else if(NativeGame.D(state,"happy")<45||_random.NextDouble()<.2){_target=Town.PetPropPoint("toy")+new Vector3(0,0,2.5f);_intent="玩玩具";}
        else if(_random.NextDouble()>=.3||!ChooseCompanion())_target=WorldData.Expand(new(104+(float)_random.NextDouble()*91,0,99+(float)_random.NextDouble()*14));
        var route=_navigation.Route(Position,_target,1.6f);if(route.Count>0)_path.AddRange(route);else{_target=Position;_wait=2;Activity="等待通道畅通";}
    }
    void SetHome(System.Text.Json.Nodes.JsonNode state)
    {
        _homePlot=NativeGame.I(state,"building_id");
        _homePoint=Main.PetHomePoint(state);
        _atHospital=Town.Game.IsPetInHospital(state);
    }

    bool UpdateResidence(System.Text.Json.Nodes.JsonNode state,bool nighttime,double delta)
    {
        if(NativeGame.I(state,"building_id")!=_homePlot){
            SetHome(state);
            _movingHome=true;
            _returningHome=false;
        }
        bool homebound=_movingHome||nighttime||_atHospital;
        if(!homebound){
            if(_returningHome){_path.Clear();_index=0;_wait=.3;}
            _returningHome=false;
            return false;
        }
        EndCompanionship();
        if(Position.DistanceSquaredTo(_homePoint)<2.5f){
            _movingHome=false;
            _returningHome=false;
            _path.Clear();
            _index=0;
            Velocity=Vector3.Zero;
            Activity=_atHospital?"在诊疗区休息":nighttime?"在自己的居所休息":"已到新居所";
            ShowPose(state,nighttime||_atHospital?"sleep":"idle");
            return true;
        }
        if(!_returningHome){
            _returningHome=true;
            _path.Clear();
            _index=0;
            _wait=0;
        }
        if(_index<_path.Count)return false;
        _wait-=delta;
        if(_wait<=0&&Town.TryScheduleFarmRoutes()){
            _navigation??=new FarmNavigation(Town);
            _path.Clear();
            _path.AddRange(_navigation.Route(Position,_homePoint,1.45f));
            _index=0;
            _intent=_atHospital?"诊疗区":"自己的居所";
            _wait=1.5;
            if(_path.Count>0)return false;
        }
        Velocity=Vector3.Zero;
        Activity="等待居所通道畅通";
        ShowPose(state,"idle");
        return true;
    }
    public override void _PhysicsProcess(double delta)
    {
        if(Town.Paused)return;
        var state=Town.Data.FindPet(Ident);if(state==null)return;
        _visual.Position=Vector3.Zero;
        string stage=Town.Game.Stage(state);_visual.Scale=Vector3.One*Main.PetWorldScale(Species)*(stage=="幼崽"?.72f:stage=="少年"?.86f:stage=="老年"?.94f:1);
        if(_reaction>0){_reaction-=delta;ShowPose(state,_pose);Activity=_pose switch{"eat"=>"进食中","bath"=>"洗澡中","pet"=>"互动中","soothe"=>"幼崽正在接受安抚",_=>"等待照护"};Velocity=Vector3.Zero;return;}
        double hour=Town.Game.Now%86400/3600;
        if(UpdateResidence(state,hour<6||hour>=22,delta))return;
        if(NativeGame.D(state,"health")<25&&!_returningHome){EndCompanionship();ShowPose(state,"sleep");Velocity=Vector3.Zero;Activity="休息";return;}
        if(!_returningHome&&UpdateCompanionActivity(state,delta))return;
        if(_index>=_path.Count){Velocity=Vector3.Zero;_wait-=delta;ShowIdle(state);if(_wait<=0&&Town.TryScheduleFarmRoutes(2)){Decide(state);_wait=4+_random.NextDouble()*7;}return;}
        var d=_path[_index]-Position;d.Y=0;if(d.Length()<.3f){_index++;return;}
        var direction=d.Normalized();Velocity=direction*Math.Min(2.4f,d.Length()/(float)Math.Max(delta,.001))+Vector3.Down*2;
        var before=Position;MoveAndSlide();Position=new(Position.X,Math.Max(0,Position.Y),Position.Z);Rotation=new(0,Mathf.Atan2(direction.X,direction.Z),0);ShowPose(state,"walk");Activity=_companionTime>0&&!_returningHome?"结伴散步":"前往"+_intent;
        _stuck=Position.DistanceTo(before)<.005?_stuck+delta:0;if(_stuck>1.5){_navigation?.Invalidate();_path.Clear();_wait=.4;_stuck=0;}
    }
    public override void _ExitTree()=>_navigation?.Dispose();
}
