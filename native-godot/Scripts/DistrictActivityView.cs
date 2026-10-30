using Godot;
using System;
using System.Linq;
using System.Collections.Generic;
using System.Text.Json.Nodes;

public partial class Main
{
    Node3D? _districtWalkways;
    double _districtRefresh;
    readonly Dictionary<string,Label3D> _workerLabels=new();
    void MigrateSceneCoordinates()
    {
        if(NativeGame.I(Data.Meta,"native_spatial_version")>=2)return;
        void Expand(JsonNode node,params string[] keys){foreach(string key in keys)if(node[key]!=null)node[key]=NativeGame.D(node,key)*WorldData.LayoutScale;}
        foreach(var p in Data.People)Expand(p!,"home_door_x","home_door_z");
        foreach(var w in Game.Rows("waste"))Expand(w!,"native_x","native_z");
        Expand(Data.Meta,"manager_native_x","manager_native_z");Data.Meta["native_spatial_version"]=2;
    }
    static uint PersonHash(string id){uint hash=2166136261;foreach(char c in id){hash^=c;hash*=16777619;}return hash;}
    static float WorkOffset(JsonNode person)=>NativeGame.S(person,"role")=="通勤者"?(NativeGame.S(person,"person_id").EndsWith("-0")?-.8f:.8f):(PersonHash(NativeGame.S(person,"person_id"))%3-1f)*1.15f;
    NativeGame.UrbanAccess? WorkAccess(JsonNode person,string? zone=null)
    {
        zone??=NativeGame.S(person,"destination",Game.DistrictDestination(person));
        var options=Game.UrbanAccessPoints().Where(a=>a.Zone==zone&&a.Entrance!=null).ToArray();
        return options.Length==0?null:options[PersonHash(NativeGame.S(person,"person_id"))%options.Length];
    }
    public Vector3 DistrictWorkPosition(JsonNode person)
    {
        string zone=NativeGame.S(person,"destination",Game.DistrictDestination(person));var access=WorkAccess(person,zone);
        var origin=access==null?Data.PavementPoint(Data.Portals.GetValueOrDefault(zone,Data.Portals["estate"])):new Vector3((float)access.DoorX*WorldData.GridSize,0,(float)access.DoorY*WorldData.GridSize);
        return origin+Vector3.Right*WorkOffset(person);
    }
    public List<Vector3> BeginPublicRoute(RoadAgent actor,out Vector2I start)
    {
        start=WorldData.Grid(actor.Position);var prefix=new List<Vector3>();if(Data.Roads.ContainsKey(start))return prefix;
        var access=Game.UrbanAccessPoints().FirstOrDefault(a=>a.Id==actor.WorkSiteId&&a.Entrance!=null);
        if(access!=null&&actor.Position.DistanceTo(new((float)access.DoorX*WorldData.GridSize,0,(float)access.DoorY*WorldData.GridSize))<WorldData.GridSize*2){
            start=new(access.Entrance!.Value.X,access.Entrance.Value.Y);
            if(access.Walkway!=null)prefix.AddRange(access.Walkway.Reverse().Select(p=>new Vector3((float)p.X*WorldData.GridSize,0,(float)p.Y*WorldData.GridSize)));
            else prefix.Add(WorldData.Cell(start));
        }else if(Data.Roads.Count>0)start=Data.Roads.Keys.MinBy(c=>WorldData.Cell(c).DistanceSquaredTo(actor.Position));
        return prefix;
    }
    bool TryAssignDaytimeRoute(RoadAgent actor)
    {
        if(actor.IsCar)return false;
        var person=Data.People.FirstOrDefault(p=>NativeGame.S(p!,"person_id")==actor.Ident);if(person==null)return false;
        bool commuter=NativeGame.S(person,"role")=="通勤者";
        if(Game.IsResidentNight&&!commuter&&NativeGame.S(person,"role")=="居民")return false;
        if(Data.Roads.Count==0)return false;
        actor.ReturningHome=false;actor.Path.Clear();actor.Index=0;actor.Wait=6;
        string zone=Game.IsResidentNight&&commuter?"station":NativeGame.S(person,"destination","estate");
        var job=Game.Jobs.FirstOrDefault(j=>NativeGame.S(j!,"worker")==actor.Ident);
        if(job!=null&&!Game.IsResidentNight)zone=NativeGame.S(job,"zone",zone);
        var access=WorkAccess(person,zone);
        Vector2I goal=access?.Entrance is NativeGame.Cell e?new(e.X,e.Y):Data.Portals.GetValueOrDefault(zone,Data.Portals["estate"]);
        var target=access==null?Data.PavementPoint(goal)+Vector3.Right*WorkOffset(person):new Vector3((float)access.DoorX*WorldData.GridSize,0,(float)access.DoorY*WorldData.GridSize)+Vector3.Right*WorkOffset(person);
        if(actor.Position.DistanceTo(target)<.65f){
            actor.WorkSiteId=access?.Id??"";
            actor.ResidentialActivity=Game.IsResidentNight&&commuter?"已下班离镇":zone switch{"fields"=>"照料农田","ranch"=>"照看牲畜","market"=>"营业 / 采购","industry"=>"生产 / 装卸","city"=>"上班","park"=>"休憩 / 养护",_=>"日常活动"};
            if(Game.IsResidentNight&&commuter){actor.Visual.Hide();actor.CollisionLayer=0;actor.CollisionMask=0;}
            return true;
        }
        actor.Visual.Show();actor.CollisionLayer=2;actor.CollisionMask=7;
        var path=BeginPublicRoute(actor,out var start);var streetPath=Data.PedestrianRoute(path.Count>0?path[^1]:actor.Position,start,goal,target);
        if(streetPath.Count==0){actor.ResidentialActivity="等待道路接通";return true;}
        path.AddRange(streetPath);
        // Enter/leave a building by its public front path, then use the pavement.
        if(access!=null&&actor.WorkSiteId==access.Id&&actor.Position.DistanceTo(target)<2)path.Clear();
        else if(access?.Walkway!=null)path.AddRange(access.Walkway.Skip(1).Select(p=>new Vector3((float)p.X*WorldData.GridSize,0,(float)p.Y*WorldData.GridSize)));
        path.Add(target);actor.Path.AddRange(path);
        actor.WorkSiteId=access?.Id??"";
        actor.ResidentialActivity=Game.IsResidentNight&&commuter?"下班前往车站":"前往"+NativeGame.S(Data.Root["zones"]![zone]!,"name");return true;
    }
    void CreateDistrictWalkways()
    {
        _districtWalkways?.QueueFree();_districtWalkways=new Node3D{Name="BuildingAccessWalkways"};AddChild(_districtWalkways);
        var material=new StandardMaterial3D{AlbedoColor=new Color("d4c9a7")};
        foreach(var access in Game.UrbanAccessPoints())if(access.Entrance is NativeGame.Cell entry){
            var points=access.Walkway?.Select(p=>new Vector3((float)p.X*WorldData.GridSize,0,(float)p.Y*WorldData.GridSize)).ToList()??new(){WorldData.Cell(new(entry.X,entry.Y)),new((float)access.DoorX*WorldData.GridSize,0,(float)access.DoorY*WorldData.GridSize)};
            for(int i=1;i<points.Count;i++){
            var from=points[i-1];var to=points[i];
            var direction=to-from;direction.Y=0;float length=direction.Length();if(length<.5f)continue;
            // Only fill the setback beyond the pavement; keep medians and lanes clear.
            var start=from+direction.Normalized()*(i==1?Math.Min(length,WorldData.GridSize*.45f):0);length=start.DistanceTo(to);
            if(length>.1f)_districtWalkways.AddChild(new MeshInstance3D{Mesh=new BoxMesh{Size=new(1.5f,.05f,length)},MaterialOverride=material,Position=(start+to)/2+Vector3.Up*.04f,Rotation=new(0,Mathf.Atan2(direction.X,direction.Z),0)});
            }
        }
    }
    void UpdateDistrictActivity(double delta)
    {
        _districtRefresh-=delta;if(_districtRefresh>0)return;_districtRefresh=.5;
        foreach(var actor in Agents.Where(a=>!a.IsCar)){
            var person=Data.People.FirstOrDefault(p=>NativeGame.S(p!,"person_id")==actor.Ident);if(person==null||NativeGame.S(person,"role")!="通勤者")continue;
            if(!_workerLabels.TryGetValue(actor.Ident,out var label)){
                label=new Label3D{Font=new SystemFont{FontNames=new[]{"Microsoft YaHei"}},FontSize=24,PixelSize=.012f,Position=Vector3.Up*2.3f,Billboard=BaseMaterial3D.BillboardModeEnum.Enabled,Modulate=new Color("fff0ca")};actor.AddChild(label);_workerLabels[actor.Ident]=label;
            }
            label.Text=NativeGame.S(person,"name")+" · "+NativeGame.S(person,"profession");label.Visible=actor.Visual.Visible;
        }
    }
}
