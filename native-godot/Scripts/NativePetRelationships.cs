using System;
using System.Linq;
using System.Text.Json.Nodes;

public sealed partial class NativeGame
{
    const string PetRelationshipsKey = "native_pet_relationships";
    internal enum CompanionActivity { Play, Walk, Rest }
    internal readonly record struct PetRelationship(string OtherId, int Encounters, int Play, int Walk, int Rest,
        double LastAt, CompanionActivity LastActivity)
    {
        public string Familiarity => Encounters >= 7 ? "亲密伙伴" : Encounters >= 3 ? "熟悉伙伴" : "刚认识";
    }

    internal static string CompanionActivityName(CompanionActivity activity) => activity switch
    {
        CompanionActivity.Play => "共同玩耍",
        CompanionActivity.Walk => "结伴散步",
        _ => "一起休息"
    };

    static string PetRelationshipId(string first, string second) =>
        string.CompareOrdinal(first, second) < 0 ? first + "|" + second : second + "|" + first;

    internal PetRelationship[] PetRelationships(string id)
    {
        if (Meta[PetRelationshipsKey] is not JsonObject relations) return Array.Empty<PetRelationship>();
        return relations.Select(entry => entry.Value).OfType<JsonNode>()
            .Where(row => S(row, "first") == id || S(row, "second") == id)
            .Select(row => new PetRelationship(S(row, "first") == id ? S(row, "second") : S(row, "first"),
                I(row, "encounters"), I(row, "play"), I(row, "walk"), I(row, "rest"), D(row, "last_ts"),
                (CompanionActivity)I(row, "last_activity")))
            .OrderByDescending(row => row.Encounters).ThenByDescending(row => row.LastAt).ToArray();
    }

    internal int Familiarity(string first, string second) =>
        Meta[PetRelationshipsKey] is JsonObject relations
        && relations[PetRelationshipId(first, second)] is JsonNode row ? I(row, "encounters") : 0;

    internal void RecordCompanionActivity(string first, string second, CompanionActivity activity)
    {
        if (first.Length == 0 || second.Length == 0 || first == second) return;
        if (Pets.OfType<JsonNode>().FirstOrDefault(p => S(p, "pet_id") == first) is not { } firstPet
            || Pets.OfType<JsonNode>().FirstOrDefault(p => S(p, "pet_id") == second) is not { } secondPet
            || !Active(firstPet) || !Active(secondPet) || B(firstPet, "fostered") || B(secondPet, "fostered")
            || IsPetInHospital(firstPet) || IsPetInHospital(secondPet)) return;
        var relations = (Meta[PetRelationshipsKey] ??= new JsonObject()).AsObject();
        string key = PetRelationshipId(first, second);
        var row = (relations[key] ??= new JsonObject { ["first"] = first, ["second"] = second }).AsObject();
        string counter = activity switch { CompanionActivity.Play => "play", CompanionActivity.Walk => "walk", _ => "rest" };
        row[counter] = I(row, counter) + 1;
        row["encounters"] = I(row, "encounters") + 1;
        row["last_ts"] = Now;
        row["last_activity"] = (int)activity;
    }
}
