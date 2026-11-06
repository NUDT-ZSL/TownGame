using Godot;
using System;
using System.Linq;
using System.Text.Json.Nodes;

public partial class Main
{
    const int WarehousePageSize = 12;
    const double WarehouseSoon = 12 * NativeGame.Hour;
    int _warehouseFilter, _warehousePage, _warehouseSort, _warehouseOverviewPage;
    string _warehouseQuery = "", _warehouseFocusItem = "";
    bool _warehouseOverviewOpen;

    readonly record struct WarehouseBatch(string Id, string Item, int Quantity, double Created, double Expires, int Reserved)
    {
        internal bool Fresh(double now) => Expires <= 0 || Expires > now;
        internal bool DueSoon(double now) => Expires > now && Expires - now <= WarehouseSoon;
    }

    Action MarketWarehouse(VBoxContainer content)
    {
        var overview = MarketCard(content, color: "f2ebdb");
        MarketLabel(overview, "仓库 · 物资批次", 19, TownTheme.Accent("商店").Darkened(.15f));
        var summary = MarketLabel(overview, "", wrap: true);
        summary.Name = "WarehouseSummary";
        var capacityBar = new ProgressBar { ShowPercentage = false, Step = 0, CustomMinimumSize = new(0, 8) };
        capacityBar.AddThemeStyleboxOverride("background", TownTheme.Box("e5dcc8", 4, 0));
        capacityBar.AddThemeStyleboxOverride("fill", TownTheme.Box("93aa80", 4, 0));
        overview.AddChild(capacityBar);
        MarketLabel(overview, "默认先消耗最早到期的批次，可为指定物资设置临期规则。", 14, TownTheme.Muted, wrap: true);
        Action<string>? showBatches = null;
        var refreshOverview = AddWarehouseItemOverview(overview, item => showBatches?.Invoke(item));
        var ruleLink = new Button { Name = "WarehouseRuleJump", Text = "查看临期规则与保底线", SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin };
        TownTheme.AccentButton(ruleLink, "商店", 6);
        overview.AddChild(ruleLink);

        var tools = new HFlowContainer();
        tools.AddThemeConstantOverride("h_separation", 8);
        tools.AddThemeConstantOverride("v_separation", 6);
        content.AddChild(tools);
        var search = new LineEdit { Name = "WarehouseQuery", PlaceholderText = "按物资名找批次", Text = _warehouseQuery,
            ClearButtonEnabled = true, CustomMinimumSize = new(150, 34), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        tools.AddChild(search);
        var filter = MarketOptions(tools, new[] { "全部批次", "12 游戏小时内到期", "有保质期", "有预留" }, _warehouseFilter);
        filter.Name = "WarehouseFilter";
        var sort = MarketOptions(tools, new[] { "临期优先", "数量最多", "最近入库" }, _warehouseSort);
        sort.Name = "WarehouseSort";
        var reset = new Button { Name = "WarehouseReset", Text = "清除筛选" };
        TownTheme.AccentButton(reset, "商店", 6);
        tools.AddChild(reset);
        var navigation = new HFlowContainer();
        navigation.AddThemeConstantOverride("h_separation", 8);
        content.AddChild(navigation);
        var previous = new Button { Text = "上一页", Name = "WarehousePrevious" };
        var next = new Button { Text = "下一页", Name = "WarehouseNext" };
        TownTheme.AccentButton(previous, "商店", 6);
        TownTheme.AccentButton(next, "商店", 6);
        navigation.AddChild(previous);
        var position = MarketLabel(navigation, "", color: TownTheme.Muted);
        position.Name = "WarehouseResultCount";
        position.VerticalAlignment = VerticalAlignment.Center;
        navigation.AddChild(next);
        var empty = MarketLabel(content, "当前筛选下没有物资批次。", color: TownTheme.Muted, wrap: true);
        var rows = Enumerable.Range(0, WarehousePageSize).Select(_ => AddWarehouseRow()).ToArray();
        int rulesStart = content.GetChildCount();
        var refreshPolicy = ExpiryPolicyControls(content);
        var refreshWatch = SupplyWatchControls(content);
        var rules = content.GetChild<Control>(rulesStart);
        ruleLink.Pressed += () => _managementScroll?.EnsureControlVisible(rules);
        previous.Pressed += () => { _warehousePage--; Refresh(); };
        next.Pressed += () => { _warehousePage++; Refresh(); };
        filter.ItemSelected += value => { _warehouseFilter = (int)value; _warehousePage = 0; Refresh(); };
        sort.ItemSelected += value => { _warehouseSort = (int)value; _warehousePage = 0; Refresh(); };
        search.TextChanged += value =>
        {
            _warehouseQuery = value;
            _warehouseFocusItem = "";
            _warehousePage = _warehouseOverviewPage = 0;
            Refresh();
        };
        reset.Pressed += () =>
        {
            search.Text = _warehouseQuery = _warehouseFocusItem = "";
            _warehouseFilter = _warehouseSort = _warehousePage = _warehouseOverviewPage = 0;
            filter.Select(0);
            sort.Select(0);
            Refresh();
        };
        showBatches = item =>
        {
            search.Text = item;
            _warehouseQuery = _warehouseFocusItem = item;
            _warehouseFilter = _warehousePage = _warehouseOverviewPage = 0;
            filter.Select(0);
            Refresh();
            Callable.From(() => { if (GodotObject.IsInstanceValid(tools) && tools.IsVisibleInTree()) _managementScroll?.EnsureControlVisible(tools); }).CallDeferred();
        };
        Refresh();
        return Refresh;

        void Refresh()
        {
            refreshPolicy();
            refreshWatch();
            double now = Game.Now;
            var batches = Game.Rows("inventory").OfType<JsonNode>().Select((node, index) => new WarehouseBatch(
                NativeGame.S(node, "batch_id", $"legacy_{index}"), NativeGame.S(node, "item"),
                NativeGame.I(node, "quantity"), NativeGame.D(node, "created_ts"), NativeGame.D(node, "expiry_ts"), Game.ReservedInventoryBatch(node))).ToArray();
            string query = _warehouseQuery.Trim();
            var matching = batches.Where(batch => _warehouseFocusItem.Length > 0 ? batch.Item == _warehouseFocusItem :
                batch.Item.Contains(query, StringComparison.OrdinalIgnoreCase)).ToArray();
            refreshOverview(matching, now);
            var scoped = _warehouseFilter switch
            {
                1 => matching.Where(batch => batch.DueSoon(now)),
                2 => matching.Where(batch => batch.Expires > 0),
                3 => matching.Where(batch => batch.Reserved > 0),
                _ => matching.AsEnumerable(),
            };
            var sorted = _warehouseSort switch
            {
                1 => scoped.OrderByDescending(batch => batch.Quantity),
                2 => scoped.OrderByDescending(batch => batch.Created),
                _ => scoped.OrderBy(batch => batch.Expires > 0 ? batch.Expires : double.PositiveInfinity),
            };
            var filtered = sorted.ToArray();
            int stored = batches.Sum(batch => batch.Quantity), capacity = Game.Storage;
            int due = batches.Where(batch => batch.DueSoon(now)).Sum(batch => batch.Quantity);
            int reserved = batches.Sum(batch => batch.Reserved);
            summary.Text = $"仓库 {stored} / {capacity} 份 · {batches.Length} 批 · 12 游戏小时内到期 {due} 份"+
                (reserved > 0 ? $" · 已预留 {reserved} 份" : "");
            capacityBar.MaxValue = Math.Max(1, capacity);
            capacityBar.Value = stored;
            int pages = Math.Max(1, (filtered.Length + WarehousePageSize - 1) / WarehousePageSize);
            _warehousePage = Math.Clamp(_warehousePage, 0, pages - 1);
            position.Text = $"{filtered.Length} / {batches.Length} 批 · 第 {_warehousePage + 1} / {pages} 页";
            previous.Disabled = _warehousePage == 0;
            next.Disabled = _warehousePage >= pages - 1;
            reset.Disabled = query.Length == 0 && _warehouseFilter == 0 && _warehouseSort == 0;
            empty.Visible = filtered.Length == 0;
            empty.Text = batches.Length == 0 ? "仓库目前没有物资批次。" : "未找到匹配批次。可修改关键词或清除筛选。";
            var visible = filtered.Skip(_warehousePage * WarehousePageSize).Take(WarehousePageSize).ToArray();
            for (int slot = 0; slot < rows.Length; slot++)
            {
                var desired = slot < visible.Length ? $"WarehouseBatch_{visible[slot].Id}" : $"WarehouseSlot_{slot}";
                if (rows[slot].Panel.Name != desired) rows[slot].Panel.Name = $"WarehouseSlot_{slot}";
            }
            for (int slot = 0; slot < rows.Length; slot++)
                rows[slot].Bind(slot < visible.Length ? visible[slot] : null);
        }

        (PanelContainer Panel, Action<WarehouseBatch?> Bind) AddWarehouseRow()
        {
            var card = MarketCard(content);
            var panel = card.GetParent<PanelContainer>();
            var header = new HFlowContainer();
            header.AddThemeConstantOverride("h_separation", 10);
            card.AddChild(header);
            var title = MarketLabel(header, "", 16);
            var deadline = MarketLabel(header, "", 14, TownTheme.Muted);
            deadline.Name = "WarehouseDeadline";
            var received = MarketLabel(card, "", 14, TownTheme.Muted, wrap: true);
            var actions = new HFlowContainer();
            actions.AddThemeConstantOverride("h_separation", 6);
            card.AddChild(actions);
            WarehouseBatch? current = null;
            var sell = MarketButton(actions, "前往出售", () =>
            {
                if (current is not WarehouseBatch batch || Game.Stock(batch.Item) <= 0) return;
                _marketSearch = batch.Item;
                _marketFilter = 2;
                _marketSellQuantity = Math.Clamp(Math.Min(batch.Quantity - batch.Reserved, Game.Stock(batch.Item)), 1, 99);
                _marketSection = 1;
                ShowManagement("商店", $"supply:{batch.Item}");
            }, expand: false);
            sell.Name = "WarehouseSell";
            sell.TooltipText = "打开该物资的出售入口。批次会按已设置的临期规则扣除。";
            var recipeActions = Game.Rules["RECIPES"]!.AsObject().Select(pair =>
            {
                string output = pair.Key;
                var button = MarketButton(actions, $"配方 · {output}", () =>
                {
                    _marketSection = 2;
                    ShowManagement("商店", $"recipe:{output}");
                }, expand: false);
                button.Name = $"WarehouseRecipe_{output}";
                return (Inputs: pair.Value!["inputs"]!.AsObject(), Button: button);
            }).ToArray();

            return (panel, batch =>
            {
                current = batch;
                panel.Visible = batch.HasValue;
                if (batch is not WarehouseBatch visible) return;
                panel.Name = $"WarehouseBatch_{visible.Id}";
                title.Text = $"{visible.Item} · {visible.Quantity} 份"+
                    (visible.Reserved > 0 ? $" · 可用 {visible.Quantity - visible.Reserved} · 已预留 {visible.Reserved}" : "");
                deadline.Text = visible.Expires > 0 ? DeadlineText(visible.Expires - Game.Now) : "无保质期";
                deadline.AddThemeColorOverride("font_color", visible.DueSoon(Game.Now) ? new Color("8a4938") : TownTheme.Muted);
                received.Text = visible.Created > 0 ? $"{TownProjectClock(visible.Created)} 入库" : "入库时间未记录";
                bool fresh = visible.Expires <= 0 || visible.Expires > Game.Now;
                var item = Game.Rules["ITEMS"]?[visible.Item];
                sell.Visible = fresh && visible.Reserved < visible.Quantity && item != null && NativeGame.I(item, "sell") > 0;
                foreach (var recipe in recipeActions)
                    recipe.Button.Visible = fresh && recipe.Inputs.ContainsKey(visible.Item);
                actions.Visible = sell.Visible || recipeActions.Any(recipe => recipe.Button.Visible);
            });
        }
    }

    Action<WarehouseBatch[], double> AddWarehouseItemOverview(Container parent, Action<string> openBatches)
    {
        const int pageSize = 6;
        var toggle = new Button { Name = "WarehouseItemOverview", Text = "按物资查看库存汇总", ToggleMode = true };
        TownTheme.AccentButton(toggle, "商店", 6);
        toggle.SetPressedNoSignal(_warehouseOverviewOpen);
        parent.AddChild(toggle);
        var details = new VBoxContainer { Visible = _warehouseOverviewOpen };
        parent.AddChild(details);
        MarketLabel(details, "按名称汇总全部匹配批次，点击可展开该物资的批次。", 14, TownTheme.Muted, wrap: true);
        var pager = new HFlowContainer();
        details.AddChild(pager);
        var previous = new Button { Text = "上组" };
        var next = new Button { Text = "下组" };
        TownTheme.AccentButton(previous, "商店", 5);
        TownTheme.AccentButton(next, "商店", 5);
        pager.AddChild(previous);
        var page = MarketLabel(pager, "", 14, TownTheme.Muted);
        page.VerticalAlignment = VerticalAlignment.Center;
        pager.AddChild(next);
        var rows = Enumerable.Range(0, pageSize).Select(_ =>
        {
            var button = new Button { Alignment = HorizontalAlignment.Left, ClipText = true };
            TownTheme.AccentButton(button, "商店", 6);
            details.AddChild(button);
            string? item = null;
            button.Pressed += () => { if (item != null) openBatches(item); };
            return (Button: button, SetItem: (Action<string?>)(value => item = value));
        }).ToArray();
        WarehouseBatch[] batches = Array.Empty<WarehouseBatch>();
        double now = 0;
        toggle.Toggled += expanded => { _warehouseOverviewOpen = details.Visible = expanded; if (expanded) Refresh(); };
        previous.Pressed += () => { _warehouseOverviewPage--; Refresh(); };
        next.Pressed += () => { _warehouseOverviewPage++; Refresh(); };
        return (current, time) => { batches = current; now = time; if (details.Visible) Refresh(); };

        void Refresh()
        {
            var items = batches.GroupBy(batch => batch.Item).OrderBy(group => group.Key, StringComparer.Ordinal).ToArray();
            int pages = Math.Max(1, (items.Length + pageSize - 1) / pageSize);
            _warehouseOverviewPage = Math.Clamp(_warehouseOverviewPage, 0, pages - 1);
            page.Text = items.Length == 0 ? "暂无匹配物资" : $"{items.Length} 类物资 · 第 {_warehouseOverviewPage + 1} / {pages} 组";
            previous.Disabled = _warehouseOverviewPage == 0;
            next.Disabled = _warehouseOverviewPage + 1 >= pages;
            for (int index = 0; index < rows.Length; index++)
            {
                int at = _warehouseOverviewPage * pageSize + index;
                if (at >= items.Length || rows[index].Button.Name != $"WarehouseItem_{items[at].Key}")
                    rows[index].Button.Name = $"WarehouseItemSlot_{index}";
            }
            for (int index = 0; index < rows.Length; index++)
            {
                var (button, setItem) = rows[index];
                int at = _warehouseOverviewPage * pageSize + index;
                button.Visible = at < items.Length;
                if (!button.Visible) { setItem(null); continue; }
                var group = items[at];
                setItem(group.Key);
                button.Name = $"WarehouseItem_{group.Key}";
                int available = group.Where(batch => batch.Fresh(now)).Sum(batch => batch.Quantity - batch.Reserved);
                int reserved = group.Sum(batch => batch.Reserved);
                int expiring = group.Where(batch => batch.DueSoon(now)).Sum(batch => batch.Quantity);
                button.Text = $"{group.Key} · 可用 {available} · 预留 {reserved} · 临期 {expiring} · {group.Count()} 批";
                button.TooltipText = button.Text;
            }
        }
    }
}
