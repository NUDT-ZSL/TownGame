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
        return EditRoadStroke(new JsonObject{{"cells",new JsonArray(new JsonArray(I(a,"cx"),I(a,"cy")))},{"style",S(a,"style","asphalt")},{"remove",remove}});
    }
    public static System.Collections.Generic.List<Cell> RoadLine(Cell from,Cell to)
    {
        var result=new System.Collections.Generic.List<Cell>{from};var c=from;
        while(c.X!=to.X||c.Y!=to.Y){if(c.X!=to.X)c=new(c.X+Math.Sign(to.X-c.X),c.Y);else c=new(c.X,c.Y+Math.Sign(to.Y-c.Y));result.Add(c);}return result;
    }
    public System.Collections.Generic.List<Cell> AutoRoadPlan(Cell target)=>RoadPlan(new[]{target});
    public string RoadReason(Cell cell,string style,bool remove=false)
    {
        if(cell.X<0||cell.Y<0||cell.X>=125||cell.Y>=80)return "超出地图";
        if(remove)return "";
        if(Rules["ROAD_STYLES"]?[style]==null)return "未知道路风格";
        return Allowed(cell)?"":"建筑占用、保护地形或街区未开放";
    }
    public (int Cost,string Reason) RoadQuote(System.Collections.Generic.IEnumerable<Cell> cells,string style,bool remove=false)
    {
        int cost=0;var roads=Rows("roads").ToDictionary(r=>new Cell(I(r!,"cx"),I(r!,"cy")));
        foreach(var c in cells.Distinct()){
            string reason=RoadReason(c,style,remove);if(reason!="")return(0,$"({c.X},{c.Y}) {reason}");
            roads.TryGetValue(c,out var old);
            if(remove){if(old!=null)cost-=(int)Math.Floor(I(old,"paid")*Rule("ROAD_REFUND",.5));}
            else if(old==null||S(old,"road_style")!=style)cost+=I(Spec("ROAD_STYLES",style),"cost");
        }
        return(cost,cost>I(Meta,"coins")?"金币不足":"");
    }
    string EditRoadStroke(JsonObject args)
    {
        string style=S(args,"style","asphalt");bool remove=B(args,"remove");
        var cells=args["cells"]?.AsArray().Select(c=>new Cell(c![0]!.GetValue<int>(),c[1]!.GetValue<int>())).Distinct().ToArray()??throw new InvalidOperationException("道路笔画为空");
        if(cells.Length==0||cells.Length>10000)throw new InvalidOperationException("道路笔画为空或过长");
        var quote=RoadQuote(cells,style,remove);if(quote.Reason!="")throw new InvalidOperationException(quote.Reason);
        var changes=new JsonArray();
        foreach(var c in cells){
            var old=Rows("roads").FirstOrDefault(r=>I(r!,"cx")==c.X&&I(r!,"cy")==c.Y);
            if(remove&&old==null)continue;
            if(!remove&&old!=null&&S(old,"road_style")==style)continue;
            var change=new JsonObject{{"cx",c.X},{"cy",c.Y},{"before",old?.DeepClone()}};if(old!=null)Rows("roads").Remove(old);
            if(!remove){var road=new JsonObject{{"road_id",$"{c.X},{c.Y}"},{"cx",c.X},{"cy",c.Y},{"road_style",style},{"built_ts",Now},{"paid",old==null?I(Spec("ROAD_STYLES",style),"cost"):I(old,"paid")}};Rows("roads").Add(road);change["after"]=road.DeepClone();}
            changes.Add(change);
        }
        if(changes.Count==0)throw new InvalidOperationException(remove?"此处没有可拆除道路":"道路风格相同，无需施工");
        if(quote.Cost>0)Pay(quote.Cost);else Meta["coins"]=I(Meta,"coins")-quote.Cost;
        if(!remove){ClearGround(cells);Meta["roads_built"]=I(Meta,"roads_built")+changes.Count;}
        Meta["native_road_history"]!.AsArray().Add(new JsonObject{{"changes",changes},{"cost",quote.Cost}});while(Meta["native_road_history"]!.AsArray().Count>20)Meta["native_road_history"]!.AsArray().RemoveAt(0);
        Meta["native_road_undo"]=null;Meta["road_revision"]=I(Meta,"road_revision")+1;WorldRevision++;
        return $"道路笔画完成 {changes.Count}格，{(quote.Cost<0?"返还":"花费")}{Math.Abs(quote.Cost)}金币";
    }
    string UndoRoadStroke()
    {
        var history=Meta["native_road_history"]!.AsArray();
        if(history.Count==0){
            // 兼容迁移前仅保存一笔的撤销记录。
            if(Meta["native_road_undo"] is not JsonNode old||I(old,"revision")!=I(Meta,"road_revision"))throw new InvalidOperationException("没有可撤销道路");
            var current=Rows("roads").FirstOrDefault(r=>I(r!,"cx")==I(old,"cx")&&I(r!,"cy")==I(old,"cy"));
            history.Add(new JsonObject{{"cost",I(old,"cost")},{"changes",new JsonArray(new JsonObject{{"cx",I(old,"cx")},{"cy",I(old,"cy")},{"before",old["previous"]?.DeepClone()},{"after",current?.DeepClone()}})}});
        }
        var undo=history.Last()!;var changes=undo["changes"]!.AsArray();
        foreach(var c in changes){var cell=new Cell(I(c!,"cx"),I(c!,"cy"));if(Occupied().Contains(cell))throw new InvalidOperationException("原道路位置已被建筑或装饰占用");var current=Rows("roads").FirstOrDefault(r=>I(r!,"cx")==cell.X&&I(r!,"cy")==cell.Y);if(!JsonNode.DeepEquals(current,c!["after"]))throw new InvalidOperationException("原笔画道路已被其他施工修改");}
        int cost=I(undo,"cost");if(cost<0)Pay(-cost);else Meta["coins"]=I(Meta,"coins")+cost;
        foreach(var c in changes){var current=Rows("roads").FirstOrDefault(r=>I(r!,"cx")==I(c!,"cx")&&I(r!,"cy")==I(c!,"cy"));if(current!=null)Rows("roads").Remove(current);if(c!["before"] is JsonNode before)Rows("roads").Add(before.DeepClone());}
        history.Remove(undo);Meta["native_road_undo"]=null;Meta["road_revision"]=I(Meta,"road_revision")+1;WorldRevision++;return "已撤销整笔道路，费用已恢复";
    }
    public (int Refund,string Reason) TownDemolitionQuote(JsonNode building)
    {
        if(HomeMembers(S(building,"building_id")).Any())return (0,"这是居民的固定住宅，不能拆除有人居住的房屋");
        if(B(building,"starter_home"))return (0,"初始安置住宅不可拆除");
        int refund=(int)(D(Spec("CATALOG",S(building,"catalog_key")),"cost")*D(Rules["BUILDING_REFUND"]!,S(building,"status")));
        return (refund,"");
    }

    public (int Refund,string Reason) ManorDemolitionQuote(JsonNode building)
    {
        string kind=S(building,"kind");
        if(IncubatingAtHome(I(building,"building_id"))>0)return (0,"有宠物蛋已预留这处育幼床位");
        if(kind==""||Pets.Any(p=>Active(p!)&&I(p!,"building_id")==I(building,"building_id")))return (0,"请先迁走全部伙伴");
        int capacity=Storage-(kind=="仓库"?I(building,"level")*I(Rules,"STORAGE_PER_LEVEL"):0);
        if(Rows("inventory").Sum(i=>I(i!,"quantity"))>capacity)return (0,"拆除后仓库容量不足");
        return ((int)(D(Spec("BUILDINGS",kind),"cost")*Rule("DEMOLISH_REFUND")),"");
    }

    string EditCommand(string action,JsonObject a)
    {
        switch(action){
            case "edit_road":return EditRoad(a,false);
            case "remove_road":return EditRoad(a,true);
            case "undo_road":return UndoRoadStroke();
            case "remove_town":
            {
                var building=Rows("town_buildings").FirstOrDefault(b=>S(b!,"building_id")==S(a,"building_id"))??throw new InvalidOperationException("建筑不存在");
                var (refund,reason)=TownDemolitionQuote(building);
                if(reason!="")throw new InvalidOperationException(reason);
                Rows("town_buildings").Remove(building);
                ClearConstructionNotice(S(building,"building_id"));
                Meta["coins"]=I(Meta,"coins")+refund;
                WorldRevision++;
                return $"建筑已拆除，返还{refund}金币";
            }
            case "demolish_manor":
            {
                var building=Rows("buildings").First(b=>I(b!,"building_id")==I(a,"building_id"))!;
                var (refund,reason)=ManorDemolitionQuote(building);
                if(reason!="")throw new InvalidOperationException(reason);
                Meta["coins"]=I(Meta,"coins")+refund;
                building["kind"]="";
                building["level"]=0;
                building.AsObject().Remove("paint_wall");
                building.AsObject().Remove("paint_roof");
                WorldRevision++;
                return "建筑已拆除";
            }
            case "decoration":return EditDecoration(a);
            case "paint_building":return PaintBuilding(a);
            default:throw new InvalidOperationException("不支持的操作："+action);
        }
    }
    internal string DecorationName(string kind)=>Rules["DECORATIONS"]?[kind]?[0]?.GetValue<string>()??kind;
    internal int DecorationCatalogPrice(string kind)=>Rules["DECORATIONS"]?[kind]?[1]?.GetValue<int>()??0;
    internal int DecorationPlacementPrice(string kind)=>FestivalDecorationCredits(kind)>0?0:DecorationCatalogPrice(kind);
    internal JsonNode? FindDecoration(string id)=>(Meta["decorations"] as JsonArray)?.OfType<JsonObject>().FirstOrDefault(row=>S(row,"id")==id);
    internal JsonNode? DecorationAt(Cell cell)=>(Meta["decorations"] as JsonArray)?.OfType<JsonObject>()
        .FirstOrDefault(row=>I(row,"cx")==cell.X&&I(row,"cy")==cell.Y);

    internal (int Paid,int Refund,bool Reward) DecorationRemovalQuote(JsonNode decoration)
    {
        int listPrice=DecorationCatalogPrice(S(decoration,"kind"));
        int paid=I(decoration,"paid",listPrice);
        bool reward=decoration["festival_reward"]==null?paid==0&&listPrice>0:B(decoration,"festival_reward");
        return (paid,paid/2,reward);
    }

    public (int Cost,string Reason) DecorationQuote(string kind,int x,int y,int orientation,string? movingId=null)
    {
        var rows=Meta["decorations"]?.AsArray();
        int cost=0;
        if(movingId==null){
            var spec=Rules["DECORATIONS"]?[kind];
            if(spec==null)return (0,"未知装饰");
            cost=DecorationPlacementPrice(kind);
        }else if(rows?.Any(d=>S(d!,"id")==movingId)!=true){
            return (0,"装饰不存在");
        }

        if(orientation<0||orientation>3)return (cost,"无效朝向");
        if(x<0||y<0||x>=125||y>=80)return (cost,"超出地图");
        var cell=new Cell(x,y);
        if(RoadCells().Contains(cell))return (cost,"这里已有道路");
        if(Rows("town_buildings").Any(b=>Footprint(b!).Contains(cell)))return (cost,"这里被建筑占用");
        if(rows?.Any(d=>S(d!,"id")!=movingId&&I(d!,"cx")==x&&I(d!,"cy")==y)==true)
            return (cost,"这里已有装饰");
        string land=root["terrain"]![y]![x]!.GetValue<string>();
        if(land!="grass"&&land!="zone")return (cost,"这里是保护地形");
        string zone=Zone(cell);
        if(zone!=""&&ZoneLevel(zone)==0)return (cost,"街区尚未开放");
        return (cost,cost>I(Meta,"coins")?"金币不足":"");
    }

    string EditDecoration(JsonObject a)
    {
        Meta["decorations"]??=new JsonArray();Meta["decoration_undo"]??=new JsonArray();var rows=Meta["decorations"]!.AsArray();var history=Meta["decoration_undo"]!.AsArray();string op=S(a,"operation");
        if(op=="undo"){
            var undo=history.LastOrDefault()??throw new InvalidOperationException("没有可撤销装饰操作");var before=undo["before"]!.AsArray();var occupied=Rows("town_buildings").SelectMany(b=>Footprint(b!)).Concat(RoadCells()).ToHashSet();if(before.Any(d=>occupied.Contains(new(I(d!,"cx"),I(d!,"cy")))))throw new InvalidOperationException("位置已被道路或建筑占用");int delta=I(undo,"delta");if(delta>0)Pay(delta);else Meta["coins"]=I(Meta,"coins")-delta;Meta["decorations"]=before.DeepClone();
            if(S(undo,"festival_credit") is { Length:>0 } credit)ChangeFestivalDecorationCredits(credit,1);
            history.Remove(undo);WorldRevision++;return "装饰已恢复";}
        var original=rows.DeepClone();int coins=I(Meta,"coins");string usedCredit="";
        var selected=FindDecoration(S(a,"id"));
        if(op=="remove"){
            if(selected==null)throw new InvalidOperationException("装饰不存在");
            Meta["coins"]=I(Meta,"coins")+DecorationRemovalQuote(selected).Refund;
            rows.Remove(selected);
        }
        else{
            if(op is not ("place" or "move"))throw new InvalidOperationException("装饰操作无效");
            int x=I(a,"cx"),y=I(a,"cy"),orientation=I(a,"orientation");
            string kind=S(a,"kind");
            var quote=DecorationQuote(kind,x,y,orientation,op=="move"?S(a,"id"):null);
            if(quote.Reason!="")throw new InvalidOperationException(quote.Reason);
            if(op=="place"){
                Pay(quote.Cost);
                if(FestivalDecorationCredits(kind)>0){ChangeFestivalDecorationCredits(kind,-1);usedCredit=kind;}
                selected=new JsonObject{{"id",NextId("decoration")},{"kind",kind},{"paid",quote.Cost},{"festival_reward",usedCredit.Length>0}};
                rows.Add(selected);
            }
            selected!["cx"]=x;
            selected["cy"]=y;
            selected["orientation"]=orientation;
            ClearGround(new[]{new Cell(x,y)});
        }
        var edit=new JsonObject{{"before",original},{"delta",I(Meta,"coins")-coins}};
        if(usedCredit.Length>0)edit["festival_credit"]=usedCredit;
        history.Add(edit);while(history.Count>20)history.RemoveAt(0);WorldRevision++;return "装饰已更新";
    }
}
