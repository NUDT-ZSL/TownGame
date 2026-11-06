using Godot;
using System;
using System.Linq;
using System.Text.Json.Nodes;

public partial class Main
{
    void VerifyFinanceExperience(Action<bool,string> check)
    {
        var original=Data.Snapshot;
        var finance=(_financePeriod,_financeDirection,_financeSort,_financePage,_financeBucket,_financeSearch);
        var logs=(_logPeriod,_logSource,_logPage,_logSearch);
        try{
            Data.Root["snapshot"]=original.DeepClone();Data.Reindex();
            (_financePeriod,_financeDirection,_financeSort,_financePage,_financeBucket,_financeSearch)=(0,0,0,0,-1,"");
            (_logPeriod,_logSource,_logPage,_logSearch)=(0,0,0,"");
            double now=Game.Now;
            Game.Meta["finance_ledger"]=new JsonArray(
                Entry(now-2*NativeGame.Day,-120,"旧采购"),Entry(now-14*NativeGame.Hour,-70,"较早采购"),
                Entry(now-3*NativeGame.Hour,-9,"刚才采购"),Entry(now-NativeGame.Hour,42,"验收收入"));
            Game.Meta["coins"]=0;
            Game.Meta["farm_manager"]=true;
            Game.Meta["caretaker"]=false;
            Game.Meta["manager_next_wage_ts"]=now+NativeGame.Hour;
            ShowManagement("财务");
            check(LabelText("FinanceIncome")=="+42"&&LabelText("FinanceExpense")=="−79"&&LabelText("FinanceNet")=="-37",
                "滚动 24 小时的收入、支出或净额与实际账目不同");
            var chart=Find<FinanceChart>("FinanceOverviewChart");
            chart._GuiInput(new InputEventKey{Pressed=true,Keycode=Key.Left});
            check(LabelText("FinanceLedgerStatus").StartsWith("2 笔")&&LabelText("FinanceChartSelection").Contains("收入 42"),
                "图表时间段没有定位对应明细或显示收入");
            Press("FinanceClearBucket");
            check(LabelText("FinanceLedgerStatus").StartsWith("3 笔"),"没有恢复完整时段明细");
            Select("FinancePeriod",1);
            check(LabelText("FinanceExpense")=="−199","七天筛选未同步更新收支统计");
            Select("FinanceLedgerDirection",2);
            Select("FinanceLedgerSort",2);
            var rows=Find<VBoxContainer>("FinanceLedgerRows");
            check(rows.GetChildCount()==3&&Contains(rows.GetChild(0),"旧采购"),"支出筛选或金额排序不正确");
            Input("FinanceLedgerSearch","刚才");
            check(rows.GetChildCount()==1&&Contains(rows,"刚才采购"),"收支明细没有响应实时搜索");
            Press("FinanceLedgerReset");
            for(int i=0;i<LedgerPageSize;i++)Game.Meta["finance_ledger"]!.AsArray().Add(Entry(now,i+1,"分页记录"));
            _refreshManagementPage?.Invoke();
            Press("FinanceLedgerNext");
            check(LabelText("FinanceLedgerStatus").Contains("第 2 / 2 页")&&rows.GetChildCount()==4,"收支明细翻页漏掉后续记录");
            var forecast=Game.UpcomingExpenses();
            var payments=WagePayments(forecast);
            check(payments.Length>0&&payments[0].Due==now+NativeGame.Hour&&payments[0].Remaining<0&&
                payments[^1].Remaining==forecast.PrivateRemaining&&LabelText("ScheduledWageRisk").Contains("首个资金缺口"),
                "工资时间线没有按顺序计算余额或标出首个缺口");
            Find<CheckButton>("ScheduledWageToggle").ButtonPressed=true;
            check(Find<VBoxContainer>("ScheduledWageTimeline").Visible,"工资时间线无法展开");

            var recent=Game.Rows("logs");recent.Clear();recent.Add(Log(now,"近期验收日志"));
            var archive=Game.Rows("log_archive");archive.Clear();archive.Add(Log(now-3*NativeGame.Day,"归档验收日志"));
            ShowManagement("日志");
            var logRows=Find<VBoxContainer>("TownLogRows");
            check(logRows.GetChildCount()==2&&Contains(logRows,"归档验收日志"),"日志合并遗漏归档记录");
            Select("TownLogSource",2);
            check(logRows.GetChildCount()==1&&Contains(logRows,"归档验收日志")&&!Contains(logRows,"近期验收日志"),"归档来源筛选混入了近期日志");
            Select("TownLogPeriod",1);
            check(LabelText("TownLogCount").StartsWith("0 条")&&Contains(logRows,"没有符合条件"),"日志时段筛选或空结果提示不正确");
            Press("TownLogReset");
            Input("TownLogSearch","近期验收");
            check(logRows.GetChildCount()==1&&Contains(logRows,"近期验收日志"),"日志没有响应实时搜索或复位");
            GD.Print("FINANCE_EXPERIENCE_VERIFIED period chart_bucket ledger_filters pagination wage_gap log_source");
        }finally{
            _management?.Hide();
            Data.Root["snapshot"]=original;Data.Reindex();
            (_financePeriod,_financeDirection,_financeSort,_financePage,_financeBucket,_financeSearch)=finance;
            (_logPeriod,_logSource,_logPage,_logSearch)=logs;
        }

        static JsonObject Entry(double time,int delta,string reason)=>new(){{"ts",time},{"delta",delta},{"balance",400},{"reason",reason}};
        static JsonObject Log(double time,string message)=>new(){{"ts",time},{"text",message}};
        T Find<T>(string name) where T:Node=>(T)_managementBody!.FindChild(name,true,false)!;
        string LabelText(string name)=>Find<Label>(name).Text;
        void Press(string name)=>Find<Button>(name).EmitSignal(BaseButton.SignalName.Pressed);
        void Select(string name,int selected)
        {
            var option=Find<OptionButton>(name);
            option.Select(selected);option.EmitSignal(OptionButton.SignalName.ItemSelected,(long)selected);
        }
        void Input(string name,string value)
        {
            var input=Find<LineEdit>(name);
            input.Text=value;input.EmitSignal(LineEdit.SignalName.TextChanged,value);
        }
        static bool Contains(Node node,string text)=>node.FindChildren("*","Label",true,false).OfType<Label>()
            .Any(label=>label.Text.Contains(text,StringComparison.Ordinal));
    }
}
