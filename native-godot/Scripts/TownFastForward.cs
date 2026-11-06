using Godot;
using System;
using System.Globalization;
using System.Linq;
using System.Text.Json.Nodes;

public partial class Main
{
    enum FastForwardGoal { Morning, Construction, Commission, Festival, Harvest, Workshop }
    enum FastForwardEnd { Arrived, Stopped, Changed, Care, Waiting }
    readonly record struct FastForwardTarget(FastForwardGoal Goal,double At,string Label,string Key="",string Crop="");
    readonly record struct FastForwardResult(FastForwardTarget Target,FastForwardEnd End,double Elapsed,string Text,string PetId);
    const int FastForwardMultiplier=600;
    FastForwardTarget? _fastForward;
    FastForwardResult? _lastFastForward;
    double _fastForwardBegan;
    Button? _townPauseButton;

    double SimulationInterval=>_fastForward.HasValue?.5:1;

    FastForwardTarget? FindFastForwardTarget(FastForwardGoal goal)
    {
        double now=Game.Now;
        if(goal==FastForwardGoal.Morning)
            return new(goal,(Math.Floor(now/NativeGame.Day)+1)*NativeGame.Day+7*NativeGame.Hour,"次日 07:00");
        if(goal==FastForwardGoal.Commission){
            var commission=Game.TownCommission();
            return commission.Stage==1&&commission.Opens>now&&commission.Opens<commission.Deadline
                ?new(goal,commission.Opens,"委托交付时段",Game.TownCommissionSeason.ToString(CultureInfo.InvariantCulture)):null;
        }
        if(goal==FastForwardGoal.Festival){
            var upcoming=Game.FestivalOccurrences(now).FirstOrDefault(festival=>festival.Opens>now&&Game.Effective(festival.Zone));
            return upcoming==null?null:new(goal,upcoming.Opens,upcoming.Title,upcoming.Key);
        }
        if(goal==FastForwardGoal.Harvest){
            if(Game.FarmRoadReason.Length>0)return null;
            var next=Enumerable.Range(1,NativeGame.FarmPlotCount).Select(plot=>(Plot:plot,Plan:Game.FarmPlan(plot)))
                .Where(row=>row.Plan is {Ready:false}&&row.Plan.Mature>now).OrderBy(row=>row.Plan!.Mature).FirstOrDefault();
            return next.Plan is { } plan?new(goal,plan.Mature,$"{next.Plot} 号地 · {plan.Crop}成熟",
                next.Plot.ToString(CultureInfo.InvariantCulture),plan.Crop):null;
        }
        if(goal==FastForwardGoal.Workshop){
            var batch=Game.WorkshopOrders().FirstOrDefault(order=>order.Running&&order.Finishes>now);
            return batch.Id==null?null:new(goal,batch.Finishes,$"{batch.Item}加工结束",batch.Id);
        }
        var project=Game.Rows("town_buildings").OfType<JsonNode>()
            .Where(building=>IsTownProjectUnderway(building)&&NativeGame.D(building,"finish_ts")>now)
            .OrderBy(building=>NativeGame.D(building,"finish_ts")).FirstOrDefault();
        return project==null?null:new(goal,NativeGame.D(project,"finish_ts"),$"「{TownProjectName(project)}」完工",NativeGame.S(project,"building_id"));
    }

    JsonNode? UrgentCareCompanion()=>Game.Pets.OfType<JsonNode>().FirstOrDefault(pet=>Game.PetCarePriority(pet)==0);

    void BeginFastForward(FastForwardGoal goal)
    {
        if(FindFastForwardTarget(goal) is not FastForwardTarget target){
            _selection="当前没有可等待的目标。";
            return;
        }
        BeginFastForward(target);
    }

    void BeginFastForward(FastForwardTarget target)
    {
        if(_fastForward.HasValue){
            SetManagementFeedback("请先停止当前快进。",true);
            return;
        }
        if(target.At<=Game.Now||FastForwardBlock(target).Length>0){
            SetManagementFeedback("目标状态已变化，请重新查看时间计划。",true);
            _selection="目标状态已变化，请重新查看时间计划。";
            return;
        }
        if(UrgentCareCompanion() is { } pet){
            _selection=$"「{NativeGame.S(pet,"name","伙伴")}」需要紧急照护，处理后可以快进。";
            return;
        }
        _fastForward=target;
        _fastForwardBegan=Game.Now;
        _simulationAccumulator=0;
        SetTownPaused(false);
        _management?.Hide();
        _selection=$"正在快进到{target.Label} · 可在设置菜单停止";
        RefreshSpeedControl();
    }

    void StopFastForward(string feedback,FastForwardEnd end=FastForwardEnd.Stopped,string petId="")
    {
        if(_fastForward is { } target)_lastFastForward=new(target,end,Math.Max(0,Game.Now-_fastForwardBegan),
            feedback.Length>0?feedback:"模拟已暂停 · 快进停止",petId);
        _fastForward=null;
        Data.Meta["native_speed"]=1;
        _selection=feedback;
        RefreshSpeedControl();
    }

    void AdvanceTownClock(double realSeconds)
    {
        if(Paused)return;
        if(_fastForward is not FastForwardTarget target){
            Game.Advance(realSeconds*WorldData.D(Data.Meta,"native_speed",1));
            return;
        }
        if(InterruptForCare()||InterruptForChangedTarget(target))return;
        Game.Advance(Math.Min(realSeconds*FastForwardMultiplier,Math.Max(0,target.At-Game.Now)));
        if(InterruptForCare())return;
        if(Game.Now>=target.At-1e-6)CompleteFastForward(target);
        else InterruptForChangedTarget(target);
    }

    bool InterruptForCare()
    {
        if(UrgentCareCompanion() is not { } pet)return false;
        StopFastForward($"已恢复正常速度 · 「{NativeGame.S(pet,"name","伙伴")}」需要紧急照护",FastForwardEnd.Care,NativeGame.S(pet,"pet_id"));
        return true;
    }

    bool InterruptForChangedTarget(FastForwardTarget target)
    {
        string reason=FastForwardBlock(target);
        if(reason.Length==0)return false;
        StopFastForward($"快进已停止 · {reason}",FastForwardEnd.Changed);
        return true;
    }

    string FastForwardBlock(FastForwardTarget target)
    {
        switch(target.Goal){
            case FastForwardGoal.Construction:
                var project=Game.FindTownBuilding(target.Key);
                return project!=null&&IsTownProjectUnderway(project)&&NativeGame.D(project,"finish_ts")==target.At
                    ?"":"工程已移除或完工时间已变化";
            case FastForwardGoal.Commission:
                var commission=Game.TownCommission();
                return Game.TownCommissionSeason.ToString(CultureInfo.InvariantCulture)==target.Key&&commission.Stage==1&&commission.Opens==target.At
                    ?"":"季节委托的交付时段已变化";
            case FastForwardGoal.Festival:
                return Game.FestivalOccurrences(Game.Now).Any(activity=>activity.Key==target.Key&&activity.Opens==target.At&&Game.Effective(activity.Zone))
                    ?"":"活动场次已变化或街区无法到达";
            case FastForwardGoal.Harvest:
                if(Game.FarmRoadReason is {Length:>0} road)return road;
                return FastForwardFarmPlan(target) is {Ready:false} plot&&plot.Mature==target.At&&plot.Crop==target.Crop
                    ?"":"地块作物或成熟时间已变化";
            case FastForwardGoal.Workshop:
                return Game.WorkshopOrders().Any(order=>order.Id==target.Key&&order.Running&&order.Finishes==target.At)
                    ?"":"加工批次或预计完成时间已变化";
            default:return "";
        }
    }

    NativeGame.FarmPlot? FastForwardFarmPlan(FastForwardTarget target)=>int.TryParse(target.Key,out int plot)?Game.FarmPlan(plot):null;

    void CompleteFastForward(FastForwardTarget target)
    {
        string waiting=target.Goal switch{
            FastForwardGoal.Construction when Game.FindTownBuilding(target.Key) is not JsonNode project||
                NativeGame.S(project,"status")!="ready"=>"已到预计完工时间 · 工程仍未完工",
            FastForwardGoal.Commission when Game.TownCommission().Stage!=2=>"已到交付时段 · 委托仍在等待实际货运或通路",
            FastForwardGoal.Festival when FastForwardBlock(target).Length>0=>"已到开场时间 · 活动街区暂不可到达",
            FastForwardGoal.Harvest when FastForwardFarmPlan(target) is not {Ready:true} plot||plot.Crop!=target.Crop=>
                "已到预计成熟时间 · 农田仍未成熟",
            FastForwardGoal.Workshop when Game.WorkshopOrders().Any(order=>order.Id==target.Key)=>
                "已到加工结束时间 · 仓库空间不足，成品暂存在加工坊",
            _=>""
        };
        StopFastForward(waiting.Length==0?$"已到{target.Label} · 已恢复正常速度":waiting,
            waiting.Length==0?FastForwardEnd.Arrived:FastForwardEnd.Waiting);
    }

    void SetTownPaused(bool paused)
    {
        Paused=paused;
        if(_townPauseButton!=null)_townPauseButton.Text=paused?"继续":"暂停";
        if(_townTravelBanner!=null)RefreshTownStatus();
    }

    void RefreshSpeedControl()
    {
        if(_speedControl!=null)_speedControl.Text=_fastForward.HasValue?"目标快进":$"速度 ×{WorldData.I(Data.Meta,"native_speed",1)}";
        if(_townTravelBanner!=null)RefreshTownStatus();
    }
}
