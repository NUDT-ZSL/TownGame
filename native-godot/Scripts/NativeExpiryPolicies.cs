using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;

public sealed partial class NativeGame
{
    const string ExpiryPoliciesKey="native_expiry_policies";
    const string FarmCompostKey="native_farm_compost";
    const double AutoCompostWindow=2*Hour;
    internal readonly record struct ExpiryReminder(string Item,int Count,double Due);
    internal int FarmCompost=>I(Meta,FarmCompostKey);
    internal string ExpiryPolicy(string item)=>Meta[ExpiryPoliciesKey]?[item]?.GetValue<string>()??"earliest";

    internal IEnumerable<ExpiryReminder> ExpirySaleReminders()=>Rows("inventory").OfType<JsonObject>()
        .Where(batch=>D(batch,"expiry_ts")>Now&&D(batch,"expiry_ts")-Now<=12*Hour&&ExpiryPolicy(S(batch,"item"))=="sell"&&
            ReservedInventoryBatch(batch)<I(batch,"quantity"))
        .GroupBy(batch=>S(batch,"item")).Select(group=>new ExpiryReminder(group.Key,
            group.Sum(batch=>I(batch,"quantity")-ReservedInventoryBatch(batch)),group.Min(batch=>D(batch,"expiry_ts"))))
        .Where(reminder=>reminder.Count>0).OrderBy(reminder=>reminder.Due);

    string ExpiryPolicyCommand(JsonObject args)
    {
        string item=S(args,"item"),mode=S(args,"mode");
        if(Rules["ITEMS"]?[item] is not JsonObject spec||D(spec,"ttl")<=0||item=="腐败物")
            throw new InvalidOperationException("请选择有保质期的正常物资");
        if(mode is not ("earliest" or "newest" or "sell" or "compost"))throw new InvalidOperationException("临期策略无效");
        if(mode=="sell"&&I(spec,"sell")<=0)throw new InvalidOperationException("这种物资无法出售");
        var policies=(Meta[ExpiryPoliciesKey]??=new JsonObject()).AsObject();
        if(mode=="earliest")policies.Remove(item);
        else policies[item]=mode;
        return $"{item}的临期规则已保存";
    }

    double NextCompostBoundary()
    {
        if(Meta[ExpiryPoliciesKey] is not JsonObject policies||policies.Count==0)return double.PositiveInfinity;
        return Rows("inventory").OfType<JsonObject>().Where(batch=>D(batch,"expiry_ts")-AutoCompostWindow>Now&&
            ExpiryPolicy(S(batch,"item"))=="compost").Select(batch=>D(batch,"expiry_ts")-AutoCompostWindow)
            .DefaultIfEmpty(double.PositiveInfinity).Min();
    }

    void ApplyCompostPolicies()
    {
        if(Meta[ExpiryPoliciesKey] is not JsonObject policies||policies.Count==0)return;
        int converted=0;
        foreach(var batch in Rows("inventory").OfType<JsonObject>().ToArray()){
            double due=D(batch,"expiry_ts");
            if(due<=Now||due-Now>AutoCompostWindow||ExpiryPolicy(S(batch,"item"))!="compost")continue;
            int reserved=ReservedInventoryBatch(batch);
            converted+=I(batch,"quantity")-reserved;
            if(reserved>0)batch["quantity"]=reserved;
            else Rows("inventory").Remove(batch);
        }
        if(converted<=0)return;
        Meta[FarmCompostKey]=checked(FarmCompost+converted);
        Log($"未预留的临期物资已转作农田堆肥 · {converted} 份");
    }
}
