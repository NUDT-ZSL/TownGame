using Godot;
using System;
using System.Linq;
using System.Text.Json.Nodes;

public partial class Main
{
    int _activeFarmPlot=1;
    int _plannedFarmCrop;

    void FarmPlotControls()
    {
        var card=MarketCard(_managementBody!,color:"edf2e3");
        if(_managementEntryTarget=="farm-plots")_managementFocusTarget=card.GetParent<Control>();
        MarketLabel(card,"农田种植计划",18,TownTheme.Accent("环境"));
        var access=MarketLabel(card,"",color:TownTheme.Muted,wrap:true);
        var plots=new HFlowContainer();
        plots.AddThemeConstantOverride("h_separation",6);
        plots.AddThemeConstantOverride("v_separation",6);
        card.AddChild(plots);
        var plotButtons=new Button[NativeGame.FarmPlotCount];
        var plotDetails=new Label[plotButtons.Length];
        var plotMoisture=new ProgressBar[plotButtons.Length];
        for(int index=0;index<plotButtons.Length;index++){
            var plot=MarketCard(plots,154,"f8f6ec");
            var button=new Button{Name=$"FarmPlotSelect{index+1}",ToggleMode=true};
            TownTheme.AccentButton(button,"环境",6);
            plot.AddChild(button);
            plotButtons[index]=button;
            plotDetails[index]=MarketLabel(plot,"",13,TownTheme.Muted,wrap:true);
            plotDetails[index].Name=$"FarmPlotStatus{index+1}";
            plotMoisture[index]=FarmProgressBar(plot,"95ad81");
        }
        var plotHeading=MarketLabel(card,"",17,TownTheme.Leaf);
        plotHeading.Name="FarmPlotHeading";
        var form=new HFlowContainer();
        form.AddThemeConstantOverride("h_separation",6);
        form.AddThemeConstantOverride("v_separation",6);
        card.AddChild(form);
        var crop=MarketOptions(form,NativeGame.FarmCrops,_plannedFarmCrop);
        crop.Name="FarmPlotCrop";
        var route=MarketOptions(form,new[]{"收获后普通入库"},0);
        route.Name="FarmPlotRoute";
        route.FitToLongestItem=false;
        route.CustomMinimumSize=new(245,36);
        var summary=MarketLabel(card,"",color:TownTheme.Muted,wrap:true);
        summary.Name="FarmPlotEstimate";
        var growth=FarmProgressBar(card,"87a776");
        growth.Name="FarmPlotGrowth";
        var timing=MarketLabel(card,"",14,TownTheme.Leaf,wrap:true);
        timing.Name="FarmPlotCountdown";
        var output=MarketLabel(card,"",14,TownTheme.Muted,wrap:true);
        output.Name="FarmPlotHarvestProjection";
        var soil=MarketLabel(card,"",color:TownTheme.Muted,wrap:true);
        soil.Name="FarmPlotSoil";
        var actions=new HFlowContainer();
        actions.AddThemeConstantOverride("h_separation",6);
        actions.AddThemeConstantOverride("v_separation",6);
        card.AddChild(actions);
        var sow=new Button{Text="播种"};
        var harvest=new Button{Name="FarmPlotHarvest",Text="收获所选地块"};
        var changeRoute=new Button{Text="更新收获去向"};
        var fertilize=new Button{Text="施肥"};
        var water=new Button{Text="浇水 · 1 份清水"};
        var resume=new Button{Text="恢复自动混种"};
        var locate=new Button{Name="FarmPlotLocate",Text="查看地块现场"};
        foreach(var button in new[]{sow,harvest,changeRoute,fertilize,water,resume,locate}){
            TownTheme.AccentButton(button,"环境",6);
            actions.AddChild(button);
        }
        var succession=new HFlowContainer();succession.AddThemeConstantOverride("h_separation",6);card.AddChild(succession);
        var nextCrop=MarketOptions(succession,NativeGame.FarmCrops,0);
        nextCrop.Name="FarmPlotNextCrop";
        var planNext=new Button{Text="安排下一茬"};
        var clearNext=new Button{Text="取消下一茬"};
        foreach(var button in new[]{planNext,clearNext}){TownTheme.AccentButton(button,"环境",6);succession.AddChild(button);}
        var compare=new Button{Name="FarmPlotCompare",Text="比较可选作物",ToggleMode=true,SizeFlagsHorizontal=Control.SizeFlags.ShrinkBegin};
        TownTheme.AccentButton(compare,"环境",6);card.AddChild(compare);
        var comparison=MarketCard(card,color:"f3f0dd");
        var comparisonFrame=comparison.GetParent<Control>();
        comparisonFrame.Hide();
        var comparisonHeading=MarketLabel(comparison,"",14,TownTheme.Muted,wrap:true);
        var cropChoices=new HFlowContainer();
        cropChoices.AddThemeConstantOverride("h_separation",6);comparison.AddChild(cropChoices);
        var alternatives=new Button[NativeGame.FarmCrops.Length];
        for(int i=0;i<alternatives.Length;i++){
            var alternative=new Button{Name=$"FarmCropChoice{i}",ToggleMode=true,CustomMinimumSize=new(146,0)};
            TownTheme.AccentButton(alternative,"商店",6);cropChoices.AddChild(alternative);
            alternatives[i]=alternative;
        }
        NativeGame.FarmRoute[] currentRoutes=Array.Empty<NativeGame.FarmRoute>();
        NativeGame.FarmPlot? lastPlan=null;
        int displayedPlot=0;
        string displayedNext="";
        string Destination()=>route.Selected>0&&route.Selected<=currentRoutes.Length?currentRoutes[route.Selected-1].Id:"";
        void Apply(string operation)=>Act("farm_plot",new JsonObject
            {{"operation",operation},{"plot",_activeFarmPlot},{"crop",NativeGame.FarmCrops[operation=="plan"?nextCrop.Selected:crop.Selected]},{"order_id",Destination()}});
        void UpdateRouteButton(NativeGame.FarmPlot? plan,string road)
        {
            changeRoute.Visible=plan!=null;
            changeRoute.Disabled=plan==null||road!=""||Destination()==plan.OrderId;
        }
        void RefreshHarvestProjection(NativeGame.FarmPlot? plan,string cropName)
        {
            output.Visible=Game.FarmRoadReason.Length==0||plan!=null;
            int yield=plan?.Yield??Game.ExpectedFarmYield(_activeFarmPlot,cropName);
            int available=Game.FarmWarehouseSpace;
            string configured=plan?.OrderId??Destination();
            string text=plan==null?"播种安排 · ":"当前收获安排 · ";
            output.Text=text+Split(configured)+$"\n当前仓库余量 {available} 份"+
                (available<yield?$" · 收获前至少还需腾出 {yield-available} 份":"");
            if(plan!=null&&configured!=Destination())output.Text+="\n待保存的去向 · "+Split(Destination());

            string Split(string orderId)
            {
                if(orderId.Length==0)return $"普通入库 {yield} 份";
                var order=currentRoutes.FirstOrDefault(item=>item.Id==orderId);
                if(order==null)return $"订单在可收获时已失效 · 普通入库 {yield} 份";
                int reserved=Game.FarmHarvestReservation(cropName,orderId,yield);
                return reserved==0?$"{order.Name}的订单已预留足额 · 普通入库 {yield} 份":
                    $"预计为{order.Name}预留 {reserved} 份 · 普通入库 {yield-reserved} 份";
            }
        }
        void Refresh(bool resetDestination=false)
        {
            string road=Game.FarmRoadReason;
            var plans=Enumerable.Range(1,NativeGame.FarmPlotCount).Select(Game.FarmPlan).ToArray();
            var plan=plans[_activeFarmPlot-1];
            var conditions=Game.FarmConditions(_activeFarmPlot);
            bool occupied=plan!=null;
            string species=plan?.Crop??NativeGame.FarmCrops[crop.Selected];
            double maturity=plan?.Mature??Game.FarmMaturity;
            access.Text=road!=""?road:Game.FarmPlotsEnabled
                ?"分地种植 · 选择地块、作物与收获去向。"
                :"农田目前自动混种。播种第一块地会启用分地计划，已成熟的混种作物可照常收获。";
            for(int i=0;i<plans.Length;i++){
                var field=plans[i];
                var fieldSoil=Game.FarmConditions(i+1);
                var visual=Game.FarmVisualPlot(i+1);
                plotButtons[i].Text=$"{i+1} 号地 · {FarmGrowthCaption(visual)}";
                plotButtons[i].SetPressedNoSignal(i+1==_activeFarmPlot);
                plotDetails[i].Text=$"湿度 {fieldSoil.Moisture:0}%"+(field!=null?$" · {field.Yield} 份":"")+
                    (fieldSoil.NextCrop.Length>0?$"\n下一茬 {fieldSoil.NextCrop}":"");
                plotMoisture[i].Value=fieldSoil.Moisture;
            }
            plotHeading.Text=$"{_activeFarmPlot} 号地 · {(plan!=null?plan.Crop:Game.FarmPlotsEnabled?"待播种":"分地计划")}";
            crop.Visible=!occupied;
            crop.Disabled=road!="";
            route.Disabled=road!="";
            var freshRoutes=Game.FarmRoutes(species,Math.Max(maturity,Game.Now));
            bool changed=lastPlan?.Crop!=plan?.Crop||lastPlan?.Mature!=plan?.Mature||lastPlan?.OrderId!=plan?.OrderId;
            if(resetDestination||changed||!currentRoutes.SequenceEqual(freshRoutes)){
                string chosen=resetDestination||changed?plan?.OrderId??"":Destination();
                currentRoutes=freshRoutes;
                route.Clear();
                route.AddItem("收获后普通入库");
                foreach(var item in currentRoutes)route.AddItem($"{item.Name}的订单 · {TownProjectClock(item.Expires)} 到期");
                int selected=Array.FindIndex(currentRoutes,item=>item.Id==chosen);
                route.Select(selected+1);
            }
            lastPlan=plan;
            if(occupied){
                summary.Text=(plan!.Ready?$"已成熟 · 产量 {plan.Yield} 份":$"预计 {TownProjectClock(maturity)} 成熟 · 预计 {plan.Yield} 份")
                    +$" · {TownProjectClock(plan.Expires)} 前可收获";
            }else summary.Text=road!=""?"接通农田后可选择作物。"
                :$"{species} · 预计 {TownProjectClock(maturity)} 成熟 · 预计 {Game.ExpectedFarmYield(_activeFarmPlot,species)} 份";
            growth.Visible=timing.Visible=plan!=null;
            if(plan!=null){
                growth.Value=plan.Ready?100:Math.Clamp(1-(plan.Mature-Game.Now)/Math.Max(1,Game.FarmGrowthDuration),0,1)*100;
                timing.Text=plan.Ready?$"采收窗口 · {DeadlineText(plan.Expires-Game.Now)}":$"生长 {growth.Value:0}% · {DeadlineText(plan.Mature-Game.Now)} 成熟";
            }
            RefreshHarvestProjection(plan,species);
            string moisture=conditions.Moisture<40?"偏干 · 会影响产量":conditions.Moisture>=85?"湿润":"适宜";
            string rotation=FarmRotationCaption(conditions,species);
            string planned=conditions.NextCrop.Length>0?$" · 下一茬 {conditions.NextCrop}":"";
            soil.Text=$"湿度 {conditions.Moisture:0}% · {moisture} · {rotation}{planned}";
            sow.Visible=!occupied;
            sow.Disabled=road!="";
            harvest.Visible=occupied;
            harvest.Disabled=road!=""||plan?.Ready!=true||Game.FarmWarehouseSpace<plan.Yield;
            fertilize.Visible=plan is {Ready:false};
            fertilize.Text=plan?.Fertilized==true?"已施肥":$"施肥 · 有 {Game.FarmCompost} 份";
            fertilize.Disabled=road!=""||plan is not {Ready:false,Fertilized:false}||Game.FarmCompost<=0;
            water.Visible=plan is {Ready:false};
            water.Disabled=road!=""||conditions.Moisture>=85||Game.Stock("清水")<1;
            water.TooltipText=conditions.Moisture>=85?"土壤已湿润":Game.Stock("清水")<1?"可用清水不足":"增加地块湿度，雨天会自然补水";
            succession.Visible=occupied||conditions.NextCrop.Length>0;
            nextCrop.Visible=planNext.Visible=occupied;
            planNext.Disabled=road!="";
            clearNext.Visible=conditions.NextCrop.Length>0;
            if(displayedPlot!=_activeFarmPlot||displayedNext!=conditions.NextCrop){
                displayedPlot=_activeFarmPlot;displayedNext=conditions.NextCrop;
                int index=Array.IndexOf(NativeGame.FarmCrops,conditions.NextCrop);
                if(index<0&&plan!=null)index=Array.FindIndex(NativeGame.FarmCrops,kind=>kind!=plan.Crop);
                nextCrop.Select(Math.Max(0,index));
            }
            UpdateRouteButton(plan,road);
            resume.Visible=Game.FarmPlotsEnabled;
            resume.Disabled=plans.Any(field=>field!=null);
            resume.TooltipText=resume.Disabled?"现有作物收完后可恢复自动混种":"恢复自动生成谷物和鲜草批次";
            locate.Disabled=!HasFarmScenePlot(_activeFarmPlot);
            locate.TooltipText=locate.Disabled?"该地块暂无可查看的场景位置":"在小镇中聚焦这块农田";
            if(comparisonFrame.Visible){
                var forecastSoil=plan==null?conditions:Game.FarmConditionsAfterHarvest(_activeFarmPlot,plan.Crop);
                comparisonHeading.Text=$"{Game.Season}季 · {Game.Weather} · 土壤湿度 {forecastSoil.Moisture:0}%\n"+
                    (plan==null?"按当前条件比较预计产量。点击作物可填入播种选项。":"按收获后的轮作情况估算下一茬。点击作物后还可修改或确认安排。");
                for(int i=0;i<alternatives.Length;i++){
                    var candidate=NativeGame.FarmCrops[i];
                    alternatives[i].Text=$"{candidate} · 预计 {Game.ExpectedFarmYield(forecastSoil,candidate)} 份\n{FarmRotationCaption(forecastSoil,candidate)}";
                    alternatives[i].SetPressedNoSignal(i==(plan==null?crop.Selected:nextCrop.Selected));
                    alternatives[i].Disabled=road!="";
                }
            }
        }
        crop.ItemSelected+=choice=>{_plannedFarmCrop=(int)choice;Refresh(resetDestination:true);};
        for(int index=0;index<plotButtons.Length;index++){
            int id=index+1;
            plotButtons[index].Pressed+=()=>{_activeFarmPlot=id;Refresh(resetDestination:true);};
        }
        route.ItemSelected+=_=>{UpdateRouteButton(lastPlan,Game.FarmRoadReason);RefreshHarvestProjection(lastPlan,lastPlan?.Crop??NativeGame.FarmCrops[crop.Selected]);};
        nextCrop.ItemSelected+=_=>Refresh();
        compare.Toggled+=open=>{comparisonFrame.Visible=open;Refresh();};
        for(int i=0;i<alternatives.Length;i++){
            int choice=i;
            alternatives[i].Pressed+=()=>{
                if(lastPlan==null){crop.Select(choice);_plannedFarmCrop=choice;Refresh(resetDestination:true);}
                else{nextCrop.Select(choice);Refresh();}
            };
        }
        sow.Pressed+=()=>Apply("sow");
        harvest.Pressed+=()=>Apply("harvest");
        changeRoute.Pressed+=()=>Apply("route");
        fertilize.Pressed+=()=>Apply("fertilize");
        water.Pressed+=()=>Apply("irrigate");
        planNext.Pressed+=()=>Apply("plan");
        clearNext.Pressed+=()=>Apply("clear_plan");
        resume.Pressed+=()=>Apply("resume");
        locate.Pressed+=()=>FocusFarmScenePlot(_activeFarmPlot);
        void RefreshPage()=>Refresh();
        RefreshPage();
        _refreshManagementPage+=RefreshPage;
        card.TreeExiting+=()=>_refreshManagementPage-=RefreshPage;
    }

    static string FarmRotationCaption(NativeGame.FarmSoil soil,string crop)=>soil.LastCrop.Length==0?"首次种植":
        soil.LastCrop!=crop?"轮作增产 1 份":soil.Repeats>=2?"连续种植 · 产量减少":"同茬种植";

    static ProgressBar FarmProgressBar(Control parent,string fill)
    {
        var bar=new ProgressBar{ShowPercentage=false,MaxValue=100,Step=0,CustomMinimumSize=new(0,6)};
        bar.AddThemeStyleboxOverride("background",TownTheme.Box("e4e1d0",4,0));
        bar.AddThemeStyleboxOverride("fill",TownTheme.Box(fill,4,0));
        parent.AddChild(bar);
        return bar;
    }
}
