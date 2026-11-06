using Godot;
using System;
using System.Linq;
using System.Text.Json.Nodes;

public partial class Main
{
    const int LedgerPageSize=12;
    static readonly string[] FinancePeriods={"近 24 游戏小时","近 7 游戏日","近 30 游戏日"};
    int _financePeriod,_financeDirection,_financeSort,_financePage,_financeBucket=-1;
    string _financeSearch="";

    readonly record struct FinanceEntry(double Time,int Delta,int Balance,string Reason);
    readonly record struct FinanceWindow(double Start,double Step,int Count)
    {
        public int Bucket(double time)=>Math.Clamp((int)Math.Floor((time-Start)/Step),0,Count-1);
    }

    static FinanceWindow FinanceWindowAt(double now,int period)=>period switch{
        0=>new(now-NativeGame.Day,4*NativeGame.Hour,6),
        _=>CalendarDays(now,period==1?7:30)
    };

    static FinanceWindow CalendarDays(double now,int days)=>new(Math.Floor(now/NativeGame.Day)*NativeGame.Day-(days-1)*NativeGame.Day,NativeGame.Day,days);

    void FinanceControls()
    {
        var overview=MarketCard(_managementBody!,color:"eaf1e5");
        var heading=new HFlowContainer();heading.AddThemeConstantOverride("h_separation",10);overview.AddChild(heading);
        var title=MarketLabel(heading,"收支概览",19,TownTheme.Leaf);
        title.VerticalAlignment=VerticalAlignment.Center;
        var period=MarketOptions(heading,FinancePeriods,_financePeriod);
        period.Name="FinancePeriod";
        period.AccessibilityName="收支统计时段";
        var forecastLink=FinanceButton(heading,"FinanceForecastLink","查看未来支出");
        var overviewNote=MarketLabel(overview,"",14,TownTheme.Muted,wrap:true);
        var metrics=new HFlowContainer();metrics.AddThemeConstantOverride("h_separation",6);overview.AddChild(metrics);
        var income=Metric("FinanceIncome","期间收入","e3eddf",TownTheme.Leaf);
        var expenses=Metric("FinanceExpense","期间支出","f6e6d8",new Color("784735"));
        var net=Metric("FinanceNet","期间净额","ede8f1",TownTheme.Ink);
        var balance=Metric("FinanceBalance","当前余额","f1eddb",TownTheme.Ink);
        var chart=new FinanceChart{Name="FinanceOverviewChart",CustomMinimumSize=new(0,160),FocusMode=Control.FocusModeEnum.All,
            TooltipText="点击柱形或按方向键查看时间段"};
        overview.AddChild(chart);
        var chartControls=new HFlowContainer();chartControls.AddThemeConstantOverride("h_separation",8);overview.AddChild(chartControls);
        var chartNote=MarketLabel(chartControls,"",14,TownTheme.Muted,wrap:true);
        chartNote.Name="FinanceChartSelection";
        chartNote.CustomMinimumSize=new(220,0);
        chartNote.SizeFlagsHorizontal=Control.SizeFlags.ExpandFill;
        chartNote.VerticalAlignment=VerticalAlignment.Center;
        var clearBucket=FinanceButton(chartControls,"FinanceClearBucket","显示整个时段");

        var ledger=MarketCard(_managementBody!,color:"f5efe3");
        MarketLabel(ledger,"收支明细",19,TownTheme.Leaf);
        var toolbar=new HFlowContainer();toolbar.AddThemeConstantOverride("h_separation",6);ledger.AddChild(toolbar);
        var search=new LineEdit{Name="FinanceLedgerSearch",Text=_financeSearch,PlaceholderText="搜索收支原因",ClearButtonEnabled=true,CustomMinimumSize=new(162,36)};
        toolbar.AddChild(search);
        var direction=MarketOptions(toolbar,new[]{"全部收支","仅收入","仅支出"},_financeDirection);direction.Name="FinanceLedgerDirection";
        var sort=MarketOptions(toolbar,new[]{"最新在前","最早在前","金额从大到小"},_financeSort);sort.Name="FinanceLedgerSort";
        var reset=FinanceButton(toolbar,"FinanceLedgerReset","清除筛选");
        var paging=new HFlowContainer();paging.AddThemeConstantOverride("h_separation",6);ledger.AddChild(paging);
        var previous=FinanceButton(paging,"FinanceLedgerPrevious","上一页");
        var status=MarketLabel(paging,"",14,TownTheme.Muted);status.Name="FinanceLedgerStatus";status.VerticalAlignment=VerticalAlignment.Center;
        var next=FinanceButton(paging,"FinanceLedgerNext","下一页");
        var rows=new VBoxContainer{Name="FinanceLedgerRows"};ledger.AddChild(rows);
        var forecast=ScheduledExpenseControls();
        forecastLink.Pressed+=()=>_managementScroll?.EnsureControlVisible(forecast);

        JsonArray? source=null;
        JsonNode? newest=null;
        int sourceCount=-1,lastCount=-1,lastPage=-1;
        var entries=Array.Empty<FinanceEntry>();
        var shown=Array.Empty<FinanceEntry>();

        period.ItemSelected+=index=>{_financePeriod=(int)index;_financeBucket=-1;_financePage=0;Refresh();};
        chart.BucketSelected+=index=>{_financeBucket=index;_financePage=0;Refresh();};
        clearBucket.Pressed+=()=>{_financeBucket=-1;_financePage=0;Refresh();};
        search.TextChanged+=value=>{_financeSearch=value;_financePage=0;Refresh();};
        direction.ItemSelected+=index=>{_financeDirection=(int)index;_financePage=0;Refresh();};
        sort.ItemSelected+=index=>{_financeSort=(int)index;_financePage=0;Refresh();};
        reset.Pressed+=()=>{
            _financeSearch=search.Text="";
            _financeDirection=_financeSort=_financePage=0;
            _financeBucket=-1;
            direction.Select(0);sort.Select(0);
            Refresh();
        };
        previous.Pressed+=()=>{_financePage--;Refresh();};
        next.Pressed+=()=>{_financePage++;Refresh();};
        Refresh();
        _refreshManagementPage+=Refresh;

        Label Metric(string name,string caption,string color,Color ink)
        {
            var card=MarketCard(metrics,116,color);
            MarketLabel(card,caption,14,TownTheme.Muted);
            var value=MarketLabel(card,"",21,ink);value.Name=name;
            return value;
        }

        void Refresh()
        {
            var current=Data.Meta["finance_ledger"]!.AsArray();
            if(!ReferenceEquals(source,current)||sourceCount!=current.Count||!ReferenceEquals(newest,current.LastOrDefault())){
                source=current;sourceCount=current.Count;newest=current.LastOrDefault();
                entries=current.OfType<JsonNode>().Reverse().Select(row=>new FinanceEntry(NativeGame.D(row,"ts"),NativeGame.I(row,"delta"),
                    NativeGame.I(row,"balance"),NativeGame.S(row,"reason"))).ToArray();
            }
            double now=Game.Now;
            var window=FinanceWindowAt(now,_financePeriod);
            var inPeriod=entries.Where(entry=>entry.Time>=window.Start&&entry.Time<=now).ToArray();
            var incoming=new long[window.Count];var outgoing=new long[window.Count];
            foreach(var entry in inPeriod){
                int bucket=window.Bucket(entry.Time);
                if(entry.Delta>0)incoming[bucket]+=entry.Delta;
                else outgoing[bucket]-=(long)entry.Delta;
            }
            long plus=incoming.Sum(),minus=outgoing.Sum();
            income.Text=plus==0?"0":$"+{plus:N0}";expenses.Text=minus==0?"0":$"−{minus:N0}";net.Text=$"{plus-minus:+#,0;-#,0;0}";
            balance.Text=$"{NativeGame.I(Data.Meta,"coins"):N0}";
            overviewNote.Text=$"公共预算 {NativeGame.I(Game.Commons,"funds"):N0} 金币 · 与庄园收支分开 · 期间记录 {inPeriod.Length} 笔";
            var buckets=new FinanceChart.Bucket[window.Count];
            for(int i=0;i<buckets.Length;i++){
                double from=window.Start+i*window.Step,until=Math.Min(now,from+window.Step);
                string label=_financePeriod==0?$"{(int)Math.Floor((from%NativeGame.Day+NativeGame.Day)%NativeGame.Day/NativeGame.Hour):00}:00":
                    i==buckets.Length-1?"今天":$"−{buckets.Length-1-i}";
                buckets[i]=new(label,$"{TownProjectClock(from)} 至 {TownProjectClock(until)}",incoming[i],outgoing[i]);
            }
            chart.SetBuckets(buckets,_financeBucket);
            if(_financeBucket>=0){
                var selected=buckets[_financeBucket];
                chartNote.Text=$"{selected.Range} · 收入 {selected.Income:N0} · 支出 {selected.Expense:N0}";
            }else chartNote.Text="点选柱形或用方向键查看对应明细。";
            clearBucket.Visible=_financeBucket>=0;
            var filtered=inPeriod.Where(entry=>(_financeBucket<0||window.Bucket(entry.Time)==_financeBucket)&&
                (_financeDirection==0||(_financeDirection==1?entry.Delta>0:entry.Delta<0))&&
                entry.Reason.Contains(_financeSearch.Trim(),StringComparison.OrdinalIgnoreCase));
            filtered=_financeSort switch{
                1=>filtered.OrderBy(entry=>entry.Time),
                2=>filtered.OrderByDescending(entry=>Math.Abs((long)entry.Delta)),
                _=>filtered.OrderByDescending(entry=>entry.Time)
            };
            var matching=filtered.ToArray();
            int pages=Math.Max(1,(matching.Length+LedgerPageSize-1)/LedgerPageSize);
            _financePage=Math.Clamp(_financePage,0,pages-1);
            var visible=matching.Skip(_financePage*LedgerPageSize).Take(LedgerPageSize).ToArray();
            status.Text=$"{matching.Length} 笔 · 第 {_financePage+1} / {pages} 页";
            previous.Disabled=_financePage==0;next.Disabled=_financePage+1>=pages;
            if(lastCount==matching.Length&&lastPage==_financePage&&shown.SequenceEqual(visible))return;
            lastCount=matching.Length;lastPage=_financePage;shown=visible;
            foreach(var child in rows.GetChildren()){rows.RemoveChild(child);child.QueueFree();}
            if(visible.Length==0){
                MarketLabel(rows,"当前条件下没有收支明细。可切换时段或清除筛选。",14,TownTheme.Muted,wrap:true);
                return;
            }
            foreach(var entry in visible){
                var card=MarketCard(rows,color:"fffdf7");
                var row=new HFlowContainer();row.AddThemeConstantOverride("h_separation",8);card.AddChild(row);
                MarketLabel(row,$"{entry.Delta:+#,0;-#,0;0} 金币",17,entry.Delta>0?TownTheme.Leaf:new Color("884b39"));
                MarketLabel(row,$"余额 {entry.Balance:N0} · {TownProjectClock(entry.Time)}",14,TownTheme.Muted);
                MarketLabel(card,entry.Reason.Length==0?"收支原因未记录":entry.Reason,15,TownTheme.Ink,wrap:true);
            }
        }
    }

    static Button FinanceButton(Container parent,string name,string caption)
    {
        var button=new Button{Name=name,Text=caption,CustomMinimumSize=new(0,34)};
        TownTheme.AccentButton(button,"财务",5);
        parent.AddChild(button);
        return button;
    }
}
