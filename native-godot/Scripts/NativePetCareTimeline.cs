using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;

public sealed partial class NativeGame
{
    const string HealthTimelineKey="native_health_timeline";
    internal readonly record struct HealthSnapshot(double Day,double Health,double Hunger,double Cleanliness);

    double HealthDay(double time)=>Math.Floor((time-D(Meta,"start_ts",time))/Day)+1;

    void CapturePetHealthDay()
    {
        double day=HealthDay(Now);
        if(D(Meta,"native_health_sample_day",double.MinValue)==day)return;
        Meta["native_health_sample_day"]=day;
        var histories=(Meta[HealthTimelineKey]??=new JsonObject()).AsObject();
        foreach(var pet in Pets.OfType<JsonNode>().Where(Active)){
            string id=S(pet,"pet_id");
            if(id.Length==0)continue;
            var history=(histories[id]??=new JsonArray()).AsArray();
            history.Add(new JsonObject{{"day",day},{"health",D(pet,"health")},{"hunger",D(pet,"hunger")},{"clean",D(pet,"cleanliness")}});
            while(history.Count>7)history.RemoveAt(0);
        }
    }

    double NextPetHealthDay()=>D(Meta,"start_ts",Now)+HealthDay(Now)*Day;

    internal HealthSnapshot[] PetHealthTimeline(JsonNode pet)
    {
        var saved=Meta[HealthTimelineKey]?[S(pet,"pet_id")] as JsonArray;
        var samples=saved?.OfType<JsonNode>().Select(row=>new HealthSnapshot(D(row,"day"),D(row,"health"),D(row,"hunger"),D(row,"clean")))
            ??Enumerable.Empty<HealthSnapshot>();
        double day=HealthDay(Now);
        return samples.Where(sample=>sample.Day<day).TakeLast(6)
            .Append(new(day,D(pet,"health"),D(pet,"hunger"),D(pet,"cleanliness"))).ToArray();
    }

    internal IEnumerable<string> PetCareRisks(JsonNode pet)
    {
        if(!Active(pet)||B(pet,"fostered"))yield break;
        if(D(pet,"hunger")<=20)yield return "饱食偏低，先喂食";
        if(D(pet,"cleanliness")<=25)yield return "清洁偏低，建议洗澡";
        if(Disease(pet).Length>0)yield return $"已出现{Disease(pet)}，可查看诊疗";
        if(D(pet,"injury")>0)yield return "外伤正在消耗健康";
        var home=Home(pet);
        string kind=S(home,"kind"),preferred=S(Spec("SPECIES",S(pet,"species")),"building");
        if(kind!=preferred&&kind is not ("宠物医院" or "游乐区"))yield return $"当前居所不适配，适宜居所为{preferred}";
        int occupants=Pets.OfType<JsonNode>().Count(other=>Active(other)&&I(other,"building_id")==I(home,"building_id"));
        if(occupants>1&&occupants>=Slots(home))yield return "同居已满，争斗风险偏高";
        var exposure=Exposure(I(home,"building_id"));
        if(exposure.Stale>0)yield return "居所有陈旧粪便，清扫可降低疾病风险";
        if(Outdoor(home)&&(D(WeatherSpec,"health")>0||Season=="冬"&&Rule("WINTER_DAMAGE")>0))
            yield return $"户外正受{Weather}和季节影响，可查看室内居所";
        if(Stage(pet)=="老年")yield return "已进入老年阶段，需要留意健康变化";
    }
}
