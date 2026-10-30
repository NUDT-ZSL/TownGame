using Godot;
using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using System.Linq;

public partial class RoadAgent : CharacterBody3D
{
    public bool IsCar;
    public string Ident="",Kind="person";
    public float Speed=2.5f;
    public MeshInstance3D Visual=new();
    public readonly List<Vector3> Path=new();
    public int Index;
    public double Wait;
    public Main Town=null!;
    public bool ReturningHome,InsideHome;
    public Vector3 HomeDoor;
    public string ResidentialActivity="外出活动";
    public string InsideHomeBuildingId="";
    public string WorkSiteId="";
    string _schedule="";
    double _scheduleCheck;
    bool _exitRequested;
    double _exitDelay;
    public void EnterHome(JsonNode person)
    {
        InsideHome=true;ReturningHome=false;_exitRequested=false;ResidentialActivity="在家休息";Path.Clear();Index=0;
        Velocity=Vector3.Zero;Visual.Hide();CollisionLayer=0;CollisionMask=0;
        person["at_home"]=true;person["home_arrived_ts"]=Town.Game.Now;
        InsideHomeBuildingId=NativeGame.S(person,"home_building_id");person["inside_home_building_id"]=InsideHomeBuildingId;
        person["home_door_x"]=HomeDoor.X;person["home_door_z"]=HomeDoor.Z;
    }
    void LeaveHome(JsonNode person)
    {
        InsideHome=false;ReturningHome=false;_exitRequested=false;ResidentialActivity="外出活动";Visual.Show();CollisionLayer=2;CollisionMask=7;
        person["at_home"]=false;Path.Clear();Index=0;Wait=0;
        InsideHomeBuildingId="";person["inside_home_building_id"]="";
    }
    public override void _Ready()
    {
        CollisionLayer=IsCar?4u:2u;CollisionMask=7;
        AddChild(Visual);
        Shape3D shape=IsCar?new BoxShape3D{Size=new(1.5f,1.8f,Kind=="bus"?7.75f:Kind=="truck"?5.625f:4.5f)}:new CapsuleShape3D{Radius=.27f,Height=1.8f};
        AddChild(new CollisionShape3D{Shape=shape,Position=new(0,IsCar?.9f:.9f,0)});
        SafeMargin=.02f;
        if(!IsCar&&Town.Resident(Ident) is JsonNode person&&NativeGame.B(person,"at_home")){
            string savedHome=NativeGame.S(person,"inside_home_building_id",NativeGame.S(person,"home_building_id"));
            var home=Town.Game.Rows("town_buildings").FirstOrDefault(h=>NativeGame.S(h!,"building_id")==savedHome);
            if(home!=null){
                int x=NativeGame.I(home,"cx"),y=NativeGame.I(home,"cy"),w=NativeGame.I(home,"w"),h=NativeGame.I(home,"h");
                var fallback=NativeGame.I(home,"orientation") switch{0=>new NativeGame.Cell(x,y+h),1=>new NativeGame.Cell(x-1,y),2=>new NativeGame.Cell(x,y-1),_=>new NativeGame.Cell(x+w,y)};
                var entry=Town.Game.HomeRoadEntrance(home)??fallback;
                HomeDoor=person["home_door_x"]!=null?new((float)NativeGame.D(person,"home_door_x"),0,(float)NativeGame.D(person,"home_door_z")):Town.ResidenceDoor(home,entry);
                double arrived=NativeGame.D(person,"home_arrived_ts",Town.Game.Now);Position=HomeDoor;EnterHome(person);person["home_arrived_ts"]=arrived;InsideHomeBuildingId=savedHome;person["inside_home_building_id"]=savedHome;
            }else{person["at_home"]=false;person["inside_home_building_id"]="";}
        }
    }
    public override void _PhysicsProcess(double delta)
    {
        if(Town.Paused)return;
        if(Town.TickFreightActor(this,delta))return;
        _scheduleCheck-=delta;
        if(!IsCar&&_scheduleCheck<=0){
            _scheduleCheck=.25;
            var scheduled=Town.Data.People.FirstOrDefault(p=>NativeGame.S(p!,"person_id")==Ident);
            if(scheduled is JsonNode person){
                string schedule=$"{Town.Game.IsResidentNight}:{NativeGame.S(person,"home_building_id")}:{NativeGame.S(person,"destination")}:{NativeGame.S(person,"job_id")}:{NativeGame.I(Town.Data.Meta,"road_revision")}";
                if(schedule!=_schedule){
                    _schedule=schedule;
                    if(InsideHome&&(!Town.Game.IsResidentNight||InsideHomeBuildingId!=NativeGame.S(person,"home_building_id")||Town.Game.ResidentHome(person)==null)){
                        var oldHome=Town.Game.Rows("town_buildings").FirstOrDefault(h=>NativeGame.S(h!,"building_id")==InsideHomeBuildingId);
                        if(oldHome==null||Town.Game.HomeRoadEntrance(oldHome)!=null){
                            _exitRequested=true;_exitDelay=.4*Town.Game.HomeMembers(InsideHomeBuildingId).OrderBy(p=>NativeGame.S(p,"person_id"),StringComparer.Ordinal).TakeWhile(p=>NativeGame.S(p,"person_id")!=Ident).Count();ResidentialActivity="准备出门";
                        }else{_exitRequested=false;ResidentialActivity="在家等待道路修复";}
                    }
                    if(!InsideHome)Town.AssignRoute(this);
                }
            }
        }
        if(InsideHome){
            if(_exitRequested&&Town.Resident(Ident) is JsonNode person){
                _exitDelay-=delta;
                if(_exitDelay<=0&&!Town.Agents.Any(a=>a!=this&&!a.InsideHome&&a.Position.DistanceTo(Position)<.8f)){LeaveHome(person);Town.AssignRoute(this);}
            }
            if(InsideHome)return;
        }
        if(Index>=Path.Count){
            if(ReturningHome&&Position.DistanceTo(HomeDoor)<.5f&&Town.Resident(Ident) is JsonNode person&&Town.Game.ResidentHome(person) is JsonNode home&&Town.Game.HomeRoadEntrance(home)!=null){EnterHome(person);return;}
            Wait-=delta;if(Wait<=0)Town.AssignRoute(this);return;
        }
        Vector3 target=Path[Index];Vector3 desired=target-Position;desired.Y=0;
        if(desired.Length()<.2f){Index++;return;}
        var direction=desired.Normalized();float speed=Speed;
        bool signal=Town.RedLight(this,direction,(float)delta);
        if(signal)speed=0;
        foreach(var other in Town.Agents) {
            if(other==this||other.InsideHome||!other.Visual.Visible||other.ProcessMode==ProcessModeEnum.Disabled)continue;
            if(!IsCar&&Town.Game.IsFreightDriver(Ident)&&other.Ident==NativeGame.S(Town.Game.FreightState,"vehicle_id"))continue;
            var separation=other.Position-Position;separation.Y=0;
            float forward=separation.Dot(direction);
            float lateral=(separation-direction*forward).Length();
            if(IsCar&&forward>0&&forward<(Kind=="bus"?6:4)&&lateral<1.1f)speed=0;
            if(!IsCar&&other.IsCar&&forward>0&&forward<4.8f&&lateral<1.2f) {
                var side=new Vector3(-direction.Z,0,direction.X);
                float sign=(Position-other.Position).Dot(side)>=0?1:-1;
                var candidate=Position+side*sign*.25f;
                if(Town.Data.Roads.ContainsKey(WorldData.Grid(candidate)))direction=(direction*.12f+side*sign).Normalized();
            }
        }
        speed=Math.Min(speed,desired.Length()/(float)Math.Max(delta,.0001));
        Velocity=direction*speed+Vector3.Down*2;
        MoveAndSlide();
        Position=new(Position.X,Math.Max(0,Position.Y),Position.Z);
        if(speed>.1f)Rotation=new(0,Mathf.Atan2(direction.X,direction.Z),0);
        if(!IsCar&&speed>.1f)Visual.Mesh=Town.Prototype("person-"+((int)(Time.GetTicksMsec()/140)%4));
    }
}
