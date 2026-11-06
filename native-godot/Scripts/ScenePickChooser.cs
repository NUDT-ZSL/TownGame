using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

public partial class Main
{
    PanelContainer? _scenePickChooser;
    VBoxContainer? _scenePickRows;
    Label? _scenePickHeading,_scenePickDetails;
    Vector2 _scenePickPoint;
    ScenePick[] _scenePickCandidates=Array.Empty<ScenePick>();
    Button[] _scenePickButtons=Array.Empty<Button>();
    Action<ScenePick>? _acceptScenePick;
    int _scenePickIndex;
    MeshInstance3D? _scenePickedMesh;
    Material? _scenePickedPreviousOverlay;
    ShaderMaterial? _scenePickedOutline;

    static bool IsActorScenePick(ScenePick pick)=>pick.Kind is "pet" or "person" or "vehicle" or "manager";

    ScenePick[] FindScenePickCandidates(Vector2 point)
    {
        if(PickSceneAt(point) is not ScenePick first||!IsActorScenePick(first))return Array.Empty<ScenePick>();
        var candidates=new List<ScenePick>{first};
        var seen=new HashSet<(string Kind,string Id)>{(first.Kind,first.Id)};
        var offsets=new[]{Vector2.Zero,new Vector2(-11,0),new Vector2(11,0),new Vector2(0,-11),new Vector2(0,11),
            new Vector2(-8,-8),new Vector2(8,-8),new Vector2(-8,8),new Vector2(8,8)};
        var world=GetWorld3D().DirectSpaceState;
        foreach(var offset in offsets){
            var pixel=point+offset;
            if(!GetViewport().GetVisibleRect().HasPoint(pixel))continue;
            var from=_camera.ProjectRayOrigin(pixel);
            using var query=PhysicsRayQueryParameters3D.Create(from,from+_camera.ProjectRayNormal(pixel)*1500);
            var excluded=new Godot.Collections.Array<Rid>();
            for(int hit=0;hit<5&&candidates.Count<5;hit++){
                query.Exclude=excluded;
                var result=world.IntersectRay(query);
                if(result.Count==0||result["collider"].AsGodotObject() is not CollisionObject3D collider||!collider.HasMeta("kind"))break;
                var pick=new ScenePick(collider.GetMeta("kind").AsString(),collider.GetMeta("ident").AsString(),
                    collider.GetChildren().OfType<MeshInstance3D>().FirstOrDefault(),default);
                if(!IsActorScenePick(pick))break;
                if(seen.Add((pick.Kind,pick.Id)))candidates.Add(pick);
                excluded.Add(collider.GetRid());
            }
        }
        return candidates.ToArray();
    }

    void CreateScenePickChooser(CanvasLayer layer,Theme theme)
    {
        var popup=new PanelContainer{Name="TownScenePickChooser",Visible=false,Theme=theme,CustomMinimumSize=new(300,0)};
        popup.AddThemeStyleboxOverride("panel",TownTheme.Box("fff5e7fa",10,9));
        var body=new VBoxContainer();
        popup.AddChild(body);
        _scenePickHeading=MarketLabel(body,"选择重叠的对象",16,TownTheme.Leaf);
        _scenePickHeading.Name="TownScenePickHeading";
        MarketLabel(body,"↑↓ 轮选 · 数字直达 · Enter 确认 · Esc 取消",12,TownTheme.Muted);
        _scenePickRows=new VBoxContainer();
        body.AddChild(_scenePickRows);
        _scenePickDetails=MarketLabel(body,"",13,TownTheme.Muted);
        _scenePickDetails.Name="TownScenePickDetails";
        _scenePickDetails.ClipText=true;
        layer.AddChild(popup);
        _scenePickChooser=popup;
        FitTownHudCard(popup);
        popup.Resized+=PositionScenePickChooser;
    }

    bool TryOpenScenePickChooser(Vector2 point,Action<ScenePick> accept)
    {
        if(_scenePickChooser==null||!SceneInteractionEnabled())return false;
        var candidates=FindScenePickCandidates(point);
        if(candidates.Length<2){CloseScenePickChooser(true);return false;}
        ShowScenePickChoices(point,candidates,accept);
        return true;
    }

    void ShowScenePickChoices(Vector2 point,ScenePick[] candidates,Action<ScenePick> accept)
    {
        ClearSceneHover();
        _scenePickCandidates=candidates;
        _acceptScenePick=accept;
        _scenePickPoint=point;
        _scenePickHeading!.Text=$"选择重叠对象 · {candidates.Length} 项";
        var rows=_scenePickRows!;
        foreach(var child in rows.GetChildren()){rows.RemoveChild(child);child.QueueFree();}
        _scenePickButtons=candidates.Select((candidate,index)=>{
            string title=SceneActionHint(candidate);
            if(title.Length==0)title=candidate.Kind=="vehicle"?"货车":candidate.Id;
            string summary=$"{index+1}  ·  {ScenePickKind(candidate.Kind)}  ·  {title}";
            var button=MarketButton(rows,summary,()=>AcceptScenePick(index),accent:"地图");
            button.ToggleMode=true;
            button.FocusMode=Control.FocusModeEnum.None;
            button.Alignment=HorizontalAlignment.Left;
            button.ClipText=true;
            button.TooltipText=summary;
            button.AccessibilityName=summary;
            button.MouseEntered+=()=>MoveScenePick(index);
            return button;
        }).ToArray();
        _scenePickChooser!.ResetSize();
        _scenePickChooser.Show();
        PositionScenePickChooser();
        MoveScenePick(0);
    }

    void PositionScenePickChooser()
    {
        if(_scenePickChooser?.Visible!=true)return;
        var area=GetViewport().GetVisibleRect().Grow(-8);
        _scenePickChooser.Scale=Vector2.One*UiScale;
        var size=_scenePickChooser.Size*UiScale;
        _scenePickChooser.Position=(_scenePickPoint+new Vector2(10,12)).Clamp(area.Position,(area.End-size).Max(area.Position));
    }

    static string ScenePickKind(string kind)=>kind switch{
        "pet"=>"伙伴","person"=>"人物","vehicle"=>"车辆","manager"=>"管理员",_=>"对象"
    };

    bool HandleScenePickChooserInput(InputEvent input)
    {
        if(_scenePickChooser?.Visible!=true)return false;
        if(input is InputEventMouseButton {Pressed:true} mouse&&!_scenePickChooser.GetGlobalRect().HasPoint(mouse.Position)){
            CloseScenePickChooser(true);
            return false;
        }
        if(input is not InputEventKey {Pressed:true,AltPressed:false,CtrlPressed:false,MetaPressed:false,ShiftPressed:false} key)return false;
        var pressed=TownPhysicalKey(key);
        int slot=pressed switch{
            >=Key.Key1 and <=Key.Key5=>(int)pressed-(int)Key.Key1,
            >=Key.Kp1 and <=Key.Kp5=>(int)pressed-(int)Key.Kp1,
            _=>-1
        };
        if(slot>=0){if(!key.Echo)AcceptScenePick(slot);return true;}
        switch(pressed){
            case Key.Escape:CloseScenePickChooser(true);return true;
            case Key.Up:MoveScenePick(_scenePickIndex-1);return true;
            case Key.Down:MoveScenePick(_scenePickIndex+1);return true;
            case Key.Enter:
            case Key.KpEnter:if(!key.Echo)AcceptScenePick(_scenePickIndex);return true;
            default:return false;
        }
    }

    void MoveScenePick(int index)
    {
        if(_scenePickCandidates.Length==0)return;
        _scenePickIndex=(index+_scenePickCandidates.Length)%_scenePickCandidates.Length;
        for(int i=0;i<_scenePickButtons.Length;i++)_scenePickButtons[i].SetPressedNoSignal(i==_scenePickIndex);
        var pick=_scenePickCandidates[_scenePickIndex];
        string location="";
        if(GodotObject.IsInstanceValid(pick.Mesh)&&pick.Mesh!.IsInsideTree()){
            var cell=WorldData.Grid(pick.Mesh.GlobalPosition);
            location=$" · 地图格 {cell.X}, {cell.Y}";
        }
        _scenePickDetails!.Text=$"第 {_scenePickIndex+1} / {_scenePickCandidates.Length} 项 · {ScenePickKind(pick.Kind)}{location}";
        SetScenePickedOutline(pick.Mesh);
    }

    void AcceptScenePick(int index)
    {
        if(index<0||index>=_scenePickCandidates.Length)return;
        var target=_scenePickCandidates[index];
        var callback=_acceptScenePick;
        CloseScenePickChooser(false);
        if(GodotObject.IsInstanceValid(target.Mesh)&&!target.Mesh!.IsQueuedForDeletion())callback?.Invoke(target);
        else SetScenePickedOutline(null);
    }

    void CloseScenePickChooser(bool clearOutline)
    {
        if(_scenePickChooser?.Visible!=true)return;
        _scenePickChooser.Hide();
        _scenePickCandidates=Array.Empty<ScenePick>();
        _acceptScenePick=null;
        if(clearOutline)SetScenePickedOutline(null);
    }

    void SetScenePickedOutline(MeshInstance3D? mesh)
    {
        if(GodotObject.IsInstanceValid(_scenePickedMesh)&&_scenePickedMesh!.MaterialOverlay==_scenePickedOutline)
            _scenePickedMesh.MaterialOverlay=_scenePickedPreviousOverlay;
        _scenePickedMesh=null;
        _scenePickedPreviousOverlay=null;
        if(!GodotObject.IsInstanceValid(mesh)||mesh!.Mesh==null||mesh.IsQueuedForDeletion())return;
        ClearSceneHover();
        _scenePickedOutline??=new ShaderMaterial{Shader=new Shader{Code=SceneOutlineShader}};
        _scenePickedOutline.SetShaderParameter("mesh_center",mesh.Mesh.GetAabb().GetCenter());
        _scenePickedMesh=mesh;
        _scenePickedPreviousOverlay=mesh.MaterialOverlay;
        mesh.MaterialOverlay=_scenePickedOutline;
    }
}
