using System;
using System.Linq;
using System.Text.Json.Nodes;

public sealed partial class NativeGame
{
    bool ChallengeSpecies(JsonNode spec)=>I(spec,"buy_price")>=420;

    internal (string Item,int Quantity) StageMatchedOrder(int maximum)
    {
        var uncommon=Rules["SPECIES"]!.AsObject().Where(pair=>ChallengeSpecies(pair.Value!))
            .Select(pair=>S(pair.Value!,"product")).ToHashSet();
        var basics=new[]{"普通饲料","清水","谷物","鲜草"};
        var materials=Rules["ORDER_ITEM_POOL"]!.AsArray().Select(item=>item!.GetValue<string>())
            .Concat(basics).Where(item=>!uncommon.Contains(item)&&Rules["ITEMS"]?[item]!=null).Distinct().ToArray();
        var available=materials.Select(item=>(Item:item,Count:Stock(item))).OrderByDescending(stock=>stock.Count).FirstOrDefault();
        if(available.Count>0)return(available.Item,Math.Min(maximum,available.Count));
        int room=FarmWarehouseSpace,coins=I(Meta,"coins");
        foreach(var item in basics.Where(item=>Rules["ITEMS"]?[item]!=null).OrderBy(BuyPrice)){
            int price=BuyPrice(item);
            if(price<=0)continue;
            int affordable=Math.Min(room,coins/price);
            if(affordable>0)return(item,Math.Min(maximum,affordable));
        }
        var product=Pets.OfType<JsonNode>().Where(pet=>Active(pet)&&!B(pet,"fostered")&&D(pet,"product_ready_ts")>0
                &&D(pet,"product_expiry_ts")>Now&&room>=(B(pet,"rare")?2:1))
            .Select(pet=>S(Spec("SPECIES",S(pet,"species")),"product")).FirstOrDefault(item=>materials.Contains(item));
        if(product!=null)return(product,1);
        if(FarmRoadReason==""){
            var field=Enumerable.Range(1,FarmPlotCount).Select(FarmPlan).FirstOrDefault(plot=>plot is {Ready:true,OrderId:""}&&plot.Yield<=room);
            if(field!=null)return(field.Crop,Math.Min(maximum,field.Yield));
        }
        return ("",0);
    }
}
