using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Text;
using System.Security.Cryptography;

public sealed partial class NativeGame
{
    public readonly record struct Cell(int X,int Y);
    static readonly Cell[] Directions={new(0,-1),new(1,0),new(0,1),new(-1,0)};
    public Cell Portal(string name)=>new(root["portals"]![name]![0]!.GetValue<int>(),root["portals"]![name]![1]!.GetValue<int>());
    long mapRevision=-1;JsonObject? indexedState;HashSet<Cell>? roadCache,connectedCache,occupiedCache;
    void CheckMapCache(){if(mapRevision==WorldRevision&&ReferenceEquals(indexedState,State))return;mapRevision=WorldRevision;indexedState=State;roadCache=null;connectedCache=null;occupiedCache=null;}
    public HashSet<Cell> RoadCells(){CheckMapCache();return roadCache??=Rows("roads").Select(r=>new Cell(I(r!,"cx"),I(r!,"cy"))).ToHashSet();}
    HashSet<Cell> Connected()
    {
        CheckMapCache();if(connectedCache!=null)return connectedCache;
        var roads=RoadCells();var found=new HashSet<Cell>();var q=new Queue<Cell>();var start=Portal("estate");
        if(roads.Contains(start)){q.Enqueue(start);found.Add(start);}while(q.Count>0){var c=q.Dequeue();foreach(var d in Directions){var n=new Cell(c.X+d.X,c.Y+d.Y);if(roads.Contains(n)&&found.Add(n))q.Enqueue(n);}}return connectedCache=found;
    }
    public int ZoneLevel(string name)=>Rows("town_zones").Where(z=>S(z!,"zone_id")==name).Select(z=>I(z!,"level")).FirstOrDefault();
    public void SetZone(string name,int level){var zone=Rows("town_zones").First(z=>S(z!,"zone_id")==name)!;zone["level"]=level;}
    public bool Effective(string name)=>ZoneLevel(name)>0&&(name=="estate"||Connected().Contains(Portal(name)));
    string Zone(Cell c)
    {
        foreach(var pair in root["zones"]!.AsObject()) {
            var r=pair.Value!["rect"]!;double x=(c.X+.5)*48,y=(c.Y+.5)*48;
            if(x>=r[0]!.GetValue<double>()&&y>=r[1]!.GetValue<double>()&&x<r[0]!.GetValue<double>()+r[2]!.GetValue<double>()&&y<r[1]!.GetValue<double>()+r[3]!.GetValue<double>())return pair.Key;
        }return "";
    }
    IEnumerable<Cell> Footprint(JsonNode b){for(int y=I(b,"cy");y<I(b,"cy")+I(b,"h");y++)for(int x=I(b,"cx");x<I(b,"cx")+I(b,"w");x++)yield return new(x,y);}
    IEnumerable<Cell> Frontage(JsonNode b){var own=Footprint(b).ToHashSet();return own.SelectMany(c=>Directions.Select(d=>new Cell(c.X+d.X,c.Y+d.Y))).Where(c=>!own.Contains(c)).Distinct();}
    HashSet<Cell> Occupied(){CheckMapCache();return occupiedCache??=Rows("town_buildings").SelectMany(b=>Footprint(b!)).Concat(Meta["decorations"]?.AsArray().Select(d=>new Cell(I(d!,"cx"),I(d!,"cy")))??Enumerable.Empty<Cell>()).ToHashSet();}
    bool Allowed(Cell c)
    {
        if(c.X<0||c.Y<0||c.X>=125||c.Y>=80||Occupied().Contains(c))return false;
        string land=root["terrain"]![c.Y]![c.X]!.GetValue<string>();if(land!="grass"&&land!="zone")return false;
        var zone=Zone(c);return zone==""||ZoneLevel(zone)>0||RoadCells().Contains(c);
    }
    public string NextId(string kind)
    {
        long serial=(long)D(Meta,"native_serial")+1;Meta["native_serial"]=serial;
        return Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes($"{kind}:{Now:R}:{serial}"))).ToLowerInvariant();
    }
    JsonObject NewPet(string species,string sex,bool rare,int? homeId=null)
    {
        if(Pets.Count(p=>Active(p!))+(homeId==null?Pets.Count(p=>Active(p!)&&D(p!,"pregnancy_due_ts")>0):0)>=I(Rules,"PET_LIMIT",100))throw new InvalidOperationException("宠物数量已达上限");
        var spec=Spec("SPECIES",species);
        var home=Rows("buildings").FirstOrDefault(b=>(homeId==null?S(b!,"kind")==S(spec,"building"):I(b!,"building_id")==homeId)&&Used(I(b!,"building_id"))<Slots(b!))??throw new InvalidOperationException("没有适配的居所空位");
        var pet=Pets.Count>0?Pets[0]!.DeepClone().AsObject():new JsonObject();string ident=NextId("pet");
        pet["pet_id"]=ident;pet["species"]=species;pet["sex"]=sex;pet["name"]="小"+species+ident[..3];pet["rare"]=rare;
        pet["birth_ts"]=Now;pet["last_update_ts"]=Now;pet["lifespan_max"]=D(spec,"life")*(rare?Rule("RARE_LIFE_FACTOR",1.5):1);pet["building_id"]=I(home,"building_id");
        foreach(string key in new[]{"death_ts","paused_seconds","product_ready_ts","product_expiry_ts","breed_ready_ts","interact_ready_ts","injury","treatment_ready_ts","pregnancy_start_ts","pregnancy_due_ts"})pet[key]=0;
        foreach(string key in new[]{"death_reason","departure","illness_type","mate_id","mother_id","father_id","pregnancy_father_id"})pet[key]="";
        pet["fostered"]=false;pet["pregnancy_rare"]=false;pet["status"]="正常";pet["last_stage"]="幼崽";pet["illness_budget"]=-Math.Log(Math.Max(1e-9,Random()));
        foreach(var field in Rules["PET_INITIAL"]!.AsObject())pet[field.Key]=field.Value!.DeepClone();pet["next_product_ts"]=Now+D(spec,"interval");pet["history"]=new JsonArray();
        pet["personality"]=Choose(Rules["PERSONALITIES"]!.AsArray().Select(p=>p!.GetValue<string>()));pet["next_excretion_ts"]=Now+D(spec,"excretion_interval",Rule("EXCRETION_INTERVAL"));pet["fight_ready_ts"]=0;
        Pets.Add(pet);WorldRevision++;return pet;
    }
    string Conceive(JsonObject first,JsonObject second)
    {
        if(first==second||S(first,"species")!=S(second,"species")||S(first,"sex")==S(second,"sex"))throw new InvalidOperationException("需要同物种的异性伙伴");
        if(I(first,"building_id")!=I(second,"building_id")||S(Home(first),"kind")=="宠物医院")throw new InvalidOperationException("请先同居，住院期间不能繁育");
        if(Expecting(first)||Expecting(second)||D(first,"injury")>0||D(second,"injury")>0||Pets.Count(p=>Active(p!))+Pets.Count(p=>Active(p!)&&D(p!,"pregnancy_due_ts")>0)>=I(Rules,"PET_LIMIT"))throw new InvalidOperationException("已有孕期、伤势或缺少人口空位");
        foreach(var p in new[]{first,second})if(Stage(p)!="成年"||D(p,"health")<Rule("BREED_MIN_HEALTH",50)||D(p,"favor")<Rule("BREED_FAVOR",35)||D(p,"breed_ready_ts")>Now||D(p,"pregnancy_due_ts")>0||S(p,"illness_type")!="")throw new InvalidOperationException("年龄、健康、好感或繁育冷却不满足");
        if(new[]{S(first,"mother_id"),S(first,"father_id")}.Where(s=>s!="").Intersect(new[]{S(second,"mother_id"),S(second,"father_id"),S(second,"pet_id")}).Any()||S(second,"mother_id")==S(first,"pet_id")||S(second,"father_id")==S(first,"pet_id"))throw new InvalidOperationException("近亲不能繁育");
        var mother=S(first,"sex")=="雌"?first:second;var father=mother==first?second:first;if(Used(I(mother,"building_id"))+1>Slots(Home(mother)))throw new InvalidOperationException("需要预留幼崽空位");
        Pair(first,second);Pay(I(Rules,"BREED_COST",100));foreach(var pair in Rules["BREED_MATERIALS"]!.AsObject())Take(pair.Key,pair.Value!.GetValue<int>());
        bool success=Random()<Rule("CONCEPTION_CHANCE",.85);double cooldown=Now+Rule(success?"BREED_COOLDOWN":"CONCEPTION_RETRY",Day);
        first["breed_ready_ts"]=cooldown;second["breed_ready_ts"]=cooldown;
        if(!success)return "本次未成功受孕，稍后再尝试";
        mother["pregnancy_start_ts"]=Now;mother["pregnancy_due_ts"]=Now+D(Spec("SPECIES",S(mother,"species")),"gestation",Rule("PREGNANCY_DURATION",Day));mother["pregnancy_father_id"]=S(father,"pet_id");mother["pregnancy_rare"]=Random()<Rule("MUTATION_CHANCE",.08);first["breed_ready_ts"]=D(mother,"pregnancy_due_ts")+Rule("BREED_COOLDOWN");second["breed_ready_ts"]=D(first,"breed_ready_ts");
        return "孕期已开始，已预留幼崽空位";
    }
    void DeliverBabies()
    {
        foreach(var mother in Pets.ToArray())if(Active(mother!)&&!B(mother!,"fostered")&&D(mother!,"pregnancy_due_ts")>0&&D(mother!,"pregnancy_due_ts")<=Now) {
            string father=S(mother!,"pregnancy_father_id");bool rare=B(mother!,"pregnancy_rare");mother!["pregnancy_due_ts"]=0;
            var child=NewPet(S(mother,"species"),Random()<.5?"雌":"雄",rare,I(mother,"building_id"));child["mother_id"]=S(mother,"pet_id");child["father_id"]=father;Stat("breeds");Meta["reputation"]=I(Meta,"reputation")+I(Rules,rare?"BREED_RARE_REPUTATION":"BREED_REPUTATION");Log(S(mother,"name")+"的幼崽出生了");
        }
    }
    public string PlaceTown(string key,int x,int y,bool community,int orientation=0)
    {
        string reason=PlacementReason(key,x,y,orientation);if(reason!="")throw new InvalidOperationException(reason);
        var spec=Spec("CATALOG",key);int w=spec["footprint"]![orientation%2]!.GetValue<int>(),h=spec["footprint"]![1-orientation%2]!.GetValue<int>();
        var b=new JsonObject{{"cx",x},{"cy",y},{"w",w},{"h",h},{"orientation",orientation},{"catalog_key",key},{"family",S(spec,"family")}};
        var roads=RoadCells();if(Footprint(b).Any(c=>!Allowed(c)||roads.Contains(c))||!Frontage(b).Any(Connected().Contains))throw new InvalidOperationException("选址有占用、街区未开放或未连接道路");
        if(!community)Pay(I(spec,"cost"));b["building_id"]=NextId("building");b["placed_ts"]=Now;b["finish_ts"]=Now+D(spec,"build_hours")*Hour;b["status"]="building";b["community"]=community;
        Rows("town_buildings").Add(b);Meta["town_buildings_built"]=I(Meta,"town_buildings_built")+1;ClearGround(Footprint(b));WorldRevision++;return "已开始施工";
    }
    public string BuildRoad(int x,int y)
    {
        var c=new Cell(x,y);if(!Allowed(c)||RoadCells().Contains(c))throw new InvalidOperationException("此处不可铺路");Pay(10);AppendRoad(c,10);return "道路已铺设";
    }
    void AppendRoad(Cell c,int paid){Rows("roads").Add(new JsonObject{{"road_id",$"{c.X},{c.Y}"},{"cx",c.X},{"cy",c.Y},{"road_style","asphalt"},{"built_ts",Now},{"paid",paid}});Meta["road_revision"]=I(Meta,"road_revision")+1;WorldRevision++;}
    string DeliverOrder(string id,string petId)
    {
        var order=Rows("orders").FirstOrDefault(o=>S(o!,"order_id")==id)??throw new InvalidOperationException("订单不存在");
        if(S(order,"status")!="进行中"||D(order,"expiry_ts")<=Now)throw new InvalidOperationException("订单已结束");
        if(S(order,"kind")=="item")Take(S(order,"item"),I(order,"quantity"));else{var p=Pet(petId);if(S(p,"species")!=S(order,"species")||Stage(p)!=S(order,"stage")||Disease(p)!=""||D(p,"injury")>0||Expecting(p))throw new InvalidOperationException("需要指定品种、年龄且健康、非孕期的伙伴");p["departure"]="订单交付";p["product_ready_ts"]=0;p["product_expiry_ts"]=0;WorldRevision++;}
        Meta["coins"]=I(Meta,"coins")+I(order,"reward");Meta["reputation"]=I(Meta,"reputation")+I(order,"reputation");order["status"]="完成";Stat("orders");return "订单已交付";
    }
    void TickTown()
    {
        foreach(var order in Rows("orders"))if(S(order!,"status")=="进行中"&&D(order!,"expiry_ts")<=Now)order!["status"]="过期";
        if(D(Meta,"next_tax_ts")<=Now) {
            int tax=Math.Max(0,(int)Math.Floor(Bonus("tax")));
            Meta["coins"]=I(Meta,"coins")+tax;Meta["town_tax_total"]=I(Meta,"town_tax_total")+tax;Meta["next_tax_ts"]=Now+Day;
        }
        if(B(Meta,"caretaker")&&D(Meta,"next_care_ts")<=Now) {
            if(I(Meta,"coins")>=I(Rules,"CARE_WAGE",8)) {
                Pay(I(Rules,"CARE_WAGE",8));foreach(var p in Pets)if(Active(p!)&&!B(p!,"fostered")) {
                    if(D(p!,"hunger")<70&&Stock("普通饲料")>0){Take("普通饲料",1);Gain(p!,"hunger",30);}
                    if(D(p!,"cleanliness")<Rule("CARE_CLEAN_THRESHOLD",65)&&Stock("清水")>0){Take("清水",1);Gain(p!,"cleanliness",Rule("CARE_CLEAN_GAIN",35));}
                }
            } else Meta["caretaker"]=false;
            Meta["next_care_ts"]=Now+Rule("CARE_INTERVAL",3*Hour);
        }
    }
}
