using Godot;
using System;
using System.Linq;
using System.Text.Json.Nodes;

public partial class Main
{
    void VerifyDecorationExperience(Action<bool,string> check)
    {
        const string reward="kit_market_crates",purchase="flowers";
        var original=Data.Snapshot;
        var directory=(_buildWorkspace,_buildX,_buildY,_buildOrientation,_buildDecorTheme,_buildDecorKind,_buildDecorationId,
            _decorationCatalogQuery,_decorationBudgetFilter,_decorationPlacedQuery,_decorationPlacedFilter,_decorationPlacedPage,_rememberedMapView);
        var camera=(_center,_distance,_pitch,_portraitAnchor,_buildCamera,_selection,_previousCameraSite);
        var past=_cameraPast.ToArray();var forward=_cameraForward.ToArray();
        try{
            Data.Root["snapshot"]=original.DeepClone();Data.Reindex();
            Game.Meta["decorations"]=new JsonArray();Game.Meta["decoration_undo"]=new JsonArray();
            Game.Meta["native_festival_decor"]=new JsonObject{{reward,2}};
            Game.Meta["coins"]=2000;
            var cells=(from y in Enumerable.Range(0,80) from x in Enumerable.Range(0,125)
                where Game.DecorationQuote(purchase,x,y,0).Reason=="" select new Vector2I(x,y)).Take(12).ToArray();
            check(cells.Length==12,"装饰验收缺少可用地块");
            string paidId="";
            for(int i=0;i<10;i++)paidId=Place(purchase,cells[i],0);
            string rewardId=Place(reward,cells[10],3);
            _buildWorkspace=3;_buildX=cells[11].X;_buildY=cells[11].Y;
            _decorationCatalogQuery="无匹配装饰验证";_buildDecorTheme=1;_decorationBudgetFilter=1;
            _decorationPlacedQuery="";_decorationPlacedFilter=_decorationPlacedPage=0;
            _buildDecorationId="";

            ShowManagement("建造","decor:"+reward);
            check(_buildDecorKind==reward&&Find<LineEdit>("DecorationCatalogQuery").Text==""&&
                Find<Label>("DecorationPreviewPrice").Text.Contains("使用 1 份活动奖励"),"奖励跳转未选中实际装饰或显示真实成本");
            check(Find<MeshInstance3D>("DecorationPreviewModel").Mesh==Prototype("decoration:"+reward),"预览未使用实际场景模型");
            Game.Meta["coins"]=0;Choose("DecorationCatalogBudget",1);
            check(Find<OptionButton>("DecorationKitItem").ItemCount==1&&_buildDecorKind==reward,"预算筛选未计入活动免费额度");
            Choose("DecorationCatalogBudget",2);
            Type("DecorationCatalogQuery","果蔬 无匹配");
            check(!Find<Control>("DecorationPreview").IsVisibleInTree()&&Find<Button>("DecorationPlaceAtCell").Disabled,"目录空结果仍可摆放旧条目");
            Type("DecorationCatalogQuery","果蔬 市集");
            check(_buildDecorKind==reward&&Find<OptionButton>("DecorationKitItem").ItemCount==1,"目录搜索没有组合匹配名称和主题");

            Press("DecorationPlacedNext");
            check(Find<Label>("DecorationPlacedCount").Text.Contains("2 / 2 页"),"已摆放装饰名册未翻页");
            Choose("DecorationPlacedSource",2);
            check(_buildDecorationId==rewardId&&Find<OptionButton>("DecorationPlacedItem").ItemCount==1,"奖励来源筛选不准确");
            Press("DecorationLocateMap");
            var map=Find<TownMap>("TownNavigationMap");
            var tile=new NativeGame.Cell(cells[10].X,cells[10].Y);
            check(map.InspectedCell==tile&&map.InspectTile(tile).Use.Contains(Game.DecorationName(reward)),"地图未标明真实装饰地块");
            ShowManagement("建造","placed-decor:"+rewardId);
            Press("DecorationLocate");
            check(_center==WorldData.Cell(cells[10]),"场景未定位到装饰格");

            ShowManagement("建造","placed-decor:"+rewardId);
            Press("DecorationDuplicate");
            check(_buildDecorKind==reward&&Find<OptionButton>("DecorationPreviewFacing").Selected==3&&
                Game.Meta["decorations"]!.AsArray().Count==11&&Game.FestivalDecorationCredits(reward)==1,"复制没有保留朝向或提前修改了进度");
            Press("DecorationPlaceInScene");
            check(_placementAction=="decoration"&&NativeGame.S(_placementArgs,"operation")=="place"&&
                NativeGame.S(_placementArgs,"kind")==reward&&NativeGame.I(_placementArgs,"orientation")==3,"复制未进入新的同款摆放预览");
            EndPlacement();
            string duplicate=Place(reward,cells[11],3);
            check(Game.FestivalDecorationCredits(reward)==0&&Game.DecorationRemovalQuote(Game.FindDecoration(duplicate)!).Refund==0,
                "复制未按当前可用的活动额度计费");
            Game.Execute("decoration",new JsonObject{{"operation","undo"}});
            check(Game.FindDecoration(duplicate)==null&&Game.FestivalDecorationCredits(reward)==1,"撤销奖励摆放没有归还额度");

            ShowManagement("建造","placed-decor:"+rewardId);
            Press("DecorationRemove");
            var dialog=RemovalDialog();
            check(dialog.DialogText.Contains("返还 0 金币")&&dialog.DialogText.Contains("活动奖励不会返还"),"奖励移除说明与实际结算不符");
            Game.FindDecoration(rewardId)!["orientation"]=2;
            dialog.EmitSignal(AcceptDialog.SignalName.Confirmed);
            check(Game.FindDecoration(rewardId)!=null,"移除确认接受了已经变化的装饰状态");

            ShowManagement("建造","placed-decor:"+paidId);
            Press("DecorationRemove");
            int refund=Game.DecorationCatalogPrice(purchase)/2;
            dialog=RemovalDialog();
            check(dialog.DialogText.Contains($"返还 {refund} 金币"),"购买装饰移除未按实际支付报价");
            dialog.EmitSignal(ConfirmationDialog.SignalName.Canceled);
            check(Game.FindDecoration(paidId)!=null,"取消后装饰仍被移除");
            Game.Execute("decoration",new JsonObject{{"operation","remove"},{"id",paidId}});
            check(Game.FindDecoration(paidId)==null&&NativeGame.I(Game.Meta,"coins")==refund,"移除实际支付退款有误");
            Game.Execute("decoration",new JsonObject{{"operation","undo"}});
            check(Game.FindDecoration(paidId)!=null&&NativeGame.I(Game.Meta,"coins")==0,"撤销未恢复原装饰和金币");
            GD.Print("DECORATION_EXPERIENCE_VERIFIED live_catalog real_model placed_directory locate copy safe_remove");
        }finally{
            EndPlacement();_management?.Hide();Data.Root["snapshot"]=original;Data.Reindex();
            (_buildWorkspace,_buildX,_buildY,_buildOrientation,_buildDecorTheme,_buildDecorKind,_buildDecorationId,
                _decorationCatalogQuery,_decorationBudgetFilter,_decorationPlacedQuery,_decorationPlacedFilter,_decorationPlacedPage,_rememberedMapView)=directory;
            (_center,_distance,_pitch,_portraitAnchor,_buildCamera,_selection,_previousCameraSite)=camera;
            _cameraPast.Clear();_cameraPast.AddRange(past);_cameraForward.Clear();_cameraForward.AddRange(forward);
        }

        string Place(string kind,Vector2I cell,int orientation)
        {
            Game.Execute("decoration",new JsonObject{{"operation","place"},{"kind",kind},{"cx",cell.X},{"cy",cell.Y},{"orientation",orientation}});
            return NativeGame.S(Game.Meta["decorations"]!.AsArray().Last()!,"id");
        }
        ConfirmationDialog RemovalDialog()=>_management!.GetChildren().OfType<ConfirmationDialog>().Single(window=>window.Visible&&window.Title=="移除装饰");
        T Find<T>(string name) where T:Node=>(T)_managementBody!.FindChild(name,true,false)!;
        void Press(string name)=>Find<Button>(name).EmitSignal(BaseButton.SignalName.Pressed);
        void Choose(string name,int index){var menu=Find<OptionButton>(name);menu.Select(index);menu.EmitSignal(OptionButton.SignalName.ItemSelected,(long)index);}
        void Type(string name,string value){var input=Find<LineEdit>(name);input.Text=value;input.EmitSignal(LineEdit.SignalName.TextChanged,value);}
    }
}
