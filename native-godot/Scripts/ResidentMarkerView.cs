using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;

public partial class Main
{
    const int SceneResidentMarkerLimit=5;
    const float SceneResidentMarkerRange=155;
    enum ResidentMarkerSite { Person, Home, Truck }
    readonly record struct SceneResidentTarget(string Id,string Name,ResidentMarkerSite Site,Node3D Anchor,string HomeId="");
    sealed class SceneResidentMarker(Button button)
    {
        public Button Button { get; }=button;
        public SceneResidentTarget? Target { get; set; }
        public bool Selected { get; set; }
    }

    readonly List<SceneResidentMarker> _sceneResidentMarkers=new();
    readonly List<SceneResidentTarget> _sceneResidentCandidates=new();
    StyleBoxFlat _sceneResidentNormal=null!,_sceneResidentActive=null!;
    SceneResidentTarget? _sceneResidentFocus;
    string _selectedSceneResident="";
    bool _sceneResidentsDirty=true;

    void CreateResidentSceneMarkers(CanvasLayer layer,Theme theme)
    {
        _sceneResidentNormal=TownTheme.Box("f3e9f4f5",7,4);
        _sceneResidentActive=TownTheme.Box("d6bad9ff",7,4);
        for(int i=0;i<SceneResidentMarkerLimit;i++){
            var button=new Button{
                Name=$"SceneResidentMarker{i}",Theme=theme,Visible=false,FocusMode=Control.FocusModeEnum.None,
                MouseFilter=Control.MouseFilterEnum.Pass,MouseDefaultCursorShape=Control.CursorShape.PointingHand,
                TextOverrunBehavior=TextServer.OverrunBehavior.TrimEllipsis,ClipText=true,
                ActionMode=BaseButton.ActionModeEnum.Release
            };
            var marker=new SceneResidentMarker(button);
            button.AddThemeFontSizeOverride("font_size",SceneNameplateFontSize);
            button.AddThemeStyleboxOverride("normal",_sceneResidentNormal);
            button.AddThemeStyleboxOverride("hover",_sceneResidentActive);
            button.AddThemeStyleboxOverride("pressed",_sceneResidentActive);
            button.Pressed+=()=>{
                if(!SceneInteractionEnabled()||marker.Target is not SceneResidentTarget target||!ResidentMarkerPoint(target).HasValue)return;
                if(target.Site==ResidentMarkerSite.Home)SelectResidence(target.HomeId);
                else SelectSceneResident(target.Id);
            };
            layer.AddChild(button);
            _sceneResidentMarkers.Add(marker);
        }
    }

    void MarkLocatedResident(string id)
    {
        if(_followResidentId!=id)StopResidentFollow();
        _selectedSceneResident=id;
        _sceneResidentsDirty=true;
    }

    void ClearSceneResidentSelection()=>MarkLocatedResident("");

    bool SelectSceneResident(string id)
    {
        if(DirectoryPerson(id) is not JsonNode person)return false;
        StopPetFollow();
        _selectedPet="";
        MarkLocatedResident(id);
        FocusSceneInspection("person",id);
        _selection=ResidentSceneDescription(person);
        RefreshScenePetCare();
        return true;
    }

    string ResidentSceneDescription(JsonNode person)=>NativeGame.S(person,"role")=="居民"?ResidentDescription(person):
        $"{NativeGame.S(person,"name")} · 通勤者\n{NativeGame.S(person,"profession")} · {ResidentActivity(person)}";

    static bool IsVisibleSceneResident(RoadAgent actor)=>!actor.IsCar&&!actor.IsQueuedForDeletion()&&!actor.InsideHome&&
        actor.ProcessMode!=ProcessModeEnum.Disabled&&actor.Visual.IsVisibleInTree();

    void AssignResidentSceneMarkers()
    {
        _sceneResidentsDirty=false;
        _sceneResidentFocus=null;
        _sceneResidentCandidates.Clear();
        if(_benchmark||_sceneResidentMarkers.Count==0)return;
        var people=Data.People.OfType<JsonNode>().Where(person=>IsDirectoryPerson(person)&&NativeGame.S(person,"status")!="离镇")
            .ToDictionary(person=>NativeGame.S(person,"person_id"));
        if(people.TryGetValue(_selectedSceneResident,out var selected))_sceneResidentFocus=SelectedResidentMarker(selected);
        if(_shownCamera.Distance>SceneResidentMarkerRange)return;

        var cameraPoint=_camera.GlobalPosition;
        var bounds=GetViewport().GetVisibleRect().Grow(-8);
        var nearby=Agents.Where(actor=>actor.Ident!=_selectedSceneResident&&IsVisibleSceneResident(actor)&&people.ContainsKey(actor.Ident))
            .Select(actor=>(Actor:actor,Distance:actor.GlobalPosition.DistanceSquaredTo(cameraPoint)))
            .Where(item=>item.Distance<SceneResidentMarkerRange*SceneResidentMarkerRange)
            .OrderByDescending(item=>NativeGame.S(people[item.Actor.Ident],"role")=="居民").ThenBy(item=>item.Distance);
        foreach(var item in nearby){
            var actor=item.Actor;
            var target=new SceneResidentTarget(actor.Ident,NativeGame.S(people[actor.Ident],"name"),ResidentMarkerSite.Person,actor);
            if(ResidentMarkerPoint(target) is not Vector3 point||_camera.IsPositionBehind(point)||!bounds.HasPoint(_camera.UnprojectPosition(point)))continue;
            _sceneResidentCandidates.Add(target);
            if(_sceneResidentCandidates.Count>=SceneResidentMarkerLimit*3)break;
        }
    }

    SceneResidentTarget? SelectedResidentMarker(JsonNode person)
    {
        string id=NativeGame.S(person,"person_id");
        var actor=Agents.FirstOrDefault(agent=>!agent.IsCar&&agent.Ident==id&&!agent.IsQueuedForDeletion());
        var entry=DescribeDirectoryPerson(person,actor);
        if(entry.Place==ResidentPlace.Freight&&_freightTruck is RoadAgent truck)return new(id,entry.Name,ResidentMarkerSite.Truck,truck);
        if(entry.Place==ResidentPlace.Outdoors&&actor!=null&&IsVisibleSceneResident(actor))return new(id,entry.Name,ResidentMarkerSite.Person,actor);
        if(entry.Place!=ResidentPlace.Home||entry.Home==null)return null;
        string homeId=NativeGame.S(entry.Home,"building_id");
        return _dynamic.TryGetValue("town_building:"+homeId,out var house)?new(id,entry.Name,ResidentMarkerSite.Home,house,homeId):null;
    }

    Vector3? ResidentMarkerPoint(SceneResidentTarget target)
    {
        var anchor=target.Anchor;
        if(!GodotObject.IsInstanceValid(anchor)||anchor.IsQueuedForDeletion()||!anchor.IsVisibleInTree())return null;
        if(target.Site==ResidentMarkerSite.Home&&anchor is MeshInstance3D house)
            return house.GlobalPosition+Vector3.Up*(Math.Max(2,house.Mesh?.GetAabb().End.Y??0)+.45f);
        if(anchor is not RoadAgent actor)return null;
        if(target.Site==ResidentMarkerSite.Truck)return FreightAboard(target.Id)?actor.GlobalPosition+Vector3.Up*3.8f:null;
        if(!IsVisibleSceneResident(actor)||FreightAboard(target.Id))return null;
        float height=actor.Visual.Mesh?.GetAabb().End.Y??1.9f;
        return actor.Visual.GlobalPosition+Vector3.Up*(Math.Max(.9f,height*actor.Visual.Scale.Y)+.35f);
    }

    bool PlaceResidentMarker(int index,SceneResidentTarget target,bool selected,Rect2 bounds)
    {
        if(ResidentMarkerPoint(target) is not Vector3 point)return false;
        var marker=_sceneResidentMarkers[index];
        string suffix=target.Site switch { ResidentMarkerSite.Home=>" · 在家",ResidentMarkerSite.Truck=>" · 驾车中",_=>"" };
        string caption=(selected?"◆ ":"● ")+target.Name+suffix;
        if(marker.Button.Text!=caption){
            marker.Button.Text=caption;
            float width=marker.Button.GetThemeFont("font").GetStringSize(caption,fontSize:SceneNameplateFontSize).X;
            marker.Button.Size=new(Mathf.Clamp(width+14,48,172),27);
        }
        if(!PlaceSceneNameplate(marker.Button,point,bounds))return false;
        if(marker.Target!=target){
            marker.Target=target;
            marker.Button.TooltipText=target.Name+(target.Site==ResidentMarkerSite.Home?" · 点击查看住户":" · 点击查看人物");
        }
        if(marker.Selected!=selected){
            marker.Selected=selected;
            marker.Button.AddThemeStyleboxOverride("normal",selected?_sceneResidentActive:_sceneResidentNormal);
        }
        return true;
    }

    int PlaceSelectedResidentMarker(bool refreshName,Rect2 bounds)
    {
        if(refreshName||_sceneResidentsDirty)AssignResidentSceneMarkers();
        return _sceneResidentMarkers.Count>0&&_sceneResidentFocus is SceneResidentTarget target&&PlaceResidentMarker(0,target,true,bounds)?1:0;
    }

    void PlaceNearbyResidentMarkers(int index,Rect2 bounds)
    {
        foreach(var target in _sceneResidentCandidates){
            if(index==_sceneResidentMarkers.Count)break;
            if(PlaceResidentMarker(index,target,false,bounds))index++;
        }
        for(;index<_sceneResidentMarkers.Count;index++){
            _sceneResidentMarkers[index].Target=null;
            _sceneResidentMarkers[index].Button.Hide();
        }
    }

    void HideResidentSceneMarkers()
    {
        foreach(var marker in _sceneResidentMarkers){marker.Target=null;marker.Button.Hide();}
        _sceneResidentsDirty=true;
    }
}
