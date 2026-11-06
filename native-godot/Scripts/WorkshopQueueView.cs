using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;

public partial class Main
{
    int _workshopQueueFilter;

    Action AddWorkshopQueue(Container content)
    {
        var card=MarketCard(content,color:"ebefe1");
        var heading=MarketLabel(card,"加工队列",19,TownTheme.Leaf);
        if(_managementEntryTarget=="workshop-queue")_managementFocusTarget=heading;
        MarketLabel(card,"加工费预付，材料留在仓库直到开工。未开工时可取消并释放材料、退回加工费。",color:TownTheme.Muted,wrap:true);
        var tools=new HFlowContainer();
        tools.AddThemeConstantOverride("h_separation",8);
        card.AddChild(tools);
        var options=MarketOptions(tools,new[]{"全部批次","正在加工","等待开工","需要处理"},_workshopQueueFilter);
        options.Name="WorkshopQueueFilter";
        var counts=MarketLabel(tools,"",14,TownTheme.Muted);
        counts.Name="WorkshopQueueSummary";
        counts.VerticalAlignment=VerticalAlignment.Center;
        var rows=new VBoxContainer();
        card.AddChild(rows);
        var empty=MarketLabel(rows,"队列为空。",color:TownTheme.Muted);
        var cards=new Dictionary<string,(PanelContainer Panel,Action<NativeGame.WorkshopOrder,int> Bind)>();
        options.ItemSelected+=selected=>{_workshopQueueFilter=(int)selected;Refresh();};
        return Refresh;

        void Refresh()
        {
            var orders=Game.WorkshopOrders();
            foreach(string id in cards.Keys.Where(id=>!orders.Any(order=>order.Id==id)).ToArray()){
                var panel=cards[id].Panel;
                rows.RemoveChild(panel);
                panel.QueueFree();
                cards.Remove(id);
            }
            int active=0,waiting=0,blocked=0,visible=0;
            for(int index=0;index<orders.Length;index++){
                var order=orders[index];
                int state=order.Reason.Length>0?3:order.Running?1:2;
                if(state==1)active++;
                else if(state==2)waiting++;
                else blocked++;
                if(!cards.TryGetValue(order.Id,out var row))cards[order.Id]=row=BuildRow(order.Id);
                row.Panel.Visible=_workshopQueueFilter==0||state==_workshopQueueFilter;
                if(!row.Panel.Visible)continue;
                visible++;
                row.Bind(order,index+1);
            }
            empty.Visible=visible==0;
            empty.Text=orders.Length==0?"队列为空，可从下方配方安排加工。":"此状态下没有批次。";
            options.SetItemText(0,$"全部批次 · {orders.Length}");
            options.SetItemText(1,$"正在加工 · {active}");
            options.SetItemText(2,$"等待开工 · {waiting}");
            options.SetItemText(3,$"需要处理 · {blocked}");
            counts.Text=$"{visible} / {orders.Length} 批";
        }

        (PanelContainer,Action<NativeGame.WorkshopOrder,int>) BuildRow(string id)
        {
            var body=MarketCard(rows,color:"f6efe2");
            var panel=body.GetParent<PanelContainer>();
            panel.Name=$"WorkshopQueue_{id}";
            var title=MarketLabel(body,"",17,TownTheme.Ink,wrap:true);
            var materials=MarketLabel(body,"",14,TownTheme.Muted,wrap:true);
            var timing=MarketLabel(body,"",14,TownTheme.Muted,wrap:true);
            var progress=new ProgressBar{Name="WorkshopQueueProgress",ShowPercentage=false,Step=0,CustomMinimumSize=new(0,8)};
            progress.AddThemeStyleboxOverride("background",TownTheme.Box("e6ddc9",4,0));
            progress.AddThemeStyleboxOverride("fill",TownTheme.Box("93aa80",4,0));
            body.AddChild(progress);
            NativeGame.WorkshopOrder current=default;
            var cancel=MarketButton(body,"取消这批",()=>ConfirmManagementAction("取消待加工批次",
                $"{current.Item} ×{current.Yield}\n退回 {current.Fee} 金币，释放仍有效的预留材料。", "确认取消",
                ()=>Act("workshop_queue",new JsonObject{{"operation","cancel"},{"id",id}})),expand:false,accent:"存档");
            cancel.Name="WorkshopQueueCancel";
            cancel.SizeFlagsHorizontal=Control.SizeFlags.ShrinkBegin;
            return (panel,(order,position)=>{
                current=order;
                bool done=order.Running&&order.Finishes<=Game.Now;
                string phase=order.Running?(done?"等待入库":"加工中"):order.Reason.Length>0?"需要处理":"等待开工";
                title.Text=$"第 {position} 批 · {order.Item} ×{order.Yield} · {phase}";
                materials.Text=$"{order.Inputs} · 已付 {order.Fee} 金币";
                timing.Text=order.Reason.Length>0?order.Reason:done?"成品等待入库。":order.Running?
                    $"{DeadlineText(order.Finishes-Game.Now)} · 预计 {TownProjectClock(order.Finishes)} 完成":
                    $"预计 {TownProjectClock(order.Starts)} 开工 · {TownProjectClock(order.Finishes)} 完成";
                timing.AddThemeColorOverride("font_color",order.Reason.Length>0?new Color("8a4938"):TownTheme.Muted);
                progress.Visible=order.Running;
                progress.MaxValue=Math.Max(1,order.Finishes-order.Starts);
                progress.Value=Math.Clamp(Game.Now-order.Starts,0,progress.MaxValue);
                cancel.Visible=!order.Running;
            });
        }
    }

    Action AddWorkshopEnqueue(Container card,string item,(string Item,int Need)[] inputs,int fee,int output)
    {
        var row=new HFlowContainer();
        row.AddThemeConstantOverride("h_separation",6);
        card.AddChild(row);
        var batches=new SpinBox{Name="WorkshopBatchCount",Prefix="批数 ",MinValue=1,MaxValue=12,Value=1,CustomMinimumSize=new(110,0)};
        row.AddChild(batches);
        foreach(int preset in new[]{1,3,5}){
            var quantity=MarketButton(row,$"{preset} 批",()=>batches.Value=preset,expand:false,accent:"商店");
            quantity.Name=$"WorkshopBatchPreset{preset}";
            quantity.TooltipText="调整批数，确认加入队列前只计算报价。";
        }
        var enqueue=MarketButton(row,"加入加工队列",()=>Act("workshop_queue",new JsonObject{{"operation","enqueue"},{"item",item},{"batches",(int)batches.Value}}),expand:false);
        enqueue.Name="WorkshopEnqueue";
        var status=MarketLabel(card,"",14,TownTheme.Muted,wrap:true);
        status.Name="WorkshopEnqueueStatus";
        var materials=MarketCard(card,color:"f4efe3");
        MarketLabel(materials,"排队用料 · 以当前库存估算",14,TownTheme.Muted);
        var needs=inputs.Select(input=>{
            var line=MarketLabel(materials,"",14,TownTheme.Ink,wrap:true);
            line.Name=$"WorkshopMaterial_{input.Item}";
            var progress=new ProgressBar{Name=$"WorkshopMaterialProgress_{input.Item}",ShowPercentage=false,Step=0,CustomMinimumSize=new(0,6)};
            progress.AddThemeStyleboxOverride("background",TownTheme.Box("e4dbc8",3,0));
            progress.AddThemeStyleboxOverride("fill",TownTheme.Box("97ad88",3,0));
            materials.AddChild(progress);
            return (input.Item,input.Need,Label:line,Progress:progress);
        }).ToArray();
        void Refresh()
        {
            int count=(int)batches.Value;
            string reason=Game.WorkshopEnqueueReason(item,count);
            enqueue.Disabled=reason.Length>0;
            enqueue.TooltipText=reason;
            string estimate=$"{count} 批合计 {fee*count} 金币 · 预计产出 {output*count} 份";
            status.Text=reason.Length>0?$"{estimate} · {reason}":$"{estimate} · 可以排队";
            status.AddThemeColorOverride("font_color",reason.Length>0?new Color("8a4938"):TownTheme.Muted);
            foreach(var (ingredient,needed,label,progress) in needs){
                var stock=Game.InventoryAvailability(ingredient);
                int required=needed*count,shortage=Math.Max(0,required-stock.Available);
                label.Text=$"{ingredient} · 需要 {required} · 可用 {stock.Available} · 已预留 {stock.Reserved}"+
                    (shortage>0?$" · 缺 {shortage}":"");
                label.AddThemeColorOverride("font_color",shortage>0?new Color("8a4938"):TownTheme.Ink);
                progress.MaxValue=required;
                progress.Value=Math.Min(required,stock.Available);
            }
        }
        batches.ValueChanged+=_=>Refresh();
        return Refresh;
    }
}
