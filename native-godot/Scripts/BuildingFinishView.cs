using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;

internal static class BuildingFinish
{
    public static readonly (string Name,string Hex)[] Walls={
        ("原有外墙",""),("燕麦奶白","ead9b8"),("晨雾淡蓝","b4cbd2"),
        ("暖杏陶土","d9a78b"),("浅苔绿","b8c3a0"),
    };
    public static readonly (string Name,string Hex)[] Roofs={
        ("原有屋顶",""),("莓果红瓦","a95a61"),("池畔青瓦","577f79"),
        ("暮蓝灰瓦","566b88"),("晒谷蜜棕","b28550"),
    };

    public static (int Wall,int Roof) Read(JsonNode row)=>(ReadIndex(row,"paint_wall",Walls.Length),ReadIndex(row,"paint_roof",Roofs.Length));

    static bool HasFinish(JsonNode? row)=>row?["paint_wall"]!=null||row?["paint_roof"]!=null;

    public static string ObstacleSignature(JsonArray rows)
    {
        if(!rows.Any(HasFinish))return rows.ToJsonString();
        // Preserve the existing layout comparison when a building only changes color.
        return "["+string.Join(",",rows.Select(row=>{
            if(!HasFinish(row))return row?.ToJsonString()??"null";
            var footprint=row!.DeepClone().AsObject();
            footprint.Remove("paint_wall");footprint.Remove("paint_roof");
            return footprint.ToJsonString();
        }))+"]";
    }

    static int ReadIndex(JsonNode row,string key,int count)=>row[key] is JsonValue field&&field.TryGetValue<int>(out int index)
        ?Math.Clamp(index,0,count-1):0;
}

public sealed partial class NativeGame
{
    public bool CanPaintBuilding(JsonNode building,bool town)=>town
        ?S(building,"status")=="ready"&&S(Spec("CATALOG",S(building,"catalog_key")),"family")!="public"
        :B(building,"unlocked")&&S(building,"kind") is not ("" or "游乐区");

    string PaintBuilding(JsonObject args)
    {
        bool town=B(args,"town");
        var building=town?FindTownBuilding(S(args,"building_id")):
            Rows("buildings").FirstOrDefault(row=>I(row!,"building_id")==I(args,"building_id",-1));
        if(building==null||!CanPaintBuilding(building,town))throw new InvalidOperationException("请选择一栋可换色的已建建筑");
        if(args["wall"] is not JsonValue wallValue||!wallValue.TryGetValue<int>(out int wall)||wall<0||wall>=BuildingFinish.Walls.Length||
            args["roof"] is not JsonValue roofValue||!roofValue.TryGetValue<int>(out int roof)||roof<0||roof>=BuildingFinish.Roofs.Length)
            throw new InvalidOperationException("请选择有效的屋顶和外墙颜色");
        if(BuildingFinish.Read(building)==(wall,roof))return "建筑外观已是所选配色";
        if(wall==0)building.AsObject().Remove("paint_wall");else building["paint_wall"]=wall;
        if(roof==0)building.AsObject().Remove("paint_roof");else building["paint_roof"]=roof;
        WorldRevision++;
        return "建筑配色已更新";
    }
}

public partial class Main
{
    readonly Dictionary<(ArrayMesh Source,int Wall,int Roof),ArrayMesh> _buildingFinishMeshes=new();

    Mesh BuildingFinishMesh(Mesh source,JsonNode building)
    {
        var (wall,roof)=BuildingFinish.Read(building);
        if((wall,roof)==(0,0)||source is not ArrayMesh mesh)return source;
        if(_buildingFinishMeshes.TryGetValue((mesh,wall,roof),out var cached))return cached;
        var arrays=mesh.SurfaceGetArrays(0);
        var positions=arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
        var normals=arrays[(int)Mesh.ArrayType.Normal].AsVector3Array();
        var colors=arrays[(int)Mesh.ArrayType.Color].AsColorArray();
        var wallInk=wall==0?default:new Color(BuildingFinish.Walls[wall].Hex);
        var roofInk=roof==0?default:new Color(BuildingFinish.Roofs[roof].Hex);
        float roofWidth=NativeGame.S(building,"kind")=="牧场"?.95f:1.9f;
        var wallFaces=new bool[positions.Length/3];
        float wallTop=0;
        // Main wall planes run upward from the foundation. Their top height identifies the roof and gables.
        for(int i=0;i+2<positions.Length;i+=3){
            var low=positions[i].Min(positions[i+1]).Min(positions[i+2]);
            var high=positions[i].Max(positions[i+1]).Max(positions[i+2]);
            bool isWall=Math.Abs(normals[i].Y)<.04f&&low.Y<2f&&high.Y-low.Y>2.25f&&
                Math.Max(high.X-low.X,high.Z-low.Z)>1.9f;
            if(!isWall)continue;
            wallFaces[i/3]=true;
            wallTop=Math.Max(wallTop,high.Y);
        }
        for(int i=0;i+2<positions.Length;i+=3){
            var low=positions[i].Min(positions[i+1]).Min(positions[i+2]);
            var high=positions[i].Max(positions[i+1]).Max(positions[i+2]);
            bool isRoof=wallTop>0&&low.Y>=wallTop-.18f&&normals[i].Y>-.05f&&
                Math.Max(high.X-low.X,high.Z-low.Z)>roofWidth&&
                (high.Y-low.Y>.32f||normals[i].Y>.95f&&low.Y>wallTop+.10f);
            if(wall>0&&wallFaces[i/3])for(int vertex=i;vertex<i+3;vertex++)colors[vertex]=colors[vertex].Lerp(wallInk,.90f);
            else if(roof>0&&isRoof)for(int vertex=i;vertex<i+3;vertex++)colors[vertex]=colors[vertex].Lerp(roofInk,.93f);
        }
        arrays[(int)Mesh.ArrayType.Color]=colors;
        var result=new ArrayMesh();
        result.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles,arrays);
        result.SurfaceSetMaterial(0,mesh.SurfaceGetMaterial(0));
        return _buildingFinishMeshes[(mesh,wall,roof)]=result;
    }

    Action AddBuildingFinishControls(Container parent,Func<JsonNode?> selected,bool town)
    {
        var body=BuildCard(parent,"屋顶与外墙配色");
        var card=body.GetParent<Control>();
        card.Name="BuildingFinishControls";
        BuildText(body,"自由搭配外墙和屋顶，保存后沿用所选颜色。");
        var row=BuildOperations(body);
        var wall=BuildChoice(row,BuildingFinish.Walls.Select(item=>item.Name),0);
        wall.Name="BuildingFinishWall";
        var roof=BuildChoice(row,BuildingFinish.Roofs.Select(item=>item.Name),0);
        roof.Name="BuildingFinishRoof";
        var wallSwatch=FinishSwatch(wall,BuildingFinish.Walls);
        var roofSwatch=FinishSwatch(roof,BuildingFinish.Roofs);
        var apply=BuildButton(row,"应用配色",()=>{
            if(selected() is not JsonNode building)return;
            Act("paint_building",new JsonObject{{"town",town},{"building_id",building["building_id"]!.DeepClone()},
                {"wall",wall.Selected},{"roof",roof.Selected}});
        });
        apply.Name="ApplyBuildingFinish";
        (string Id,int Wall,int Roof)? shown=null;
        void Update()
        {
            if(selected() is not JsonNode building||!Game.CanPaintBuilding(building,town)){
                card.Hide();shown=null;return;
            }
            card.Show();
            var saved=BuildingFinish.Read(building);
            var key=(building["building_id"]!.ToString(),saved.Wall,saved.Roof);
            if(shown==key)return;
            shown=key;
            wall.Select(saved.Wall);roof.Select(saved.Roof);
            wallSwatch();roofSwatch();
        }
        Update();
        return Update;

        Action FinishSwatch(OptionButton option,(string Name,string Hex)[] colors)
        {
            var swatch=new ColorRect{CustomMinimumSize=new(20,19),SizeFlagsVertical=Control.SizeFlags.ShrinkCenter,
                MouseFilter=Control.MouseFilterEnum.Ignore};
            row.AddChild(swatch);
            row.MoveChild(swatch,option.GetIndex()+1);
            void UpdateSwatch()=>swatch.Color=new Color(colors[option.Selected].Hex is {Length:>0} hex?hex:"d5c9ae");
            option.ItemSelected+=_=>UpdateSwatch();
            return UpdateSwatch;
        }
    }
}
