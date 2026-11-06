using Godot;
using System;
using System.Linq;
using System.Text.Json.Nodes;

public partial class Main
{
    void VerifyOptimizationLists(Action<bool, string> check)
    {
        VerifyOrderList(check);
        VerifyPetRoster(check);
    }

    void VerifyOrderList(Action<bool, string> check)
    {
        const string item = "清水";
        const string lateId = "verify_order_list_late";
        const string urgentId = "verify_order_list_urgent";
        const string archivedId = "verify_order_list_archived";
        var orders = Game.Rows("orders");
        var archive = Game.Rows("order_archive");
        var inventory = Game.Rows("inventory");
        var originalBatches = inventory.ToHashSet();
        var previousFilter = _orderFilter;
        var previousSort = _orderListSort;
        int previousHistoryPage = _orderHistoryPage;
        JsonObject Order(string id, int quantity, double expires, string status) => new()
        {
            ["order_id"] = id, ["npc"] = "邻居", ["item"] = item, ["kind"] = "item", ["quantity"] = quantity,
            ["created_ts"] = Game.Now, ["expiry_ts"] = expires, ["status"] = status, ["reward"] = 15,
        };

        JsonObject? late = null, urgent = null, archived = null;
        try
        {
            if (Game.Stock(item) == 0) Game.Add(item, 1);
            late = Order(lateId, 1, Game.Now + NativeGame.Hour, "进行中");
            urgent = Order(urgentId, Game.Stock(item) + 1, Game.Now + 60, "进行中");
            archived = Order(archivedId, 1, Game.Now - 60, "完成");
            orders.Add(late);
            orders.Add(urgent);
            archive.Add(archived);
            _orderFilter = OrderFilter.Active;
            _orderListSort = OrderListSort.Deadline;
            _orderHistoryPage = 0;
            ShowManagement("订单");
            var body = _managementBody!;
            Node? Card(string id) => body.FindChild($"OrderCard_{id}", true, false);
            void Press(Node parent, string name)
            {
                var button = parent.FindChild(name, true, false) as Button;
                check(button is { Visible: true, Disabled: false }, $"订单筛选缺少可用按钮 {name}");
                button!.EmitSignal(BaseButton.SignalName.Pressed);
            }

            check(Card(urgentId) is Node early && Card(lateId) is Node later && early.GetIndex() < later.GetIndex(),
                "订单默认列表没有按到期时间排序");
            var sort = (OptionButton)body.FindChild("OrderListSort", true, false)!;
            sort.Select((int)OrderListSort.Ready);
            sort.EmitSignal(OptionButton.SignalName.ItemSelected, (int)OrderListSort.Ready);
            check(Card(lateId)!.GetIndex() < Card(urgentId)!.GetIndex(), "可交付优先排序仍把缺货订单排在前面");
            var readiness = (ProgressBar)Card(urgentId)!.FindChild("OrderReadiness", true, false)!;
            check(readiness.Value == Game.Stock(item) && readiness.MaxValue == NativeGame.I(urgent, "quantity"),
                "订单就绪进度未显示实际可用于本订单的物资");
            Press(body, "OrderFilterReady");
            check(Card(lateId) != null && Card(urgentId) == null, "可交付筛选没有区分缺货订单");
            Press(body, "OrderFilterClosed");
            check(Card(archivedId) != null && Card(urgentId) == null, "已结束筛选没有包含订单归档");
            Press(body, "OrderFilterMissing");
            check(Card(urgentId) != null && Card(lateId) == null, "缺货筛选显示了已经备齐的订单");
            Press(Card(urgentId)!, "OrderBuySupply");
            check(_tab == "商店", "缺货订单没有打开对应的补货页");
            Game.Add(item, 1);
            Press(body, "OrderSupplyReturn");
            check(_tab == "订单" && _orderFilter == OrderFilter.Missing && _managementEntryTarget == urgentId
                && _managementFocusTarget == Card(urgentId) && Card(urgentId)?.GetIndex() == 0
                && body.FindChild("OrderFocusNotice", true, false) is Label { Visible: true },
                "补货后没有保留筛选并置顶已经备齐的原订单");
        }
        finally
        {
            if (late != null) orders.Remove(late);
            if (urgent != null) orders.Remove(urgent);
            if (archived != null) archive.Remove(archived);
            foreach (var batch in inventory.Where(batch => !originalBatches.Contains(batch)).ToArray())
                inventory.Remove(batch);
            _orderFilter = previousFilter;
            _orderListSort = previousSort;
            _orderHistoryPage = previousHistoryPage;
            _management?.Hide();
        }
    }

    void VerifyPetRoster(Action<bool, string> check)
    {
        var pets = Data.Pets.OfType<JsonNode>().Where(Game.Active).Take(2).ToArray();
        var patient = pets[0];
        var companion = pets[1];
        string patientId = NativeGame.S(patient, "pet_id"), companionId = NativeGame.S(companion, "pet_id");
        const string patientName = "名单验证伤病伙伴";
        const string companionName = "名单验证寄养伙伴";
        var patientFields = new[] { "name", "illness_type", "status", "injury" }
            .Select(key => (Key: key, Value: patient[key]?.DeepClone(), Exists: patient.AsObject().ContainsKey(key))).ToArray();
        var originalCompanionName = companion["name"]?.DeepClone();
        var originalFostered = companion["fostered"]?.DeepClone();
        bool hadFostered = companion.AsObject().ContainsKey("fostered");
        var previousQuery = _petRosterQuery;
        var previousFilter = _petRosterFilter;
        var previousSort = _petRosterSort;
        string previousSelectedPet = _selectedPet;
        bool previousExpanded = _petManagementExpanded;
        try
        {
            patient["name"] = patientName;
            patient["illness_type"] = "";
            patient["status"] = "正常";
            patient["injury"] = 0;
            companion["name"] = companionName;
            companion["fostered"] = true;
            _petRosterFilter = PetRosterFilter.All;
            _petRosterSort = PetRosterSort.CarePriority;
            _petRosterQuery = "";
            OpenPetManagement(companionId);
            var body = _managementBody!;
            T ControlOnPage<T>(string name) where T : Control => (T)body.FindChild(name, true, false)!;
            void Search(string query)
            {
                var search = ControlOnPage<LineEdit>("PetRosterSearch");
                search.Text = query;
                search.EmitSignal(LineEdit.SignalName.TextChanged, query);
            }
            void Filter(PetRosterFilter selection)
            {
                var filter = ControlOnPage<OptionButton>("PetRosterFilter");
                filter.Select((int)selection);
                filter.EmitSignal(OptionButton.SignalName.ItemSelected, (int)selection);
            }
            string[] Results()
            {
                var picker = ControlOnPage<OptionButton>("PetRosterPicker");
                return Enumerable.Range(0, picker.ItemCount).Select(index => picker.GetItemMetadata(index).AsString()).ToArray();
            }

            string species = NativeGame.S(patient, "species");
            Search(species);
            check(Results().Contains(patientId), "伙伴名册无法用物种找到名字不同的伙伴");
            Search(patientName);
            check(Results().SequenceEqual(new[] { patientId }), "伙伴名册没有按名字筛选出准确伙伴");
            Filter(PetRosterFilter.NeedsTreatment);
            check(Results().Length == 0, "伤病筛选显示了当前健康的伙伴");
            patient["injury"] = 8;
            RefreshManagementPage(1);
            check(Results().SequenceEqual(new[] { patientId }), "伤病状态变化后名册没有原地更新");
            var condition = ControlOnPage<Label>("PetRosterCondition0");
            check(condition.IsVisibleInTree() && condition.Text.Contains("优先诊疗") && condition.Text.Contains("健康"),
                "名册未在列表中显示照护原因和健康读数");
            ControlOnPage<OptionButton>("PetRosterPicker").EmitSignal(OptionButton.SignalName.ItemSelected, 0);
            check(_selectedPet == patientId, "选择名册结果没有打开对应伙伴");
            Search(companionName);
            Filter(PetRosterFilter.Fostered);
            check(Results().SequenceEqual(new[] { companionId }), "寄养筛选没有定位对应伙伴");
            OpenPetManagement(patientId, "clinic");
            check(_selectedPet == patientId && _managementFocusTarget?.Name == "PetClinic"
                && _petRosterFilter == PetRosterFilter.Fostered && ControlOnPage<LineEdit>("PetRosterSearch").Text == companionName
                && Results().SequenceEqual(new[] { companionId }) && ControlOnPage<Label>("PetRosterPinned").Visible,
                "诊疗跳转丢失了准确伙伴、诊疗焦点或原名册筛选");
        }
        finally
        {
            foreach (var (key, value, exists) in patientFields)
                if (exists) patient[key] = value;
                else patient.AsObject().Remove(key);
            companion["name"] = originalCompanionName;
            if (hadFostered) companion["fostered"] = originalFostered;
            else companion.AsObject().Remove("fostered");
            _petRosterQuery = previousQuery;
            _petRosterFilter = previousFilter;
            _petRosterSort = previousSort;
            _selectedPet = previousSelectedPet;
            _petManagementExpanded = previousExpanded;
            _management?.Hide();
            UpdateJournalBadge();
        }
    }
}
