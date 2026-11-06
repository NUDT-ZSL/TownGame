using Godot;
using System;
using System.Linq;
using System.Text.Json.Nodes;

public partial class Main
{
    readonly record struct SceneInspection(string Kind,string Id);
    SceneInspection? _sceneInspection,_pinnedSceneInspection;
    PanelContainer? _sceneInspector;
    Label? _sceneInspectorTitle,_sceneInspectorDetails;
    Button? _sceneInspectorPin,_sceneInspectorOpen,_sceneInspectorLocate;
    double _sceneInspectorRefresh;

    void CreateSceneInspector(CanvasLayer layer,Theme theme)
    {
        var panel=new PanelContainer{Name="TownSceneInspector",Visible=false,Theme=theme,CustomMinimumSize=new(245,0)};
        panel.AddThemeStyleboxOverride("panel",TownTheme.Box("f4f1e8f8",12,11));
        var body=new VBoxContainer();
        body.AddThemeConstantOverride("separation",6);
        panel.AddChild(body);
        _sceneInspectorTitle=MarketLabel(body,"场景资料",16,TownTheme.Leaf,wrap:true);
        _sceneInspectorTitle.CustomMinimumSize=new(215,0);
        _sceneInspectorDetails=MarketLabel(body,"",13,TownTheme.Muted,wrap:true);
        var buttons=new HFlowContainer();
        body.AddChild(buttons);
        _sceneInspectorPin=MarketButton(buttons,"固定",()=>{
            _pinnedSceneInspection=_pinnedSceneInspection.HasValue?null:_sceneInspection;
            _sceneInspectorRefresh=0;
        },expand:false,accent:"地图");
        _sceneInspectorPin.Name="TownSceneInspectorPin";
        _sceneInspectorOpen=MarketButton(buttons,"查看详情",OpenSceneInspection,expand:false,accent:"家庭");
        _sceneInspectorLocate=MarketButton(buttons,"定位",LocateSceneInspection,expand:false,accent:"地图");
        _sceneInspectorLocate.Name="TownSceneInspectorLocate";
        MarketButton(buttons,"收起",()=>{
            _sceneInspection=_pinnedSceneInspection=null;
            panel.Hide();
        },expand:false,accent:"存档");
        layer.AddChild(panel);
        FitTownHudCard(panel);
        _sceneInspector=panel;
    }

    void TrackSceneInspectorSelection(ScenePick pick)
    {
        if(IsActorScenePick(pick)&&pick.Mesh!=_scenePickedMesh)SetScenePickedOutline(pick.Mesh);
        else if(!IsActorScenePick(pick)&&_scenePickedMesh!=null)SetScenePickedOutline(null);
        if((pick.Kind is "pet" or "town_building")||pick.Kind=="person"&&DirectoryPerson(pick.Id)!=null)FocusSceneInspection(pick.Kind,pick.Id);
        else _sceneInspection=null;
    }

    void FocusSceneInspection(string kind,string id,bool pin=false)
    {
        _sceneInspection=new(kind,id);
        if(pin)_pinnedSceneInspection=_sceneInspection;
        _sceneInspectorRefresh=0;
    }

    void UpdateSceneInspector(double delta)
    {
        if(_sceneInspector==null)return;
        var target=_pinnedSceneInspection??_sceneInspection;
        if(_benchmark||InPhotoMode||_placementAction.Length>0||_management?.Visible==true||_townSearchWindow?.Visible==true||
            _scenePickChooser?.Visible==true||target==null){
            _sceneInspector.Hide();
            return;
        }
        _sceneInspectorRefresh-=delta;
        if(_sceneInspectorRefresh<=0||!_sceneInspector.Visible){
            _sceneInspectorRefresh=.3;
            var info=SceneInspectionInfo(target.Value);
            _sceneInspectorTitle!.Text=(_pinnedSceneInspection.HasValue?"◆ ":"")+info.Title;
            _sceneInspectorDetails!.Text=info.Detail;
            _sceneInspectorOpen!.Disabled=!info.Present;
            _sceneInspectorLocate!.Disabled=!CanLocateSceneInspection(target.Value);
            _sceneInspectorLocate.TooltipText=_sceneInspectorLocate.Disabled?"该对象目前没有可定位的场景位置":$"将镜头移到「{info.Title}」";
            _sceneInspectorPin!.Text=_pinnedSceneInspection.HasValue?"取消固定":"固定";
            _sceneInspector.ResetSize();
        }
        _sceneInspector.Show();
        var area=GetViewport().GetVisibleRect();
        float top=_townToolbar?.GetGlobalRect().End.Y??area.Position.Y;
        _sceneInspector.Scale=Vector2.One*UiScale;
        _sceneInspector.Position=new(area.End.X-_sceneInspector.Size.X*UiScale-18,Math.Max(area.Position.Y+18,top+12));
    }

    (string Title,string Detail,bool Present) SceneInspectionInfo(SceneInspection target)
    {
        if(target.Kind=="pet"&&Data.FindPet(target.Id) is JsonNode pet)
            return ($"{NativeGame.S(pet,"name")} · {NativeGame.S(pet,"species")}",
                $"{PetLocationStatus(pet)} · {Game.Stage(pet)}\n健康 {NativeGame.D(pet,"health"):0} · 饱食 {NativeGame.D(pet,"hunger"):0}",true);
        if(target.Kind=="person"&&DirectoryPerson(target.Id) is JsonNode person){
            var entry=DescribeDirectoryPerson(person,DirectoryActor(target.Id));
            return (entry.Name,$"{entry.Profession} · {entry.District}\n{entry.Activity}",true);
        }
        if(target.Kind=="town_building"&&Game.FindTownBuilding(target.Id) is JsonNode project){
            bool building=IsTownProjectUnderway(project);
            string status=building?ConstructionTimeLeft(NativeGame.D(project,"finish_ts")-Game.Now):"已完工";
            return(TownProjectName(project),$"{TownProjectCell(project)} · {status}\n{Game.ConstructionRoadStatus(project)}",true);
        }
        return("目标已离开","这处对象暂时无法查看。",false);
    }

    void OpenSceneInspection()
    {
        if((_pinnedSceneInspection??_sceneInspection) is not SceneInspection target)return;
        switch(target.Kind){
            case "pet":OpenPetManagement(target.Id);break;
            case "person":OpenResidentDirectoryEntry(target.Id);break;
            case "town_building":OpenTownProject(target.Id);break;
        }
    }

    bool CanLocateSceneInspection(SceneInspection target)=>target.Kind switch{
        "pet"=>Data.FindPet(target.Id)!=null&&FarmPet(target.Id)!=null,
        "person"=>DirectoryPerson(target.Id) is JsonNode person&&DescribeDirectoryPerson(person,DirectoryActor(target.Id)).Position.HasValue,
        "town_building"=>Game.FindTownBuilding(target.Id)!=null,
        _=>false
    };

    void LocateSceneInspection()
    {
        if((_pinnedSceneInspection??_sceneInspection) is not SceneInspection target||!CanLocateSceneInspection(target))return;
        switch(target.Kind){
            case "pet":LocatePet(target.Id);break;
            case "person":LocateResident(target.Id);break;
            case "town_building":LocateTownProject(target.Id);break;
        }
        _sceneInspectorRefresh=0;
    }
}
