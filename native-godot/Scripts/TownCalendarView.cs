using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;

public sealed partial class NativeGame
{
    internal readonly record struct CalendarEntry(double Due,string Category,string Title,string Target="");

    internal CalendarEntry[] CalendarEntries(double horizon)
    {
        var entries=new List<CalendarEntry>();
        void Schedule(double due,string category,string title,string target="")
        {
            if(due>=Now&&due<horizon)entries.Add(new(due,category,title,target));
        }

        foreach(var building in Rows("town_buildings").OfType<JsonNode>().Where(building=>S(building,"status")=="building")){
            string key=S(building,"catalog_key");
            string name=Rules["CATALOG"]?[key] is JsonObject catalog?S(catalog,"name",key):key;
            Schedule(D(building,"finish_ts"),"建造",$"{name}完工",S(building,"building_id"));
        }
        for(int index=1;index<=FarmPlotCount;index++)
            if(FarmPlan(index) is {Ready:false} plan)Schedule(plan.Mature,"环境",$"{index} 号地 · {plan.Crop}成熟",$"farm-plot:{index}");
        if(!FarmPlotsEnabled&&Effective("fields")&&D(Meta,"field_ready_ts")==0)
            Schedule(D(Meta,"next_field_ts"),"环境","混种农田预计成熟","farm-plots");
        foreach(var wage in UpcomingWages(horizon)){
            int shift=0;
            for(double due=wage.FirstDue;due<horizon&&shift<wage.Shifts;due+=wage.Interval,shift++)
                Schedule(due,"财务",$"{wage.Role}工资 · {wage.PerShift} 金币","scheduled-expenses");
        }
        foreach(var order in Rows("orders").OfType<JsonNode>().Where(order=>S(order,"status")=="进行中")){
            string item=OrderRequestCaption(order);
            Schedule(D(order,"expiry_ts"),"订单",$"{S(order,"npc")}的{item}委托截止",S(order,"order_id"));
        }
        foreach(var egg in IncubatorEggs())Schedule(egg.Due,"商店",$"{egg.Species}蛋预计破壳","incubator");
        foreach(var entry in Rows("events").OfType<JsonNode>().Where(entry=>S(entry,"status")=="待处理"))
            Schedule(D(entry,"expiry_ts"),"事件",$"{S(entry,"text")} · 处理截止",S(entry,"event_id"));
        var commission=TownCommission();
        if(commission.Stage<3)Schedule(commission.Deadline,"成长","本季委托截止","town-commission");
        foreach(var festival in FestivalOccurrences(Now))
            Schedule(festival.Opens,"事件",$"{festival.Title}开场","weekend-festivals");
        return entries.OrderBy(entry=>entry.Due).ThenBy(entry=>entry.Category,StringComparer.Ordinal).ToArray();
    }
}

public partial class Main
{
    bool _townCalendarOpen;
    double _townCalendarDate=double.NaN;
    string _townCalendarCategory="";
    static readonly (string Page,string Caption)[] CalendarCategories={
        ("","全部"),("建造","工程"),("环境","农田"),("财务","工资"),("订单","订单"),
        ("成长","委托"),("事件","活动与事件"),("商店","孵化")};
    static readonly string[] TownWeekdays={"周一","周二","周三","周四","周五","周六","周日"};

    string CalendarDayTitle(double day)
    {
        int number=(int)Math.Max(0,Math.Floor(day/NativeGame.Day)-Math.Floor(NativeGame.D(Game.Meta,"start_ts")/NativeGame.Day));
        return $"第 {number+1} 天 · {TownWeekdays[number%TownWeekdays.Length]}";
    }

    void CalendarControls()
    {
        ExpiryJournalControls();
        var card=MarketCard(_managementBody!,color:"e9edf2");
        var toggle=MarketButton(card,"小镇周历",()=>{},accent:"地图");
        toggle.Name="TownCalendarToggle";
        toggle.ToggleMode=true;
        toggle.SetPressedNoSignal(_townCalendarOpen);
        var details=new VBoxContainer{Visible=_townCalendarOpen};
        details.AddThemeConstantOverride("separation",5);
        card.AddChild(details);
        MarketLabel(details,"查看未来七天的截止时间与预计事项。周历在 00:00 换日。",color:TownTheme.Muted,wrap:true);
        var dateBar=new HFlowContainer();dateBar.AddThemeConstantOverride("h_separation",6);details.AddChild(dateBar);
        var back=MarketButton(dateBar,"‹ 前一天",()=>{},expand:false,accent:"地图");
        back.Name="TownCalendarPrevious";
        var days=MarketOptions(dateBar,Array.Empty<string>(),-1);
        days.Name="TownCalendarDay";
        var next=MarketButton(dateBar,"后一天 ›",()=>{},expand:false,accent:"地图");
        next.Name="TownCalendarNext";
        var today=MarketButton(dateBar,"回到今天",()=>{},expand:false,accent:"地图");
        today.Name="TownCalendarToday";
        var categoryRow=new HFlowContainer();categoryRow.AddThemeConstantOverride("h_separation",6);details.AddChild(categoryRow);
        var categories=MarketOptions(categoryRow,CalendarCategories.Select(category=>category.Caption).ToArray(),0);
        categories.Name="TownCalendarCategory";
        var summary=MarketLabel(categoryRow,"",color:TownTheme.Muted);
        summary.Name="TownCalendarSummary";
        summary.VerticalAlignment=VerticalAlignment.Center;
        var rows=new VBoxContainer{Name="TownCalendarEntries"};
        details.AddChild(rows);
        double shownToday=double.NaN;
        NativeGame.CalendarEntry[] previous=Array.Empty<NativeGame.CalendarEntry>();
        Label[] timing=Array.Empty<Label>();

        void ChooseDay(double day){_townCalendarDate=day;Refresh(true);}
        void Refresh(bool force=false)
        {
            if(!details.Visible)return;
            double first=Math.Floor(Game.Now/NativeGame.Day)*NativeGame.Day;
            bool dateChanged=shownToday!=first;
            if(dateChanged){
                shownToday=first;
                days.Clear();
                for(int index=0;index<7;index++)days.AddItem((index==0?"今天 · ":index==1?"明天 · ":"")+CalendarDayTitle(first+index*NativeGame.Day));
            }
            if(!double.IsFinite(_townCalendarDate))_townCalendarDate=first;
            double day=_townCalendarDate=Math.Clamp(_townCalendarDate,first,first+6*NativeGame.Day);
            int indexDay=(int)((day-first)/NativeGame.Day);
            days.Select(indexDay);
            back.Disabled=today.Disabled=indexDay==0;
            next.Disabled=indexDay==6;
            var all=Game.CalendarEntries(day+NativeGame.Day).Where(entry=>entry.Due>=day).ToArray();
            int selected=Math.Max(0,Array.FindIndex(CalendarCategories,category=>category.Page==_townCalendarCategory));
            for(int index=0;index<CalendarCategories.Length;index++){
                var category=CalendarCategories[index];
                int count=index==0?all.Length:all.Count(entry=>entry.Category==category.Page);
                categories.SetItemText(index,$"{category.Caption} · {count}");
            }
            categories.Select(selected);
            var entries=selected==0?all:all.Where(entry=>entry.Category==CalendarCategories[selected].Page).ToArray();
            summary.Text=$"{CalendarDayTitle(day)} · {entries.Length} 项";
            if(force||dateChanged||!entries.SequenceEqual(previous)){
                previous=entries;
                foreach(var row in rows.GetChildren()){rows.RemoveChild(row);row.QueueFree();}
                if(entries.Length==0)MarketLabel(rows,selected==0?"当天没有排定事项，可以切换日期查看。":"当天没有此类事项，可切换回全部。",color:TownTheme.Muted,wrap:true);
                timing=new Label[entries.Length];
                for(int index=0;index<entries.Length;index++){
                    var entry=entries[index];
                    var row=MarketCard(rows,color:"f8f4e9");
                    MarketLabel(row,entry.Title,16,TownTheme.Ink,wrap:true);
                    timing[index]=MarketLabel(row,"",14,TownTheme.Muted,wrap:true);
                    MarketButton(row,$"查看{entry.Category}",()=>OpenCalendarEntry(entry),expand:false,accent:entry.Category);
                }
            }
            for(int index=0;index<entries.Length;index++){
                var entry=entries[index];
                int hour=(int)(entry.Due%NativeGame.Day/NativeGame.Hour),minute=(int)(entry.Due%NativeGame.Hour/60);
                string status=entry.Due<=Game.Now?"现在":DeadlineText(entry.Due-Game.Now);
                timing[index].Text=$"{entry.Category} · {hour:00}:{minute:00} · {status}";
            }
        }
        back.Pressed+=()=>ChooseDay(_townCalendarDate-NativeGame.Day);
        next.Pressed+=()=>ChooseDay(_townCalendarDate+NativeGame.Day);
        today.Pressed+=()=>ChooseDay(Math.Floor(Game.Now/NativeGame.Day)*NativeGame.Day);
        toggle.Toggled+=open=>{details.Visible=_townCalendarOpen=open;if(open)Refresh(true);};
        days.ItemSelected+=index=>ChooseDay((shownToday+index*NativeGame.Day));
        categories.ItemSelected+=index=>{_townCalendarCategory=CalendarCategories[(int)index].Page;Refresh(true);};
        _refreshManagementPage+=()=>Refresh();
        Refresh(true);
    }

    void OpenCalendarEntry(NativeGame.CalendarEntry entry)
    {
        if(entry.Category=="建造"){OpenTownProject(entry.Target);return;}
        if(entry.Target=="incubator")_marketSection=0;
        if(entry.Target.StartsWith("farm-plot:",StringComparison.Ordinal)&&int.TryParse(entry.Target[10..],out int plot)){
            _activeFarmPlot=plot;
            ShowManagement("环境","farm-plots");
            return;
        }
        ShowManagement(entry.Category,entry.Target);
    }
}
