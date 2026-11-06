using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;

public sealed partial class NativeGame
{
    const string ConstructionNoticeKey = "native_construction_notices";

    public JsonNode? FindTownBuilding(string id) =>
        Rows("town_buildings").OfType<JsonNode>().FirstOrDefault(building => S(building, "building_id") == id);

    public double ConstructionProgress(JsonNode building)
    {
        if (S(building, "status") == "ready")
            return 1;

        double start = D(building, "placed_ts");
        double finish = D(building, "finish_ts");
        return finish <= start
            ? (Now >= finish ? 1 : 0)
            : Math.Clamp((Now - start) / (finish - start), 0, 1);
    }

    public int ConstructionStage(JsonNode building) =>
        S(building, "status") == "ready" ? 4 : Math.Min(3, (int)(ConstructionProgress(building) * 4));

    public string ConstructionRoadStatus(JsonNode building)
    {
        var linked = Connected();
        var roads = RoadCells();
        bool hasRoad = false;
        foreach (var cell in Frontage(building))
        {
            if (linked.Contains(cell))
                return "已接入庄园路网";
            hasRoad |= roads.Contains(cell);
        }
        return hasRoad ? "有临街道路，尚未连通庄园" : "缺少临街道路";
    }

    internal readonly record struct ConstructionAccess(Cell? Road, bool OnDoorSide, int Adjacent, int Connected);

    internal ConstructionAccess ConstructionRoadAccess(JsonNode building)
    {
        int x=I(building,"cx"), y=I(building,"cy"), width=I(building,"w"), depth=I(building,"h");
        var direction=EntranceDirection(I(building,"orientation"));
        bool OnDoorSide(Cell road)=>direction.X==0
            ?road.Y==y+(direction.Y>0?depth:-1):road.X==x+(direction.X>0?width:-1);
        var connected=Connected();
        var roads=Frontage(building).Where(RoadCells().Contains).OrderByDescending(connected.Contains)
            .ThenByDescending(OnDoorSide)
            .ThenBy(road=>Math.Abs(2*(road.X-x)-width+1)+Math.Abs(2*(road.Y-y)-depth+1))
            .ThenBy(road=>road.Y).ThenBy(road=>road.X).ToArray();
        return roads.Length==0?default:new(roads[0],OnDoorSide(roads[0]),roads.Length,roads.Count(connected.Contains));
    }

    internal string ConstructionDistrict(JsonNode building)=>Zone(new(I(building,"cx"),I(building,"cy")));

    public IEnumerable<JsonNode> UnreadConstructionProjects()
    {
        if (Meta[ConstructionNoticeKey] is not JsonObject { Count: > 0 } notices)
            return Enumerable.Empty<JsonNode>();

        return Rows("town_buildings").OfType<JsonNode>()
            .Where(building => S(building, "status") == "ready" && B(notices, S(building, "building_id")));
    }

    void CompleteTownConstruction()
    {
        foreach (var building in Rows("town_buildings").OfType<JsonNode>())
        {
            if (S(building, "status") != "building" || D(building, "finish_ts") > Now)
                continue;

            building["status"] = "ready";
            if (!B(building, "community"))
            {
                Meta[ConstructionNoticeKey] ??= new JsonObject();
                Meta[ConstructionNoticeKey]![S(building, "building_id")] = true;
            }
            WorldRevision++;
        }
    }

    void ClearConstructionNotice(string id) => (Meta[ConstructionNoticeKey] as JsonObject)?.Remove(id);
}
