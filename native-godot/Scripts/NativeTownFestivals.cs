using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;

public sealed partial class NativeGame
{
    internal sealed record TownFestival(string Key,string Title,string Zone,double Opens,double Closes,string Decoration);
    internal readonly record struct FestivalCompletion(string Title,double At,string Contribution,string Decoration);
    const string FestivalAwardsKey="native_festival_awards";
    const string FestivalDecorKey="native_festival_decor";
    const string FestivalHistoryKey="native_festival_history";
    const string MarketDecoration="kit_market_crates",ShowcaseDecoration="kit_park_bench";
    internal const int FestivalHistoryLimit=12;
    internal static IReadOnlyList<string> FestivalDecorationKinds {get;}=Array.AsReadOnly(new[]{MarketDecoration,ShowcaseDecoration});

    internal IEnumerable<TownFestival> FestivalOccurrences(double from)
    {
        double origin=Math.Floor(D(Meta,"start_ts")/Day)*Day;
        double week=origin+Math.Max(0,Math.Floor((from-origin)/(7*Day)))*7*Day;
        for(int i=0;i<2;i++){
            var market=new TownFestival("market","周末集市","market",week+5*Day+10*Hour,week+5*Day+15*Hour,MarketDecoration);
            var show=new TownFestival("showcase","伙伴展示日","park",week+6*Day+11*Hour,week+6*Day+16*Hour,ShowcaseDecoration);
            if(market.Closes>from)yield return market;
            if(show.Closes>from)yield return show;
            week+=7*Day;
        }
    }

    internal TownFestival? CurrentFestival=>FestivalOccurrences(Now).FirstOrDefault(festival=>festival.Opens<=Now&&Effective(festival.Zone));
    internal bool FestivalClaimed(TownFestival festival)=>Meta[FestivalAwardsKey] is JsonObject awards&&D(awards,festival.Key)==festival.Opens;

    IEnumerable<double> FestivalBoundaries()
    {
        foreach(var festival in FestivalOccurrences(Now)){
            yield return festival.Opens;
            yield return festival.Closes;
        }
    }

    static bool FestivalEligibleResident(JsonNode person)=>S(person,"role")=="居民"&&S(person,"status")!="离镇";
    HashSet<string> FestivalGuestIds(TownFestival? festival)
    {
        if(festival==null)return new();
        var working=Jobs.OfType<JsonNode>().Select(job=>S(job,"worker")).ToHashSet();
        return People.OfType<JsonNode>().Where(person=>FestivalEligibleResident(person)&&!working.Contains(S(person,"person_id"))
            &&(Meta["native_freight"]==null||!IsFreightDriver(S(person,"person_id"))))
            .OrderByDescending(person=>ResidentWorkZone(person)==festival.Zone)
            .ThenBy(person=>S(person,"person_id"),StringComparer.Ordinal).Take(5).Select(person=>S(person,"person_id")).ToHashSet();
    }

    internal string FestivalShowPetBlock(JsonNode pet)
    {
        if(!Active(pet))return "伙伴已离开庄园";
        if(B(pet,"fostered"))return "寄养中，接回后可重新评估";
        if(IsPetInHospital(pet))return "住院中，出院后可重新评估";
        if(NeedsPetTreatment(pet))return "有疾病或外伤，需要先诊疗";
        return D(pet,"health")<50?"健康低于 50，需要先恢复":"";
    }
    internal bool FestivalShowPetEligible(JsonNode pet)=>FestivalShowPetBlock(pet).Length==0;
    internal int FestivalDecorationCredits(string kind)=>Meta[FestivalDecorKey] is JsonObject credits?I(credits,kind):0;
    internal FestivalCompletion[] FestivalCompletions()=>Meta[FestivalHistoryKey] is JsonArray rows
        ?rows.TakeLast(FestivalHistoryLimit).OfType<JsonObject>().Reverse().Select(row=>new FestivalCompletion(
            ReadReceiptValue(row,"title",""),ReadReceiptValue(row,"at",0d),ReadReceiptValue(row,"contribution",""),
            ReadReceiptValue(row,"decoration","")))
            .Where(row=>row.Title.Length>0&&double.IsFinite(row.At)&&row.At>0&&FestivalDecorationKinds.Contains(row.Decoration)).ToArray()
        :Array.Empty<FestivalCompletion>();

    void RecordFestivalCompletion(TownFestival festival,string contribution)
    {
        if(Meta[FestivalHistoryKey] is not JsonArray history)Meta[FestivalHistoryKey]=history=new JsonArray();
        history.Add(new JsonObject{{"title",festival.Title},{"at",Now},{"contribution",contribution},{"decoration",festival.Decoration}});
        while(history.Count>FestivalHistoryLimit)history.RemoveAt(0);
    }
    void ChangeFestivalDecorationCredits(string kind,int change)
    {
        var credits=(Meta[FestivalDecorKey]??=new JsonObject()).AsObject();
        credits[kind]=FestivalDecorationCredits(kind)+change;
    }

    string FestivalCommand(JsonObject args)
    {
        var festival=CurrentFestival;
        if(festival==null||festival.Key!=S(args,"festival"))throw new InvalidOperationException("活动已结束或街区尚未接通");
        if(FestivalClaimed(festival))throw new InvalidOperationException("本次活动奖励已领取");
        string resident=S(args,"resident_id");
        if(!FestivalGuestIds(festival).Contains(resident))throw new InvalidOperationException("等参加活动的居民到场后再参加");
        string subject="为集市交付 2 份谷物";
        if(festival.Key=="market")Take("谷物",2);
        else{
            var pet=Pet(S(args,"pet_id"));
            if(!FestivalShowPetEligible(pet))throw new InvalidOperationException("请选择健康、在园的伙伴参加展示");
            subject=$"为{S(pet,"name")}完成展示登记";
            Note(pet,"参加了周末伙伴展示日。");
        }
        (Meta[FestivalAwardsKey]??=new JsonObject())[festival.Key]=festival.Opens;
        ChangeFestivalDecorationCredits(festival.Decoration,1);
        RecordFestivalCompletion(festival,subject);
        string decoration=DecorationName(festival.Decoration);
        return $"{subject} · 获得{decoration}，可在建造页免费摆放一件";
    }
}
