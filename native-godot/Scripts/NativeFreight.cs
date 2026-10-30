using System;
using System.Linq;
using System.Text.Json.Nodes;

public sealed partial class NativeGame
{
    public JsonObject FreightState=>Meta["native_freight"]!.AsObject();
    public void InitializeFreight()
    {
        Meta["native_freight"]??=new JsonObject{{"vehicle_id","resident-freight-1"},{"driver_id",""},{"phase","parked"},{"source","fields"},{"destination","market"},{"cargo_item",""},{"cargo_count",0},{"trips",0},{"status","停在庄园货运点，等待司机"},{"next_trip_ts",0}};
        RefreshFreight(false);
    }
    public bool IsFreightDriver(string id)=>id!=""&&S(FreightState,"driver_id")==id;
    JsonNode? FreightDriver=>People.FirstOrDefault(p=>S(p!,"person_id")==S(FreightState,"driver_id")&&S(p!,"role")=="居民"&&Now-D(p!,"born_ts")>=7*Day);
    public void RefreshFreight(bool assign=true)
    {
        foreach(var p in People.Where(p=>B(p!,"freight_reserved")&&!IsFreightDriver(S(p!,"person_id")))){p!["freight_reserved"]=false;p["freight_vehicle_id"]="";}
        if(S(FreightState,"driver_id")!=""&&FreightDriver==null){CancelFreight("司机离开，货物退回公共仓库");return;}
        if(FreightDriver!=null){
            FreightDriver["freight_reserved"]=true;
            if(IsResidentNight&&S(FreightState,"phase")=="boarding")ReleaseFreight();
            return;
        }
        if(!assign||IsResidentNight||!B(Commons,"enabled")||Now<D(FreightState,"next_trip_ts"))return;
        string[] sources={"fields","ranch","industry"};
        string source=sources.Skip(I(FreightState,"trips")%3).Concat(sources).FirstOrDefault(Effective)??"";
        if(source==""||!Effective("market")){FreightState["status"]="等待生产区与商业区接通道路";return;}
        var driver=People.Where(p=>S(p!,"role")=="居民"&&Now-D(p!,"born_ts")>=7*Day&&S(p!,"job_id")==""&&!B(p!,"freight_reserved"))
            .OrderBy(p=>S(p!,"profession")=="建筑工"?0:S(p!,"profession")=="工匠"?1:2).ThenBy(p=>S(p!,"person_id"),StringComparer.Ordinal).FirstOrDefault();
        if(driver==null){FreightState["status"]="等待空闲成年居民驾驶";return;}
        driver["freight_reserved"]=true;driver["freight_vehicle_id"]=S(FreightState,"vehicle_id");
        FreightState["driver_id"]=S(driver,"person_id");FreightState["phase"]="boarding";FreightState["source"]=source;FreightState["destination"]="market";FreightState["status"]=S(driver,"name")+"正在前往货车";
    }
    public bool BoardFreight()
    {
        if(FreightDriver==null||S(FreightState,"phase")!="boarding"||IsResidentNight)return false;
        FreightState["phase"]="to_source";FreightState["status"]="驾驶货车前往生产区装货";return true;
    }
    public bool LoadFreight()
    {
        if(FreightDriver==null||S(FreightState,"phase")!="loading"||I(FreightState,"cargo_count")>0)return false;
        string item=S(FreightState,"source")=="industry"?"wood":"food";
        int reserve=item=="food"?People.Count(p=>S(p!,"role")=="居民")*3:8;
        int amount=Math.Min(item=="food"?8:4,Math.Max(0,I(PublicStock,item)-reserve));
        if(amount<=0){FreightState["phase"]="to_depot";FreightState["status"]="可运货物不足，保留居民口粮，空车回库";return false;}
        PublicStock[item]=I(PublicStock,item)-amount;FreightState["cargo_item"]=item;FreightState["cargo_count"]=amount;
        FreightState["phase"]="to_destination";FreightState["status"]="已装货，驶向商业区";return true;
    }
    public bool DeliverFreight()
    {
        if(FreightDriver==null||S(FreightState,"phase")!="unloading"||I(FreightState,"cargo_count")<=0)return false;
        int amount=I(FreightState,"cargo_count"),income=amount*(S(FreightState,"cargo_item")=="food"?3:5);
        Commons["funds"]=I(Commons,"funds")+income;
        FreightState["cargo_count"]=0;FreightState["cargo_item"]="";FreightState["trips"]=I(FreightState,"trips")+1;
        FreightState["phase"]="to_depot";FreightState["status"]=$"已卸货并收入公共预算{income}金币，正在回库";
        Log($"居民司机{S(FreightDriver!,"name")}运送{amount}份物资到商业区，公共收入{income}金币");return true;
    }
    public void ReleaseFreight()
    {
        if(I(FreightState,"cargo_count")>0)return;
        if(FreightDriver is JsonNode driver){driver["freight_reserved"]=false;driver["freight_vehicle_id"]="";}
        FreightState["driver_id"]="";FreightState["phase"]="parked";FreightState["status"]="停靠货运点，司机已下车";FreightState["next_trip_ts"]=Now+Hour;
    }
    public void CancelFreight(string reason)
    {
        int cargo=I(FreightState,"cargo_count");string item=S(FreightState,"cargo_item");
        if(cargo>0&&item is "food" or "wood")PublicStock[item]=I(PublicStock,item)+cargo;
        FreightState["cargo_count"]=0;FreightState["cargo_item"]="";ReleaseFreight();FreightState["status"]=reason;
    }
}
