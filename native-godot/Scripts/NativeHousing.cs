using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;

public sealed partial class NativeGame
{
    public bool IsResidentNight=>Now%Day>=20*Hour||Now%Day<7*Hour;
    public double ResidentAgeDays(JsonNode person)=>Math.Max(0,(Now-D(person,"born_ts",Now))/Day);
    public string ResidentLifeStage(JsonNode person)=>ResidentAgeDays(person)<7?"儿童":ResidentAgeDays(person)<60?"成年":"老年";
    public bool IsResidentialHome(JsonNode house)=>Rules["CATALOG"]![S(house,"catalog_key")] is JsonNode spec&&S(spec,"category")=="住宅";
    public int ResidentHomeCapacity(JsonNode house)=>IsResidentialHome(house)&&S(house,"status")=="ready"?I(Spec("CATALOG",S(house,"catalog_key")),"population"):0;
    public JsonObject? ResidentHome(JsonNode person)=>Rows("town_buildings").FirstOrDefault(b=>S(b!,"building_id")==S(person,"home_building_id")&&ResidentHomeCapacity(b!)>0)?.AsObject();
    public IEnumerable<JsonNode> HomeMembers(string buildingId)=>People.Where(p=>S(p!,"role")=="居民"&&S(p!,"home_building_id")==buildingId).Select(p=>p!);

    // 保留住房身份；路断只使入口暂时不可达，不会自动把家搬到别处。
    public Cell? HomeRoadEntrance(JsonNode house)
    {
        int x=I(house,"cx"),y=I(house,"cy"),w=I(house,"w"),h=I(house,"h"),orientation=I(house,"orientation");
        bool DoorSide(Cell c)=>orientation switch{0=>c.Y==y+h,1=>c.X==x-1,2=>c.Y==y-1,_=>c.X==x+w};
        var entrance=Frontage(house).Where(Connected().Contains).OrderByDescending(DoorSide).ThenBy(c=>Math.Abs(c.X-(x+(w-1)/2.0))+Math.Abs(c.Y-(y+(h-1)/2.0))).ThenBy(c=>c.Y).ThenBy(c=>c.X).ToArray();
        return entrance.Length==0?null:entrance[0];
    }

    List<List<JsonNode>> ResidentFamilyGroups()
    {
        var residents=People.Where(p=>S(p!,"role")=="居民").Select(p=>p!).OrderBy(p=>S(p,"person_id"),StringComparer.Ordinal).ToList();
        var parent=residents.ToDictionary(p=>S(p,"person_id"),p=>S(p,"person_id"));
        string Find(string id){while(parent[id]!=id)id=parent[id];return id;}
        void Join(string a,string b){if(!parent.ContainsKey(b))return;string pa=Find(a),pb=Find(b);if(pa!=pb)parent[string.CompareOrdinal(pa,pb)<0?pb:pa]=string.CompareOrdinal(pa,pb)<0?pa:pb;}
        foreach(var p in residents){string id=S(p,"person_id");Join(id,S(p,"partner_id"));if(ResidentAgeDays(p)<7&&p["parents"] is JsonArray parents)foreach(var q in parents)Join(id,q!.GetValue<string>());}
        return residents.GroupBy(p=>Find(S(p,"person_id"))).Select(g=>g.ToList()).OrderByDescending(g=>g.Count).ThenBy(g=>S(g[0],"person_id"),StringComparer.Ordinal).ToList();
    }

    void InitializeResidentHousing()
    {
        if(I(Meta,"native_housing_version")<1){
            // 原版初始居民已经存在；只在首次迁移补齐他们的实体住宅。
            PairResidentAdults();
            EnsureResidentHomes();
            foreach(var family in ResidentFamilyGroups()){
                if(family.All(p=>ResidentHome(p)!=null)&&family.Select(p=>S(p,"home_building_id")).Distinct().Count()==1)continue;
                string key=family.Count<=I(Spec("CATALOG","cream_home"),"population")?"cream_home":"glass_home";
                if(family.Count>I(Spec("CATALOG",key),"population")){Commons["housing_status"]="现有家庭超过住宅容量，需要安排更大的住所";continue;}
                var plot=StarterHomePlot(key);if(plot==null){Commons["housing_status"]="没有合法临街地块，等待开放道路或建造住宅";break;}
                PlaceTown(key,plot.Value.X,plot.Value.Y,true,plot.Value.Orientation);
                var house=Rows("town_buildings").Last()!;house["placed_ts"]=Now-D(Spec("CATALOG",key),"build_hours")*Hour;house["finish_ts"]=Now;house["status"]="ready";house["starter_home"]=true;
                EnsureResidentHomes();
            }
            Meta["native_housing_version"]=1;
        }
        EnsureResidentHomes();
    }

    (int X,int Y,int Orientation)? StarterHomePlot(string key)
    {
        var origin=Portal("estate");var candidates=new HashSet<Cell>();
        int size=Spec("CATALOG",key)["footprint"]![0]!.GetValue<int>();
        foreach(var road in Connected())for(int offset=0;offset<size;offset++)foreach(var c in new[]{new Cell(road.X-offset,road.Y+1),new Cell(road.X-offset,road.Y-size),new Cell(road.X+1,road.Y-offset),new Cell(road.X-size,road.Y-offset)})candidates.Add(c);
        foreach(var c in candidates.OrderBy(c=>Math.Abs(c.X-origin.X)+Math.Abs(c.Y-origin.Y)).ThenBy(c=>c.Y).ThenBy(c=>c.X)){
            if(PlacementReason(key,c.X,c.Y)!="")continue;
            bool South=Enumerable.Range(0,size).Any(i=>Connected().Contains(new(c.X+i,c.Y+size))),West=Enumerable.Range(0,size).Any(i=>Connected().Contains(new(c.X-1,c.Y+i))),North=Enumerable.Range(0,size).Any(i=>Connected().Contains(new(c.X+i,c.Y-1)));
            int orientation=South?0:West?1:North?2:3;
            return(c.X,c.Y,orientation);
        }
        return null;
    }

    public void EnsureResidentHomes()
    {
        var houses=Rows("town_buildings").Where(b=>ResidentHomeCapacity(b!)>0).Select(b=>b!).OrderBy(b=>D(b,"placed_ts")).ThenBy(b=>S(b,"building_id"),StringComparer.Ordinal).ToList();
        bool changed=false;int waiting=0;
        foreach(var family in ResidentFamilyGroups()){
            var ids=family.Select(p=>S(p,"person_id")).ToHashSet();
            bool Fits(JsonNode b)=>ResidentHomeCapacity(b)>=family.Count+HomeMembers(S(b,"building_id")).Count(p=>!ids.Contains(S(p,"person_id")));
            // 一旦有家就优先保留；新家庭仅在容量允许时一起迁入其中一人的家。
            var existing=family.Select(ResidentHome).Where(b=>b!=null).Select(b=>b!).DistinctBy(b=>S(b,"building_id")).ToList();
            var house=existing.FirstOrDefault(Fits)??houses.FirstOrDefault(b=>HomeRoadEntrance(b)!=null&&Fits(b)&&!HomeMembers(S(b,"building_id")).Any());
            string household=family.Select(p=>S(p,"household_id")).FirstOrDefault(id=>id!="")??"family-"+S(family[0],"person_id");
            if(house==null){waiting++;foreach(var p in family){p["household_id"]??=household;if(ResidentHome(p)==null){if(S(p,"home_building_id")!="")changed=true;p["home_building_id"]="";}p["housing_status"]="等待可容纳全家的住宅";}continue;}
            string homeId=S(house,"building_id"),zone=Zone(new(I(house,"cx"),I(house,"cy")));if(zone=="")zone="estate";
            // 住户家庭以固定住所标识，成年孩子迁出后不会继续共用父母家的家庭ID。
            household="family-"+homeId;
            foreach(var p in family){if(S(p,"home_building_id")!=homeId||S(p,"household_id")!=household)changed=true;p["home_building_id"]=homeId;p["household_id"]=household;p["home_zone"]=zone;p["housing_status"]="已安家";}
        }
        Commons["housing_waiting_families"]=waiting;Commons["housing_status"]=waiting>0?$"{waiting}个家庭等待可容纳全家的住宅":"所有居民已安家";
        if(changed)WorldRevision++;
    }

    bool FamilyHasBirthRoom(JsonNode mother,bool reserve)
    {
        var house=ResidentHome(mother);if(house==null)return false;string id=S(house,"building_id");var partner=People.FirstOrDefault(p=>S(p!,"person_id")==S(mother,"partner_id"));if(partner==null||S(partner,"home_building_id")!=id)return false;var members=HomeMembers(id).ToArray();
        int pending=members.Count(p=>D(p,"pregnant_until")>0&&(!reserve?S(p,"person_id")!=S(mother,"person_id"):true));
        return members.Length+pending<ResidentHomeCapacity(house);
    }

    double NextResidentRoutineBoundary()
    {
        double today=Math.Floor(Now/Day)*Day,time=Now-today;return time<7*Hour?today+7*Hour:time<20*Hour?today+20*Hour:today+Day+7*Hour;
    }
    static double ResidentNightTime(double time)
    {
        double days=Math.Floor(time/Day),hour=time-days*Day;return days*11*Hour+Math.Min(hour,7*Hour)+Math.Max(0,hour-20*Hour);
    }
    void PauseResidentWorkAtNight(double start,double end)
    {
        double night=ResidentNightTime(end)-ResidentNightTime(start);if(night<=0)return;
        foreach(var job in Jobs){job!["due"]=D(job,"due")+night;if(D(job,"arrive")>start)job["arrive"]=D(job,"arrive")+night;}
    }
}
