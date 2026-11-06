using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;

public sealed partial class NativeGame
{
    const string WorkshopQueueKey="native_workshop_queue";
    const string WorkshopOwnerPrefix="workshop:";
    const int WorkshopQueueLimit=12;
    const double WorkshopStartDelay=5*60;
    internal readonly record struct WorkshopOrder(string Id,string Item,string Inputs,int Yield,int Fee,double Starts,double Finishes,bool Running,string Reason);

    IEnumerable<JsonObject> WorkshopJobs()=> (Meta[WorkshopQueueKey] as JsonArray)?.OfType<JsonObject>()??Enumerable.Empty<JsonObject>();
    bool IsWorkshopPending(JsonNode job)=>D(job,"finished_due")==0;
    JsonObject? PendingWorkshopOwner(string owner)=>owner.StartsWith(WorkshopOwnerPrefix,StringComparison.Ordinal)
        ?WorkshopJobs().FirstOrDefault(job=>IsWorkshopPending(job)&&WorkshopOwner(job)==owner):null;
    static string WorkshopOwner(JsonNode job)=>WorkshopOwnerPrefix+S(job,"id");
    double WorkshopDuration(string item)=>I(Spec("RECIPES",item),"level")*Hour;
    bool HasWorkshop(string item)=>Rows("buildings").OfType<JsonNode>().Any(building=>S(building,"kind")=="加工坊"&&I(building,"level")>=I(Spec("RECIPES",item),"level"));
    JsonObject WorkshopIngredients(string item)=>Spec("RECIPES",item)["inputs"]!.AsObject();

    internal WorkshopOrder[] WorkshopOrders()
    {
        var orders=new List<WorkshopOrder>();
        double next=Now;
        foreach(var job in WorkshopJobs()){
            string id=S(job,"id"),item=S(job,"item");
            bool pending=IsWorkshopPending(job);
            var ingredients=WorkshopIngredients(item);
            double start=pending?Math.Max(next,D(job,"earliest_start")):D(job,"started_ts");
            double finish=pending?start+WorkshopDuration(item):D(job,"finished_due");
            string reason=pending?WorkshopStartBlock(job):finish<=Now&&!WorkshopOutputFits(item)?"仓库空间不足，成品暂存在加工坊":"";
            orders.Add(new(id,item,string.Join("、",ingredients.Select(entry=>$"{entry.Key} ×{entry.Value!.GetValue<int>()}")),
                I(Spec("RECIPES",item),"count"),I(job,"fee"),start,finish,!pending,reason));
            next=Math.Max(next,finish);
        }
        return orders.ToArray();
    }

    double WorkshopNextBoundary()=>WorkshopJobs().FirstOrDefault() is JsonObject first
        ?D(first,IsWorkshopPending(first)?"earliest_start":"finished_due"):0;

    internal string WorkshopEnqueueReason(string item,int count)
    {
        if(count<1||count>WorkshopQueueLimit||WorkshopJobs().Count()+count>WorkshopQueueLimit)
            return $"加工队列最多容纳 {WorkshopQueueLimit} 批";
        if(!HasWorkshop(item))return "需要对应等级加工坊";
        if(I(Meta,"coins")<I(Spec("RECIPES",item),"cost")*count)return "金币不足";
        double last=Math.Max(Now+WorkshopStartDelay,WorkshopOrders().LastOrDefault().Finishes)+(count-1)*WorkshopDuration(item);
        foreach(var ingredient in WorkshopIngredients(item)){
            int needed=ingredient.Value!.GetValue<int>();
            int remaining=needed*count;
            // Check each start from last to first. The freshest stock must cover the latest batch.
            for(int batch=count-1;batch>=0;batch--){
                double start=last-(count-1-batch)*WorkshopDuration(item);
                int fresh=EarliestInventoryBatches(ingredient.Key).Where(row=>UsableAt(row,start)).Sum(row=>I(row,"quantity")-ReservedInventoryBatch(row));
                if(fresh<remaining-batch*needed)return $"{ingredient.Key}有效期或可用库存不足";
            }
        }
        return "";
    }

    static bool UsableAt(JsonNode batch,double time)=>D(batch,"expiry_ts")<=0||D(batch,"expiry_ts")>time;

    string WorkshopQueueCommand(JsonObject args)
    {
        var queue=(Meta[WorkshopQueueKey]??=new JsonArray()).AsArray();
        string operation=S(args,"operation");
        if(operation=="cancel"){
            var job=WorkshopJobs().FirstOrDefault(row=>S(row,"id")==S(args,"id"));
            if(job==null||!IsWorkshopPending(job))throw new InvalidOperationException("只可取消尚未开始的加工批次");
            ReleaseStockReservation(WorkshopOwner(job));
            Meta["coins"]=checked(I(Meta,"coins")+I(job,"fee"));
            queue.Remove(job);
            return "已取消待加工批次，预留材料和加工费已退回";
        }
        string item=S(args,"item");
        if(operation!="enqueue"||Rules["RECIPES"]?[item]==null)throw new InvalidOperationException("加工操作无效");
        int count=I(args,"batches",1);
        string reason=WorkshopEnqueueReason(item,count);
        if(reason.Length>0)throw new InvalidOperationException(reason);
        int fee=I(Spec("RECIPES",item),"cost");
        Pay(fee*count);
        double planned=Math.Max(Now+WorkshopStartDelay,WorkshopOrders().LastOrDefault().Finishes);
        for(int index=0;index<count;index++){
            var job=new JsonObject{{"id",NextId("workshop")},{"item",item},{"fee",fee},{"earliest_start",Now+WorkshopStartDelay}};
            queue.Add(job);
            foreach(var ingredient in WorkshopIngredients(item)){
                int remaining=ingredient.Value!.GetValue<int>();
                foreach(var batch in EarliestInventoryBatches(ingredient.Key).Where(row=>UsableAt(row,planned))){
                    int take=Math.Min(remaining,I(batch,"quantity")-ReservedInventoryBatch(batch));
                    if(take<=0)continue;
                    SetBatchReservation(batch,WorkshopOwner(job),take);
                    remaining-=take;
                    if(remaining==0)break;
                }
                if(remaining>0)throw new InvalidOperationException($"{ingredient.Key}可预留数量已变化");
            }
            planned+=WorkshopDuration(item);
        }
        return $"已排入{item}加工 · {count} 批";
    }

    string WorkshopStartBlock(JsonObject job)
    {
        string item=S(job,"item");
        if(!HasWorkshop(item))return "等待对应等级加工坊";
        foreach(var ingredient in WorkshopIngredients(item))
            if(InventoryAvailability(ingredient.Key,WorkshopOwner(job)).ReservedForOwner<ingredient.Value!.GetValue<int>())
                return $"预留的{ingredient.Key}不足，请取消该批后重新安排";
        return "";
    }

    bool WorkshopOutputFits(string item)=>FarmWarehouseSpace>=I(Spec("RECIPES",item),"count");

    void TickWorkshopQueue()
    {
        if(WorkshopJobs().FirstOrDefault() is not JsonObject job)return;
        string item=S(job,"item");
        if(!IsWorkshopPending(job)){
            if(D(job,"finished_due")>Now||!WorkshopOutputFits(item))return;
            int count=I(Spec("RECIPES",item),"count");
            Add(item,count);
            Meta[WorkshopQueueKey]!.AsArray().Remove(job);
            Log($"加工完成 · {item} {count} 份已入库");
            if(WorkshopJobs().FirstOrDefault() is not JsonObject next)return;
            job=next;
            item=S(job,"item");
        }
        if(Now<D(job,"earliest_start")||WorkshopStartBlock(job).Length>0)return;
        foreach(var ingredient in WorkshopIngredients(item))
            TakeReservedInventory(ingredient.Key,ingredient.Value!.GetValue<int>(),WorkshopOwner(job));
        job["started_ts"]=Now;
        job["finished_due"]=Now+WorkshopDuration(item);
    }
}
