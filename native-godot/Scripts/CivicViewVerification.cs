using Godot;
using System;
using System.Linq;

public partial class Main
{
    void VerifyResidentMapAndOutfits(RoadAgent first,Action<bool,string> check)
    {
        var other=Agents.First(actor=>actor!=first&&!actor.IsCar&&DirectoryPerson(actor.Ident) is { } person&&NativeGame.S(person,"status")!="离镇");
        var positions=(first.Position,other.Position,other.InsideHome,other.Visual.Visible);
        var map=new TownMap{Town=this,Size=new(620,400),ShowConstruction=false,ShowFreight=false,ShowDisconnected=false};
        AddChild(map);
        try{
            first.Position=other.Position=new(WorldData.WorldWidth-60,0,WorldData.WorldDepth-60);
            other.InsideHome=false;
            other.Visual.Show();
            map.SetResidentLayer(true);
            MapResident[] chosen=Array.Empty<MapResident>();
            map.ResidentsSelected+=entries=>chosen=entries;
            Click(map.WorldToView(new(first.Position.X,first.Position.Z)));
            check(chosen.Any(entry=>entry.Id==first.Ident)&&chosen.Any(entry=>entry.Id==other.Ident),"地图没有合并附近村民并显示准确名单");
            map.SetResidentFilter("不存在的职业","");
            Click(map.WorldToView(new(first.Position.X,first.Position.Z)));
            check(chosen.Length==0,"村民地图职业筛选没有隐藏不匹配的人物");
        }finally{
            (first.Position,other.Position,other.InsideHome,other.Visual.Visible)=positions;
            RemoveChild(map);
            map.QueueFree();
        }

        var farmer=ResidentOutfitPieces("农民")!;
        var driver=ResidentOutfitPieces("司机")!;
        check(farmer.GetAabb().Size.X>driver.GetAabb().Size.X&&ResidentOutfitBody("农民",0,0)!=ResidentOutfitBody("司机",0,0),
            "不同职业没有独立服装配色和轮廓");
        var obstruction=new MeshInstance3D{Mesh=new BoxMesh{Size=new(2,2,2)},MaterialOverride=_material};
        AddChild(obstruction);
        try{
            check(BlocksActorRay(obstruction,new Vector3(-3,0,0),new Vector3(3,0,0))&&!BlocksActorRay(obstruction,new Vector3(-3,3,0),new Vector3(3,3,0)),
                "遮挡检测没有区分挡住人物与远离视线的对象");
            FadeActorOccluder(obstruction);
            check(obstruction.MaterialOverride!=_material,"遮挡物没有切换至局部淡化材质");
            RestoreActorOccluder(obstruction);
            check(obstruction.MaterialOverride==_material,"离开视线后没有恢复原材质");
        }finally{
            RestoreActorOccluder(obstruction);
            RemoveChild(obstruction);
            obstruction.QueueFree();
        }
        GD.Print("CIVIC_VIEW_VERIFIED resident_follow clustered_map profession_outfits obstruction_restore");

        void Click(Vector2 point){
            map._GuiInput(new InputEventMouseButton{Position=point,ButtonIndex=MouseButton.Left,Pressed=true});
            map._GuiInput(new InputEventMouseButton{Position=point,ButtonIndex=MouseButton.Left});
        }
    }
}
