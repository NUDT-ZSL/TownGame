using System;
using System.Linq;
using System.Text.Json.Nodes;

public sealed partial class NativeGame
{
    const string NeighborhoodOrdersKey = "native_neighbor_orders";
    internal readonly record struct NeighborOrderProgress(int Deliveries, int StoryStep);

    internal NeighborOrderProgress NeighborhoodProgress(string personId)
    {
        var row = Meta[NeighborhoodOrdersKey]?[personId];
        return row == null ? default : new(I(row, "deliveries"), I(row, "story_step"));
    }

    void AssignOrderPublisher(JsonObject order)
    {
        var publishers = People.OfType<JsonNode>().Where(person => S(person, "role") == "居民"
            && S(person, "status") != "离镇" && Now - D(person, "born_ts", Now) >= 7 * Day
            && S(person, "person_id").Length > 0).ToArray();
        if (publishers.Length == 0) return;
        var open = Rows("orders").OfType<JsonNode>()
            .Where(row => S(row, "status") == "进行中" && D(row, "expiry_ts") > Now).ToArray();
        int Pending(JsonNode person) => open.Count(row => S(row, "publisher_id") == S(person, "person_id"));
        bool WantsFollowUp(JsonNode person)
        {
            string id = S(person, "person_id");
            var progress = NeighborhoodProgress(id);
            return progress.StoryStep < 3 && progress.Deliveries >= 2 * (progress.StoryStep + 1)
                && !open.Any(row => S(row, "publisher_id") == id && I(row, "story_step") > 0);
        }

        var publisher = S(order, "kind") == "item" && !B(order,"stage_matched")
            ? publishers.Where(WantsFollowUp).OrderBy(Pending).FirstOrDefault() : null;
        bool followUp = publisher != null;
        publisher ??= publishers.OrderBy(Pending).ThenBy(person => NeighborhoodProgress(S(person, "person_id")).Deliveries)
            .First();
        string id = S(publisher, "person_id");
        order["publisher_id"] = id;
        order["npc"] = S(publisher, "name");
        order["publisher_work"] = ResidentWorkZone(publisher);
        if (!followUp) return;

        int next = NeighborhoodProgress(id).StoryStep + 1;
        string zone = S(order, "publisher_work");
        order["story_step"] = next;
        string result=NeighborhoodResultKind(zone);
        order["story"] = next switch
        {
            1 => $"先前的物资已派上用场，{S(publisher, "name")} 想在熟悉的街区筹备一次邻里分享。",
            2 => "上次分享很受欢迎，邻居们约好了再办一回，这次会留一些物资给新搬来的住户。",
            _ => $"邻里们正在准备{NeighborhoodResultLabel(result)}。交齐最后一批物资，成果就会出现在街区中。"
        };
        string item = zone switch
        {
            "fields" => next == 1 ? "鲜草" : "谷物",
            "ranch" => next == 1 ? "普通饲料" : "高级饲料",
            "industry" => next == 1 ? "谷物" : "高级饲料",
            _ => next == 1 ? "清水" : "零食"
        };
        int quantity = I(order, "quantity");
        order["item"] = item;
        order["reward"] = Math.Max(I(Rules, "ORDER_ITEM_REWARD_MIN"),
            I(Spec("ITEMS", item), "sell") * quantity * I(Rules, "ORDER_ITEM_REWARD_MULT")) + next * 10;
        order["reputation"] = I(order, "reputation") + 1;
    }

    string FinishNeighborhoodOrder(JsonNode order)
    {
        string id = S(order, "publisher_id");
        if (id.Length == 0) return "订单已交付";
        var ledger = (Meta[NeighborhoodOrdersKey] ??= new JsonObject()).AsObject();
        var progress = NeighborhoodProgress(id);
        int story = I(order, "story_step");
        var record=(ledger[id]??=new JsonObject()).AsObject();
        record["deliveries"]=progress.Deliveries+1;
        record["story_step"]=Math.Max(progress.StoryStep,story);
        if(story>=3&&S(record,"result_kind").Length==0){
            string zone=S(order,"publisher_work","estate"),kind=NeighborhoodResultKind(zone);
            record["result_kind"]=kind;
            record["result_zone"]=NeighborhoodResultZone(zone,kind);
            record["result_name"]=S(order,"npc");
            return $"邻里委托已交付 · {NeighborhoodResultLabel(kind)}已落成";
        }
        return story > 0 ? $"邻里委托已交付 · {S(order, "npc")} 的故事推进至第 {story} 章" : $"订单已交付 · {S(order, "npc")} 已收到";
    }

    internal readonly record struct NeighborhoodResult(string Publisher,string Name,string Zone,string Kind);
    internal bool HasNeighborhoodResult(string id)=>Meta[NeighborhoodOrdersKey]?[id] is JsonNode row&&I(row,"story_step")>=3;
    static string NeighborhoodResultKind(string zone)=>zone switch{"ranch"=>"walk","fields"=>"garden","market"=>"stall",_=>"bench"};
    string NeighborhoodResultZone(string zone,string kind)=>kind=="bench"?(Effective("park")?"park":"estate"):Effective(zone)?zone:"estate";
    internal static string NeighborhoodResultLabel(string kind)=>kind switch{
        "walk"=>"牧场散步路线","garden"=>"田间花圃","stall"=>"邻里分享摊",_=>"广场长椅"};

    internal NeighborhoodResult[] NeighborhoodResults()=>Meta[NeighborhoodOrdersKey] is JsonObject ledger
        ?ledger.Where(entry=>entry.Value is JsonNode row&&I(row,"story_step")>=3)
            .OrderBy(entry=>entry.Key,StringComparer.Ordinal)
            .Select(entry=>ReadNeighborhoodResult(entry.Key,entry.Value!))
            .ToArray()
        :Array.Empty<NeighborhoodResult>();

    NeighborhoodResult ReadNeighborhoodResult(string id,JsonNode row)
    {
        string kind=S(row,"result_kind");
        if(kind.Length>0)return new(id,S(row,"result_name"),S(row,"result_zone","estate"),kind);
        var resident=People.OfType<JsonNode>().FirstOrDefault(person=>S(person,"person_id")==id);
        string zone=resident==null?"estate":ResidentWorkZone(resident);
        kind=NeighborhoodResultKind(zone);
        return new(id,resident==null?"邻里":S(resident,"name"),NeighborhoodResultZone(zone,kind),kind);
    }
}
