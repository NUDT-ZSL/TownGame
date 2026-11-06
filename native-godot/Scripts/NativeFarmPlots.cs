using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;

public sealed partial class NativeGame
{
    internal const int FarmPlotCount=3;
    internal static readonly string[] FarmCrops={"谷物","鲜草"};
    internal sealed record FarmPlot(string Crop,string OrderId,double Mature,double Expires,int Yield,bool Ready,bool Fertilized);
    internal sealed record FarmRoute(string Id,string Name,double Expires);
    internal bool FarmPlotsEnabled=>Meta["farm_plots"] is JsonObject;
    JsonObject? FarmPlots=>Meta["farm_plots"] as JsonObject;
    internal double FarmGrowthDuration=>Rule("TOWN_FIELD_INTERVAL");
    internal double FarmMaturity=>Now+FarmGrowthDuration;
    internal int FarmWarehouseSpace=>Math.Max(0,Storage-Rows("inventory").Sum(batch=>I(batch!,"quantity")));
    internal string FarmRoadReason=>ZoneLevel("fields")==0?"先在建造中开放农田街区":!Effective("fields")?"农田需接入庄园路网":"";

    internal int FarmYield(string crop)
    {
        if(!FarmCrops.Contains(crop))throw new InvalidOperationException("无效作物");
        double amount=Rule(crop=="谷物"?"TOWN_FIELD_GRAIN":"TOWN_FIELD_GRASS");
        double factor=D(WeatherSpec,"crop",1)*ZoneLevel("fields")*(1+Bonus("crops"));
        return Math.Max(1,(int)Math.Round(amount*factor/FarmPlotCount));
    }

    internal FarmPlot? FarmPlan(int plot)
    {
        if(FarmPlots?[plot.ToString()] is not JsonNode state||D(state,"expires")<=Now)return null;
        string crop=S(state,"crop");
        int ripe=I(state,"yield");
        return new(crop,S(state,"order_id"),D(state,"mature"),D(state,"expires"),
            ripe>0?ripe:ExpectedFarmYield(plot,crop,B(state,"fertilized")),ripe>0,B(state,"fertilized"));
    }

    internal FarmRoute[] FarmRoutes(string crop,double readyAt)=>ActiveProcurementOrders()
        .Where(order=>S(order,"item")==crop&&D(order,"expiry_ts")>readyAt)
        .Select(order=>new FarmRoute(S(order,"order_id"),S(order,"npc"),D(order,"expiry_ts"))).ToArray();

    internal int FarmHarvestReservation(string crop,string orderId,int yield)
    {
        if(orderId.Length==0)return 0;
        string owner=OrderReservationOwner(orderId);
        int limit=ReservationLimit(owner,crop);
        return limit<=0?0:Math.Min(yield,Math.Max(0,limit-InventoryAvailability(crop,owner).ReservedForOwner));
    }

    IEnumerable<double> FarmPlotBoundaries()
    {
        if(FarmPlots is not JsonObject plots)yield break;
        foreach(var pair in plots){
            var row=pair.Value!;
            if(I(row,"yield")==0)yield return D(row,"mature");
            yield return D(row,"expires");
        }
    }

    void TickFarmPlots()
    {
        if(FarmPlots is not JsonObject plots)return;
        foreach(var pair in plots.ToArray()){
            var plot=pair.Value!;
            if(!int.TryParse(pair.Key,out int id))continue;
            if(Now>=D(plot,"expires")){
                RecordFarmCrop(id,S(plot,"crop"));
                plots.Remove(pair.Key);
                Log($"农田 {pair.Key} 号地的{S(plot,"crop")}已过收获期");
            }else if(Now>=D(plot,"mature")&&I(plot,"yield")==0){
                plot["yield"]=ExpectedFarmYield(id,S(plot,"crop"),B(plot,"fertilized"));
                Log($"农田 {pair.Key} 号地的{S(plot,"crop")}成熟");
            }
        }
    }

    string FarmPlotCommand(JsonObject args)
    {
        string operation=S(args,"operation");
        if(operation=="resume"){
            if(FarmPlots is not JsonObject plots||plots.Count>0)throw new InvalidOperationException("请先收完分地作物");
            Meta.Remove("farm_plots");
            Meta["next_field_ts"]=FarmMaturity;
            return "已恢复农田自动混种";
        }
        int id=I(args,"plot");
        if(id<1||id>FarmPlotCount)throw new InvalidOperationException("农田地块无效");
        if(operation=="clear_plan"){
            TrackFarmSoil(id).Remove("next_crop");
            return $"已取消 {id} 号地的下一茬计划";
        }
        if(FarmRoadReason!="")throw new InvalidOperationException(FarmRoadReason);
        string key=id.ToString();
        var plot=FarmPlots?[key]?.AsObject();
        if(operation=="sow"){
            if(plot!=null)throw new InvalidOperationException("该地块已有作物");
            string crop=S(args,"crop"),order=S(args,"order_id");
            if(!FarmCrops.Contains(crop))throw new InvalidOperationException("无效作物");
            double maturity=FarmMaturity;
            CheckFarmDestination(crop,maturity,order);
            PlantFarmPlot(id,crop,order,maturity);
            return $"农田 {id} 号地已播种{crop}";
        }
        if(plot==null||D(plot,"expires")<=Now)throw new InvalidOperationException("这块地暂无作物");
        string item=S(plot,"crop");
        if(operation=="plan"){
            string nextCrop=S(args,"crop");
            if(!FarmCrops.Contains(nextCrop))throw new InvalidOperationException("无效作物");
            TrackFarmSoil(id)["next_crop"]=nextCrop;
            return $"已安排 {id} 号地收获后播种{nextCrop}";
        }
        if(operation=="irrigate"){
            if(I(plot,"yield")>0||FarmConditions(id).Moisture>=85)throw new InvalidOperationException("农田当前无需浇水");
            Take("清水",1);
            var soil=TrackFarmSoil(id);
            soil["moisture"]=Math.Min(100,D(soil,"moisture")+40);
            return $"已为 {id} 号地浇水，消耗清水 1 份";
        }
        if(operation=="fertilize"){
            if(D(plot,"mature")<=Now||B(plot,"fertilized")||FarmCompost<=0)throw new InvalidOperationException("需要一份堆肥和一块未施肥的生长中农田");
            Meta[FarmCompostKey]=FarmCompost-1;
            plot["fertilized"]=true;
            return $"已施肥 · {id} 号地产量增加 1 份";
        }
        if(operation=="route"){
            string order=S(args,"order_id");
            CheckFarmDestination(item,Math.Max(Now,D(plot,"mature")),order);
            plot["order_id"]=order;
            return "已更新这块地的收获去向";
        }
        if(operation!="harvest")throw new InvalidOperationException("农田操作无效");
        int yield=I(plot,"yield");
        if(yield<=0||D(plot,"mature")>Now)throw new InvalidOperationException("作物尚未成熟");
        Add(item,yield);
        string destination=S(plot,"order_id");
        int reserved=FarmHarvestReservation(item,destination,yield);
        if(reserved>0)SetBatchReservation(Rows("inventory").Last()!,OrderReservationOwner(destination),reserved);
        string next=FarmConditions(id).NextCrop;
        RecordFarmCrop(id,item);
        FarmPlots!.Remove(key);
        MarkFarmVisualHarvest(id);
        if(FarmCrops.Contains(next))PlantFarmPlot(id,next,"",FarmMaturity);
        return $"收获{item} {yield} 份入库"+(reserved>0?$" · 为订单预留 {reserved} 份":"")+(next.Length>0?$" · 已播种下一茬{next}":"");
    }

    void PlantFarmPlot(int id,string crop,string order,double maturity)
    {
        if(!FarmPlotsEnabled){
            Meta["farm_plots"]=new JsonObject();
            Meta["next_field_ts"]=0;
        }
        TrackFarmSoil(id).Remove("next_crop");
        FarmPlots![id.ToString()]=new JsonObject{{"crop",crop},{"order_id",order},{"mature",maturity},
            {"expires",maturity+Rule("TOWN_FIELD_TTL")},{"yield",0}};
    }

    void CheckFarmDestination(string crop,double mature,string order)
    {
        if(order!=""&&!FarmRoutes(crop,mature).Any(route=>route.Id==order))
            throw new InvalidOperationException("请选择收获时仍有效的同作物订单");
    }
}
