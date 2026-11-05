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
    Node3D? _strokePreview;
    readonly List<NativeGame.Cell> _strokeCells=new();
    bool _strokeActive;
    string _strokeSignature="";
    readonly StandardMaterial3D _ghostGood=new(){AlbedoColor=new(.25f,.9f,.55f,.5f),Transparency=BaseMaterial3D.TransparencyEnum.Alpha,ShadingMode=BaseMaterial3D.ShadingModeEnum.Unshaded};
    readonly StandardMaterial3D _ghostBad=new(){AlbedoColor=new(.95f,.3f,.25f,.5f),Transparency=BaseMaterial3D.TransparencyEnum.Alpha,ShadingMode=BaseMaterial3D.ShadingModeEnum.Unshaded};
    void ArmPlacement(string action,JsonObject args){CancelRoadStroke();_placementAction=action;_placementArgs=args;_management?.Hide();_selection=action is "road" or "remove_road"?"按住左键拖拽，松开提交整笔；Ctrl+Z撤销，Esc退出。":"移动鼠标预览，左键施工，R旋转，Esc退出。";if(_ghost==null){_ghost=new MeshInstance3D{CastShadow=GeometryInstance3D.ShadowCastingSetting.Off};AddChild(_ghost);}_ghost.Show();}
    void CancelRoadStroke(){_strokeActive=false;_strokeCells.Clear();_strokeSignature="";_strokePreview?.QueueFree();_strokePreview=null;}
    void BeginRoadStroke(Vector2 screen){_strokeActive=true;_strokeCells.Clear();var c=PlacementCell(screen);_strokeCells.Add(new(c.X,c.Y));}
    public override void _Input(InputEvent input)
    {
        if(input is InputEventMouseMotion motion&&_strokeActive)SampleStroke(PlacementCell(motion.Position));
        if(input is InputEventMouseButton b&&b.ButtonIndex==MouseButton.Left&&!b.Pressed&&_strokeActive){
            if(GetViewport().GuiGetHoveredControl()!=null||!GetViewport().GetVisibleRect().HasPoint(b.Position)){CancelRoadStroke();_selection="已取消道路笔画";return;}
            SampleStroke(PlacementCell(b.Position));var args=new JsonObject{{"cells",new JsonArray(_strokeCells.Select(c=>(JsonNode)new JsonArray(c.X,c.Y)).ToArray())},{"style",NativeGame.S(_placementArgs,"style","asphalt")},{"remove",_placementAction=="remove_road"}};CancelRoadStroke();Act("road_stroke",args);GetViewport().SetInputAsHandled();
        }
    }
    void SampleStroke(Vector2I current)
    {
        if(NativeGame.B(_placementArgs,"auto_connect")){_strokeCells.Clear();_strokeCells.AddRange(Game.AutoRoadPlan(new(current.X,current.Y)));return;}
        var target=new NativeGame.Cell(Math.Clamp(current.X,0,124),Math.Clamp(current.Y,0,79));
        if(_strokeCells.Count==0)_strokeCells.Add(target);else foreach(var c in NativeGame.RoadLine(_strokeCells[^1],target))if(!_strokeCells.Contains(c))_strokeCells.Add(c);
    }
    Vector2I PlacementCell(Vector2 screen){var ray=_camera.ProjectRayNormal(screen);var origin=_camera.ProjectRayOrigin(screen);if(ray.Y>=-.00001f)return new(-1,-1);return WorldData.Grid(origin+ray*(-origin.Y/ray.Y));}
    void UpdatePlacement()
    {
        if(_placementAction==""||_ghost==null)return;var cell=PlacementCell(GetViewport().GetMousePosition());
        if(_placementAction is "road" or "remove_road"){
            _ghost.Hide();
            var cells=_strokeActive?_strokeCells:NativeGame.B(_placementArgs,"auto_connect")?Game.AutoRoadPlan(new(cell.X,cell.Y)):new List<NativeGame.Cell>{new(cell.X,cell.Y)};
            string style=NativeGame.S(_placementArgs,"style","asphalt");bool remove=_placementAction=="remove_road";var quote=Game.RoadQuote(cells,style,remove);
            string signature=style+remove+quote.Reason+string.Join(';',cells);if(signature==_strokeSignature)return;_strokeSignature=signature;_strokePreview?.QueueFree();_strokePreview=new Node3D();AddChild(_strokePreview);
            var pending=cells.ToHashSet();foreach(var c in cells.Take(500)){int mask=0,bit=0;foreach(var d in new[]{Vector2I.Up,Vector2I.Right,Vector2I.Down,Vector2I.Left}){if(pending.Contains(new(c.X+d.X,c.Y+d.Y))||Data.Roads.ContainsKey(new(c.X+d.X,c.Y+d.Y)))mask|=1<<bit;bit++;}
                _strokePreview.AddChild(new MeshInstance3D{Mesh=RoadMesh(mask,style),Position=WorldData.Cell(new(c.X,c.Y))+Vector3.Up*.12f,MaterialOverride=quote.Reason==""?_ghostGood:_ghostBad,CastShadow=GeometryInstance3D.ShadowCastingSetting.Off});}
            _selection=$"道路预览{cells.Count}格 · {(quote.Cost<0?"返还":"费用")}{Math.Abs(quote.Cost)}金币 · {(quote.Reason==""?cells.Count==0?"目标已接通或无可用路线":"松开左键施工":quote.Reason)}";
        }
        else{string key=NativeGame.S(_placementArgs,"catalog_key");int rotation=NativeGame.I(_placementArgs,"orientation");var size=Game.Rules["CATALOG"]![key]!["footprint"]!;int w=size[rotation%2]!.GetValue<int>(),h=size[1-rotation%2]!.GetValue<int>();_ghost.Mesh=Prototype("town:"+key+":4");_ghost.Scale=new(WorldData.LayoutScale,1,WorldData.LayoutScale);_ghost.Position=new((cell.X+w*.5f)*WorldData.GridSize,.05f,(cell.Y+h*.5f)*WorldData.GridSize);_ghost.Rotation=new(0,-rotation*Mathf.Pi/2,0);_ghost.MaterialOverride=Game.PlacementReason(key,cell.X,cell.Y,rotation)==""?_ghostGood:_ghostBad;}
    }
}
