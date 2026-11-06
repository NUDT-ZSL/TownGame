using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

public partial class Main
{
    bool _procurementExpanded;
    readonly HashSet<string> _procurementOrders=new(StringComparer.Ordinal);

    void AddProcurementChecklist()
    {
        var card=MarketCard(_managementBody!,color:"eef3e8");
        var toggle=new CheckButton{Name="ProcurementToggle",Text="多订单采购清单",ButtonPressed=_procurementExpanded};
        TownTheme.AccentButton(toggle,"订单",6);
        card.AddChild(toggle);
        var details=new VBoxContainer{Visible=_procurementExpanded};
        details.AddThemeConstantOverride("separation",6);
        card.AddChild(details);
        MarketLabel(details,"勾选要一起备货的订单。清单会合并同类需求，并按每单已预留的物资分别抵扣。",color:TownTheme.Muted,wrap:true);
        var choices=new VBoxContainer();
        details.AddChild(choices);
        var selection=new HFlowContainer();
        selection.AddThemeConstantOverride("h_separation",8);
        details.AddChild(selection);
        var totals=new VBoxContainer{Name="ProcurementTotals"};
        totals.AddThemeConstantOverride("separation",6);
        details.AddChild(totals);
        string[] visibleIds=Array.Empty<string>();
        NativeGame.ProcurementLine[]? visibleLines=null;
        bool initialized=false;
        MarketButton(selection,"全选物资订单",()=>{
            _procurementOrders.UnionWith(Game.ActiveProcurementOrders().Select(order=>NativeGame.S(order,"order_id")));
            Update(force:true);
        },expand:false,accent:"订单");
        MarketButton(selection,"取消全选",()=>{_procurementOrders.Clear();Update(force:true);},expand:false,accent:"订单");

        void Clear(Container container)
        {
            foreach(Node child in container.GetChildren()){container.RemoveChild(child);child.QueueFree();}
        }
        void Update(bool force=false)
        {
            if(!details.Visible)return;
            var active=Game.ActiveProcurementOrders().ToArray();
            var ids=active.Select(order=>NativeGame.S(order,"order_id")).ToArray();
            if(force||!initialized||!visibleIds.SequenceEqual(ids)){
                visibleIds=ids;
                initialized=true;
                _procurementOrders.IntersectWith(ids);
                Clear(choices);
                selection.Visible=active.Length>0;
                if(active.Length==0)MarketLabel(choices,"目前没有进行中的物资订单。",color:TownTheme.Muted);
                foreach(var order in active){
                    string id=NativeGame.S(order,"order_id");
                    var check=new CheckBox{
                        Text=$"{NativeGame.S(order,"npc")} · {NativeGame.S(order,"item")} ×{NativeGame.I(order,"quantity")}",
                        ButtonPressed=_procurementOrders.Contains(id)
                    };
                    choices.AddChild(check);
                    check.Toggled+=enabled=>{
                        if(enabled)_procurementOrders.Add(id);
                        else _procurementOrders.Remove(id);
                        Update();
                    };
                }
            }
            var lines=Game.OrderProcurement(_procurementOrders);
            if(visibleLines?.SequenceEqual(lines)==true)return;
            visibleLines=lines;
            Clear(totals);
            if(lines.Length==0){
                MarketLabel(totals,"勾选订单后查看需求和缺口。",color:TownTheme.Muted);
                return;
            }
            MarketLabel(totals,lines.All(line=>line.Missing==0)?"所选订单物资已备齐":"合并后的物资缺口",17,TownTheme.Accent("订单"));
            foreach(var line in lines){
                var itemCard=MarketCard(totals);
                MarketLabel(itemCard,$"{line.Item} · 需要 {line.Required} · 缺 {line.Missing}",16,TownTheme.Accent(line.Missing>0?"事件":"订单"));
                string other=line.OtherReserved>0?$" · 其他用途预留 {line.OtherReserved}":"";
                MarketLabel(itemCard,$"所选订单已预留 {line.ReservedForOrders} · 可分配现货 {line.Free}{other}",color:TownTheme.Muted,wrap:true);
                if(line.Missing==0)continue;
                var links=new HFlowContainer();
                itemCard.AddChild(links);
                bool buy=Game.Rules["ITEMS"]?[line.Item]!=null&&Game.BuyPrice(line.Item)>0,craft=Game.Rules["RECIPES"]?[line.Item]!=null;
                if(buy)MarketButton(links,"去商店补货",()=>{
                    _marketSection=1;
                    _marketFilter=0;
                    _marketSearch=line.Item;
                    _marketBuyQuantity=Math.Clamp(line.Missing,1,99);
                    ShowManagement("商店",$"supply:{line.Item}");
                },expand:false);
                if(craft)MarketButton(links,"查看加工配方",()=>{
                    _marketSection=2;
                    ShowManagement("商店",$"recipe:{line.Item}");
                },expand:false);
                if(!buy&&!craft)MarketLabel(itemCard,OrderSupplySource(line.Item),color:TownTheme.Muted,wrap:true);
            }
        }
        void Refresh()=>Update();
        toggle.Toggled+=expanded=>{
            details.Visible=_procurementExpanded=expanded;
            if(expanded)Update();
        };
        if(_managementEntryTarget=="procurement"){
            _managementFocusTarget=card.GetParent<Control>();
            toggle.ButtonPressed=true;
        }
        Refresh();
        _refreshManagementPage+=Refresh;
        card.TreeExiting+=()=>_refreshManagementPage-=Refresh;
    }
}
