using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;

public partial class Main
{
    const int SceneNameplateLimit=6;
    const int SceneNameplateFontSize=14;
    readonly record struct ScenePick(string Kind,string Id,MeshInstance3D? Mesh,Vector2I Cell);

    sealed class SceneNameplate
    {
        public SceneNameplate(Button button){Button=button;}
        public Button Button {get;}
        public PetAgent? Pet {get;set;}
        public bool Selected {get;set;}
    }

    readonly List<SceneNameplate> _sceneNameplates=new();
    readonly List<Rect2> _sceneNameplateRects=new();
    StyleBoxFlat _sceneNameplateNormal=null!,_sceneNameplateSelected=null!;
    PanelContainer _sceneHoverCard=null!;
    Label _sceneHoverLabel=null!;
    ShaderMaterial? _sceneOutline;
    MeshInstance3D? _sceneHoverMesh;
    Material? _sceneHoverPreviousOverlay;
    ScenePick? _sceneHoverPick;
    double _sceneHoverElapsed;
    bool _sceneNameplatesReady;

    const string SceneOutlineShader="""
shader_type spatial;
render_mode unshaded, cull_front, depth_draw_never, fog_disabled, shadows_disabled;
uniform vec3 mesh_center;
void vertex() {
    vec4 point = PROJECTION_MATRIX * MODELVIEW_MATRIX * vec4(VERTEX, 1.0);
    vec4 center = PROJECTION_MATRIX * MODELVIEW_MATRIX * vec4(mesh_center, 1.0);
    if (point.w > 0.01 && center.w > 0.01) {
        vec2 outward = (point.xy / point.w - center.xy / center.w) * VIEWPORT_SIZE;
        float radius = length(outward);
        if (radius > 0.01) {
            point.xy += (outward / radius) * 4.0 * point.w / VIEWPORT_SIZE;
        }
    }
    POSITION = point;
}
void fragment() {
    ALBEDO = vec3(1.0, 0.74, 0.36);
}
""";

    void CreateSceneInteractionUi(CanvasLayer layer,Theme theme)
    {
        _sceneNameplateNormal=TownTheme.Box("fff5e6f2",7,4);
        _sceneNameplateSelected=TownTheme.Box("ffdfacf8",7,4);
        for(int index=0;index<SceneNameplateLimit;index++){
            var button=new Button{
                Name=$"PetSceneNameplate{index}",Theme=theme,Visible=false,FocusMode=Control.FocusModeEnum.None,
                MouseDefaultCursorShape=Control.CursorShape.PointingHand,ClipText=true,TextOverrunBehavior=TextServer.OverrunBehavior.TrimEllipsis,
                TooltipText="点击选中伙伴",ActionMode=BaseButton.ActionModeEnum.Press
            };
            var plate=new SceneNameplate(button);
            button.AddThemeFontSizeOverride("font_size",SceneNameplateFontSize);
            button.AddThemeStyleboxOverride("normal",_sceneNameplateNormal);
            button.AddThemeStyleboxOverride("hover",_sceneNameplateSelected);
            button.AddThemeStyleboxOverride("pressed",_sceneNameplateSelected);
            button.Pressed+=()=>{
                if(SceneInteractionEnabled()&&plate.Pet is PetAgent pet&&GodotObject.IsInstanceValid(pet)&&!pet.IsQueuedForDeletion())
                    SelectScenePet(pet.Ident);
            };
            layer.AddChild(button);
            _sceneNameplates.Add(plate);
        }
        CreateResidentSceneMarkers(layer,theme);
        CreateScenePickChooser(layer,theme);
        CreateSceneInspector(layer,theme);
        _sceneHoverCard=new PanelContainer{Name="SceneHoverTooltip",Theme=theme,Visible=false,MouseFilter=Control.MouseFilterEnum.Ignore};
        _sceneHoverCard.AddThemeStyleboxOverride("panel",TownTheme.Box("fff5e7fa",8,7));
        _sceneHoverLabel=new Label{MouseFilter=Control.MouseFilterEnum.Ignore};
        _sceneHoverLabel.AddThemeFontSizeOverride("font_size",14);
        _sceneHoverCard.AddChild(_sceneHoverLabel);
        layer.AddChild(_sceneHoverCard);
    }

    bool SceneInteractionEnabled()=>!InPhotoMode&&_placementAction.Length==0&&_management?.Visible!=true&&_townSearchWindow?.Visible!=true
        &&!_townCameraDragging&&Input.MouseMode!=Input.MouseModeEnum.Captured;

    ScenePick? PickSceneAt(Vector2 point)
    {
        var from=_camera.ProjectRayOrigin(point);
        using var query=PhysicsRayQueryParameters3D.Create(from,from+_camera.ProjectRayNormal(point)*1500);
        var result=GetWorld3D().DirectSpaceState.IntersectRay(query);
        if(result.Count==0)return null;
        var collider=result["collider"].AsGodotObject();
        if(collider is Node3D node&&node.HasMeta("kind")){
            if(node.IsQueuedForDeletion())return null;
            var mesh=node.GetParent() as MeshInstance3D??node.GetChildren().OfType<MeshInstance3D>().FirstOrDefault();
            return new ScenePick(node.GetMeta("kind").AsString(),node.GetMeta("ident").AsString(),mesh,default);
        }
        var cell=WorldData.Grid(result["position"].AsVector3());
        if(Data.Roads.ContainsKey(cell)&&_dynamic.TryGetValue("road:"+cell,out var road))
            return new ScenePick("road","",road as MeshInstance3D,cell);
        return null;
    }

    JsonNode? ScenePetData(string id)=>Data.Pets.FirstOrDefault(pet=>pet!=null&&NativeGame.S(pet,"pet_id")==id);

    void SelectScenePet(string id)
    {
        ClearSceneResidentSelection();
        if(_selectedPet!=id)StopPetFollow();
        _selectedPet=id;
        FocusSceneInspection("pet",id);
        _selection="";
        RefreshScenePetCare();
        UpdatePetMarker(true);
    }

    void OpenSceneRoad(Vector2I cell)
    {
        if(!Data.Roads.TryGetValue(cell,out var road))return;
        string style=NativeGame.S(road,"road_style","asphalt");
        _buildRoadStyle=Math.Max(0,Array.IndexOf(Game.Rules["ROAD_STYLES"]!.AsObject().Select(pair=>pair.Key).ToArray(),style));
        _buildWorkspace=1;
        _buildX=cell.X;
        _buildY=cell.Y;
        ShowManagement("建造");
    }

    string SceneActionHint(ScenePick target)
    {
        string id=target.Id;
        if(target.Kind=="road"&&Data.Roads.TryGetValue(target.Cell,out var road)){
            string style=NativeGame.S(road,"road_style","asphalt");
            string name=Game.Rules["ROAD_STYLES"]![style] is JsonNode spec?NativeGame.S(spec,"name"):"道路";
            return $"{name} ({target.Cell.X}, {target.Cell.Y}) · 查看道路";
        }
        if(target.Kind=="town_building"&&Game.FindTownBuilding(id) is JsonNode building)
            return $"{TownProjectName(building)} · {(Game.IsResidentialHome(building)&&!IsTownProjectUnderway(building)?"查看住户":"查看工程")}";
        if(target.Kind=="prop"&&PetActivityStations.TryGetValue(id,out var station))
            return $"{station.Label} · {station.Help}";
        if(target.Kind=="pet"&&ScenePetData(id) is JsonNode pet)
            return $"{NativeGame.S(pet,"name")} · 选中伙伴";
        if(target.Kind=="person"&&Data.People.FirstOrDefault(person=>person!=null&&NativeGame.S(person,"person_id")==id) is JsonNode person)
            return $"{NativeGame.S(person,"name")} · {NativeGame.S(person,"profession",NativeGame.S(person,"role"))} · 查看人物";
        return target.Kind switch{
            "building"=>"庄园地块 · 查看建造",
            "decoration"=>"装饰物 · 查看摆放",
            "manager"=>"农场管理员 · 查看工作",
            "vehicle"=>"货车 · 查看运输",
            "waste"=>"粪便 · 清扫",
            "herd"=>"牧场绵羊 · 查看牧群",
            "zone"=>"街区地标 · 查看街区",
            _=>""
        };
    }

    void UpdateSceneInteraction(double delta)
    {
        if(_sceneHoverCard==null)return;
        UpdateSceneInspector(delta);
        if(!SceneInteractionEnabled()){
            CloseScenePickChooser(true);
            if(InPhotoMode||_placementAction.Length>0)SetScenePickedOutline(null);
        }
        if(_scenePickChooser?.Visible==true){ClearSceneHover();return;}
        var viewport=GetViewport();
        var point=viewport.GetMousePosition();
        if(!SceneInteractionEnabled()||_benchmark||viewport.GuiGetHoveredControl()!=null
            ||!viewport.GetVisibleRect().HasPoint(point)){
            ClearSceneHover();
            _sceneHoverElapsed=.1;
            return;
        }
        _sceneHoverElapsed+=delta;
        if(_sceneHoverElapsed>=.075){
            _sceneHoverElapsed=0;
            UpdateSceneHover(point);
        }
        if(_sceneHoverCard.Visible)PositionSceneHover(point);
    }

    void UpdateSceneHover(Vector2 point)
    {
        var pick=PickSceneAt(point);
        if(pick is not ScenePick target){ClearSceneHover();return;}
        if(_sceneHoverPick!=pick){
            ClearSceneHover();
            string hint=SceneActionHint(target);
            if(hint.Length==0)return;
            _sceneHoverPick=pick;
            _sceneHoverLabel.Text=hint;
            _sceneHoverCard.ResetSize();
            _sceneHoverCard.Show();
            if(target.Mesh is MeshInstance3D mesh&&mesh!=_scenePickedMesh&&mesh.Mesh!=null){
                _sceneOutline??=new ShaderMaterial{Shader=new Shader{Code=SceneOutlineShader}};
                _sceneHoverMesh=mesh;
                _sceneHoverPreviousOverlay=mesh.MaterialOverlay;
                mesh.MaterialOverlay=_sceneOutline;
            }
        }
        if(_sceneHoverMesh?.Mesh is Mesh geometry)
            _sceneOutline!.SetShaderParameter("mesh_center",geometry.GetAabb().GetCenter());
        PositionSceneHover(point);
    }

    void PositionSceneHover(Vector2 pointer)
    {
        var bounds=GetViewport().GetVisibleRect().Grow(-8);
        var size=_sceneHoverCard.Size;
        if(TownReducedInterfaceMotion){_sceneHoverCard.Position=new(Math.Max(bounds.Position.X,bounds.End.X-size.X),bounds.Position.Y+82);return;}
        float x=Math.Min(pointer.X+15,bounds.End.X-size.X);
        float y=pointer.Y+23+size.Y<=bounds.End.Y?pointer.Y+23:pointer.Y-size.Y-12;
        _sceneHoverCard.Position=new(Math.Max(bounds.Position.X,x),Math.Max(bounds.Position.Y,y));
    }

    void ClearSceneHover()
    {
        if(GodotObject.IsInstanceValid(_sceneHoverMesh)&&_sceneHoverMesh!.MaterialOverlay==_sceneOutline)
            _sceneHoverMesh.MaterialOverlay=_sceneHoverPreviousOverlay;
        _sceneHoverMesh=null;
        _sceneHoverPreviousOverlay=null;
        _sceneHoverPick=null;
        _sceneHoverCard?.Hide();
    }

    void UpdatePetMarker(bool refreshName)
    {
        if(!SceneInteractionEnabled()){
            foreach(var plate in _sceneNameplates){plate.Button.Hide();plate.Pet=null;}
            HideResidentSceneMarkers();
            _sceneNameplatesReady=false;
            return;
        }
        if(_sceneNameplates.Count==0)return;
        if(refreshName||!_sceneNameplatesReady){AssignSceneNameplates();_sceneNameplatesReady=true;}
        var bounds=GetViewport().GetVisibleRect().Grow(-10);
        _sceneNameplateRects.Clear();
        int residentMarkers=PlaceSelectedResidentMarker(refreshName,bounds);
        foreach(var plate in _sceneNameplates){
            var pet=plate.Pet;
            if(!GodotObject.IsInstanceValid(pet)||pet!.IsQueuedForDeletion()){plate.Button.Hide();continue;}
            var head=pet.GlobalPosition+Vector3.Up*pet.NameplateHeight;
            if(!PlaceSceneNameplate(plate.Button,head,bounds))plate.Button.Hide();
        }
        PlaceNearbyResidentMarkers(residentMarkers,bounds);
    }

    bool PlaceSceneNameplate(Button button,Vector3 head,Rect2 bounds)
    {
        if(_camera.IsPositionBehind(head))return false;
        var screen=_camera.UnprojectPosition(head);
        var rect=new Rect2(screen-new Vector2(button.Size.X*.5f,button.Size.Y+2),button.Size);
        if(!bounds.Encloses(rect)||_sceneNameplateRects.Any(other=>other.Intersects(rect))
            ||(_townToolbar?.Visible==true&&_townToolbar.GetGlobalRect().Intersects(rect))
            ||(_firstDayGuidePanel?.Visible==true&&_firstDayGuidePanel.GetGlobalRect().Intersects(rect))
            ||(_sceneInspector?.Visible==true&&_sceneInspector.GetGlobalRect().Intersects(rect))
            ||(_scenePickChooser?.Visible==true&&_scenePickChooser.GetGlobalRect().Intersects(rect))
            ||(_panel.Visible&&_panel.GetGlobalRect().Intersects(rect)))return false;
        _sceneNameplateRects.Add(rect.Grow(2));
        button.Position=rect.Position;
        button.Show();
        return true;
    }

    void AssignSceneNameplates()
    {
        var bounds=GetViewport().GetVisibleRect().Grow(-12);
        var cameraPoint=_camera.GlobalPosition;
        var candidates=GetChildren().OfType<PetAgent>()
            .Where(pet=>!pet.IsQueuedForDeletion())
            .Select(pet=>(Pet:pet,Selected:pet.Ident==_selectedPet,Distance:pet.GlobalPosition.DistanceSquaredTo(cameraPoint)))
            .Where(item=>item.Selected||item.Distance<115*115)
            .OrderByDescending(item=>item.Selected).ThenBy(item=>item.Distance);
        int index=0;
        foreach(var item in candidates){
            var pet=item.Pet;
            var head=pet.GlobalPosition+Vector3.Up*pet.NameplateHeight;
            if(_camera.IsPositionBehind(head)||!bounds.HasPoint(_camera.UnprojectPosition(head)))continue;
            if(!item.Selected&&!NeedsSceneNameplate(pet,head))continue;
            if(ScenePetData(pet.Ident) is not JsonNode row||!Game.Active(row)||NativeGame.B(row,"fostered"))continue;
            var plate=_sceneNameplates[index++];
            if(plate.Pet!=pet||plate.Selected!=item.Selected){
                plate.Pet=pet;
                plate.Selected=item.Selected;
                plate.Button.AddThemeStyleboxOverride("normal",item.Selected?_sceneNameplateSelected:_sceneNameplateNormal);
            }
            string caption=(item.Selected?"◆ ":"")+NativeGame.S(row,"name");
            if(plate.Button.Text!=caption){
                plate.Button.Text=caption;
                float width=plate.Button.GetThemeFont("font").GetStringSize(caption,fontSize:SceneNameplateFontSize).X;
                plate.Button.Size=new(Mathf.Clamp(width+12,48,142),27);
            }
            if(index==_sceneNameplates.Count)break;
        }
        for(;index<_sceneNameplates.Count;index++){
            _sceneNameplates[index].Pet=null;
            _sceneNameplates[index].Button.Hide();
        }
    }

    bool NeedsSceneNameplate(PetAgent pet,Vector3 head)
    {
        // Short species can disappear behind decorative fences that have no
        // simulation collider, even when their projected model is sizeable.
        if(pet.NameplateHeight<1.35f||_camera.UnprojectPosition(head).DistanceTo(_camera.UnprojectPosition(pet.GlobalPosition))<34)
            return true;
        var center=pet.GlobalPosition.Lerp(head,.5f);
        var from=_camera.ProjectRayOrigin(_camera.UnprojectPosition(center));
        using var ray=PhysicsRayQueryParameters3D.Create(from,center,1);
        return GetWorld3D().DirectSpaceState.IntersectRay(ray).Count>0;
    }
}
