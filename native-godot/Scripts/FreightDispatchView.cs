using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;

public sealed partial class NativeGame
{
    const string FreightPriorityKey="dispatch_priority";
    internal string FreightPriority=>S(FreightState,FreightPriorityKey,"auto");
    internal readonly record struct FreightDispatchLine(string Source,string Destination,string Item,int Ready,int Stock,int Reserve,string Reason);

    IEnumerable<string> FreightSourceOrder()
    {
        int offset=I(FreightState,"trips")%FreightSources.Length;
        var rotation=FreightSources.Skip(offset).Concat(FreightSources.Take(offset));
        return FreightPriority=="auto"?rotation:rotation.OrderByDescending(source=>(source=="industry"?"wood":"food")==FreightPriority);
    }

    internal FreightDispatchLine[] FreightDispatchQueue()=>FreightSourceOrder().Select(source=>{
        const string destination="market";
        var cargo=FreightCargoAvailability(source);
        string reason=ZoneLevel(source)==0?"生产街区未开放":!Effective(source)?"生产街区与庄园道路不通":
            ZoneLevel(destination)==0?"商业区未开放":!Effective(destination)?"商业区与庄园道路不通":
            cargo.Loadable==0?"达到公共储备下限": "";
        return new FreightDispatchLine(source,destination,cargo.Item,cargo.Loadable,cargo.Stock,cargo.Reserve,reason);
    }).ToArray();

    string FreightPriorityCommand(JsonObject args)
    {
        string value=S(args,"priority");
        if(value is not ("auto" or "food" or "wood"))throw new InvalidOperationException("货运优先级无效");
        FreightState[FreightPriorityKey]=value;
        return "已更新下一趟货运的调度顺序";
    }
}

public partial class Main
{
    static readonly string[] FreightPriorityChoices={"auto","food","wood"};
    int _freightDispatchReadiness,_freightDispatchCargo;

    Action FreightDispatchControls(Container parent)
    {
        var card=MarketCard(parent,color:"e8edf1");
        MarketLabel(card,"货运调度清单",18,TownTheme.Leaf);
        MarketLabel(card,"指定货类后优先安排有货、通路的来源，其他可出发线路依次候运。车上的货照原路线完成。",14,TownTheme.Muted,wrap:true);
        var form=new HFlowContainer();
        form.AddThemeConstantOverride("h_separation",6);
        card.AddChild(form);
        var priority=MarketOptions(form,new[]{"默认轮换","优先公共粮食","优先公共木材"},Math.Max(0,Array.IndexOf(FreightPriorityChoices,Game.FreightPriority)));
        priority.Name="TownFreightPriority";
        var save=MarketButton(form,"更新优先级",()=>Act("freight_priority",new JsonObject{{"priority",FreightPriorityChoices[priority.Selected]}}),expand:false,accent:"自治");
        var current=MarketLabel(card,"",14,TownTheme.Muted,wrap:true);
        var filters=AutonomyLinks(card);
        var readiness=MarketOptions(filters,new[]{"全部线路","货与路就绪","需处理"},_freightDispatchReadiness);
        readiness.Name="TownFreightDispatchReadiness";
        readiness.AccessibilityName="按候运状态筛选";
        var cargoType=MarketOptions(filters,new[]{"全部物资","粮食","木材"},_freightDispatchCargo);
        cargoType.Name="TownFreightDispatchCargo";
        cargoType.AccessibilityName="按候运物资筛选";
        var reset=new Button{Name="TownFreightDispatchReset",Text="清除筛选"};
        TownTheme.AccentButton(reset,"自治",6);
        filters.AddChild(reset);
        var count=MarketLabel(card,"",14,TownTheme.Muted);
        count.Name="TownFreightDispatchCount";
        var rows=new VBoxContainer{Name="TownFreightDispatchRows"};
        card.AddChild(rows);
        NativeGame.FreightDispatchLine[]? previous=null;
        priority.ItemSelected+=_=>save.Disabled=FreightPriorityChoices[priority.Selected]==Game.FreightPriority;
        readiness.ItemSelected+=index=>{_freightDispatchReadiness=(int)index;Refresh();};
        cargoType.ItemSelected+=index=>{_freightDispatchCargo=(int)index;Refresh();};
        reset.Pressed+=()=>{_freightDispatchReadiness=_freightDispatchCargo=0;readiness.Select(0);cargoType.Select(0);Refresh();};
        return Refresh;

        void Refresh()
        {
            save.Disabled=FreightPriorityChoices[priority.Selected]==Game.FreightPriority;
            var freight=Game.FreightState;
            double next=NativeGame.D(freight,"next_trip_ts");
            current.Text=!NativeGame.B(Game.Commons,"enabled")?"自治已暂停":NativeGame.S(freight,"driver_id").Length>0?"现有车次在途，以下是后续候运清单":
                Game.IsResidentNight?"夜间暂停新车次":next>Game.Now?$"下一趟最早 {TownProjectClock(next)}":
                Game.FreightDriversAvailable==0?"正在等待空闲成年司机":"可安排下一趟";
            var all=Game.FreightDispatchQueue();
            int ready=all.Count(line=>line.Reason.Length==0);
            readiness.SetItemText(0,$"全部线路 · {all.Length}");
            readiness.SetItemText(1,$"货与路就绪 · {ready}");
            readiness.SetItemText(2,$"需处理 · {all.Length-ready}");
            var lines=all.Where(line=>(_freightDispatchReadiness==0||(_freightDispatchReadiness==1)==(line.Reason.Length==0))&&
                (_freightDispatchCargo==0||(_freightDispatchCargo==1?line.Item=="food":line.Item=="wood"))).ToArray();
            count.Text=$"显示 {lines.Length} / {all.Length} 条线路";
            reset.Disabled=_freightDispatchReadiness==0&&_freightDispatchCargo==0;
            if(previous!=null&&lines.SequenceEqual(previous))return;
            previous=lines;
            foreach(var row in rows.GetChildren()){rows.RemoveChild(row);row.QueueFree();}
            if(lines.Length==0)MarketLabel(rows,"没有符合条件的候运线路。可调整物资或候运状态。",14,TownTheme.Muted,wrap:true);
            foreach(var line in lines){
                var entry=MarketCard(rows);
                entry.GetParent().Name=$"TownFreightDispatchLine_{line.Source}";
                MarketLabel(entry,$"{AutonomyZoneName(line.Source)} → {AutonomyZoneName(line.Destination)} · {(line.Item=="food"?"粮食":"木材")}",16,TownTheme.Ink,wrap:true);
                string state=line.Reason.Length>0?$"需处理 · {line.Reason}":"货与路就绪";
                MarketLabel(entry,state,14,line.Reason.Length==0?TownTheme.Leaf:new Color("873d31"),wrap:true);
                int surplus=Math.Max(0,line.Stock-line.Reserve);
                MarketLabel(entry,$"公共库存 {line.Stock} 份 · 保护 {line.Reserve} 份 · 余量 {surplus} 份 · 本趟可装 {line.Ready} / {NativeGame.FreightLoadLimit(line.Item)} 份",14,TownTheme.Muted,wrap:true);
                string mapZone=line.Reason.StartsWith("商业区",StringComparison.Ordinal)?line.Destination:line.Source;
                MarketButton(entry,"在地图查看",()=>OpenAutonomyDistrict(mapZone),expand:false,accent:"地图");
            }
        }
    }
}
