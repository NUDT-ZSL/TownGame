using System;
using System.Linq;
using System.Text.Json.Nodes;

public sealed partial class NativeGame
{
    const string IncubationKey="native_incubation";
    const string HatchHistoryKey="native_hatches";
    internal const int IncubatorCapacity=3;
    internal readonly record struct IncubationEgg(string Id,string Species,int Home,double Start,double Due);

    internal IncubationEgg[] IncubatorEggs()=>Meta[IncubationKey] is JsonArray eggs
        ?eggs.OfType<JsonNode>().Select(egg=>new IncubationEgg(S(egg,"id"),S(egg,"species"),I(egg,"building_id"),D(egg,"start"),D(egg,"due"))).ToArray()
        :Array.Empty<IncubationEgg>();
    int IncubatingEggCount()=> (Meta[IncubationKey] as JsonArray)?.Count??0;
    int IncubatingAtHome(int home)=>Meta[IncubationKey] is JsonArray eggs?eggs.Count(egg=>I(egg!,"building_id")==home):0;
    internal string IncubationReason(string home,int price)=>IncubatingEggCount()>=IncubatorCapacity?"孵化台已满":CompanionPurchaseReason(home,price);

    double NextIncubationBoundary()=>Meta[IncubationKey] is JsonArray eggs
        ?eggs.OfType<JsonNode>().Select(egg=>D(egg,"due")).DefaultIfEmpty(0).Min():0;

    string BuyCompanion(JsonObject args)
    {
        string species=S(args,"species"),sex=S(args,"sex","雌");
        bool egg=B(args,"egg");
        if(sex is not ("雌" or "雄"))throw new InvalidOperationException("性别只能为雌或雄");
        var spec=Spec("SPECIES",species);
        if(egg&&spec["egg_available"]!=null&&!B(spec,"egg_available"))throw new InvalidOperationException("该品种不出售宠物蛋");
        string preferred=S(spec,"building");
        int cost=(int)(D(spec,"buy_price",Rule("PET_BUY_COST"))*(egg?Rule("EGG_PRICE_FACTOR",1):1));
        string reason=egg?IncubationReason(preferred,cost):CompanionPurchaseReason(preferred,cost);
        if(reason.Length>0)throw new InvalidOperationException(reason);
        Pay(cost);
        if(!egg){NewPet(species,sex,false);return "新伙伴已到庄园";}
        var home=Rows("buildings").OfType<JsonNode>().First(building=>S(building,"kind")==preferred&&HomeHasSpace(building));
        var eggs=(Meta[IncubationKey]??=new JsonArray()).AsArray();
        double hours=Math.Max(1,Rule("EGG_INCUBATION_HOURS",8));
        eggs.Add(new JsonObject{{"id",NextId("egg")},{"species",species},{"sex",sex},{"rare",Random()<Rule("MUTATION_CHANCE")},
            {"building_id",I(home,"building_id")},{"start",Now},{"due",Now+hours*Hour}});
        return $"{species}蛋已放入孵化台 · 预计 {hours:0.#} 游戏小时破壳";
    }

    void TickIncubator()
    {
        if(Meta[IncubationKey] is not JsonArray eggs||eggs.Count==0)return;
        foreach(var egg in eggs.OfType<JsonNode>().Where(egg=>D(egg,"due")<=Now+1e-6).ToArray()){
            string species=S(egg,"species");
            int home=I(egg,"building_id");
            var target=Rows("buildings").OfType<JsonNode>().FirstOrDefault(building=>I(building,"building_id")==home
                &&S(building,"kind")==S(Spec("SPECIES",species),"building")&&Used(home)<=Slots(building));
            if(target==null){egg["due"]=Now+Hour;continue;}
            eggs.Remove(egg);
            bool rare=B(egg,"rare");
            var pet=NewPet(species,S(egg,"sex"),rare,home);
            if(rare)Meta["reputation"]=I(Meta,"reputation")+I(Rules,"EGG_RARE_REPUTATION");
            var hatches=(Meta[HatchHistoryKey]??=new JsonArray()).AsArray();
            hatches.Add(new JsonObject{{"pet_id",S(pet,"pet_id")},{"name",S(pet,"name")},{"when",Now}});
            while(hatches.Count>8)hatches.RemoveAt(0);
            Note(pet,"在庄园孵化台破壳。");
            Log($"{S(pet,"name")}从孵化台破壳，已入住预留居所");
        }
    }

    internal JsonNode[] RecentHatches()=>Meta[HatchHistoryKey] is JsonArray hatches?hatches.OfType<JsonNode>().Reverse().ToArray():Array.Empty<JsonNode>();

    internal string JuvenileCareReason(JsonNode pet)
    {
        if(!Active(pet)||B(pet,"fostered")||IsPetInHospital(pet))return "仅可照护在居所中的幼崽";
        if(Stage(pet)!="幼崽")return "只适用于幼崽";
        if(D(pet,"juvenile_care_ts")>Now)return "这位幼崽正在休息";
        if(Stock("清水")<1)return "需要 1 份清水";
        return "";
    }

    string JuvenileCare(string id)
    {
        var pet=Pet(id);
        string reason=JuvenileCareReason(pet);
        if(reason.Length>0)throw new InvalidOperationException(reason);
        Take("清水",1);
        Gain(pet,"cleanliness",18);
        Gain(pet,"happy",12);
        Gain(pet,"favor",3);
        pet["juvenile_care_ts"]=Now+3*Hour;
        Note(pet,"幼崽专属照护：擦拭绒毛并安抚。");
        return "已为幼崽擦拭绒毛并安抚";
    }
}
