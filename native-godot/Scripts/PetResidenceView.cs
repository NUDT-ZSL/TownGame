using Godot;
using System.Text.Json.Nodes;

public partial class Main
{
    internal static Vector3 PetHomePoint(JsonNode pet)
    {
        int plot=NativeGame.I(pet,"building_id");
        uint hash=2166136261;
        foreach(char c in NativeGame.S(pet,"pet_id"))hash=unchecked((hash^c)*16777619);
        var center=WorldData.Expand(new((1314+plot%5*230)/12f,0,(870+plot/5*88)/12f));
        return center+new Vector3((hash%7-3f)*.9f,0,6.5f+(hash/7%3)*.9f);
    }
}
