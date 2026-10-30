using System;
using System.Linq;
using System.Text.Json.Nodes;

public sealed partial class NativeGame
{
    void ClearGround(System.Collections.Generic.IEnumerable<Cell> cells)
    {
        Meta["town_cleared"]??=new JsonArray();var cleared=Meta["town_cleared"]!.AsArray();var known=cleared.Select(n=>n!.GetValue<string>()).ToHashSet();foreach(var c in cells){string id=$"{c.X},{c.Y}";if(known.Add(id))cleared.Add(id);}
    }
    public string PlacementReason(string key,int x,int y,int orientation=0)
    {
        if(orientation<0||orientation>3)return "无效朝向";var spec=Spec("CATALOG",key);int w=spec["footprint"]![orientation%2]!.GetValue<int>(),h=spec["footprint"]![1-orientation%2]!.GetValue<int>();
        var b=new JsonObject{{"cx",x},{"cy",y},{"w",w},{"h",h}};
        if(Footprint(b).Any(c=>!Allowed(c)||RoadCells().Contains(c)))return "此处有占用、保护地形或街区未开放";
        return Frontage(b).Any(Connected().Contains)?"":"需要连接庄园路网的临街位置";
    }
    string EditRoad(JsonObject a,bool remove)
    {
        var c=new Cell(I(a,"cx"),I(a,"cy"));var previous=Rows("roads").FirstOrDefault(r=>I(r!,"cx")==c.X&&I(r!,"cy")==c.Y);string style=S(a,"style","asphalt");var old=previous?.DeepClone();int cost;
        if(remove){if(previous==null)throw new InvalidOperationException("此处没有道路");cost=-(int)Math.Floor(I(previous,"paid")*Rule("ROAD_REFUND",.5));Rows("roads").Remove(previous);}
        else{if(!Allowed(c))throw new InvalidOperationException("此处不可铺路");if(previous!=null&&S(previous,"road_style")==style)throw new InvalidOperationException("道路风格相同");cost=I(Spec("ROAD_STYLES",style),"cost");Pay(cost);if(previous!=null)Rows("roads").Remove(previous);Rows("roads").Add(new JsonObject{{"road_id",$"{c.X},{c.Y}"},{"cx",c.X},{"cy",c.Y},{"road_style",style},{"built_ts",Now},{"paid",old==null?cost:I(old,"paid")}});Meta["roads_built"]=I(Meta,"roads_built")+1;ClearGround(new[]{c});}
        if(remove)Meta["coins"]=I(Meta,"coins")-cost;Meta["road_revision"]=I(Meta,"road_revision")+1;WorldRevision++;
        Meta["native_road_undo"]=new JsonObject{{"cx",c.X},{"cy",c.Y},{"previous",old},{"cost",cost},{"revision",I(Meta,"road_revision")}};
        return remove?$"道路已拆除，返还{-cost}金币":$"道路已铺设，花费{cost}金币";
    }
    string EditCommand(string action,JsonObject a)
    {
        switch(action){
            case "edit_road":return EditRoad(a,false);
            case "remove_road":return EditRoad(a,true);
            case "undo_road":{
                var undo=Meta["native_road_undo"]??throw new InvalidOperationException("没有可撤销道路");if(I(undo,"revision")!=I(Meta,"road_revision"))throw new InvalidOperationException("路网已变更，无法撤销");var cell=new Cell(I(undo,"cx"),I(undo,"cy"));if(Occupied().Contains(cell))throw new InvalidOperationException("位置已被建筑占用");int cost=I(undo,"cost");if(cost<0)Pay(-cost);else Meta["coins"]=I(Meta,"coins")+cost;
                var current=Rows("roads").FirstOrDefault(r=>I(r!,"cx")==cell.X&&I(r!,"cy")==cell.Y);if(current!=null)Rows("roads").Remove(current);if(undo["previous"] is JsonNode previous)Rows("roads").Add(previous.DeepClone());Meta["native_road_undo"]=null;Meta["road_revision"]=I(Meta,"road_revision")+1;WorldRevision++;return "道路与费用已恢复";}
            case "remove_town":{var b=Rows("town_buildings").FirstOrDefault(b=>S(b!,"building_id")==S(a,"building_id"))??throw new InvalidOperationException("建筑不存在");if(HomeMembers(S(b,"building_id")).Any())throw new InvalidOperationException("这是居民的固定住宅，不能拆除有人居住的房屋");if(B(b,"starter_home"))throw new InvalidOperationException("初始安置住宅不提供拆除退款");int refund=(int)(D(Spec("CATALOG",S(b,"catalog_key")),"cost")*D(Rules["BUILDING_REFUND"]!,S(b,"status")));Rows("town_buildings").Remove(b);Meta["coins"]=I(Meta,"coins")+refund;WorldRevision++;return $"建筑已拆除，返还{refund}金币";}
            case "demolish_manor":{var b=Rows("buildings").First(b=>I(b!,"building_id")==I(a,"building_id"))!;if(S(b,"kind")==""||Pets.Any(p=>Active(p!)&&I(p!,"building_id")==I(b,"building_id")))throw new InvalidOperationException("请先迁走全部伙伴");int capacity=Storage-(S(b,"kind")=="仓库"?I(b,"level")*I(Rules,"STORAGE_PER_LEVEL"):0);if(Rows("inventory").Sum(i=>I(i!,"quantity"))>capacity)throw new InvalidOperationException("拆除后仓库容量不足");Meta["coins"]=I(Meta,"coins")+(int)(D(Spec("BUILDINGS",S(b,"kind")),"cost")*Rule("DEMOLISH_REFUND"));b["kind"]="";b["level"]=0;WorldRevision++;return "建筑已拆除";}
            case "decoration":return EditDecoration(a);
            default:throw new InvalidOperationException("尚未迁移的操作："+action);
        }
    }
    string EditDecoration(JsonObject a)
    {
        Meta["decorations"]??=new JsonArray();Meta["decoration_undo"]??=new JsonArray();var rows=Meta["decorations"]!.AsArray();var history=Meta["decoration_undo"]!.AsArray();string op=S(a,"operation");
        if(op=="undo"){
            var undo=history.LastOrDefault()??throw new InvalidOperationException("没有可撤销装饰操作");var before=undo["before"]!.AsArray();var occupied=Rows("town_buildings").SelectMany(b=>Footprint(b!)).Concat(RoadCells()).ToHashSet();if(before.Any(d=>occupied.Contains(new(I(d!,"cx"),I(d!,"cy")))))throw new InvalidOperationException("位置已被道路或建筑占用");int delta=I(undo,"delta");if(delta>0)Pay(delta);else Meta["coins"]=I(Meta,"coins")-delta;Meta["decorations"]=before.DeepClone();history.Remove(undo);WorldRevision++;return "装饰已恢复";}
        var original=rows.DeepClone();int coins=I(Meta,"coins");var selected=rows.FirstOrDefault(d=>S(d!,"id")==S(a,"id"));
        if(op=="remove"){if(selected==null)throw new InvalidOperationException("装饰不存在");Meta["coins"]=I(Meta,"coins")+Rules["DECORATIONS"]![S(selected,"kind")]![1]!.GetValue<int>()/2;rows.Remove(selected);}
        else{
            int x=I(a,"cx"),y=I(a,"cy"),orientation=I(a,"orientation");var cell=new Cell(x,y);if(orientation<0||orientation>3||x<0||y<0||x>=125||y>=80||RoadCells().Contains(cell)||Rows("town_buildings").Any(b=>Footprint(b!).Contains(cell))||rows.Any(d=>d!=selected&&I(d!,"cx")==x&&I(d!,"cy")==y))throw new InvalidOperationException("位置或朝向不可用");string land=root["terrain"]![y]![x]!.GetValue<string>(),zone=Zone(cell);if(land!="grass"&&land!="zone"||zone!=""&&ZoneLevel(zone)==0)throw new InvalidOperationException("保护地形或街区未开放");
            if(op=="place"){string kind=S(a,"kind");var spec=Rules["DECORATIONS"]![kind]??throw new InvalidOperationException("未知装饰");Pay(spec[1]!.GetValue<int>());selected=new JsonObject{{"id",NextId("decoration")},{"kind",kind}};rows.Add(selected);}else if(op!="move"||selected==null)throw new InvalidOperationException("装饰操作无效");
            selected!["cx"]=x;selected["cy"]=y;selected["orientation"]=orientation;ClearGround(new[]{cell});
        }
        history.Add(new JsonObject{{"before",original},{"delta",I(Meta,"coins")-coins}});while(history.Count>20)history.RemoveAt(0);WorldRevision++;return "装饰已更新";
    }
}
