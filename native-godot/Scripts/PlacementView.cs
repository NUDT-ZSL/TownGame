using Godot;
using System.Text.Json.Nodes;

public partial class Main
{
    string _placementAction="";
    JsonObject _placementArgs=new();
    MeshInstance3D? _ghost;
    readonly StandardMaterial3D _ghostGood=new(){AlbedoColor=new(.25f,.9f,.55f,.5f),Transparency=BaseMaterial3D.TransparencyEnum.Alpha,ShadingMode=BaseMaterial3D.ShadingModeEnum.Unshaded};
    readonly StandardMaterial3D _ghostBad=new(){AlbedoColor=new(.95f,.3f,.25f,.5f),Transparency=BaseMaterial3D.TransparencyEnum.Alpha,ShadingMode=BaseMaterial3D.ShadingModeEnum.Unshaded};
    void ArmPlacement(string action,JsonObject args){_placementAction=action;_placementArgs=args;_management?.Hide();_selection="移动鼠标预览，左键施工，Esc退出。";if(_ghost==null){_ghost=new MeshInstance3D{CastShadow=GeometryInstance3D.ShadowCastingSetting.Off};AddChild(_ghost);}_ghost.Show();}
    Vector2I PlacementCell(Vector2 screen){var ray=_camera.ProjectRayNormal(screen);var origin=_camera.ProjectRayOrigin(screen);if(ray.Y>=-.00001f)return new(-1,-1);return WorldData.Grid(origin+ray*(-origin.Y/ray.Y));}
    void UpdatePlacement()
    {
        if(_placementAction==""||_ghost==null)return;var cell=PlacementCell(GetViewport().GetMousePosition());
        if(_placementAction=="road"){_ghost.Mesh=RoadMesh(0,NativeGame.S(_placementArgs,"style"));_ghost.Scale=Vector3.One;_ghost.Position=WorldData.Cell(cell)+Vector3.Up*.05f;_ghost.Rotation=Vector3.Zero;_ghost.MaterialOverride=cell.X>=0&&cell.Y>=0&&cell.X<125&&cell.Y<80?_ghostGood:_ghostBad;}
        else{string key=NativeGame.S(_placementArgs,"catalog_key");int rotation=NativeGame.I(_placementArgs,"orientation");var size=Game.Rules["CATALOG"]![key]!["footprint"]!;int w=size[rotation%2]!.GetValue<int>(),h=size[1-rotation%2]!.GetValue<int>();_ghost.Mesh=Prototype("town:"+key+":4");_ghost.Scale=new(WorldData.LayoutScale,1,WorldData.LayoutScale);_ghost.Position=new((cell.X+w*.5f)*WorldData.GridSize,.05f,(cell.Y+h*.5f)*WorldData.GridSize);_ghost.Rotation=new(0,-rotation*Mathf.Pi/2,0);_ghost.MaterialOverride=Game.PlacementReason(key,cell.X,cell.Y,rotation)==""?_ghostGood:_ghostBad;}
    }
}
