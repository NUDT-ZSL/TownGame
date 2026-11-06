using Godot;
using System;
using System.Linq;
using System.Text.Json.Nodes;

public partial class Main
{
    sealed record CareSupplyRoute(string PetId, string Action);

    static readonly (string Item, string Key)[] FeedingSupplies = { ("普通饲料", "Feed") };
    static readonly (string Item, string Key)[] BathSupplies = { ("清水", "Water"), ("沐浴露", "Soap") };
    static readonly (string Item, string Key)[] TreatmentSupplies = { ("药品", "Medicine") };
    CareSupplyRoute? _careSupplyRoute;

    static (string Item, string Key)[] SuppliesForCare(string action) => action switch
    {
        "feed" => FeedingSupplies,
        "bath" => BathSupplies,
        "treat" => TreatmentSupplies,
        _ => Array.Empty<(string, string)>(),
    };

    JsonNode? FindCareSupplyPet(string id) => Data.Pets.OfType<JsonNode>()
        .FirstOrDefault(pet => NativeGame.S(pet, "pet_id") == id);

    string CareSupplyBlockReason(JsonNode? pet, string action)
    {
        if (pet == null || !Game.Active(pet)) return "原伙伴已离园，可返回查看档案。";
        if (action == "treat")
        {
            if (!Game.NeedsPetTreatment(pet)) return "这位伙伴目前无需治疗。";
            if (!Game.IsPetInHospital(pet)) return "这位伙伴目前未住院，可返回查看状态。";
        }
        return PetCareBlockReason(pet, action, checkSupplies: false);
    }

    Action AddPetCareSupplyShortcut(Container group, string id, Func<JsonNode> currentPet,
        Func<(string Label, string Action, bool Visible)> currentAction)
    {
        var button = PetPageButton("购买缺少物资", accent: "商店");
        group.AddChild(button);
        bool CanOpen(string action, bool visible) => visible
            && SuppliesForCare(action).Any(supply => Game.Stock(supply.Item) < 1 && Game.BuyPrice(supply.Item) > 0)
            && CareSupplyBlockReason(currentPet(), action).Length == 0;

        void Update()
        {
            var (_, action, visible) = currentAction();
            button.Name = $"PetCareSupply_{action}";
            button.Text = action == "treat" ? "补购药品" : "购买缺少物资";
            button.Visible = CanOpen(action, visible);
        }

        button.Pressed += () =>
        {
            var (_, action, visible) = currentAction();
            if (!CanOpen(action, visible))
            {
                Update();
                return;
            }
            _careSupplyRoute = new CareSupplyRoute(id, action);
            ShowManagement("商店");
        };
        return Update;
    }

    void CareSupplyControls(CareSupplyRoute route)
    {
        string action = route.Action;
        string title = action switch { "feed" => "喂食", "bath" => "洗澡", _ => "治疗" };
        var card = MarketCard(_managementBody!, color: "e9f1e8");
        _managementFocusTarget = card;
        MarketLabel(card, $"照护补给 · {title}", 19, TownTheme.Accent("照护"));
        var status = MarketLabel(card, "", color: TownTheme.Muted, wrap: true);
        status.Name = "CareSupplyStatus";
        var supplies = SuppliesForCare(action);
        var checklist = new VBoxContainer { Name = "CareSupplyChecklist" };
        card.AddChild(checklist);
        var counts = supplies.Select(supply => (supply.Item, Label: MarketLabel(checklist, "", 14, TownTheme.Ink, wrap: true))).ToArray();
        var estimate = MarketLabel(card, "", 14, TownTheme.Muted, wrap: true);
        estimate.Name = "CareSupplyEstimate";
        var buttons = new HFlowContainer();
        buttons.AddThemeConstantOverride("h_separation", 8);
        card.AddChild(buttons);
        var back = MarketButton(buttons, "返回伙伴照护", () => OpenPetManagement(route.PetId, $"care:{action}"),
            expand: false, accent: "照护");
        back.Name = "CareSupplyReturn";
        MarketButton(buttons, "浏览全部物资", () =>
        {
            _careSupplyRoute = null;
            _marketSection = 1;
            ShowManagement("商店");
        }, expand: false);

        string CurrentBlockReason() => CareSupplyBlockReason(FindCareSupplyPet(route.PetId), action);
        void UpdateStatus()
        {
            var pet = FindCareSupplyPet(route.PetId);
            string name = pet == null ? "原伙伴" : NativeGame.S(pet, "name");
            string unavailable = CareSupplyBlockReason(pet, action);
            long cost = 0;
            int missingCount = 0;
            bool unlisted = false;
            foreach (var (item, label) in counts)
            {
                var stock = Game.InventoryAvailability(item);
                bool missing = stock.Available < 1;
                label.Text = $"{(missing ? "缺少" : "齐备")} · {item}需 1 份 · 可用 {stock.Available} · 已预留 {stock.Reserved}";
                label.AddThemeColorOverride("font_color", missing ? TownTheme.Accent("事件").Darkened(.25f) : TownTheme.Ink);
                if (!missing) continue;
                int price = Game.BuyPrice(item);
                cost += price;
                missingCount++;
                unlisted |= price == 0;
            }
            bool canResume = unavailable.Length == 0 && missingCount == 0;
            if (unavailable.Length == 0)
            {
                var missing = supplies.Where(supply => Game.Stock(supply.Item) < 1).Select(supply => supply.Item).ToArray();
                unavailable = missing.Length == 0 ? "物资已备齐，可返回照护伙伴。" : $"还缺 {string.Join("、", missing)}，每种需要 1 份。";
            }
            string location = pet == null ? "" : $" · {PetLocationStatus(pet)}";
            status.Text = $"{name}{location} · {unavailable}";
            string treatment = action == "treat" && pet != null && Game.Active(pet) && Game.IsPetInHospital(pet)
                && Game.NeedsPetTreatment(pet) ? $" · 另需治疗费 {PetTreatmentCost(pet)} 金币" : "";
            estimate.Text = missingCount == 0 ? "补给已备齐" + treatment : unlisted
                ? $"有 {missingCount} 种物资待补齐 · 部分物资不在售{treatment}"
                : $"补齐 {missingCount} 种物资预计 {cost} 金币{treatment}";
            back.Text = canResume ? $"返回{title}" : "返回伙伴照护";
        }
        UpdateStatus();
        _refreshManagementPage += UpdateStatus;

        foreach (var (item, key) in supplies)
        {
            string ItemBlockReason()
            {
                string unavailable = CurrentBlockReason();
                if (unavailable.Length > 0) return unavailable;
                return Game.Stock(item) >= 1 ? "此物资已备齐。" : "";
            }
            var purchase = SupplyPurchase(item, 1, ItemBlockReason, $"CareSupply{key}");
            void UpdateVisibility() => purchase.Visible = CurrentBlockReason().Length == 0 && Game.Stock(item) < 1;
            UpdateVisibility();
            _refreshManagementPage += UpdateVisibility;
        }
    }
}
