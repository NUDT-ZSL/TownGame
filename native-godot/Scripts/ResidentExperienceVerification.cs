using Godot;
using System;
using System.Linq;
using System.Text.Json.Nodes;

public partial class Main
{
    void VerifyResidentExperience(Action<bool,string> check)
    {
        var person=Data.People.OfType<JsonNode>().First(row=>Game.ResidentHome(row)!=null&&
            DirectoryActor(NativeGame.S(row,"person_id"))!=null);
        string id=NativeGame.S(person,"person_id");
        var actor=DirectoryActor(id)!;
        var home=Game.ResidentHome(person)!;
        string homeId=NativeGame.S(home,"building_id");
        var favorites=_residentFavorites.ToArray();
        bool favoritesLoaded=_residentFavoritesLoaded;
        var directory=(_residentDirectoryOpen,_residentDirectoryQuery,_residentDirectoryFilter,_residentDirectoryPage);
        var view=(_center,_pitch,_distance,_cameraFrom,_cameraTo,_cameraTransition,_portraitAnchor,_buildCamera,_selectedPet,
            _selectedResidence,_previousCameraSite,_selection,_sceneInspection,_residentLocatedText,_lastFollowedResidentId,_selectedSceneResident);
        var resident=(actor.InsideHome,actor.InsideHomeBuildingId,actor.Visual.Visible,actor.ResidentialActivity,actor.ReturningHome,actor.Index);
        var path=actor.Path.ToArray();
        var freight=Game.FreightState;
        var freightKeys=new[]{"driver_id","phase","source","status"};
        var freightValues=freightKeys.Select(key=>freight[key]?.DeepClone()).ToArray();
        var status=person["status"]?.DeepClone();
        try{
            StopResidentFollow();
            _residentFavoritesLoaded=true;
            _residentFavorites.Clear();
            person["status"]="在镇";
            actor.InsideHome=false;
            actor.Visual.Show();
            actor.ReturningHome=false;
            actor.ResidentialActivity="前往验收终点";
            actor.Path.Clear();
            var target=WorldData.Cell(new(62,40));
            actor.Path.Add(target);
            actor.Index=0;
            _residentDirectoryQuery=id;
            _residentDirectoryFilter=0;
            ShowManagement("家庭","residents");

            var filter=Find<OptionButton>("ResidentDirectoryFilter");
            var search=Find<LineEdit>("ResidentDirectoryQuery");
            check(filter.ItemCount==6&&filter.GetItemText(1)=="在户外 · 1"&&filter.GetItemText(3)=="驾驶货车 · 0",
                "村民位置筛选没有同步匹配人数");
            check(Row<Label>("ResidentNextStop").Text.Contains(ResidentSiteAt(target))&&
                Row<Label>("ResidentCurrentPlace").Text.Contains(ResidentSiteAt(actor.Position)),"当前地点和实际路线终点没有分别显示");
            Choose(1);
            Press(Row<Button>("ResidentFavorite"));
            Choose(ResidentFavoritesFilter);
            check(Row<Button>("ResidentFavorite").ButtonPressed&&filter.GetItemText(ResidentFavoritesFilter)=="已收藏 · 1",
                "收藏筛选没有保留已加星村民");
            using var config=new ConfigFile();
            config.SetValue("residents","favorites",new[]{id,id,"不在名册中的居民"});
            check(ReadResidentFavorites(config).SequenceEqual(new[]{id})&&ResidentFavoritesPath(1)!=ResidentFavoritesPath(2),
                "收藏读取未去除无效数据或未隔离槽位");
            search.Text="没有匹配的验收查询";
            search.EmitSignal(LineEdit.SignalName.TextChanged,search.Text);
            check(Find<Button>("ResidentDirectoryReset").IsVisibleInTree(),"空结果没有提供恢复操作");
            Press(Find<Button>("ResidentDirectoryReset"));
            check(search.Text==""&&filter.Selected==0&&!Find<Button>("ResidentDirectoryReset").IsVisibleInTree(),"清除未恢复全部村民");
            search.Text=id;
            search.EmitSignal(LineEdit.SignalName.TextChanged,id);

            actor.InsideHome=true;
            actor.InsideHomeBuildingId=homeId;
            actor.Visual.Hide();
            actor.ResidentialActivity="在家休息";
            _refreshManagementPage?.Invoke();
            check(filter.GetItemText(2)=="在家 · 1"&&filter.GetItemText(1)=="在户外 · 0","在家状态没有更新筛选数量");
            Choose(2);
            Press(Row<Button>("LocateDirectoryResident"));
            check(_residentSceneTools.Visible&&_residentSceneTitle.Text.Contains("已定位住宅")&&_residentSceneStatus.Text.Contains(ResidenceName(home))&&
                _residentSceneInterior.Visible&&_center.IsEqualApprox(TownProjectCenter(home)),"室内定位没有显示实际住宅和切面入口");
            Press(_residentSceneInterior);
            check(_managementEntryTarget=="interior:"+homeId,"室内定位按钮没有打开正确住宅切面");

            check(_freightTruck!=null,"缺少实际货车场景对象");
            var truck=_freightTruck!;
            freight["driver_id"]=id;
            freight["phase"]="to_source";
            freight["source"]="industry";
            freight["status"]="前往装货";
            check(LocateResident(id)&&_residentSceneTitle.Text.Contains("已定位货车")&&
                _residentSceneRoute.Text.Contains(AutonomyZoneName("industry"))&&!_residentSceneInterior.Visible&&
                _center.IsEqualApprox(new(truck.Position.X,0,truck.Position.Z)),"驾驶定位没有落在货车或没有给出货运目的地");
            Press(_residentSceneFollow);
            check(_followResidentId==id&&_residentSceneFollow.Text=="停止跟随"&&_residentSceneTitle.Text.Contains("正在跟随"),
                "场景跟随没有显示目标和停止入口");
            Press(_residentSceneFollow);
            check(_followResidentId==""&&_residentSceneFollow.Text=="继续跟随","停止后缺少继续跟随动作");
            Press(_residentSceneFollow);
            check(_followResidentId==id,"继续跟随没有恢复此前的目标");
            StopResidentFollow();
            person["status"]="离镇";
            check(!LocateResident(id)&&_selection.Contains("已离镇"),"未提供无法定位的实际原因");
            GD.Print("RESIDENT_EXPERIENCE_VERIFIED live_filters route_labels favorites locator_actions follow_resume");

            void Choose(int index){filter.Select(index);filter.EmitSignal(OptionButton.SignalName.ItemSelected,index);}
        }finally{
            StopResidentFollow();
            _residentFavorites.Clear();
            _residentFavorites.UnionWith(favorites);
            _residentFavoritesLoaded=favoritesLoaded;
            (_residentDirectoryOpen,_residentDirectoryQuery,_residentDirectoryFilter,_residentDirectoryPage)=directory;
            (_center,_pitch,_distance,_cameraFrom,_cameraTo,_cameraTransition,_portraitAnchor,_buildCamera,_selectedPet,
                _selectedResidence,_previousCameraSite,_selection,_sceneInspection,_residentLocatedText,_lastFollowedResidentId,_selectedSceneResident)=view;
            (actor.InsideHome,actor.InsideHomeBuildingId,actor.Visual.Visible,actor.ResidentialActivity,actor.ReturningHome,actor.Index)=resident;
            actor.Path.Clear();
            actor.Path.AddRange(path);
            for(int i=0;i<freightKeys.Length;i++)freight[freightKeys[i]]=freightValues[i];
            person["status"]=status;
            MarkLocatedResident(_selectedSceneResident);
            _management?.Hide();
            RefreshScenePetCare();
        }

        T Find<T>(string name) where T:Control=>(T)_managementBody!.FindChild(name,true,false)!;
        T Row<T>(string name) where T:Control=>(T)Find<Control>("ResidentDirectory_"+id).FindChild(name,true,false)!;
        static void Press(Button button)=>button.EmitSignal(BaseButton.SignalName.Pressed);
    }
}
