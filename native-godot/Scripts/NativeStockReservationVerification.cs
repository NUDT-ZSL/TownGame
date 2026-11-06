using System;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;

internal static class NativeStockReservationVerification
{
    internal static int Run(JsonObject assets)
    {
        int count = 0;
        void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException($"STOCK_RESERVATION_TEST_FAILED: {message}");
        }
        void Test(string name, Action<NativeGame, JsonObject> run)
        {
            var fixture = new JsonObject();
            foreach (string key in new[] { "version", "snapshot", "rules", "terrain", "zones", "portals" })
                fixture[key] = assets[key]!.DeepClone();
            var game = new NativeGame(fixture);
            game.Rows("inventory").Clear();
            game.Rows("orders").Clear();
            game.Commons["enabled"] = false;
            run(game, fixture);
            game.Validate();
            count++;
            Godot.GD.Print($"RULE_OK {name}");
        }
        void AddOrder(NativeGame game, string id, string item, int quantity, double expires) =>
            game.Rows("orders").Add(new JsonObject
            {
                ["order_id"] = id, ["kind"] = "item", ["item"] = item, ["quantity"] = quantity,
                ["status"] = "进行中", ["created_ts"] = game.Now, ["expiry_ts"] = expires, ["reward"] = 20,
            });
        JsonObject Reserve(string owner, string item, int quantity) =>
            new() { ["owner"] = owner, ["item"] = item, ["quantity"] = quantity };

        Test("reservation_owners_persist_and_isolate", (game, fixture) =>
        {
            const string item = "普通饲料";
            string first = NativeGame.OrderReservationOwner("first"), second = NativeGame.OrderReservationOwner("second");
            AddOrder(game, "first", item, 2, game.Now + NativeGame.Day);
            AddOrder(game, "second", item, 2, game.Now + NativeGame.Day);
            game.Add(item, 4);
            game.Execute("reserve_stock", Reserve(first, item, 2));
            string before = game.State.ToJsonString();
            bool failed = false;
            try { game.Execute("reserve_stock", Reserve(second, item, 2), () => throw new IOException("test persistence failure")); }
            catch (IOException) { failed = true; }
            Check(failed && game.State.ToJsonString() == before, "预留保存失败后没有恢复库存");
            game.Execute("reserve_stock", Reserve(second, item, 2));
            var restored = new NativeGame(fixture.DeepClone().AsObject());
            Check(restored.Stock(item) == 0 && restored.Stock(item, first) == 2 && restored.Stock(item, second) == 2,
                "读取存档后同一库存被多个委托使用");
            bool rejected = false;
            try { restored.Execute("sell", new JsonObject { ["item"] = item, ["quantity"] = 1 }); }
            catch (InvalidOperationException) { rejected = true; }
            Check(rejected, "出售可以绕过预留");
            restored.Execute("order", new JsonObject { ["order_id"] = "first" });
            Check(restored.InventoryAvailability(item, second) is { Total: 2, Available: 0, ReservedForOwner: 2 },
                "交付扣除了其他订单的预留");
            restored.Execute("reserve_stock", Reserve(second, item, 0));
            Check(restored.Stock(item) == 2, "释放后物资没有恢复可用");
        });

        Test("reservation_fifo_expiry_and_spoilage", (game, _) =>
        {
            const string item = "鲜草";
            string owner = NativeGame.OrderReservationOwner("food");
            AddOrder(game, "food", item, 3, game.Now + 3 * NativeGame.Hour);
            game.Add(item, 2);
            game.Add(item, 3);
            game.Rows("inventory")[0]!["expiry_ts"] = game.Now + 90;
            game.Rows("inventory")[1]!["expiry_ts"] = game.Now + 2 * NativeGame.Hour;
            game.Execute("reserve_stock", Reserve(owner, item, 3));
            Check(game.InventoryAvailability(item, owner) is { ReservedForOwner: 3, ExpiringForOwner: 3 }
                && game.ReservedInventoryBatch(game.Rows("inventory")[0]!) == 2, "预留没有优先使用最早到期批次");
            game.Take(item, 1);
            Check(NativeGame.I(game.Rows("inventory")[0]!, "quantity") == 2, "通用消费使用了预留的临期批次");
            game.Advance(100);
            Check(game.InventoryAvailability(item, owner) is { Total: 2, Available: 1, ReservedForOwner: 1 }
                && game.Stock("腐败物") == 2, "过期的预留没有正常腐坏并释放");
            game.Meta["sim_ts"] = game.Now + 3 * NativeGame.Hour;
            Check(game.InventoryAvailability(item, owner).ReservedForOwner == 0, "委托到期仍锁定库存");
        });

        Test("season_reservation_progress_and_rollover", (game, _) =>
        {
            game.Meta["start_ts"] = game.Now;
            var commission = game.TownCommission();
            int season = game.TownCommissionSeason;
            string owner = NativeGame.CommissionReservationOwner(season);
            foreach (var supply in commission.Plan.Supplies)
            {
                game.Add(supply.Item, supply.Count);
                game.Rows("inventory").Last()!["expiry_ts"] = 0;
                game.Execute("reserve_stock", Reserve(owner, supply.Item, supply.Count));
            }
            Check(game.TownCommission().SuppliesReady && commission.Plan.Supplies.All(item => game.Stock(item.Item) == 0),
                "季节委托看不到自己预留的物资");
            game.Execute("town_commission", new JsonObject { ["step"] = "prepare", ["season"] = season });
            Check(game.TownCommission() is { Stage: 1, SuppliesReady: true }, "确认备货时释放或消耗了预留");
            game.Meta["sim_ts"] = commission.Deadline + 1;
            Check(game.TownCommissionSeason == season + 1 &&
                commission.Plan.Supplies.All(item => game.Stock(item.Item) == item.Count),
                "换季后上季物资仍被锁定");
        });
        return count;
    }
}
