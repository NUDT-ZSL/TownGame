using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;

public partial class Main
{
    int _marketSection, _marketSex, _marketFilter, _carePage;
    int _marketBuyQuantity = 5, _marketSellQuantity = 1;
    string _marketSearch = "";
    int _workshopRecipeFilter;
    string _workshopRecipeQuery = "";

    void ShopControls()
    {
        if (_orderSupplyRoute is { } supplyRoute)
        {
            OrderSupplyControls(supplyRoute);
            return;
        }
        if (_careSupplyRoute is { } careRoute)
        {
            CareSupplyControls(careRoute);
            return;
        }

        var sections = new HFlowContainer();
        sections.AddThemeConstantOverride("h_separation", 8);
        sections.AddThemeConstantOverride("v_separation", 6);
        _managementBody!.AddChild(sections);
        var content = new VBoxContainer();
        content.AddThemeConstantOverride("separation", 10);
        _managementBody.AddChild(content);
        var buttons = new List<Button>();
        Action? refreshSection = null;
        foreach (var (label, name) in new[]
        {
            ("挑选伙伴", "MarketTabCompanions"), ("饲料与物资", "MarketTabSupplies"),
            ("加工坊", "MarketTabRecipes"), ("仓库批次", "MarketTabWarehouse"),
        })
        {
            int section = buttons.Count;
            var button = MarketButton(sections, label, () =>
            {
                _marketSection = section;
                Render();
            });
            button.Name = name;
            button.ToggleMode = true;
            buttons.Add(button);
        }
        void Render()
        {
            for (int i = 0; i < buttons.Count; i++)
                buttons[i].SetPressedNoSignal(i == _marketSection);
            foreach (var child in content.GetChildren())
            {
                content.RemoveChild(child);
                child.QueueFree();
            }
            refreshSection = null;
            switch (_marketSection)
            {
                case 0: refreshSection = MarketCompanions(content); break;
                case 1: refreshSection = MarketSupplies(content); break;
                case 2: refreshSection = MarketRecipes(content); break;
                case 3: refreshSection = MarketWarehouse(content); break;
            }
        }
        Render();
        _refreshManagementPage += () => refreshSection?.Invoke();
    }

    Action MarketCompanions(VBoxContainer content)
    {
        var refreshIncubator=BuildIncubatorOverview(content);
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 10);
        content.AddChild(row);
        MarketLabel(row, "新伙伴的性别", color: TownTheme.Muted);
        var sex = MarketOptions(row, new[] { "雌", "雄" }, _marketSex);
        sex.ItemSelected += index => _marketSex = (int)index;
        row.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
        var adopt = MarketButton(row, "公益领养", () => Act("adopt", new JsonObject()), expand: false);
        adopt.TooltipText = "每游戏日可领养一次，需有适配的居所空位";
        MarketLabel(content, "选择物种与性别。公益领养每游戏日开放一次，领回前请留出合适的居所空位。", color: TownTheme.Muted, wrap: true);
        var refreshGallery = MarketSpeciesGallery(content);
        void Refresh()
        {
            string reason=Game.AdoptionReason();
            adopt.Disabled=reason.Length>0;
            adopt.TooltipText=reason.Length>0?reason:"每游戏日可领养一次，需有适配的居所空位";
            refreshGallery();
            refreshIncubator();
        }
        Refresh();
        return Refresh;
    }

    Action MarketSpeciesGallery(VBoxContainer content)
    {
        var grid = ManagementCardGrid(content);
        Action refresh=()=>{};
        foreach (var species in Game.Rules["SPECIES"]!.AsObject())
        {
            string name = species.Key;
            var spec = species.Value!;
            var card = MarketCard(grid, 164);
            card.AddChild(AnimalBadge(name, 144, 100));
            if (_managementEntryTarget == "companion-shop:" + name)
                _managementFocusTarget = card.GetParent<Control>();
            string home = NativeGame.S(spec, "building");
            MarketLabel(card, $"居所 · {home}", color: TownTheme.Muted).HorizontalAlignment = HorizontalAlignment.Center;
            double price = NativeGame.D(spec, "buy_price", NativeGame.D(Game.Rules, "PET_BUY_COST"));
            var buy = MarketButton(card, $"领回 · {(int)price} 金币", () => AdoptSpecies(egg: false));
            refresh+=()=>SetAvailability(buy,(int)price);
            if (spec["egg_available"] == null || NativeGame.B(spec, "egg_available"))
            {
                int eggPrice = (int)(price * NativeGame.D(Game.Rules, "EGG_PRICE_FACTOR", 1));
                var egg = MarketButton(card, $"购买宠物蛋 · {eggPrice} 金币", () => AdoptSpecies(egg: true));
                refresh+=()=>SetAvailability(egg,eggPrice,true);
            }

            void AdoptSpecies(bool egg)
            {
                Act("buy_pet", new JsonObject
                {
                    ["species"] = name,
                    ["sex"] = _marketSex == 0 ? "雌" : "雄",
                    ["egg"] = egg
                });
            }
            void SetAvailability(Button button,int cost,bool egg=false)
            {
                string reason=egg?Game.IncubationReason(home,cost):Game.CompanionPurchaseReason(home,cost);
                button.Disabled=reason.Length>0;
                button.TooltipText=reason.Length>0?reason:egg?$"孵化后会入住{home}":$"领回后会入住{home}";
            }
        }
        return refresh;
    }

    Action MarketSupplies(VBoxContainer content)
    {
        var summary = MarketLabel(content, "", color: TownTheme.Muted, wrap: true);
        var tools = new HFlowContainer();
        tools.AddThemeConstantOverride("h_separation", 8);
        content.AddChild(tools);
        var filter = MarketOptions(tools, new[] { "全部物资", "可购买", "有库存" }, _marketFilter);
        var search = new LineEdit
        {
            Text = _marketSearch, PlaceholderText = "搜索物资",
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, ClearButtonEnabled = true
        };
        tools.AddChild(search);
        var quantities = new HFlowContainer();
        quantities.AddThemeConstantOverride("h_separation", 8);
        quantities.AddThemeConstantOverride("v_separation", 5);
        content.AddChild(quantities);
        MarketLabel(quantities, "买入份数");
        var buyQuantity = MarketQuantity(quantities, _marketBuyQuantity);
        buyQuantity.Name = "MarketBuyQuantity";
        foreach (int preset in new[] { 1, 5, 10 })
            MarketButton(quantities, $"{preset} 份", () => buyQuantity.Value = preset, expand: false).Name = $"MarketBuyPreset{preset}";
        MarketLabel(quantities, "卖出份数");
        var sellQuantity = MarketQuantity(quantities, _marketSellQuantity);

        var empty = MarketLabel(content, "没有找到符合条件的物资。", color: TownTheme.Muted);
        var rows = new List<(string Name, Func<int,int,int,int,int,bool> Refresh)>();
        foreach (var pair in Game.Rules["ITEMS"]!.AsObject())
        {
            string name = pair.Key;
            bool targeted = _managementEntryTarget == $"supply:{name}";
            int shownPrice = Game.BuyPrice(name);
            int sellPrice = NativeGame.I(pair.Value!, "sell");
            var panel = new PanelContainer { Name = $"MarketSupply_{name}" };
            panel.AddThemeStyleboxOverride("panel", TownTheme.Box(targeted ? "f4e7cd" : "fffdf7", 9, 8));
            content.AddChild(panel);
            if (targeted) _managementFocusTarget = panel;
            var details = new VBoxContainer();
            panel.AddChild(details);
            var row = new HFlowContainer();
            row.AddThemeConstantOverride("h_separation", 8);
            row.AddThemeConstantOverride("v_separation", 4);
            details.AddChild(row);
            var title = MarketLabel(row, name, size: 16);
            title.CustomMinimumSize = new Vector2(100, 0);
            title.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            var stockLabel = MarketLabel(row, "", color: TownTheme.Muted);
            stockLabel.Name = $"MarketStock_{name}";
            stockLabel.CustomMinimumSize = new Vector2(74, 0);
            var buy = MarketButton(row, "", () =>
            {
                if (Game.BuyPrice(name) != shownPrice)
                {
                    RefreshRows();
                    SetManagementFeedback("物资价格已变动，请核对新价格后再购买。", failed: true);
                    return;
                }
                Trade("buy", name, _marketBuyQuantity);
            }, expand: false);
            var sell = MarketButton(row, "", () => Trade("sell", name, _marketSellQuantity), expand: false);
            buy.Name = $"MarketBuy_{name}";
            sell.Name = $"MarketSell_{name}";
            buy.CustomMinimumSize = sell.CustomMinimumSize = new Vector2(120, 38);
            var fill = MarketButton(row, "填入上限", () =>
            {
                int freeSpace = Game.Storage - Game.Rows("inventory").OfType<JsonNode>().Sum(batch => NativeGame.I(batch, "quantity"));
                int limit = MarketPurchaseLimit(Game.BuyPrice(name), freeSpace, NativeGame.I(Game.Meta, "coins"), (int)buyQuantity.MaxValue);
                if (limit > 0) buyQuantity.Value = limit;
                else RefreshRows();
            }, expand: false);
            fill.Name = $"MarketBuyLimit_{name}";
            var purchaseHint = MarketLabel(details, "", 14, TownTheme.Muted, wrap: true);
            purchaseHint.Name = $"MarketBuyHint_{name}";
            rows.Add((name, RefreshRow));

            bool RefreshRow(int stock, int available, int reserved, int freeSpace, int coins)
            {
                shownPrice = Game.BuyPrice(name);
                stockLabel.Text = reserved > 0 ? $"库存 {stock}\n可用 {available} · 已预留 {reserved}" : $"库存 {stock}";
                bool filterMatches = _marketFilter switch { 1 => shownPrice > 0, 2 => stock > 0, _ => true };
                panel.Visible = filterMatches && name.Contains(_marketSearch.Trim(), StringComparison.OrdinalIgnoreCase);
                long cost = (long)shownPrice * _marketBuyQuantity;
                int limit = MarketPurchaseLimit(shownPrice, freeSpace, coins, (int)buyQuantity.MaxValue);
                bool noSpace = freeSpace < _marketBuyQuantity;
                buy.Text = shownPrice <= 0 ? "不可购买" : $"买 {_marketBuyQuantity} 份 · {cost} 金币";
                buy.Disabled = _marketBuyQuantity > limit;
                string blocked = shownPrice <= 0 ? "商店不出售此物资" : noSpace ? "仓库空间不足" : cost > coins ? "金币不足" : "";
                buy.TooltipText = blocked.Length > 0 ? blocked : $"每份 {shownPrice} 金币";
                fill.Visible = shownPrice > 0;
                fill.Disabled = limit == 0 || limit == _marketBuyQuantity;
                fill.TooltipText = $"将买入数量改为 {limit} 份，等待确认购买。";
                purchaseHint.Text = shownPrice <= 0 ? blocked : $"每份 {shownPrice} 金币 · 本次最多可买 {limit} 份" +
                    (blocked.Length > 0 ? $" · {blocked}" : "");
                sell.Text = $"卖 {_marketSellQuantity} 份 · {(long)sellPrice * _marketSellQuantity} 金币";
                sell.Disabled = available < _marketSellQuantity;
                sell.TooltipText = sell.Disabled ? $"仅有 {available} 份可用库存" : $"每份 {sellPrice} 金币";
                return panel.Visible;
            }
        }
        filter.ItemSelected += index => { _marketFilter = (int)index; RefreshRows(); };
        search.TextChanged += value => { _marketSearch = value; RefreshRows(); };
        buyQuantity.ValueChanged += value => { _marketBuyQuantity = (int)value; RefreshRows(); };
        sellQuantity.ValueChanged += value => { _marketSellQuantity = (int)value; RefreshRows(); };
        RefreshRows();
        return RefreshRows;

        void RefreshRows()
        {
            var stocks = InventoryTotals();
            int stored = stocks.Values.Sum(stock => stock.Total), capacity = Game.Storage, coins = NativeGame.I(Game.Meta, "coins");
            summary.Text = $"仓库 {stored}/{capacity} 份 · 点击买卖按钮会按所选数量交易。";
            bool any = false;
            foreach (var item in rows)
            {
                var stock = stocks.GetValueOrDefault(item.Name);
                any |= item.Refresh(stock.Total, stock.Available, stock.Reserved, capacity - stored, coins);
            }
            empty.Visible = !any;
        }
        void Trade(string action, string item, int quantity) =>
            Act(action, new JsonObject { ["item"] = item, ["quantity"] = quantity });
    }

    static int MarketPurchaseLimit(int price, int freeSpace, int coins, int quantityLimit) =>
        price <= 0 ? 0 : Math.Max(0, Math.Min(quantityLimit, Math.Min(freeSpace, coins / price)));

    Dictionary<string,(int Total,int Available,int Reserved)> InventoryTotals()
    {
        var totals = new Dictionary<string,(int Total,int Available,int Reserved)>(StringComparer.Ordinal);
        foreach (var batch in Game.Rows("inventory").OfType<JsonNode>())
        {
            string item = NativeGame.S(batch, "item");
            int quantity = NativeGame.I(batch, "quantity"), reserved = Game.ReservedInventoryBatch(batch);
            double expiry = NativeGame.D(batch, "expiry_ts");
            int available = expiry <= 0 || expiry > Game.Now ? quantity - reserved : 0;
            var total = totals.GetValueOrDefault(item);
            totals[item] = (total.Total + quantity, total.Available + available, total.Reserved + reserved);
        }
        return totals;
    }

    Action MarketRecipes(VBoxContainer content)
    {
        if (_managementEntryTarget.StartsWith("recipe:", StringComparison.Ordinal))
            (_workshopRecipeQuery, _workshopRecipeFilter) = ("", 0);
        var refreshQueue = AddWorkshopQueue(content);
        var overview = MarketCard(content, color: "f2ebdb");
        MarketLabel(overview, "制作配方", 19, TownTheme.Leaf);
        var tools = new HFlowContainer();
        tools.AddThemeConstantOverride("h_separation", 8);
        overview.AddChild(tools);
        var search = new LineEdit { Name = "WorkshopRecipeQuery", PlaceholderText = "搜索产物或原料", Text = _workshopRecipeQuery,
            ClearButtonEnabled = true, CustomMinimumSize = new(150, 34), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        tools.AddChild(search);
        var filter = MarketOptions(tools, new[] { "全部配方", "可立即制作", "可安排一批", "待补齐条件" }, _workshopRecipeFilter);
        filter.Name = "WorkshopRecipeFilter";
        var reset = new Button { Name = "WorkshopRecipeReset", Text = "清除筛选" };
        TownTheme.AccentButton(reset, "商店", 6);
        tools.AddChild(reset);
        var summary = MarketLabel(overview, "", 14, TownTheme.Muted, wrap: true);
        summary.Name = "WorkshopRecipeSummary";
        var empty = MarketLabel(content, "没有符合条件的配方。可换个关键词或清除筛选。", 14, TownTheme.Muted, wrap: true);
        empty.Name = "WorkshopRecipeEmpty";
        var recipes = new List<Func<int, int, bool>>();
        foreach (var recipe in Game.Rules["RECIPES"]!.AsObject())
        {
            string name = recipe.Key;
            var rules = recipe.Value!;
            int level = NativeGame.I(rules, "level"), fee = NativeGame.I(rules, "cost"), output = NativeGame.I(rules, "count");
            var inputs = rules["inputs"]!.AsObject().Select(entry => (Item: entry.Key, Need: entry.Value!.GetValue<int>())).ToArray();
            bool targeted = _managementEntryTarget == $"recipe:{name}";
            var card = MarketCard(content, color: targeted ? "f4e7cd" : "fffdf7");
            var panel = card.GetParent<Control>();
            panel.Name = $"MarketRecipe_{name}";
            if (targeted) _managementFocusTarget = panel;
            MarketLabel(card, $"{name} · 每批 {output} 份", 17, TownTheme.Accent("商店").Darkened(.15f));
            MarketLabel(card, $"要求 {level} 级加工坊 · 每批 {fee} 金币", 14, TownTheme.Muted);
            var immediate = new HFlowContainer();
            immediate.AddThemeConstantOverride("h_separation", 7);
            card.AddChild(immediate);
            var button = MarketButton(immediate, "立即制作 1 批",
                () => Act("craft", new JsonObject { ["item"] = name }), expand: false);
            button.Name = $"WorkshopCraft_{name}";
            var instantStatus = MarketLabel(immediate, "", 14, TownTheme.Muted);
            instantStatus.VerticalAlignment = VerticalAlignment.Center;
            var refreshEnqueue = AddWorkshopEnqueue(card, name, inputs, fee, output);
            recipes.Add((levelAvailable, freeSpace) =>
            {
                string query = _workshopRecipeQuery.Trim();
                if (!name.Contains(query, StringComparison.OrdinalIgnoreCase) &&
                    !inputs.Any(input => input.Item.Contains(query, StringComparison.OrdinalIgnoreCase)))
                { panel.Hide(); return false; }

                string missing = inputs.FirstOrDefault(input => Game.Stock(input.Item) < input.Need).Item ?? "";
                string unavailable = levelAvailable < level ? $"需 {level} 级加工坊" : missing.Length > 0 ? $"缺少可用{missing}" :
                    NativeGame.I(Game.Meta, "coins") < fee ? "金币不足" : freeSpace + inputs.Sum(input => input.Need) < output ? "仓库空间不足" : "";
                bool readyNow = unavailable.Length == 0;
                string queueReason = _workshopRecipeFilter is 2 or 3 ? Game.WorkshopEnqueueReason(name, 1) : "";
                panel.Visible = _workshopRecipeFilter switch
                {
                    1 => readyNow, 2 => queueReason.Length == 0, 3 => !readyNow && queueReason.Length > 0, _ => true,
                };
                if (!panel.Visible) return false;
                button.Disabled = !readyNow;
                button.TooltipText = unavailable;
                instantStatus.Text = readyNow ? "可制作" : unavailable;
                refreshEnqueue();
                return true;
            });
        }
        search.TextChanged += text => { _workshopRecipeQuery = text; Refresh(); };
        filter.ItemSelected += index => { _workshopRecipeFilter = (int)index; Refresh(); };
        reset.Pressed += () => { search.Text = _workshopRecipeQuery = ""; filter.Select(_workshopRecipeFilter = 0); Refresh(); };
        Refresh();
        return Refresh;

        void Refresh()
        {
            refreshQueue();
            int level = Game.Rows("buildings").Where(building => NativeGame.S(building!, "kind") == "加工坊")
                .Select(building => NativeGame.I(building!, "level")).DefaultIfEmpty(0).Max();
            int freeSpace = Game.Storage - Game.Rows("inventory").Sum(batch => NativeGame.I(batch!, "quantity"));
            int count = recipes.Count(update => update(level, freeSpace));
            string workplace = level > 0 ? $"现有加工坊最高 {level} 级" : "庄园尚未建造加工坊";
            summary.Text = $"{workplace} · {count} / {recipes.Count} 个配方";
            empty.Visible = count == 0;
            reset.Disabled = _workshopRecipeQuery.Length == 0 && _workshopRecipeFilter == 0;
        }
    }

    void CareControls()
    {
        var active = Data.Pets.Where(p => p != null && Game.Active(p)).Select(p => p!).ToArray();
        var inManor = active.Where(p => !NativeGame.B(p, "fostered")).ToArray();
        Section("照护概览", $"在园 {inManor.Length} 只 · 寄养 {active.Length - inManor.Length} 只");
        AddCareWorkbench();
        var rosterIds = active.Select(p => NativeGame.S(p, "pet_id")).ToArray();
        _refreshManagementPage += () =>
        {
            var ids = Data.Pets.OfType<JsonNode>().Where(Game.Active).Select(p => NativeGame.S(p, "pet_id"));
            if (!ids.SequenceEqual(rosterIds)) ShowManagement("照护");
        };
        if (active.Length == 0)
        {
            Text("庄园还没有伙伴，可以去商店领养。");
            ActionButton("前往商店", () => ShowManagement("商店"));
            return;
        }

        var summary = new HBoxContainer();
        summary.AddThemeConstantOverride("separation", 8);
        _managementBody!.AddChild(summary);
        AddCount("饱食未满", p => NativeGame.D(p, "hunger") < 100, "faeade");
        AddCount("清洁未满", p => NativeGame.D(p, "cleanliness") < 100, "e1eee9");
        AddCount("可互动", p => NativeGame.D(p, "interact_ready_ts") <= Game.Now, "ede8f6");
        AddCount("有产物", p => NativeGame.D(p, "product_ready_ts") != 0 &&
            NativeGame.D(p, "product_expiry_ts") > Game.Now, "f8efd9");

        var actions = new HBoxContainer();
        actions.AddThemeConstantOverride("separation", 8);
        _managementBody.AddChild(actions);
        var preview = CreateBatchCarePreview();
        foreach (var (title, kind) in new[] { ("批量喂食", "feed"), ("批量洗澡", "bath"), ("批量互动", "play"), ("批量收取", "collect") })
        {
            var button = MarketButton(actions, title, () => preview(title,kind), accent: "照护");
            button.Name = $"PreviewBatchCare_{kind}";
            button.Disabled = inManor.Length == 0;
        }
        Text("批量照护会优先处理需求更高的伙伴，自动跳过寄养中或暂不符合条件的伙伴。");
        ActionButton("定位宠物活动区", LocateActivityArea);
        Section("伙伴状态", "需求较高的在园伙伴优先显示");

        const int perPage = 8;
        int pageCount = (active.Length + perPage - 1) / perPage;
        _carePage = Math.Clamp(_carePage, 0, pageCount - 1);
        var pets = active.OrderBy(p => NativeGame.B(p, "fostered"))
            .ThenBy(p => Math.Min(NativeGame.D(p, "health"), Math.Min(NativeGame.D(p, "hunger"), NativeGame.D(p, "cleanliness")))).ToArray();
        if (pageCount > 1)
        {
            var pager = new HBoxContainer();
            pager.AddThemeConstantOverride("separation", 8);
            _managementBody.AddChild(pager);
            MarketLabel(pager, $"第 {_carePage + 1} / {pageCount} 页", color: TownTheme.Muted);
            pager.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
            MarketButton(pager, "上一页", () => ChangeCarePage(-1), expand: false, accent: "照护").Disabled = _carePage == 0;
            MarketButton(pager, "下一页", () => ChangeCarePage(1), expand: false, accent: "照护").Disabled = _carePage == pageCount - 1;
        }
        var grid = ManagementCardGrid(_managementBody);
        foreach (var pet in pets.Skip(_carePage * perPage).Take(perPage))
        {
            string id = NativeGame.S(pet, "pet_id");
            var card = MarketCard(grid, 164);
            card.AddChild(AnimalBadge(NativeGame.S(pet, "species"), 144, 92, pet));
            var condition = MarketLabel(card, "", color: TownTheme.Muted);
            condition.HorizontalAlignment = HorizontalAlignment.Center;
            void UpdateCondition() => condition.Text = NativeGame.B(pet, "fostered") ? "寄养中" :
                NativeGame.D(pet, "interact_ready_ts") > Game.Now ? "互动冷却中" : "在园 · 可互动";
            UpdateCondition();
            _refreshManagementPage += UpdateCondition;
            card.AddChild(LivePetNeedMeter("饱食", () => NativeGame.D(pet, "hunger"), "d8a16c"));
            card.AddChild(LivePetNeedMeter("清洁", () => NativeGame.D(pet, "cleanliness"), "80b5b8"));
            card.AddChild(LivePetNeedMeter("健康", () => NativeGame.D(pet, "health"), "7eaa84"));
            MarketButton(card, "查看伙伴", () => OpenPetManagement(id), accent: "照护");
        }

        void AddCount(string label, Func<JsonNode,bool> matches, string color)
        {
            var card = MarketCard(summary, color: color);
            MarketLabel(card, label, 14, TownTheme.Muted).HorizontalAlignment = HorizontalAlignment.Center;
            var count = MarketLabel(card, "", 19);
            count.HorizontalAlignment = HorizontalAlignment.Center;
            void UpdateCount() => count.Text = $"{inManor.Count(matches)} 只";
            UpdateCount();
            _refreshManagementPage += UpdateCount;
        }
        void ChangeCarePage(int direction)
        {
            _carePage += direction;
            ShowManagement("照护");
        }
    }

    GridContainer ManagementCardGrid(Control parent)
    {
        const int spacing = 10, minWidth = 164;
        var grid = new GridContainer { Columns = 3 };
        grid.AddThemeConstantOverride("h_separation", spacing);
        grid.AddThemeConstantOverride("v_separation", spacing);
        parent.AddChild(grid);
        var scroll = _managementScroll!;
        void Fit()
        {
            if (scroll.Size.X > 0)
                grid.Columns = Math.Clamp((int)((scroll.Size.X - 20 + spacing) / (minWidth + spacing)), 1, 4);
        }
        scroll.Resized += Fit;
        grid.TreeExiting += () => scroll.Resized -= Fit;
        Fit();
        return grid;
    }

    static VBoxContainer MarketCard(Control parent, int minWidth = 0, string color = "fffdf7")
    {
        var panel = new PanelContainer
        {
            CustomMinimumSize = new Vector2(minWidth, 0),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
        };
        panel.AddThemeStyleboxOverride("panel", TownTheme.Box(color, 11, 9));
        parent.AddChild(panel);
        var body = new VBoxContainer();
        body.AddThemeConstantOverride("separation", 5);
        panel.AddChild(body);
        return body;
    }

    static Label MarketLabel(Control parent, string text, int size = 15, Color? color = null, bool wrap = false)
    {
        var label = new Label { Text = text };
        if (wrap) label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        label.AddThemeFontSizeOverride("font_size", size);
        label.AddThemeColorOverride("font_color", color ?? TownTheme.Ink);
        parent.AddChild(label);
        return label;
    }

    static Button MarketButton(Control parent, string text, Action action, bool expand = true, string accent = "商店")
    {
        var button = new Button
        {
            Text = text, CustomMinimumSize = new Vector2(0, 38),
            SizeFlagsHorizontal = expand ? Control.SizeFlags.ExpandFill : Control.SizeFlags.Fill
        };
        TownTheme.AccentButton(button, accent);
        button.Pressed += action;
        parent.AddChild(button);
        return button;
    }

    static OptionButton MarketOptions(Control parent, string[] values, int selected)
    {
        var options = new OptionButton();
        TownTheme.AccentButton(options, "商店");
        foreach (string value in values) options.AddItem(value);
        options.Select(selected);
        parent.AddChild(options);
        return options;
    }

    static SpinBox MarketQuantity(Control parent, int value)
    {
        var quantity = new SpinBox { MinValue = 1, MaxValue = 99, Step = 1, Value = value, CustomMinimumSize = new Vector2(96, 0) };
        parent.AddChild(quantity);
        return quantity;
    }
}
