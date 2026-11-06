using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

public sealed partial class NativeGame
{
    internal bool BuildCellConflicts(Cell cell)=>!Allowed(cell)||RoadCells().Contains(cell);

    internal static Cell EntranceDirection(int orientation)=>orientation switch{
        0=>new(0,1),1=>new(-1,0),2=>new(0,-1),_=>new(1,0)
    };

    internal bool HasFrontRoad(Cell origin,int width,int depth,int orientation)
    {
        var linked=Connected();
        var direction=EntranceDirection(orientation);
        if(direction.X==0){
            int row=origin.Y+(direction.Y>0?depth:-1);
            return Enumerable.Range(origin.X,width).Any(x=>linked.Contains(new(x,row)));
        }
        int column=origin.X+(direction.X>0?width:-1);
        return Enumerable.Range(origin.Y,depth).Any(y=>linked.Contains(new(column,y)));
    }
}

public partial class Main
{
    readonly record struct TownPlacementGuide(Vector2I Cell,int Width,int Depth,int Rotation,bool Snapped,bool FrontRoad);
    bool _snapPlacementToRoad=true;
    MeshInstance3D? _placementGuide;
    object? _placementGuideSignature;

    TownPlacementGuide AssistedTownPlacement(Vector2I raw)
    {
        string key=NativeGame.S(_placementArgs,"catalog_key");
        int orientation=NativeGame.I(_placementArgs,"orientation");
        var footprint=Game.Spec("CATALOG",key)["footprint"]!;
        int width=footprint[orientation%2]!.GetValue<int>(),depth=footprint[1-orientation%2]!.GetValue<int>();
        bool FrontRoad(Vector2I origin)=>Game.HasFrontRoad(new(origin.X,origin.Y),width,depth,orientation);
        if(FrontRoad(raw))return new(raw,width,depth,orientation,false,true);
        if(_snapPlacementToRoad){
            var forward=NativeGame.EntranceDirection(orientation);
            var front=new Vector2I(forward.X,forward.Y);
            var side=new Vector2I(-front.Y,front.X);
            foreach(var shift in new[]{front,-front,side,-side}){
                var proposed=raw+shift;
                if(IsMapCell(proposed)&&FrontRoad(proposed)&&Game.PlacementReason(key,proposed.X,proposed.Y,orientation)=="")
                    return new(proposed,width,depth,orientation,true,true);
            }
        }
        return new(raw,width,depth,orientation,false,false);
    }

    void UpdatePlacementGuide(Vector2I origin,int width,int depth,object signature,IEnumerable<NativeGame.Cell> conflicts,Vector2I? facing=null,bool frontRoad=false)
    {
        var key=(signature,Game.WorldRevision,Data.Snapshot);
        if(_placementGuide!=null&&key.Equals(_placementGuideSignature)){_placementGuide.Show();return;}
        _placementGuideSignature=key;
        if(_placementGuide==null){
            _placementGuide=new MeshInstance3D{Name="TownPlacementGuide",Mesh=new ImmediateMesh(),
                MaterialOverride=new StandardMaterial3D{VertexColorUseAsAlbedo=true,ShadingMode=BaseMaterial3D.ShadingModeEnum.Unshaded,
                    Transparency=BaseMaterial3D.TransparencyEnum.Alpha,CullMode=BaseMaterial3D.CullModeEnum.Disabled},
                CastShadow=GeometryInstance3D.ShadowCastingSetting.Off};
            AddChild(_placementGuide);
        }
        _placementGuide.Show();
        var mesh=(ImmediateMesh)_placementGuide.Mesh;
        mesh.ClearSurfaces();
        const float grid=WorldData.GridSize;
        int left=Math.Max(0,origin.X-3),top=Math.Max(0,origin.Y-3);
        int right=Math.Min((int)(WorldData.WorldWidth/grid),origin.X+width+3),bottom=Math.Min((int)(WorldData.WorldDepth/grid),origin.Y+depth+3);
        mesh.SurfaceBegin(Mesh.PrimitiveType.Lines);
        var gridColor=new Color("fff5dcb0");
        for(int x=left;x<=right;x++)Line(new(x*grid,.17f,top*grid),new(x*grid,.17f,bottom*grid),gridColor);
        for(int y=top;y<=bottom;y++)Line(new(left*grid,.17f,y*grid),new(right*grid,.17f,y*grid),gridColor);
        var edgeColor=new Color("ffe08cf5");
        var tl=new Vector3(origin.X*grid,.23f,origin.Y*grid);
        var br=new Vector3((origin.X+width)*grid,.23f,(origin.Y+depth)*grid);
        var tr=new Vector3(br.X,tl.Y,tl.Z);
        var bl=new Vector3(tl.X,tl.Y,br.Z);
        Line(tl,tr,edgeColor);Line(tr,br,edgeColor);Line(br,bl,edgeColor);Line(bl,tl,edgeColor);
        mesh.SurfaceEnd();
        var blocked=conflicts.ToArray();
        if(blocked.Length==0&&facing==null)return;
        mesh.SurfaceBegin(Mesh.PrimitiveType.Triangles);
        foreach(var cell in blocked){
            var center=WorldData.Cell(new(cell.X,cell.Y))+Vector3.Up*.28f;
            float half=grid*.46f;
            var a=center+new Vector3(-half,0,-half);var b=center+new Vector3(half,0,-half);
            var c=center+new Vector3(half,0,half);var d=center+new Vector3(-half,0,half);
            Triangle(a,b,c,new Color("d5423c8c"));Triangle(a,c,d,new Color("d5423c8c"));
        }
        if(facing is Vector2I direction){
            var forward=new Vector3(direction.X,0,direction.Y);
            var across=new Vector3(-direction.Y,0,direction.X);
            var middle=(tl+br)/2+forward*((direction.X!=0?width:depth)*grid/2)+Vector3.Up*.36f;
            var point=middle+forward*(grid*.74f);
            var wing=middle+forward*(grid*.4f);
            Triangle(point,wing-across*(grid*.26f),wing+across*(grid*.26f),new Color(frontRoad?"4fb894":"ffbd65"));
            Triangle(middle-across*.58f,middle+across*.58f,wing+across*.58f,new Color(frontRoad?"4fb894":"ffbd65"));
            Triangle(middle-across*.58f,wing+across*.58f,wing-across*.58f,new Color(frontRoad?"4fb894":"ffbd65"));
        }
        mesh.SurfaceEnd();

        void Line(Vector3 a,Vector3 b,Color ink){Vertex(a,ink);Vertex(b,ink);}
        void Triangle(Vector3 a,Vector3 b,Vector3 c,Color ink){Vertex(a,ink);Vertex(b,ink);Vertex(c,ink);}
        void Vertex(Vector3 point,Color ink){mesh.SurfaceSetColor(ink);mesh.SurfaceAddVertex(point);}
    }

    IEnumerable<NativeGame.Cell> BuildingPlacementConflicts(TownPlacementGuide guide)
    {
        for(int x=0;x<guide.Width;x++)for(int y=0;y<guide.Depth;y++){
            var cell=new NativeGame.Cell(guide.Cell.X+x,guide.Cell.Y+y);
            if(Game.BuildCellConflicts(cell))yield return cell;
        }
    }
}
