using Godot;
using System;
using System.Linq;
using System.Text.Json.Nodes;

public partial class Main
{
    sealed record OrderSupplyRoute(string OrderId, string Item, bool UseRecipe);

    OrderSupplyRoute? _orderSupplyRoute;

    JsonNode? FindSupplyOrder(string id) => Game.Rows("orders").OfType<JsonNode>()
        .FirstOrDefault(order => NativeGame.S(order, "order_id") == id);

    void OpenOrderSupply(string id, string item, bool useRecipe)
    {
        var order = FindSupplyOrder(id);
        if (order == null || !OrderPending(order, Game.Now))
        {
            SetManagementFeedback("原订单已结束，请查看订单状态。", failed: true);
            return;
        }
        if (Game.Stock(item, NativeGame.OrderReservationOwner(id)) >= NativeGame.I(order, "quantity"))
        {
            SetManagementFeedback("所需物资已备齐，可直接交付订单。");
            return;
        }

        _orderSupplyRoute = new OrderSupplyRoute(id, item, useRecipe);
        ShowManagement("商店");
    }

    string OrderSupplySource(string item)
    {
        var species = Game.Rules["SPECIES"]!.AsObject()
            .Where(pair => pair.Value != null && NativeGame.S(pair.Value, "product") == item)
            .Select(pair => pair.Key).ToArray();
        if (species.Length > 0)
            return $"获取方式 · 在「照护」收取{string.Join("、", species)}的产物。";
        return item switch
        {
            "粪便" => "获取方式 · 在「环境」清扫入库，或点击庄园地面的粪便。",
            "腐败物" => "获取方式 · 有保质期的库存过期后会转为腐败物。",
            _ => "商店不出售此物资，也没有对应的加工配方。",
        };
    }

    void OrderSupplyControls(OrderSupplyRoute route)
    {
        var card = MarketCard(_managementBody!, color: "f5eddc");
        _managementFocusTarget = card;
        MarketLabel(card, $"订单补货 · {route.Item}", 19, TownTheme.Accent("商店"));
        var orderStatus = MarketLabel(card, "", color: TownTheme.Muted, wrap: true);
        var navigation = new HFlowContainer();
        navigation.AddThemeConstantOverride("h_separation", 8);
        card.AddChild(navigation);
        var back = MarketButton(navigation, "返回原订单", () => ShowManagement("订单", route.OrderId), expand: false, accent: "订单");
        back.Name = "OrderSupplyReturn";
        MarketButton(navigation, "浏览全部商品", () =>
        {
            _orderSupplyRoute = null;
            ShowManagement("商店");
        }, expand: false);

        string owner = NativeGame.OrderReservationOwner(route.OrderId);
        int Missing(JsonNode order) => Math.Max(0, NativeGame.I(order, "quantity") - Game.Stock(route.Item, owner));
        string BlockReason(JsonNode? order)
        {
            if (order == null) return "原订单已归档，可返回查看。";
            if (!OrderPending(order, Game.Now))
            {
                string status = NativeGame.S(order, "status");
                return status is "进行中" or "过期" ? "原订单已到期，可返回查看。" : "原订单已结束，可返回查看。";
            }
            return Missing(order) == 0 ? "所需物资已备齐，可返回原订单交付。" : "";
        }
        void RefreshStatus()
        {
            var order = FindSupplyOrder(route.OrderId);
            string unavailable = BlockReason(order);
            if (order == null || unavailable.Length > 0)
            {
                orderStatus.Text = unavailable;
                return;
            }
            orderStatus.Text = $"{NativeGame.S(order, "npc")} · 需要 {NativeGame.I(order, "quantity")} 份 · {ReservationStockText(Game.InventoryAvailability(route.Item, owner))} · 还差 {Missing(order)} 份 · {DeadlineText(NativeGame.D(order, "expiry_ts") - Game.Now)}";
        }

        RefreshStatus();
        _refreshManagementPage += RefreshStatus;
        string CurrentBlockReason() => BlockReason(FindSupplyOrder(route.OrderId));
        if (route.UseRecipe)
            OrderSupplyRecipe(route.Item, CurrentBlockReason);
        else
        {
            var order = FindSupplyOrder(route.OrderId);
            SupplyPurchase(route.Item, order == null ? 1 : Missing(order), CurrentBlockReason, "OrderSupply");
        }
    }

    Control SupplyPurchase(string item, int missing, Func<string> blockReason, string namePrefix)
    {
        var card = MarketCard(_managementBody!);
        MarketLabel(card, $"采购 · {item}", 17, TownTheme.Accent("商店"));
        var details = MarketLabel(card, "", color: TownTheme.Muted, wrap: true);
        var row = new HFlowContainer();
        row.AddThemeConstantOverride("h_separation", 8);
        card.AddChild(row);
        MarketLabel(row, "买入份数");
        var quantity = MarketQuantity(row, Math.Clamp(missing, 1, 99));
        quantity.Name = $"{namePrefix}Quantity";
        int shownPrice = Game.BuyPrice(item);
        var buy = new Button { Name = $"{namePrefix}Buy", CustomMinimumSize = new Vector2(0, 38) };
        TownTheme.AccentButton(buy, "商店");
        row.AddChild(buy);
        var feedback = MarketLabel(card, "", color: TownTheme.Muted, wrap: true);
        buy.Pressed += () =>
        {
            int currentPrice = Game.BuyPrice(item);
            if (shownPrice != currentPrice)
            {
                Refresh();
                SetManagementFeedback("物资价格已变动，请核对新价格后再购买。", failed: true);
                return;
            }
            string reason = PurchaseBlockReason(currentPrice);
            if (reason.Length > 0)
            {
                Refresh();
                SetManagementFeedback(reason, failed: true);
                return;
            }
            Act("buy", new JsonObject { ["item"] = item, ["quantity"] = (int)quantity.Value });
        };
        quantity.ValueChanged += _ => Refresh();

        int Stored() => Game.Rows("inventory").Sum(batch => NativeGame.I(batch!, "quantity"));
        string PurchaseBlockReason(int price)
        {
            string reason = blockReason();
            if (reason.Length > 0) return reason;
            if (price <= 0) return "商店不出售此物资。";
            if (Stored() + (int)quantity.Value > Game.Storage) return "仓库空间不足，请减少购买份数。";
            return (long)price * (int)quantity.Value > NativeGame.I(Game.Meta, "coins") ? "金币不足。" : "";
        }
        void Refresh()
        {
            shownPrice = Game.BuyPrice(item);
            details.Text = $"每份 {shownPrice} 金币 · 仓库 {Stored()} / {Game.Storage} 份";
            buy.Text = $"购买 {(int)quantity.Value} 份 · {(long)shownPrice * (int)quantity.Value} 金币";
            feedback.Text = PurchaseBlockReason(shownPrice);
            feedback.Visible = feedback.Text.Length > 0;
            buy.Disabled = feedback.Visible;
        }

        Refresh();
        _refreshManagementPage += Refresh;
        return card.GetParent<Control>();
    }

    void OrderSupplyRecipe(string item, Func<string> orderBlockReason)
    {
        var recipe = Game.Rules["RECIPES"]![item]!;
        int level = NativeGame.I(recipe, "level"), fee = NativeGame.I(recipe, "cost"), count = NativeGame.I(recipe, "count");
        var card = MarketCard(_managementBody!);
        MarketLabel(card, $"加工配方 · {item}", 17, TownTheme.Accent("商店"));
        MarketLabel(card, $"每次产出 {count} 份 · 要求 {level} 级加工坊 · 加工费 {fee} 金币", color: TownTheme.Muted, wrap: true);
        var inputs = recipe["inputs"]!.AsObject().Select(pair => (
            Item: pair.Key, Count: pair.Value!.GetValue<int>(), Label: MarketLabel(card, ""))).ToArray();
        var make = new Button
        {
            Name = "OrderSupplyCraft", Text = $"加工 {item} · {count} 份", CustomMinimumSize = new Vector2(0, 38),
            SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin,
        };
        TownTheme.AccentButton(make, "商店");
        card.AddChild(make);
        var feedback = MarketLabel(card, "", color: TownTheme.Muted, wrap: true);
        make.Pressed += () =>
        {
            string reason = RecipeBlockReason();
            if (reason.Length > 0)
            {
                Refresh();
                SetManagementFeedback(reason, failed: true);
                return;
            }
            Act("craft", new JsonObject { ["item"] = item });
        };

        string RecipeBlockReason()
        {
            string reason = orderBlockReason();
            if (reason.Length > 0) return reason;
            if (!Game.Rows("buildings").OfType<JsonNode>().Any(building =>
                NativeGame.S(building, "kind") == "加工坊" && NativeGame.I(building, "level") >= level))
                return $"需要 {level} 级加工坊。";
            if (inputs.Any(input => Game.Stock(input.Item) < input.Count)) return "原料不足。";
            if (NativeGame.I(Game.Meta, "coins") < fee) return "金币不足。";
            int stored = Game.Rows("inventory").Sum(batch => NativeGame.I(batch!, "quantity"));
            return stored - inputs.Sum(input => input.Count) + count > Game.Storage ? "仓库空间不足。" : "";
        }
        void Refresh()
        {
            foreach (var input in inputs)
            {
                int owned = Game.Stock(input.Item);
                input.Label.Text = $"{input.Item} 需 {input.Count} 份 · 库存 {owned} 份";
                input.Label.AddThemeColorOverride("font_color", owned < input.Count ? new Color("a55749") : TownTheme.Ink);
            }
            feedback.Text = RecipeBlockReason();
            feedback.Visible = feedback.Text.Length > 0;
            make.Disabled = feedback.Visible;
        }

        Refresh();
        _refreshManagementPage += Refresh;
    }
}
