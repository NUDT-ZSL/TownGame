using Godot;
using System;
using System.Linq;
using System.Text.Json.Nodes;

public partial class Main
{
    void VerifyWorkshopExperience(Action<bool,string> check)
    {
        const string recipe = "高级饲料", otherRecipe = "特制零食";
        var original = Data.Snapshot;
        var page = (_tab, _managementEntryTarget, _selection, Open: _management?.Visible == true);
        var market = (_marketSection, _orderSupplyRoute, _careSupplyRoute);
        var warehouse = (_warehouseQuery, _warehouseFocusItem, _warehouseFilter, _warehouseSort,
            _warehousePage, _warehouseOverviewOpen, _warehouseOverviewPage);
        var workshopUi = (_workshopRecipeQuery, _workshopRecipeFilter, _workshopQueueFilter);
        bool suppressed = _firstDayGuideSuppressWrites;
        try
        {
            Data.Root["snapshot"] = original.DeepClone();
            Data.Reindex();
            _firstDayGuideSuppressWrites = true;
            _orderSupplyRoute = null;
            _careSupplyRoute = null;
            (_warehouseQuery, _warehouseFocusItem, _warehouseFilter, _warehouseSort,
                _warehousePage, _warehouseOverviewOpen, _warehouseOverviewPage) = ("", "", 0, 0, 0, false, 0);
            (_workshopRecipeQuery, _workshopRecipeFilter, _workshopQueueFilter) = ("", 0, 0);
            Game.Meta["native_workshop_queue"] = new JsonArray();
            Game.Meta["coins"] = 200;
            Game.State["inventory"] = new JsonArray();
            var workshop = Game.Rows("buildings").OfType<JsonObject>().FirstOrDefault(building => NativeGame.S(building,"kind") == "加工坊") ??
                Game.Rows("buildings").OfType<JsonObject>().First(building => NativeGame.S(building,"kind") == "");
            workshop["kind"] = "加工坊";
            workshop["level"] = 1;
            workshop["unlocked"] = true;
            Game.Add("谷物", 8);
            Game.Add("谷物", 2);
            Game.Add("清水", 5);
            var grain = Game.Rows("inventory").OfType<JsonObject>().Where(batch => NativeGame.S(batch,"item") == "谷物").ToArray();
            grain[1]["expiry_ts"] = Game.Now + NativeGame.Hour;
            Enqueue();

            _marketSection = 3;
            ShowManagement("商店");
            Find<Button>("WarehouseItemOverview").ButtonPressed = true;
            var stock = Find<Button>("WarehouseItem_谷物");
            check(stock.Text.Contains("可用 7 · 预留 3 · 临期 2 · 2 批"), "仓库物资汇总没有区分真实可用、预留和临期数量");
            stock.EmitSignal(BaseButton.SignalName.Pressed);
            check(Find<LineEdit>("WarehouseQuery").Text == "谷物" && VisibleBatchIds().Length == 2,
                "从物资汇总未聚焦到相应批次");
            Select("WarehouseSort", 1);
            check(VisibleBatchIds().First() == $"WarehouseBatch_{NativeGame.S(grain[0],"batch_id")}", "按批次数量排序无效");
            Query("WarehouseQuery", "清水");
            Select("WarehouseFilter", 3);
            check(VisibleBatchIds().Length == 1 && Find<Label>("WarehouseResultCount").Text.Contains("1 / 3 批"),
                "仓库未组合物资查询和预留筛选");
            Press("WarehouseReset");
            check(VisibleBatchIds().Length == 3 && Find<OptionButton>("WarehouseSort").Selected == 0, "清除筛选未恢复批次列表");

            Press("MarketTabRecipes");
            Select("WorkshopRecipeFilter", 1);
            check(Find<PanelContainer>($"MarketRecipe_{recipe}").Visible && !Find<PanelContainer>($"MarketRecipe_{otherRecipe}").Visible,
                "可立即制作筛选未使用真实等级和可用库存");
            Select("WorkshopRecipeFilter", 0);
            Query("WorkshopRecipeQuery", "鸟蛋");
            check(!Find<PanelContainer>($"MarketRecipe_{recipe}").Visible && Find<PanelContainer>($"MarketRecipe_{otherRecipe}").Visible,
                "原料搜索未找到对应配方");
            ShowManagement("商店", $"recipe:{recipe}");
            var recipeCard = Find<PanelContainer>($"MarketRecipe_{recipe}");
            check(recipeCard.Visible && _managementFocusTarget == recipeCard, "配方直达链接没有穿过搜索筛选");
            ((Button)recipeCard.FindChild("WorkshopBatchPreset3", true, false)!).EmitSignal(BaseButton.SignalName.Pressed);
            var material = (Label)recipeCard.FindChild("WorkshopMaterial_谷物", true, false)!;
            var materialBar = (ProgressBar)recipeCard.FindChild("WorkshopMaterialProgress_谷物", true, false)!;
            var enqueue = (Button)recipeCard.FindChild("WorkshopEnqueue", true, false)!;
            check(material.Text.Contains("需要 9 · 可用 7 · 已预留 3 · 缺 2") && materialBar.MaxValue == 9 && materialBar.Value == 7 && enqueue.Disabled,
                "批量原料预览未扣除已预留物资或限制排队");

            var first = Game.WorkshopOrders().Single();
            var activeCard = Find<PanelContainer>($"WorkshopQueue_{first.Id}");
            Enqueue();
            var second = Game.WorkshopOrders().Last();
            Game.Advance(35 * 60);
            _refreshManagementPage!.Invoke();
            var progress = (ProgressBar)activeCard.FindChild("WorkshopQueueProgress", true, false)!;
            check(ReferenceEquals(activeCard, Find<PanelContainer>($"WorkshopQueue_{first.Id}")) && progress.Visible &&
                progress.Value > 0 && progress.Value < progress.MaxValue &&
                !((Button)activeCard.FindChild("WorkshopQueueCancel", true, false)!).Visible,
                "加工开工后未原地更新进度，或仍能取消正在加工的批次");
            Select("WorkshopQueueFilter", 2);
            var waiting = Find<PanelContainer>($"WorkshopQueue_{second.Id}");
            check(!activeCard.Visible && waiting.Visible, "加工队列未按开工状态筛选");
            int coins = NativeGame.I(Game.Meta,"coins"), available = Game.Stock("谷物");
            ((Button)waiting.FindChild("WorkshopQueueCancel", true, false)!).EmitSignal(BaseButton.SignalName.Pressed);
            var confirmation = _management!.GetChildren().OfType<ConfirmationDialog>().Last(dialog => dialog.Visible);
            check(confirmation.DialogText.Contains($"退回 {second.Fee} 金币") && Game.WorkshopOrders().Length == 2,
                "取消加工未说明实际退款，或确认前改变队列");
            confirmation.EmitSignal(AcceptDialog.SignalName.Confirmed);
            check(Game.WorkshopOrders().Length == 1 && NativeGame.I(Game.Meta,"coins") == coins + second.Fee && Game.Stock("谷物") == available + 3,
                "确认取消加工后没有返还加工费或释放原料");
            GD.Print("WORKSHOP_EXPERIENCE_VERIFIED item_drilldown batch_sort recipe_readiness material_quote queue_progress refund");
        }
        finally
        {
            Data.Root["snapshot"] = original;
            Data.Reindex();
            _firstDayGuideSuppressWrites = suppressed;
            (_marketSection, _orderSupplyRoute, _careSupplyRoute) = market;
            (_warehouseQuery, _warehouseFocusItem, _warehouseFilter, _warehouseSort,
                _warehousePage, _warehouseOverviewOpen, _warehouseOverviewPage) = warehouse;
            (_workshopRecipeQuery, _workshopRecipeFilter, _workshopQueueFilter) = workshopUi;
            _selection = page._selection;
            if (page.Open) ShowManagement(page._tab, page._managementEntryTarget);
            else { _management?.Hide(); (_tab, _managementEntryTarget) = (page._tab, page._managementEntryTarget); }
        }

        T Find<T>(string name) where T : Control => (T)_managementBody!.FindChild(name, true, false)!;
        void Press(string name) => Find<Button>(name).EmitSignal(BaseButton.SignalName.Pressed);
        void Query(string name, string value) { var search = Find<LineEdit>(name); search.Text = value; search.EmitSignal(LineEdit.SignalName.TextChanged, value); }
        void Select(string name, int index) { var menu = Find<OptionButton>(name); menu.Select(index); menu.EmitSignal(OptionButton.SignalName.ItemSelected, index); }
        string[] VisibleBatchIds() => _managementBody!.FindChildren("WarehouseBatch_*", "PanelContainer", true, false)
            .OfType<PanelContainer>().Where(panel => panel.Visible).Select(panel => panel.Name.ToString()).ToArray();
        void Enqueue() => Game.Execute("workshop_queue", new JsonObject { ["operation"] = "enqueue", ["item"] = recipe });
    }
}
