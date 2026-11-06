using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;

public partial class Main
{
    sealed record GrowthCard(PanelContainer Panel, string Title, Label Status, ProgressBar Progress, Label Hint,
        Label Reward, Button Action, Button Track);
    static readonly string[] GrowthFilters={"全部目标","进行中","已达成"};
    int _growthFilter;
    string _trackedGrowthGoal="";
    bool _growthTrackingLoaded;

    void GrowthControls()
    {
        LoadGrowthTracking();
        var body = _managementBody!;
        var summary = Section("庄园成长路线", "已达成 0 项成就")!;
        Text("选一个目标开始。达成成就后，金币和声望奖励会自动发放。");
        var cards = new Dictionary<string, GrowthCard>();
        var groups = new List<(Control Heading, GridContainer Grid)>();
        var tracked = MarketCard(body,color:"e8efe7");
        var trackedHeading=MarketLabel(tracked,"",17,TownTheme.Leaf,wrap:true);
        trackedHeading.Name="GrowthTrackedHeading";
        var trackedHint=MarketLabel(tracked,"",color:TownTheme.Muted,wrap:true);
        var trackedActions=new HFlowContainer();tracked.AddChild(trackedActions);
        var trackedAction=MarketButton(trackedActions,"继续目标",()=>{
            if(cards.TryGetValue(_trackedGrowthGoal,out var card)&&card.Action.Visible)
                card.Action.EmitSignal(BaseButton.SignalName.Pressed);
        },expand:false,accent:"成长");
        trackedAction.Name="GrowthTrackedAction";
        var stopTracking=new Button{Name="GrowthStopTracking",Text="取消追踪"};
        TownTheme.AccentButton(stopTracking,"成长",6);trackedActions.AddChild(stopTracking);

        var tools=new HFlowContainer();tools.AddThemeConstantOverride("h_separation",8);body.AddChild(tools);
        var filter=MarketOptions(tools,GrowthFilters,_growthFilter);
        filter.Name="GrowthGoalFilter";
        MarketButton(tools,"查看季节委托",()=>ShowManagement("成长","town-commission"),expand:false,accent:"成长");
        var empty=Text("此分类暂无目标。");
        empty.Name="GrowthGoalEmpty";

        GridContainer CreateGrid(string title, string detail)
        {
            var heading=Section(title, detail)!.GetParent<Control>().GetParent<Control>();
            var grid = new GridContainer { Columns = 2 };
            grid.AddThemeConstantOverride("h_separation", 10);
            grid.AddThemeConstantOverride("v_separation", 10);
            body.AddChild(grid);
            groups.Add((heading,grid));
            return grid;
        }

        void AddAchievement(GridContainer grid, string key, string actionText, string page, Action? action = null)
        {
            var achievement = Game.Rules["ACHIEVEMENTS"]![key]!;
            string title = achievement[0]!.GetValue<string>();
            string requirement = achievement[2]!.GetValue<string>();
            cards.Add(key, CreateGrowthCard(grid, key, title, requirement, actionText, page,
                action ?? (() => ShowManagement(page))));
        }

        var starting = CreateGrid("经营起步", "从眼前能做的事开始");
        AddAchievement(starting, "orders", "查看邻里订单", "订单");
        AddAchievement(starting, "builder", "查看庄园地块", "建造", OpenGrowthManorPlot);
        AddAchievement(starting, "friends", "去照护伙伴", "照护");
        var district = CreateGrowthCard(starting, "districts", "开放小镇街区", "开放庄园以外的街区，扩展小镇经营。",
            "查看街区建设", "建造", OpenGrowthDistrict);
        cards.Add("districts",district);

        var collection = CreateGrid("伙伴与收藏", "记录陪伴、繁育和经营成果");
        AddAchievement(collection, "collector", "去认识伙伴", "商店", OpenGrowthPetShop);
        AddAchievement(collection, "all_species", "查看物种图鉴", "档案", () => ShowManagement("档案", "codex"));
        AddAchievement(collection, "first_breed", "查看伴侣与繁育", "宠物", OpenGrowthPetManagement);
        AddAchievement(collection, "rare", "查看物种图鉴", "档案", () => ShowManagement("档案", "codex"));
        AddAchievement(collection, "old_friend", "查看伙伴档案", "档案");
        AddAchievement(collection, "seller", "查看伙伴管理", "宠物", OpenGrowthPetManagement);

        void Update()
        {
            var achievements = Game.GrowthAchievements().ToArray();
            summary.Text = $"已达成 {achievements.Count(goal => goal.Earned)} / {achievements.Length} 项成就";
            var known = Game.KnownGrowthSpecies();
            foreach (var goal in achievements)
            {
                var card = cards[goal.Key];
                UpdateGrowthProgress(card, goal.Current, goal.Target, goal.Earned);
                var reward = Game.Rules["ACHIEVEMENTS"]![goal.Key]!;
                card.Reward.Text = $"{(goal.Earned ? "已获" : "奖励")} {reward[3]!.GetValue<int>()} 金币 · {reward[4]!.GetValue<int>()} 声望";
                SetGrowthHint(card, goal.Earned ? "" : GrowthHint(goal.Key, known));
            }

            var zones = GrowthDistricts();
            int opened = zones.Count(zone => Game.ZoneLevel(zone) > 0);
            UpdateGrowthProgress(district, opened, zones.Length, opened == zones.Length);
            var next = NextGrowthDistrict();
            if (next == null) SetGrowthHint(district,"所有街区均已开放。");
            else{
                string name = NativeGame.S(Data.Root["zones"]![next]!, "name");
                int price = Game.DistrictUpgradePrice(next);
                int shortage = Math.Max(0, price - NativeGame.I(Data.Meta, "coins"));
                string cost = shortage > 0 ? $"需 {price} 金币，还差 {shortage}" : $"需 {price} 金币，已备齐";
                SetGrowthHint(district, $"下一站 · {name} · {cost}");
            }
            RefreshFilters();
            RefreshTracking();
        }

        void RefreshFilters()
        {
            int earned=cards.Values.Count(card=>!card.Action.Visible);
            int[] counts={cards.Count,cards.Count-earned,earned};
            for(int i=0;i<counts.Length;i++){
                string label=$"{GrowthFilters[i]} · {counts[i]}";
                if(filter.GetItemText(i)!=label)filter.SetItemText(i,label);
            }
            foreach(var card in cards.Values)
                card.Panel.Visible=_growthFilter==0||(_growthFilter==1?card.Action.Visible:!card.Action.Visible);
            foreach(var (heading,grid) in groups)
                heading.Visible=grid.Visible=grid.GetChildren().OfType<Control>().Any(card=>card.Visible);
            empty.Visible=counts[_growthFilter]==0;
        }

        void RefreshTracking()
        {
            foreach(var (key,card) in cards){
                card.Track.Text=key==_trackedGrowthGoal?"取消追踪":"追踪";
                card.Track.SetPressedNoSignal(key==_trackedGrowthGoal);
            }
            if(cards.TryGetValue(_trackedGrowthGoal,out var selected)){
                trackedHeading.Text=$"正在追踪 · {selected.Title} · {selected.Status.Text}";
                trackedHint.Text=selected.Hint.Visible?selected.Hint.Text:selected.Action.Visible?"可继续完成当前目标。":"目标已达成，可换一个目标追踪。";
                trackedAction.Text=selected.Action.Text;
                trackedAction.Visible=selected.Action.Visible;
                stopTracking.Show();
            }else{
                trackedHeading.Text="追踪一个成长目标";
                trackedHint.Text="点选目标旁的「追踪」，这里会汇总进度和下一步。";
                trackedAction.Hide();stopTracking.Hide();
            }
        }

        foreach(var (key,card) in cards)
            card.Track.Pressed+=()=>{SetGrowthTracking(_trackedGrowthGoal==key?"":key);RefreshTracking();};
        stopTracking.Pressed+=()=>{SetGrowthTracking("");RefreshTracking();};
        filter.ItemSelected+=index=>{_growthFilter=(int)index;RefreshFilters();};
        Update();
        _refreshManagementPage += Update;
        TownCommissionControls();
    }

    GrowthCard CreateGrowthCard(GridContainer grid, string key, string title, string requirement,
        string actionText, string page, Action action)
    {
        Color accent = TownTheme.Accent(page);
        var panel = new PanelContainer { Name = $"GrowthGoal_{key}", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        var style = TownTheme.Box(accent.Lerp(new Color("fffcf4"), .91f).ToHtml(), 10, 10);
        style.BorderColor = accent;
        style.BorderWidthTop = 2;
        panel.AddThemeStyleboxOverride("panel", style);
        grid.AddChild(panel);

        var content = new VBoxContainer();
        content.AddThemeConstantOverride("separation", 5);
        panel.AddChild(content);
        var heading = new HBoxContainer();
        content.AddChild(heading);
        var name = MarketLabel(heading, title, 17, accent.Darkened(.22f), wrap: true);
        name.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        var status = MarketLabel(heading, "", 14, accent.Darkened(.18f));
        status.Name = $"GrowthStatus_{key}";

        MarketLabel(content, requirement, color: TownTheme.Ink, wrap: true);
        var progress = new ProgressBar
        {
            Name = $"GrowthProgress_{key}", MinValue = 0, Step = 0, ShowPercentage = false,
            CustomMinimumSize = new(0, 8), MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        progress.AddThemeStyleboxOverride("background", TownTheme.Box("eee8dd", 4, 0));
        progress.AddThemeStyleboxOverride("fill", TownTheme.Box(accent.ToHtml(), 4, 0));
        content.AddChild(progress);
        var hint = MarketLabel(content, "", color: TownTheme.Muted, wrap: true);
        var reward = MarketLabel(content, "", 14, TownTheme.Leaf);
        reward.Visible = key != "districts";
        var actions=new HFlowContainer();actions.AddThemeConstantOverride("h_separation",6);content.AddChild(actions);
        var button = MarketButton(actions, actionText, action, expand: false, accent: page);
        button.Name = $"GrowthAction_{key}";
        button.SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin;
        var track=new Button{Name=$"GrowthTrack_{key}",ToggleMode=true};
        TownTheme.AccentButton(track,"成长",6);actions.AddChild(track);
        return new GrowthCard(panel,title,status,progress,hint,reward,button,track);
    }

    string GrowthTrackingPath=>$"user://town-growth-{_saveSlot}.cfg";

    string ReadGrowthTracking(ConfigFile config)
    {
        string key=config.GetValue("growth","tracked","").AsString();
        return key=="districts"||Game.GrowthAchievements().Any(goal=>goal.Key==key)?key:"";
    }

    void LoadGrowthTracking()
    {
        if(_growthTrackingLoaded)return;
        _growthTrackingLoaded=true;
        if(!UseTownPreferences()||ExplicitSavePath!=null)return;
        using var config=new ConfigFile();
        if(config.Load(GrowthTrackingPath)==Error.Ok)_trackedGrowthGoal=ReadGrowthTracking(config);
    }

    void SetGrowthTracking(string key)
    {
        _trackedGrowthGoal=key;
        if(!UseTownPreferences()||ExplicitSavePath!=null)return;
        using var config=new ConfigFile();
        config.SetValue("growth","tracked",key);
        if(config.Save(GrowthTrackingPath)!=Error.Ok)SetManagementFeedback("成长追踪设置未能保存。",true);
    }

    static void UpdateGrowthProgress(GrowthCard card, double current, int target, bool earned)
    {
        card.Progress.MaxValue = Math.Max(1, target);
        card.Progress.Value = earned ? card.Progress.MaxValue : Math.Clamp(current, 0, target);
        double displayed = earned ? target : Math.Floor(card.Progress.Value);
        card.Status.Text = earned ? $"已达成 · {displayed:0}/{target}" : $"{displayed:0}/{target}";
        card.Action.Visible = !earned;
    }

    static void SetGrowthHint(GrowthCard card, string hint)
    {
        card.Hint.Text = hint;
        card.Hint.Visible = hint.Length > 0;
    }

    string GrowthHint(string key, HashSet<string> known) => key switch
    {
        "orders" => "从邻里订单中挑选物资齐全的委托。",
        "builder" => GrowthManorPlot() is JsonNode plot
            ? NativeGame.B(plot, "unlocked")
                ? $"地块 {NativeGame.I(plot, "building_id")} 可直接建造。"
                : $"可解锁地块 {NativeGame.I(plot, "building_id")}，需 {Game.ManorUnlockPrice()} 金币。"
            : "查看已有建筑和庄园地块。",
        "friends" => "照护伙伴时，可通过互动提高好感。",
        "collector" => GrowthMissingSpecies(Game.Rules["CLASSIC_SPECIES"]!.AsArray().Select(entry => entry!.GetValue<string>()), known),
        "all_species" => GrowthMissingSpecies(Game.Rules["SPECIES"]!.AsObject().Select(entry => entry.Key), known),
        "first_breed" => "打开伙伴管理，可查看伴侣和繁育选项。",
        "rare" => "图鉴会保留已发现的稀有外观。",
        "old_friend" => "档案会记录伙伴从入园到离园的经历。",
        "seller" => "在伙伴管理里可查看出售条件。",
        _ => "",
    };

    static string GrowthMissingSpecies(IEnumerable<string> species, HashSet<string> known)
    {
        var missing = species.Where(name => !known.Contains(name)).ToArray();
        if (missing.Length == 0) return "所需物种均已记录。";
        string names = string.Join("、", missing.Take(3));
        return $"尚未结识 {names}{(missing.Length > 3 ? $"等 {missing.Length} 种" : "")}。";
    }

    JsonNode? GrowthManorPlot()
    {
        var empty = Game.Rows("buildings").OfType<JsonNode>()
            .Where(plot => NativeGame.S(plot, "kind").Length == 0).ToArray();
        return empty.FirstOrDefault(plot => NativeGame.B(plot, "unlocked"))
            ?? empty.FirstOrDefault(plot => Game.HasOpenManorNeighbor(NativeGame.I(plot, "building_id")));
    }

    void OpenGrowthManorPlot()
    {
        _buildWorkspace = 0;
        if (GrowthManorPlot() is JsonNode plot) _buildPlot = NativeGame.I(plot, "building_id");
        ShowManagement("建造", "manor");
    }

    string[] GrowthDistricts() => Data.Root["zones"]!.AsObject().Select(zone => zone.Key)
        .Where(zone => zone != "estate").ToArray();

    string? NextGrowthDistrict() => GrowthDistricts().Where(zone => Game.ZoneLevel(zone) == 0)
        .OrderBy(Game.DistrictUpgradePrice).FirstOrDefault();

    void OpenGrowthDistrict()
    {
        _buildWorkspace = 2;
        if (NextGrowthDistrict() is string next) _buildDistrict = Array.IndexOf(GrowthDistricts(), next);
        ShowManagement("建造", "district");
    }

    void OpenGrowthPetShop()
    {
        _marketSection = 0;
        ShowManagement("商店");
    }

    void OpenGrowthPetManagement()
    {
        var pet = Data.Pets.OfType<JsonNode>().FirstOrDefault(pet => NativeGame.S(pet, "pet_id") == _selectedPet && Game.Active(pet))
            ?? Data.Pets.OfType<JsonNode>().FirstOrDefault(Game.Active);
        if (pet != null) _selectedPet = NativeGame.S(pet, "pet_id");
        _petManagementExpanded = true;
        ShowManagement("宠物", "pet-management");
    }
}
