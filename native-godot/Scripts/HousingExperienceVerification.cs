using Godot;
using System;
using System.Linq;
using System.Text.Json.Nodes;

public partial class Main
{
    void VerifyHousingExperience(Action<bool,string> check)
    {
        var original=Data.Snapshot;
        var filterState=(_housingQuery,_housingFilter,_housingPage,_housingWaitOpen,_residentDirectoryOpen,_selectedResidence,
            _residentDirectoryQuery,_residentDirectoryFilter,_residentDirectoryPage);
        var cameraState=(_center,_distance,_pitch,_buildCamera,_portraitAnchor,_cameraFrom,_cameraTo,_cameraTransition,
            _previousCameraSite,_selection,_selectedPet,_sceneInspection,_selectedSceneResident);
        var past=_cameraPast.ToArray();var forward=_cameraForward.ToArray();
        RoadAgent? actor=null;
        (bool Inside,string Home,string Activity)? actorState=null;
        try{
            Data.Root["snapshot"]=original.DeepClone();Data.Reindex();
            (_housingQuery,_housingFilter,_housingPage,_housingWaitOpen)=("",0,0,false);
            var occupied=Game.Rows("town_buildings").OfType<JsonNode>().Where(home=>Game.ResidentHomeCapacity(home)>0&&Game.HomeRoadEntrance(home)!=null&&
                Game.HomeMembers(NativeGame.S(home,"building_id")).Any()).Take(2).ToArray();
            check(occupied.Length==2,"住房验收缺少两栋已经入住房屋");
            var first=occupied[0];var vacant=occupied[1];
            string firstId=NativeGame.S(first,"building_id"),vacantId=NativeGame.S(vacant,"building_id");
            var displaced=Game.HomeMembers(vacantId).ToArray();
            foreach(var member in displaced){member["home_building_id"]="";member["housing_status"]="等待可容纳全家的住宅";}
            var waiting=Game.ResidentHousingWaitlist().Single(group=>group.Members.Contains(displaced[0]));
            check(waiting.VacantHomes.Contains(vacant)&&!Game.HomeMembers(vacantId).Any(),"空屋预览未按真实安置条件筛选或提前安置了家庭");

            ShowManagement("家庭","residence");
            var summary=Find<Label>("HousingDirectorySummary");
            check(summary.Text.Contains("可分配空屋")&&summary.Text.Contains("道路未连通"),"总览没有区分空屋与路网状态");
            Choose("HousingDirectoryFilter",2);
            check(_managementBody!.FindChild("HousingDirectory_"+vacantId,true,false)!=null&&
                _managementBody.FindChild("HousingDirectory_"+firstId,true,false)==null,"可分配空屋筛选混入已入住住宅");
            var query=Find<LineEdit>("HousingDirectoryQuery");
            query.Text="无匹配住宅验收";query.EmitSignal(LineEdit.SignalName.TextChanged,query.Text);
            check(Find<Label>("HousingDirectoryPage").Text.StartsWith("0 /")&&_managementBody.FindChild("HousingDirectory_"+vacantId,true,false)==null,
                "住房搜索没有清除旧结果");
            Press("HousingDirectoryReset");
            check(query.Text==""&&Find<OptionButton>("HousingDirectoryFilter").Selected==0,"住房筛选没有恢复默认");
            Find<Button>("HousingWaitlistToggle").ButtonPressed=true;
            var groups=Find<OptionButton>("HousingWaitlistFamily");
            int waitingIndex=Array.IndexOf(Game.ResidentHousingWaitlist().Select(group=>NativeGame.S(group.Members[0],"person_id")).ToArray(),
                NativeGame.S(waiting.Members[0],"person_id"));
            check(groups.ItemCount>waitingIndex&&waitingIndex>=0,"待安置列表缺少验收家庭");
            Choose("HousingWaitlistFamily",waitingIndex);
            check(Find<Label>("HousingWaitlistSummary").Text.Contains($"需要 {waiting.Members.Length} 张床")&&
                Find<OptionButton>("HousingWaitlistVacancies").ItemCount==waiting.VacantHomes.Length,"待安置卡未显示容量及匹配空屋");

            var person=Game.HomeMembers(firstId).First();
            string personId=NativeGame.S(person,"person_id"),name=NativeGame.S(person,"name");
            actor=DirectoryActor(personId)??throw new InvalidOperationException("住房验收缺少居民角色");
            actorState=(actor.InsideHome,actor.InsideHomeBuildingId,actor.ResidentialActivity);
            actor.InsideHome=true;actor.InsideHomeBuildingId=firstId;actor.ResidentialActivity="在家休息";
            SelectResidence(firstId);
            var row=Find<PanelContainer>("HouseholdMember_"+personId);
            check(row.FindChildren("*","Label",true,false).OfType<Label>().Any(label=>label.Text.Contains("在家休息"))&&
                row.FindChild("HouseholdProfile",true,false) is Button&&row.FindChild("HouseholdLocateMember",true,false) is Button {Disabled:false},
                "家庭成员卡没有显示真实在家状态或操作入口");
            var entrance=Game.HomeRoadEntrance(first)!.Value;
            check(Find<Label>("HouseholdEntrance").Text.Contains($"{entrance.X}, {entrance.Y}"),"住宅未显示实际连通道路格");
            Press("HouseholdLocateEntrance");
            check(_center.IsEqualApprox(ResidenceDoor(first,entrance))&&_management?.Visible==false,"门口定位没有到达真实入口");
            SelectResidence(firstId);
            Press("HouseholdEntranceMap");
            check(_tab=="地图"&&_managementEntryTarget==$"cell:{entrance.X},{entrance.Y}","住宅入口未跳到地图上的对应道路格");

            ShowManagement("家庭","interior:"+firstId,()=>_selectedResidence=firstId);
            var camera=Find<Camera3D>("ResidenceInteriorCamera");
            var ring=Find<MeshInstance3D>("ResidenceInteriorFocusRing");
            Choose("ResidenceInteriorPreset",1);
            check(camera.Size<5&&Find<Label>("ResidenceInteriorViewStatus").Text.Contains("餐桌"),"室内餐桌视图未调整切面镜头");
            var memberFocus=Find<OptionButton>("ResidenceInteriorMemberFocus");
            int memberIndex=Enumerable.Range(1,memberFocus.ItemCount-1).First(index=>memberFocus.GetItemText(index)==name);
            Choose("ResidenceInteriorMemberFocus",memberIndex);
            check(ring.Visible&&Find<Label>("ResidenceInteriorViewStatus").Text.Contains(name),"成员焦点没有高亮真实在家的居民");
            Press("ResidenceInteriorReset");
            check(!ring.Visible&&Mathf.IsEqualApprox(camera.Size,7.6f),"回到全屋没有重置局部镜头");
            GD.Print("HOUSING_EXPERIENCE_VERIFIED occupancy search vacancy_fit live_members exact_door room_views");
        }finally{
            if(actor!=null&&actorState is { } previousActorState)
                (actor.InsideHome,actor.InsideHomeBuildingId,actor.ResidentialActivity)=previousActorState;
            Data.Root["snapshot"]=original;Data.Reindex();
            _management?.Hide();
            (_housingQuery,_housingFilter,_housingPage,_housingWaitOpen,_residentDirectoryOpen,_selectedResidence,
                _residentDirectoryQuery,_residentDirectoryFilter,_residentDirectoryPage)=filterState;
            (_center,_distance,_pitch,_buildCamera,_portraitAnchor,_cameraFrom,_cameraTo,_cameraTransition,
                _previousCameraSite,_selection,_selectedPet,_sceneInspection,_selectedSceneResident)=cameraState;
            _cameraPast.Clear();_cameraPast.AddRange(past);_cameraForward.Clear();_cameraForward.AddRange(forward);
            UpdateCamera();
        }

        T Find<T>(string name) where T:Node=>(T)_managementBody!.FindChild(name,true,false)!;
        void Press(string name)=>Find<Button>(name).EmitSignal(BaseButton.SignalName.Pressed);
        void Choose(string name,int index){var option=Find<OptionButton>(name);option.Select(index);option.EmitSignal(OptionButton.SignalName.ItemSelected,(long)index);}
    }
}
