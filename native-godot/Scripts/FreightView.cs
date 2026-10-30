using Godot;
using System;
using System.Linq;
using System.Text.Json.Nodes;

public partial class Main
{
    RoadAgent? _freightTruck;
    MeshInstance3D? _freightCabDriver;
    Node3D? _freightCargo;
    Label3D? _freightLabel;
    string _freightRoutePhase="",_freightDriver="";
    long _freightRoadRevision=-1;
    double _freightRefresh,_freightWork,_freightRetry;
    Vector3 _freightGoal;
    bool _freightHasRoute;
    const float FreightLane=1.65f,FreightWalk=3.35f;
    public bool FreightAboard(string id)=>Game.IsFreightDriver(id)&&NativeGame.S(Game.FreightState,"phase") is "to_source" or "loading" or "to_destination" or "unloading" or "to_depot";
    public string FreightActivity(string id)=>Game.IsFreightDriver(id)?NativeGame.S(Game.FreightState,"status"):"";
    public string FreightDescription(string id)
    {
        if(id!=NativeGame.S(Game.FreightState,"vehicle_id"))return "车辆";
        var driver=Resident(NativeGame.S(Game.FreightState,"driver_id"));
        string item=NativeGame.S(Game.FreightState,"cargo_item")=="wood"?"木材":"农产品";
        return $"居民货车 · 司机：{(driver==null?"暂未上车":NativeGame.S(driver,"name"))}\n{NativeGame.S(Game.FreightState,"status")}\n车载：{item} {NativeGame.I(Game.FreightState,"cargo_count")}份 · 已完成 {NativeGame.I(Game.FreightState,"trips")} 趟运输";
    }
    Vector2I FreightStop(string zone)
    {
        var portal=Data.Portals[zone];
        var access=Game.UrbanAccessPoints().Where(a=>a.Zone==zone&&a.Entrance!=null)
            .OrderBy(a=>a.Id=="manor:1"?-1:(a.Entrance!.Value.X-portal.X)*(a.Entrance.Value.X-portal.X)+(a.Entrance.Value.Y-portal.Y)*(a.Entrance.Value.Y-portal.Y)).FirstOrDefault();
        if(access?.Entrance is NativeGame.Cell entrance&&(zone!="estate"||access.Id=="manor:1"))portal=new(entrance.X,entrance.Y);
        // Stop on a nearby straight section, keeping the intersection itself clear.
        bool Straight(Vector2I cell){var n=Data.Neighbors(cell).ToArray();return n.Length==2&&n[0]+n[1]==cell*2;}
        return Data.Roads.Keys.Where(c=>(c-portal).LengthSquared()<=25&&Straight(c)&&Data.Route(portal,c,FreightLane).Count>0)
            .OrderBy(c=>(c-portal).LengthSquared()<4?100:(c-portal).LengthSquared()).ThenBy(c=>c.X).ThenBy(c=>c.Y).DefaultIfEmpty(portal).First();
    }
    void CreateFreightTruck()
    {
        var state=Game.FreightState;var stop=FreightStop("estate");var parking=Data.Route(Data.Portals["estate"],stop,FreightLane);
        var start=parking.Count>0?parking[^1]:WorldData.Cell(stop);Vector3 direction=parking.Count>1?parking[^1]-parking[^2]:Vector3.Back;
        if(state["world_x"]!=null)start=new((float)NativeGame.D(state,"world_x"),0,(float)NativeGame.D(state,"world_z"));
        _freightTruck=new RoadAgent{Town=this,IsCar=true,Kind="truck",Ident=NativeGame.S(state,"vehicle_id"),Position=start,Speed=7};
        _freightTruck.Rotation=new(0,state["world_yaw"]!=null?(float)NativeGame.D(state,"world_yaw"):Mathf.Atan2(direction.X,direction.Z),0);
        _freightTruck.Visual.Mesh=Prototype("truck");_freightTruck.SetMeta("kind","vehicle");_freightTruck.SetMeta("ident",_freightTruck.Ident);
        AddChild(_freightTruck);Agents.Add(_freightTruck);
        _freightCabDriver=new MeshInstance3D{Mesh=Prototype("person-0"),Scale=Vector3.One*.7f,Position=new(.4f,.8f,1.8f)};_freightTruck.Visual.AddChild(_freightCabDriver);
        _freightCargo=new Node3D();_freightTruck.Visual.AddChild(_freightCargo);
        var crateMaterial=new StandardMaterial3D{AlbedoColor=new Color("cfac72")};
        for(int i=0;i<4;i++)_freightCargo.AddChild(new MeshInstance3D{Mesh=new BoxMesh{Size=new(.65f,.55f,.75f)},MaterialOverride=crateMaterial,Position=new(i%2==0?-.38f:.38f,1.85f,-.5f-i/2*.9f)});
        _freightLabel=new Label3D{Font=new SystemFont{FontNames=new[]{"Microsoft YaHei"}},FontSize=28,PixelSize=.018f,Billboard=BaseMaterial3D.BillboardModeEnum.Enabled,Position=Vector3.Up*3.1f,Modulate=new Color("ffe3ab")};_freightTruck.AddChild(_freightLabel);
        _freightRoutePhase="";
    }
    Vector3 FreightBoardingPoint()
    {
        var truck=_freightTruck!;var lateral=truck.Position-WorldData.Cell(WorldData.Grid(truck.Position));
        // Board from the pavement side of the parked lane, never from the median.
        float side=lateral.Dot(truck.Basis.X)>=0?1:-1;
        return truck.Position+truck.Basis.X*(side*1.55f);
    }
    System.Collections.Generic.List<Vector3> FreightRoadRoute(RoadAgent truck,Vector2I start,Vector2I goal)
    {
        var route=Data.Route(start,goal,FreightLane);var centerline=Data.Route(start,goal,0);
        if(centerline.Count<2)return route;
        var desired=(centerline[1]-centerline[0]).Normalized();var heading=truck.Basis.Z;
        if(heading.Dot(desired)>-.5f)return route;
        var step=Math.Abs(heading.X)>Math.Abs(heading.Z)?new Vector2I(Math.Sign(heading.X),0):new Vector2I(0,Math.Sign(heading.Z));
        var turn=start;
        // Continue in the current lane until a junction, corner or road end.
        // Those cells have no planted median and can safely contain the U-turn.
        for(int i=0;i<125;i++){
            var next=turn+step;if(!Data.Roads.ContainsKey(next))break;turn=next;
            var neighbors=Data.Neighbors(turn).ToArray();
            if(neighbors.Length!=2||neighbors[0]+neighbors[1]!=turn*2)break;
        }
        if(turn==start)return route;
        var onward=Data.Route(turn,goal,FreightLane);if(onward.Count==0)return route;
        var prefix=Data.Route(start,turn,FreightLane);prefix.AddRange(onward);return prefix;
    }
    public bool TryAssignFreightRoute(RoadAgent actor)
    {
        if(actor.Ident==NativeGame.S(Game.FreightState,"vehicle_id")){
            actor.Wait=1;
            string phase=NativeGame.S(Game.FreightState,"phase");
            if(phase is not ("to_source" or "to_destination" or "to_depot")){actor.Path.Clear();actor.Index=0;return true;}
            long revision=NativeGame.I(Data.Meta,"road_revision");
            if(phase==_freightRoutePhase&&revision==_freightRoadRevision&&_freightHasRoute)return true;
            if(_freightRetry>0)return true;
            _freightRoutePhase=phase;_freightRoadRevision=revision;
            string zone=phase=="to_source"?NativeGame.S(Game.FreightState,"source"):phase=="to_destination"?NativeGame.S(Game.FreightState,"destination"):"estate";
            var start=WorldData.Grid(actor.Position);if(!Data.Roads.ContainsKey(start))start=Data.Roads.Keys.MinBy(c=>(WorldData.Cell(c)-actor.Position).LengthSquared());
            var route=FreightRoadRoute(actor,start,FreightStop(zone));actor.Path.Clear();actor.Index=0;_freightHasRoute=route.Count>0;
            if(!_freightHasRoute){Game.FreightState["status"]="货运道路中断，保留货物等待接通";_freightRetry=2;return true;}
            actor.Path.AddRange(route);_freightGoal=route[^1];return true;
        }
        if(!Game.IsFreightDriver(actor.Ident)||_freightTruck==null)return false;
        if(FreightAboard(actor.Ident))return true;
        if(Game.IsResidentNight||actor.InsideHome)return false;
        actor.ReturningHome=false;actor.Path.Clear();actor.Index=0;actor.Wait=1;
        var path=BeginPublicRoute(actor,out var source);
        var target=WorldData.Grid(_freightTruck.Position);var door=FreightBoardingPoint();
        var streetPath=source==target?new System.Collections.Generic.List<Vector3>():Data.PedestrianRoute(path.Count>0?path[^1]:actor.Position,source,target,door);
        if(streetPath.Count>0||source==target){
            path.AddRange(streetPath);
            var approach=path.Count>0?path[^1]:actor.Position;var local=_freightTruck.ToLocal(approach);var localDoor=_freightTruck.ToLocal(door);
            if(local.X*Math.Sign(localDoor.X)<1.1f){
                float around=local.Z>=0?3.65f:-3.65f;
                path.Add(_freightTruck.ToGlobal(new Vector3(local.X,0,around)));
                path.Add(_freightTruck.ToGlobal(new Vector3(localDoor.X,0,around)));
            }
            actor.Path.AddRange(path);actor.Path.Add(door);
        }
        actor.ResidentialActivity="正在走向货车驾驶室";return true;
    }
    public bool TickFreightActor(RoadAgent actor,double delta)
    {
        if(actor.IsCar||_freightTruck==null||!FreightAboard(actor.Ident))return false;
        actor.Visual.Hide();actor.CollisionLayer=0;actor.CollisionMask=0;actor.Velocity=Vector3.Zero;
        actor.Position=_freightTruck.Position;actor.Path.Clear();actor.Index=0;actor.InsideHome=false;actor.ReturningHome=false;
        if(Resident(actor.Ident) is JsonNode person){person["at_home"]=false;person["inside_home_building_id"]="";}
        actor.ResidentialActivity=FreightActivity(actor.Ident);return true;
    }
    void DisembarkFreight(string driver)
    {
        var actor=Agents.FirstOrDefault(a=>!a.IsCar&&a.Ident==driver);if(actor==null||_freightTruck==null)return;
        actor.Position=FreightBoardingPoint();actor.Visual.Show();actor.CollisionLayer=2;actor.CollisionMask=7;actor.InsideHome=false;
        actor.Path.Clear();actor.Index=0;actor.Wait=0;AssignRoute(actor);
    }
    void UpdateFreight(double delta)
    {
        if(Paused)return;_freightRetry-=delta;_freightRefresh-=delta;
        if(_freightRefresh<=0){Game.RefreshFreight();_freightRefresh=.5;}
        if(_freightTruck==null)CreateFreightTruck();
        var state=Game.FreightState;string driver=NativeGame.S(state,"driver_id"),phase=NativeGame.S(state,"phase");
        if(driver!=_freightDriver){
            string old=_freightDriver;_freightDriver=driver;if(old!="")DisembarkFreight(old);
            var actor=Agents.FirstOrDefault(a=>!a.IsCar&&a.Ident==driver);if(actor!=null)AssignRoute(actor);
        }
        _freightCabDriver!.Visible=driver!=""&&FreightAboard(driver);_freightCargo!.Visible=NativeGame.I(state,"cargo_count")>0;
        _freightLabel!.Text=driver==""?"货运车 · 等待居民驾驶":"司机 "+NativeGame.S(Resident(driver)??new JsonObject(),"name")+" · "+(NativeGame.I(state,"cargo_count")>0?"载货":"空车");
        if(_freightRefresh>.49){state["world_x"]=_freightTruck!.Position.X;state["world_z"]=_freightTruck.Position.Z;state["world_yaw"]=_freightTruck.Rotation.Y;}
        if(phase=="boarding"){
            var actor=Agents.FirstOrDefault(a=>!a.IsCar&&a.Ident==driver);
            if(actor!=null&&!actor.InsideHome&&actor.Position.DistanceTo(FreightBoardingPoint())<.65f&&Game.BoardFreight()){
                TickFreightActor(actor,0);_freightRoutePhase="";_freightHasRoute=false;AssignRoute(_freightTruck!);
            }
        }else if(phase is "to_source" or "to_destination" or "to_depot"){
            TryAssignFreightRoute(_freightTruck!);
            if(_freightHasRoute&&_freightTruck!.Index>=_freightTruck.Path.Count&&_freightTruck.Position.DistanceTo(_freightGoal)<.4f){
                _freightTruck.Velocity=Vector3.Zero;_freightHasRoute=false;_freightWork=0;
                if(phase=="to_depot")Game.ReleaseFreight();
                else {state["phase"]=phase=="to_source"?"loading":"unloading";state["status"]=phase=="to_source"?"司机在生产区装货":"司机在商业区卸货";}
            }
        }else if(phase is "loading" or "unloading"){
            _freightWork+=delta;if(_freightWork>=3){if(phase=="loading")Game.LoadFreight();else Game.DeliverFreight();_freightWork=0;_freightRoutePhase="";_freightHasRoute=false;}
        }else {_freightTruck!.Path.Clear();_freightTruck.Index=0;_freightTruck.Velocity=Vector3.Zero;_freightHasRoute=false;}
        _freightCabDriver.Visible=driver!=""&&FreightAboard(driver);
        _freightCargo.Visible=NativeGame.I(state,"cargo_count")>0;
    }
}
