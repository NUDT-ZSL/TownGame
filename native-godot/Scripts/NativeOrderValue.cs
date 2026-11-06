using System;
using System.Linq;
using System.Text.Json.Nodes;

public sealed partial class NativeGame
{
    internal readonly record struct OrderSourceCost(string Source,long Purchase,long Fee,long MaterialValue)
    {
        internal long Cash=>Purchase+Fee;
        internal long Total=>Cash+MaterialValue;
    }
    internal readonly record struct OrderValue(long Reward,int Missing,long OnHandValue,OrderSourceCost? Buy,OrderSourceCost? Craft);

    internal OrderValue QuoteOrderValue(JsonNode order)
    {
        int reward=I(order,"reward");
        string item=S(order,"item");
        if(S(order,"kind")!="item"||Rules["ITEMS"]?[item] is not JsonNode spec)return new(reward,0,0,null,null);
        int quantity=I(order,"quantity"),owned=Math.Min(quantity,Stock(item,OrderReservationOwner(S(order,"order_id"))));
        int missing=Math.Max(0,quantity-owned);
        long onHandValue=(long)owned*I(spec,"sell");
        if(missing==0)return new(reward,missing,onHandValue,null,null);

        int price=BuyPrice(item);
        OrderSourceCost? buy=price>0?new OrderSourceCost("购买",(long)price*missing,0,0):null;
        OrderSourceCost? craft=null;
        if(Rules["RECIPES"]?[item] is JsonObject recipe&&HasWorkshop(item)){
            int output=I(recipe,"count");
            if(output>0){
                long batches=(missing+(long)output-1)/output, fees=batches*I(recipe,"cost"),purchase=0,materials=0;
                bool available=true;
                foreach(var input in recipe["inputs"]!.AsObject()){
                    long needed=input.Value!.GetValue<int>()*batches;
                    long inStock=Math.Min(needed,Stock(input.Key)),toBuy=needed-inStock;
                    int inputPrice=BuyPrice(input.Key);
                    if(toBuy>0&&inputPrice<=0){available=false;break;}
                    purchase+=toBuy*inputPrice;
                    materials+=inStock*I(Spec("ITEMS",input.Key),"sell");
                }
                if(available)craft=new("加工",purchase,fees,materials);
            }
        }
        return new(reward,missing,onHandValue,buy,craft);
    }

    internal static string OrderRequestCaption(JsonNode order)=>S(order,"kind") switch{
        "checkin"=>"登记庄园近况",
        "item"=>$"{S(order,"item")} ×{I(order,"quantity")}",
        _=>S(order,"stage")+S(order,"species")
    };
}
