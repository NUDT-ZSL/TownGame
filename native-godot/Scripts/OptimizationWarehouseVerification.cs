using Godot;
using System;
using System.Linq;
using System.Text.Json.Nodes;

public partial class Main
{
    void VerifyOptimizationWarehouse(Action<bool,string> check)
    {
        const string product = "羊奶", recipe = "特制零食";
        var original = Data.Snapshot;
        string originalTab = _tab, originalEntry = _managementEntryTarget, originalSelection = _selection;
        bool managementWasOpen = _management?.Visible == true;
        var marketState = (_marketSection, _marketFilter, _marketSearch, _marketSellQuantity, _warehouseFilter, _warehousePage);
        var supplyRoutes = (_orderSupplyRoute, _careSupplyRoute);
        try
        {
            Data.Root["snapshot"] = original.DeepClone();
            Data.Reindex();
            double now = Game.Now;
            JsonObject Batch(string id, string item, int quantity, double expiry) => new()
            {
                ["batch_id"] = id, ["item"] = item, ["quantity"] = quantity,
                ["created_ts"] = now - NativeGame.Hour, ["expiry_ts"] = expiry,
            };
            var late = Batch("warehouse_late", product, 3, now + 20 * NativeGame.Hour);
            var shelf = Batch("warehouse_shelf", "清水", 4, 0);
            var soon = Batch("warehouse_soon", product, 2, now + .5 * NativeGame.Hour);
            var middle = Batch("warehouse_middle", product, 1, now + 3 * NativeGame.Hour);
            Game.State["inventory"] = new JsonArray(late, shelf, soon, middle);
            Game.Meta["inflation_until"] = 0;
            Game.Meta["discount_until"] = 0;
            (_orderSupplyRoute, _careSupplyRoute) = (null, null);
            (_marketSection, _marketFilter, _marketSearch, _warehouseFilter, _warehousePage) = (1, 0, "", 0, 0);
            ShowManagement("商店");

            T ControlOnPage<T>(string name) where T : Control
            {
                var control = _managementBody!.FindChild(name, true, false) as T;
                check(control != null, $"找不到仓库验证控件 {name}");
                return control!;
            }
            void Press(Button button)
            {
                check(button.IsVisibleInTree() && !button.Disabled, $"仓库验证控件不可用 {button.Name}");
                button.EmitSignal(BaseButton.SignalName.Pressed);
            }
            PanelContainer BatchPanel(JsonNode batch) =>
                ControlOnPage<PanelContainer>($"WarehouseBatch_{NativeGame.S(batch, "batch_id")}");
            void WarehouseFilter(int index)
            {
                var filter = ControlOnPage<OptionButton>("WarehouseFilter");
                filter.Select(index);
                filter.EmitSignal(OptionButton.SignalName.ItemSelected, index);
            }
            void CheckVisibleBatches(params string[] ids)
            {
                var visible = _managementBody!.FindChildren("WarehouseBatch_*", "PanelContainer", true, false)
                    .OfType<PanelContainer>().Where(panel => panel.IsVisibleInTree()).Select(panel => panel.Name.ToString());
                check(visible.SequenceEqual(ids.Select(id => $"WarehouseBatch_warehouse_{id}")), "仓库批次排序或到期筛选不正确");
            }

            var water = ControlOnPage<Label>("MarketStock_清水");
            var buy = ControlOnPage<Button>("MarketBuy_高级饲料");
            string regularPrice = buy.Text;
            Game.Take("清水", 1);
            Game.Meta["inflation_until"] = now + NativeGame.Hour;
            RefreshManagementPage(1);
            check(ReferenceEquals(water, ControlOnPage<Label>("MarketStock_清水")) && water.Text == "库存 3",
                "营业中库存未原地更新");
            check(ReferenceEquals(buy, ControlOnPage<Button>("MarketBuy_高级饲料")) && buy.Text != regularPrice &&
                buy.Text.Contains($"{(long)Game.BuyPrice("高级饲料") * _marketBuyQuantity} 金币", StringComparison.Ordinal),
                "商店临时涨价未原地更新报价");

            Press(ControlOnPage<Button>("MarketTabWarehouse"));
            CheckVisibleBatches("soon", "middle", "late", "shelf");
            check(((Label)BatchPanel(soon).FindChild("WarehouseDeadline", true, false)!).Text.Contains("30 游戏分钟") &&
                ((Label)BatchPanel(shelf).FindChild("WarehouseDeadline", true, false)!).Text == "无保质期",
                "仓库未区分实际到期时间和无保质期物资");
            WarehouseFilter(1);
            CheckVisibleBatches("soon", "middle");
            check(ControlOnPage<Label>("WarehouseSummary").Text.Contains("12 游戏小时内到期 3 份"),
                "仓库临期数量统计不准确");

            var recipeLink = (Button)BatchPanel(soon).FindChild($"WarehouseRecipe_{recipe}", true, false)!;
            Press(recipeLink);
            check(_managementFocusTarget == ControlOnPage<PanelContainer>($"MarketRecipe_{recipe}"),
                "仓库加工入口未聚焦到使用该批物资的配方");
            Press(ControlOnPage<Button>("MarketTabWarehouse"));
            CheckVisibleBatches("soon", "middle");
            WarehouseFilter(0);
            var saleLink = (Button)BatchPanel(late).FindChild("WarehouseSell", true, false)!;
            Press(saleLink);
            check(_managementFocusTarget == ControlOnPage<PanelContainer>($"MarketSupply_{product}") && _marketSellQuantity == 3,
                "仓库出售入口未聚焦到对应物资或预填数量");
            Press(ControlOnPage<Button>($"MarketSell_{product}"));
            var remainingMilk = Game.Rows("inventory").OfType<JsonNode>()
                .Where(batch => NativeGame.S(batch, "item") == product).ToArray();
            check(remainingMilk.Length == 1 && NativeGame.S(remainingMilk[0], "batch_id") == "warehouse_late" &&
                NativeGame.I(remainingMilk[0], "quantity") == 3, "出售未沿用同类物资最早到期优先规则");
        }
        finally
        {
            Data.Root["snapshot"] = original;
            Data.Reindex();
            (_marketSection, _marketFilter, _marketSearch, _marketSellQuantity, _warehouseFilter, _warehousePage) = marketState;
            (_orderSupplyRoute, _careSupplyRoute) = supplyRoutes;
            _selection = originalSelection;
            if (managementWasOpen) ShowManagement(originalTab, originalEntry);
            else
            {
                _management?.Hide();
                _tab = originalTab;
                _managementEntryTarget = originalEntry;
            }
        }
    }
}
