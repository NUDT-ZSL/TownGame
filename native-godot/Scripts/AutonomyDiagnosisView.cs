using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;

public partial class Main
{
    static readonly (string Key,string Caption)[] PublicResources={
        ("food","粮食"),("seed","种子"),("wood","木材"),("stone","石料"),("feed","饲料"),("herd","牲畜")
    };
    static readonly (string Id,string Activity,string Tint)[] ProductionDistricts={
        ("fields","播种与收获","f2eedb"),("ranch","牧场与养殖","e8eee0"),
        ("industry","木材与石料","e6edf0"),("market","交付与贸易","f4e8dd")
    };
    bool _autonomyAttentionOnly;

    void AutonomyControls()
    {
        Section("小镇自治","公共经营与货运诊断");
        var body=_managementBody!;
        var overview=MarketCard(body,color:"e9eee1");
        var balance=MarketLabel(overview,"",wrap:true);
        var access=MarketLabel(overview,"",color:TownTheme.Muted,wrap:true);
        var planning=MarketLabel(overview,"",color:TownTheme.Muted,wrap:true);
        var overviewLinks=AutonomyLinks(overview);
        var toggle=MarketButton(overviewLinks,"暂停自治",()=>Act("autonomy",new JsonObject()),expand:false,accent:"自治");
        toggle.Name="TownAutonomyToggle";
        var warehouse=MarketButton(overviewLinks,"查看公共仓库",()=>ShowManagement("自治","public-stock"),expand:false,accent:"商店");
        warehouse.Name="TownAutonomyStockLink";
        var attentionLink=MarketButton(overviewLinks,"查看待处理街区",()=>{
            _autonomyAttentionOnly=true;
            ShowManagement("自治","district-attention");
        },expand:false,accent:"自治");
        attentionLink.Name="TownDistrictAttentionLink";
        MarketButton(overviewLinks,"查看游客游览",()=>ShowManagement("自治","tourism"),expand:false,accent:"自治").Name="TownTourismLink";

        Section("居民货运","生产 · 公共库存 · 装车 · 交付");
        var freight=MarketCard(body,color:"e9eef2");
        freight.GetParent().Name="TownAutonomyDiagnosis";
        var phase=MarketLabel(freight,"",18,wrap:true);
        phase.Name="TownFreightPhase";
        var status=MarketLabel(freight,"",color:TownTheme.Muted,wrap:true);
        var driver=MarketLabel(freight,"",wrap:true);
        driver.Name="TownFreightDriver";
        var cargo=MarketLabel(freight,"",wrap:true);
        cargo.Name="TownFreightCargo";
        var bottleneck=MarketLabel(freight,"",wrap:true);
        bottleneck.Name="TownFreightBottleneck";
        var refreshJourney=FreightJourneyControls(freight);
        var freightLinks=AutonomyLinks(freight);
        var locate=MarketButton(freightLinks,"定位货车与司机",LocateAutonomyTruck,expand:false,accent:"地图");
        locate.Name="TownAutonomyTruck";
        string roadZone="estate";
        var map=MarketButton(freightLinks,"查看道路",()=>OpenAutonomyDistrict(roadZone),expand:false,accent:"地图");
        map.Name="TownFreightMapLink";
        var residence=MarketButton(freightLinks,"查看司机住所",()=>{
            var resident=Resident(NativeGame.S(Game.FreightState,"driver_id"));
            if(resident!=null&&Game.ResidentHome(resident) is JsonNode house)SelectResidence(NativeGame.S(house,"building_id"));
        },expand:false,accent:"家庭");
        residence.Name="TownFreightHomeLink";
        var refreshReceipts=FreightReceiptControls(body);
        var refreshDispatch=FreightDispatchControls(body);
        _refreshManagementPage+=TourismReportControls(body);

        Section("生产街区","公共库存由全镇共享");
        var districtSummary=MarketCard(body,color:"edf0df");
        var attentionCount=MarketLabel(districtSummary,"",wrap:true);
        attentionCount.Name="TownDistrictAttentionCount";
        var onlyAttention=new CheckButton{Name="TownDistrictAttentionOnly",Text="仅看待处理街区",ButtonPressed=_autonomyAttentionOnly};
        TownTheme.AccentButton(onlyAttention,"自治",6);
        districtSummary.AddChild(onlyAttention);
        var attentionEmpty=MarketLabel(districtSummary,"暂无待处理街区。可关闭筛选查看全部。",14,TownTheme.Muted,wrap:true);
        if(_managementEntryTarget=="district-attention")_managementFocusTarget=districtSummary.GetParent<Control>();
        var lanes=AutonomyLinks(body);
        var districts=new Dictionary<string,(Control Card,Label Route,Label Stock,Label Job,Label Warning)>();
        foreach(var (zone,activity,tint) in ProductionDistricts){
            var card=MarketCard(lanes,260,tint);
            var container=card.GetParent<Control>();
            container.Name=$"TownAutonomyDistrict_{zone}";
            MarketLabel(card,$"{AutonomyZoneName(zone)} · {activity}",17,wrap:true);
            var route=MarketLabel(card,"",wrap:true);
            route.Name=$"TownDistrictStatus_{zone}";
            var shared=MarketLabel(card,"",color:TownTheme.Muted,wrap:true);
            var work=MarketLabel(card,"",wrap:true);
            var warning=MarketLabel(card,"",color:new Color("873d31"),wrap:true);
            var districtLink=MarketButton(card,"在地图查看",()=>OpenAutonomyDistrict(zone),expand:false,accent:"地图");
            districtLink.Name=$"TownDistrictMap_{zone}";
            districts.Add(zone,(container,route,shared,work,warning));
        }

        Section("当前自治任务");
        var jobCard=MarketCard(body,color:"f4efe1");
        var jobs=MarketLabel(jobCard,"",wrap:true);
        jobs.Name="TownAutonomyJobs";
        var lastBlock=MarketLabel(jobCard,"",color:TownTheme.Muted,wrap:true);

        Section("公共仓库","自治任务会先预留所需资源");
        var stockCard=MarketCard(body,color:"eef0df");
        var stock=MarketLabel(stockCard,"",wrap:true);
        stock.Name="TownPublicStock";
        if(_managementEntryTarget=="public-stock")_managementFocusTarget=stockCard;
        onlyAttention.Toggled+=value=>{_autonomyAttentionOnly=value;Refresh();};
        _refreshManagementPage+=Refresh;
        Refresh();

        void Refresh()
        {
            refreshDispatch();
            refreshReceipts();
            var commons=Game.Commons;
            var publicStock=commons["stock"]!.AsObject();
            bool enabled=NativeGame.B(commons,"enabled");
            bool night=Game.IsResidentNight;
            int residents=Data.People.Count(person=>NativeGame.S(person!,"role")=="居民");
            balance.Text=$"{(enabled?"自治运行中":"自治已暂停")} · 公共预算 {NativeGame.I(commons,"funds")} 金币 · 居民 {residents} / 床位 {Game.PopulationCapacity} · 新生儿 {NativeGame.I(commons,"births")}";
            access.Text=Game.AccessRoadStats;
            planning.Text=NativeGame.S(Game.Meta,"urban_plan_status");
            planning.Visible=planning.Text.Length>0;
            toggle.Text=enabled?"暂停自治":"开启自治";
            var jobsNow=Game.Jobs.OfType<JsonNode>().ToArray();
            jobs.Text=jobsNow.Length==0?"暂无已安排的自治任务":string.Join("\n",jobsNow.Select(AutonomyJobSummary));
            string latest=NativeGame.S(commons,"blocked");
            lastBlock.Text=$"最近一次任务受阻 · {latest}";
            lastBlock.Visible=latest.Length>0;
            stock.Text=string.Join("　·　",PublicResources.Select(item=>$"{item.Caption} {NativeGame.I(publicStock,item.Key)}"));

            var state=Game.FreightState;
            string step=NativeGame.S(state,"phase","parked");
            var resident=Resident(NativeGame.S(state,"driver_id"));
            bool active=resident!=null&&step!="parked";
            string source=active?NativeGame.S(state,"source"):Game.PlannedFreightSource();
            string destination=active?NativeGame.S(state,"destination","market"):"market";
            string travelZone=step switch{"to_depot" or "boarding"=>"estate","to_destination" or "unloading"=>destination,_=>source};
            int availableDrivers=Game.FreightDriversAvailable;
            int loaded=NativeGame.I(state,"cargo_count");
            var loading=Game.FreightCargoAvailability(source);
            string statusText=NativeGame.S(state,"status");
            phase.Text=$"{AutonomyFreightPhase(step)} · {AutonomyZoneName(source)} → {AutonomyZoneName(destination)}";
            status.Text=statusText;
            driver.Text=$"司机 {(resident==null?"待分配":NativeGame.S(resident,"name"))} · 可派成年居民 {availableDrivers} 位";
            cargo.Text=$"车载 {(loaded>0?AutonomyResource(NativeGame.S(state,"cargo_item"))+" "+loaded+" 份":"空车")} · 已完成 {NativeGame.I(state,"trips")} 趟交付";
            if(source.Length>0&&loaded==0&&step!="to_depot")
                cargo.Text+=$" · 公库{AutonomyResource(loading.Item)} {loading.Stock} 份，保留 {loading.Reserve} 份，本趟可装 {loading.Loadable} 份";
            var (reason,actionZone)=FreightBottleneck();
            roadZone=actionZone.Length==0?"estate":actionZone;
            bottleneck.Text=reason.Length==0?"目前无明显阻塞":$"当前限制 · {reason}";
            bottleneck.AddThemeColorOverride("font_color",reason.Length==0?TownTheme.Leaf:new Color("873d31"));
            map.Text=$"地图查看{AutonomyZoneName(roadZone)}";
            locate.Disabled=_freightTruck==null||!GodotObject.IsInstanceValid(_freightTruck);
            residence.Disabled=resident==null||Game.ResidentHome(resident)==null;
            refreshJourney(step,source,destination);

            int needingAttention=0;
            foreach(var (zone,labels) in districts){
                bool connected=Game.Effective(zone);
                var zoneJobs=jobsNow.Where(job=>NativeGame.S(job,"zone")==zone).ToArray();
                labels.Job.Text=zoneJobs.Length==0?"当前暂无自治任务":string.Join("\n",zoneJobs.Select(AutonomyJobSummary));
                labels.Stock.Text=DistrictResources(zone,publicStock);
                string issue=DistrictInputs(zone,publicStock,zoneJobs,enabled,night);
                bool attention=!connected||(enabled&&!night&&issue.Length>0);
                if(attention)needingAttention++;
                labels.Card.Visible=!_autonomyAttentionOnly||attention;
                labels.Route.Text=$"{(attention?"需处理 · ":"")}{AutonomyRoadReadiness(zone)}";
                labels.Route.AddThemeColorOverride("font_color",attention?new Color("873d31"):TownTheme.Leaf);
                labels.Warning.Text=issue;
                labels.Warning.Visible=issue.Length>0;
                labels.Warning.AddThemeColorOverride("font_color",attention?new Color("873d31"):TownTheme.Muted);
            }
            attentionCount.Text=$"待处理 {needingAttention} / {districts.Count} 个街区 · {(!enabled?"自治暂停":night?"夜间休息":"自治运行中")}";
            attentionLink.Text=$"查看待处理街区 · {needingAttention}";
            attentionEmpty.Visible=_autonomyAttentionOnly&&needingAttention==0;

            (string,string) FreightBottleneck()
            {
                if(active){
                    if(travelZone.Length>0&&!Game.Effective(travelZone))return (AutonomyRoadReadiness(travelZone),travelZone);
                    if((step is "to_source" or "to_destination" or "to_depot")&&_freightRoutePhase==step&&!_freightHasRoute
                        &&statusText.Contains("道路中断",StringComparison.Ordinal))return (statusText,travelZone);
                    if(step=="loading"&&loading.Loadable==0)return ($"没有可装载的{AutonomyResource(loading.Item)}，需保留公共库存",source);
                    return ("",travelZone);
                }
                if(!enabled)return ("小镇自治已暂停","estate");
                if(!Game.Effective("market"))return (AutonomyRoadReadiness("market"),"market");
                if(source.Length==0)return ("农田、牧场和工业区均未开放或未接通","fields");
                if(night)return ("夜间暂停派车，次日早晨恢复","estate");
                if(availableDrivers==0)return ("没有空闲成年居民可以驾驶","estate");
                double delay=NativeGame.D(state,"next_trip_ts")-Game.Now;
                if(delay>0)return ($"货车等待下次发车 · {DeadlineText(delay)}","estate");
                return loading.Loadable==0?($"没有可装载的{AutonomyResource(loading.Item)}，需保留公共库存",source):("",source);
            }
        }
    }

    static HFlowContainer AutonomyLinks(Control parent)
    {
        var row=new HFlowContainer();
        row.AddThemeConstantOverride("h_separation",8);
        row.AddThemeConstantOverride("v_separation",6);
        parent.AddChild(row);
        return row;
    }

    void OpenAutonomyDistrict(string zone)=>ShowManagement("地图",$"district:{zone}");

    void LocateAutonomyTruck()
    {
        if(_freightTruck==null||!GodotObject.IsInstanceValid(_freightTruck))return;
        FocusFreightPoint(_freightTruck.Position,FreightDescription(_freightTruck.Ident));
    }

    string AutonomyZoneName(string zone)=>Data.Root["zones"]?[zone] is JsonNode record
        ?NativeGame.S(record,"name",zone):"未选生产区";

    string AutonomyRoadReadiness(string zone)
    {
        string name=AutonomyZoneName(zone);
        return Game.ZoneLevel(zone)==0?$"{name}尚未开放":Game.Effective(zone)?$"{name}道路连通":$"{name}道路中断";
    }

    static string AutonomyResource(string item)=>PublicResources.FirstOrDefault(resource=>resource.Key==item).Caption
        ??(item=="funds"?"金币":item);

    static string AutonomyFreightPhase(string phase)=>phase switch{
        "boarding"=>"司机前往货车","to_source"=>"驶往生产区","loading"=>"生产区装车","to_destination"=>"载货前往商业区",
        "unloading"=>"商业区交付","to_depot"=>"回库","parked"=>"停靠待命",_=>"待同步"
    };

    string AutonomyJobSummary(JsonNode job)
    {
        string kind=NativeGame.S(job,"kind");
        string label=kind switch{
            "crop"=>"播种","harvest"=>"收获","ranch"=>"养殖","gather"=>"采集","trade"=>"贸易",
            "road"=>"铺路","build"=>"建设住宅","open"=>"开放街区",_=>kind
        };
        string zone=AutonomyZoneName(NativeGame.S(job,"zone"));
        var worker=Resident(NativeGame.S(job,"worker"));
        string person=worker==null?"等待居民":NativeGame.S(worker,"name");
        double remaining=NativeGame.D(job,"due")-Game.Now;
        string when=remaining>0?DeadlineText(remaining):"等待结算";
        string resources=job["cost"] is JsonObject cost&&cost.Count>0
            ?$" · 已预留 {string.Join("、",cost.Select(pair=>$"{AutonomyResource(pair.Key)} {pair.Value}"))}":"";
        return $"{zone} · {label} · {person} · {when}{resources}";
    }

    string DistrictResources(string zone,JsonObject stock)
    {
        int food=NativeGame.I(stock,"food");
        return zone switch{
            "fields"=>$"公共粮食 {food} · 种子 {NativeGame.I(stock,"seed")}",
            "ranch"=>$"公共牲畜 {NativeGame.I(stock,"herd")} · 饲料 {NativeGame.I(stock,"feed")}",
            "industry"=>$"公共木材 {NativeGame.I(stock,"wood")} · 石料 {NativeGame.I(stock,"stone")}",
            _=>$"公共预算 {NativeGame.I(Game.Commons,"funds")} 金币 · 货运已交付 {NativeGame.I(Game.FreightState,"trips")} 趟"
        };
    }

    string DistrictInputs(string zone,JsonObject stock,JsonNode[] jobs,bool enabled,bool night)
    {
        if(!Game.Effective(zone))return "";
        if(!enabled)return "自治已暂停";
        if(night)return "夜间暂停安排新任务";
        if(jobs.Length>0)return "";
        return zone switch{
            "fields" when NativeGame.I(stock,"seed")==0=>"缺少公共种子，播种需要 1 份",
            "ranch" when NativeGame.I(stock,"herd")==0=>"没有可饲养的公共牲畜",
            "ranch" when NativeGame.I(stock,"feed")<Math.Max(2,(int)Math.Ceiling(NativeGame.I(stock,"herd")/2d))=>"公共饲料不足，暂无法安排养殖",
            "industry" when NativeGame.I(stock,"food")==0=>"缺少公共粮食，采集需要 1 份",
            _=>""
        };
    }
}
