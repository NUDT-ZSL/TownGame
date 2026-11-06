using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;

public partial class Main
{
    const string SceneSelectionHint = "左键拖动平移 · 右键拖动旋转\n点击人物或宠物查看资料";
    readonly List<(Label Label, ProgressBar Bar, string Caption, string Key, Color Tint)> _scenePetNeeds = new();
    Label _sceneTownHeading = null!, _scenePetName = null!, _scenePetActionReasons = null!;
    VBoxContainer _scenePetCare = null!;
    Button _scenePetFeed = null!, _scenePetPlay = null!, _scenePetCollect = null!, _scenePetHospital = null!;

    void CreateScenePetCare(VBoxContainer body)
    {
        _scenePetCare = new VBoxContainer { Name = "ScenePetCare", Visible = false };
        _scenePetCare.AddThemeConstantOverride("separation", 6);
        body.AddChild(_scenePetCare);
        _scenePetName = new Label
        {
            Name = "ScenePetName",
            TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
        };
        _scenePetName.AddThemeFontSizeOverride("font_size", 18);
        _scenePetName.AddThemeColorOverride("font_color", TownTheme.Accent("宠物").Darkened(.24f));
        _scenePetCare.AddChild(_scenePetName);

        var needs = new HBoxContainer();
        needs.AddThemeConstantOverride("separation",7);
        _scenePetCare.AddChild(needs);
        void Need(string name, string caption, string key, string tint)
        {
            var column=new VBoxContainer{SizeFlagsHorizontal=Control.SizeFlags.ExpandFill};
            column.AddThemeConstantOverride("separation",3);
            needs.AddChild(column);
            var label = new Label { Name = name };
            label.AddThemeFontSizeOverride("font_size", 15);
            column.AddChild(label);
            var bar=new ProgressBar{Name=name+"Meter",MaxValue=100,ShowPercentage=false,CustomMinimumSize=new(0,6),MouseFilter=Control.MouseFilterEnum.Ignore};
            bar.AddThemeStyleboxOverride("background",TownTheme.Box("e7dfd0",3,0));
            column.AddChild(bar);
            _scenePetNeeds.Add((label,bar,caption,key,new Color(tint)));
        }
        Need("ScenePetHunger", "饱食", "hunger", "835b30");
        Need("ScenePetHealth", "健康", "health", "44775b");
        Need("ScenePetCleanliness", "清洁", "cleanliness", "416e89");
        Need("ScenePetMood", "心情", "happy", "80546f");

        var actions = new HBoxContainer();
        actions.AddThemeConstantOverride("separation", 6);
        _scenePetCare.AddChild(actions);
        Button AddAction(Container row, string name, string caption, string tab, Action run)
        {
            var button = new Button { Name = name, Text = caption, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            TownTheme.AccentButton(button, tab, 6);
            button.Pressed += run;
            row.AddChild(button);
            return button;
        }

        _scenePetFeed = AddAction(actions, "ScenePetFeed", "喂普通饲料", "照护", FeedSelected);
        _followPetButton = AddAction(actions, "ScenePetFollow", "跟随宠物", "宠物", () =>
        {
            if (_followPet != null) { StopPetFollow(); return; }
            if (SelectedScenePet() is JsonNode pet) StartPetFollow(NativeGame.S(pet, "pet_id"));
        });
        _followPetButton.TooltipText = "按 Esc 或移动镜头可退出跟随";
        AddAction(actions, "ScenePetCareDetails", "照护详情", "家庭", () =>
        {
            if (SelectedScenePet() is JsonNode pet) OpenPetManagement(NativeGame.S(pet, "pet_id"));
        });
        var care = new HBoxContainer();
        care.AddThemeConstantOverride("separation", 6);
        _scenePetCare.AddChild(care);
        _scenePetPlay=AddAction(care,"ScenePetPlay","互动","宠物",()=>RunScenePetCare("play"));
        _scenePetCollect=AddAction(care,"ScenePetCollect","收产物","商店",()=>RunScenePetCare("collect"));
        _scenePetHospital=AddAction(care,"ScenePetHospital","送医","照护",()=>RunScenePetCare("hospitalize"));
        _scenePetActionReasons=new Label{Name="ScenePetCareReasons",AutowrapMode=TextServer.AutowrapMode.WordSmart};
        _scenePetActionReasons.AddThemeFontSizeOverride("font_size",13);
        _scenePetActionReasons.AddThemeColorOverride("font_color",TownTheme.Muted);
        _scenePetCare.AddChild(_scenePetActionReasons);
    }

    JsonNode? SelectedScenePet()
    {
        if (_placementAction.Length > 0 || _management?.Visible == true || _selectedPet.Length == 0) return null;
        var pet = Data.FindPet(_selectedPet);
        return pet != null && Game.Active(pet) && !NativeGame.B(pet, "fostered") && FarmPet(_selectedPet) != null ? pet : null;
    }

    void RefreshScenePetCare()
    {
        var pet = SelectedScenePet();
        bool hasPet = pet != null;
        string feedback = _selection;
        bool wasShowingResident=_residentSceneTools.Visible;
        bool showResident=RefreshResidentSceneTools(!hasPet&&_placementAction.Length==0&&_management?.Visible!=true);
        bool showFeedback = !showResident&&feedback.Length > 0 && feedback != SceneSelectionHint;
        bool canShowGuide=!hasPet&&!showResident&&_placementAction.Length==0;
        bool expanded=canShowGuide&&_townFieldGuideExpanded;
        bool resize = wasShowingResident!=showResident || _scenePetCare.Visible != hasPet || _details.Visible != showFeedback ||
            _townFieldGuide.Visible!=expanded || _townFieldGuideToggle.Visible!=canShowGuide || _details.Text!=(showFeedback?feedback:"");
        _scenePetCare.Visible = hasPet;
        _sceneTownHeading.Visible = !hasPet&&showFeedback;
        _details.Visible = showFeedback;
        _details.Text = showFeedback?feedback:"";
        _panel.CustomMinimumSize=new(hasPet||showResident||showFeedback||expanded?355:0,0);
        _panel.Visible=(hasPet||showResident||showFeedback||canShowGuide)&&_management?.Visible!=true;
        RefreshTownFieldGuide(canShowGuide);
        if (resize) _panel.ResetSize();
        if (pet == null)
        {
            _scenePetFeed.Disabled = true;
            return;
        }

        bool needsTreatment = Game.NeedsPetTreatment(pet), inHospital=Game.IsPetInHospital(pet);
        string clinic = inHospital ? " · 住院中" : needsTreatment ? " · 待送医" : "";
        _scenePetName.Text = $"{NativeGame.S(pet, "name")} · {NativeGame.S(pet, "species")}{clinic}";
        foreach (var (label, bar, caption, key, tint) in _scenePetNeeds)
        {
            double value = Math.Clamp(NativeGame.D(pet, key),0,100);
            bool warning = value <= 25 || key == "health" && (value < 60 || needsTreatment);
            label.Text = $"{(warning?"! ":"")}{caption} {value:0}";
            Color ink = warning ? new Color(TownTheme.HighContrast?"641d24":"8b4244") : TownTheme.HighContrast?TownTheme.Ink:tint;
            if (label.GetThemeColor("font_color") != ink) label.AddThemeColorOverride("font_color", ink);
            bar.Value=value;
            if(bar.GetThemeStylebox("fill") is not StyleBoxFlat fill||fill.BgColor!=ink)
                bar.AddThemeStyleboxOverride("fill",TownTheme.Box(ink.ToHtml(),3,0));
        }
        string reason = PetCareBlockReason(pet, "feed");
        _scenePetFeed.Disabled = reason.Length > 0;
        _scenePetFeed.TooltipText = reason.Length == 0 ? "消耗 1 份普通饲料" : reason;
        string play=PetCareBlockReason(pet,"play"), collect=PetCareBlockReason(pet,"collect");
        string hospital=inHospital?"已在医院":needsTreatment?PetCareBlockReason(pet,"hospitalize"):"";
        _scenePetHospital.Visible=needsTreatment||inHospital;
        SetCare(_scenePetPlay,play);
        SetCare(_scenePetCollect,collect);
        SetCare(_scenePetHospital,hospital);
        string hints=string.Join(" · ",new[]{reason,play,collect,hospital}.Where(value=>value.Length>0));
        if(_scenePetActionReasons.Text!=hints){
            _scenePetActionReasons.Text=hints;
            _scenePetActionReasons.Visible=hints.Length>0;
            _panel.ResetSize();
        }

        static void SetCare(Button button,string blocked){button.Disabled=blocked.Length>0;button.TooltipText=blocked;}
    }

    void FeedSelected()=>RunScenePetCare("feed");

    void RunScenePetCare(string action)
    {
        if (_placementAction.Length > 0 || _management?.Visible == true) return;
        if (SelectedScenePet() is not JsonNode pet)
            _selection = "请先选中一只在庄园里的宠物";
        else if(action=="hospitalize"&&(Game.IsPetInHospital(pet)||!Game.NeedsPetTreatment(pet)))
            _selection=Game.IsPetInHospital(pet)?"这位伙伴已经住院":"这位伙伴目前无需住院";
        else if (PetCareBlockReason(pet, action) is { Length: > 0 } reason)
            _selection = reason;
        else
            Act(action, new JsonObject { ["pet_id"] = NativeGame.S(pet, "pet_id"), ["item"] = "普通饲料" });
        RefreshScenePetCare();
    }
}
