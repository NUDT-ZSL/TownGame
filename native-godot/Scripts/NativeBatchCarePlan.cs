using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

public sealed partial class NativeGame
{
    internal sealed record CarePlanRow(string Id,string Name,string Item,int Quantity,string Skip);
    internal sealed record BatchCarePlan(string Kind,CarePlanRow[] Rows,Dictionary<string,int> Supplies,Dictionary<string,int> Products)
    {
        internal string Token=>JsonSerializer.Serialize(Rows);
    }

    internal BatchCarePlan PreviewBatchCare(string kind,IReadOnlySet<string>? selected=null)
    {
        if(kind is not ("feed" or "bath" or "play" or "collect"))throw new InvalidOperationException("不支持的批量照护");
        var budget=new Dictionary<string,int>();
        var supplies=new Dictionary<string,int>();
        var products=new Dictionary<string,int>();
        int space=Storage-Rows("inventory").Sum(batch=>I(batch!,"quantity"));
        var food=kind=="feed"?Rules["ITEMS"]!.AsObject().Where(pair=>pair.Key!="腐败物"&&pair.Value?["feed"]!=null)
            .Select(pair=>pair.Key).ToArray():Array.Empty<string>();
        var candidates=Pets.OfType<JsonNode>().Where(pet=>Active(pet)&&(selected==null||selected.Contains(S(pet,"pet_id"))))
            .OrderBy(pet=>kind=="feed"?D(pet,"hunger"):kind=="bath"?D(pet,"cleanliness"):0);
        var plan=new List<CarePlanRow>();
        foreach(var pet in candidates){
            string id=S(pet,"pet_id"),name=S(pet,"name"),item="",skip="";
            int quantity=0;
            if(B(pet,"fostered"))skip="寄养中";
            else switch(kind){
                case "feed":
                    if(D(pet,"hunger")>=100){skip="饱食已满";break;}
                    item=food.FirstOrDefault(key=>Available(key)>0)??"";
                    if(item.Length==0){skip="缺少可用食物";break;}
                    quantity=1;
                    Use(item);
                    break;
                case "bath":
                    if(D(pet,"cleanliness")>=100){skip="清洁已满";break;}
                    if(Available("清水")==0||Available("沐浴露")==0){skip="清水或沐浴露不足";break;}
                    Use("清水");
                    Use("沐浴露");
                    break;
                case "play":
                    if(D(pet,"interact_ready_ts")>Now)skip="互动冷却中";
                    break;
                case "collect":
                    if(D(pet,"product_ready_ts")==0||D(pet,"product_expiry_ts")<=Now){skip="暂无可收取产物";break;}
                    int count=B(pet,"rare")?2:1;
                    if(space<count){skip="仓库空间不足";break;}
                    item=S(Spec("SPECIES",S(pet,"species")),"product");
                    quantity=count;
                    space-=count;
                    products[item]=products.GetValueOrDefault(item)+count;
                    break;
            }
            plan.Add(new(id,name,item,quantity,skip));
        }
        return new(kind,plan.ToArray(),supplies,products);

        int Available(string item)
        {
            if(!budget.TryGetValue(item,out int amount))budget[item]=amount=Stock(item);
            return amount;
        }
        void Use(string item)
        {
            budget[item]=Available(item)-1;
            supplies[item]=supplies.GetValueOrDefault(item)+1;
        }
    }
}
