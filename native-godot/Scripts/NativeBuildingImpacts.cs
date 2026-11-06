using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;

public sealed partial class NativeGame
{
    public string ManorUpgradeImpact(JsonNode building)
    {
        string kind=S(building,"kind");
        int level=I(building,"level");
        var effects=new List<string>();
        int growth=I(Spec("BUILDINGS",kind),"growth");
        int slots=Slots(building);
        if(growth>0)effects.Add($"伙伴容量 {slots} → {slots+growth}");
        if(slots>0){
            double decay=Rule("LEVEL_DECAY_BONUS",.06);
            effects.Add($"日常状态基础消耗倍率 {1/(1+decay*(level-1)):P0} → {1/(1+decay*level):P0}");
        }
        if(kind=="仓库")effects.Add($"总仓储 {Storage} → {Storage+I(Rules,"STORAGE_PER_LEVEL",100)} 格");
        if(kind=="加工坊"){
            string[] recipes=WorkshopRecipesChanged(building,level,level+1);
            if(recipes.Length>0)effects.Add("解锁配方 "+string.Join("、",recipes));
        }
        if(kind=="宠物医院"){
            int fee(int at)=>Math.Max(0,I(Rules,"TREATMENT_FEE")-at*I(Rules,"HOSPITAL_FEE_REDUCTION"));
            effects.Add($"诊费 {fee(level)} → {fee(level+1)} 金币");
            effects.Add($"治疗成功率等级加成 +{Rule("MEDICAL_LEVEL_BONUS"):P0}，受单项成功率上限约束");
            effects.Add($"治疗恢复量 +{Rule("HOSPITAL_HEAL_BONUS"):0.##}");
            effects.Add($"住院每小时恢复 +{Rule("HOSPITAL_RECOVERY",.08):0.##}");
        }
        if(kind=="商店"){
            effects.Add($"门票收入系数 +{Rule("VISITOR_SHOP_BONUS"):P0}");
            int other=OtherManorLevel(building);
            double saleGain=(Math.Max(level+1,other)-Math.Max(level,other))*Rule("SELL_SHOP_BONUS");
            if(saleGain>0)effects.Add($"伙伴出售估价系数 +{saleGain:P0}");
        }
        if(kind=="游乐区")effects.Add($"门票收入系数 +{Rule("VISITOR_PARK_BONUS"):P0}");
        return effects.Count==0?"本级没有额外经营收益。":"升级收益 · "+string.Join(" · ",effects);
    }

    string[] WorkshopRecipesChanged(JsonNode building,int previous,int next)
    {
        int low=Math.Max(Math.Min(previous,next),OtherManorLevel(building)),high=Math.Max(previous,next);
        return Rules["RECIPES"]!.AsObject().Where(recipe=>I(recipe.Value!,"level")>low&&I(recipe.Value!,"level")<=high)
            .Select(recipe=>recipe.Key).ToArray();
    }

    int OtherManorLevel(JsonNode building)=>Rows("buildings").OfType<JsonNode>()
        .Where(b=>!ReferenceEquals(b,building)&&S(b,"kind")==S(building,"kind"))
        .Select(b=>I(b,"level")).DefaultIfEmpty(0).Max();

    public string ManorDemolitionImpact(JsonNode building)
    {
        string kind=S(building,"kind");
        int level=I(building,"level"),slots=Slots(building),used=Used(I(building,"building_id"));
        var effects=new List<string>{"释放庄园地块"};
        if(slots>0)effects.Add($"减少 {slots} 个伙伴位，当前占用 {used}");
        if(kind=="仓库")effects.Add($"总仓储 {Storage} → {Storage-level*I(Rules,"STORAGE_PER_LEVEL",100)} 格");
        if(kind=="加工坊"){
            string[] recipes=WorkshopRecipesChanged(building,level,0);
            if(recipes.Length>0)effects.Add("停用配方 "+string.Join("、",recipes));
        }
        int other=OtherManorLevel(building);
        bool last=other==0;
        if(kind=="宠物医院"&&last)effects.Add("庄园将失去诊疗设施");
        if(kind=="商店"){
            if(last)effects.Add("庄园将失去商店交易和购买伙伴的入口");
            effects.Add($"门票收入系数减少 {level*Rule("VISITOR_SHOP_BONUS"):P0}");
            if(level>other&&!last)effects.Add($"伙伴出售估价系数减少 {(level-other)*Rule("SELL_SHOP_BONUS"):P0}");
        }
        if(kind=="游乐区")effects.Add($"门票收入系数减少 {level*Rule("VISITOR_PARK_BONUS"):P0}");
        return string.Join(" · ",effects);
    }

    public string TownDemolitionImpact(JsonNode building)
    {
        string key=S(building,"catalog_key");
        var spec=Spec("CATALOG",key);
        var effects=new List<string>{$"释放 {I(building,"w")}×{I(building,"h")} 格"};
        if(S(building,"status")=="building")effects.Add("取消当前施工");
        int residents=HomeMembers(S(building,"building_id")).Count();
        if(residents>0)effects.Add($"现住 {residents} 人");
        int capacity=ResidentHomeCapacity(building);
        if(capacity>0)effects.Add($"城镇住房容量减少 {capacity} 人");
        if(S(building,"status")=="ready"&&Frontage(building).Any(Connected().Contains)){
            double tax=D(spec,"tax")*(S(spec,"category")=="住宅"?LandFactor(building):1);
            if(tax>0){
                int loss=(int)Math.Floor(Bonus("tax"))-(int)Math.Floor(Math.Max(0,Bonus("tax")-tax));
                if(loss>0)effects.Add($"当前日税贡献约 {loss} 金币");
            }
            if(D(spec,"commerce")>0)effects.Add($"失去 {D(spec,"commerce"):P0} 商业加成");
            if(D(spec,"attraction")>0)effects.Add("失去吸引力贡献");
            if(D(spec,"desirability")!=0)effects.Add("周边地价重新评估");
            if(key=="windmill")effects.Add($"失去 {Rule("CROP_WINDMILL_BONUS",.15):P0} 作物产量系数");
            if(key=="bus_stop")effects.Add($"失去 {Rule("BUS_TRAFFIC_BONUS",.1):P0} 到访系数");
        }
        return string.Join(" · ",effects);
    }
}
