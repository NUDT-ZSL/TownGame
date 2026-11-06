using Godot;
using System;

public partial class Main
{
    int _farmScenePlot;
    PanelContainer? _farmScenePanel;
    Label? _farmSceneTitle,_farmSceneDetails;
    Button? _farmScenePrevious,_farmSceneNext;
    MeshInstance3D? _farmSceneMarker;

    bool HasFarmScenePlot(int plot)=>Game.ZoneLevel("fields")>0&&plot>0&&plot<=_farmGrowthBeds.Count&&
        GodotObject.IsInstanceValid(_farmGrowthBeds[plot-1].Root)&&!_farmGrowthBeds[plot-1].Root.IsQueuedForDeletion();

    void FocusFarmScenePlot(int plot,bool remember=true)
    {
        UpdateFarmGrowthView();
        if(!HasFarmScenePlot(plot)){
            SetManagementFeedback("该地块暂无可查看的场景位置。",true);
            return;
        }
        var point=_farmGrowthBeds[plot-1].Root.Position;
        if(remember)RememberCameraSite(point);
        StopPetFollow();StopResidentFollow();ClearSceneResidentSelection();EndPlacement();
        _selectedPet="";_portraitAnchor=null;_buildCamera=false;_cameraTransition=0;
        _center=point;_distance=27;_pitch=.97f;
        _activeFarmPlot=_farmScenePlot=plot;
        _selection="";
        _management?.Hide();
        _farmScenePanel??=CreateFarmScenePanel();
        RefreshFarmScenePanel();
        UpdateCamera();
    }

    PanelContainer CreateFarmScenePanel()
    {
        var panel=new PanelContainer{Name="FarmScenePanel",Theme=_panel.Theme,CustomMinimumSize=new(228,0)};
        panel.AddThemeStyleboxOverride("panel",TownTheme.Box("f5f0e2",11,9));
        var content=new VBoxContainer();
        panel.AddChild(content);
        _townHudLayer.AddChild(panel);
        FitTownHudCard(panel);
        _farmSceneTitle=MarketLabel(content,"",16,TownTheme.Leaf,wrap:true);
        _farmSceneTitle.Name="FarmSceneTitle";
        _farmSceneDetails=MarketLabel(content,"",14,TownTheme.Muted,wrap:true);
        var actions=new HFlowContainer();
        actions.AddThemeConstantOverride("h_separation",5);
        content.AddChild(actions);
        _farmScenePrevious=MarketButton(actions,"‹ 上一块",()=>Cycle(-1),expand:false,accent:"环境");
        _farmScenePrevious.Name="FarmScenePrevious";
        _farmSceneNext=MarketButton(actions,"下一块 ›",()=>Cycle(1),expand:false,accent:"环境");
        _farmSceneNext.Name="FarmSceneNext";
        MarketButton(actions,"种植计划",()=>ShowManagement("环境","farm-plots"),expand:false,accent:"地图").Name="FarmSceneOpenPlan";
        MarketButton(actions,"收起",()=>{_farmScenePlot=0;RefreshFarmScenePanel();},expand:false,accent:"存档");
        var border=new DistrictAccentMesh();
        for(int side=-1;side<=1;side+=2){
            border.Box(new(3.56f*side,.11f,0),new(.13f,.1f,6.8f),"f2c277");
            border.Box(new(0,.11f,3.36f*side),new(7.25f,.1f,.13f),"f2c277");
        }
        _farmSceneMarker=new MeshInstance3D{Name="FarmSceneFocusMarker",Mesh=border.Finish(),CastShadow=GeometryInstance3D.ShadowCastingSetting.Off};
        AddChild(_farmSceneMarker);
        return panel;

        void Cycle(int step)
        {
            int count=_farmGrowthBeds.Count;
            if(count>0)FocusFarmScenePlot((_farmScenePlot-1+step+count)%count+1,remember:false);
        }
    }

    void RefreshFarmScenePanel()
    {
        if(_farmScenePanel==null)return;
        bool available=HasFarmScenePlot(_farmScenePlot);
        var position=available?_farmGrowthBeds[_farmScenePlot-1].Root.Position:Vector3.Zero;
        if(!available||position.DistanceTo(_center)>14||_distance>64)_farmScenePlot=0;
        bool visible=_farmScenePlot>0&&!_benchmark&&!InPhotoMode&&_placementAction.Length==0&&
            _management?.Visible!=true&&_townSearchWindow?.Visible!=true;
        _farmScenePanel.Visible=_farmSceneMarker!.Visible=visible;
        if(!visible)return;
        var visual=Game.FarmVisualPlot(_farmScenePlot);
        var plan=Game.FarmPlan(_farmScenePlot);
        var soil=Game.FarmConditions(_farmScenePlot);
        _farmSceneTitle!.Text=$"{_farmScenePlot} 号地 · {FarmGrowthCaption(visual)}";
        _farmSceneDetails!.Text=$"土壤湿度 {soil.Moisture:0}%"+(plan==null?"":plan.Ready?
            $" · {plan.Yield} 份可收\n{DeadlineText(plan.Expires-Game.Now)} 过期":$"\n{DeadlineText(plan.Mature-Game.Now)} 成熟");
        _farmScenePrevious!.Disabled=_farmSceneNext!.Disabled=_farmGrowthBeds.Count<2;
        var bounds=GetViewport().GetVisibleRect();
        float top=_townToolbar?.GetGlobalRect().End.Y??bounds.Position.Y;
        if(_firstDayGuidePanel?.Visible==true)top=Math.Max(top,_firstDayGuidePanel.GetGlobalRect().End.Y);
        _farmScenePanel.Scale=Vector2.One*UiScale;
        _farmScenePanel.Position=new(bounds.Position.X+18,top+10);
        _farmSceneMarker.Position=position;
    }
}
