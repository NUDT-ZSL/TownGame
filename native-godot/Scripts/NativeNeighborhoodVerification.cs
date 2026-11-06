using System;
using System.Linq;
using System.Text.Json.Nodes;

internal static class NativeNeighborhoodVerification
{
    internal static int Run(JsonObject source)
    {
        int passed = 0;
        void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException("NEIGHBORHOOD_TEST_FAILED: " + message);
        }
        void Test(string name, Action<NativeGame, JsonObject> run)
        {
            var fixture = source.DeepClone().AsObject();
            var game = new NativeGame(fixture);
            game.Commons["enabled"] = false;
            run(game, fixture);
            game.Validate();
            passed++;
            Godot.GD.Print("RULE_OK " + name);
        }
        string Failure(NativeGame game, string action, JsonObject args)
        {
            try { game.Execute(action, args); }
            catch (InvalidOperationException failure) { return failure.Message; }
            return "";
        }

        Test("pet_preflight_matches_commands", (game, _) =>
        {
            var first = game.Pets[0]!;
            var second = game.Pets[1]!;
            var plot = game.Rows("buildings").OfType<JsonNode>().First(home => NativeGame.S(home, "kind") == "");
            plot["kind"] = "寄养屋";
            plot["level"] = 1;
            first["pregnancy_due_ts"] = game.Now + NativeGame.Day;
            game.Meta["coins"] = 0;
            string firstId = NativeGame.S(first, "pet_id"), secondId = NativeGame.S(second, "pet_id");
            var move = game.QuotePetMove(first, plot);
            Check(move is { Required: 2, Free: 3, Fee: > 0, Reason: "金币不足" }, "迁居未预检孕期床位或寄养费用");
            Check(Failure(game, "move", new JsonObject { ["pet_id"] = firstId,
                ["building_id"] = NativeGame.I(plot, "building_id") }) == move.Reason, "迁居预检和执行不一致");

            first = game.Pets[0]!;
            second = game.Pets[1]!;
            first["pregnancy_due_ts"] = 0;
            second["birth_ts"] = game.Now - 1;
            var breeding = game.CheckBreeding(first, second);
            Check(breeding.Any(check => check.Caption == "年龄" && check.Reason.Length > 0)
                && breeding.Any(check => check.Caption.StartsWith("金币") && check.Reason.Length > 0),
                "繁育没有同时预检年龄和费用");
            Check(Failure(game, "breed", new JsonObject { ["pet_id"] = firstId, ["mate_id"] = secondId })
                == breeding.First(check => check.Reason.Length > 0).Reason, "繁育预检和执行不一致");
        });

        Test("companions_activity_archive_persists", (game, fixture) =>
        {
            string first = NativeGame.S(game.Pets[0]!, "pet_id"), second = NativeGame.S(game.Pets[1]!, "pet_id");
            game.RecordCompanionActivity(first, second, NativeGame.CompanionActivity.Play);
            game.RecordCompanionActivity(second, first, NativeGame.CompanionActivity.Walk);
            game.RecordCompanionActivity(first, second, NativeGame.CompanionActivity.Rest);
            game.Pets[1]!["fostered"] = true;
            game.RecordCompanionActivity(first, second, NativeGame.CompanionActivity.Play);
            var restored = new NativeGame(fixture.DeepClone().AsObject());
            Check(restored.PetRelationships(first).Single() is { Encounters: 3, Play: 1, Walk: 1, Rest: 1,
                    LastActivity: NativeGame.CompanionActivity.Rest } && restored.Familiarity(second, first) == 3,
                "结伴活动未双向持久化，或错误计入寄养期间");
        });

        Test("resident_orders_unlock_followup", (game, fixture) =>
        {
            var residents = game.People.OfType<JsonNode>().Where(person => NativeGame.S(person, "role") == "居民").ToArray();
            var publisher = residents.First(person => game.Now - NativeGame.D(person, "born_ts") >= 7 * NativeGame.Day);
            string id = NativeGame.S(publisher, "person_id");
            foreach (var resident in residents.Where(person => person != publisher)) resident["status"] = "离镇";
            game.Rows("orders").Clear();
            game.Rows("inventory").Clear();
            void RefreshOrders()
            {
                game.Meta["next_order_ts"] = game.Now + 30;
                game.Advance(30);
            }
            void Deliver(JsonNode order)
            {
                game.Add(NativeGame.S(order, "item"), NativeGame.I(order, "quantity"));
                game.Execute("order", new JsonObject { ["order_id"] = NativeGame.S(order, "order_id") });
            }
            RefreshOrders();
            var orders = game.Rows("orders").OfType<JsonNode>().Where(order => NativeGame.S(order, "kind") == "item").Take(2).ToArray();
            Check(orders.Length == 2 && orders.All(order => NativeGame.S(order, "publisher_id") == id),
                "新订单没有使用实际在镇的成年发布人");
            foreach (var order in orders) Deliver(order);
            Check(game.NeighborhoodProgress(id) is { Deliveries: 2, StoryStep: 0 }, "普通订单错误推进故事或漏计交付");
            RefreshOrders();
            var followUp = game.Rows("orders").OfType<JsonNode>().Single(order => NativeGame.I(order, "story_step") == 1);
            Check(NativeGame.S(followUp, "publisher_id") == id && NativeGame.S(followUp, "story").Length > 0,
                "已完成的普通订单没有解锁同一居民的后续委托");
            Deliver(followUp);
            var restored = new NativeGame(fixture.DeepClone().AsObject());
            Check(restored.NeighborhoodProgress(id) is { Deliveries: 3, StoryStep: 1 }, "邻里故事没有随存档保留");
        });

        Test("season_commission_facility_rotation_and_pin", (game, fixture) =>
        {
            game.Meta["start_ts"] = game.Now;
            game.Meta["coins"] = 10000;
            game.Rows("inventory").Clear();
            const string facility = "windmill";
            var cell = (from y in Enumerable.Range(0, 80) from x in Enumerable.Range(0, 125)
                where game.PlacementReason(facility, x, y) == "" select new NativeGame.Cell(x, y)).First();
            game.Execute("build_town", new JsonObject { ["catalog_key"] = facility, ["cx"] = cell.X, ["cy"] = cell.Y });
            var building = game.Rows("town_buildings").Last()!;
            Check(game.TownCommission().Plan.Key == "spring-market", "在建设施提前替换了委托");
            building["status"] = "ready";
            var upgraded = game.TownCommission();
            Check(upgraded.Plan.Key == "spring-windmill" && upgraded.Plan.Coins > 190, "已完工并连通的设施没有解锁新方案");
            var supply = upgraded.Plan.Supplies[0];
            game.Add(supply.Item, supply.Count);
            game.Execute("reserve_stock", new JsonObject { ["owner"] = NativeGame.CommissionReservationOwner(game.TownCommissionSeason),
                ["item"] = supply.Item, ["quantity"] = supply.Count });
            building["status"] = "building";
            var restored = new NativeGame(fixture.DeepClone().AsObject());
            Check(restored.TownCommission().Plan.Key == upgraded.Plan.Key, "预留后更换设施或重新载入改动了备货清单");
            building["status"] = "ready";
            game.Meta["sim_ts"] = game.Now + NativeGame.D(game.Rules, "SEASON_DAYS") * 4 * NativeGame.Day;
            Check(game.TownCommission().Plan.Key == "spring-market", "同一季节在下一年没有轮换回标准方案");
        });
        return passed;
    }
}
