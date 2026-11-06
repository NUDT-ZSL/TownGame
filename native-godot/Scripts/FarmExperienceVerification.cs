using Godot;
using System;
using System.Linq;
using System.Text.Json.Nodes;

public partial class Main
{
    void VerifyFarmExperience(Action<bool,string> check)
    {
        var original=Data.Snapshot;
        var farm=(_activeFarmPlot,_plannedFarmCrop,_farmScenePlot);
        var camera=(_center,_distance,_pitch,_buildCamera,_portraitAnchor,_cameraTransition,_previousCameraSite,_selection,_selectedPet);
        var past=_cameraPast.ToArray();
        var forward=_cameraForward.ToArray();
        try{
            Data.Root["snapshot"]=original.DeepClone();Data.Reindex();
            Game.SetZone("fields",1);
            Game.Meta.Remove("farm_plots");
            Game.Meta["native_farm_soil"]=new JsonObject{
                {"1",new JsonObject{{"moisture",68d},{"last_crop","谷物"},{"repeats",2},{"updated",Game.Now}}}
            };
            const string id="farm-experience-order";
            double maturity=Game.FarmMaturity;
            Game.Rows("orders").Add(new JsonObject{
                {"order_id",id},{"npc","农田验收"},{"kind","item"},{"item","谷物"},{"quantity",1},
                {"created_ts",Game.Now},{"expiry_ts",maturity+NativeGame.Day},{"status","进行中"},{"reward",20}
            });
            Game.Execute("farm_plot",new JsonObject{{"operation","sow"},{"plot",1},{"crop","谷物"},{"order_id",id}});
            Game.Meta["sim_ts"]=Game.Now+Game.FarmGrowthDuration*.5;
            _activeFarmPlot=1;
            UpdateFarmGrowthView();
            ShowManagement("环境","farm-plots");
            check(Find<Button>("FarmPlotSelect1").Text.Contains("谷物")&&Find<Button>("FarmPlotSelect2").Text.Contains("空地")&&
                Find<Label>("FarmPlotStatus1").Text.Contains("湿度 68%"),"农田总览没有同时反映各地块的实际作物与湿度");
            check(Find<ProgressBar>("FarmPlotGrowth").Value==50&&Find<Label>("FarmPlotCountdown").Text.Contains("成熟")&&
                Find<Label>("FarmPlotHarvestProjection").Text.Contains("预计为农田验收预留 1 份"),"收获计时、进度或订单分配预览与实况不符");

            Find<Button>("FarmPlotCompare").ButtonPressed=true;
            var rotation=Find<Button>("FarmCropChoice1");
            check(rotation.IsVisibleInTree()&&rotation.Text.Contains("轮作增产 1 份")&&Find<Button>("FarmCropChoice0").Text.Contains("连续种植"),
                "作物比较没有按收获后的轮作条件报价");
            rotation.EmitSignal(BaseButton.SignalName.Pressed);
            check(Find<OptionButton>("FarmPlotNextCrop").Selected==1&&Game.FarmConditions(1).NextCrop=="",
                "点选作物比较未填入表单，或未经确认就改变了种植计划");

            Press("FarmPlotLocate");
            check(_management?.Visible==false&&_farmScenePanel?.Visible==true&&_farmSceneMarker?.Visible==true&&
                _center.IsEqualApprox(_farmGrowthBeds[0].Root.Position),"无法从计划页聚焦真实农田");
            _farmSceneNext!.EmitSignal(BaseButton.SignalName.Pressed);
            check(_farmScenePlot==2&&_farmSceneTitle!.Text.StartsWith("2 号地")&&_center.IsEqualApprox(_farmGrowthBeds[1].Root.Position),
                "场景农田导航没有切换地块及镜头");
            ((Button)_farmScenePanel!.FindChild("FarmSceneOpenPlan",true,false)!).EmitSignal(BaseButton.SignalName.Pressed);
            check(_tab=="环境"&&_managementEntryTarget=="farm-plots"&&_activeFarmPlot==2,"返回计划时没有保留现场选中的地块");
            Game.Advance(maturity-Game.Now);
            Press("FarmPlotSelect1");
            check(Find<ProgressBar>("FarmPlotGrowth").Value==100&&Find<Label>("FarmPlotCountdown").Text.Contains("采收窗口"),"成熟后未切换为采收状态");
            Game.Execute("farm_plot",new JsonObject{{"operation","harvest"},{"plot",1}});
            check(Game.InventoryAvailability("谷物",NativeGame.OrderReservationOwner(id)).ReservedForOwner==1,
                "预览的订单预留与真实收获不一致");

            var actor=FarmPets.First(pet=>Data.FindPet(pet.Ident)!=null);
            var pet=Data.FindPet(actor.Ident)!;
            Game.Meta["farm_manager"]=true;
            Game.Meta["manager_next_wage_ts"]=Game.Now+NativeGame.Hour;
            Game.Meta["native_manager_policy"]=new JsonObject{{"feed_limit",2}};
            Game.Meta["native_manager_daily_feed"]=new JsonObject{{"day",(int)Math.Floor(Game.Now/NativeGame.Day)},{"count",1}};
            Game.Meta["manager_job"]=new JsonObject{{"job_id","farm-experience-job"},{"kind","feed"},{"pet_id",actor.Ident},
                {"building_id",NativeGame.I(pet,"building_id")},{"arrived",false}};
            ShowManagement("环境");
            check(Find<Label>("FarmManagerLiveTask").Text.Contains(NativeGame.S(pet,"name"))&&
                Find<ProgressBar>("FarmManagerQuota").Value==50&&Find<Label>("FarmManagerLogistics").Text.Contains("下次工资"),
                "管理员面板遗漏了实际任务、配额或工资时间");
            Find<SpinBox>("FarmManagerPolicyLimit").Value=1;
            check(Game.ManagerFeedLimit==2&&Find<Label>("FarmManagerPolicyPreview").Text.Contains("保存后会结束当前喂食任务"),
                "修改未保存的配额时，预览没有说明当前任务的变化");
            Press("FarmManagerTaskLocate");
            check(_management?.Visible==false&&_center.IsEqualApprox(actor.Position),"管理员任务定位没有使用当前目标位置");
            GD.Print("FARM_EXPERIENCE_VERIFIED plot_overview countdown harvest_split crop_compare scene_cycle manager_task_policy");
        }finally{
            _management?.Hide();
            Data.Root["snapshot"]=original;Data.Reindex();
            (_activeFarmPlot,_plannedFarmCrop,_farmScenePlot)=farm;
            (_center,_distance,_pitch,_buildCamera,_portraitAnchor,_cameraTransition,_previousCameraSite,_selection,_selectedPet)=camera;
            _cameraPast.Clear();_cameraPast.AddRange(past);
            _cameraForward.Clear();_cameraForward.AddRange(forward);
            UpdateFarmGrowthView();
        }

        T Find<T>(string name) where T:Node=>(T)_managementBody!.FindChild(name,true,false)!;
        void Press(string name)=>Find<Button>(name).EmitSignal(BaseButton.SignalName.Pressed);
    }
}
