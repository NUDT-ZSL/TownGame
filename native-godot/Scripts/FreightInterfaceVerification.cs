using Godot;
using System;
using System.Linq;
using System.Text.Json.Nodes;

public partial class Main
{
    void VerifyFreightInterface(Action<bool,string> check)
    {
        var original=Data.Snapshot;
        var presentation=(_freightDispatchReadiness,_freightDispatchCargo,_autonomyAttentionOnly,_freightReceiptsExpanded);
        var view=(_center,_distance,_buildCamera,_portraitAnchor,_selection,_previousCameraSite);
        var past=_cameraPast.ToArray();var forward=_cameraForward.ToArray();
        var truck=_freightTruck!;
        var route=(truck.Path.ToArray(),truck.Index,_freightRoutePhase,_freightHasRoute,_freightWork);
        bool routePending=_routeRefreshPending.Remove(truck);
        try{
            var fixture=new NativeGame(NativeContent.Load());
            Data.Root["snapshot"]=fixture.State.DeepClone();
            Data.Reindex();
            Game.CancelFreight("验收准备");
            Game.Meta.Remove("native_freight_receipts");
            Game.Jobs.Clear();
            foreach(var person in Game.People.OfType<JsonObject>())person["job_id"]="";
            Game.Commons["enabled"]=true;
            Game.Meta["sim_ts"]=Math.Floor(Game.Now/NativeGame.Day)*NativeGame.Day+10*NativeGame.Hour;
            foreach(string zone in new[]{"fields","ranch","industry","market"})Game.SetZone(zone,1);
            Game.Commons["stock"]!["food"]=Game.People.Count(person=>NativeGame.S(person!,"role")=="居民")*3+14;
            Game.Commons["stock"]!["wood"]=7;
            Game.FreightState["next_trip_ts"]=0;
            Game.FreightState["dispatch_priority"]="food";
            _freightDispatchReadiness=_freightDispatchCargo=0;
            _autonomyAttentionOnly=_freightReceiptsExpanded=false;
            Game.RefreshFreight();
            check(Game.BoardFreight(),"验收司机无法接到实际运输车次");
            string source=NativeGame.S(Game.FreightState,"source");
            var stop=FreightSceneStop(source).Cell;
            check(stop.HasValue,"实际装货街区没有可定位停靠点");
            var start=FreightSceneStop("estate").Cell??Data.Portals["estate"];
            var path=FreightRoadRoute(truck,start,stop!.Value);
            check(path.Count>1,"验收没有找到实际货运路线");
            truck.Path.Clear();truck.Path.AddRange(path);truck.Index=path.Count/2;
            _freightRoutePhase="to_source";_freightHasRoute=true;
            ShowManagement("自治");
            check(Find<Label>("TownFreightJourneyStage_to_source").Text.StartsWith("●",StringComparison.Ordinal)&&
                Find<Label>("TownFreightJourneyProgressText").Text.Contains($"{truck.Index} / {path.Count}")&&
                Math.Abs(Find<ProgressBar>("TownFreightJourneyProgress").Value-truck.Index/(double)path.Count)<.001,
                "当前阶段或本段路线进度未使用真实路点");

            Choose("TownFreightDispatchReadiness",2);
            Choose("TownFreightDispatchCargo",2);
            var visible=Find<VBoxContainer>("TownFreightDispatchRows");
            check(visible.GetChildCount()==1&&visible.GetChild(0).Name=="TownFreightDispatchLine_industry"&&
                visible.GetChild(0).FindChildren("*","Label",true,false).OfType<Label>().Any(label=>label.Text.Contains("保护 8 份 · 余量 0 份 · 本趟可装 0 / 4 份")),
                "候运筛选或可装载计算未反映公共木材储备");
            Find<Button>("TownFreightDispatchReset").EmitSignal(BaseButton.SignalName.Pressed);
            check(visible.GetChildCount()==3&&Find<Label>("TownFreightDispatchCount").Text.Contains("3 / 3"),"候运筛选无法复位");

            var locate=Find<Button>("TownFreightStop1");
            check(!locate.Disabled&&locate.TooltipText.Contains($"{stop.Value.X}, {stop.Value.Y}"),"装货点没有显示真实道路格");
            locate.EmitSignal(BaseButton.SignalName.Pressed);
            check(_center==WorldData.Cell(stop.Value)&&_management?.Visible==false,"货运按钮没有定位实际装货点");

            Game.FreightState["phase"]="loading";
            _freightWork=1.5;
            ShowManagement("自治");
            check(Math.Abs(Find<ProgressBar>("TownFreightJourneyProgress").Value-.5)<.001,"装货进度未反映当前装车时间");
            check(Game.LoadFreight()&&Game.FreightReceipts().Length==0,"装货途中提前写入交付回执");
            int amount=NativeGame.I(Game.FreightState,"cargo_count"),gold=NativeGame.I(Game.Commons,"funds");
            Game.FreightState["phase"]="unloading";
            check(Game.DeliverFreight()&&!Game.DeliverFreight()&&Game.FreightReceipts().Length==1,"卸货没有记录回执，或重复回调多写了一条");
            var receipt=Game.FreightReceipts().Single();
            check(receipt.Source==source&&receipt.Count==amount&&receipt.Gold==NativeGame.I(Game.Commons,"funds")-gold,
                "交付回执与真实货量或公共收入不一致");
            fixture.State["meta"]=Data.Meta.DeepClone();
            check(fixture.FreightReceipts().SequenceEqual(Game.FreightReceipts()),"复制存档后无法读取交付回执");
            _refreshManagementPage?.Invoke();
            Find<CheckButton>("TownFreightReceiptToggle").ButtonPressed=true;
            check(Find<Control>($"TownFreightReceipt_{receipt.Trip}") is {Visible:true},"已结算货运未出现在交付回执界面");

            Game.SetZone("ranch",0);
            Game.Commons["enabled"]=false;
            ShowManagement("自治");
            Find<CheckButton>("TownDistrictAttentionOnly").ButtonPressed=true;
            check(Find<Control>("TownAutonomyDistrict_ranch").Visible&&!Find<Control>("TownAutonomyDistrict_fields").Visible&&
                Find<Label>("TownDistrictAttentionCount").Text.Contains("自治暂停"),"仅看待处理未区分未开放街区与全镇暂停");
            Game.SetZone("market",0);
            ShowManagement("自治");
            check(Find<Button>("TownFreightStop2").Disabled&&Find<Label>("TownFreightStopSummary").Text.Contains("尚未开放"),
                "未开放的交付点仍可定位或没有显示原因");
            GD.Print("FREIGHT_INTERFACE_VERIFIED phase_progress dispatch_filters receipts actual_stops district_attention");
        }finally{
            Data.Root["snapshot"]=original;Data.Reindex();
            (_freightDispatchReadiness,_freightDispatchCargo,_autonomyAttentionOnly,_freightReceiptsExpanded)=presentation;
            (_center,_distance,_buildCamera,_portraitAnchor,_selection,_previousCameraSite)=view;
            _cameraPast.Clear();_cameraPast.AddRange(past);_cameraForward.Clear();_cameraForward.AddRange(forward);
            truck.Path.Clear();truck.Path.AddRange(route.Item1);
            (truck.Index,_freightRoutePhase,_freightHasRoute,_freightWork)=(route.Index,route._freightRoutePhase,route._freightHasRoute,route._freightWork);
            if(routePending)_routeRefreshPending.Add(truck);
            _management?.Hide();UpdateCamera();
        }

        T Find<T>(string name) where T:Node=>(T)_managementBody!.FindChild(name,true,false)!;
        void Choose(string name,int index){var option=Find<OptionButton>(name);option.Select(index);option.EmitSignal(OptionButton.SignalName.ItemSelected,(long)index);}
    }
}
