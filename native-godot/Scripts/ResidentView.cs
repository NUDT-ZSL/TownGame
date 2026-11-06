using Godot;
using System;
using System.Linq;
using System.Text.Json.Nodes;

public partial class Main
{
    Godot.Environment? _residentialSky;
    DirectionalLight3D? _residentialSun;
    string _selectedResidence="";
    double _familyRefresh;
    string GameClockText=>$"{(int)(Game.Now%86400/3600):00}:{(int)(Game.Now%3600/60):00}";
    public JsonNode? Resident(string id)=>Data.People.FirstOrDefault(p=>NativeGame.S(p!,"person_id")==id&&NativeGame.S(p!,"role")=="居民");
    Vector3 ResidentSpawn(JsonNode person)
    {
        if(NativeGame.S(person,"role")=="通勤者"&&!Game.IsResidentNight)return DistrictWorkPosition(person);
        var home=Game.ResidentHome(person);var entry=home==null?null:Game.HomeRoadEntrance(home);
        if(entry!=null){
            var members=Game.HomeMembers(NativeGame.S(home!,"building_id")).OrderBy(p=>NativeGame.S(p,"person_id"),StringComparer.Ordinal).ToArray();int index=Array.FindIndex(members,p=>NativeGame.S(p,"person_id")==NativeGame.S(person,"person_id"));
            var tangent=entry.Value.X<NativeGame.I(home!,"cx")||entry.Value.X>=NativeGame.I(home!,"cx")+NativeGame.I(home!,"w")?Vector3.Back:Vector3.Right;
            var towardHome=entry.Value.X<NativeGame.I(home!,"cx")?Vector3.Right:entry.Value.X>=NativeGame.I(home!,"cx")+NativeGame.I(home!,"w")?Vector3.Left:entry.Value.Y<NativeGame.I(home!,"cy")?Vector3.Back:Vector3.Forward;
            return WorldData.Cell(new(entry.Value.X,entry.Value.Y))+towardHome*WorldData.Sidewalk+tangent*(index-(members.Length-1)*.5f)*.65f;
        }
        string zone=NativeGame.S(person,"role")=="居民"?NativeGame.S(person,"home_zone","estate"):"station";return Data.PavementPoint(Data.Portals.TryGetValue(zone,out var portal)?portal:Data.Portals["estate"]);
    }
    public Vector3 ResidenceDoor(JsonNode house,NativeGame.Cell entry)
    {
        var road=WorldData.Cell(new(entry.X,entry.Y));
        float cx=(NativeGame.I(house,"cx")+NativeGame.I(house,"w")*.5f)*WorldData.GridSize,cz=(NativeGame.I(house,"cy")+NativeGame.I(house,"h")*.5f)*WorldData.GridSize;
        float halfX=NativeGame.I(house,"w")*WorldData.GridSize/2,halfZ=NativeGame.I(house,"h")*WorldData.GridSize/2;
        // 停在建筑碰撞盒外的门前；只有走到此处才切换到室内状态。
        if(entry.X<NativeGame.I(house,"cx"))return new(cx-halfX-.9f,0,cz);
        if(entry.X>=NativeGame.I(house,"cx")+NativeGame.I(house,"w"))return new(cx+halfX+.9f,0,cz);
        return new(cx,0,cz+(entry.Y<NativeGame.I(house,"cy")?-halfZ-.9f:halfZ+.9f));
    }
    bool TryAssignResidentialRoute(RoadAgent actor)
    {
        if(actor.IsCar)return false;var person=Resident(actor.Ident);
        if(person==null||!Game.IsResidentNight)return false;
        actor.Path.Clear();actor.Index=0;actor.Wait=3;actor.ReturningHome=true;
        var home=Game.ResidentHome(person);
        if(home==null){actor.ResidentialActivity="等待分配住宅";return true;}
        var entry=Game.HomeRoadEntrance(home);
        if(entry==null||Data.Roads.Count==0){actor.ResidentialActivity="归家道路中断，等待修复";return true;}
        actor.HomeDoor=ResidenceDoor(home,entry.Value);
        if(actor.InsideHome)return true;
        if(actor.Position.DistanceTo(actor.HomeDoor)<.5f){actor.EnterHome(person);return true;}
        var prefix=BeginPublicRoute(actor,out var start);
        if(!Data.Roads.ContainsKey(start)){
            start=Data.Roads.Keys.MinBy(p=>(WorldData.Cell(p)-actor.Position).LengthSquared());
            if(WorldData.Cell(start).DistanceTo(actor.Position)>WorldData.GridSize*2){actor.ResidentialActivity="附近道路中断，等待修复";return true;}
        }
        var goal=new Vector2I(entry.Value.X,entry.Value.Y);var route=Data.PedestrianRoute(prefix.Count>0?prefix[^1]:actor.Position,start,goal,actor.HomeDoor);
        if(route.Count==0){actor.ResidentialActivity="归家道路中断，等待修复";return true;}
        // 从相邻道路接近门前，不穿过住宅占地；入室后不再参与道路碰撞。
        route.Add(actor.HomeDoor);
        actor.Path.AddRange(prefix);actor.Path.AddRange(route);actor.WorkSiteId="";actor.ResidentialActivity="正在回家";return true;
    }
    string ResidenceName(JsonNode house)=>$"{NativeGame.S(Game.Rules["CATALOG"]![NativeGame.S(house,"catalog_key")]!,"name")} · {NativeGame.I(house,"cx")},{NativeGame.I(house,"cy")}";
    string ResidentAge(JsonNode person){double days=Math.Max(0,(Game.Now-NativeGame.D(person,"born_ts",Game.Now))/86400);return $"{Math.Floor(days):0}游戏日（{(days<7?"儿童":days<60?"成年":"老年")}）";}
    string ResidentActivity(JsonNode person)
    {
        var actor=Agents.FirstOrDefault(a=>a.Ident==NativeGame.S(person,"person_id"));
        if(actor?.InsideHome==true)return actor.ResidentialActivity;
        if(Game.IsFreightDriver(NativeGame.S(person,"person_id")))return FreightActivity(NativeGame.S(person,"person_id"));
        if(Game.IsResidentNight)return actor?.ResidentialActivity??"准备回家";
        return actor?.ResidentialActivity??"外出活动";
    }
    string ResidentDescription(JsonNode person)
    {
        var home=Game.ResidentHome(person);return $"{NativeGame.S(person,"name")} · {NativeGame.S(person,"sex")}\n年龄：{ResidentAge(person)} · {NativeGame.S(person,"profession")}\n住所：{(home==null?"等待分配":ResidenceName(home))}\n{ResidentActivity(person)}";
    }
    void SelectResidence(string id)
    {
        ShowManagement("家庭","residence:"+id,()=>{_selectedPet="";_selectedResidence=id;});
    }
    void FamilyControls()
    {
        var selected=Game.FindTownBuilding(_selectedResidence);
        if(selected!=null&&_managementEntryTarget=="interior:"+_selectedResidence&&CanShowResidenceInterior(selected)){
            ResidenceInteriorControls(selected);
            return;
        }
        if(AddFamilyViewChoice()){
            ResidentDirectoryControls();
            return;
        }
        if(selected!=null&&_managementEntryTarget=="residence:"+_selectedResidence&&Game.IsResidentialHome(selected))
            HouseholdResidenceControls(selected);
        else HousingDirectoryControls();
    }
    void UpdateResidentialView(double delta)
    {
        _familyRefresh+=delta;if(_familyRefresh<.5)return;_familyRefresh=0;
        UpdateAtmosphere();
    }
}
