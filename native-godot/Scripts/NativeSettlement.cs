using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

public sealed partial class NativeGame
{
    public JsonObject Commons=>Meta["settlement"]!.AsObject();
    JsonObject PublicStock=>Commons["stock"]!.AsObject();
    public JsonArray Jobs=>Commons["jobs"]!.AsArray();
    static JsonObject Node(object value)=>JsonSerializer.SerializeToNode(value)!.AsObject();
    void InitializeSettlement()
    {
        Meta["settlement"]??=Node(new{enabled=true,next_tick=Now+Hour,funds=3000,stock=new{food=60,seed=12,wood=30,stone=30,feed=16,herd=2},jobs=Array.Empty<object>(),serial=0,last_day=(int)(Now/Day),births=0,completed=0,history=Array.Empty<object>(),blocked="",next_herd_birth=Now+3*Day});
        foreach(var p in People.Where(p=>S(p!,"role")=="居民")) {
            p!["born_ts"]??=Now-12*Day;p["sex"]??=Random()<.5?"女":"男";p["partner_id"]??="";p["job_id"]??="";p["profession"]??="农民";p["pregnant_until"]??=0;p["family_cooldown"]??=Now;
        }
    }
    public int PopulationCapacity=>Rows("town_buildings").Sum(b=>ResidentHomeCapacity(b!));
    string ToggleAutonomy()
    {
        Commons["enabled"]=!B(Commons,"enabled");
        if(!B(Commons,"enabled"))foreach(var j in Jobs.ToArray())CancelJob(j!,"自治暂停");
        Commons["next_tick"]=Now+Hour;Commons["last_day"]=(int)(Now/Day);return B(Commons,"enabled")?"自治已开启":"自治已暂停，预约资源已退回";
    }
    bool Affordable(JsonObject cost)=>cost.All(p=>(p.Key=="funds"?I(Commons,"funds"):I(PublicStock,p.Key))>=p.Value!.GetValue<int>());
    void PublicChange(JsonObject cost,int sign){foreach(var pair in cost){var stock=pair.Key=="funds"?Commons:PublicStock;stock[pair.Key]=I(stock,pair.Key)+sign*pair.Value!.GetValue<int>();}}
    void EndJob(JsonNode job){var person=People.FirstOrDefault(p=>S(p!,"person_id")==S(job,"worker"));if(person!=null)person["job_id"]="";Jobs.Remove(job);}
    void CancelJob(JsonNode job,string reason){PublicChange(job["cost"]!.AsObject(),1);EndJob(job);Commons["blocked"]=reason;}
    void Work(string kind,string zone,JsonObject cost,double hours,JsonObject? payload=null)
    {
        if(IsResidentNight||Jobs.Count>=3||Jobs.Any(j=>S(j!,"kind")==kind))return;
        string preferred=kind switch{"crop" or "harvest"=>"农民","ranch"=>"牧民","trade"=>"商人","gather"=>"工匠","build"=>"建筑工",_=>""};
        var worker=People.Where(p=>S(p!,"role")=="居民"&&Now-D(p!,"born_ts")>=7*Day&&S(p!,"job_id")==""&&!B(p!,"freight_reserved")).OrderByDescending(p=>S(p!,"profession")==preferred).FirstOrDefault();
        if(worker==null)return;
        if(!Affordable(cost)){Commons["blocked"]="公共资源不足，等待生产或贸易";return;}
        PublicChange(cost,-1);int serial=I(Commons,"serial")+1;Commons["serial"]=serial;string id="job-"+serial;worker["job_id"]=id;
        Jobs.Add(Node(new{id,kind,zone,worker=S(worker,"person_id"),start=Now,arrive=Now+Hour,due=Now+(1+hours)*Hour,cost=cost,payload=payload??new JsonObject()}));
    }
    List<Cell> RoadPlan(IEnumerable<Cell> targets)
    {
        var linked=Connected();var roads=RoadCells();if(linked.Count==0)return Allowed(Portal("estate"))?new(){Portal("estate")}:new();
        var queue=new PriorityQueue<Cell,int>();var dist=new Dictionary<Cell,int>();var parents=new Dictionary<Cell,Cell?>();
        foreach(var c in targets)if(Allowed(c)){if(linked.Contains(c))return new();dist[c]=0;parents[c]=null;queue.Enqueue(c,0);}
        Cell? goal=null;
        while(queue.TryDequeue(out var c,out var priority)) {
            if(priority!=dist[c])continue;if(linked.Contains(c)){goal=c;break;}
            foreach(var d in Directions){var next=new Cell(c.X+d.X,c.Y+d.Y);if(!Allowed(next))continue;int value=priority+(roads.Contains(next)?1:10);if(value<dist.GetValueOrDefault(next,int.MaxValue)){dist[next]=value;parents[next]=c;queue.Enqueue(next,value);}}
        }
        var result=new List<Cell>();while(goal!=null){if(!roads.Contains(goal.Value))result.Add(goal.Value);goal=parents[goal.Value];}return result;
    }
    void CompleteJob(JsonNode j)
    {
        string kind=S(j,"kind"),zone=S(j,"zone");var payload=j["payload"]!.AsObject();
        if(!People.Any(p=>S(p!,"person_id")==S(j,"worker"))||zone!="estate"&&!Effective(zone)){CancelJob(j,"人员离开或工作地点不通路");return;}
        if(kind=="crop") {
            PublicStock["food"]=Math.Min(1000,I(PublicStock,"food")+30);PublicStock["seed"]=Math.Min(100,I(PublicStock,"seed")+3);PublicStock["feed"]=Math.Min(300,I(PublicStock,"feed")+6);
        } else if(kind=="gather"){PublicStock["wood"]=Math.Min(300,I(PublicStock,"wood")+6);PublicStock["stone"]=Math.Min(300,I(PublicStock,"stone")+4);}
        else if(kind=="trade")Commons["funds"]=I(Commons,"funds")+36;
        else if(kind=="ranch") {
            PublicStock["food"]=Math.Min(1000,I(PublicStock,"food")+I(PublicStock,"herd")*4);
            if(I(PublicStock,"herd")<12&&I(PublicStock,"feed")>=4&&Now>=D(Commons,"next_herd_birth")){PublicStock["feed"]=I(PublicStock,"feed")-4;PublicStock["herd"]=I(PublicStock,"herd")+1;Commons["next_herd_birth"]=Now+3*Day;WorldRevision++;}
        } else if(kind=="open") {
            string target=S(payload,"target_zone");if(ZoneLevel(target)>0)PublicChange(j["cost"]!.AsObject(),1);else{SetZone(target,1);WorldRevision++;}
        } else if(kind=="build") {
            var b=payload["building"]!;
            try{PlaceTown(S(b,"catalog_key"),I(b,"cx"),I(b,"cy"),true);}catch(InvalidOperationException){CancelJob(j,"选址变化，退回建材");return;}
        } else if(kind=="road") {
            var cells=payload["cells"]!.AsArray().Select(c=>new Cell(c![0]!.GetValue<int>(),c[1]!.GetValue<int>())).ToList();var roads=RoadCells();
            if(cells.Any(c=>!Allowed(c))){CancelJob(j,"道路位置被占用或街区未开放");return;}
            foreach(var c in cells){if(!roads.Contains(c))AppendRoad(c,0);else Commons["funds"]=I(Commons,"funds")+10;}
        } else if(kind=="harvest") {
            var batch=Meta["field_batch"]!.AsObject();
            if(D(Meta,"field_ready_ts")==0||D(Meta,"field_expiry_ts")<=Now){CancelJob(j,"待收作物已过期或被收走");return;}
            if(Rows("inventory").Sum(b=>I(b!,"quantity"))+batch.Sum(p=>p.Value!.GetValue<int>())>Storage){CancelJob(j,"仓库已满");return;}
            foreach(var p in batch)Add(p.Key,p.Value!.GetValue<int>());Meta["field_batch"]=new JsonObject();Meta["field_ready_ts"]=0;Meta["field_expiry_ts"]=0;
        }
        Commons["completed"]=I(Commons,"completed")+1;Log("居民完成："+kind);EndJob(j);
    }
    void TickSettlement()
    {
        if(!B(Commons,"enabled"))return;
        if(!IsResidentNight)foreach(var job in Jobs.ToArray())if(D(job!,"due")<=Now)CompleteJob(job!);
        int today=(int)(Now/Day),residents=People.Count(p=>S(p!,"role")=="居民");
        if(today>I(Commons,"last_day")){PublicStock["food"]=Math.Max(0,I(PublicStock,"food")-residents*(today-I(Commons,"last_day")));Commons["last_day"]=today;}
        Families();
        EnsureResidentHomes();
        if(IsResidentNight){Commons["next_tick"]=Now+Hour;return;}
        if(D(Meta,"field_ready_ts")>0&&Effective("fields"))Work("harvest","fields",new JsonObject(),.25);
        if(I(PublicStock,"food")<Math.Max(80,residents*8))Work("crop",Effective("fields")?"fields":"estate",Node(new{seed=1}),8);
        if(I(PublicStock,"wood")<24||I(PublicStock,"stone")<20)Work("gather",Effective("industry")?"industry":"estate",Node(new{food=1}),4);
        if((PopulationCapacity-residents<3||I(Commons,"housing_waiting_families")>0)&&!Rows("town_buildings").Any(b=>S(b!,"status")=="building")) {
            var roads=RoadCells();bool found=false;
            foreach(var c in roads.OrderBy(c=>c.X).ThenBy(c=>c.Y)) {
                foreach(var at in new[]{new Cell(c.X,c.Y+1),new Cell(c.X+1,c.Y)}) {
                    var b=Node(new{cx=at.X,cy=at.Y,w=2,h=2,orientation=0,catalog_key="cream_home"});
                    if(Footprint(b).All(p=>Allowed(p)&&!roads.Contains(p))){Work("build","estate",Node(new{funds=240,wood=12,stone=8}),3,Node(new{building=b}));found=true;break;}
                }if(found)break;
            }
        }
        var linked=Connected();List<Cell>? path=null;
        if(!Jobs.Any(j=>S(j!,"kind")=="road")) {
            foreach(var pair in root["portals"]!.AsObject())if(ZoneLevel(pair.Key)>0&&!linked.Contains(Portal(pair.Key))){path=RoadPlan(new[]{Portal(pair.Key)});break;}
            if(path==null)foreach(var b in Rows("town_buildings"))if(!Frontage(b!).Any(linked.Contains)){path=RoadPlan(Frontage(b!));break;}
            if(path?.Count>0)Work("road","estate",Node(new{funds=path.Count*10}),Math.Max(1,path.Count/6.0),new JsonObject{{"cells",new JsonArray(path.Select(c=>(JsonNode)new JsonArray(c.X,c.Y)).ToArray())}});
        }
        if(Effective("ranch")&&I(PublicStock,"herd")>0)Work("ranch","ranch",Node(new{feed=Math.Max(2,(int)Math.Ceiling(I(PublicStock,"herd")/2.0))}),6);
        if(I(PublicStock,"food")>Math.Max(45,residents*6))Work("trade",Effective("market")?"market":"estate",Node(new{food=12}),2);
        foreach(string zone in new[]{"fields","homes","ranch","market","park","industry","city","station"})if(ZoneLevel(zone)==0){Work("open","estate",Node(new{funds=I(root["zones"]![zone]!,"cost")}),4,Node(new{target_zone=zone}));break;}
        Commons["next_tick"]=Now+Hour;
    }
    void PairResidentAdults()
    {
        var residents=People.Where(p=>S(p!,"role")=="居民").Select(p=>p!).ToList();
        foreach(var p in residents)if(S(p,"partner_id")!=""&&!residents.Any(other=>S(other,"person_id")==S(p,"partner_id")))p["partner_id"]="";
        var females=residents.Where(p=>Now-D(p,"born_ts")>=7*Day&&S(p,"sex")=="女"&&S(p,"partner_id")=="").ToList();
        var males=residents.Where(p=>Now-D(p,"born_ts")>=7*Day&&S(p,"sex")=="男"&&S(p,"partner_id")=="").ToList();
        foreach(var woman in females) {
            bool Related(JsonNode a,JsonNode b){var ap=a["parents"]?.AsArray().Select(v=>v!.ToString()).ToHashSet()??new();var bp=b["parents"]?.AsArray().Select(v=>v!.ToString()).ToHashSet()??new();return ap.Overlaps(bp)||ap.Contains(S(b,"person_id"))||bp.Contains(S(a,"person_id"));}
            var man=males.FirstOrDefault(m=>!Related(woman,m));
            if(man==null)continue;woman["partner_id"]=S(man,"person_id");man["partner_id"]=S(woman,"person_id");males.Remove(man);
        }
    }
    void Families()
    {
        PairResidentAdults();EnsureResidentHomes();
        var residents=People.Where(p=>S(p!,"role")=="居民").Select(p=>p!).ToList();
        foreach(var woman in residents.Where(p=>S(p,"sex")=="女"&&S(p,"partner_id")!="").ToArray()) {
            if(D(woman,"pregnant_until")>0&&D(woman,"pregnant_until")<=Now) {
                if(residents.Count>=PopulationCapacity||People.Count>=80||I(PublicStock,"food")<6||!FamilyHasBirthRoom(woman,false)){woman["pregnant_until"]=Now+Day;continue;}
                var child=woman.DeepClone();string ident=NextId("resident");child["person_id"]=ident;child["name"]=S(woman,"name")[..1]+"小禾";child["sex"]=Random()<.5?"女":"男";child["born_ts"]=Now;child["entered_ts"]=Now;child["partner_id"]="";child["pregnant_until"]=0;child["job_id"]="";child["profession"]="孩童";child["parents"]=new JsonArray(S(woman,"person_id"),S(woman,"partner_id"));
                child["at_home"]=false;child["inside_home_building_id"]="";child["home_arrived_ts"]=0;child["family_cooldown"]=Now+7*Day;
                child["freight_reserved"]=false;child["freight_vehicle_id"]="";
                People.Add(child);residents.Add(child);PublicStock["food"]=I(PublicStock,"food")-6;woman["pregnant_until"]=0;woman["family_cooldown"]=Now+14*Day;Commons["births"]=I(Commons,"births")+1;WorldRevision++;Log("居民家庭迎来新生儿");
            } else if(D(woman,"pregnant_until")==0&&D(woman,"family_cooldown")<=Now&&Now-D(woman,"born_ts")>=7*Day&&Now-D(woman,"born_ts")<60*Day&&residents.Count+residents.Count(p=>D(p,"pregnant_until")>0)<PopulationCapacity&&I(PublicStock,"food")>=residents.Count*3+6&&FamilyHasBirthRoom(woman,true))woman["pregnant_until"]=Now+3*Day;
        }
        foreach(var child in residents)if(S(child,"profession")=="孩童"&&Now-D(child,"born_ts")>=7*Day){child["profession"]="农民";WorldRevision++;}
    }
}
