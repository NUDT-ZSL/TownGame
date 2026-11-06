using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;

public sealed partial class NativeGame
{
    internal readonly record struct AchievementProgress(string Key, double Current, int Target, bool Earned)
    {
        public bool MeetsCondition => Current >= Target;
    }

    internal HashSet<string> KnownGrowthSpecies() =>
        State["codex"]!.AsObject().Where(entry => entry.Value != null)
            .Select(entry => entry.Key).ToHashSet();

    internal IEnumerable<AchievementProgress> GrowthAchievements()
    {
        var stats = Meta["stats"]!;
        var pets = Pets.OfType<JsonNode>().ToArray();
        var known = KnownGrowthSpecies();
        var classic = Rules["CLASSIC_SPECIES"]!.AsArray();
        var species = Rules["SPECIES"]!.AsObject();
        var achieved = State["achievements"]!.AsObject();

        AchievementProgress Goal(string key, double current, int target) =>
            new(key, current, target, achieved[key] != null);

        yield return Goal("first_breed", I(stats, "breeds"), 1);
        yield return Goal("rare", pets.Count(pet => B(pet, "rare")), 1);
        yield return Goal("collector", classic.Count(entry => known.Contains(entry!.GetValue<string>())), classic.Count);
        yield return Goal("all_species", species.Count(entry => known.Contains(entry.Key)), species.Count);
        yield return Goal("old_friend", pets.Count(pet => S(pet, "death_reason") == "寿终正寝"), 1);
        yield return Goal("builder", Rows("buildings").Count(building => S(building!, "kind") != ""), 6);
        yield return Goal("orders", I(stats, "orders"), 3);
        yield return Goal("friends", pets.Select(pet => D(pet, "favor")).DefaultIfEmpty().Max(), 80);
        yield return Goal("seller", I(stats, "sold"), 3);
    }
}
