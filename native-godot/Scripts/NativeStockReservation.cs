using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json.Nodes;

public sealed partial class NativeGame
{
    const string ReservationKey = "native_reservations";
    const double ReservationWarning = 12 * Hour;
    const string OrderReservationPrefix = "order:";
    const string CommissionReservationPrefix = "commission:";

    internal readonly record struct StockAvailability(int Total, int Available, int ReservedForOwner,
        int ExpiringForOwner, double NextExpiry)
    {
        public int Reserved => Total - Available;
        public int ForOwner => Available + ReservedForOwner;
    }

    internal static string OrderReservationOwner(string id) => OrderReservationPrefix + id;
    internal static string CommissionReservationOwner(int season) =>
        CommissionReservationPrefix + season.ToString(CultureInfo.InvariantCulture);

    bool ActiveMaterialOrder(JsonNode order) =>
        S(order, "kind") == "item" && S(order, "status") == "进行中" && D(order, "expiry_ts") > Now;

    JsonNode? ReservationOrder(string owner) => owner.StartsWith(OrderReservationPrefix, StringComparison.Ordinal)
        ? Rows("orders").OfType<JsonNode>().FirstOrDefault(order =>
            S(order, "order_id") == owner[OrderReservationPrefix.Length..] && ActiveMaterialOrder(order))
        : null;

    bool ActiveCommissionReservation(string owner) =>
        owner == CommissionReservationOwner(CommissionSeasonIndex)
        && (CurrentCommission is JsonNode state
            ? !B(state, "delivered")
            : (Math.Floor(Now / Day) + 1) * Day + 7 * Hour < D(Meta, "start_ts") + (CommissionSeasonIndex + 1) * CommissionSeasonLength);

    bool ActiveReservationOwner(string owner) => owner.StartsWith(CommissionReservationPrefix, StringComparison.Ordinal)
        ? ActiveCommissionReservation(owner)
        : ReservationOrder(owner) != null || PendingWorkshopOwner(owner)!=null;

    int ReservationLimit(string owner, string item)
    {
        if (ActiveCommissionReservation(owner))
            return ActiveCommissionPlan().Supplies
                .Where(supply => supply.Item == item).Sum(supply => supply.Count);
        var order = ReservationOrder(owner);
        return order != null && S(order, "item") == item ? I(order, "quantity") : 0;
    }

    bool FreshInventoryBatch(JsonNode batch)
    {
        double expiry = D(batch, "expiry_ts");
        return expiry <= 0 || expiry > Now;
    }

    IEnumerable<JsonNode> InventoryBatches(string item) => Rows("inventory").OfType<JsonNode>()
        .Where(batch => S(batch, "item") == item && FreshInventoryBatch(batch));

    IEnumerable<JsonNode> EarliestInventoryBatches(string item) => InventoryBatches(item)
        .OrderBy(batch => D(batch, "expiry_ts") > 0 ? D(batch, "expiry_ts") : double.PositiveInfinity);

    (int Total, int ForOwner) BatchReservations(JsonNode batch, string owner = "")
    {
        if (batch[ReservationKey] is not JsonObject holds || !FreshInventoryBatch(batch)) return (0, 0);
        int total = 0, forOwner = 0;
        int quantity = I(batch, "quantity");
        foreach (var hold in holds)
        {
            if (hold.Value is not JsonValue value || !value.TryGetValue<int>(out int requested) ||
                requested <= 0 || !ActiveReservationOwner(hold.Key)) continue;
            int accepted = Math.Min(requested, quantity - total);
            total += accepted;
            if (hold.Key == owner) forOwner = accepted;
        }
        return (total, forOwner);
    }

    internal int ReservedInventoryBatch(JsonNode batch) => BatchReservations(batch).Total;

    internal StockAvailability InventoryAvailability(string item, string owner = "")
    {
        int total = 0, available = 0, reserved = 0, expiring = 0;
        double nextExpiry = 0;
        foreach (var batch in InventoryBatches(item))
        {
            int count = I(batch, "quantity");
            var holds = BatchReservations(batch, owner);
            total += count;
            available += count - holds.Total;
            reserved += holds.ForOwner;
            double expiry = D(batch, "expiry_ts");
            if (holds.ForOwner <= 0 || expiry <= 0 || expiry - Now > ReservationWarning) continue;
            expiring += holds.ForOwner;
            if (nextExpiry == 0 || expiry < nextExpiry) nextExpiry = expiry;
        }
        return new(total, available, reserved, expiring, nextExpiry);
    }

    static void SetBatchReservation(JsonNode batch, string owner, int count)
    {
        if (count > 0)
        {
            var holds = (batch[ReservationKey] ??= new JsonObject()).AsObject();
            holds[owner] = count;
            return;
        }
        if (batch[ReservationKey] is not JsonObject existing) return;
        existing.Remove(owner);
        if (existing.Count == 0) batch.AsObject().Remove(ReservationKey);
    }

    int ReleaseStockReservation(string owner, string item = "")
    {
        int released = 0;
        foreach (var batch in Rows("inventory").OfType<JsonNode>())
        {
            if (item.Length > 0 && S(batch, "item") != item) continue;
            released += BatchReservations(batch, owner).ForOwner;
            SetBatchReservation(batch, owner, 0);
        }
        return released;
    }

    void PruneStockReservations()
    {
        foreach (var batch in Rows("inventory").OfType<JsonNode>())
        {
            if (batch[ReservationKey] is not JsonObject holds) continue;
            foreach (var owner in holds.Select(hold => hold.Key).ToArray())
                if (!ActiveReservationOwner(owner)) SetBatchReservation(batch, owner, 0);
        }
    }

    string StockReservationCommand(JsonObject args)
    {
        string owner = S(args, "owner"), item = S(args, "item");
        if(owner.StartsWith(WorkshopOwnerPrefix,StringComparison.Ordinal))throw new InvalidOperationException("加工材料请在队列中管理");
        int count = I(args, "quantity", -1);
        if (owner.Length == 0 || count < 0) throw new InvalidOperationException("预留操作无效");
        if (count == 0)
        {
            int released = ReleaseStockReservation(owner, item);
            if (released == 0) throw new InvalidOperationException("没有可释放的预留物资");
            return $"已释放 {released} 份预留物资";
        }

        int limit = ReservationLimit(owner, item);
        if (limit <= 0) throw new InvalidOperationException("这个委托已经结束或不需要该物资");
        var stock = InventoryAvailability(item, owner);
        if (count > limit - stock.ReservedForOwner) throw new InvalidOperationException("预留数量超过委托需求");
        if (count > stock.Available) throw new InvalidOperationException("可用库存不足");
        PinCommissionOffer(owner);
        PruneStockReservations();
        int remaining = count;
        foreach (var batch in EarliestInventoryBatches(item))
        {
            var holds = BatchReservations(batch, owner);
            int assigned = Math.Min(remaining, I(batch, "quantity") - holds.Total);
            if (assigned == 0) continue;
            SetBatchReservation(batch, owner, holds.ForOwner + assigned);
            remaining -= assigned;
            if (remaining == 0) break;
        }
        return $"已预留 {item} {count} 份";
    }

    void TakeReservedInventory(string item, int count, string owner)
    {
        if (count <= 0 || Stock(item, owner) < count) throw new InvalidOperationException(item + "可用库存不足");
        RecordInventoryOutflow(item,count);
        var batches = (owner.Length==0&&ExpiryPolicy(item)=="newest"?EarliestInventoryBatches(item).Reverse():EarliestInventoryBatches(item)).ToArray();
        if (owner.Length > 0)
        {
            foreach (var batch in batches)
            {
                int own = BatchReservations(batch, owner).ForOwner;
                int taken = Math.Min(count, own);
                if (taken == 0) continue;
                SetBatchReservation(batch, owner, own - taken);
                Deduct(batch, taken);
                count -= taken;
                if (count == 0) return;
            }
        }
        foreach (var batch in batches)
        {
            int taken = Math.Min(count, I(batch, "quantity") - BatchReservations(batch).Total);
            if (taken == 0) continue;
            Deduct(batch, taken);
            count -= taken;
            if (count == 0) return;
        }

        void Deduct(JsonNode batch, int amount)
        {
            int left = I(batch, "quantity") - amount;
            batch["quantity"] = left;
            if (left == 0) Rows("inventory").Remove(batch);
        }
    }
}
