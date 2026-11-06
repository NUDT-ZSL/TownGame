using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;

public sealed partial class NativeGame
{
    internal readonly record struct PetMoveQuote(string PreferredHome, string Kind, int Free, int Required,
        int Fee, bool FitsSpecies, string Reason);

    internal PetMoveQuote QuotePetMove(JsonNode pet, JsonNode home)
    {
        string preferred = S(Spec("SPECIES", S(pet, "species")), "building"), kind = S(home, "kind");
        int homeId = I(home, "building_id"), free = Math.Max(0, Slots(home) - Used(homeId));
        int required = D(pet, "pregnancy_due_ts") > 0 ? 2 : 1;
        int fee = kind == "寄养屋" ? I(Rules, "FOSTER_ENTER_COST", 40) : 0;
        string reason = !Active(pet) ? "伙伴已不在庄园"
            : I(pet, "building_id") == homeId ? "已经住在此处"
            : kind.Length == 0 || free < required ? "居所已满或没有足够的预留位"
            : I(Meta, "coins") < fee ? "金币不足" : "";
        return new(preferred, kind, free, required, fee,
            kind == preferred || kind is "寄养屋" or "宠物医院" or "游乐区", reason);
    }

    internal string PairBlockReason(JsonNode first, JsonNode second)
    {
        if (!Active(first) || !Active(second)) return "伙伴已不在庄园";
        if (B(first, "fostered") || B(second, "fostered")) return "请先接出寄养宠物";
        if (S(first, "pet_id") == S(second, "pet_id") || S(first, "species") != S(second, "species")
            || S(first, "sex") == S(second, "sex") || Stage(first) != "成年" || Stage(second) != "成年")
            return "伴侣需为同品种异性成年伙伴";
        if (S(first, "mate_id") is { Length: > 0 } left && left != S(second, "pet_id")
            || S(second, "mate_id") is { Length: > 0 } right && right != S(first, "pet_id"))
            return "已有其他伴侣";
        return "";
    }

    internal sealed record BreedCheck(string Caption, string Reason);

    internal BreedCheck[] CheckBreeding(JsonNode first, JsonNode second)
    {
        var checks = new List<BreedCheck>();
        void Add(string caption, bool passes, string reason) => checks.Add(new(caption, passes ? "" : reason));
        bool sameSpecies = S(first, "pet_id") != S(second, "pet_id") && S(first, "species") == S(second, "species")
            && S(first, "sex") != S(second, "sex");
        Add("物种与性别", sameSpecies, "需要同物种的异性伙伴");
        Add("在园状态", Active(first) && Active(second) && !B(first, "fostered") && !B(second, "fostered"), "双方均需在园");
        Add("同居与住院", I(first, "building_id") == I(second, "building_id") && S(Home(first), "kind") != "宠物医院",
            "请先同居，住院期间不能繁育");
        Add("年龄", Stage(first) == "成年" && Stage(second) == "成年", "双方均需成年");
        Add("健康", new[] { first, second }.All(pet => D(pet, "health") >= Rule("BREED_MIN_HEALTH", 50)
            && D(pet, "injury") <= 0 && S(pet, "illness_type") == ""), $"需无伤病且健康 ≥ {Rule("BREED_MIN_HEALTH", 50):0}");
        Add("好感与冷却", new[] { first, second }.All(pet => D(pet, "favor") >= Rule("BREED_FAVOR", 35)
            && D(pet, "breed_ready_ts") <= Now && D(pet, "pregnancy_due_ts") <= 0)
            && !Expecting(first) && !Expecting(second), $"需好感 ≥ {Rule("BREED_FAVOR", 35):0}，且无冷却或孕期");
        bool related = new[] { S(first, "mother_id"), S(first, "father_id") }.Where(id => id != "")
            .Intersect(new[] { S(second, "mother_id"), S(second, "father_id"), S(second, "pet_id") }).Any()
            || S(second, "mother_id") == S(first, "pet_id") || S(second, "father_id") == S(first, "pet_id");
        Add("亲缘", !related, "近亲不能繁育");
        string pair = PairBlockReason(first, second);
        Add("伴侣", pair.Length == 0, pair);
        var mother = S(first, "sex") == "雌" ? first : second;
        int available = Slots(Home(mother)) - Used(I(mother, "building_id"));
        int limit = I(Rules, "PET_LIMIT");
        int used = IncubatingEggCount()+Pets.OfType<JsonNode>().Where(Active).Sum(pet => D(pet, "pregnancy_due_ts") > 0 ? 2 : 1);
        Add($"幼崽预留位 · 空位 {Math.Max(0, available)}", available > 0 && used < limit, "需要预留幼崽空位和人口容量");
        int fee = I(Rules, "BREED_COST", 100);
        Add($"金币 · {fee}", I(Meta, "coins") >= fee, "金币不足");
        foreach (var supply in Rules["BREED_MATERIALS"]!.AsObject())
        {
            int quantity = supply.Value!.GetValue<int>();
            Add($"{supply.Key} · {quantity} 份", Stock(supply.Key) >= quantity, $"{supply.Key}可用库存不足");
        }
        return checks.ToArray();
    }
}
