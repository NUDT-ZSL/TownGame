using Godot;
using System;
using System.Linq;
using System.Text.Json.Nodes;

public partial class Main
{
    const string FirstDayGuideKey = "native_first_day_guide";
    const int GuideFed = 1, GuideLocated = 2, GuideOrders = 4, GuideSkipped = 8;
    const int GuideComplete = GuideFed | GuideLocated | GuideOrders;

    PanelContainer? _firstDayGuidePanel;
    Label? _firstDayGuideTitle, _firstDayGuideHint;
    Button? _firstDayGuideAction, _firstDayGuideDismiss;
    bool _firstDayGuideSession, _firstDayGuideSuppressWrites, _firstDayGrowthOffer;

    int FirstDayGuideProgress => Data.Meta[FirstDayGuideKey] is JsonValue value
        && value.TryGetValue<int>(out int progress) ? progress & (GuideComplete | GuideSkipped) : 0;

    bool FirstDayGuideActive => _firstDayGuideSession
        && (FirstDayGuideProgress & GuideSkipped) == 0
        && (FirstDayGuideProgress & GuideComplete) != GuideComplete;

    void InitializeFirstDayGuide(bool hasSavedGame)
    {
        _firstDayGuideSuppressWrites = DisplayServer.GetName() == "headless" || OS.GetCmdlineUserArgs().Any(arg =>
            arg is "--smoke" or "--ui-smoke" || arg.StartsWith("--verify-", StringComparison.Ordinal)
            || arg.StartsWith("--benchmark", StringComparison.Ordinal) || arg.EndsWith("-preview", StringComparison.Ordinal));
        if (!_firstDayGuideSuppressWrites && (!hasSavedGame || Data.Meta.ContainsKey(FirstDayGuideKey)))
        {
            Data.Meta[FirstDayGuideKey] ??= 0;
            _firstDayGuideSession = true;
        }
        RefreshFirstDayGuide();
    }

    void CreateFirstDayGuide(CanvasLayer layer, Theme theme)
    {
        var panel = new PanelContainer { Name = "FirstDayGuide", Position = new(18, 115), CustomMinimumSize = new(375, 0), Theme = theme, Visible = false };
        var style = TownTheme.Box("faf4e7", 12, 10);
        style.BorderColor = TownTheme.Leaf;
        style.BorderWidthLeft = 4;
        panel.AddThemeStyleboxOverride("panel", style);
        layer.AddChild(panel);
        _firstDayGuidePanel = panel;

        var content = new VBoxContainer();
        content.AddThemeConstantOverride("separation", 6);
        panel.AddChild(content);
        _firstDayGuideTitle = new Label();
        _firstDayGuideTitle.AddThemeColorOverride("font_color", TownTheme.Leaf);
        _firstDayGuideTitle.AddThemeFontSizeOverride("font_size", 18);
        content.AddChild(_firstDayGuideTitle);
        _firstDayGuideHint = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new(350, 0) };
        content.AddChild(_firstDayGuideHint);

        var buttons = new HBoxContainer();
        buttons.AddThemeConstantOverride("separation", 8);
        content.AddChild(buttons);
        _firstDayGuideAction = new Button { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        TownTheme.AccentButton(_firstDayGuideAction, "照护", 6);
        _firstDayGuideAction.Pressed += DoFirstDayGuideAction;
        buttons.AddChild(_firstDayGuideAction);
        _firstDayGuideDismiss = new Button { Text = "跳过引导" };
        TownTheme.AccentButton(_firstDayGuideDismiss, "存档", 6);
        _firstDayGuideDismiss.Pressed += () =>
        {
            if (_firstDayGrowthOffer)
            {
                _firstDayGrowthOffer = false;
                RefreshFirstDayGuide();
                return;
            }
            if (SaveFirstDayGuideProgress(FirstDayGuideProgress | GuideSkipped)) RefreshFirstDayGuide();
        };
        buttons.AddChild(_firstDayGuideDismiss);
    }

    void StartFirstDayGuide()
    {
        if (!SaveFirstDayGuideProgress(0)) return;
        _firstDayGuideSession = true;
        _firstDayGrowthOffer = false;
        EndPlacement();
        _management?.Hide();
        RefreshFirstDayGuide();
    }

    void AddFirstDayGuideShortcut(Container shortcuts)
    {
        var button = new Button { Text = "新手引导", SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin };
        TownTheme.AccentButton(button, "照护", 6);
        button.Pressed += () =>
        {
            if (!FirstDayGuideActive) { StartFirstDayGuide(); return; }
            _management?.Hide();
            RefreshFirstDayGuide();
        };
        shortcuts.AddChild(button);
    }

    void RefreshFirstDayGuide()
    {
        if (!GodotObject.IsInstanceValid(_firstDayGuidePanel)) return;
        bool visible = (FirstDayGuideActive || _firstDayGrowthOffer) && _management?.Visible != true;
        _firstDayGuidePanel!.Visible = visible;
        if (!visible) return;

        if (_firstDayGrowthOffer)
        {
            _firstDayGuideTitle!.Text = "首日引导已完成";
            _firstDayGuideHint!.Text = "成长路线会记录庄园扩建、订单和物种收集的进展。";
            _firstDayGuideAction!.Text = "查看成长路线";
            _firstDayGuideDismiss!.Text = "收起";
            return;
        }

        int progress = FirstDayGuideProgress;
        int step = (progress & GuideFed) == 0 ? 1 : (progress & GuideLocated) == 0 ? 2 : 3;
        _firstDayGuideTitle!.Text = $"首日引导 · {step} / 3";
        _firstDayGuideDismiss!.Text = "跳过引导";
        (_firstDayGuideHint!.Text, _firstDayGuideAction!.Text) = step switch
        {
            1 => ("照顾一只伙伴。用一份普通饲料喂食，也可以从左下方的照护按钮操作。", "喂一份普通饲料"),
            2 => ("使用顶栏的「找宠物」，镜头会移到伙伴身边。", "找宠物"),
            _ => ("打开订单页，看看邻居需要什么，以及交付后能获得多少金币。", "查看订单"),
        };
    }

    void DoFirstDayGuideAction()
    {
        if (_firstDayGrowthOffer) { ShowManagement("成长"); return; }
        if (!FirstDayGuideActive) return;
        if ((FirstDayGuideProgress & GuideFed) == 0)
        {
            var actor = FarmPet(_selectedPet) ?? GetChildren().OfType<PetAgent>().FirstOrDefault(pet => !pet.IsQueuedForDeletion());
            if (actor == null) { _selection = "庄园暂无可照护的伙伴，可先去商店领养。"; return; }
            _selectedPet = actor.Ident;
            FeedSelected();
        }
        else if ((FirstDayGuideProgress & GuideLocated) == 0) FindPet();
        else ShowManagement("订单");
    }

    // Feeding records the checkpoint inside the existing action transaction.
    void RecordFirstDayFeeding()
    {
        if (FirstDayGuideActive && (FirstDayGuideProgress & GuideFed) == 0)
            Data.Meta[FirstDayGuideKey] = FirstDayGuideProgress | GuideFed;
    }

    void RecordFirstDayVisit(int step)
    {
        int previous = FirstDayGuideProgress;
        if (!FirstDayGuideActive || (previous & step) != 0 || !SaveFirstDayGuideProgress(previous | step)) return;
        RefreshFirstDayGuide();
        if (!FirstDayGuideActive) OfferGrowthAfterGuide();
    }

    void OfferGrowthAfterGuide()
    {
        _firstDayGrowthOffer = true;
        if (_management?.Visible == true)
        {
            SetManagementFeedback("首日引导已完成。可以在成长路线查看接下来的目标。");
            var button = AddFirstDayGrowthLink();
            Callable.From(() =>
            {
                if (GodotObject.IsInstanceValid(button) && button.IsInsideTree() && _management?.Visible == true)
                    _managementScroll?.EnsureControlVisible(button);
            }).CallDeferred();
        }
        RefreshFirstDayGuide();
    }

    Button AddFirstDayGrowthLink()
    {
        var button = ActionButton("查看成长路线", () => ShowManagement("成长"));
        button.Name = "FirstDayGrowthLink";
        button.SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin;
        TownTheme.AccentButton(button, "成长", 7);
        _managementBody!.MoveChild(button, 0);
        return button;
    }

    bool SaveFirstDayGuideProgress(int progress)
    {
        var previous = Data.Meta[FirstDayGuideKey]?.DeepClone();
        var anchor = Data.Meta["native_real_anchor"]?.DeepClone();
        Data.Meta[FirstDayGuideKey] = progress;
        if (_firstDayGuideSuppressWrites) return true;
        try
        {
            Data.Meta["native_real_anchor"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            WriteTownProgress();
            return true;
        }
        catch (Exception error)
        {
            if (previous == null) Data.Meta.Remove(FirstDayGuideKey);
            else Data.Meta[FirstDayGuideKey] = previous;
            if (anchor == null) Data.Meta.Remove("native_real_anchor");
            else Data.Meta["native_real_anchor"] = anchor;
            _selection = "引导进度保存失败：" + error.Message;
            if (_management?.Visible == true) SetManagementFeedback(_selection, true);
            return false;
        }
    }
}
