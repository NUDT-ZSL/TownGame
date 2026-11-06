using Godot;
using System;
using System.Linq;
using System.Text.Json.Nodes;

public partial class Main
{
    void VerifyTimePlanner(Action<bool,string> check)
    {
        var original=Data.Snapshot;
        var planner=(_fastForward,_lastFastForward,_fastForwardBegan,_simulationAccumulator,_timePlannerGoal,
            _activeFarmPlot,_marketSection,_selection,_selectedPet);
        bool paused=Paused;
        try{
            Data.Root["snapshot"]=original.DeepClone();Data.Reindex();
            _fastForward=null;_lastFastForward=null;
            Game.Commons["enabled"]=false;
            Game.Meta["farm_manager"]=false;
            Game.Meta["caretaker"]=false;
            var pets=Game.Pets.OfType<JsonNode>().Where(Game.Active).ToArray();
            check(pets.Length>0,"时间计划缺少验收伙伴");
            foreach(var pet in pets){
                pet["status"]="正常";pet["illness_type"]="";pet["illness_budget"]=10000;
                pet["injury"]=0;pet["health"]=100;pet["hunger"]=100;pet["cleanliness"]=100;
            }

            double before=Game.Now;
            Game.SetZone("fields",1);
            check(Game.FarmRoadReason=="","时间计划验收农田未接路");
            var plot=new JsonObject{{"crop","谷物"},{"mature",before+100},{"expires",before+NativeGame.Hour},{"yield",0}};
            Game.Meta["farm_plots"]=new JsonObject{{"1",plot}};
            var speed=(MenuButton)_speedControl!;
            speed.EmitSignal(MenuButton.SignalName.AboutToPopup);
            speed.GetPopup().EmitSignal(PopupMenu.SignalName.IdPressed,(long)TimePlannerMenu);
            check(_managementEntryTarget=="time-plan"&&Find<OptionButton>("TownTimePlanTarget").ItemCount==6&&Game.Now==before,
                "速度菜单未打开只读目标计划");
            Choose(FastForwardGoal.Harvest);
            check(Find<Label>("TownTimePlanName").Text.Contains("谷物")&&Find<Label>("TownTimePlanTiming").Text.Contains("现实秒"),
                "计划没有显示真实作物或现实用时");
            Press("TownTimePlanStart");
            check(_fastForward is {Goal:FastForwardGoal.Harvest,Key:"1"}&&_townTravelBanner.Visible,"无法从计划锁定地块快进");
            AdvanceTownClock(.05);RefreshTownStatus();
            check(Game.Now==before+30&&Mathf.IsEqualApprox((float)_townTravelProgress.Value,30),"快进进度未根据真实时间计算");
            _townTravelPause.EmitSignal(BaseButton.SignalName.Pressed);
            AdvanceTownClock(.5);
            check(Game.Now==before+30&&Paused&&_townTravelPause.Text=="继续快进"&&_townTravelCaption.Text.Contains("已暂停"),
                "暂停快进仍在推进或未更新 HUD");
            _townTravelPause.EmitSignal(BaseButton.SignalName.Pressed);
            AdvanceTownClock(.5);
            check(Game.Now==before+100&&Game.FarmPlan(1) is {Ready:true}&&_lastFastForward is {End:FastForwardEnd.Arrived}&&
                NativeGame.I(Game.Meta,"native_speed")==1,"快进未精确停在真实成熟点");
            OpenTownTimePlanner();
            check(Find<Label>("TownTimePlanResultText").Text.Contains("谷物成熟"),"最近快进没有显示结果");
            Press("TownTimePlanResultLink");
            check(_managementEntryTarget=="farm-plots"&&_activeFarmPlot==1,"成熟结果未跳到对应地块");

            Game.Meta["native_workshop_queue"]=new JsonArray();
            Game.Meta["coins"]=200;
            Game.State["inventory"]=new JsonArray();
            var workshop=Game.Rows("buildings").OfType<JsonObject>().FirstOrDefault(b=>NativeGame.S(b,"kind")=="加工坊")??
                Game.Rows("buildings").OfType<JsonObject>().First(b=>NativeGame.S(b,"kind")=="");
            workshop["kind"]="加工坊";workshop["level"]=1;workshop["unlocked"]=true;
            Game.Add("谷物",6);Game.Add("清水",2);
            Game.Execute("workshop_queue",new JsonObject{{"operation","enqueue"},{"item","高级饲料"},{"batches",1}});
            check(FindFastForwardTarget(FastForwardGoal.Workshop)==null,"尚未开工的估算被当成实际完工目标");
            Game.Advance(300);
            var batch=Game.WorkshopOrders().Single();
            check(batch.Running,"加工验收批次未开始");
            BeginFastForward(FastForwardGoal.Workshop);
            AdvanceTownClock((batch.Finishes-Game.Now)/FastForwardMultiplier+1);
            check(Game.Now==batch.Finishes&&Game.Stock("高级饲料")==batch.Yield&&_lastFastForward is {End:FastForwardEnd.Arrived},
                "加工快进没有走实际完工入库流程");
            OpenTownTimePlanner();Press("TownTimePlanResultLink");
            check(_tab=="商店"&&_managementEntryTarget=="workshop-queue"&&_managementFocusTarget!=null,"加工结果未跳到实际队列");

            before=Game.Now;
            plot=new JsonObject{{"crop","谷物"},{"mature",before+100},{"expires",before+NativeGame.Hour},{"yield",0}};
            Game.Meta["farm_plots"]=new JsonObject{{"1",plot}};
            BeginFastForward(FastForwardGoal.Harvest);
            check(_fastForward is {Goal:FastForwardGoal.Harvest},"目标变化验收未开始快进");
            plot["mature"]=before+200;
            AdvanceTownClock(.5);
            check(Game.Now==before&&_lastFastForward is {End:FastForwardEnd.Changed}&&_selection.Contains("成熟时间已变化"),
                "目标失效后仍推进至旧时间");

            BeginFastForward(FastForwardGoal.Morning);
            pets[0]["health"]=35;
            AdvanceTownClock(.5);
            check(Game.Now==before&&_lastFastForward is {End:FastForwardEnd.Care} result&&result.PetId==NativeGame.S(pets[0],"pet_id"),
                "紧急照护没有停止快进或关联实际伙伴");
            OpenTownTimePlanner();Press("TownTimePlanResultLink");
            check(_tab=="宠物"&&_selectedPet==NativeGame.S(pets[0],"pet_id")&&_managementEntryTarget=="care:health",
                "中断结果未打开对应伙伴的照护信息");
            GD.Print("TIME_PLANNER_VERIFIED preview exact_harvest actual_workshop pause_progress changed_target care_result_links");
        }finally{
            _management?.Hide();Data.Root["snapshot"]=original;Data.Reindex();
            (_fastForward,_lastFastForward,_fastForwardBegan,_simulationAccumulator,_timePlannerGoal,
                _activeFarmPlot,_marketSection,_selection,_selectedPet)=planner;
            SetTownPaused(paused);RefreshSpeedControl();
        }

        T Find<T>(string name) where T:Node=>(T)_managementBody!.FindChild(name,true,false)!;
        void Press(string name)=>Find<Button>(name).EmitSignal(BaseButton.SignalName.Pressed);
        void Choose(FastForwardGoal goal){
            int index=Array.FindIndex(TownTimeGoals,row=>row.Goal==goal);
            var picker=Find<OptionButton>("TownTimePlanTarget");picker.Select(index);picker.EmitSignal(OptionButton.SignalName.ItemSelected,(long)index);
        }
    }
}
