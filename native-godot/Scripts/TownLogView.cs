using Godot;
using System;
using System.Linq;
using System.Text.Json.Nodes;

public partial class Main
{
    const int TownLogPageSize=18;
    int _logPeriod,_logSource,_logPage;
    string _logSearch="";
    readonly record struct TownLogEntry(double Time,string Message,bool Archived);

    void LogControls()
    {
        var card=MarketCard(_managementBody!,color:"efeaf0");
        MarketLabel(card,"小镇日志",19,TownTheme.Leaf);
        var filters=new HFlowContainer();filters.AddThemeConstantOverride("h_separation",7);card.AddChild(filters);
        var search=new LineEdit{Name="TownLogSearch",PlaceholderText="按关键词查找",ClearButtonEnabled=true,Text=_logSearch,CustomMinimumSize=new(164,36)};
        filters.AddChild(search);
        var period=MarketOptions(filters,new[]{"全部时间","近 24 游戏小时","近 7 游戏日"},_logPeriod);period.Name="TownLogPeriod";
        var source=MarketOptions(filters,new[]{"全部来源","近期日志","归档日志"},_logSource);source.Name="TownLogSource";
        var reset=FinanceButton(filters,"TownLogReset","清除筛选");
        var toolbar=new HFlowContainer();toolbar.AddThemeConstantOverride("h_separation",7);card.AddChild(toolbar);
        var previous=FinanceButton(toolbar,"TownLogPrevious","上一页");
        var count=MarketLabel(toolbar,"",14,TownTheme.Muted);count.Name="TownLogCount";count.VerticalAlignment=VerticalAlignment.Center;
        var next=FinanceButton(toolbar,"TownLogNext","下一页");
        var rows=new VBoxContainer{Name="TownLogRows"};card.AddChild(rows);

        JsonArray? priorArchive=null,priorRecent=null;
        JsonNode? priorLast=null;
        int archiveCount=-1,recentCount=-1,lastPage=-1,lastCount=-1;
        double lastNow=double.NegativeInfinity,expires=double.PositiveInfinity;
        TownLogEntry[] shown=Array.Empty<TownLogEntry>();
        search.TextChanged+=value=>{_logSearch=value;_logPage=0;Refresh(true);};
        period.ItemSelected+=index=>{_logPeriod=(int)index;_logPage=0;Refresh(true);};
        source.ItemSelected+=index=>{_logSource=(int)index;_logPage=0;Refresh(true);};
        reset.Pressed+=()=>{
            _logSearch=search.Text="";
            _logPeriod=_logSource=_logPage=0;
            period.Select(0);source.Select(0);
            Refresh(true);
        };
        previous.Pressed+=()=>{_logPage--;Refresh(true);};
        next.Pressed+=()=>{_logPage++;Refresh(true);};
        Refresh(true);
        _refreshManagementPage+=()=>Refresh(false);

        void Refresh(bool force)
        {
            var archive=Game.Rows("log_archive");var recent=Game.Rows("logs");
            var last=recent.LastOrDefault();
            double now=Game.Now;
            if(!force&&ReferenceEquals(priorArchive,archive)&&ReferenceEquals(priorRecent,recent)&&ReferenceEquals(priorLast,last)&&
                archive.Count==archiveCount&&recent.Count==recentCount&&now>=lastNow&&now<=expires)return;
            priorArchive=archive;priorRecent=recent;priorLast=last;
            archiveCount=archive.Count;recentCount=recent.Count;lastNow=now;
            double span=_logPeriod==0?double.PositiveInfinity:(_logPeriod==1?1:7)*NativeGame.Day;
            string keyword=_logSearch.Trim();
            var recentRows=_logSource==2?Enumerable.Empty<TownLogEntry>():Read(recent,false);
            var archivedRows=_logSource==1?Enumerable.Empty<TownLogEntry>():Read(archive,true);
            var matching=recentRows.Concat(archivedRows).ToArray();
            expires=matching.Length==0?double.PositiveInfinity:matching.Min(row=>row.Time)+span;
            int pages=Math.Max(1,(matching.Length+TownLogPageSize-1)/TownLogPageSize);
            _logPage=Math.Clamp(_logPage,0,pages-1);
            var visible=matching.Skip(_logPage*TownLogPageSize).Take(TownLogPageSize).ToArray();
            count.Text=$"{matching.Length} 条 · 第 {_logPage+1} / {pages} 页";
            previous.Disabled=_logPage==0;next.Disabled=_logPage+1>=pages;
            if(lastCount==matching.Length&&lastPage==_logPage&&shown.SequenceEqual(visible))return;
            lastCount=matching.Length;lastPage=_logPage;shown=visible;
            foreach(var child in rows.GetChildren()){rows.RemoveChild(child);child.QueueFree();}
            if(visible.Length==0){
                MarketLabel(rows,recent.Count+archive.Count==0?"目前还没有小镇日志。":"没有符合条件的日志。可切换来源、扩大时段或清除筛选。",
                    14,TownTheme.Muted,wrap:true);
                return;
            }
            foreach(var entry in visible){
                var item=MarketCard(rows,color:entry.Archived?"f1edf0":"fffaf1");
                MarketLabel(item,$"{TownProjectClock(entry.Time)} · {(entry.Archived?"归档":"近期")}",14,TownTheme.Muted);
                MarketLabel(item,entry.Message,15,TownTheme.Ink,wrap:true);
            }

            System.Collections.Generic.IEnumerable<TownLogEntry> Read(JsonArray list,bool archived)
            {
                foreach(var row in list.OfType<JsonNode>().Reverse()){
                    double time=NativeGame.D(row,"ts");
                    if(time<now-span||time>now)continue;
                    string message=NativeGame.S(row,"text");
                    if(message.Contains(keyword,StringComparison.OrdinalIgnoreCase))yield return new(time,message,archived);
                }
            }
        }
    }
}
