using System;
using System.Linq;
using System.Text.Json.Nodes;

public sealed partial class NativeGame
{
    const string FreightReceiptKey="native_freight_receipts";
    internal const int FreightReceiptLimit=12;
    internal readonly record struct FreightReceipt(int Trip,double When,string Driver,string Source,string Destination,string Item,int Count,int Gold);

    void RecordFreightReceipt(string item,int amount,int income,string driver)
    {
        if(Meta[FreightReceiptKey] is not JsonArray receipts)Meta[FreightReceiptKey]=receipts=new JsonArray();
        receipts.Add(new JsonObject{{"trip",I(FreightState,"trips")},{"when",Now},{"driver",driver},
            {"source",S(FreightState,"source")},{"destination",S(FreightState,"destination","market")},
            {"item",item},{"count",amount},{"gold",income}});
        while(receipts.Count>FreightReceiptLimit)receipts.RemoveAt(0);
    }

    internal FreightReceipt[] FreightReceipts()=>Meta[FreightReceiptKey] is JsonArray rows
        ?rows.TakeLast(FreightReceiptLimit).OfType<JsonObject>().Reverse()
            .Select(row=>new FreightReceipt(ReadReceiptValue(row,"trip",0),ReadReceiptValue(row,"when",0d),ReadReceiptValue(row,"driver","居民司机"),
                ReadReceiptValue(row,"source",""),ReadReceiptValue(row,"destination",""),ReadReceiptValue(row,"item",""),
                ReadReceiptValue(row,"count",0),ReadReceiptValue(row,"gold",0)))
            .Where(row=>row.Trip>0&&double.IsFinite(row.When)&&row.When>0&&row.Count>0&&row.Gold>0&&row.Item is "food" or "wood").ToArray()
        :Array.Empty<FreightReceipt>();

    static T ReadReceiptValue<T>(JsonObject row,string key,T fallback)=>
        row[key] is JsonValue json&&json.TryGetValue<T>(out var value)?value:fallback;
}
