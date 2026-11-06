using Godot;
using System;
using System.Linq;
using System.Text.Json.Nodes;

public partial class Main
{
    const int DecorationDirectoryPageSize=8;
    string _decorationPlacedQuery="";
    int _decorationPlacedFilter,_decorationPlacedPage;

    Action BuildPlacedDecorationDirectory(Container workspace,Action<string,int> duplicate,Func<int> facing)
    {
        var card=BuildCard(workspace,"已摆放装饰");
        string target=_managementEntryTarget.StartsWith("placed-decor:",StringComparison.Ordinal)?_managementEntryTarget[13..]:"";
        if(target.Length>0&&Game.FindDecoration(target)!=null){
            _buildDecorationId=target;
            _decorationPlacedQuery="";
            _decorationPlacedFilter=0;
            _managementFocusTarget=card.GetChild<Control>(0);
        }
        var filters=BuildOperations(card);
        var search=new LineEdit{Name="DecorationPlacedQuery",Text=_decorationPlacedQuery,PlaceholderText="搜索名称、街区或坐标",
            AccessibilityName="搜索已摆放装饰",ClearButtonEnabled=true,CustomMinimumSize=new(175,38),SizeFlagsHorizontal=Control.SizeFlags.ExpandFill};
        filters.AddChild(search);
        var filter=BuildChoice(filters,new[]{"全部来源","金币购入","活动奖励"},_decorationPlacedFilter);
        filter.Name="DecorationPlacedSource";
        var selector=BuildChoice(card,Array.Empty<string>(),0);selector.Name="DecorationPlacedItem";
        var pager=BuildOperations(card);
        var previous=BuildButton(pager,"上一页");previous.Name="DecorationPlacedPrevious";
        var count=BuildText(pager,"");count.Name="DecorationPlacedCount";
        count.AutowrapMode=TextServer.AutowrapMode.Off;
        count.VerticalAlignment=VerticalAlignment.Center;
        var next=BuildButton(pager,"下一页");next.Name="DecorationPlacedNext";

        var details=MarketCard(card,color:"eef1e8");
        var header=MarketLabel(details,"",17,TownTheme.Leaf,wrap:true);header.Name="DecorationPlacedDetails";
        var payment=BuildText(details,"");payment.Name="DecorationPlacedPayment";
        var quote=BuildText(details,"");quote.Name="DecorationMoveQuote";
        var actions=BuildOperations(details);
        var locate=BuildButton(actions,"定位装饰");locate.Name="DecorationLocate";
        var map=BuildButton(actions,"地图查看地块");map.Name="DecorationLocateMap";
        var copy=BuildButton(actions,"复制样式");copy.Name="DecorationDuplicate";
        var moveScene=BuildButton(actions,"在场景中移动");moveScene.Name="DecorationMoveScene";
        var move=BuildButton(actions,"移动到指定格");move.Name="DecorationMoveAtCell";
        var remove=BuildButton(actions,"核对并移除",removal:true);remove.Name="DecorationRemove";

        JsonNode[] displayed=Array.Empty<JsonNode>();
        string[] captions=Array.Empty<string>();
        JsonNode? current=null;
        search.TextChanged+=value=>{_decorationPlacedQuery=value;_decorationPlacedPage=0;Refresh();};
        filter.ItemSelected+=value=>{_decorationPlacedFilter=(int)value;_decorationPlacedPage=0;Refresh();};
        previous.Pressed+=()=>{_decorationPlacedPage--;Refresh(keepSelection:false);};
        next.Pressed+=()=>{_decorationPlacedPage++;Refresh(keepSelection:false);};
        selector.ItemSelected+=value=>Select(displayed[(int)value]);
        locate.Pressed+=()=>{if(current!=null)LocatePlacedDecoration(NativeGame.S(current,"id"));};
        map.Pressed+=()=>{
            if(current==null)return;
            var cell=DecorationCell(current);
            ShowManagement("地图",FormattableString.Invariant($"cell:{cell.X},{cell.Y}"));
        };
        copy.Pressed+=()=>{
            if(current!=null)duplicate(NativeGame.S(current,"kind"),DecorationOrientation(current));
        };
        moveScene.Pressed+=()=>{if(current!=null)ArmPlacement("decoration",MoveArgs(current,DecorationOrientation(current)));};
        move.Pressed+=()=>{if(current!=null)Act("decoration",MoveArgs(current,facing()));};
        remove.Pressed+=ConfirmRemoval;
        return ()=>Refresh();

        void Refresh(bool keepSelection=true)
        {
            var rows=Data.Meta["decorations"]!.AsArray().OfType<JsonNode>().Reverse().ToArray();
            var filtered=rows.Where(row=>{
                var funding=Game.DecorationRemovalQuote(row);
                if(_decorationPlacedFilter==1&&funding.Paid<=0||_decorationPlacedFilter==2&&!funding.Reward)return false;
                var cell=DecorationCell(row);
                return DecorationSearchMatches(_decorationPlacedQuery,$"{DecorationCaption(row)} {cell.X},{cell.Y} {NativeGame.S(row,"kind")}");
            }).ToArray();
            int selected=keepSelection?Array.FindIndex(filtered,row=>NativeGame.S(row,"id")==_buildDecorationId):-1;
            int pages=(filtered.Length+DecorationDirectoryPageSize-1)/DecorationDirectoryPageSize;
            if(selected>=0)_decorationPlacedPage=selected/DecorationDirectoryPageSize;
            _decorationPlacedPage=Math.Clamp(_decorationPlacedPage,0,Math.Max(0,pages-1));
            var page=filtered.Skip(_decorationPlacedPage*DecorationDirectoryPageSize).Take(DecorationDirectoryPageSize).ToArray();
            var names=page.Select(DecorationCaption).ToArray();
            if(!displayed.SequenceEqual(page)||!captions.SequenceEqual(names)){
                displayed=page;
                captions=names;
                selector.Clear();
                foreach(var caption in names)selector.AddItem(caption);
            }
            count.Text=pages==0?(rows.Length==0?"还没有摆放装饰。":"没有符合条件的装饰。"):
                $"{filtered.Length} / {rows.Length} 件 · {_decorationPlacedPage+1} / {pages} 页";
            previous.Disabled=_decorationPlacedPage==0;
            next.Disabled=_decorationPlacedPage+1>=pages;
            selector.Visible=displayed.Length>0;
            if(displayed.Length==0){Select(null);return;}
            int index=selected<0?0:selected%DecorationDirectoryPageSize;
            selector.Select(index);
            Select(displayed[index]);
        }

        void Select(JsonNode? row)
        {
            current=row;
            details.GetParent<Control>().Visible=row!=null;
            if(row==null)return;
            _buildDecorationId=NativeGame.S(row,"id");
            string kind=NativeGame.S(row,"kind");
            var funding=Game.DecorationRemovalQuote(row);
            header.Text=$"{DecorationCaption(row)} · {BuildOrientations[DecorationOrientation(row)]}";
            payment.Text=funding.Reward?"活动奖励 · 移除返还 0 金币":$"实际支付 {funding.Paid} 金币 · 移除返还 {funding.Refund} 金币";
            var movement=Game.DecorationQuote(kind,_buildX,_buildY,facing(),_buildDecorationId);
            quote.Text=BuildQuoteText($"移动免费 · 目标 ({_buildX}, {_buildY}) · {BuildOrientations[facing()]}",
                movement.Reason.Length==0?"此处可以移动":movement.Reason);
            SetBuildAvailability(move,movement.Reason);
        }

        JsonObject MoveArgs(JsonNode row,int orientation)=>new(){{"operation","move"},{"id",NativeGame.S(row,"id")},
            {"kind",NativeGame.S(row,"kind")},{"cx",_buildX},{"cy",_buildY},{"orientation",orientation}};

        void ConfirmRemoval()
        {
            if(current==null)return;
            string id=NativeGame.S(current,"id");
            if(Game.FindDecoration(id) is not JsonNode row){Refresh();return;}
            var shown=row.DeepClone();
            var funding=Game.DecorationRemovalQuote(row);
            string effect=funding.Reward?"已使用的活动奖励不会返还。":"";
            ConfirmManagementAction("移除装饰",$"{DecorationCaption(row)} · 返还 {funding.Refund} 金币\n{effect}"+
                "移除后可撤销，撤销会恢复原装饰并收回退款。","确认移除",()=>{
                if(Game.FindDecoration(id) is not JsonNode latest||!JsonNode.DeepEquals(latest,shown)||Game.DecorationRemovalQuote(latest)!=funding){
                    Refresh();SetManagementFeedback("装饰位置或报价已变化，请重新核对。",true);return;
                }
                Act("decoration",new JsonObject{{"operation","remove"},{"id",id}});
            });
        }
    }

    string DecorationCaption(JsonNode row)
    {
        var cell=DecorationCell(row);
        return $"{Game.DecorationName(NativeGame.S(row,"kind"))} · ({cell.X}, {cell.Y}) · {ProjectDistrictCaption(Game.ConstructionDistrict(row))}";
    }

    static Vector2I DecorationCell(JsonNode row)=>new(NativeGame.I(row,"cx"),NativeGame.I(row,"cy"));
    static int DecorationOrientation(JsonNode row)=>Math.Clamp(NativeGame.I(row,"orientation"),0,BuildOrientations.Length-1);

    void LocatePlacedDecoration(string id)
    {
        if(Game.FindDecoration(id) is not JsonNode row){SetManagementFeedback("该装饰已移除。",true);return;}
        var position=WorldData.Cell(DecorationCell(row));
        RememberCameraSite(position);
        StopPetFollow();StopResidentFollow();EndPlacement();
        _portraitAnchor=null;_buildCamera=false;
        _center=position;_pitch=.69f;_distance=28;
        _selection=DecorationCaption(row);
        _management?.Hide();
    }
}
