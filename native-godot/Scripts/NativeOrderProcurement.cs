using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;

public sealed partial class NativeGame
{
    internal sealed record ProcurementLine(string Item,int Required,int ReservedForOrders,int Free,int OtherReserved,int Missing);

    internal IEnumerable<JsonNode> ActiveProcurementOrders()=>Rows("orders").OfType<JsonNode>()
        .Where(ActiveMaterialOrder).OrderBy(order=>D(order,"expiry_ts"));

    internal ProcurementLine[] OrderProcurement(IReadOnlySet<string> selected)
    {
        return ActiveProcurementOrders().Where(order=>selected.Contains(S(order,"order_id")))
            .GroupBy(order=>S(order,"item")).OrderBy(group=>group.Key,StringComparer.Ordinal).Select(group=>{
                int needed=group.Sum(order=>I(order,"quantity")),usableHolds=0,totalSelectedHolds=0;
                foreach(var order in group){
                    int held=InventoryAvailability(group.Key,OrderReservationOwner(S(order,"order_id"))).ReservedForOwner;
                    totalSelectedHolds+=held;
                    usableHolds+=Math.Min(held,I(order,"quantity"));
                }
                var stock=InventoryAvailability(group.Key);
                int free=Math.Min(stock.Available,Math.Max(0,needed-usableHolds));
                return new ProcurementLine(group.Key,needed,usableHolds,free,Math.Max(0,stock.Reserved-totalSelectedHolds),Math.Max(0,needed-usableHolds-free));
            }).ToArray();
    }
}
