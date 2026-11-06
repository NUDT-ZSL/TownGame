using System;
using System.Linq;
using System.Text.Json.Nodes;

public sealed partial class NativeGame
{
    const string JournalReviewKey="native_journal_reviewed";
    internal const int JournalReviewLimit=24;
    internal readonly record struct JournalNotice(string Page,string Id,string Title,string Detail,double ReviewedAt);

    internal JournalNotice[] ReviewedJournalNotices()=>Meta[JournalReviewKey] is JsonArray history
        ?history.OfType<JsonObject>().TakeLast(JournalReviewLimit).Reverse()
            .Select(row=>new JournalNotice(S(row,"page"),S(row,"id"),S(row,"title"),S(row,"detail"),D(row,"reviewed_ts")))
            .Where(row=>row.Page is "建造" or "宠物"&&row.Id.Length>0).ToArray()
        :Array.Empty<JournalNotice>();

    string AcknowledgeConstruction(string id)
    {
        var project=UnreadConstructionProjects().FirstOrDefault(project=>S(project,"building_id")==id);
        if(project!=null){
            string key=S(project,"catalog_key");
            string name=Rules["CATALOG"]?[key] is JsonObject spec?S(spec,"name",key):key;
            RecordJournalReview("建造",id,$"{name}已完工",$"小镇坐标 ({I(project,"cx")}, {I(project,"cy")})");
        }
        ClearConstructionNotice(id);
        return "完工提醒已标记已读";
    }

    string AcknowledgeHatch(string id)
    {
        var hatch=RecentHatches().FirstOrDefault(row=>S(row,"pet_id")==id);
        if(hatch==null)throw new InvalidOperationException("这条孵化消息已离开近期记录。");
        if(!B(hatch,"journal_read")){
            hatch["journal_read"]=true;
            RecordJournalReview("宠物",id,$"{S(hatch,"name")}已破壳","孵化记录 · 已入住预留居所");
        }
        return "孵化消息已标记已读";
    }

    void RecordJournalReview(string page,string id,string title,string detail)
    {
        var history=Meta[JournalReviewKey] as JsonArray;
        if(history==null)Meta[JournalReviewKey]=history=new JsonArray();
        history.Add(new JsonObject{{"page",page},{"id",id},{"title",title},{"detail",detail},{"reviewed_ts",Now}});
        while(history.Count>JournalReviewLimit)history.RemoveAt(0);
    }
}
