using Godot;
using System;
using System.Linq;
using System.Text.Json.Nodes;

public partial class Main
{
    static readonly string[] ExpiryModes={"earliest","newest","sell","compost"};
    string _expiryPolicyItem="";

    Action ExpiryPolicyControls(Container content)
    {
        var card=MarketCard(content,color:"eef1e4");
        MarketLabel(card,"物资临期规则",18,TownTheme.Leaf);
        MarketLabel(card,"默认先用最早到期的批次。堆肥仅处理距离到期 2 游戏小时内且未预留的份数。",14,TownTheme.Muted,wrap:true);
        var items=Game.Rules["ITEMS"]!.AsObject().Where(pair=>pair.Key!="腐败物"&&NativeGame.D(pair.Value!,"ttl")>0).Select(pair=>pair.Key).ToArray();
        if(items.Length==0)return ()=>{};
        var options=new HFlowContainer();
        options.AddThemeConstantOverride("h_separation",6);
        card.AddChild(options);
        var item=MarketOptions(options,items,Math.Max(0,Array.IndexOf(items,_expiryPolicyItem)));
        var mode=MarketOptions(options,new[]{"优先使用临期批次","优先使用较新批次","临期提醒出售","未预留部分自动堆肥"},0);
        var save=MarketButton(options,"保存规则",()=>Act("expiry_policy",new JsonObject{{"item",items[item.Selected]},{"mode",ExpiryModes[mode.Selected]}}),expand:false);
        var compost=MarketLabel(card,"",14,TownTheme.Muted,wrap:true);
        var reminders=MarketLabel(card,"",14,TownTheme.Ink,wrap:true);
        string activeMode="";
        void RefreshChoice()
        {
            bool sell=NativeGame.I(Game.Spec("ITEMS",items[item.Selected]),"sell")>0;
            mode.SetItemDisabled(2,!sell);
            activeMode=Game.ExpiryPolicy(items[item.Selected]);
            mode.Select(Math.Max(0,Array.IndexOf(ExpiryModes,activeMode)));
            save.Disabled=true;
        }
        void Refresh()
        {
            if(activeMode!=Game.ExpiryPolicy(items[item.Selected]))RefreshChoice();
            compost.Text=$"农田堆肥 {Game.FarmCompost} 份 · 可在环境页为生长中的作物施肥。";
            var alerts=Game.ExpirySaleReminders().Select(reminder=>$"{reminder.Item} {reminder.Count} 份 · 最早 {TownProjectClock(reminder.Due)} 到期").ToArray();
            reminders.Text=alerts.Length>0?"出售提醒\n"+string.Join("\n",alerts):"暂无临期出售提醒。";
        }
        item.ItemSelected+=index=>{_expiryPolicyItem=items[(int)index];RefreshChoice();};
        mode.ItemSelected+=index=>save.Disabled=ExpiryModes[(int)index]==activeMode;
        RefreshChoice();
        return Refresh;
    }

    void ExpiryJournalControls()
    {
        var card=MarketCard(_managementBody!,color:"f4ebda");
        var label=MarketLabel(card,"",16,TownTheme.Accent("商店"),wrap:true);
        NativeGame.ExpiryReminder[] alerts=Array.Empty<NativeGame.ExpiryReminder>();
        MarketButton(card,"查看可出售物资",()=>{
            if(alerts.Length==0)return;
            _marketSection=1;
            _marketFilter=2;
            _marketSearch=alerts[0].Item;
            _marketSellQuantity=Math.Clamp(alerts[0].Count,1,99);
            ShowManagement("商店",$"supply:{alerts[0].Item}");
        },expand:false);
        void Refresh()
        {
            alerts=Game.ExpirySaleReminders().ToArray();
            card.GetParent<Control>().Visible=alerts.Length>0;
            label.Text="临期出售提醒 · "+string.Join("、",alerts.Select(item=>$"{item.Item} {item.Count} 份"));
        }
        _refreshManagementPage+=Refresh;
        Refresh();
    }
}
