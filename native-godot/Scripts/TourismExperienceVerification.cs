using Godot;
using System;
using System.Linq;
using System.Text.Json.Nodes;

public partial class Main
{
    void VerifyTourismExperience(Action<bool,string> check)
    {
        var snapshot=Data.Snapshot;
        var display=(_tourismFilter,_tourismSort,_touristPage,_tourismAttraction,_tourismItinerariesOpen,_tourismReceiptsOpen,_rememberedMapView);
        var camera=(_center,_distance,_pitch,_buildCamera,_portraitAnchor,_cameraFrom,_cameraTo,_cameraTransition,
            _previousCameraSite,_selection,_selectedPet,_sceneInspection);
        var past=_cameraPast.ToArray();var forward=_cameraForward.ToArray();
        RoadAgent? travelerActor=null;
        try{
            Data.Root["snapshot"]=snapshot.DeepClone();Data.Reindex();
            foreach(var old in Game.People.OfType<JsonNode>().Where(person=>NativeGame.S(person,"role")=="游客").ToArray())
                Game.People.Remove(old);
            Game.SetZone("market",0);
            Game.Meta.Remove("native_tourism_receipts");
            Game.Meta["native_tourism"]=new JsonObject{
                ["estate"]=new JsonObject{{"planned",1}},
                ["market"]=new JsonObject{{"planned",5},{"arrived",1},{"coins",24},{"closed",1},{"disconnected",1},{"timeouts",1}},
                ["park"]=new JsonObject{{"planned",2},{"arrived",2},{"coins",8}}
            };
            var traveler=MakeTourist("tourism-check-live","estate");traveler["native_tourism_planned"]=true;
            Game.People.Add(traveler);
            Game.People.Add(MakeTourist("tourism-check-blocked","market"));
            var departing=MakeTourist("tourism-check-leaving","station");departing["status"]="离镇";Game.People.Add(departing);
            var position=WorldData.Cell(Data.Portals["estate"])+Vector3.Right*1.4f;
            travelerActor=new RoadAgent{Town=this,Ident="tourism-check-live",Position=position};
            AddChild(travelerActor);Agents.Add(travelerActor);
            (_tourismFilter,_tourismSort,_touristPage,_tourismAttraction,_tourismItinerariesOpen,_tourismReceiptsOpen)=(0,0,0,"",false,false);

            ShowManagement("自治");
            Press("TownTourismLink");
            check(_managementEntryTarget=="tourism"&&_managementFocusTarget?.Name=="TownTourismHeading","自治游客入口没有跳到游览标题");
            check(Find<Label>("TownTourismTotals").Text.Contains("计划 8 次 · 到达 3 次 · 收入 32 金币")&&
                Math.Abs(Find<ProgressBar>("TownTourismArrivalRate").Value-.375)<.001&&
                Find<Label>("TownTourismLive").Text.Contains("在途 1 · 受阻 1 · 参观 0 · 返程 1"),"游览汇总没有区分累计进度与实时状态");
            var list=Find<OptionButton>("TownTourismAttraction");
            check(list.GetItemText(0).Contains(AutonomyZoneName("park")),"按实际到达次数排序失败");
            Choose("TownTourismSort",1);
            check(list.GetItemText(0).Contains(AutonomyZoneName("market"))&&list.GetItemText(0).Contains("24 金币"),"按实际收入排序失败");
            Choose("TownTourismFilter",2);
            check(list.ItemCount>0&&!Enumerable.Range(0,list.ItemCount).Any(index=>list.GetItemText(index).Contains(AutonomyZoneName("estate"))),
                "不可游览筛选仍显示通行景点");
            Choose("TownTourismAttraction",0);
            check(Find<Label>("TownTourismEligibility").Text.Contains("景点尚未开放")&&
                Find<Label>("TownTourismBlocks").Text.Contains("未开放 1 · 道路受阻 1 · 其他停留到期 1"),"景点详情没有区分现况和过往受阻原因");
            Press("TownTourismMap");
            check(_tab=="地图"&&_managementEntryTarget=="district:market","景点地图未打开对应街区");
            ShowManagement("自治","tourism");Press("TownTourismScene");
            check(_center==WorldData.Cell(Data.Portals["market"])&&_management?.Visible==false,"场景定位未找到真实街区入口");

            ShowManagement("自治","tourism");
            Find<CheckButton>("TownTourismItineraries").ButtonPressed=true;
            var row=Find<Control>("TownTourist_tourism-check-live");
            var locate=(Button)row.FindChild("TownTouristLocate",true,false)!;
            check(!locate.Disabled&&row.FindChildren("*","Label",true,false).OfType<Label>().Any(label=>label.Text.Contains("前往景点")),
                "当前游览行没有显示实际目的地状态或场景定位");
            var blocked=Find<Control>("TownTourist_tourism-check-blocked");
            check(((Button)blocked.FindChild("TownTouristLocate",true,false)!).Disabled,"没有场景实体的游客仍可定位");
            locate.EmitSignal(BaseButton.SignalName.Pressed);
            check(_center==position&&_management?.Visible==false,"旅人定位没有到达实际场景位置");

            int coins=NativeGame.I(Game.Meta,"coins");
            check(Game.TouristReceipts().Length==0&&!Game.ReportTouristArrival(traveler,"market"),"旧汇总或错误景点生成了新回执");
            check(Game.ReportTouristArrival(traveler,"estate")&&!Game.ReportTouristArrival(traveler,"estate"),"游客实际到达没有结算或重复结算");
            var receipt=Game.TouristReceipts().Single();
            check(receipt.Id=="tourism-check-live"&&receipt.Zone=="estate"&&receipt.ArrivedAt==Game.Now&&receipt.Coins==NativeGame.I(Game.Meta,"coins")-coins,
                "到达回执与实际金币入账不符");
            ShowManagement("自治","tourism");Find<CheckButton>("TownTourismReceipts").ButtonPressed=true;
            check(Find<Control>("TownTourismReceiptRows").GetChildCount()==1&&Find<Label>("TownTourismLive").Text.Contains("参观 1"),
                "旅人到达后未更新实时状态和明细");
            for(int index=0;index<=NativeGame.TouristReceiptLimit;index++){
                var extra=MakeTourist($"tourism-check-extra-{index}","estate");
                Game.People.Add(extra);
                check(Game.ReportTouristArrival(extra,"estate"),"后续游客未结算");
                Game.People.Remove(extra);
            }
            var reload=new NativeGame(Data.Root.DeepClone().AsObject());
            var receipts=reload.TouristReceipts();
            check(receipts.Length==NativeGame.TouristReceiptLimit&&receipts[0].Id==$"tourism-check-extra-{NativeGame.TouristReceiptLimit}"&&
                receipts.SequenceEqual(Game.TouristReceipts())&&!receipts.Contains(receipt),"到达记录重载后顺序改变或未清理旧记录");
            GD.Print("TOURISM_EXPERIENCE_VERIFIED totals attraction_filters visit_location receipts_bounded_reload");
        }finally{
            if(travelerActor!=null){Agents.Remove(travelerActor);travelerActor.QueueFree();}
            _management?.Hide();
            Data.Root["snapshot"]=snapshot;Data.Reindex();
            (_tourismFilter,_tourismSort,_touristPage,_tourismAttraction,_tourismItinerariesOpen,_tourismReceiptsOpen,_rememberedMapView)=display;
            (_center,_distance,_pitch,_buildCamera,_portraitAnchor,_cameraFrom,_cameraTo,_cameraTransition,
                _previousCameraSite,_selection,_selectedPet,_sceneInspection)=camera;
            _cameraPast.Clear();_cameraPast.AddRange(past);_cameraForward.Clear();_cameraForward.AddRange(forward);
            UpdateCamera();
        }

        JsonObject MakeTourist(string id,string zone)=>new(){{"person_id",id},{"name","验收旅人"},{"role","游客"},
            {"destination",zone},{"status","游览"},{"leave_ts",Game.Now+NativeGame.Hour},{"exit_ts",Game.Now+2*NativeGame.Hour}};
        T Find<T>(string name) where T:Node=>(T)_managementBody!.FindChild(name,true,false)!;
        void Press(string name)=>Find<Button>(name).EmitSignal(BaseButton.SignalName.Pressed);
        void Choose(string name,int index){var option=Find<OptionButton>(name);option.Select(index);option.EmitSignal(OptionButton.SignalName.ItemSelected,(long)index);}
    }
}
