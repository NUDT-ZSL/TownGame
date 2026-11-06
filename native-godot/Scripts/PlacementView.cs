using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;

public partial class Main
{
    string _placementAction="";
    JsonObject _placementArgs=new();
    MeshInstance3D? _ghost;
    MeshInstance3D? _decorationFootprint;
    Node3D? _strokePreview;
    readonly List<NativeGame.Cell> _strokeCells=new();
    bool _strokeActive;
    string _strokeSignature="";
    readonly StandardMaterial3D _ghostGood=new(){AlbedoColor=new(.25f,.9f,.55f,.5f),Transparency=BaseMaterial3D.TransparencyEnum.Alpha,ShadingMode=BaseMaterial3D.ShadingModeEnum.Unshaded};
    readonly StandardMaterial3D _ghostBad=new(){AlbedoColor=new(.95f,.3f,.25f,.5f),Transparency=BaseMaterial3D.TransparencyEnum.Alpha,ShadingMode=BaseMaterial3D.ShadingModeEnum.Unshaded};
    void ArmPlacement(string action,JsonObject args)
    {
        CancelTownPointer();
        StopPetFollow();
        EndPlacement();
        _placementAction=action;
        ClearSceneHover();
        UpdatePetMarker(false);
        _placementArgs=args;
        _management?.Hide();
        _selection=action is "road" or "remove_road"
            ?$"按住左键拖拽，松开提交整笔。{TownBindingText("undo")} 撤销，Esc 退出。"
            :$"移动鼠标预览。左键确认，{TownBindingText("rotate")} 旋转，Esc 退出。";
        if(_ghost==null){
            _ghost=new MeshInstance3D{CastShadow=GeometryInstance3D.ShadowCastingSetting.Off};
            AddChild(_ghost);
        }
        if(action=="decoration"&&_decorationFootprint==null){
            _decorationFootprint=new MeshInstance3D{
                Mesh=new BoxMesh{Size=new Vector3(WorldData.GridSize*.9f,.04f,WorldData.GridSize*.9f)},
                CastShadow=GeometryInstance3D.ShadowCastingSetting.Off
            };
            AddChild(_decorationFootprint);
            _decorationFootprint.Hide();
        }
    }

    void EndPlacement()
    {
        _placementAction="";
        _ghost?.Hide();
        _decorationFootprint?.Hide();
        _placementGuide?.Hide();
        _placementGuideSignature=null;
        CancelRoadStroke();
    }

    void CancelRoadStroke()
    {
        _strokeActive=false;
        _strokeCells.Clear();
        _strokeSignature="";
        _strokePreview?.QueueFree();
        _strokePreview=null;
    }

    void BeginRoadStroke(Vector2 screen)
    {
        CancelRoadStroke();
        if(!TryPlacementCell(screen,out var cell)){
            _selection="请从地图内的地块开始拖拽。";
            return;
        }
        _strokeActive=true;
        SampleStroke(cell);
    }

    public override void _Input(InputEvent input)
    {
        if(HandleScenePickChooserInput(input)){GetViewport().SetInputAsHandled();return;}
        if(ContinueTownPointer(input)){GetViewport().SetInputAsHandled();return;}
        if(!_strokeActive)return;
        if(input is InputEventMouseMotion motion){
            if((motion.ButtonMask&MouseButtonMask.Left)==0)CancelRoadStroke();
            else if(TryPlacementCell(motion.Position,out var cell))SampleStroke(cell);
            return;
        }
        if(input is not InputEventMouseButton { ButtonIndex:MouseButton.Left, Pressed:false } release)return;
        var viewport=GetViewport();
        viewport.SetInputAsHandled();
        if(viewport.GuiGetHoveredControl()!=null||!TryPlacementCell(release.Position,out var endpoint)){
            CancelRoadStroke();
            _selection="终点不在可操作的地图区域，已取消道路笔画。";
            return;
        }
        SampleStroke(endpoint);
        var args=new JsonObject{
            {"cells",new JsonArray(_strokeCells.Select(c=>(JsonNode)new JsonArray(c.X,c.Y)).ToArray())},
            {"style",NativeGame.S(_placementArgs,"style","asphalt")},
            {"remove",_placementAction=="remove_road"}
        };
        bool hasCells=_strokeCells.Count>0;
        CancelRoadStroke();
        if(hasCells)Act("road_stroke",args);
        else _selection="目标已接通或没有可用路线。";
    }

    void SampleStroke(Vector2I current)
    {
        if(!IsMapCell(current))return;
        var target=new NativeGame.Cell(current.X,current.Y);
        if(NativeGame.B(_placementArgs,"auto_connect")){
            _strokeCells.Clear();
            _strokeCells.AddRange(Game.AutoRoadPlan(target));
            return;
        }
        if(_strokeCells.Count==0)_strokeCells.Add(target);
        else if(_strokeCells[^1]!=target)
            foreach(var c in NativeGame.RoadLine(_strokeCells[^1],target))if(!_strokeCells.Contains(c))_strokeCells.Add(c);
    }

    static bool IsMapCell(Vector2I cell)=>cell.X>=0&&cell.Y>=0&&cell.X<WorldData.WorldWidth/WorldData.GridSize&&cell.Y<WorldData.WorldDepth/WorldData.GridSize;

    bool TryPlacementCell(Vector2 screen,out Vector2I cell)
    {
        cell=default;
        if(!GetViewport().GetVisibleRect().HasPoint(screen))return false;
        var ray=_camera.ProjectRayNormal(screen);
        var origin=_camera.ProjectRayOrigin(screen);
        if(ray.Y>=-.00001f||origin.Y<=0)return false;
        var point=origin+ray*(-origin.Y/ray.Y);
        if(!(point.X>=0&&point.Z>=0&&point.X<WorldData.WorldWidth&&point.Z<WorldData.WorldDepth))return false;
        cell=WorldData.Grid(point);
        return true;
    }

    void CommitPlacement(Vector2 screen)
    {
        if(!TryPlacementCell(screen,out var cell)){
            _selection="请在地图内选择位置。";
            return;
        }
        var args=_placementArgs.DeepClone().AsObject();
        if(_placementAction=="build_town")cell=AssistedTownPlacement(cell).Cell;
        args["cx"]=cell.X;
        args["cy"]=cell.Y;
        bool moving=_placementAction=="decoration"&&NativeGame.S(args,"operation")=="move";
        long revision=Game.WorldRevision;
        Act(_placementAction,args);
        if(moving&&Game.WorldRevision!=revision)EndPlacement();
    }

    void UpdatePlacement()
    {
        if(_placementAction==""||_ghost==null)return;
        if(!TryPlacementCell(GetViewport().GetMousePosition(),out var cell)){
            _ghost.Hide();
            _decorationFootprint?.Hide();
            _placementGuide?.Hide();
            _strokePreview?.Hide();
            _selection=_strokeActive?"指针已离开地图。在地图外松开左键会取消施工。":"将鼠标移到地图内查看预览。";
            return;
        }
        if(_placementAction is "road" or "remove_road"){
            _ghost.Hide();
            var cells=_strokeActive?_strokeCells:NativeGame.B(_placementArgs,"auto_connect")?Game.AutoRoadPlan(new(cell.X,cell.Y)):new List<NativeGame.Cell>{new(cell.X,cell.Y)};
            string style=NativeGame.S(_placementArgs,"style","asphalt");bool remove=_placementAction=="remove_road";var quote=Game.RoadQuote(cells,style,remove);
            _selection=$"道路预览{cells.Count}格 · {(quote.Cost<0?"返还":"费用")}{Math.Abs(quote.Cost)}金币 · {(quote.Reason==""?cells.Count==0?"目标已接通或无可用路线":"松开左键施工":quote.Reason)}";
            string signature=style+remove+quote.Reason+string.Join(';',cells);
            UpdatePlacementGuide(cell,1,1,(cell,signature),cells.Where(c=>Game.RoadReason(c,style,remove)!=""));
            _strokePreview?.Show();
            if(signature==_strokeSignature)return;_strokeSignature=signature;_strokePreview?.QueueFree();_strokePreview=new Node3D();AddChild(_strokePreview);
            var pending=cells.ToHashSet();foreach(var c in cells.Take(500)){int mask=0,bit=0;foreach(var d in new[]{Vector2I.Up,Vector2I.Right,Vector2I.Down,Vector2I.Left}){if(pending.Contains(new(c.X+d.X,c.Y+d.Y))||Data.Roads.ContainsKey(new(c.X+d.X,c.Y+d.Y)))mask|=1<<bit;bit++;}
                _strokePreview.AddChild(new MeshInstance3D{Mesh=RoadMesh(mask,style),Position=WorldData.Cell(new(c.X,c.Y))+Vector3.Up*.12f,MaterialOverride=quote.Reason==""?_ghostGood:_ghostBad,CastShadow=GeometryInstance3D.ShadowCastingSetting.Off});}
        }
        else if(_placementAction=="decoration"){
            UpdateDecorationPlacement(cell);
        }
        else {
            var guide=AssistedTownPlacement(cell);
            cell=guide.Cell;
            string key=NativeGame.S(_placementArgs,"catalog_key");int rotation=NativeGame.I(_placementArgs,"orientation");
            var spec=Game.Rules["CATALOG"]![key]!;
            int w=guide.Width,h=guide.Depth,cost=NativeGame.I(spec,"cost");
            string reason=Game.PlacementReason(key,cell.X,cell.Y,rotation);
            if(reason==""&&NativeGame.I(Game.Meta,"coins")<cost)reason="金币不足";
            _ghost.Mesh=Prototype("town:"+key+":4");_ghost.Scale=new(WorldData.LayoutScale,1,WorldData.LayoutScale);
            _ghost.Position=new((cell.X+w*.5f)*WorldData.GridSize,.05f,(cell.Y+h*.5f)*WorldData.GridSize);
            _ghost.Rotation=new(0,-rotation*Mathf.Pi/2,0);_ghost.MaterialOverride=reason==""?_ghostGood:_ghostBad;
            _ghost.Show();
            var direction=NativeGame.EntranceDirection(rotation);
            UpdatePlacementGuide(cell,w,h,(_placementAction,key,guide),BuildingPlacementConflicts(guide),new(direction.X,direction.Y),guide.FrontRoad);
            string entrance=guide.Snapped?"已吸附到路边":guide.FrontRoad?"正门连通道路":"箭头指向正门";
            _selection=$"{NativeGame.S(spec,"name")} · 占地 {w}×{h} 格 · 费用 {cost} 金币\n{entrance} · {(reason==""?"左键开工":reason)} · {TownBindingText("rotate")} 旋转 · Esc 退出";
        }
    }

    void UpdateDecorationPlacement(Vector2I cell)
    {
        string key=NativeGame.S(_placementArgs,"kind");
        int rotation=NativeGame.I(_placementArgs,"orientation");
        bool moving=NativeGame.S(_placementArgs,"operation")=="move";
        var quote=Game.DecorationQuote(key,cell.X,cell.Y,rotation,moving?NativeGame.S(_placementArgs,"id"):null);
        var material=quote.Reason==""?_ghostGood:_ghostBad;
        var center=WorldData.Cell(cell);
        _ghost!.Mesh=Prototype("decoration:"+key);
        _ghost.Scale=new Vector3(WorldData.LayoutScale,1,WorldData.LayoutScale)*(key.StartsWith("tree_")?.8f:1);
        _ghost.Position=center+Vector3.Up*.05f;
        _ghost.Rotation=new(0,-rotation*Mathf.Pi/2,0);
        _ghost.MaterialOverride=material;
        _ghost.Show();
        _decorationFootprint!.Position=center+Vector3.Up*.025f;
        _decorationFootprint.MaterialOverride=material;
        _decorationFootprint.Show();
        UpdatePlacementGuide(cell,1,1,(_placementAction,key,cell,quote.Reason),
            quote.Reason.Length>0&&quote.Reason!="金币不足"?new[]{new NativeGame.Cell(cell.X,cell.Y)}:Array.Empty<NativeGame.Cell>());
        string name=Game.Rules["DECORATIONS"]![key]![0]!.GetValue<string>();
        string cost=moving?"移动免费":$"费用 {quote.Cost} 金币";
        string next=quote.Reason!=""?quote.Reason:moving?"左键确认移动":"左键摆放";
        _selection=$"{name} · ({cell.X}, {cell.Y}) · {BuildOrientations[rotation]} · {cost}\n{next} · {TownBindingText("rotate")} 旋转 · Esc 退出";
    }
}
