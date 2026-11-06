using Godot;
using System;
using System.Linq;
using System.Text.Json.Nodes;

public partial class Main
{
    readonly NativePetMesh _petModels=new();
    public Mesh PetMesh(JsonNode pet,string pose,int frame)=>_petModels.Get(NativeGame.S(pet,"species"),Game.Appearance(pet),NativeGame.B(pet,"rare"),NativeGame.S(pet,"illness_type")!=""||NativeGame.D(pet,"injury")>0,Game.Stage(pet),pose,frame);
    SubViewport CreatePetPreviewViewport(SubViewportContainer container,Vector2I size,SubViewport.UpdateMode updates)
    {
        var viewport=new SubViewport{
            Name="TownPetPreviewAppearance",
            Size=size,RenderTargetUpdateMode=updates,OwnWorld3D=true,Msaa3D=SceneAntialiasing
        };
        container.AddChild(viewport);
        return viewport;
    }
    PanelContainer AnimalBadge(string species,int width=130,int height=100,JsonNode? pet=null)
    {
        var speciesIndex=Game.Rules["SPECIES"]!.AsObject().Select((row,index)=>(row.Key,index)).First(row=>row.Key==species).index;
        string background=TownTheme.AnimalColor(speciesIndex);
        var card=new PanelContainer{CustomMinimumSize=new(width,height+28),SizeFlagsVertical=Control.SizeFlags.ShrinkCenter};card.AddThemeStyleboxOverride("panel",TownTheme.Box(background,10,5));
        var column=new VBoxContainer();column.AddThemeConstantOverride("separation",2);card.AddChild(column);
        column.AddChild(QueuePetPortrait(species,width-10,height,background,pet));
        string caption=pet==null?species:NativeGame.S(pet,"name");
        var label=new Label{Text=caption,TooltipText=caption,HorizontalAlignment=HorizontalAlignment.Center,TextOverrunBehavior=TextServer.OverrunBehavior.TrimEllipsis};label.AddThemeColorOverride("font_color",TownTheme.Ink);column.AddChild(label);
        return card;
    }
    HBoxContainer AnimalHeader(string tab)
    {
        var strip=new HBoxContainer();strip.AddThemeConstantOverride("separation",8);
        var species=Game.Rules["SPECIES"]!.AsObject().Select(p=>p.Key).ToArray();int start=Math.Max(0,Array.IndexOf(ManagementTabs,tab))%species.Length;
        strip.AddChild(AnimalBadge(species[start],92,43));
        return strip;
    }
    static string Stamp(double ts)=>DateTimeOffset.FromUnixTimeSeconds((long)Math.Clamp(ts,0,253402300799)).UtcDateTime.ToString("yyyy-MM-dd HH:mm");
    bool _mapShowsLandValues, _mapShowsConstruction=true, _mapShowsDisconnected=true, _mapShowsFreight=true;
    void MapControls()
    {
        LoadMapLayerPreferences();
        Section("小镇地图","拖动或方向键平移 · 滚轮或 + / − 缩放");
        var map=new TownMap{
            Name="TownNavigationMap",AccessibilityName="小镇导航地图",
            AccessibilityDescription="用方向键平移，按加号和减号缩放，按 Home 回到全镇概览。",Town=this,Heat=_mapShowsLandValues,
            ShowConstruction=_mapShowsConstruction, ShowDisconnected=_mapShowsDisconnected, ShowFreight=_mapShowsFreight
        };
        var viewTools=new HFlowContainer();
        viewTools.AddThemeConstantOverride("h_separation",6);
        _managementBody!.AddChild(viewTools);
        AddMapZoomTools(viewTools,map);

        var toolbar=new HFlowContainer();
        toolbar.AddThemeConstantOverride("h_separation",8);
        toolbar.AddThemeConstantOverride("v_separation",4);
        _managementBody.AddChild(toolbar);
        var residentFilters=new HFlowContainer();
        residentFilters.AddThemeConstantOverride("h_separation",6);
        _managementBody.AddChild(residentFilters);
        AddMapLayerControls(toolbar,residentFilters,map);
        AddMapLayerLegend(map);

        var openRoadReconnect=AddRoadReconnectControls(map,point=>Locate(point,55,"断开道路 · 尚未连通庄园"));
        var survey=AddMapSurveyTools(viewTools,map,Locate);

        var frame=new AspectRatioContainer{Ratio=WorldData.WorldWidth/WorldData.WorldDepth};
        var host=GetWindow();
        void ResizeMap()=>frame.CustomMinimumSize=new(0,Math.Clamp(host.GetVisibleRect().Size.Y*.55f,275,460));
        ResizeMap();
        host.SizeChanged+=ResizeMap;
        frame.TreeExiting+=()=>host.SizeChanged-=ResizeMap;
        _managementBody.AddChild(frame);
        var clip=new Control{ClipContents=true,MouseFilter=Control.MouseFilterEnum.Ignore};
        frame.AddChild(clip);
        clip.AddChild(map);
        map.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        if(_rememberedMapView is TownMap.MapView recent)map.RestoreMapView(recent);
        map.ViewChanged+=()=>_rememberedMapView=map.CurrentMapView;

        var details=new Label{Name="TownMapDetails",Text=TownMap.EmptyHoverText,AutowrapMode=TextServer.AutowrapMode.WordSmart};
        details.AddThemeColorOverride("font_color",TownTheme.Muted);
        _managementBody.AddChild(details);
        map.HoverChanged+=description=>details.Text=description;
        AddMapPinTools(viewTools,map);
        map.Navigate+=point=>Locate(point,100,"");
        map.ProjectSelected+=OpenTownProject;
        var residentResults=new VBoxContainer{Name="TownMapResidentResults",Visible=false};
        _managementBody.AddChild(residentResults);
        map.ResidentsSelected+=residents=>ResidentMapResults(residentResults,map,residents);
        map.SetResidentLayer(_mapShowsResidents);
        const string districtTarget="district:";
        if(_managementEntryTarget.StartsWith(districtTarget,StringComparison.Ordinal)&&map.FocusDistrict(_managementEntryTarget[districtTarget.Length..]))
            _managementFocusTarget=frame;
        else if(_managementEntryTarget.StartsWith("repair:",StringComparison.Ordinal)){
            var coordinates=_managementEntryTarget[7..].Split(',');
            if(coordinates.Length==2&&TownMap.TrySurveyCell(coordinates[0],coordinates[1],out var cell))openRoadReconnect(cell);
        }
        else if(_managementEntryTarget.StartsWith("cell:",StringComparison.Ordinal)){
            var coordinates=_managementEntryTarget[5..].Split(',');
            if(coordinates.Length==2&&TownMap.TrySurveyCell(coordinates[0],coordinates[1],out var cell)){
                map.InspectMapCell(cell,true);
                _managementFocusTarget=survey;
            }
        }
        _refreshManagementPage+=map.RefreshData;
        map.TreeExiting+=()=>_refreshManagementPage-=map.RefreshData;

        void Locate(Vector2 point, float distance, string selection)
        {
            RememberCameraSite(new Vector3(point.X,0,point.Y));
            StopPetFollow();
            StopResidentFollow();
            EndPlacement();
            _center=new(point.X,0,point.Y);
            _distance=distance;
            _selection=selection;
            _management?.Hide();
        }
    }
    void SaveControls()
    {
        SaveSlotControls();
        OfflineProgressControls();
        SnapshotHistoryControls();
        TownBundleControls();
        Section("存档管理","导入与备份");
        Text("新版存档："+SavePath);Text("可导入旧版v1～v6 SQLite数据库或新版JSON存档。导入前自动备份当前新版存档；旧数据库以只读方式打开。");
        ActionButton("导入旧存档 / JSON存档",()=>OpenSaveFileDialog(true));
        ActionButton("导出新版存档副本",()=>OpenSaveFileDialog(false));
    }

    void OpenSaveFileDialog(bool import)
    {
        var dialog=new FileDialog{
            FileMode=import?FileDialog.FileModeEnum.OpenFile:FileDialog.FileModeEnum.SaveFile,
            Access=FileDialog.AccessEnum.Filesystem,Filters=import?new[]{"*.db ; 旧版SQLite存档","*.json ; JSON存档"}:new[]{"*.json ; JSON存档"},
            Title=import?"选择要导入的存档":"导出存档副本",Theme=_panel.Theme,Transient=true,Exclusive=true
        };
        if(!import)dialog.CurrentFile="town-backup.json";
        _management!.AddChild(dialog);
        dialog.Canceled+=dialog.QueueFree;
        dialog.FileSelected+=path=>{
            dialog.Hide();
            dialog.QueueFree();
            if(import){
                ConfirmManagementAction("导入存档","导入后切换到所选存档，当前新版存档会自动备份。","确认导入",()=>{
                    try{
                        var state=LegacyImport.Read(path,Data.Root);
                        LegacyImport.SaveImported(state,SavePath);
                        GetTree().ReloadCurrentScene();
                    }catch(Exception ex){SetManagementFeedback("导入失败："+ex.Message,true);}
                });
                return;
            }
            try{Data.Save(path);SetManagementFeedback("存档副本已导出");}
            catch(Exception ex){SetManagementFeedback("导出失败："+ex.Message,true);}
        };
        dialog.PopupCenteredClamped(new Vector2I(800,560),.94f);
    }
}
