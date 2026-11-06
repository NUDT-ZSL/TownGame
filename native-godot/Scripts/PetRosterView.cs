using Godot;
using System;
using System.Linq;
using System.Text.Json.Nodes;

public partial class Main
{
    enum PetRosterFilter { All, InManor, NeedsTreatment, InHospital, Fostered, Departed }
    enum PetRosterSort { CarePriority, Name }
    const int PetRosterPageSize = 3;

    string _petRosterQuery = "";
    PetRosterFilter _petRosterFilter;
    PetRosterSort _petRosterSort;
    int _petRosterPage;

    void AddPetRoster(Control parent, string currentId)
    {
        var body = MarketCard(parent, color: "f4ede6");
        var heading = PetPageRow();
        body.AddChild(heading);
        MarketLabel(heading, "伙伴名册", 17, TownTheme.Accent("宠物").Darkened(.16f))
            .SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        var count = MarketLabel(heading, "", 14, TownTheme.Muted);
        count.Name = "PetRosterStatus";

        var searchRow = new HFlowContainer();
        searchRow.AddThemeConstantOverride("h_separation", 8);
        searchRow.AddThemeConstantOverride("v_separation", 6);
        body.AddChild(searchRow);
        var search = new LineEdit
        {
            Name = "PetRosterSearch", Text = _petRosterQuery, PlaceholderText = "搜索名字或物种",
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, CustomMinimumSize = new(126, 34),
            ClearButtonEnabled = true,
        };
        searchRow.AddChild(search);
        var filter = new OptionButton { Name = "PetRosterFilter", TooltipText = "按伙伴状态筛选" };
        TownTheme.AccentButton(filter, "宠物", 6);
        foreach (string label in new[] { "全部状态", "在园", "伤病", "住院", "寄养", "已离园" })
            filter.AddItem(label);
        filter.Select((int)_petRosterFilter);
        searchRow.AddChild(filter);
        var sort = MarketOptions(searchRow, new[] { "照护优先", "按名字" }, (int)_petRosterSort);
        sort.Name = "PetRosterSort";
        TownTheme.AccentButton(sort, "宠物", 6);

        var picker = new OptionButton
        {
            Name = "PetRosterPicker", FitToLongestItem = false,
            TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
            CustomMinimumSize = new(0, 34),
        };
        TownTheme.AccentButton(picker, "宠物", 6);
        body.AddChild(picker);
        var pinned = MarketLabel(body, "", 14, TownTheme.Muted, wrap: true);
        pinned.Name = "PetRosterPinned";
        var shortlist = new VBoxContainer();
        body.AddChild(shortlist);
        var cards = Enumerable.Range(0, PetRosterPageSize).Select(index =>
        {
            var row = MarketCard(shortlist, color: "fbf7ef");
            var line = new HFlowContainer();
            line.AddThemeConstantOverride("h_separation", 8);
            row.AddChild(line);
            var name = MarketLabel(line, "", 15, TownTheme.Ink, wrap: true);
            name.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            var open = PetPageButton("查看");
            open.Name = $"PetRosterEntry{index}";
            line.AddChild(open);
            var health = MarketLabel(row, "", 14, TownTheme.Muted, wrap: true);
            health.Name = $"PetRosterCondition{index}";
            return (Row: row.GetParent<Control>(), Name: name, Open: open, Health: health);
        }).ToArray();
        var pages = PetPageRow();
        body.AddChild(pages);
        var previous = PetPageButton("上一页");
        pages.AddChild(previous);
        var pageText = MarketLabel(pages, "", 14, TownTheme.Muted);
        var next = PetPageButton("下一页");
        pages.AddChild(next);

        (string Id, string Label)[]? options = null;
        JsonNode[] sorted = Array.Empty<JsonNode>();
        for (int index = 0; index < cards.Length; index++)
        {
            int offset = index;
            cards[index].Open.Pressed += () =>
            {
                int i = _petRosterPage * PetRosterPageSize + offset;
                if (i < sorted.Length) OpenPetManagement(NativeGame.S(sorted[i], "pet_id"));
            };
        }
        void Render()
        {
            var roster = Data.Pets.OfType<JsonNode>().ToArray();
            var current = roster.FirstOrDefault(pet => NativeGame.S(pet, "pet_id") == currentId);
            string query = _petRosterQuery.Trim();
            var filtered = roster.Where(pet => MatchesRoster(pet, query));
            string Name(JsonNode pet) => NativeGame.S(pet, "name");
            sorted = (_petRosterSort == PetRosterSort.CarePriority
                    ? filtered.OrderByDescending(Game.Active).ThenBy(PetRosterUrgency)
                        .ThenBy(pet => NativeGame.D(pet, "health")).ThenBy(Name, StringComparer.CurrentCulture)
                    : filtered.OrderBy(Name, StringComparer.CurrentCulture)).ToArray();
            var matching = sorted
                .Select(pet => (Id: NativeGame.S(pet, "pet_id"),
                    Label: $"{NativeGame.S(pet, "name")} · {NativeGame.S(pet, "species")} · {PetLocationStatus(pet)}"
                        + (Game.Active(pet) && Game.NeedsPetTreatment(pet) ? " · 伤病" : "")))
                .ToArray();

            count.Text = $"匹配 {matching.Length} / {roster.Length} 只";
            int selected = Array.FindIndex(matching, entry => entry.Id == currentId);
            pinned.Visible = current != null && selected < 0;
            pinned.Text = current == null ? "" : $"当前仍在查看「{NativeGame.S(current, "name")}」，它在筛选结果之外。";
            picker.Disabled = matching.Length == 0;
            int totalPages = Math.Max(1, (sorted.Length + PetRosterPageSize - 1) / PetRosterPageSize);
            _petRosterPage = Math.Clamp(_petRosterPage, 0, totalPages - 1);
            pages.Visible = totalPages > 1;
            previous.Disabled = _petRosterPage == 0;
            next.Disabled = _petRosterPage == totalPages - 1;
            pageText.Text = $"{_petRosterPage + 1} / {totalPages}";
            for (int offset = 0; offset < cards.Length; offset++)
            {
                var card = cards[offset];
                int i = _petRosterPage * PetRosterPageSize + offset;
                card.Row.Visible = i < sorted.Length;
                if (!card.Row.Visible) continue;
                var pet = sorted[i];
                card.Name.Text = matching[i].Label;
                card.Open.Text = matching[i].Id == currentId ? "正在查看" : "查看";
                card.Open.Disabled = matching[i].Id == currentId;
                card.Health.Text = PetRosterCondition(pet);
            }
            if (options != null && options.SequenceEqual(matching)) return;
            options = matching;
            picker.Clear();
            foreach (var entry in options)
            {
                picker.AddItem(entry.Label);
                picker.SetItemMetadata(picker.ItemCount - 1, entry.Id);
            }
            picker.Select(selected);
            if (selected < 0) picker.Text = matching.Length == 0 ? "未找到符合条件的伙伴" : "从结果中选择伙伴";
        }

        search.TextChanged += value => { _petRosterQuery = value; _petRosterPage = 0; Render(); };
        search.TextSubmitted += _ =>
        {
            if (options is { Length: 1 } && options[0].Id != currentId)
                OpenPetManagement(options[0].Id);
        };
        filter.ItemSelected += index => { _petRosterFilter = (PetRosterFilter)index; _petRosterPage = 0; Render(); };
        sort.ItemSelected += index => { _petRosterSort = (PetRosterSort)index; _petRosterPage = 0; Render(); };
        previous.Pressed += () => { _petRosterPage--; Render(); };
        next.Pressed += () => { _petRosterPage++; Render(); };
        picker.ItemSelected += index => OpenPetManagement(picker.GetItemMetadata((int)index).AsString());
        Render();
        _refreshManagementPage += Render;
    }

    int PetRosterUrgency(JsonNode pet)
    {
        if (!Game.Active(pet) || NativeGame.B(pet, "fostered") || Game.IsPetInHospital(pet)) return 3;
        return Game.PetCarePriority(pet) ?? 2;
    }

    string PetRosterCondition(JsonNode pet)
    {
        if (!Game.Active(pet) || NativeGame.B(pet, "fostered")) return PetLocationStatus(pet);
        string reason = PetRosterUrgency(pet) switch
        {
            0 => Game.NeedsPetTreatment(pet) ? "优先诊疗" : "健康偏低",
            1 => NativeGame.D(pet, "hunger") <= 45 ? "饱食偏低" : "清洁偏低",
            3 => "住院照护中",
            _ => "状态平稳",
        };
        return $"{reason} · 健康 {NativeGame.D(pet, "health"):0} · 饱食 {NativeGame.D(pet, "hunger"):0} · 清洁 {NativeGame.D(pet, "cleanliness"):0}";
    }

    bool MatchesRoster(JsonNode pet, string query)
    {
        if (query.Length > 0 &&
            !NativeGame.S(pet, "name").Contains(query, StringComparison.OrdinalIgnoreCase) &&
            !NativeGame.S(pet, "species").Contains(query, StringComparison.OrdinalIgnoreCase))
            return false;
        bool active = Game.Active(pet);
        return _petRosterFilter switch
        {
            PetRosterFilter.InManor => active && !NativeGame.B(pet, "fostered") && !Game.IsPetInHospital(pet),
            PetRosterFilter.NeedsTreatment => active && Game.NeedsPetTreatment(pet),
            PetRosterFilter.InHospital => active && Game.IsPetInHospital(pet),
            PetRosterFilter.Fostered => active && NativeGame.B(pet, "fostered"),
            PetRosterFilter.Departed => !active,
            _ => true,
        };
    }
}
