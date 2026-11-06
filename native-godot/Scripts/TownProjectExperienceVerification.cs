using Godot;
using System;
using System.Linq;
using System.Text.Json.Nodes;

public partial class Main
{
    void VerifyTownProjectExperience(Action<bool,string> check)
    {
        var original=Data.Snapshot;
        var view=(_buildWorkspace,_buildTownId,_townProjectQuery,_townProjectFilter,_townProjectOrder,_townProjectPage,_rememberedMapView);
        try{
            Data.Root["snapshot"]=original.DeepClone();Data.Reindex();
            (_buildWorkspace,_buildTownId,_townProjectQuery,_townProjectFilter,_townProjectOrder,_townProjectPage)=
                (2,"","",TownProjectFilter.All,TownProjectOrder.Soonest,0);
            foreach(var building in Game.Rows("town_buildings").OfType<JsonNode>())building["status"]="ready";
            var candidates=Game.ConnectedRoads.SelectMany(road=>new NativeGame.Cell[]{new(road.X-1,road.Y),new(road.X,road.Y-1),
                new(road.X+1,road.Y),new(road.X,road.Y+1)}).Distinct().OrderBy(cell=>cell.Y).ThenBy(cell=>cell.X).ToArray();
            var fixtures=new JsonNode[10];
            for(int index=0;index<fixtures.Length;index++){
                var cell=candidates.First(c=>Game.PlacementReason("bus_stop",c.X,c.Y)=="");
                Game.PlaceTown("bus_stop",cell.X,cell.Y,community:true);
                var project=Game.Rows("town_buildings").Last()!;
                project["finish_ts"]=Game.Now+NativeGame.Hour*(index+2);
                fixtures[index]=project;
            }
            var next=fixtures[7];
            next["finish_ts"]=Game.Now+3*60;
            string id=NativeGame.S(next,"building_id");
            var house=Game.Rows("town_buildings").OfType<JsonNode>().First(building=>Game.HomeMembers(NativeGame.S(building,"building_id")).Any());
            string homeId=NativeGame.S(house,"building_id");

            ShowManagement("建造","projects");
            check(Find<Label>("TownProjectTotals").Text.Contains("施工中 10 项")&&
                Find<Label>("TownProjectNextCompletion").Text.Contains("3 分钟"),"工程名册没有展示实际施工总量和下一项完工倒计时");
            var picker=Find<OptionButton>("TownProjectPicker");
            check(picker.ItemCount==TownProjectPageSize&&_buildTownId==id,"即将完工排序或工程分页错误");
            Press("TownProjectFollowing");
            check(Find<Label>("TownProjectResults").Text.Contains("第 2 /")&&_buildTownId!=id,"下一页没有切换工程条目");
            Choose("TownProjectFilter",2);
            check(!Find<Label>("ConstructionStatus").Text.StartsWith("施工中"),"已完工筛选显示了施工项目");
            Press("TownProjectJumpNext");
            check(_buildTownId==id&&Find<OptionButton>("TownProjectFilter").Selected==0,"从完工概览无法查看被筛选隐藏的工程");
            Choose("TownProjectSort",2);
            check(_buildTownId==id,"切换排序丢失已选工程");
            var search=Find<LineEdit>("TownProjectQuery");
            SetQuery("无匹配工程验证");
            check(!Find<Control>("TownProjectDetails").Visible&&Find<Label>("TownProjectResults").Text.Contains("没有匹配"),"空搜索结果仍显示旧工程详情");
            SetQuery($"{TownProjectName(next)} {NativeGame.I(next,"cx")},{NativeGame.I(next,"cy")}");
            check(_buildTownId==id&&picker.ItemCount==1,"工程搜索没有同时匹配名称和坐标");

            var access=Game.ConstructionRoadAccess(next);
            var linkedRoad=access.Road??throw new InvalidOperationException("项目测试缺少临街道路");
            check(access.Connected>0&&Find<Label>("ConstructionRoadStatus").Text.Contains($"{linkedRoad.X}, {linkedRoad.Y}"),"项目详情未显示真实临街道路格");
            Press("TownProjectEntranceMap");
            check(_tab=="地图"&&Find<TownMap>("TownNavigationMap").InspectedCell==linkedRoad,"临街道路入口未跳到地图中的真实道路格");
            OpenTownProject(id);
            Press("TownProjectFootprintMap");
            check(Find<TownMap>("TownNavigationMap").InspectedCell==new NativeGame.Cell(NativeGame.I(next,"cx"),NativeGame.I(next,"cy")),
                "工程地块未跳到真实建筑占地");

            OpenTownProject(homeId);
            check(_buildTownId==homeId&&Find<LineEdit>("TownProjectQuery").Text=="","工程直达没有清除冲突筛选");
            Find<CheckButton>("TownProjectRemovalToggle").ButtonPressed=true;
            check(Find<Button>("TownProjectRemove").Disabled&&Find<Label>("TownProjectDemolitionQuote").Text.Contains("固定住宅"),"有人居住的住宅未展示拆除限制");
            OpenTownProject(id);
            var removal=Find<Button>("TownProjectRemove");
            check(!removal.IsVisibleInTree(),"打开工程默认暴露了拆除操作");
            Find<CheckButton>("TownProjectRemovalToggle").ButtonPressed=true;
            check(!removal.Disabled&&Find<Label>("TownProjectDemolitionQuote").Text.Contains("取消当前施工"),"拆除区未展示实际报价和施工影响");
            Press("TownProjectRemove");
            var dialog=_management!.GetChildren().OfType<ConfirmationDialog>().Single(window=>window.Visible&&window.Title=="拆除城镇建筑");
            next["status"]="ready";
            dialog.EmitSignal(AcceptDialog.SignalName.Confirmed);
            check(Game.FindTownBuilding(id)!=null,"拆除确认接受了已经变化的工程报价");

            var portal=Game.Portal("estate");
            var disconnected=fixtures.First(project=>Game.ConstructionRoadAccess(project).Road is NativeGame.Cell cell&&
                Math.Abs(cell.X-portal.X)+Math.Abs(cell.Y-portal.Y)>1);
            var isolated=Game.ConstructionRoadAccess(disconnected).Road!.Value;
            string disconnectedId=NativeGame.S(disconnected,"building_id");
            var blocked=Data.Snapshot.DeepClone();
            var roads=blocked["roads"]!.AsArray();
            foreach(var row in roads.OfType<JsonNode>().ToArray()){
                var cell=new NativeGame.Cell(NativeGame.I(row,"cx"),NativeGame.I(row,"cy"));
                if(cell!=portal&&cell!=isolated)roads.Remove(row);
            }
            Data.Root["snapshot"]=blocked;Data.Reindex();
            OpenTownProject(disconnectedId);
            Choose("TownProjectFilter",3);
            check(_buildTownId==disconnectedId&&Find<Button>("TownProjectEntranceMap").Text.Contains("修复"),"待接路工程未提供真实断路的修复入口");
            Press("TownProjectEntranceMap");
            check(_managementEntryTarget==$"repair:{isolated.X},{isolated.Y}"&&Find<Control>("RoadReconnectPreview").Visible&&
                _managementFocusTarget?.Name=="RoadReconnectPreview","断路入口未打开已有的报价预览");
            GD.Print("TOWN_PROJECT_EXPERIENCE_VERIFIED search_filters stable_pages live_queue actual_frontage guarded_demolition");
        }finally{
            _management?.Hide();Data.Root["snapshot"]=original;Data.Reindex();
            (_buildWorkspace,_buildTownId,_townProjectQuery,_townProjectFilter,_townProjectOrder,_townProjectPage,_rememberedMapView)=view;
        }

        T Find<T>(string name) where T:Node=>(T)_managementBody!.FindChild(name,true,false)!;
        void Press(string name)=>Find<Button>(name).EmitSignal(BaseButton.SignalName.Pressed);
        void Choose(string name,int index){var option=Find<OptionButton>(name);option.Select(index);option.EmitSignal(OptionButton.SignalName.ItemSelected,(long)index);}
        void SetQuery(string term){var input=Find<LineEdit>("TownProjectQuery");input.Text=term;input.EmitSignal(LineEdit.SignalName.TextChanged,term);}
    }
}
