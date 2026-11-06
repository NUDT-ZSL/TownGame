using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;

// 经营活动均进入同一个模拟时钟，交互命令由Execute提供事务和持久化回滚。
public sealed partial class NativeGame
{
    public string Season=>Rules["SEASONS"]![(int)Math.Floor((Now-D(Meta,"start_ts"))/(Rule("SEASON_DAYS",7)*Day))%4]!.GetValue<string>();
    public string Weather=>S(Meta,"weather","晴天");
    JsonObject WeatherSpec=>Spec("WEATHERS",Weather);
    bool Outdoor(JsonNode b)=>S(b,"kind")!=""&&B(Spec("BUILDINGS",S(b,"kind")),"outdoor");
    string Disease(JsonNode p)=>S(p,"illness_type")!=""?S(p,"illness_type"):S(p,"status")=="生病"?"普通感染":"";
    internal bool NeedsPetTreatment(JsonNode pet) => Disease(pet).Length > 0 || D(pet, "injury") > 0;
    internal bool IsPetInHospital(JsonNode pet) => S(Home(pet), "kind") == "宠物医院";

    int FreePetPlaces(JsonNode building) => Math.Max(0, Slots(building) - Used(I(building, "building_id")));
    static int PetPlacesRequired(JsonNode pet) => D(pet, "pregnancy_due_ts") > 0 ? 2 : 1;

    internal (int Plot,int Level,int Used,int Total)[] PetHospitalBeds()=>Rows("buildings").OfType<JsonNode>()
        .Where(building=>S(building,"kind")=="宠物医院").Select(building=>{
            int id=I(building,"building_id");
            return (id,I(building,"level"),Used(id),Slots(building));
        }).ToArray();

    internal (JsonNode? Destination, int FreeBeds, bool HasHospital) PetHospitalAdmission(JsonNode pet)
    {
        JsonNode? destination = null;
        int freeBeds = 0;
        bool hasHospital = false;
        int required = PetPlacesRequired(pet);
        foreach (var hospital in Rows("buildings").OfType<JsonNode>().Where(b => S(b, "kind") == "宠物医院"))
        {
            hasHospital = true;
            int available = FreePetPlaces(hospital);
            freeBeds += available;
            if (destination == null && available >= required) destination = hospital;
        }
        return (destination, freeBeds, hasHospital);
    }

    internal JsonNode? PetDischargeHome(JsonNode pet)
    {
        string kind = S(Spec("SPECIES", S(pet, "species")), "building");
        int required = PetPlacesRequired(pet);
        return Rows("buildings").OfType<JsonNode>()
            .FirstOrDefault(home => S(home, "kind") == kind && FreePetPlaces(home) >= required);
    }

    void InitializeActivities()
    {
        State["waste"]??=new JsonArray();State["codex"]??=new JsonObject();State["achievements"]??=new JsonObject();
        UpdateSpeciesCodex();
        Meta["decorations"]??=new JsonArray();Meta["decoration_undo"]??=new JsonArray();Meta["town_cleared"]??=new JsonArray();
        Meta["stats"]??=new JsonObject();foreach(string key in new[]{"breeds","sold","orders"})Meta["stats"]![key]??=0;
        Meta["weather"]??="晴天";Meta["weather_seed"]??=(long)D(Meta,"start_ts");
        Meta["farm_manager"]??=false;Meta["manager_status"]??="未雇佣";Meta["manager_cleaned"]??=0;
        InitializeFarmManager();
        foreach(var (key,interval) in new[]{("next_weather_ts",Rule("WEATHER_INTERVAL")),("next_order_ts",Day),("next_event_ts",Rule("EVENT_INTERVAL")),("next_visitor_ts",Rule("VISITOR_INTERVAL")),("next_town_arrival_ts",Rule("TOWN_VISIT_INTERVAL")),("next_rent_ts",Day),("next_fight_ts",Rule("FIGHT_INTERVAL")),("manager_next_wage_ts",Rule("MANAGER_WAGE_INTERVAL")),("manager_next_scan_ts",0d)})Meta[key]??=Now+interval;
        foreach(var p in Pets)p!["next_excretion_ts"]??=Now+D(Spec("SPECIES",S(p!,"species")),"excretion_interval",Rule("EXCRETION_INTERVAL"));
        Meta["field_batch"]??=new JsonObject();Meta["next_field_ts"]??=0;
    }
    public int BuyPrice(string item)
    {
        if(I(Spec("ITEMS",item),"price")<=0)return 0;
        double factor=(D(Meta,"discount_until")>Now?D(Spec("EVENT_EFFECTS","merchant"),"discount",1):1)*(D(Meta,"inflation_until")>Now?D(Spec("EVENT_EFFECTS","inflation"),"mult",1):1);
        return Math.Max(1,(int)(D(Spec("ITEMS",item),"price")*factor));
    }
    void Stat(string key)=>Meta["stats"]![key]=I(Meta["stats"]!,key)+1;
    internal int PetPrice(JsonNode p)
    {
        int shop=Rows("buildings").Where(b=>S(b!,"kind")=="商店").Select(b=>I(b!,"level")).DefaultIfEmpty().Max();
        return (int)(D(Spec("SPECIES",S(p,"species")),"buy_price",Rule("PET_BUY_COST"))*Rule("SELL_BASE_FACTOR")*D(Rules["SELL_STAGE_FACTOR"]!,Stage(p))*(B(p,"rare")?Rule("RARE_PRICE_FACTOR"):1)*(1+shop*Rule("SELL_SHOP_BONUS"))*Math.Max(D(p,"health"),Rule("SELL_HEALTH_FLOOR"))/100);
    }
    string Choose(IEnumerable<string> choices){var a=choices.ToArray();return a[(int)(Random()*a.Length)%a.Length];}
    void ContractIllness(JsonNode p)
    {
        var illnesses=Rules["ILLNESSES"]!.AsObject();double choice=Random()*illnesses.Sum(pair=>D(pair.Value!,"weight"));
        foreach(var pair in illnesses){choice-=D(pair.Value!,"weight");if(choice<=0){p["illness_type"]=pair.Key;p["status"]="生病";return;}}
    }
    public (int Total,int Stale,double Dirt,double Hazard) Exposure(int building)
    {
        var piles=Rows("waste").Where(w=>I(w!,"building_id")==building).ToArray();int total=piles.Sum(w=>I(w!,"quantity")),stale=piles.Where(w=>Now-D(w!,"created_ts")>=Rule("WASTE_UNSAFE_AFTER")).Sum(w=>I(w!,"quantity"));
        return(total,stale,Math.Min(Rule("WASTE_MAX_DIRT"),total*Rule("WASTE_FRESH_DIRT")+stale*Rule("WASTE_STALE_DIRT")),1+Math.Min(Rule("WASTE_MAX_HAZARD_BONUS"),stale*Rule("WASTE_DISEASE_PER_UNIT")));
    }
    IEnumerable<double> ActivityBoundaries()
    {
        yield return NextResidentRoutineBoundary();
        foreach(double boundary in FestivalBoundaries())yield return boundary;
        foreach(double boundary in TouristBoundaries())yield return boundary;
        foreach(double boundary in FarmPlotBoundaries())yield return boundary;
        foreach(string key in new[]{"next_weather_ts","next_order_ts","next_event_ts","next_town_arrival_ts","next_rent_ts","next_fight_ts","field_expiry_ts","next_field_ts"})yield return D(Meta,key);
        yield return D(Meta,"start_ts")+(Math.Floor((Now-D(Meta,"start_ts"))/(Rule("SEASON_DAYS",7)*Day))+1)*Rule("SEASON_DAYS",7)*Day;
        foreach(var p in Pets)if(Active(p!)&&!B(p!,"fostered"))yield return D(p!,"next_excretion_ts");
        foreach(var w in Rows("waste"))yield return D(w!,"created_ts")+Rule("WASTE_UNSAFE_AFTER");
        foreach(var e in Rows("events"))if(S(e!,"status")=="待处理")yield return D(e!,"expiry_ts");
        if(B(Meta,"farm_manager")){yield return D(Meta,"manager_next_wage_ts");if(!ManagerSceneDriven)yield return Meta["manager_job"] is JsonNode job?D(job,"end_ts"):D(Meta,"manager_next_scan_ts");}
    }
    public string CleanWaste(JsonObject args,bool sell=false,bool manager=false)
    {
        var piles=Rows("waste").Where(w=>(args["building_id"]==null||I(w!,"building_id")==I(args,"building_id"))&&(args["waste_id"]==null||S(w!,"waste_id")==S(args,"waste_id"))).ToArray();
        if(piles.Length==0)throw new InvalidOperationException("没有待清理的粪便");int count=piles.Sum(w=>I(w!,"quantity"));
        if(sell){if(!Rows("buildings").Any(b=>S(b!,"kind")=="商店"))throw new InvalidOperationException("需要商店");Meta["coins"]=I(Meta,"coins")+count*I(Spec("ITEMS","粪便"),"sell");}else Add("粪便",count);
        foreach(var w in piles){if(Meta["manager_job"] is JsonNode job&&S(job,"waste_id")==S(w!,"waste_id")){Meta["manager_job"]=null;Meta["manager_next_scan_ts"]=Now;}Rows("waste").Remove(w);}
        if(manager){Meta["manager_cleaned"]=I(Meta,"manager_cleaned")+count;Meta["manager_last_clean_ts"]=Now;}
        Log($"清扫粪便×{count}，已{(sell?"出售":"入库")}");return "清扫完成";
    }
    void TickSanitation()
    {
        foreach(var p in Pets)if(Active(p!)&&!B(p!,"fostered"))while(D(p!,"next_excretion_ts")>0&&D(p!,"next_excretion_ts")<=Now) {
            Rows("waste").Add(new JsonObject{{"waste_id",NextId("waste")},{"pet_id",S(p!,"pet_id")},{"building_id",I(p!,"building_id")},{"created_ts",D(p!,"next_excretion_ts")},{"quantity",1},{"u",.5},{"v",.5}});
            p!["next_excretion_ts"]=D(p,"next_excretion_ts")+D(Spec("SPECIES",S(p,"species")),"excretion_interval",Rule("EXCRETION_INTERVAL"));
        }
        RefreshManagerTasks();
    }
    string ActivityCommand(string action,JsonObject a)
    {
        switch(action){
            case "weekend_festival":return FestivalCommand(a);
            case "clean_waste":return CleanWaste(a);
            case "sell_waste":return CleanWaste(a,true);
            case "farm_manager":{
                if(!B(Meta,"farm_manager")){Pay(I(Rules,"MANAGER_HIRE_COST"));Meta["manager_next_wage_ts"]=Now+Rule("MANAGER_WAGE_INTERVAL");Meta["manager_next_scan_ts"]=Now;}
                Meta["farm_manager"]=!B(Meta,"farm_manager");Meta["manager_job"]=null;Meta["manager_deferred"]=new JsonObject();Meta["manager_status"]=B(Meta,"farm_manager")?"检查动物和环境":"已解雇";TickSanitation();return "管理员状态已更新";}
            case "caretaker":if(!B(Meta,"caretaker"))Pay(I(Rules,"CARE_HIRE_COST"));Meta["caretaker"]=!B(Meta,"caretaker");Meta["next_care_ts"]=Now+Rule("CARE_INTERVAL");return "饲养员状态已更新";
            case "harvest":{
                if(!Effective("fields")||D(Meta,"field_ready_ts")==0)throw new InvalidOperationException("农田未通路或暂无作物");var batch=Meta["field_batch"]!.AsObject();foreach(var pair in batch)Add(pair.Key,pair.Value!.GetValue<int>());Meta["field_batch"]=new JsonObject();Meta["field_ready_ts"]=0;Meta["field_expiry_ts"]=0;if(!FarmPlotsEnabled)MarkFarmVisualHarvest(0);return "作物已入库";}
            case "event":{
                var entry=Rows("events").FirstOrDefault(e=>S(e!,"event_id")==S(a,"event_id")&&S(e!,"status")=="待处理")??throw new InvalidOperationException("事件已结束");int option=I(a,"option");if(option<0||option>1||D(entry,"expiry_ts")<=Now)throw new InvalidOperationException("事件选项无效或已过期");return ResolveEvent(entry,option);}
            case "rename":{string name=S(a,"name").Trim();if(name.Length==0||name.Length>I(Rules,"RENAME_MAX",16))throw new InvalidOperationException("名字长度不符合要求");Pet(S(a,"pet_id"),true)["name"]=name;return "改名完成";}
            case "pair":return Pair(Pet(S(a,"pet_id")),Pet(S(a,"mate_id")));
            case "unpair":{var pet=Pet(S(a,"pet_id"),true);if(Expecting(pet))throw new InvalidOperationException("孕期内不能解除伴侣");var other=Pets.FirstOrDefault(p=>S(p!,"pet_id")==S(pet,"mate_id"));pet["mate_id"]="";if(other!=null)other["mate_id"]="";return "已解除伴侣";}
            case "sell_pet":{var p=Pet(S(a,"pet_id"));if(Expecting(p))throw new InvalidOperationException("孕期内不能出售父母");if(!Rows("buildings").Any(b=>S(b!,"kind")=="商店"))throw new InvalidOperationException("需要商店");Meta["coins"]=I(Meta,"coins")+PetPrice(p);p["departure"]="出售";p["departure_ts"]=Now;p["product_ready_ts"]=0;p["product_expiry_ts"]=0;Stat("sold");WorldRevision++;return "伙伴已前往新家";}
            case "discard":Take(S(a,"item"),I(a,"quantity",1));return "物品已丢弃";
            case "adopt":{if(D(Meta,"adopt_ready_ts")>Now)throw new InvalidOperationException("公益领养冷却中");var candidates=AdoptionCandidates();if(candidates.Length==0)throw new InvalidOperationException("没有适配的居所空位");NewPet(Choose(candidates),Choose(new[]{"雌","雄"}),false);Meta["adopt_ready_ts"]=Now+Rule("ADOPTION_COOLDOWN");return "公益领养的新伙伴已到庄园";}
            case "hospitalize":{
                var pet = Pet(S(a, "pet_id"));
                if (!NeedsPetTreatment(pet)) throw new InvalidOperationException("没有伤病，无需住院");
                if (IsPetInHospital(pet)) return "已经住院";
                var hospital = PetHospitalAdmission(pet).Destination ?? throw new InvalidOperationException("需要有空床的宠物医院");
                pet["building_id"] = I(hospital, "building_id");
                WorldRevision++;
                return "已送入宠物医院";
            }
            default:return EditCommand(action,a);
        }
    }
    bool Expecting(JsonNode p)=>Pets.Any(m=>Active(m!)&&D(m!,"pregnancy_due_ts")>0&&(S(m!,"pet_id")==S(p,"pet_id")||S(m!,"pregnancy_father_id")==S(p,"pet_id")));
    string Pair(JsonNode a,JsonNode b)
    {
        string reason=PairBlockReason(a,b);
        if(reason.Length>0)throw new InvalidOperationException(reason);
        a["mate_id"]=S(b,"pet_id");b["mate_id"]=S(a,"pet_id");return "已结为伴侣";
    }
    void TickActivities()
    {
        SyncFarmMoisture();
        if(Now>=D(Meta,"next_weather_ts")){
            // 天气独立哈希随机源，不消耗疾病/繁育随机序列。
            double when=D(Meta,"next_weather_ts");uint hash=(uint)D(Meta,"weather_seed");foreach(byte b in System.Text.Encoding.UTF8.GetBytes(when.ToString("R",System.Globalization.CultureInfo.InvariantCulture))){hash^=b;hash*=16777619;}
            var weights=Rules["WEATHER_WEIGHTS"]![Season]!.AsArray();double draw=hash/(double)uint.MaxValue*weights.Sum(w=>w!.GetValue<double>());int i=0;foreach(var pair in Rules["WEATHERS"]!.AsObject()){draw-=weights[i++]!.GetValue<double>();if(draw<=0){Meta["weather"]=pair.Key;break;}}
            Meta["weather_since_ts"]=when;Meta["next_weather_ts"]=when+Rule("WEATHER_INTERVAL");Log("天气转为"+Weather);
        }
        TickSanitation();TickPopulationAndFields();
        foreach(var o in Rows("orders"))if(S(o!,"status")=="进行中"&&D(o!,"expiry_ts")<=Now)o!["status"]="过期";
        if(Now>=D(Meta,"next_order_ts")){if(Rows("orders").Count(o=>S(o!,"status")=="进行中")<I(Rules,"ORDER_COUNT")*2)CreateOrders();Meta["next_order_ts"]=D(Meta,"next_order_ts")+Rule("ORDER_INTERVAL");}
        foreach(var e in Rows("events"))if(S(e!,"status")=="待处理"&&D(e!,"expiry_ts")<=Now){ResolveEvent(e!,1);e!["status"]="过期";}
        if(Now>=D(Meta,"next_event_ts")){if(!Rows("events").Any(e=>S(e!,"status")=="待处理")&&Random()<Rule("EVENT_CHANCE"))CreateEvent();Meta["next_event_ts"]=D(Meta,"next_event_ts")+Rule("EVENT_INTERVAL");}
        if(Now>=D(Meta,"next_fight_ts")){Fights();Meta["next_fight_ts"]=D(Meta,"next_fight_ts")+Rule("FIGHT_INTERVAL");}
    }
    double LandFactor(JsonNode at)
    {
        var cell=new Cell(I(at,"cx"),I(at,"cy"));double score=Directions.Append(new Cell(0,0)).Any(d=>RoadCells().Contains(new(cell.X+d.X,cell.Y+d.Y)))?Rule("LAND_ROAD",2):0;
        string zone=Zone(cell);score+=(zone==""?0:ZoneLevel(zone))+(Effective("park")?ZoneLevel("park"):0);
        foreach(var b in Rows("town_buildings").Where(b=>S(b!,"status")=="ready"&&Frontage(b!).Any(Connected().Contains))){var spec=Spec("CATALOG",S(b!,"catalog_key"));double distance=Math.Max(Math.Abs(cell.X-I(b!,"cx")-I(b!,"w")/2.0),Math.Abs(cell.Y-I(b!,"cy")-I(b!,"h")/2.0));if(S(spec,"category")=="公共"&&distance<=Rule("LAND_PUBLIC_RADIUS",6)||S(spec,"family")=="industrial"&&distance<=Rule("LAND_INDUSTRY_RADIUS",4))score+=D(spec,"desirability");}
        return Math.Clamp(1+score*Rule("LAND_FACTOR_PER_POINT",.025),Rule("LAND_FACTOR_MIN",.75),Rule("LAND_FACTOR_MAX",1.4));
    }
    double Bonus(string key)=>Rows("town_buildings").Where(b=>S(b!,"status")=="ready"&&Frontage(b!).Any(Connected().Contains)).Sum(b=>{
        string catalog=S(b!,"catalog_key");var spec=Spec("CATALOG",catalog);return key=="crops"?(catalog=="windmill"?Rule("CROP_WINDMILL_BONUS",.15):0):key=="traffic"?(catalog=="bus_stop"?Rule("BUS_TRAFFIC_BONUS",.1):0):D(spec,key)*((key=="tax"&&S(spec,"category")=="住宅"||key=="attraction"&&S(spec,"category")=="商业")?LandFactor(b!):1);
    });
    void TickPopulationAndFields()
    {
        TickTourism();
        EnsureResidentHomes();
        AssignDaytimeDestinations();
        if(Now>=D(Meta,"next_rent_ts")){if(Effective("homes")){int rent=People.Count(p=>S(p!,"role")=="居民")*I(Rules,"TOWN_RENT_PER_PERSON");Meta["coins"]=I(Meta,"coins")+rent;Meta["town_income"]=I(Meta,"town_income")+rent;}Meta["next_rent_ts"]=D(Meta,"next_rent_ts")+Day;}
        if(D(Meta,"field_expiry_ts")>0&&Now>=D(Meta,"field_expiry_ts")){Meta["field_batch"]=new JsonObject();Meta["field_ready_ts"]=0;Meta["field_expiry_ts"]=0;}
        if(!FarmPlotsEnabled){
            if(ZoneLevel("fields")>0&&D(Meta,"next_field_ts")==0)Meta["next_field_ts"]=Now+Rule("TOWN_FIELD_INTERVAL");
            if(D(Meta,"next_field_ts")>0&&Now>=D(Meta,"next_field_ts")){if(D(Meta,"field_ready_ts")==0&&Effective("fields")){double factor=D(WeatherSpec,"crop",1)*ZoneLevel("fields")*(1+Bonus("crops"));Meta["field_batch"]=new JsonObject{{"谷物",Math.Max(1,(int)Math.Round(Rule("TOWN_FIELD_GRAIN")*factor))},{"鲜草",Math.Max(1,(int)Math.Round(Rule("TOWN_FIELD_GRASS")*factor))}};Meta["field_ready_ts"]=Now;Meta["field_expiry_ts"]=Now+Rule("TOWN_FIELD_TTL");}Meta["next_field_ts"]=D(Meta,"next_field_ts")+Rule("TOWN_FIELD_INTERVAL");}
        }
        TickFarmPlots();
    }
    void CreateOrders()
    {
        int count=I(Rules,"ORDER_COUNT",3);
        var range=Rules["ORDER_ITEM_COUNT"]!;
        int min=range[0]!.GetValue<int>(),max=range[1]!.GetValue<int>();
        var species=Rules["SPECIES"]!.AsObject();
        var rare=species.Where(pair=>ChallengeSpecies(pair.Value!)).ToArray();
        var rareProducts=rare.Select(pair=>S(pair.Value!,"product")).ToHashSet();
        var regularItems=Rules["ORDER_ITEM_POOL"]!.AsArray().Select(item=>item!.GetValue<string>())
            .Where(item=>!rareProducts.Contains(item)).DefaultIfEmpty("普通饲料").ToArray();
        for(int i=0;i<count;i++){
            bool matched=i==0,challenge=i>0&&i==count-1;
            string item="",animal="",stage="";
            int quantity=1,reward,reputation;
            string kind;
            if(challenge){
                kind="pet";
                animal=Choose(rare.Length>0?rare.Select(pair=>pair.Key):species.Select(pair=>pair.Key));
                stage=Choose(new[]{"幼崽","少年","成年"});
                reward=Math.Max(I(Rules,"ORDER_PET_REWARD"),(int)(D(Spec("SPECIES",animal),"buy_price",Rule("PET_BUY_COST"))*Rule("ORDER_PET_PRICE_MULT")));
                reputation=I(Rules,"ORDER_PET_REPUTATION");
            }else{
                (item,quantity)=matched?StageMatchedOrder(max):(Choose(regularItems),min+(int)(Random()*(max-min+1)));
                kind=item.Length>0?"item":"checkin";
                reward=kind=="item"?Math.Max(I(Rules,"ORDER_ITEM_REWARD_MIN"),I(Spec("ITEMS",item),"sell")*quantity*I(Rules,"ORDER_ITEM_REWARD_MULT")):8;
                reputation=kind=="item"?I(Rules,"ORDER_ITEM_REPUTATION"):1;
            }
            var order=new JsonObject{{"order_id",NextId("order")},{"npc",Choose(new[]{"林阿姨","邮差小周","邻居阿禾","研究员宁宁"})},
                {"kind",kind},{"item",item},{"quantity",quantity},{"species",animal},{"stage",stage},{"reward",reward},{"reputation",reputation},
                {"created_ts",Now},{"expiry_ts",Now+Rule("ORDER_DURATION")},{"status","进行中"},{"stage_matched",matched},{"challenge",challenge}};
            AssignOrderPublisher(order);
            Rows("orders").Add(order);
        }
        ArchiveOld("orders","order_archive");Log("公告栏刷新了邻里订单");
    }
    static readonly Dictionary<string,(string Text,string A,string B)> EventTexts=new(){["stray"]=("稀有流浪伙伴在门口等待新家","消耗普通饲料收留","让它继续旅行"),["merchant"]=("旅行商人带来特价采购","欢迎商人","婉拒来访"),["tourists"]=("附近庆典带来参观游客","付费布置赚取门票","自然接待"),["bonus"]=("宠物发现额外副产品","收进仓库","赠送邻居"),["rain"]=("暴雨降温，户外伙伴需要保暖","付费搭雨棚","接受淋雨"),["infection"]=("附近出现传染病","消耗药品预防","暂不防护"),["inflation"]=("饲料供应紧张","付费签保价协议","接受临时涨价"),["scare"]=("烟花惊吓到了伙伴","消耗零食安抚","等待恢复")};
    void CreateEvent(){string kind=Choose(EventTexts.Keys);var text=EventTexts[kind];Rows("events").Add(new JsonObject{{"event_id",NextId("event")},{"kind",kind},{"text",text.Text},{"options",new JsonArray(text.A,text.B)},{"created_ts",Now},{"expiry_ts",Now+Rule("EVENT_DURATION")},{"status","待处理"}});ArchiveOld("events","event_archive");}
    string ResolveEvent(JsonNode entry,int option)
    {
        string kind=S(entry,"kind");var e=Spec("EVENT_EFFECTS",kind);var living=Pets.Where(p=>Active(p!)&&!B(p!,"fostered")).Select(p=>p!).ToArray();
        switch(kind){
            case "stray":if(option==0){var species=Rules["SPECIES"]!.AsObject().Where(p=>!B(p.Value!,"purchase_only")&&Rows("buildings").Any(b=>S(b!,"kind")==S(p.Value!,"building")&&Used(I(b!,"building_id"))<Slots(b!))).Select(p=>p.Key).ToArray();if(species.Length==0)throw new InvalidOperationException("无适配居所空位");Take("普通饲料",I(e,"feed"));NewPet(Choose(species),Choose(new[]{"雌","雄"}),true);Meta["reputation"]=I(Meta,"reputation")+I(e,"rep");}break;
            case "merchant":if(option==0)Meta["discount_until"]=Now+Rule("EVENT_DURATION");break;
            case "tourists":if(option==0){Pay(I(e,"cost"));Meta["reputation"]=I(Meta,"reputation")+I(e,"rep");}Meta["coins"]=I(Meta,"coins")+I(e,option==0?"reward":"passive_reward");break;
            case "bonus":if(option==0)Add(living.Length==0?"兔毛":S(Spec("SPECIES",S(living[(int)(Random()*living.Length)],"species")),"product"),I(e,"count"));else Meta["reputation"]=I(Meta,"reputation")+I(e,"gift_rep");break;
            case "rain":if(option==0)Pay(I(e,"cost"));else foreach(var p in living.Where(p=>Outdoor(Home(p)))){Gain(p,"health",D(e,"health"));Gain(p,"cleanliness",D(e,"cleanliness"));}break;
            case "infection":if(option==0)Take("药品",I(e,"medicine"));else foreach(var p in living)if(Random()<D(e,"chance"))ContractIllness(p);break;
            case "inflation":if(option==0)Pay(I(e,"cost"));else Meta["inflation_until"]=Now+Rule("EVENT_DURATION");break;
            case "scare":if(option==0)Take("零食",I(e,"snacks"));else foreach(var p in living)Gain(p,"happy",D(e,"happy"));break;
        }
        foreach(var p in living)if(D(p,"health")<=0)Die(p,"突发天气导致健康耗尽");
        entry["status"]="完成";entry["resolved_ts"]=Now;entry["resolved_option"]=option;
        Log("事件已结算："+S(entry,"text"));return "事件已结算";
    }
    void Fights()
    {
        foreach(var home in Rows("buildings").Where(b=>!new[]{"寄养屋","宠物医院"}.Contains(S(b!,"kind")))){
            var pets=Pets.Where(p=>Active(p!)&&!B(p!,"fostered")&&I(p!,"building_id")==I(home!,"building_id")&&Stage(p!)!="幼崽"&&D(p!,"fight_ready_ts")<=Now).Select(p=>p!).ToList();if(pets.Count<2)continue;
            var a=pets[(int)(Random()*pets.Count)];pets.Remove(a);var b=pets[(int)(Random()*pets.Count)];double chance=Rule("FIGHT_CHANCE")+(Math.Min(D(a,"hunger"),D(b,"hunger"))<Rule("STARVING")?Rule("FIGHT_HUNGER_BONUS"):0)+(Math.Min(D(a,"happy"),D(b,"happy"))<Rule("FIGHT_UNHAPPY_THRESHOLD")?Rule("FIGHT_UNHAPPY_BONUS"):0)+(Used(I(home!,"building_id"))>=Slots(home!)?Rule("FIGHT_CROWD_BONUS"):0);
            chance*=(D(Rules["FIGHT_PERSONALITY"]!,S(a,"personality"),1)+D(Rules["FIGHT_PERSONALITY"]!,S(b,"personality"),1))/2;if(S(a,"mate_id")==S(b,"pet_id"))chance*=Rule("FIGHT_MATE_FACTOR");if(Random()>=Math.Min(1,chance))continue;
            foreach(var p in new[]{a,b}){var damage=Rules["FIGHT_DAMAGE"]!;var injury=Rules["FIGHT_INJURY"]!;Gain(p,"health",-(damage[0]!.GetValue<double>()+Random()*(damage[1]!.GetValue<double>()-damage[0]!.GetValue<double>())));Gain(p,"injury",injury[0]!.GetValue<double>()+Random()*(injury[1]!.GetValue<double>()-injury[0]!.GetValue<double>()));Gain(p,"happy",-Rule("FIGHT_HAPPY_LOSS"));p["fight_ready_ts"]=Now+Rule("FIGHT_COOLDOWN");if(D(p,"health")==0)Die(p,"打架伤重离世");}Log("同居伙伴发生冲突，需留意伤势并送医");
        }
    }
    void UpdateSpeciesCodex()
    {
        var codex = State["codex"]!.AsObject();
        foreach (var pet in Pets.OfType<JsonNode>())
        {
            string species = S(pet, "species");
            var entry = codex[species] ??= new JsonObject { ["rare"] = false, ["natural_death"] = false };
            entry["rare"] = B(entry, "rare") || B(pet, "rare");
            entry["natural_death"] = B(entry, "natural_death") || S(pet, "death_reason") == "寿终正寝";
        }
    }

    void CheckAchievements()
    {
        UpdateSpeciesCodex();
        var achieved = State["achievements"]!.AsObject();
        foreach (var goal in GrowthAchievements())
        {
            if (!goal.MeetsCondition || goal.Earned) continue;
            var reward = Rules["ACHIEVEMENTS"]![goal.Key]!;
            achieved[goal.Key] = Now;
            Meta["coins"] = I(Meta, "coins") + reward[3]!.GetValue<int>();
            Meta["reputation"] = I(Meta, "reputation") + reward[4]!.GetValue<int>();
            Log("成就达成：" + reward[0]!.GetValue<string>());
        }
    }
}
