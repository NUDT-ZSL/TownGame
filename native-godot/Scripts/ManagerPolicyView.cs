using Godot;
using System;
using System.Globalization;
using System.Linq;
using System.Text.Json.Nodes;

public sealed partial class NativeGame
{
    const string ManagerPolicyKey="native_manager_policy";
    const string ManagerDailyKey="native_manager_daily_feed";
    internal int ManagerFeedLimit=>Meta[ManagerPolicyKey] is JsonObject policy?I(policy,"feed_limit"):0;
    int ManagerCurrentDay=>(int)Math.Floor(Now/Day);
    internal int ManagerFedToday=>Meta[ManagerDailyKey] is JsonObject usage&&I(usage,"day")==ManagerCurrentDay?I(usage,"count"):0;
    bool ManagerFeedCapReached=>ManagerFeedLimit>0&&ManagerFedToday>=ManagerFeedLimit;

    internal bool ManagerPlotPriority(int plot,string duty)=>Meta[ManagerPolicyKey]?[duty] is JsonObject priorities&&
        B(priorities,plot.ToString(CultureInfo.InvariantCulture));

    void RecordManagerDailyFeed()=>Meta[ManagerDailyKey]=new JsonObject{{"day",ManagerCurrentDay},{"count",ManagerFedToday+1}};

    string ManagerPolicyCommand(JsonObject args)
    {
        int plot=I(args,"plot",-1),limit=I(args,"feed_limit",-1);
        if(limit<0||limit>200)throw new InvalidOperationException("每日上限必须在 0 到 200 份之间");
        var settings=(Meta[ManagerPolicyKey]??=new JsonObject()).AsObject();
        settings["feed_limit"]=limit;
        if(plot>=0){
            if(!Rows("buildings").OfType<JsonObject>().Any(building=>I(building,"building_id")==plot&&S(building,"kind").Length>0))
                throw new InvalidOperationException("指定庄园地块没有建筑");
            foreach(string duty in new[]{"feed","clean"}){
                var priorities=(settings[duty]??=new JsonObject()).AsObject();
                string key=plot.ToString(CultureInfo.InvariantCulture);
                if(B(args,duty))priorities[key]=true;
                else priorities.Remove(key);
            }
        }
        if(B(Meta,"farm_manager")){
            if(ManagerFeedCapReached&&CurrentManagerJob is JsonObject current&&S(current,"kind")=="feed")
                ClearManagerJob("已达到今日喂食上限");
            else if(CurrentManagerJob==null)Meta["manager_next_scan_ts"]=Now;
        }
        return "农场管理员的照护安排已更新";
    }
}

public partial class Main
{
    void FarmManagerPolicyControls()
    {
        var card=MarketCard(_managementBody!,color:"f0eee0");
        MarketLabel(card,"管理员安排",18,TownTheme.Leaf);
        MarketLabel(card,"优先喂食与清扫可分别设置。紧急饥饿仍优先，达到用量上限后管理员会继续清扫。",14,TownTheme.Muted,wrap:true);
        var live=MarketCard(card,color:"f7f3e7");
        var task=MarketLabel(live,"",15,TownTheme.Leaf,wrap:true);
        task.Name="FarmManagerLiveTask";
        var logistics=MarketLabel(live,"",14,TownTheme.Muted,wrap:true);
        logistics.Name="FarmManagerLogistics";
        var quota=FarmProgressBar(live,"9aa872");
        quota.Name="FarmManagerQuota";
        var locate=MarketButton(live,"定位当前任务",LocateFarmManagerTask,expand:false,accent:"地图");
        locate.Name="FarmManagerTaskLocate";
        locate.SizeFlagsHorizontal=Control.SizeFlags.ShrinkBegin;
        var plots=Game.Rows("buildings").OfType<JsonObject>().Where(building=>NativeGame.S(building,"kind").Length>0).ToArray();
        var row=new HFlowContainer();
        row.AddThemeConstantOverride("h_separation",6);
        card.AddChild(row);
        var choice=MarketOptions(row,plots.Select(plot=>$"地块 {NativeGame.I(plot,"building_id")} · {NativeGame.S(plot,"kind")}").ToArray(),0);
        int Plot()=>choice.Selected>=0&&choice.Selected<plots.Length?NativeGame.I(plots[choice.Selected],"building_id"):-1;
        var feed=new CheckBox{Text="优先喂食",Disabled=plots.Length==0};
        var clean=new CheckBox{Text="优先清扫",Disabled=plots.Length==0};
        row.AddChild(feed);row.AddChild(clean);
        var limit=new SpinBox{Name="FarmManagerPolicyLimit",Prefix="每日饲料上限 ",Suffix=" 份",MinValue=0,MaxValue=200,Value=Game.ManagerFeedLimit};
        row.AddChild(limit);
        var save=MarketButton(row,"保存安排",()=>Act("manager_policy",new JsonObject{{"plot",Plot()},{"feed_limit",(int)limit.Value},{"feed",feed.ButtonPressed},{"clean",clean.ButtonPressed}}),expand:false,accent:"环境");
        var status=MarketLabel(card,"",14,TownTheme.Muted,wrap:true);
        status.Name="FarmManagerPolicyPreview";
        void RefreshStatus()
        {
            bool hired=NativeGame.B(Data.Meta,"farm_manager");
            var job=hired?Game.CurrentManagerJob:null;
            string liveStatus=NativeGame.S(Data.Meta,"manager_status");
            if(job!=null){
                bool feeding=NativeGame.S(job,"kind")=="feed";
                string stage=NativeGame.B(job,"arrived")?"已到达":"前往中";
                int targetPlot=NativeGame.I(job,"building_id",-1);
                task.Text=$"{stage} · {ManagerTaskCaption(job)}"+(targetPlot>=0?$" · 庄园地块 {targetPlot}":"");
                locate.Disabled=feeding?FarmPet(NativeGame.S(job,"pet_id"))==null:
                    !Game.Rows("waste").OfType<JsonNode>().Any(row=>NativeGame.S(row,"waste_id")==NativeGame.S(job,"waste_id"));
            }else{
                task.Text=hired?(liveStatus.Length>0?liveStatus:"待分配任务"):"农场管理员未雇佣";
                locate.Disabled=true;
            }
            locate.Visible=job!=null;
            int saved=Game.ManagerFeedLimit,used=Game.ManagerFedToday,proposed=(int)limit.Value;
            string allowance=saved==0?"不限量":$"剩余 {Math.Max(0,saved-used)} 份";
            logistics.Text=$"今日用量 {used} 份 · {allowance} · 可用普通饲料 {Game.Stock("普通饲料")} 份";
            double wageTime=NativeGame.D(Data.Meta,"manager_next_wage_ts");
            if(hired&&wageTime>0)logistics.Text+=$"\n下次工资 {TownProjectClock(wageTime)} · {NativeGame.I(Game.Rules,"MANAGER_WAGE")} 金币";
            quota.Visible=saved>0;
            quota.Value=saved>0?Math.Clamp(used*100d/saved,0,100):0;
            bool changed=proposed!=saved||feed.ButtonPressed!=Game.ManagerPlotPriority(Plot(),"feed")||clean.ButtonPressed!=Game.ManagerPlotPriority(Plot(),"clean");
            save.Disabled=!changed;
            status.Text=changed?(proposed==0?"待保存 · 不限制今日喂食用量":
                $"待保存 · 今日还可喂食 {Math.Max(0,proposed-used)} 份"):
                "当前安排已保存。上限填 0 表示不限量。";
            if(changed&&job!=null&&proposed>0&&used>=proposed&&NativeGame.S(job,"kind")=="feed")
                status.Text+=" · 保存后会结束当前喂食任务";
        }
        void SelectPlot()
        {
            feed.SetPressedNoSignal(Game.ManagerPlotPriority(Plot(),"feed"));
            clean.SetPressedNoSignal(Game.ManagerPlotPriority(Plot(),"clean"));
            RefreshStatus();
        }
        choice.ItemSelected+=_=>SelectPlot();
        limit.ValueChanged+=_=>RefreshStatus();
        feed.Toggled+=_=>RefreshStatus();
        clean.Toggled+=_=>RefreshStatus();
        SelectPlot();
        _refreshManagementPage+=RefreshStatus;
    }
}
