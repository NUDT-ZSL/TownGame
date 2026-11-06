using Godot;
using System;
using System.Linq;
using System.Text.Json.Nodes;

public partial class Main
{
    void VerifyGrowthExperience(Action<bool,string> check)
    {
        var original=Data.Snapshot;
        var growth=(_growthFilter,_trackedGrowthGoal,_growthTrackingLoaded,_commissionReservationsExpanded,_commissionReceiptsExpanded);
        var market=(_marketSection,_marketFilter,_marketSearch,_marketBuyQuantity,_orderSupplyRoute,_careSupplyRoute);
        try{
            Data.Root["snapshot"]=original.DeepClone();Data.Reindex();
            _growthFilter=0;_trackedGrowthGoal="";_growthTrackingLoaded=true;
            _commissionReservationsExpanded=false;_commissionReceiptsExpanded=false;
            Game.Meta["stats"]!["orders"]=1;
            Game.State["achievements"]!.AsObject().Remove("orders");
            ShowManagement("成长");
            var filter=Find<OptionButton>("GrowthGoalFilter");
            check(filter.GetItemText(0).Contains("10")&&Find<Control>("GrowthGoal_orders").Visible,"成长筛选没有显示所有目标");
            Press("GrowthTrack_orders");
            var tracked=Find<Label>("GrowthTrackedHeading");
            check(tracked.Text.Contains("1/3")&&Find<Button>("GrowthTrackedAction").Text=="查看邻里订单","追踪卡没有同步原目标进度和下一步");
            Game.Meta["stats"]!["orders"]=2;RefreshManagementPage(1);
            check(tracked.Text.Contains("2/3"),"追踪进度没有实时更新");
            filter.Select(2);filter.EmitSignal(OptionButton.SignalName.ItemSelected,2L);
            check(!Find<Control>("GrowthGoal_orders").Visible&&Find<Button>("GrowthTrackedAction").Visible,"已达成筛选隐藏了追踪入口或混入进行中目标");
            Press("GrowthTrackedAction");
            check(_tab=="订单","追踪卡未打开原目标操作");
            GoBackInManagement();
            check(_tab=="成长"&&Find<OptionButton>("GrowthGoalFilter").Selected==2,"返回成长页丢失筛选");
            using(var config=new ConfigFile()){
                config.SetValue("growth","tracked","districts");
                check(ReadGrowthTracking(config)=="districts","街区目标未能读取追踪配置");
                config.SetValue("growth","tracked","无效目标");
                check(ReadGrowthTracking(config)=="","无效追踪配置未被忽略");
            }
            Press("GrowthStopTracking");
            check(_trackedGrowthGoal==""&&!Find<Button>("GrowthTrackedAction").Visible,"取消追踪后仍显示原目标操作");

            Game.Meta.Remove("native_town_commission");
            Game.Meta.Remove("native_town_commission_offer");
            Game.Meta.Remove("native_town_commission_history");
            Game.Meta["sim_ts"]=NativeGame.D(Game.Meta,"start_ts")+NativeGame.Hour;
            Game.State["inventory"]=new JsonArray();
            ShowManagement("成长","town-commission");
            var progress=Game.TownCommission();
            check(Find<Label>("TownCommissionDeliveryState").Text.Contains("明天 07:00"),"首次备货未显示次日早上开放");
            int season=Game.TownCommissionSeason;
            string owner=NativeGame.CommissionReservationOwner(season);
            int material=Array.FindIndex(progress.Plan.Supplies,entry=>Game.BuyPrice(entry.Item)>0);
            check(material>=0&&progress.CanPrepare,"无法构建季节委托测试用例");
            var supply=progress.Plan.Supplies[material];
            Game.Add(supply.Item,supply.Count-2);
            Game.Execute("reserve_stock",new JsonObject{{"owner",owner},{"item",supply.Item},{"quantity",1}});
            RefreshManagementPage(1);
            check(Find<Label>("TownCommissionPrepareState").Text.Contains("库存不足")&&Find<Label>("TownCommissionFreightState").Text.Contains("确认备货后"),
                "委托清单未区分备货不足与未启动的货运");
            check(Find<Label>($"TownCommissionMaterialStock{material}").Text.Contains("本单预留 1")&&
                Find<Label>($"TownCommissionMaterialStock{material}").Text.Contains("还差 2")&&
                Find<ProgressBar>($"TownCommissionMaterialProgress{material}").Value==supply.Count-2,"委托物资条未按本单预留计算缺口");
            Press($"TownCommissionMaterialBuy{material}");
            check(_tab=="商店"&&_managementEntryTarget==$"supply:{supply.Item}"&&_marketBuyQuantity==2,"委托补货未打开正确货架和缺口数量");
            GoBackInManagement();
            check(_tab=="成长"&&_managementEntryTarget=="town-commission","补货返回未定位原委托");

            foreach(var (item,count) in progress.Plan.Supplies){int gap=count-Game.Stock(item,owner);if(gap>0)Game.Add(item,gap);}
            Game.Execute("town_commission",new JsonObject{{"step","prepare"},{"season",season}});
            RefreshManagementPage(1);
            check(Find<Label>("TownCommissionPrepareState").Text.Contains("已确认")&&
                Find<Label>("TownCommissionFreightState").Text.Contains("等待居民货车"),"备货确认后没有独立等待真实货运");
            // The test rewinds to the first season, so existing residents may not have been born yet.
            const string driverId="growth-verification-freight-driver";
            Game.People.Add(new JsonObject{{"person_id",driverId},{"role","居民"},{"name","委托测试司机"},{"born_ts",Game.Now-20*NativeGame.Day}});
            Game.FreightState["driver_id"]=driverId;
            Game.FreightState["phase"]="unloading";
            Game.FreightState["cargo_item"]="food";
            Game.FreightState["cargo_count"]=1;
            check(Game.DeliverFreight(),"测试用居民货运未能卸货");
            RefreshManagementPage(1);
            check(Find<Label>("TownCommissionFreightState").Text.Contains("已记录")&&Find<Label>("TownCommissionDeliveryState").Text.Contains("剩余"),
                "货运已送达时没有区分尚未开放的交付时段");
            Game.Meta["sim_ts"]=progress.Opens;
            progress=Game.TownCommission();
            check(progress.Stage==2&&progress.SuppliesReady,"开放交付时测试物资、道路或货运条件不满足");
            Game.Execute("town_commission",new JsonObject{{"step","deliver"},{"season",season}});
            RefreshManagementPage(1);
            Find<Button>("TownCommissionReceiptToggle").ButtonPressed=true;
            var receipt=Game.CommissionReceipts().Single();
            check(receipt.Season==season&&receipt.Coins==progress.Plan.Coins&&receipt.Supplies.SequenceEqual(progress.Plan.Supplies)&&
                Find<Control>("TownCommissionReceipts").Visible&&Find<Control>("TownCommissionReceipts").GetChildCount()>0,
                "完成委托未留下真实物资与奖励的明细记录");
            Game.Meta["sim_ts"]=progress.Deadline;
            var reloaded=new NativeGame(Data.Root.DeepClone().AsObject());
            check(reloaded.TownCommissionSeason!=season&&reloaded.CommissionReceipts().Single().Delivered==receipt.Delivered,
                "交付记录没有在换季和重新加载后保留");
            GD.Print("GROWTH_EXPERIENCE_VERIFIED filters tracked_goal commission_checklist supply_route delivery_receipt");
        }finally{
            _management?.Hide();
            Data.Root["snapshot"]=original;Data.Reindex();
            (_growthFilter,_trackedGrowthGoal,_growthTrackingLoaded,_commissionReservationsExpanded,_commissionReceiptsExpanded)=growth;
            (_marketSection,_marketFilter,_marketSearch,_marketBuyQuantity,_orderSupplyRoute,_careSupplyRoute)=market;
        }

        T Find<T>(string id) where T:Node=>(T)_managementBody!.FindChild(id,true,false)!;
        void Press(string id)=>Find<Button>(id).EmitSignal(BaseButton.SignalName.Pressed);
    }
}
