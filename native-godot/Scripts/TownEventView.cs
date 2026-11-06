using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;

public partial class Main
{
    const string WeekendFestivalEntry="weekend-festivals";

    static readonly IReadOnlyDictionary<string,string> EventEffectCaptions=new Dictionary<string,string>{
        ["feed"]="普通饲料消耗",["rep"]="声望奖励",["discount"]="采购价格倍率",["cost"]="金币消耗",
        ["reward"]="金币奖励",["passive_reward"]="自然接待收入",["count"]="产物份数",["gift_rep"]="赠礼声望",
        ["health"]="户外健康变化",["cleanliness"]="清洁变化",["medicine"]="药品消耗",["chance"]="感染概率",
        ["mult"]="涨价倍率",["snacks"]="零食消耗",["happy"]="开心变化"
    };

    void EventControls()
    {
        bool festivals=_managementEntryTarget==WeekendFestivalEntry;
        var tabs=FestivalActions(_managementBody!);
        if(festivals||_managementEntryTarget=="pending-events")_managementFocusTarget=tabs;
        var pending=MarketButton(tabs,"待处理事件",()=>ShowManagement("事件","pending-events"),accent:"事件");
        var weekend=MarketButton(tabs,"周末活动",()=>ShowManagement("事件",WeekendFestivalEntry),accent:"事件");
        pending.Name="TownEventPendingTab";
        weekend.Name="TownEventFestivalTab";
        pending.ToggleMode=weekend.ToggleMode=true;
        pending.SetPressedNoSignal(!festivals);
        weekend.SetPressedNoSignal(festivals);
        void RefreshEventTabs()
        {
            int count=Game.Rows("events").OfType<JsonNode>().Count(IsPendingEvent);
            pending.Text=$"待处理事件 · {count}";
            weekend.Text=Game.CurrentFestival==null?"周末活动":"周末活动 · 进行中";
        }
        RefreshEventTabs();
        _refreshManagementPage+=RefreshEventTabs;
        if(festivals)FestivalControls();else PendingEventControls();
    }

    bool IsPendingEvent(JsonNode entry)=>NativeGame.S(entry,"status")=="待处理"&&NativeGame.D(entry,"expiry_ts")>Game.Now;

    void PendingEventControls()
    {
        Section("小镇事件","按处理截止时间排列");
        var list=new VBoxContainer{Name="TownPendingEventList"};
        _managementBody!.AddChild(list);
        MarketLabel(_managementBody!,"到期事件会自动选择第二项。",14,TownTheme.Muted,wrap:true);
        MarketButton(FestivalActions(_managementBody!),"查看事件历史",()=>ShowManagement("档案","records",()=>{
            _archiveRecordKind=2;_archiveRecordStatus=0;_archiveRecordPage=0;_archiveRecordQuery="";
        }),expand:false,accent:"档案");
        string target=_managementEntryTarget;
        JsonNode[] shown=Array.Empty<JsonNode>();
        bool initialized=false;
        Action[] timers=Array.Empty<Action>();
        RefreshEvents();
        _refreshManagementPage+=RefreshEvents;

        void RefreshEvents()
        {
            var current=Game.Rows("events").OfType<JsonNode>().Where(entry=>IsPendingEvent(entry)||NativeGame.S(entry,"event_id")==target)
                .OrderBy(entry=>NativeGame.D(entry,"expiry_ts")).ToArray();
            if(!initialized||!shown.SequenceEqual(current)){
                initialized=true;
                shown=current;
                foreach(var child in list.GetChildren()){list.RemoveChild(child);child.QueueFree();}
                if(current.Length==0)MarketLabel(list,"暂时没有待处理事件。",15,TownTheme.Muted);
                timers=current.Select(RenderEvent).ToArray();
            }
            foreach(var refresh in timers)refresh();
        }

        Action RenderEvent(JsonNode entry)
        {
            string id=NativeGame.S(entry,"event_id");
            var row=MarketCard(list,color:"f9f0e1");
            row.GetParent<Control>().Name="TownPendingEvent_"+id;
            var title=MarketLabel(row,NativeGame.S(entry,"text"),17,TownTheme.Ink,wrap:true);
            if(id==target)_managementFocusTarget=title;
            var status=MarketLabel(row,"",14,TownTheme.Muted,wrap:true);
            var effect=Game.Rules["EVENT_EFFECTS"]?[NativeGame.S(entry,"kind")] as JsonObject;
            string description=effect==null?"":string.Join(" · ",effect.Select(value=>$"{EventEffectCaptions.GetValueOrDefault(value.Key,value.Key)} {value.Value}"));
            var effects=MarketLabel(row,description,14,TownTheme.Muted,wrap:true);
            var actions=FestivalActions(row);
            if(entry["options"] is JsonArray options)
                for(int i=0;i<Math.Min(2,options.Count);i++){
                    int option=i;
                    MarketButton(actions,options[i]?.GetValue<string>()??$"选择 {i+1}",()=>
                        Act("event",new JsonObject{{"event_id",id},{"option",option}}),expand:false,accent:"事件");
                }
            return ()=>{
                bool pending=IsPendingEvent(entry);
                string state=NativeGame.S(entry,"status");
                status.Text=pending?$"待处理 · {DeadlineText(NativeGame.D(entry,"expiry_ts")-Game.Now)}":state=="待处理"?"已到期":state;
                effects.Visible=pending&&description.Length>0;
                actions.Visible=pending;
            };
        }
    }

    static HFlowContainer FestivalActions(Control parent)
    {
        var actions=new HFlowContainer();
        actions.AddThemeConstantOverride("h_separation",7);
        actions.AddThemeConstantOverride("v_separation",5);
        parent.AddChild(actions);
        return actions;
    }
}
