using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json.Nodes;

public sealed partial class NativeGame
{
    const string SupplyMinimumKey="native_supply_minimum",SupplyOutflowKey="native_supply_outflow";
    const int SupplyHistoryDays=3;
    internal readonly record struct SupplyWatch(string Item,int Available,int Minimum,double OutflowPerDay,double? Runway,double? IncomingAt,string Source);

    internal int SupplyMinimum(string item)=>Meta[SupplyMinimumKey] is JsonObject rules?I(rules,item):0;

    string SupplyMinimumCommand(JsonObject args)
    {
        string item=S(args,"item");
        int minimum=I(args,"minimum",-1);
        if(Rules["ITEMS"]?[item]==null||minimum<0||minimum>9999)throw new InvalidOperationException("请选择物资并输入 0 至 9999 的库存下限");
        var rules=(Meta[SupplyMinimumKey]??=new JsonObject()).AsObject();
        if(minimum==0){rules.Remove(item);return $"已关闭{item}库存下限提醒";}
        rules[item]=minimum;
        return $"{item}的库存下限已设为 {minimum} 份";
    }

    static int SupplyDay(double when)=>(int)Math.Floor(when/Day);
    static string SupplyDayKey(int day)=>day.ToString(CultureInfo.InvariantCulture);

    void RecordInventoryOutflow(string item,int count)
    {
        var history=(Meta[SupplyOutflowKey]??=new JsonObject{{"since",Now},{"days",new JsonObject()}}).AsObject();
        var days=history["days"]!.AsObject();
        int today=SupplyDay(Now);
        foreach(var day in days.Select(pair=>pair.Key).Where(key=>int.TryParse(key,out int value)&&value<=today-SupplyHistoryDays).ToArray())days.Remove(day);
        var totals=(days[SupplyDayKey(today)]??=new JsonObject()).AsObject();
        totals[item]=(int)Math.Min(int.MaxValue,(long)I(totals,item)+count);
    }

    internal SupplyWatch SupplyStatus(string item)
    {
        int available=Stock(item);
        double rate=0;
        if(Meta[SupplyOutflowKey] is JsonObject history&&history["days"] is JsonObject days){
            int today=SupplyDay(Now),observed=Math.Clamp(today-SupplyDay(D(history,"since",Now))+1,1,SupplyHistoryDays);
            double total=0;
            for(int offset=0;offset<observed;offset++)if(days[SupplyDayKey(today-offset)] is JsonObject record)total+=I(record,item);
            rate=total/observed;
        }
        var arrivals=new List<(double At,string Source)>();
        foreach(var job in WorkshopOrders()){
            if(job.Reason.Length>0)break;
            if(job.Item==item)arrivals.Add((Math.Max(Now,job.Finishes),"加工队列"));
        }
        if(FarmRoadReason==""&&FarmCrops.Contains(item)){
            foreach(int plot in Enumerable.Range(1,FarmPlotCount))
                if(FarmPlan(plot) is { } plan&&plan.Crop==item&&plan.OrderId.Length==0)
                    arrivals.Add((Math.Max(Now,plan.Mature),$"农田 {plot} 号地可收"));
            if(Meta["field_batch"] is JsonObject mixed&&I(mixed,item)>0&&D(Meta,"field_expiry_ts")>Now)
                arrivals.Add((Now,"混种农田可收"));
            else if(!FarmPlotsEnabled&&D(Meta,"next_field_ts")>Now)arrivals.Add((D(Meta,"next_field_ts"),"混种农田预计成熟"));
        }
        foreach(var pet in Pets.OfType<JsonNode>().Where(pet=>Active(pet)&&!B(pet,"fostered")&&S(Spec("SPECIES",S(pet,"species")),"product")==item)){
            if(D(pet,"product_ready_ts")>0&&D(pet,"product_expiry_ts")>Now)arrivals.Add((Now,$"{S(pet,"name")}可收产物"));
            else{
                double due=Math.Max(D(pet,"next_product_ts"),D(pet,"birth_ts")+D(pet,"paused_seconds")+D(pet,"lifespan_max")*.25);
                if(due>Now)arrivals.Add((due,$"{S(pet,"name")}预计产出"));
            }
        }
        var incoming=arrivals.OrderBy(entry=>entry.At).FirstOrDefault();
        return new(item,available,SupplyMinimum(item),rate,rate>0?available/rate:null,
            arrivals.Count>0?incoming.At:null,incoming.Source??"");
    }

    internal IEnumerable<(string Item,int Available,int Minimum)> SupplyShortages()
    {
        if(Meta[SupplyMinimumKey] is not JsonObject rules)yield break;
        foreach(var pair in rules){
            int available=Stock(pair.Key),minimum=I(rules,pair.Key);
            if(available<minimum)yield return (pair.Key,available,minimum);
        }
    }
}
