using Godot;
using System;
using System.Linq;
using System.Text.Json.Nodes;

public partial class Main
{
    bool _petManagementExpanded;

    void OpenPetManagement(string id, string entryId = "")
    {
        if (_selectedPet != id)
            _petManagementExpanded = false;
        _selectedPet = id;
        if (_tab == "宠物" && _managementScroll != null)
            _managementScroll.ScrollVertical = 0;
        _managementScrollPositions["宠物"] = 0;
        SetManagementFeedback("");
        ShowManagement("宠物", entryId);
    }

    void PetControls()
    {
        var roster = Data.Pets.OfType<JsonNode>().OrderByDescending(Game.Active).ToArray();
        if (roster.Length == 0)
        {
            Text("庄园还没有伙伴。可以到商店领养或选购新伙伴。");
            ActionButton("前往商店", () => ShowManagement("商店"));
            return;
        }

        int selectedIndex = Math.Max(0, Array.FindIndex(roster, pet => NativeGame.S(pet, "pet_id") == _selectedPet));
        var pet = roster[selectedIndex];
        string id = NativeGame.S(pet, "pet_id");
        string species = NativeGame.S(pet, "species");
        _selectedPet = id;
        JsonNode CurrentPet() => Data.Pets.OfType<JsonNode>().FirstOrDefault(entry => NativeGame.S(entry, "pet_id") == id) ?? pet;

        AddPetRoster(_managementBody!, id);

        bool active = Game.Active(pet);
        var profile = new PanelContainer();
        profile.AddThemeStyleboxOverride("panel", TownTheme.Box("f6ece7", 12, 10));
        _managementBody!.AddChild(profile);
        var profileRow = PetPageRow();
        profile.AddChild(profileRow);
        profileRow.AddChild(AnimalBadge(species, 162, 120, pet));

        var details = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        details.AddThemeConstantOverride("separation", 5);
        profileRow.AddChild(details);
        var title = new Label { Text = $"{species} · {Game.Stage(pet)} · {NativeGame.S(pet, "sex")}" };
        title.AddThemeFontSizeOverride("font_size", 19);
        details.AddChild(title);
        var status = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
        details.AddChild(status);
        void UpdateProfile()
        {
            var current = CurrentPet();
            bool living = Game.Active(current);
            string illness = NativeGame.S(current, "illness_type");
            if (illness.Length == 0 && NativeGame.S(current, "status") == "生病") illness = "普通感染";
            double injury = NativeGame.D(current, "injury");
            string condition = illness.Length > 0 ? illness : injury > 0 ? "有外伤" : "健康";
            if (injury > 0) condition += $" · 伤势 {injury:0}";
            string location = PetLocationStatus(current);
            title.Text = $"{species} · {Game.Stage(current)} · {NativeGame.S(current, "sex")}";
            string text = living ? $"{location} · {condition}" : location;
            if (status.Text != text)
            {
                status.Text = text;
                status.AddThemeColorOverride("font_color", living && (illness.Length > 0 || injury > 0) ? new Color("8b4244") : TownTheme.Muted);
            }
        }
        UpdateProfile();
        _refreshManagementPage += UpdateProfile;

        if (active)
        {
            var needs = new GridContainer { Columns = 2 };
            needs.AddThemeConstantOverride("h_separation", 12);
            needs.AddThemeConstantOverride("v_separation", 5);
            details.AddChild(needs);
            needs.AddChild(LivePetNeedMeter("饱食", () => NativeGame.D(CurrentPet(), "hunger"), "c3965c"));
            needs.AddChild(LivePetNeedMeter("健康", () => NativeGame.D(CurrentPet(), "health"), "5f9876"));
            needs.AddChild(LivePetNeedMeter("清洁", () => NativeGame.D(CurrentPet(), "cleanliness"), "5b8ca6"));
            needs.AddChild(LivePetNeedMeter("心情", () => NativeGame.D(CurrentPet(), "happy"), "a57595"));
        }

        var links = PetPageRow();
        details.AddChild(links);
        if (active)
        {
            var locate = PetPageButton("场景定位", () =>
            {
                if (!LocatePet(id))
                    SetManagementFeedback("暂时无法定位这位伙伴。", true);
            });
            var follow = PetPageButton("跟随观察", () =>
            {
                if (!StartPetFollow(id)) SetManagementFeedback("暂时无法跟随这位伙伴。", true);
            });
            var appearance = PetPageButton("外观", () => ShowManagement("外观"));
            links.AddChild(locate);
            links.AddChild(follow);
            links.AddChild(appearance);
            void UpdateLinks()
            {
                var current = CurrentPet();
                appearance.Disabled = !Game.Active(current);
                locate.Disabled = follow.Disabled = appearance.Disabled || NativeGame.B(current, "fostered");
            }
            UpdateLinks();
            _refreshManagementPage += UpdateLinks;
        }
        links.AddChild(PetPageButton("档案", () => OpenPetArchive(id)));

        if (!active)
            return;

        var home = Game.Home(pet);

        var care = new VBoxContainer();
        care.AddThemeConstantOverride("separation", 4);
        _managementBody.AddChild(care);
        var careTitle = new Label { Text = "日常照护" };
        careTitle.AddThemeColorOverride("font_color", TownTheme.Accent("照护"));
        care.AddChild(careTitle);
        var careRow = PetPageRow();
        care.AddChild(careRow);
        foreach (var (label, action) in new[] { ("喂普通饲料", "feed"), ("洗澡", "bath"), ("互动", "play"), ("收取产物", "collect") })
            AddCareAction(careRow, () => (label, action, true));
        var juvenile=PetPageButton("幼崽护理 · 清水 1 份",()=>Act("juvenile_care",new JsonObject{{"pet_id",id}}),accent:"照护");
        care.AddChild(juvenile);
        void UpdateJuvenile()
        {
            var current=CurrentPet();
            juvenile.Visible=Game.Stage(current)=="幼崽";
            juvenile.TooltipText=Game.JuvenileCareReason(current);
            juvenile.Disabled=juvenile.TooltipText.Length>0;
        }
        UpdateJuvenile();
        _refreshManagementPage+=UpdateJuvenile;
        AddPetHealthTimeline(CurrentPet);

        var clinic = new PanelContainer { Name = "PetClinic" };
        var clinicStyle = TownTheme.Box("f6ede2", 12, 10);
        clinicStyle.BorderColor = TownTheme.Accent("照护");
        clinicStyle.BorderWidthLeft = 3;
        clinic.AddThemeStyleboxOverride("panel", clinicStyle);
        _managementBody.AddChild(clinic);
        var clinicBody = new VBoxContainer();
        clinicBody.AddThemeConstantOverride("separation", 5);
        clinic.AddChild(clinicBody);
        var clinicHeading = new HFlowContainer();
        clinicHeading.AddThemeConstantOverride("h_separation", 6);
        clinicBody.AddChild(clinicHeading);
        MarketLabel(clinicHeading, "宠物诊疗", 18, TownTheme.Accent("照护").Darkened(.18f)).VerticalAlignment=VerticalAlignment.Center;
        MarketButton(clinicHeading,"查看诊疗台",()=>ShowManagement("照护","clinic-board"),expand:false,accent:"照护");
        var clinicStatus = MarketLabel(clinicBody, "", color: TownTheme.Muted, wrap: true);
        clinicStatus.Name = "PetClinicStatus";
        var medicalRow = PetPageRow();
        clinicBody.AddChild(medicalRow);
        var medicalAction = AddCareAction(medicalRow, () =>
        {
            var current = CurrentPet();
            bool living = Game.Active(current);
            bool needsTreatment = living && Game.NeedsPetTreatment(current);
            return living && Game.IsPetInHospital(current)
                ? ($"治疗 · {PetTreatmentCost(current)} 金币", "treat", needsTreatment)
                : ("送医住院", "hospitalize", needsTreatment);
        }, inline: true);
        medicalAction.Name = "PetClinicAction";
        int? dischargePlot = null;
        var discharge = PetPageButton("办理出院", () =>
        {
            var current = CurrentPet();
            if (!Game.Active(current) || !Game.IsPetInHospital(current) || Game.NeedsPetTreatment(current)) return;
            if (dischargePlot is int plot)
                Act("move", new JsonObject { { "pet_id", id }, { "building_id", plot } });
        }, accent: "照护");
        discharge.Name = "PetClinicDischarge";
        discharge.SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin;
        clinicBody.AddChild(discharge);
        void UpdateClinic()
        {
            var current = CurrentPet();
            bool living = Game.Active(current);
            bool needsTreatment = living && Game.NeedsPetTreatment(current);
            bool inHospital = living && Game.IsPetInHospital(current);
            clinic.Visible = needsTreatment || inHospital;
            if (!clinic.Visible) return;

            discharge.Visible = inHospital && !needsTreatment;
            medicalRow.Visible = needsTreatment;
            if (discharge.Visible)
            {
                var destination = Game.PetDischargeHome(current);
                dischargePlot = destination == null ? null : NativeGame.I(destination, "building_id");
                discharge.Disabled = destination == null;
                clinicStatus.Text = destination == null
                    ? "已无伤病，适配居所暂无空位。可在建造页扩建居所。"
                    : $"已无伤病 · 出院后入住地块 {dischargePlot}（{NativeGame.S(destination, "kind")}）";
            }
            else if (inHospital)
                clinicStatus.Text = $"住院中 · 地块 {NativeGame.I(current, "building_id")} · 治疗消耗药品 1 份（库存 {Game.Stock("药品")}）";
            else
            {
                var admission = Game.PetHospitalAdmission(current);
                clinicStatus.Text = admission.Destination is JsonNode destination
                    ? $"等待就诊 · 空闲床位 {admission.FreeBeds} · 将送往地块 {NativeGame.I(destination, "building_id")}"
                    : admission.HasHospital
                        ? $"当前空闲床位 {admission.FreeBeds}，暂无可接诊的医院。可在建造页升级医院。"
                        : "庄园暂无宠物医院。可在建造页建造医院。";
            }
        }
        UpdateClinic();
        _refreshManagementPage += UpdateClinic;
        if (clinic.Visible && _managementEntryTarget is "clinic" or "care:treat" or "care:hospitalize")
            _managementFocusTarget = clinic;

        Button AddCareAction(HBoxContainer row, Func<(string Label,string Action,bool Visible)> currentAction, bool inline = false)
        {
            Container group = inline ? new HFlowContainer() : new VBoxContainer();
            group.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            group.AddThemeConstantOverride(inline ? "h_separation" : "separation", inline ? 8 : 2);
            row.AddChild(group);
            if (_managementEntryTarget == $"care:{currentAction().Action}") _managementFocusTarget = group;
            var button = PetPageButton("", () => Act(currentAction().Action, new JsonObject { { "pet_id", id } }), accent: "照护");
            group.AddChild(button);
            var note = new Label
            {
                AutowrapMode = inline ? TextServer.AutowrapMode.Off : TextServer.AutowrapMode.WordSmart,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
            note.AddThemeFontSizeOverride("font_size", 13);
            note.AddThemeColorOverride("font_color", TownTheme.Muted);
            group.AddChild(note);
            var updateSupply = AddPetCareSupplyShortcut(group, id, CurrentPet, currentAction);
            void UpdateCareAction()
            {
                var (label, action, visible) = currentAction();
                group.Visible = visible;
                string reason = visible ? PetCareBlockReason(CurrentPet(), action) : "";
                button.Text = label;
                button.Disabled = reason.Length > 0;
                button.TooltipText = reason;
                note.Text = reason;
                note.Visible = reason.Length > 0;
                updateSupply();
            }
            UpdateCareAction();
            _refreshManagementPage += UpdateCareAction;
            return button;
        }

        var more = PetPageButton(_petManagementExpanded ? "收起管理" : "更多管理 · 改名、居所和伴侣");
        more.ToggleMode = true;
        more.Alignment = HorizontalAlignment.Left;
        more.SetPressedNoSignal(_petManagementExpanded);
        _managementBody.AddChild(more);
        if (_managementEntryTarget == "pet-management") _managementFocusTarget = more;
        var advanced = new VBoxContainer { Visible = _petManagementExpanded };
        advanced.AddThemeConstantOverride("separation", 7);
        _managementBody.AddChild(advanced);
        more.Toggled += open =>
        {
            _petManagementExpanded = open;
            more.Text = open ? "收起管理" : "更多管理 · 改名、居所和伴侣";
            advanced.Visible = open;
        };
        PetAdvancedControls(advanced, pet, home);
        _refreshManagementPage += () =>
        {
            more.Visible = Game.Active(CurrentPet());
            advanced.Visible = more.Visible && _petManagementExpanded;
        };

        AddPetRelationships(_managementBody!, id, archive: false);
        var otherPets = roster.Where(entry => entry != pet && Game.Active(entry)).Take(3).ToArray();
        if (otherPets.Length == 0)
            return;

        Section("其他伙伴", "点击图卡下方切换");
        var companions = PetPageRow();
        _managementBody.AddChild(companions);
        foreach (var companion in otherPets)
        {
            var card = new VBoxContainer();
            companions.AddChild(card);
            card.AddChild(AnimalBadge(NativeGame.S(companion, "species"), 124, 75, companion));
            string companionId = NativeGame.S(companion, "pet_id");
            card.AddChild(PetPageButton($"查看 · {PetLocationStatus(companion)}", () => OpenPetManagement(companionId)));
        }
    }

    void PetAdvancedControls(VBoxContainer advanced, JsonNode pet, JsonObject currentHome)
    {
        string id = NativeGame.S(pet, "pet_id");
        string name = NativeGame.S(pet, "name");
        var renameRow = PetPageRow();
        advanced.AddChild(renameRow);
        var rename = new LineEdit
        {
            Text = name,
            PlaceholderText = "伙伴名字",
            MaxLength = NativeGame.I(Game.Rules, "RENAME_MAX", 16),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        renameRow.AddChild(rename);
        renameRow.AddChild(PetPageButton("保存名字", () => Act("rename", new JsonObject { { "pet_id", id }, { "name", rename.Text } })));

        advanced.AddChild(new Label { Text = $"当前居所 · {NativeGame.S(currentHome, "kind")}（地块 {NativeGame.I(currentHome, "building_id")}）" });
        AddPetMovePreflight(advanced, pet);
        AddPetBreedingPreflight(advanced, pet);

        string saleUnavailable = NativeGame.B(pet, "fostered") ? "请先接出寄养伙伴"
            : Game.Rows("buildings").Any(building => NativeGame.S(building!, "kind") == "商店") ? "" : "需要商店";
        var sale = PetPageButton("出售给新家庭", () =>
        {
            int quote = Game.PetPrice(pet);
            ConfirmManagementAction("出售伙伴", $"将「{name}」送往新家庭，预计获得 {quote} 金币。出售后可在档案中查看它的记录。",
                "确认出售", () => Act("sell_pet", new JsonObject { { "pet_id", id } }));
        }, saleUnavailable, "事件");
        sale.SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin;
        advanced.AddChild(sale);
    }

    string PetLocationStatus(JsonNode pet)
    {
        if (Game.Active(pet))
            return NativeGame.B(pet, "fostered") ? "寄养中" : Game.IsPetInHospital(pet) ? "住院中" : "在园";
        return NativeGame.D(pet, "death_ts") > 0 ? "已离世" : NativeGame.S(pet, "departure", "已离园");
    }

    int PetTreatmentCost(JsonNode pet) => Math.Max(0, NativeGame.I(Game.Rules, "TREATMENT_FEE")
        - NativeGame.I(Game.Home(pet), "level") * NativeGame.I(Game.Rules, "HOSPITAL_FEE_REDUCTION"));

    string PetCareBlockReason(JsonNode pet, string action, bool checkSupplies = true)
    {
        if (!Game.Active(pet)) return "伙伴已不在庄园";
        if (NativeGame.B(pet, "fostered")) return "请先接出寄养伙伴";
        return action switch
        {
            "feed" when checkSupplies && Game.Stock("普通饲料") < 1 => "普通饲料不足",
            "bath" when checkSupplies && (Game.Stock("清水") < 1 || Game.Stock("沐浴露") < 1) => "需清水和沐浴露各1份",
            "play" when NativeGame.D(pet, "interact_ready_ts") > Game.Now => "互动冷却中",
            "collect" when NativeGame.D(pet, "product_ready_ts") == 0
                || NativeGame.D(pet, "product_expiry_ts") <= Game.Now => "暂无可收取产物",
            "collect" when Game.Rows("inventory").Sum(item => NativeGame.I(item!, "quantity"))
                + (NativeGame.B(pet, "rare") ? 2 : 1) > Game.Storage => "仓库空间不足",
            "hospitalize" when Game.PetHospitalAdmission(pet).Destination == null => "需要有空床的宠物医院",
            "treat" when NativeGame.D(pet, "treatment_ready_ts") > Game.Now => "治疗恢复期中",
            "treat" when NativeGame.I(Game.Meta, "coins") < PetTreatmentCost(pet) => "治疗金币不足",
            "treat" when checkSupplies && Game.Stock("药品") < 1 => "药品不足",
            _ => "",
        };
    }

    static HBoxContainer PetPageRow()
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 8);
        return row;
    }

    static Button PetPageButton(string label, Action? action = null, string reason = "", string accent = "宠物")
    {
        var button = new Button
        {
            Text = label,
            TooltipText = reason,
            Disabled = reason.Length > 0,
            CustomMinimumSize = new(0, 34),
        };
        TownTheme.AccentButton(button, accent, 6);
        if (action != null)
            button.Pressed += action;
        return button;
    }

    VBoxContainer LivePetNeedMeter(string title, Func<double> value, string color)
    {
        var meter = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        meter.AddThemeConstantOverride("separation", 2);
        var label = new Label();
        label.AddThemeFontSizeOverride("font_size", 14);
        meter.AddChild(label);
        var bar = new ProgressBar { Step = 0, ShowPercentage = false, CustomMinimumSize = new(0, 6) };
        bar.AddThemeStyleboxOverride("background", TownTheme.Box("e9dfda", 3, 0));
        meter.AddChild(bar);
        bool? low = null;
        void UpdateMeter()
        {
            double current = value();
            label.Text = $"{title} {current:0}";
            bar.Value = current;
            if (low == (current <= 25)) return;
            low = current <= 25;
            label.AddThemeColorOverride("font_color", low.Value ? new Color("8b4244") : TownTheme.Ink);
            bar.AddThemeStyleboxOverride("fill", TownTheme.Box(low.Value ? "b85e5d" : color, 3, 0));
        }
        UpdateMeter();
        _refreshManagementPage += UpdateMeter;
        return meter;
    }
}
