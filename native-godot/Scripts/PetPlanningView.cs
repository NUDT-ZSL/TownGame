using Godot;
using System;
using System.Linq;
using System.Text.Json.Nodes;

public partial class Main
{
    void AddPetMovePreflight(VBoxContainer parent, JsonNode pet)
    {
        string id = NativeGame.S(pet, "pet_id");
        int currentHome = NativeGame.I(pet, "building_id");
        var homes = Game.Rows("buildings").OfType<JsonNode>()
            .Where(home => NativeGame.S(home, "kind").Length > 0 && NativeGame.I(home, "building_id") != currentHome)
            .Select(home => NativeGame.I(home, "building_id")).ToArray();
        if (homes.Length == 0)
        {
            MarketLabel(parent, "暂无其他已建成居所。", color: TownTheme.Muted);
            return;
        }

        var card = MarketCard(parent, color: "f2efe4");
        MarketLabel(card, "迁居预检", 17, TownTheme.Leaf);
        var row = PetPageRow();
        card.AddChild(row);
        var picker = new OptionButton { Name = "PetMoveDestination", FitToLongestItem = false,
            TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        TownTheme.AccentButton(picker, "宠物", 6);
        row.AddChild(picker);
        foreach (int home in homes) picker.AddItem($"地块 {home}");
        picker.Select(0);
        var move = PetPageButton("迁入所选居所", () => Act("move",
            new JsonObject { { "pet_id", id }, { "building_id", homes[picker.Selected] } }));
        move.Name = "PetMoveConfirm";
        row.AddChild(move);
        var detail = MarketLabel(card, "", color: TownTheme.Muted, wrap: true);
        detail.Name = "PetMovePreflight";

        void Refresh()
        {
            var current = Data.FindPet(id) ?? pet;
            var buildings = Game.Rows("buildings").OfType<JsonNode>().ToDictionary(home => NativeGame.I(home, "building_id"));
            for (int i = 0; i < homes.Length; i++)
            {
                if (!buildings.TryGetValue(homes[i], out var home))
                {
                    picker.SetItemText(i, $"地块 {homes[i]} · 已移除");
                    continue;
                }
                var quote = Game.QuotePetMove(current, home);
                string caption = $"地块 {homes[i]} · {quote.Kind} · 空位 {quote.Free}";
                if (picker.GetItemText(i) != caption) picker.SetItemText(i, caption);
            }
            if (picker.Selected < 0 || !buildings.TryGetValue(homes[picker.Selected], out var selected))
            {
                move.Disabled = true;
                detail.Text = "所选居所已移除。";
                return;
            }
            var plan = Game.QuotePetMove(current, selected);
            string fit = plan.FitsSpecies ? "适配或特殊用途居所" : $"该物种通常适合{plan.PreferredHome}，迁入后照护需求可能增加";
            detail.Text = $"{fit} · 空位 {plan.Free} · 需要 {plan.Required} 位 · 迁居费用 {plan.Fee} 金币"
                + (plan.Reason.Length > 0 ? $"\n{plan.Reason}" : "");
            move.Disabled = plan.Reason.Length > 0;
            move.TooltipText = plan.Reason;
        }
        picker.ItemSelected += _ => Refresh();
        Refresh();
        _refreshManagementPage += Refresh;
    }

    void AddPetBreedingPreflight(VBoxContainer parent, JsonNode pet)
    {
        string id = NativeGame.S(pet, "pet_id"), mateId = NativeGame.S(pet, "mate_id");
        var card = MarketCard(parent, color: "f2ebe6");
        string mate = Data.FindPet(mateId) is JsonNode other ? NativeGame.S(other, "name") : "暂无";
        MarketLabel(card, $"伴侣与繁育 · {mate}", 17, TownTheme.Ink);
        var candidates = Data.Pets.OfType<JsonNode>().Where(other => other != pet && Game.Active(other)
            && NativeGame.S(other, "species") == NativeGame.S(pet, "species")
            && NativeGame.S(other, "sex") != NativeGame.S(pet, "sex"))
            .Select(other => NativeGame.S(other, "pet_id")).ToArray();
        var row = new HFlowContainer();
        row.AddThemeConstantOverride("h_separation", 8);
        card.AddChild(row);
        if (mateId.Length > 0)
            row.AddChild(PetPageButton("解除伴侣", () => Act("unpair", new JsonObject { { "pet_id", id } })));
        if (candidates.Length == 0)
        {
            MarketLabel(card, "暂无同品种的异性伙伴。", color: TownTheme.Muted);
            return;
        }
        var picker = new OptionButton { Name = "PetBreedCandidate", FitToLongestItem = false,
            TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis, CustomMinimumSize = new(140, 0) };
        TownTheme.AccentButton(picker, "宠物", 6);
        foreach (string candidate in candidates) picker.AddItem(NativeGame.S(Data.FindPet(candidate)!, "name"));
        picker.Select(Math.Max(0, Array.IndexOf(candidates, mateId)));
        row.AddChild(picker);
        JsonObject Args() => new() { { "pet_id", id }, { "mate_id", candidates[picker.Selected] } };
        var pair = PetPageButton("结为伴侣", () => Act("pair", Args()));
        row.AddChild(pair);
        var breed = PetPageButton("尝试繁育", () => Act("breed", Args()));
        breed.Name = "PetBreedConfirm";
        row.AddChild(breed);
        var detail = MarketLabel(card, "", color: TownTheme.Muted, wrap: true);
        detail.Name = "PetBreedPreflight";

        void Refresh()
        {
            var first = Data.FindPet(id) ?? pet;
            var second = Data.FindPet(candidates[picker.Selected]);
            if (second == null || !Game.Active(first) || !Game.Active(second))
            {
                pair.Disabled = breed.Disabled = true;
                detail.Text = "所选伙伴已不在庄园。";
                return;
            }
            string pairBlock = Game.PairBlockReason(first, second);
            var checks = Game.CheckBreeding(first, second);
            pair.Disabled = pairBlock.Length > 0;
            pair.TooltipText = pairBlock;
            breed.Disabled = checks.Any(check => check.Reason.Length > 0);
            breed.TooltipText = checks.FirstOrDefault(check => check.Reason.Length > 0)?.Reason ?? "";
            detail.Text = string.Join("\n", checks.Select(check => check.Reason.Length == 0
                ? $"✓ {check.Caption}" : $"待满足 · {check.Caption} · {check.Reason}"));
        }
        picker.ItemSelected += _ => Refresh();
        Refresh();
        _refreshManagementPage += Refresh;
    }
}
