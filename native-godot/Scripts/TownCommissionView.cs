using Godot;
using System;
using System.Linq;
using System.Text.Json.Nodes;

public partial class Main
{
    bool _commissionReservationsExpanded;
    bool _commissionReceiptsExpanded;
    sealed record CommissionMaterialRow(Label Stock,ProgressBar Progress,Button Buy,Button Recipe,Button Farm);

    void TownCommissionControls()
    {
        var summary=Section("季节小镇委托","本季进度")!;
        var heading=summary.GetParent<Control>().GetParent<Control>();
        heading.Name="TownCommissionHeading";
        var card=MarketCard(_managementBody!,color:"f3eddb");
        if(_managementEntryTarget=="town-commission")_managementFocusTarget=heading;
        var title=MarketLabel(card,"",19,TownTheme.Accent("商店"));
        var rotation=MarketLabel(card,"",color:TownTheme.Muted,wrap:true);
        rotation.Name="TownCommissionRotation";
        var goods=MarketLabel(card,"",color:TownTheme.Ink,wrap:true);
        var checklist=MarketCard(card,color:"f9f4e8");
        MarketLabel(checklist,"交付检查",17,TownTheme.Leaf);
        var preparation=AddCheckpoint("备货","TownCommissionPrepareState");
        var road=AddCheckpoint("供货路网","TownCommissionRoadState");
        var freight=AddCheckpoint("居民货运","TownCommissionFreightState");
        var delivery=AddCheckpoint("交付时段","TownCommissionDeliveryState");
        var materials=new VBoxContainer{Name="TownCommissionMaterials"};
        card.AddChild(materials);
        var reward=MarketLabel(card,"",color:TownTheme.Leaf,wrap:true);
        var row=new HFlowContainer();
        row.AddThemeConstantOverride("h_separation",8);
        row.AddThemeConstantOverride("v_separation",5);
        card.AddChild(row);
        int displayedSeason=Game.TownCommissionSeason;
        int displayedStage=0;
        var action=MarketButton(row,"确认备货",()=>{
            Act("town_commission",new JsonObject{{"step",displayedStage==0?"prepare":"deliver"},{"season",displayedSeason}});
        },expand:false,accent:"成长");
        action.Name="TownCommissionAction";
        var warehouse=MarketButton(row,"查看仓库",()=>{_marketSection=3;ShowManagement("商店");},expand:false);
        var map=MarketButton(row,"查看运输路网",()=>ShowManagement("地图"),expand:false,accent:"地图");
        var autonomy=MarketButton(row,"查看自治货运",()=>ShowManagement("自治"),expand:false,accent:"自治");
        var reservationCard=new VBoxContainer();
        var toggle=MarketButton(row,"预留物资",()=>{
            _commissionReservationsExpanded=!_commissionReservationsExpanded;
            reservationCard.Visible=_commissionReservationsExpanded;
        },expand:false,accent:"成长");
        toggle.Name="TownCommissionReservationToggle";
        var expiring=MarketLabel(card,"",color:new Color("8a4938"),wrap:true);
        expiring.Name="TownCommissionReservationWarning";
        card.AddChild(reservationCard);
        var reservations=new VBoxContainer();reservationCard.AddChild(reservations);
        var materialRows=Array.Empty<CommissionMaterialRow>();
        var reservationRows=Array.Empty<(Label Label,Action<NativeGame.StockAvailability,int,bool> Refresh)>();
        string shownPlan="";
        var releaseAll=MarketButton(reservationCard,"释放本季全部预留",()=>
            ChangeStockReservation(NativeGame.CommissionReservationOwner(displayedSeason),"",0),expand:false,accent:"成长");
        releaseAll.Name="TownCommissionReservationReleaseAll";
        var refreshReceipts=AddCommissionReceipts(card);

        Label AddCheckpoint(string caption,string id)
        {
            var line=new HFlowContainer();line.AddThemeConstantOverride("h_separation",8);checklist.AddChild(line);
            MarketLabel(line,caption,15,TownTheme.Ink).CustomMinimumSize=new(72,0);
            var label=MarketLabel(line,"",14,TownTheme.Muted,wrap:true);
            label.Name=id;
            label.CustomMinimumSize=new(240,0);
            label.SizeFlagsHorizontal=Control.SizeFlags.ExpandFill;
            return label;
        }

        static void Status(Label label,string text,bool done)
        {
            label.Text=text;
            label.AddThemeColorOverride("font_color",done?TownTheme.Leaf:TownTheme.Muted);
        }

        void BuildMaterials(NativeGame.CommissionPlan plan)
        {
            shownPlan=plan.Key;
            foreach(var container in new[]{materials,reservations})
                foreach(var child in container.GetChildren()){container.RemoveChild(child);child.QueueFree();}
            materialRows=plan.Supplies.Select((supply,index)=>{
                string item=supply.Item;
                var entry=MarketCard(materials,color:"fbf7ed");
                MarketLabel(entry,item,16,TownTheme.Ink);
                var stock=MarketLabel(entry,"",14,TownTheme.Muted,wrap:true);
                stock.Name=$"TownCommissionMaterialStock{index}";
                var progress=new ProgressBar{Name=$"TownCommissionMaterialProgress{index}",MaxValue=supply.Count,ShowPercentage=false,
                    CustomMinimumSize=new(0,8),MouseFilter=Control.MouseFilterEnum.Ignore};
                progress.AddThemeStyleboxOverride("background",TownTheme.Box("eee7d8",4,0));
                progress.AddThemeStyleboxOverride("fill",TownTheme.Box(TownTheme.Accent("成长").ToHtml(),4,0));
                entry.AddChild(progress);
                var links=new HFlowContainer();links.AddThemeConstantOverride("h_separation",6);entry.AddChild(links);
                var buy=MarketButton(links,"去采购",()=>OpenCommissionMaterial(item,false),expand:false,accent:"商店");
                buy.Name=$"TownCommissionMaterialBuy{index}";
                var recipe=MarketButton(links,"查看加工配方",()=>OpenCommissionMaterial(item,true),expand:false,accent:"商店");
                recipe.Name=$"TownCommissionMaterialRecipe{index}";
                var farm=MarketButton(links,"查看农田",()=>ShowManagement("环境","farm-plots"),expand:false,accent:"环境");
                farm.Name=$"TownCommissionMaterialFarm{index}";
                return new CommissionMaterialRow(stock,progress,buy,recipe,farm);
            }).ToArray();
            reservationRows=plan.Supplies.Select((supply,index)=>{
                string item=supply.Item;
                var label=MarketLabel(reservations,"",color:TownTheme.Muted,wrap:true);
                var refresh=AddStockReservationControls(reservations,()=>NativeGame.CommissionReservationOwner(displayedSeason),
                    ()=>item,$"TownCommissionReservation{index}","成长");
                return (label,refresh);
            }).ToArray();
        }

        void Refresh()
        {
            var progress=Game.TownCommission();
            displayedSeason=Game.TownCommissionSeason;
            displayedStage=progress.Stage;
            var displayedSupplies=progress.Plan.Supplies;
            if(shownPlan!=progress.Plan.Key)BuildMaterials(progress.Plan);
            string owner=NativeGame.CommissionReservationOwner(displayedSeason);
            var stocks=displayedSupplies.Select(supply=>Game.InventoryAvailability(supply.Item,owner)).ToArray();
            bool canReserve=displayedStage is 1 or 2||(displayedStage==0&&progress.CanPrepare);
            summary.Text=displayedStage==3?"本季已完成":$"{Game.Season}季 · 截止 {TownProjectClock(progress.Deadline)}";
            title.Text=progress.Plan.Name;
            rotation.Text=Game.CommissionRotationHint(progress.Plan);
            goods.Text=displayedStage==3?"物资已交付。下个季节将开放新的委托。":
                $"{DeadlineText(progress.Deadline-Game.Now)} · 最终交付时扣除仓库物资";
            materials.Visible=displayedStage!=3;
            int reserved=stocks.Sum(stock=>stock.ReservedForOwner);
            toggle.Visible=canReserve;
            toggle.Text=reserved>0?$"管理预留 · {reserved} 份":"预留物资";
            reservationCard.Visible=canReserve&&_commissionReservationsExpanded;
            releaseAll.Visible=reserved>0;
            int expiringCount=stocks.Sum(stock=>stock.ExpiringForOwner);
            expiring.Visible=canReserve&&expiringCount>0;
            if(expiring.Visible)expiring.Text=$"{expiringCount} 份预留物资临期，可补货或释放。";
            for(int i=0;i<reservationRows.Length;i++){
                reservationRows[i].Label.Text=$"{displayedSupplies[i].Item} · {ReservationStockText(stocks[i])}";
                reservationRows[i].Refresh(stocks[i],displayedSupplies[i].Count,canReserve);
                var material=materialRows[i];
                var (item,count)=displayedSupplies[i];
                int missing=Math.Max(0,count-stocks[i].ForOwner);
                material.Stock.Text=$"需 {count} 份 · {ReservationStockText(stocks[i])} · {(missing>0?$"还差 {missing} 份":"数量齐备")}";
                material.Progress.Value=Math.Min(stocks[i].ForOwner,count);
                int price=Game.BuyPrice(item);
                material.Buy.Visible=missing>0&&price>0;
                material.Buy.Text=$"采购缺少的 {missing} 份 · {(long)missing*price} 金币";
                material.Recipe.Visible=missing>0&&Game.Rules["RECIPES"]?[item]!=null;
                material.Farm.Visible=missing>0&&NativeGame.FarmCrops.Contains(item);
            }
            bool complete=displayedStage==3;
            Status(preparation,displayedStage>=1?"已确认备货":!progress.CanPrepare?"本季备货窗口已结束":
                progress.SuppliesReady?"库存齐备 · 可以确认备货":"库存不足 · 见下方物资清单",displayedStage>=1);
            Status(road,complete?"交付时已连通":progress.RoadReady?"生产区与商业区已接入路网":"生产区或商业区未接入路网",complete||progress.RoadReady);
            Status(freight,complete||progress.FreightArrived?"已记录居民送货":displayedStage==0?"确认备货后开始记录送达":
                "等待居民货车实际送达",complete||progress.FreightArrived);
            bool windowOpen=displayedStage>=1&&Game.Now>=progress.Opens;
            Status(delivery,complete?"已交付":displayedStage==2?progress.SuppliesReady?"已开放 · 可提交清单物资":"已开放 · 部分物资已消耗或过期":
                windowOpen?"已开放 · 等待路网或货运条件":displayedStage==0&&!progress.CanPrepare?"本季已无可用交付时段":
                $"{CommissionOpeningClock(progress.Opens)} 开放 · {DeadlineText(progress.Opens-Game.Now)}",complete||windowOpen);
            reward.Text=$"{(displayedStage==3?"已获得":"交付奖励")} {progress.Plan.Coins} 金币 · {progress.Plan.Reputation} 声望";
            warehouse.Visible=displayedStage!=3;
            map.Visible=displayedStage==1||displayedStage==0&&!progress.RoadReady;
            autonomy.Visible=displayedStage==1;
            action.Visible=displayedStage is 0 or 2;
            action.Disabled=!progress.SuppliesReady||(displayedStage==0&&!progress.CanPrepare);
            action.Text=displayedStage==0?"确认备货":"交付本季委托";
            action.TooltipText=action.Disabled?(displayedStage==0&&!progress.CanPrepare?"本季备货窗口已结束":"先补齐物资"):"";
            refreshReceipts();
        }
        Refresh();
        _refreshManagementPage+=Refresh;
    }

    string CommissionOpeningClock(double opens)
    {
        double days=Math.Floor(opens/NativeGame.Day)-Math.Floor(Game.Now/NativeGame.Day);
        int hour=(int)(opens%NativeGame.Day/NativeGame.Hour),minute=(int)(opens%NativeGame.Hour/60);
        return days switch{0=>$"今天 {hour:00}:{minute:00}",1=>$"明天 {hour:00}:{minute:00}",_=>TownProjectClock(opens)};
    }

    void OpenCommissionMaterial(string item,bool recipe)
    {
        var progress=Game.TownCommission();
        int count=progress.Plan.Supplies.FirstOrDefault(supply=>supply.Item==item).Count;
        string owner=NativeGame.CommissionReservationOwner(Game.TownCommissionSeason);
        int missing=Math.Max(0,count-Game.Stock(item,owner));
        if(progress.Stage==3||missing==0){
            SetManagementFeedback("当前委托已备齐这项物资。",completed:true);
            return;
        }
        if(!recipe){
            _marketBuyQuantity=Math.Clamp(missing,1,99);
            OpenSearchSupply(item);
            return;
        }
        _orderSupplyRoute=null;
        _careSupplyRoute=null;
        _marketSection=2;
        ShowManagement("商店",$"recipe:{item}");
    }

    Action AddCommissionReceipts(Container card)
    {
        var heading=new HFlowContainer();card.AddChild(heading);
        var toggle=new Button{Name="TownCommissionReceiptToggle",ToggleMode=true,ButtonPressed=_commissionReceiptsExpanded};
        TownTheme.AccentButton(toggle,"成长",6);heading.AddChild(toggle);
        var list=new VBoxContainer{Name="TownCommissionReceipts",Visible=_commissionReceiptsExpanded};
        card.AddChild(list);
        (int Season,double Delivered)[]? rendered=null;
        toggle.Toggled+=value=>{_commissionReceiptsExpanded=value;list.Visible=value;Refresh();};
        return Refresh;

        void Refresh()
        {
            var receipts=Game.CommissionReceipts();
            toggle.Text=$"交付记录 · {receipts.Length} 季";
            if(!list.Visible)return;
            var keys=receipts.Select(receipt=>(receipt.Season,receipt.Delivered)).ToArray();
            if(rendered!=null&&rendered.SequenceEqual(keys))return;
            rendered=keys;
            foreach(var child in list.GetChildren()){list.RemoveChild(child);child.QueueFree();}
            if(receipts.Length==0){MarketLabel(list,"暂无带明细的交付记录。",14,TownTheme.Muted,wrap:true);return;}
            foreach(var receipt in receipts){
                var entry=MarketCard(list,color:"f9f4e8");
                MarketLabel(entry,$"第 {receipt.Season/4+1} 年 · {receipt.Name}",16,TownTheme.Leaf,wrap:true);
                MarketLabel(entry,$"{TownProjectClock(receipt.Delivered)} 交付 · 获得 {receipt.Coins} 金币和 {receipt.Reputation} 声望",
                    14,TownTheme.Muted,wrap:true);
                MarketLabel(entry,"物资 · "+string.Join("、",receipt.Supplies.Select(supply=>$"{supply.Item} {supply.Count} 份")),
                    14,TownTheme.Ink,wrap:true);
            }
        }
    }
}
