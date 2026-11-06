using System;
using System.Linq;
using System.Text.Json.Nodes;

public sealed partial class NativeGame
{
    internal sealed record CommissionPlan(string Key,string Name,(string Item,int Count)[] Supplies,int Coins,int Reputation,
        string Facility="",string FacilityLabel="");
    internal sealed record CommissionProgress(CommissionPlan Plan,int Stage,double Deadline,double Opens,
        bool SuppliesReady,bool RoadReady,bool FreightArrived,bool CanPrepare);
    internal sealed record CommissionReceipt(int Season,string Name,double Delivered,(string Item,int Count)[] Supplies,int Coins,int Reputation);
    const string CommissionKey="native_town_commission";
    const string CommissionOfferKey="native_town_commission_offer";
    const string CommissionHistoryKey="native_town_commission_history";
    const int CommissionHistoryLimit=12;
    static readonly CommissionPlan[] CommissionPlans={
        new("spring-market","春季集市",new[]{("鲜草",6),("谷物",4)},190,4),
        new("summer-stand","夏日饮食摊",new[]{("清水",8),("零食",4)},210,4),
        new("autumn-expo","秋收展销会",new[]{("谷物",8),("普通饲料",6)},230,5),
        new("winter-shelter","冬日暖棚",new[]{("高级饲料",4),("普通饲料",8)},255,5),
    };
    static readonly CommissionPlan[] FacilityCommissionPlans={
        new("spring-windmill","风车春耕展",new[]{("谷物",12),("鲜草",8)},265,6,"windmill","接通道路的风车粮仓"),
        new("summer-teahouse","茶楼消夏会",new[]{("清水",14),("特制零食",4)},295,6,"tea_house","接通道路的木构茶楼"),
        new("autumn-workshop","工坊秋收联展",new[]{("谷物",10),("高级饲料",6)},325,7,"brick_workshop","接通道路的红砖工坊"),
        new("winter-ranch","牧场冬粮联运",new[]{("高级饲料",8),("谷物",10)},365,8,"","已连通的 2 级牧场"),
    };

    double CommissionSeasonLength=>Rule("SEASON_DAYS",7)*Day;
    int CommissionSeasonIndex=>Math.Max(0,(int)Math.Floor((Now-D(Meta,"start_ts"))/CommissionSeasonLength));
    JsonNode? CurrentCommission => Meta[CommissionKey] is JsonNode state&&I(state,"season")==CommissionSeasonIndex?state:null;

    bool CommissionFacilityReady(CommissionPlan plan)=>plan.Facility.Length==0
        ?ZoneLevel("ranch")>=2&&Effective("ranch")
        :Rows("town_buildings").OfType<JsonNode>().Any(b=>S(b,"catalog_key")==plan.Facility&&S(b,"status")=="ready"
            &&Frontage(b).Any(Connected().Contains));

    CommissionPlan ActiveCommissionPlan()
    {
        int season=CommissionSeasonIndex, index=season%CommissionPlans.Length;
        var standard=CommissionPlans[index];
        var facility=FacilityCommissionPlans[index];
        if(CurrentCommission is JsonNode active)return S(active,"plan_key")==facility.Key?facility:standard;
        if(Meta[CommissionOfferKey] is JsonNode offer&&I(offer,"season")==season)
            return S(offer,"plan_key")==facility.Key?facility:standard;
        // A pre-rotation save may already have reservations for this season's standard plan.
        string owner=CommissionReservationOwner(season);
        if(Rows("inventory").OfType<JsonNode>().Any(batch=>batch[ReservationKey]?[owner]!=null))return standard;
        return (season/CommissionPlans.Length)%2==0&&CommissionFacilityReady(facility)?facility:standard;
    }

    internal string CommissionRotationHint(CommissionPlan active)
    {
        var facility=FacilityCommissionPlans[CommissionSeasonIndex%CommissionPlans.Length];
        if(active.Key==facility.Key)return $"设施轮换 · 由{facility.FacilityLabel}解锁";
        if(CommissionFacilityReady(facility))return $"标准方案 · 设施方案「{facility.Name}」会随年份轮换";
        return $"标准方案 · 建成{facility.FacilityLabel}后可轮换至「{facility.Name}」";
    }

    void PinCommissionOffer(string owner)
    {
        if(!ActiveCommissionReservation(owner))return;
        Meta[CommissionOfferKey]=new JsonObject{{"season",CommissionSeasonIndex},{"plan_key",ActiveCommissionPlan().Key}};
    }

    internal CommissionProgress TownCommission()
    {
        int season=CommissionSeasonIndex;
        var plan=ActiveCommissionPlan();
        var state=CurrentCommission;
        double deadline=D(Meta,"start_ts")+(season+1)*CommissionSeasonLength;
        double opens=state==null?(Math.Floor(Now/Day)+1)*Day+7*Hour:D(state,"opens");
        string owner=CommissionReservationOwner(season);
        bool ready=plan.Supplies.All(item=>Stock(item.Item,owner)>=item.Count);
        bool road=Effective("market")&&(Effective("fields")||Effective("ranch")||Effective("industry"));
        bool freight=state!=null&&I(FreightState,"trips")>I(state,"freight_baseline");
        int stage=state==null?0:B(state,"delivered")?3:road&&freight&&Now>=opens?2:1;
        return new(plan,stage,deadline,opens,ready,road,freight,opens<deadline);
    }

    string CommissionCommand(string step,int expectedSeason)
    {
        if(expectedSeason!=CommissionSeasonIndex)throw new InvalidOperationException("季节已更替，请查看新的小镇委托。");
        var progress=TownCommission();
        if(step=="prepare"){
            if(progress.Stage!=0||!progress.CanPrepare)throw new InvalidOperationException("当前无法开始备货。");
            if(!progress.SuppliesReady)throw new InvalidOperationException("请先备齐清单里的物资。");
            Meta[CommissionKey]=new JsonObject{{"season",expectedSeason},{"plan_key",progress.Plan.Key},{"opens",progress.Opens},{"freight_baseline",I(FreightState,"trips")}};
            return "已确认备货 · 等待居民货车完成送货";
        }
        if(step!="deliver"||progress.Stage!=2)throw new InvalidOperationException("小镇委托尚未到交付阶段。");
        if(!progress.SuppliesReady)throw new InvalidOperationException("部分物资已使用或过期，请补齐后再交付。");
        string owner=CommissionReservationOwner(expectedSeason);
        foreach(var item in progress.Plan.Supplies)TakeReservedInventory(item.Item,item.Count,owner);
        ReleaseStockReservation(owner);
        Meta["coins"]=checked(I(Meta,"coins")+progress.Plan.Coins);
        Meta["reputation"]=checked(I(Meta,"reputation")+progress.Plan.Reputation);
        CurrentCommission!["delivered"]=true;
        RecordCommissionDelivery(expectedSeason,progress.Plan);
        Log($"小镇委托已完成 · {progress.Plan.Name}");
        return $"{progress.Plan.Name}已交付 · 获得 {progress.Plan.Coins} 金币和 {progress.Plan.Reputation} 声望";
    }

    void RecordCommissionDelivery(int season,CommissionPlan plan)
    {
        if(Meta[CommissionHistoryKey] is not JsonArray history)Meta[CommissionHistoryKey]=history=new JsonArray();
        var supplies=new JsonObject();
        foreach(var (item,count) in plan.Supplies)supplies[item]=count;
        history.Add(new JsonObject{
            ["season"]=season,["name"]=plan.Name,["delivered"]=Now,["supplies"]=supplies,
            ["coins"]=plan.Coins,["reputation"]=plan.Reputation,
        });
        while(history.Count>CommissionHistoryLimit)history.RemoveAt(0);
    }

    internal CommissionReceipt[] CommissionReceipts()=>Meta[CommissionHistoryKey] is JsonArray history
        ?history.OfType<JsonObject>().Reverse().Where(row=>row["supplies"] is JsonObject&&D(row,"delivered")>0)
            .Take(CommissionHistoryLimit).Select(row=>new CommissionReceipt(I(row,"season"),S(row,"name"),D(row,"delivered"),
                row["supplies"]!.AsObject().Select(item=>(item.Key,I(row["supplies"]!,item.Key))).ToArray(),
                I(row,"coins"),I(row,"reputation"))).ToArray()
        :Array.Empty<CommissionReceipt>();

    internal int TownCommissionSeason=>CommissionSeasonIndex;
}
