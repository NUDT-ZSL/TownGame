using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;

// 无Godot依赖的应用服务：命令事务、游戏时钟、库存、生产和生命周期。
public sealed partial class NativeGame
{
    public const double Hour=3600,Day=86400;
    readonly JsonObject root;
    public JsonObject State=>root["snapshot"]!.AsObject();
    public JsonObject Meta=>State["meta"]!.AsObject();
    public JsonObject Rules=>root["rules"]!.AsObject();
    public JsonArray Pets=>State["pets"]!.AsArray();
    public JsonArray People=>State["people"]!.AsArray();
    public double Now=>D(Meta,"sim_ts");
    public long WorldRevision {get;private set;}
    public static double D(JsonNode n,string key,double fallback=0)=>n[key] is JsonNode value?double.Parse(value.ToJsonString(),System.Globalization.CultureInfo.InvariantCulture):fallback;
    public static int I(JsonNode n,string key,int fallback=0)=>(int)D(n,key,fallback);
    public static string S(JsonNode n,string key,string fallback="")=>n[key]?.GetValue<string>()??fallback;
    public static bool B(JsonNode n,string key)=>n[key]?.GetValue<bool>()??false;
    double Rule(string name,double fallback=0)=>D(Rules,name,fallback);
    public JsonArray Rows(string name)=>State[name]!.AsArray();
    public JsonObject Spec(string name,string key)=>Rules[name]![key]!.AsObject();
    public NativeGame(JsonObject data)
    {
        root=data;if(root["rules"]==null)throw new InvalidOperationException("请重新运行export_godot.py，资源中缺少规则表。");
        Meta["native_next_hour"]??=Now+Hour;Meta["native_rng"]??=123456789L;
        Meta["native_speed"]??=1;
        InitializeSettlement();InitializeActivities();InitializeResidentHousing();InitializeUrbanPlanning();InitializeFreight();
    }
    public double Random()
    {
        uint x=(uint)(Meta["native_rng"]?.GetValue<long>()??123456789L);
        x^=x<<13;x^=x>>17;x^=x<<5;Meta["native_rng"]=(long)x;return x/4294967296.0;
    }
    public void Log(string message)
    {
        var logs=Rows("logs");logs.Add(new JsonObject{{"log_id",Guid.NewGuid().ToString("N")},{"ts",Now},{"text",message}});
        while(logs.Count>300)logs.RemoveAt(0);
    }
    public bool Active(JsonNode p)=>D(p,"death_ts")==0&&S(p,"departure")=="";
    JsonObject Pet(string id,bool foster=false)
    {
        var p=Pets.FirstOrDefault(p=>S(p!,"pet_id")==id&&Active(p!))?.AsObject()??throw new InvalidOperationException("宠物已不在庄园");
        if(!foster&&B(p,"fostered"))throw new InvalidOperationException("请先接出寄养宠物");return p;
    }
    public int Stock(string item)=>Rows("inventory").Where(b=>S(b!,"item")==item).Sum(b=>I(b!,"quantity"));
    public int Storage=>I(Rules,"BASE_STORAGE",120)+Rows("buildings").Where(b=>S(b!,"kind")=="仓库").Sum(b=>I(b!,"level")*I(Rules,"STORAGE_PER_LEVEL",100));
    public void Take(string item,int count)
    {
        if(count<=0||Stock(item)<count)throw new InvalidOperationException(item+"不足");
        foreach(var b in Rows("inventory").Where(b=>S(b!,"item")==item).OrderBy(b=>D(b!,"expiry_ts")==0?double.MaxValue:D(b!,"expiry_ts")).ToArray()) {
            int take=Math.Min(count,I(b!,"quantity"));b!["quantity"]=I(b,"quantity")-take;count-=take;if(I(b,"quantity")==0)Rows("inventory").Remove(b);if(count==0)break;
        }
    }
    public void Add(string item,int count)
    {
        if(count<=0||Rows("inventory").Sum(b=>I(b!,"quantity"))+count>Storage)throw new InvalidOperationException("仓库容量不足");
        double ttl=D(Spec("ITEMS",item),"ttl");
        Rows("inventory").Add(new JsonObject{{"batch_id",Guid.NewGuid().ToString("N")},{"item",item},{"quantity",count},{"created_ts",Now},{"expiry_ts",ttl>0?Now+ttl:0}});
    }
    void Pay(int price){if(price<0||I(Meta,"coins")<price)throw new InvalidOperationException("金币不足");Meta["coins"]=I(Meta,"coins")-price;}
    void Gain(JsonNode p,string attr,double value)=>p[attr]=Math.Clamp(D(p,attr)+value,0,100);
    public string Stage(JsonNode p)
    {
        if(!Active(p))return "离园";
        double age=Now-D(p,"birth_ts")-D(p,"paused_seconds"),life=D(p,"lifespan_max",30*Day);
        return age<life*.1?"幼崽":age<life*.25?"少年":age<life*.8?"成年":"老年";
    }
    public JsonObject Home(JsonNode p)=>Rows("buildings").First(b=>I(b!,"building_id")==I(p,"building_id"))!.AsObject();
    int Used(int id)=>Pets.Where(p=>Active(p!)&&I(p!,"building_id")==id).Sum(p=>1+(D(p!,"pregnancy_due_ts")>0?1:0));
    int Slots(JsonNode b)=>S(b,"kind")==""?0:I(Spec("BUILDINGS",S(b,"kind")),"capacity")+I(Spec("BUILDINGS",S(b,"kind")),"growth")*(I(b,"level")-1);
    public string Execute(string action,JsonObject args,Action? persist=null)
    {
        var before=State.DeepClone();long revision=WorldRevision;
        try {
            string result=Command(action,args);EnsureResidentHomes();CheckAchievements();Validate();persist?.Invoke();return result;
        }catch {root["snapshot"]=before;WorldRevision=revision;throw;}
    }
    string Command(string action,JsonObject a)
    {
        string id=S(a,"pet_id"),item=S(a,"item","普通饲料");int quantity=I(a,"quantity",1);
        switch(action) {
            case "feed": {
                var p=Pet(id);var food=Spec("ITEMS",item)["feed"]?.AsObject()??throw new InvalidOperationException("不是食物");Take(item,1);
                foreach(var entry in food)Gain(p,entry.Key,entry.Value!.GetValue<double>());
                if(item=="腐败物"){p["illness_type"]="普通感染";p["status"]="生病";}
                Log(S(p,"name")+"已喂食"+item);return "喂食完成";
            }
            case "bath":{var p=Pet(id);Take("清水",1);Take("沐浴露",1);Gain(p,"cleanliness",Rule("BATH_GAIN",55));return "洗澡完成";}
            case "play":{var p=Pet(id);if(D(p,"interact_ready_ts")>Now)throw new InvalidOperationException("互动冷却中");Gain(p,"happy",Rule("PLAY_HAPPY",18));Gain(p,"favor",Rule("PLAY_FAVOR",8)*D(Rules["PLAY_PERSONALITY_MULT"]!,S(p,"personality"),1));p["interact_ready_ts"]=Now+Hour;return "互动完成";}
            case "buy":case "sell": {
                if(quantity<=0)throw new InvalidOperationException("数量必须大于零");var spec=Spec("ITEMS",item);
                if(action=="buy"){int price=BuyPrice(item);if(price<=0)throw new InvalidOperationException("此物品不能购买");Pay(checked(price*quantity));Add(item,quantity);}
                else{Take(item,quantity);Meta["coins"]=checked(I(Meta,"coins")+I(spec,"sell")*quantity);}
                return action=="buy"?"购买完成":"出售完成";
            }
            case "craft": {
                var recipe=Spec("RECIPES",item);if(!Rows("buildings").Any(b=>S(b!,"kind")=="加工坊"&&I(b!,"level")>=I(recipe,"level")))throw new InvalidOperationException("需要对应等级加工坊");
                foreach(var pair in recipe["inputs"]!.AsObject())Take(pair.Key,pair.Value!.GetValue<int>());Pay(I(recipe,"cost"));Add(item,I(recipe,"count"));return "制作完成";
            }
            case "collect":{var p=Pet(id);if(D(p,"product_ready_ts")==0||D(p,"product_expiry_ts")<=Now)throw new InvalidOperationException("暂无产物");Add(S(Spec("SPECIES",S(p,"species")),"product"),B(p,"rare")?2:1);p["product_ready_ts"]=0;p["product_expiry_ts"]=0;return "产物已收取";}
            case "move": {
                var p=Pet(id,true);var home=Rows("buildings").FirstOrDefault(b=>I(b!,"building_id")==I(a,"building_id"))??throw new InvalidOperationException("居所不存在");
                if(I(p,"building_id")==I(home,"building_id"))throw new InvalidOperationException("已经住在此处");
                if(S(home,"kind")==""||Used(I(home,"building_id"))+1+(D(p,"pregnancy_due_ts")>0?1:0)>Slots(home))throw new InvalidOperationException("居所已满");
                if(S(home,"kind")=="寄养屋")Pay(I(Rules,"FOSTER_ENTER_COST",40));p["building_id"]=I(home,"building_id");p["fostered"]=S(home,"kind")=="寄养屋";WorldRevision++;return "搬家完成";
            }
            case "treat": {
                var p=Pet(id);string disease=Disease(p);if(disease==""&&D(p,"injury")==0)throw new InvalidOperationException("无需治疗");
                var home=Home(p);if(S(home,"kind")!="宠物医院")throw new InvalidOperationException("请先送到宠物医院");if(D(p,"treatment_ready_ts")>Now)throw new InvalidOperationException("治疗恢复期中");
                var plan=disease!=""?Spec("ILLNESSES",disease):Rules["INJURY_TREATMENT"]!.AsObject();int level=I(home,"level");
                double chance=Math.Min(1,D(plan,"cure")+(level-1)*Rule("MEDICAL_LEVEL_BONUS")),damage=D(plan,"failure_damage"),fatality=D(plan,"fatality");
                if(D(p,"injury")>0){var injury=Rules["INJURY_TREATMENT"]!;chance=Math.Min(chance,Math.Min(1,D(injury,"cure")+(level-1)*Rule("MEDICAL_LEVEL_BONUS")));damage=Math.Max(damage,D(injury,"failure_damage"));fatality=Math.Max(fatality,D(injury,"fatality"));}
                Pay(Math.Max(0,I(Rules,"TREATMENT_FEE")-level*I(Rules,"HOSPITAL_FEE_REDUCTION")));Take("药品",1);p["treatment_ready_ts"]=Now+Rule("TREATMENT_COOLDOWN");
                if(D(plan,"cure")==0){Gain(p,"health",Rule("PALLIATIVE_HEAL"));return "已进行舒缓照护，绝症仍未治愈";}
                if(Random()<chance){p["illness_type"]="";p["injury"]=0;p["status"]="正常";p["illness_budget"]=-Math.Log(Math.Max(1e-9,Random()));Gain(p,"health",Rule("MEDICINE_HEALTH")+level*Rule("HOSPITAL_HEAL_BONUS"));return "治疗成功";}
                Gain(p,"health",-damage);if(D(p,"health")==0||Random()<fatality)Die(p,"救治无效");return "治疗失败，请恢复后再尝试";
            }
            case "breed":return Conceive(Pet(id),Pet(S(a,"mate_id")));
            case "buy_pet":{string species=S(a,"species"),sex=S(a,"sex","雌");bool egg=B(a,"egg");var spec=Spec("SPECIES",species);if(!new[]{"雌","雄"}.Contains(sex))throw new InvalidOperationException("性别只能为雌或雄");if(!Rows("buildings").Any(b=>S(b!,"kind")=="商店"))throw new InvalidOperationException("需要商店");if(egg&&spec["egg_available"]!=null&&!B(spec,"egg_available"))throw new InvalidOperationException("该品种不出售宠物蛋");Pay((int)(D(spec,"buy_price",Rule("PET_BUY_COST"))*(egg?Rule("EGG_PRICE_FACTOR"):1)));bool rare=egg&&Random()<Rule("MUTATION_CHANCE");NewPet(species,sex,rare);if(rare)Meta["reputation"]=I(Meta,"reputation")+I(Rules,"EGG_RARE_REPUTATION");WorldRevision++;return "新伙伴已到庄园";}
            case "build_manor": {
                var b=Rows("buildings").First(b=>I(b!,"building_id")==I(a,"building_id"))!;string kind=S(a,"kind");
                if(!B(b,"unlocked")||S(b,"kind")!="")throw new InvalidOperationException("地块尚未解锁或已有建筑");Pay(I(Spec("BUILDINGS",kind),"cost"));b["kind"]=kind;b["level"]=1;WorldRevision++;return "建筑已完成";
            }
            case "unlock": {
                int index=I(a,"building_id");var b=Rows("buildings").First(b=>I(b!,"building_id")==index)!;if(B(b,"unlocked"))throw new InvalidOperationException("已解锁");
                if(!Rows("buildings").Any(n=>B(n!,"unlocked")&&Math.Abs(I(n!,"building_id")%5-index%5)+Math.Abs(I(n!,"building_id")/5-index/5)==1))throw new InvalidOperationException("需与开放地块相邻");
                Pay(I(Rules,"UNLOCK_BASE",180)+I(Rules,"UNLOCK_GROWTH",30)*Math.Max(0,Rows("buildings").Count(b=>B(b!,"unlocked"))-Rules["INITIAL_OPEN_PLOTS"]!.AsArray().Count));b["unlocked"]=true;WorldRevision++;return "地块已解锁";
            }
            case "upgrade_manor":{var b=Rows("buildings").First(b=>I(b!,"building_id")==I(a,"building_id"))!;if(S(b,"kind")==""||I(b,"level")>=5)throw new InvalidOperationException("无法升级");Pay((int)(I(Spec("BUILDINGS",S(b,"kind")),"cost")*I(b,"level")*Rule("UPGRADE_PRICE_FACTOR",.8)));b["level"]=I(b,"level")+1;WorldRevision++;return "建筑已升级";}
            case "autonomy":return ToggleAutonomy();
            case "build_town":return PlaceTown(S(a,"catalog_key"),I(a,"cx"),I(a,"cy"),false,I(a,"orientation"));
            case "road":return EditRoad(a,false);
            case "open_zone":{string zone=S(a,"zone");int level=ZoneLevel(zone);if(level>=3||zone=="estate")throw new InvalidOperationException("无法升级");int price=I(root["zones"]![zone]!,"cost");Pay(level==0?price:(int)(price*(level+1)*.75));SetZone(zone,level+1);WorldRevision++;return "街区已开放/升级";}
            case "order":return DeliverOrder(S(a,"order_id"),id);
            default:return ActivityCommand(action,a);
        }
    }
    void Die(JsonNode p,string reason){p["death_ts"]=Now;p["status"]="离世";p["death_reason"]=reason;p["pregnancy_due_ts"]=0;WorldRevision++;Log(S(p,"name")+"离世："+reason);}
    public void Advance(double seconds)
    {
        if(seconds<=0)return;var before=State.DeepClone();long rev=WorldRevision;
        try {
            double target=Now+seconds;
            while(Now<target-1e-8) {
                double end=Math.Min(target,Math.Min(Now+Hour,D(Meta,"native_next_hour")));
                foreach(double boundary in ActivityBoundaries())if(boundary>Now+1e-6)end=Math.Min(end,boundary);
                foreach(var p in Pets)if(Active(p!)) {
                    foreach(string key in new[]{"pregnancy_due_ts","product_expiry_ts"})if(D(p!,key)>Now)end=Math.Min(end,D(p!,key));
                }
                PauseResidentWorkAtNight(Now,end);AdvancePets(end);Meta["sim_ts"]=end;ExpireInventory();DeliverBabies();TickActivities();RefreshFreight(false);
                foreach(var b in Rows("town_buildings"))if(S(b!,"status")=="building"&&D(b!,"finish_ts")<=Now){b!["status"]="ready";WorldRevision++;}
                if(Now>=D(Meta,"native_next_hour")-1e-6){Meta["native_next_hour"]=Now+Hour;TickTown();TickSettlement();}
            }
            CheckAchievements();Validate();
        }catch{root["snapshot"]=before;WorldRevision=rev;throw;}
    }
    void ExpireInventory()
    {
        foreach(var b in Rows("inventory").ToArray())if(D(b!,"expiry_ts")>0&&D(b!,"expiry_ts")<=Now) {
            Rows("inventory").Remove(b);
            if(S(b!,"item")!="腐败物"&&D(b!,"expiry_ts")+Day>Now) {
                var rotten=b!.DeepClone();rotten["item"]="腐败物";rotten["expiry_ts"]=D(b,"expiry_ts")+Day;Rows("inventory").Add(rotten);
            }
        }
    }
    void AdvancePets(double end)
    {
        foreach(var node in Pets.ToArray()) {
            var p=node!;if(!Active(p))continue;double cursor=D(p,"last_update_ts",Now);
            if(B(p,"fostered")){double pause=end-cursor;p["paused_seconds"]=D(p,"paused_seconds")+pause;if(D(p,"pregnancy_due_ts")>0)p["pregnancy_due_ts"]=D(p,"pregnancy_due_ts")+pause;p["next_product_ts"]=D(p,"next_product_ts")+pause;p["next_excretion_ts"]=D(p,"next_excretion_ts")+pause;if(D(p,"product_expiry_ts")>0&&D(p,"product_expiry_ts")<=end){p["product_ready_ts"]=0;p["product_expiry_ts"]=0;}p["last_update_ts"]=end;continue;}
            var home=Home(p);var spec=Spec("SPECIES",S(p,"species"));int level=I(home,"level",1);
            bool fit=new[]{S(spec,"building"),"宠物医院","游乐区"}.Contains(S(home,"kind"));double mult=(fit?1:Rule("MISMATCH_MULT",1.8))/(1+Rule("LEVEL_DECAY_BONUS",.06)*(level-1));
            var rates=new Dictionary<string,double>{{"hunger",D(Rules["DECAY"]!,"hunger")*mult*(S(p,"personality")=="吃货"?Rule("GLUTTON_HUNGER_MULT",1.2):1)},{"happy",D(Rules["DECAY"]!,"happy")*mult},{"cleanliness",D(Rules["DECAY"]!,"cleanliness")*mult}};
            var exposure=Exposure(I(p,"building_id"));bool outdoor=Outdoor(home);
            if(Season=="夏")rates["cleanliness"]*=Rule("SUMMER_DIRT_MULT",1);
            rates["cleanliness"]=(rates["cleanliness"]+exposure.Dirt)*(outdoor?D(WeatherSpec,"dirt",1):1);
            while(cursor<end-1e-6&&Active(p)) {
                double age=cursor-D(p,"birth_ts")-D(p,"paused_seconds"),life=D(p,"lifespan_max");if(age>=life-1e-6){Meta["sim_ts"]=cursor;Die(p,"寿终正寝");break;}
                double hours=Math.Min((end-cursor)/Hour,(life-age)/Hour);
                foreach(double boundary in new[]{.1,.25,.8,1.0})if(life*boundary-age>1e-5)hours=Math.Min(hours,(life*boundary-age)/Hour);
                foreach(var pair in rates)foreach(double threshold in pair.Key=="hunger"?new[]{20d,0}:pair.Key=="cleanliness"?new[]{25d,0}:new[]{0d})if(D(p,pair.Key)>threshold+1e-6)hours=Math.Min(hours,(D(p,pair.Key)-threshold)/pair.Value);
                string disease=Disease(p);double health=disease==""?0:-D(Spec("ILLNESSES",disease),"damage");
                if(outdoor)health-=D(WeatherSpec,"health")+(Season=="冬"?Rule("WINTER_DAMAGE"):0);
                health-=D(p,"injury")*Rule("INJURY_HOURLY_DAMAGE",.02);if(D(p,"hunger")<=20+1e-6)health-=Rule("STARVE_DAMAGE",1.8);if(age>=life*.8)health-=Rule("OLD_DAMAGE",.07);
                if(disease==""&&D(p,"injury")==0&&D(p,"hunger")>20&&age<life*.8)health+=Rule("HEALTH_RECOVERY",.06);if(S(home,"kind")=="宠物医院")health+=Rule("HOSPITAL_RECOVERY",.08)*level;
                double hazard=Rule("ILLNESS_HOURLY",.0008)*(D(p,"cleanliness")<=25?Rule("DIRTY_ILLNESS_MULT",12):1)*(fit?1:Rule("MISMATCH_ILLNESS_MULT",2));
                hazard*=exposure.Hazard*(age>=life*.8?Rule("OLD_ILLNESS_MULT",8):1);
                if(disease=="")hours=Math.Min(hours,Math.Max(1e-9,D(p,"illness_budget",1)/hazard));if(health<0)hours=Math.Min(hours,D(p,"health")/-health);
                foreach(var pair in rates)Gain(p,pair.Key,-pair.Value*hours);Gain(p,"health",health*hours);cursor+=hours*Hour;
                if(disease==""){p["illness_budget"]=D(p,"illness_budget",1)-hazard*hours;if(D(p,"illness_budget")<=1e-8)ContractIllness(p);}
                if(D(p,"health")<1e-7){Meta["sim_ts"]=cursor;Die(p,"健康耗尽");}
                else if(cursor-D(p,"birth_ts")-D(p,"paused_seconds")>=life-1e-5){Meta["sim_ts"]=cursor;Die(p,"寿终正寝");}
            }
            p["last_update_ts"]=Math.Min(end,cursor);
            if(D(p,"product_expiry_ts")>0&&end>=D(p,"product_expiry_ts")){p["product_ready_ts"]=0;p["product_expiry_ts"]=0;}
            if(Active(p)) {
                double due=Math.Max(D(p,"next_product_ts"),D(p,"birth_ts")+D(p,"paused_seconds")+D(p,"lifespan_max")*.25),interval=D(spec,"interval");
                if(end>=due){double count=Math.Floor((end-due)/interval)+1,last=due+(count-1)*interval;p["next_product_ts"]=due+count*interval;
                    if(D(p,"product_ready_ts")==0&&D(p,"hunger")>=Rule("STARVING")&&Disease(p)==""&&D(p,"injury")==0&&last+Rule("PRODUCT_TTL")>end){p["product_ready_ts"]=last;p["product_expiry_ts"]=last+Rule("PRODUCT_TTL");}}
            }
        }
    }
    public void Validate()
    {
        if(I(Meta,"coins")<0)throw new InvalidOperationException("负金币");
        foreach(var b in Rows("inventory"))if(I(b!,"quantity")<=0||Rules["ITEMS"]![S(b!,"item")]==null)throw new InvalidOperationException("非法库存");
        if(Pets.Select(p=>S(p!,"pet_id")).Distinct().Count()!=Pets.Count)throw new InvalidOperationException("重复宠物");
        if(People.Select(p=>S(p!,"person_id")).Distinct().Count()!=People.Count)throw new InvalidOperationException("重复居民");
        if(I(Commons,"funds")<0||PublicStock.Any(p=>p.Value!.GetValue<int>()<0))throw new InvalidOperationException("负公共资源");
        if(Jobs.Select(j=>S(j!,"worker")).Distinct().Count()!=Jobs.Count||Jobs.Any(j=>!People.Any(p=>S(p!,"person_id")==S(j!,"worker"))))throw new InvalidOperationException("工作重复预约或人员不存在");
        var cells=new HashSet<Cell>();foreach(var r in Rows("roads")){var c=new Cell(I(r!,"cx"),I(r!,"cy"));if(c.X<0||c.Y<0||c.X>=125||c.Y>=80||!cells.Add(c)||Rules["ROAD_STYLES"]![S(r!,"road_style")]==null)throw new InvalidOperationException("道路重复或无效");}
        foreach(var b in Rows("town_buildings"))foreach(var c in Footprint(b!))if(c.X<0||c.Y<0||c.X>=125||c.Y>=80||!cells.Add(c))throw new InvalidOperationException("建筑重叠或越界");
        foreach(var d in Meta["decorations"]!.AsArray())if(!cells.Add(new(I(d!,"cx"),I(d!,"cy"))))throw new InvalidOperationException("装饰占位冲突");
    }
}
