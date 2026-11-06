using Godot;
using System;
using System.Linq;
using System.Text.Json.Nodes;

public partial class Main
{
    string _supplyWatchItem="普通饲料";

    Action SupplyWatchControls(Container parent)
    {
        var card=MarketCard(parent,color:"e8efe7");
        MarketLabel(card,"库存保底线",18,TownTheme.Leaf);
        MarketLabel(card,"按最近 3 个游戏日记录的实际出库量估算。预留物资不计入可用库存。",color:TownTheme.Muted,wrap:true);
        var items=Game.Rules["ITEMS"]!.AsObject().Select(pair=>pair.Key).Where(item=>item!="腐败物").ToArray();
        var form=new HFlowContainer();form.AddThemeConstantOverride("h_separation",8);card.AddChild(form);
        string target=_managementEntryTarget;
        if(target.StartsWith("supply-watch:",StringComparison.Ordinal)&&items.Contains(target[13..]))_supplyWatchItem=target[13..];
        var picker=MarketOptions(form,items,Math.Max(0,Array.IndexOf(items,_supplyWatchItem)));
        var minimum=new SpinBox{MinValue=0,MaxValue=9999,Prefix="下限 ",Value=Game.SupplyMinimum(_supplyWatchItem),CustomMinimumSize=new(124,36)};
        form.AddChild(minimum);
        MarketButton(form,"保存下限",()=>Act("supply_minimum",new JsonObject{{"item",_supplyWatchItem},{"minimum",(int)minimum.Value}}),expand:false);
        var summary=MarketLabel(card,"",wrap:true);summary.Name="SupplyWatchSummary";
        picker.ItemSelected+=selected=>{_supplyWatchItem=items[(int)selected];minimum.Value=Game.SupplyMinimum(_supplyWatchItem);Refresh();};
        if(target.StartsWith("supply-watch:",StringComparison.Ordinal))_managementFocusTarget=card.GetParent<Control>();
        return Refresh;

        void Refresh()
        {
            var status=Game.SupplyStatus(_supplyWatchItem);
            string limit=status.Minimum==0?"提醒关闭":status.Available<status.Minimum?$"低于下限 {status.Minimum} 份":$"下限 {status.Minimum} 份";
            string use=status.Runway is double days?$"日均出库 {status.OutflowPerDay:0.0} 份 · 约可用 {days:0.0} 天":"暂无近期出库记录";
            string arrival=status.IncomingAt is double when?$"预计补货 · {TownProjectClock(when)} · {status.Source}":
                Game.BuyPrice(_supplyWatchItem)>0?"暂无排定补货 · 商店可购买":"暂无排定补货";
            summary.Text=$"可用 {status.Available} 份 · {limit}\n{use}\n{arrival}";
        }
    }

    void OpenSupplyWatch(string item)
    {
        _marketSection=3;
        ShowManagement("商店","supply-watch:"+item);
    }
}
