using Godot;
using System;

public partial class Main
{
    static readonly (FastForwardGoal Goal,string Caption)[] TownTimeGoals={
        (FastForwardGoal.Morning,"次日早晨"),(FastForwardGoal.Construction,"下一项工程完工"),
        (FastForwardGoal.Harvest,"下一块农田成熟"),(FastForwardGoal.Workshop,"当前加工批次结束"),
        (FastForwardGoal.Commission,"季节委托交付时段"),(FastForwardGoal.Festival,"下一场可达的周末活动")
    };
    int _timePlannerGoal;

    void OpenTownTimePlanner()=>ShowManagement("环境","time-plan");

    Control TownTimePlannerControls()
    {
        var caption=Section("时间计划","先查看目标和预计用时，再开始快进")!;
        var heading=caption.GetParent<Control>().GetParent<Control>();
        heading.Name="TownTimePlannerHeading";
        if(_managementEntryTarget=="time-plan")_managementFocusTarget=heading;
        var card=MarketCard(_managementBody!,color:"edf1e4");
        var selectRow=new HFlowContainer();selectRow.AddThemeConstantOverride("h_separation",7);card.AddChild(selectRow);
        var picker=MarketOptions(selectRow,Array.ConvertAll(TownTimeGoals,entry=>entry.Caption),_timePlannerGoal);
        picker.Name="TownTimePlanTarget";
        picker.AccessibilityName="选择快进目标";
        var targetName=MarketLabel(card,"",18,TownTheme.Leaf,wrap:true);targetName.Name="TownTimePlanName";
        var timing=MarketLabel(card,"",15,TownTheme.Ink,wrap:true);timing.Name="TownTimePlanTiming";
        var notice=MarketLabel(card,"",14,TownTheme.Muted,wrap:true);notice.Name="TownTimePlanNotice";
        MarketLabel(card,"快进期间仍会结算经营和照护。遇到紧急照护会提前停止。",14,TownTheme.Muted,wrap:true);
        var buttons=new HFlowContainer();buttons.AddThemeConstantOverride("h_separation",8);card.AddChild(buttons);
        FastForwardTarget? displayed=null;
        var start=MarketButton(buttons,$"开始快进 · ×{FastForwardMultiplier}",()=>{
            if(displayed is { } target)BeginFastForward(target);
        },expand:false,accent:"环境");start.Name="TownTimePlanStart";
        var inspect=MarketButton(buttons,"查看目标",()=>{
            if(displayed is { } target)OpenFastForwardDestination(target);
        },expand:false,accent:"地图");inspect.Name="TownTimePlanInspect";
        var care=MarketButton(buttons,"查看紧急照护",()=>{
            if(UrgentCareCompanion() is { } pet)OpenPetManagement(NativeGame.S(pet,"pet_id"),"care:health");
        },expand:false,accent:"照护");care.Name="TownTimePlanCare";
        var stop=MarketButton(buttons,"停止当前快进",()=>StopFastForward("已停止快进 · 已恢复正常速度"),
            expand:false,accent:"事件");stop.Name="TownTimePlanStop";

        var result=MarketCard(_managementBody!,color:"e8eef0");
        result.GetParent<Control>().Name="TownTimePlanResult";
        MarketLabel(result,"上次快进",18,TownTheme.Accent("地图"));
        var report=MarketLabel(result,"",14,TownTheme.Ink,wrap:true);report.Name="TownTimePlanResultText";
        var next=MarketButton(result,"查看目标",()=>{
            if(_lastFastForward is not { } last)return;
            if(last.End==FastForwardEnd.Care&&Data.FindPet(last.PetId) is { } pet&&Game.Active(pet))
                OpenPetManagement(last.PetId,"care:health");
            else OpenFastForwardDestination(last.Target);
        },expand:false,accent:"地图");next.Name="TownTimePlanResultLink";

        picker.ItemSelected+=value=>{_timePlannerGoal=(int)value;Refresh();};
        stop.Pressed+=Refresh;
        Refresh();
        _refreshManagementPage+=Refresh;
        return heading;

        void Refresh()
        {
            var goal=TownTimeGoals[_timePlannerGoal].Goal;
            displayed=FindFastForwardTarget(goal);
            var urgent=UrgentCareCompanion();
            targetName.Text=displayed?.Label??TownTimeGoals[_timePlannerGoal].Caption;
            timing.Text=displayed is { } target?$"{TimePlannerClock(target.At)} · {FastForwardEta(target.At)}":UnavailableTimeGoal(goal);
            notice.Text=_fastForward is { } active?$"当前{(Paused?"已暂停":"正在快进至")} · {active.Label}":
                urgent!=null?$"「{NativeGame.S(urgent,"name","伙伴")}」需要紧急照护，处理后可快进。":
                Paused?"开始时会继续小镇时钟。":"抵达、手动停止或条件变化后恢复 ×1。";
            start.Disabled=displayed==null||_fastForward.HasValue||urgent!=null;
            inspect.Visible=displayed!=null;
            care.Visible=urgent!=null&&!_fastForward.HasValue;
            stop.Visible=_fastForward.HasValue;
            result.GetParent<Control>().Visible=_lastFastForward.HasValue;
            if(_lastFastForward is not { } last)return;
            report.Text=$"{last.Target.Label} · 已推进 {FastForwardElapsed(last.Elapsed)}\n{last.Text}";
            next.Text=last.End==FastForwardEnd.Care?"查看紧急照护":"查看相关页面";
            next.Visible=last.End!=FastForwardEnd.Care||Data.FindPet(last.PetId) is { } patient&&Game.Active(patient);
        }
    }

    string TimePlannerClock(double at)
    {
        int hour=(int)(at%NativeGame.Day/NativeGame.Hour),minute=(int)(at%NativeGame.Hour/60);
        double days=Math.Floor(at/NativeGame.Day)-Math.Floor(Game.Now/NativeGame.Day);
        string date=days switch{0=>"今天",1=>"明天",_=>CalendarDayTitle(at)};
        return $"{date} {hour:00}:{minute:00}";
    }

    string FastForwardEta(double at)
    {
        double remaining=Math.Max(0,at-Game.Now);
        return $"约 {Math.Max(1,Math.Ceiling(remaining/FastForwardMultiplier)):0} 现实秒";
    }

    static string FastForwardElapsed(double seconds)=>seconds<60?$"{seconds:0} 游戏秒":
        seconds<NativeGame.Hour?$"{(int)(seconds/60)} 游戏分钟":$"{(int)(seconds/NativeGame.Hour)} 游戏小时 {(int)(seconds%NativeGame.Hour/60)} 分钟";

    string UnavailableTimeGoal(FastForwardGoal goal)=>goal switch{
        FastForwardGoal.Construction=>"当前没有正在施工的城镇工程。",
        FastForwardGoal.Commission=>"请先确认本季备货。已开放的交付时段无需等待。",
        FastForwardGoal.Festival=>"暂时没有已开放且接通道路的后续场次。",
        FastForwardGoal.Harvest=>Game.FarmRoadReason is {Length:>0} road?road:"当前没有待成熟的分地作物。",
        FastForwardGoal.Workshop=>"当前没有正在加工的批次。等待开工的批次可在加工队列查看。",
        _=>"当前没有可等待的目标。"
    };

    void OpenFastForwardDestination(FastForwardTarget target)
    {
        switch(target.Goal){
            case FastForwardGoal.Construction:
                _buildWorkspace=2;
                ShowManagement("建造",Game.FindTownBuilding(target.Key)!=null?target.Key:"projects");
                break;
            case FastForwardGoal.Commission:ShowManagement("成长","town-commission");break;
            case FastForwardGoal.Festival:ShowManagement("事件","weekend-festivals");break;
            case FastForwardGoal.Harvest:
                if(int.TryParse(target.Key,out int plot))_activeFarmPlot=plot;
                ShowManagement("环境","farm-plots");
                break;
            case FastForwardGoal.Workshop:
                ShowManagement("商店","workshop-queue",()=>_marketSection=2);
                break;
            default:ShowManagement("今日待办");break;
        }
    }
}
