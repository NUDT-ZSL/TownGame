using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;

public sealed partial class NativeGame
{
    public const int BlueprintLimit=12;
    public sealed record BlueprintStep(string Action,JsonObject Args);
    public sealed record BlueprintEstimate(int Cost,int ValidSteps,string Reason);

    public BlueprintEstimate PreviewBlueprint(IReadOnlyList<BlueprintStep> steps)
    {
        if(steps.Count==0)return new(0,0,"先加入施工项目");
        if(steps.Count>BlueprintLimit)return new(0,0,$"每张蓝图最多 {BlueprintLimit} 项");
        JsonObject original=State;
        long revision=WorldRevision;
        int balance=I(Meta,"coins"),finished=0;
        const int previewBudget=int.MaxValue/2;
        root["snapshot"]=original.DeepClone();
        try{
            Meta["coins"]=previewBudget;
            foreach(var step in steps){
                ApplyBlueprintStep(step);
                finished++;
            }
            int cost=previewBudget-I(Meta,"coins");
            return new(cost,finished,cost>balance?$"还差 {cost-balance} 金币":"");
        }catch(Exception error) when(error is InvalidOperationException or ArgumentException or FormatException){
            return new(previewBudget-I(Meta,"coins"),finished,$"第 {finished+1} 项：{error.Message}");
        }finally{
            root["snapshot"]=original;
            WorldRevision=revision;
        }
    }

    void ApplyBlueprintStep(BlueprintStep step)
    {
        bool allowed=step.Action switch{
            "road_stroke"=>!B(step.Args,"remove"),
            "decoration"=>S(step.Args,"operation")=="place",
            "build_town" or "build_manor"=>true,
            _=>false
        };
        if(!allowed)throw new InvalidOperationException("蓝图仅支持新铺道路、城镇建筑、庄园建筑及新放装饰");
        if(step.Action=="build_town"&&Rules["CATALOG"]?[S(step.Args,"catalog_key")]==null)
            throw new InvalidOperationException("未知城镇建筑");
        if(step.Action=="build_manor"&&(Rules["BUILDINGS"]?[S(step.Args,"kind")]==null||
            !Rows("buildings").OfType<JsonNode>().Any(plot=>I(plot,"building_id")==I(step.Args,"building_id",-1))))
            throw new InvalidOperationException("庄园建筑或地块不存在");
        Command(step.Action,step.Args);
    }

    string BlueprintCommand(JsonObject args)
    {
        if(args["steps"] is not JsonArray rows||rows.Count>BlueprintLimit)
            throw new InvalidOperationException("蓝图项目无效");
        var steps=new List<BlueprintStep>(rows.Count);
        foreach(var row in rows){
            if(row is not JsonObject operation||operation["args"] is not JsonObject data)
                throw new InvalidOperationException("蓝图项目无效");
            steps.Add(new(S(operation,"action"),data));
        }
        var estimate=PreviewBlueprint(steps);
        if(estimate.Reason!="")throw new InvalidOperationException(estimate.Reason);
        if(estimate.Cost!=I(args,"cost",-1))throw new InvalidOperationException("施工报价已变化，请重新核对");
        foreach(var step in steps)ApplyBlueprintStep(step);
        return $"已提交蓝图 · {steps.Count} 项 · 花费 {estimate.Cost} 金币";
    }
}
