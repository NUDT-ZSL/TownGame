using System;
using System.Linq;
using System.Text.Json.Nodes;

public sealed partial class NativeGame
{
    internal enum PetFacility { Climb, Dig, Splash, Perch, Shelter }
    internal readonly record struct PetFacilityUse(PetFacility Facility,double When);
    const string PetFacilityRecordsKey="native_pet_facilities";

    internal static string PetFacilityName(PetFacility facility)=>facility switch{
        PetFacility.Climb=>"攀爬架",PetFacility.Dig=>"刨土区",PetFacility.Splash=>"戏水池",
        PetFacility.Perch=>"观景栖架",_=>"遮荫窝"
    };

    internal static PetFacility PreferredPetFacility(JsonNode pet)=>S(pet,"species") switch{
        "兔子" or "仓鼠"=>PetFacility.Dig,
        "猫咪"=>PetFacility.Climb,
        "小鸭"=>PetFacility.Splash,
        "鹦鹉" or "孔雀"=>PetFacility.Perch,
        "狐狸" or "小狗" when S(pet,"personality") is "胆小" or "高冷"=>PetFacility.Shelter,
        "狐狸" or "小狗"=>PetFacility.Dig,
        _=>PetFacility.Shelter
    };

    internal static PetFacility PersonalityPetFacility(JsonNode pet)=>S(pet,"personality") switch{
        "胆小" or "高冷"=>PetFacility.Shelter,"活泼"=>PetFacility.Climb,"粘人"=>PetFacility.Splash,
        "吃货"=>PetFacility.Dig,_=>PreferredPetFacility(pet)
    };

    internal PetFacilityUse[] RecentPetFacilityUses(string id)=>Meta[PetFacilityRecordsKey]?[id]?["recent"] is JsonArray visits
        ?visits.OfType<JsonNode>().Select(visit=>new PetFacilityUse((PetFacility)I(visit,"facility"),D(visit,"ts")))
            .Where(visit=>Enum.IsDefined(visit.Facility)).Reverse().ToArray():Array.Empty<PetFacilityUse>();

    internal int PetFacilityUseCount(string id,PetFacility facility)=>Meta[PetFacilityRecordsKey]?[id]?["counts"] is JsonObject counts
        ?I(counts,facility.ToString()):0;

    internal void RecordPetFacilityUse(string id,PetFacility facility)
    {
        if(!Enum.IsDefined(facility))return;
        var pet=Pets.OfType<JsonNode>().FirstOrDefault(pet=>S(pet,"pet_id")==id);
        if(pet==null||!Active(pet)||B(pet,"fostered")||IsPetInHospital(pet))return;
        var records=(Meta[PetFacilityRecordsKey]??=new JsonObject()).AsObject();
        var record=(records[id]??=new JsonObject{{"counts",new JsonObject()},{"recent",new JsonArray()}}).AsObject();
        record["counts"]![facility.ToString()]=Math.Min(1_000_000,PetFacilityUseCount(id,facility)+1);
        var recent=record["recent"]!.AsArray();
        recent.Add(new JsonObject{{"facility",(int)facility},{"ts",Now}});
        while(recent.Count>6)recent.RemoveAt(0);
    }
}
