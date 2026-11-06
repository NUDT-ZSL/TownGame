using System;
using System.Linq;
using System.Text.Json.Nodes;

public sealed partial class NativeGame
{
    internal const string OfflineLimitKey = "native_offline_limit_hours";
    internal static readonly int[] OfflineLimits = { 0, 1, 4, 8 };
    const int DefaultOfflineLimit = 4;

    internal int OfflineLimit => Meta[OfflineLimitKey] is JsonValue value
        && value.TryGetValue<int>(out int hours) && OfflineLimits.Contains(hours)
            ? hours
            : DefaultOfflineLimit;

    internal sealed record OfflineSettlement(
        double Elapsed, double Advanced, bool Paused, int CoinChange, int StockChange,
        int Arrivals, int Departures, int CareNeeded);

    internal OfflineSettlement? SettleOffline(long now)
    {
        double anchor = D(Meta, "native_real_anchor", now);
        double elapsed = anchor > 0 ? Math.Max(0, now - anchor) : 0;
        if (elapsed == 0)
        {
            Meta["native_real_anchor"] = now;
            return null;
        }

        double advanced = Math.Min(elapsed, OfflineLimit * Hour);
        int coins = I(Meta, "coins");
        int InventoryCount() => Rows("inventory").Sum(batch => I(batch!, "quantity"));
        var initialPets = Pets.OfType<JsonNode>().Where(Active)
            .Select(p => S(p, "pet_id")).ToHashSet();
        int stock = InventoryCount();

        if (advanced > 0)
            Advance(advanced);
        Meta["native_real_anchor"] = now;

        var pets = Pets.OfType<JsonNode>().Where(Active).ToArray();
        var currentPets = pets.Select(p => S(p, "pet_id")).ToHashSet();
        int care = pets.Count(p => PetCarePriority(p).HasValue);
        return new OfflineSettlement(elapsed, advanced, advanced == 0,
            I(Meta, "coins") - coins, InventoryCount() - stock,
            currentPets.Count(id => !initialPets.Contains(id)),
            initialPets.Count(id => !currentPets.Contains(id)), care);
    }

    internal int? PetCarePriority(JsonNode pet)
    {
        if (!Active(pet) || B(pet, "fostered")) return null;
        if (Disease(pet).Length > 0 || D(pet, "injury") > 0 || D(pet, "health") < 60) return 0;
        if (D(pet, "hunger") <= 45 || D(pet, "cleanliness") <= 40) return 1;
        return null;
    }
}
