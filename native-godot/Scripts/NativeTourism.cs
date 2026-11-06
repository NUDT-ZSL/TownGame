using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;

public sealed partial class NativeGame
{
    const string TourismKey="native_tourism";
    const string TouristReceiptKey="native_tourism_receipts";
    internal const int TouristReceiptLimit=12;
    static readonly string[] TouristAttractions={"market","park","ranch","city","estate"};
    internal static IReadOnlyList<string> TouristDestinations=>TouristAttractions;
    internal readonly record struct TourismReport(string Zone,int Planned,int Arrived,int Coins,int Closed,int Disconnected,int Timeouts);
    internal enum TouristStage { EnRoute, Blocked, Visiting, Leaving }
    internal readonly record struct TouristItinerary(string Id,string Name,string Destination,TouristStage Stage,string Issue,double Until);
    internal readonly record struct TouristReceipt(string Id,string Name,string Zone,double ArrivedAt,int Coins);

    internal TourismReport[] TourismReports()=>Meta[TourismKey] is JsonObject stats
        ?stats.Where(entry=>entry.Value!=null).Select(entry=>new TourismReport(entry.Key,I(entry.Value!,"planned"),I(entry.Value!,"arrived"),
            I(entry.Value!,"coins"),I(entry.Value!,"closed"),I(entry.Value!,"disconnected"),I(entry.Value!,"timeouts"))).ToArray()
        :Array.Empty<TourismReport>();

    JsonNode TourismStats(string zone)
    {
        var stats=(Meta[TourismKey]??=new JsonObject()).AsObject();
        return stats[zone]??=new JsonObject();
    }

    void CountTourism(string zone,string field,int amount=1)
    {
        var stats=TourismStats(zone);
        stats[field]=(int)Math.Min(int.MaxValue,(long)I(stats,field)+amount);
    }

    internal string TouristDestinationReason(string zone)
    {
        if(!TouristAttractions.Contains(zone)||ZoneLevel(zone)==0)return "景点尚未开放";
        return Connected().Contains(Portal(zone))?"":"景点道路未连通";
    }

    internal TouristItinerary[] TouristItineraries()=>People.OfType<JsonNode>().Where(person=>S(person,"role")=="游客")
        .Select(DescribeTourist).OrderBy(tourist=>tourist.Stage==TouristStage.Leaving)
        .ThenBy(tourist=>tourist.Until).ThenBy(tourist=>tourist.Id,StringComparer.Ordinal).ToArray();

    TouristItinerary DescribeTourist(JsonNode tourist)
    {
        string id=S(tourist,"person_id"),name=S(tourist,"name","旅人"),destination=S(tourist,"destination","estate");
        double until=D(tourist,"leave_ts");
        if(S(tourist,"status")=="离镇")return new(id,name,"station",TouristStage.Leaving,"",D(tourist,"exit_ts"));
        if(B(tourist,"spent"))return new(id,name,destination,TouristStage.Visiting,"",until);
        string issue=TouristDestinationReason(destination);
        if(issue.Length==0)issue=S(tourist,"native_tourism_block") switch{
            "disconnected"=>"当前步行路线受阻", "closed"=>"等待重新安排路线",_=>""
        };
        return new(id,name,destination,issue.Length==0?TouristStage.EnRoute:TouristStage.Blocked,issue,until);
    }

    internal TouristReceipt[] TouristReceipts()=>Meta[TouristReceiptKey] is JsonArray rows
        ?rows.TakeLast(TouristReceiptLimit).OfType<JsonObject>().Reverse().Select(row=>new TouristReceipt(
            ReadReceiptValue(row,"visitor",""),ReadReceiptValue(row,"name","旅人"),ReadReceiptValue(row,"zone",""),
            ReadReceiptValue(row,"arrived",0d),ReadReceiptValue(row,"coins",0)))
            .Where(row=>TouristAttractions.Contains(row.Zone)&&double.IsFinite(row.ArrivedAt)&&row.ArrivedAt>0&&row.Coins>0).ToArray()
        :Array.Empty<TouristReceipt>();

    void RecordTouristReceipt(JsonNode tourist,string zone,int coins)
    {
        if(Meta[TouristReceiptKey] is not JsonArray receipts)Meta[TouristReceiptKey]=receipts=new JsonArray();
        receipts.Add(new JsonObject{{"visitor",S(tourist,"person_id")},{"name",S(tourist,"name","旅人")},{"zone",zone},
            {"arrived",Now},{"coins",coins}});
        while(receipts.Count>TouristReceiptLimit)receipts.RemoveAt(0);
    }

    string PickTouristDestination(int sequence)
    {
        var open=TouristAttractions.Where(zone=>TouristDestinationReason(zone)=="").ToArray();
        return open.Length>0?open[sequence%open.Length]:"estate";
    }

    void TickTourism()
    {
        foreach(var tourist in People.OfType<JsonNode>().Where(p=>S(p,"role")=="游客").ToArray()){
            TrackTouristPlan(tourist);
            if(S(tourist,"status")!="离镇"&&Now>=D(tourist,"leave_ts")){
                if(!B(tourist,"spent")){
                    string zone=S(tourist,"destination","estate"),reason=TouristDestinationReason(zone);
                    if(reason.Length>0)ReportTouristBlock(tourist,zone,reason);
                    CountTourism(zone,S(tourist,"native_tourism_block","timeouts"));
                }
                tourist["status"]="离镇";
                tourist["destination"]="station";
            }
            if(Now>=D(tourist,"exit_ts")){People.Remove(tourist);WorldRevision++;}
        }
        if(Now<D(Meta,"next_town_arrival_ts"))return;
        int count=Connected().Contains(Portal("station"))?Math.Max(1,(int)((Rule("TOWN_BASE_ARRIVALS")+2*(Effective("station")?ZoneLevel("station"):0))*
            (1+.12*(Effective("park")?ZoneLevel("park"):0))*(1+Math.Min(D(Meta,"reputation"),100)/200)*D(WeatherSpec,"traffic")*(1+Bonus("traffic")))):0;
        count=Math.Min(count,I(Rules,"TOWN_MAX_PEOPLE",80)-People.Count);
        int total=I(Meta,"town_visitors_total");
        for(int i=0;i<count;i++){
            string zone=PickTouristDestination((int)(((long)total+i)%int.MaxValue));
            var tourist=new JsonObject{{"person_id",NextId("tourist")},{"name","旅人"+i},{"role","游客"},{"home_zone","estate"},
                {"destination",zone},{"entered_ts",Now},{"leave_ts",Now+Rule("TOWN_VISIT_STAY")},{"exit_ts",Now+Rule("TOWN_VISIT_STAY")+Rule("TOWN_EXIT_TIME")},
                {"spent",false},{"status","游览"},{"color_index",i%6}};
            People.Add(tourist);
            TrackTouristPlan(tourist);
        }
        if(count>0){WorldRevision++;Meta["town_visitors_total"]=(int)Math.Min(int.MaxValue,(long)total+count);}
        Meta["next_town_arrival_ts"]=D(Meta,"next_town_arrival_ts")+Rule("TOWN_VISIT_INTERVAL");
    }

    void TrackTouristPlan(JsonNode tourist)
    {
        if(B(tourist,"native_tourism_planned")||B(tourist,"spent")||S(tourist,"status")=="离镇")return;
        tourist["native_tourism_planned"]=true;
        CountTourism(S(tourist,"destination","estate"),"planned");
    }

    IEnumerable<double> TouristBoundaries()
    {
        foreach(var tourist in People.OfType<JsonNode>().Where(p=>S(p,"role")=="游客")){
            yield return D(tourist,"leave_ts");
            yield return D(tourist,"exit_ts");
        }
    }

    internal void ReportTouristBlock(JsonNode tourist,string zone,string reason)
    {
        if(S(tourist,"role")!="游客"||B(tourist,"spent")||S(tourist,"status")=="离镇"||S(tourist,"destination")!=zone)return;
        string key=reason.Contains("开放",StringComparison.Ordinal)?"closed":"disconnected";
        tourist["native_tourism_block"]=key;
    }

    internal void ReportTouristPathOpened(JsonNode tourist)=>tourist.AsObject().Remove("native_tourism_block");

    internal bool ReportTouristArrival(JsonNode tourist,string zone)
    {
        if(S(tourist,"role")!="游客"||B(tourist,"spent")||S(tourist,"status")=="离镇"||S(tourist,"destination")!=zone||Now>=D(tourist,"leave_ts"))return false;
        string reason=TouristDestinationReason(zone);
        if(reason.Length>0){ReportTouristBlock(tourist,zone,reason);return false;}
        double rate=zone=="market"?1+Bonus("commerce"):zone=="park"?.8:.6;
        int shops=Rows("buildings").OfType<JsonNode>().Where(building=>S(building,"kind")=="商店").Sum(building=>I(building,"level"));
        int parks=Rows("buildings").OfType<JsonNode>().Where(building=>S(building,"kind")=="游乐区").Sum(building=>I(building,"level"));
        rate*=1+shops*Rule("VISITOR_SHOP_BONUS")+parks*Rule("VISITOR_PARK_BONUS");
        int income=Math.Max(1,(int)(Rule("TOWN_SPEND_PER_LEVEL",6)*Math.Max(1,ZoneLevel(zone))*rate));
        int coins=checked(I(Meta,"coins")+income);
        int townIncome=(int)Math.Min(int.MaxValue,(long)I(Meta,"town_income")+income);
        TrackTouristPlan(tourist);
        ReportTouristPathOpened(tourist);
        tourist["spent"]=true;
        tourist["status"]="景点参观";
        Meta["coins"]=coins;
        Meta["town_income"]=townIncome;
        CountTourism(zone,"arrived");
        CountTourism(zone,"coins",income);
        RecordTouristReceipt(tourist,zone,income);
        Log($"游客到达{S(root["zones"]![zone]!,"name",zone)} · 收入 {income} 金币");
        return true;
    }
}
