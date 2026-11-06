using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

public partial class Main
{
    // The courtyard sits between the last estate footpath and the outer fence.
    static readonly Vector3 PetActivityCenter=new(266,0,211);
    static readonly Dictionary<string,(Vector3 Offset,float Scale,string Label,string Action,string Help)> PetActivityStations=new()
    {
        ["bowl"]=(new(-14,0,-1.2f),.55f,"食盆","feed","用一份普通饲料喂食"),
        ["toy"]=(new(0,0,-1.2f),.68f,"玩具","play","与伙伴互动，提升心情与好感"),
        ["bath"]=(new(14,0,-1.2f),.55f,"浴盆","bath","用清水与沐浴露各一份洗澡"),
    };

    static void PositionPetActivityProp(MeshInstance3D prop,string id)
    {
        if(!PetActivityStations.TryGetValue(id,out var station))return;
        prop.Position=PetActivityCenter+station.Offset;
        prop.Scale*=station.Scale;
        var name=new Label3D{
            Text=station.Label,Position=Vector3.Up*(prop.Mesh.GetAabb().End.Y+.65f),
            Billboard=BaseMaterial3D.BillboardModeEnum.Enabled,
            Font=new SystemFont{FontNames=new[]{"Microsoft YaHei","Noto Sans CJK SC"}},
            FontSize=38,PixelSize=.021f,OutlineSize=5,
            Modulate=new Color("fff6dc"),OutlineModulate=new Color("4d594b")
        };
        prop.AddChild(name);
    }

    void OpenPetActivityStation(string id)
    {
        if(!PetActivityStations.TryGetValue(id,out var station)){ShowManagement("环境");return;}
        Vector3 location=PetActivityCenter+station.Offset;
        var pet=FarmPet(_selectedPet)??GetChildren().OfType<PetAgent>().Where(actor=>!actor.IsQueuedForDeletion())
            .MinBy(actor=>actor.Position.DistanceSquaredTo(location));
        if(pet==null){
            ShowManagement("照护");
            SetManagementFeedback($"{station.Label} · 庄园内暂无可照护的伙伴。",true);
            return;
        }
        if(_selectedPet!=pet.Ident)_petManagementExpanded=false;
        _selectedPet=pet.Ident;
        ShowManagement("宠物",$"care:{station.Action}");
        SetManagementFeedback($"{station.Label} · {station.Help}");
    }

    void CreatePetActivityArea()
    {
        var courtyard=new Node3D{Name="PetActivityArea",Position=PetActivityCenter};
        AddChild(courtyard);

        // Visual surfaces have no collision. The three original props keep their real pickable colliders.
        var surface=new SurfaceTool();
        surface.Begin(Mesh.PrimitiveType.Triangles);
        void Polygon(Vector2 origin,Vector2[] points,float height,string tint)
        {
            var color=new Color(tint);
            for(int i=0;i<points.Length;i++){
                foreach(var p in new[]{origin,origin+points[i],origin+points[(i+1)%points.Length]}){
                    surface.SetNormal(Vector3.Up);
                    surface.SetColor(color);
                    surface.AddVertex(new(p.X,height,p.Y));
                }
            }
        }
        void Bed(float x,float z,float w,float d,float corner,float height,string tint)
        {
            w/=2;d/=2;
            Polygon(new(x,z),new Vector2[]{new(-w+corner,-d),new(w-corner,-d),new(w,-d+corner),new(w,d-corner),
                new(w-corner,d),new(-w+corner,d),new(-w,d-corner),new(-w,-d+corner)},height,tint);
        }
        Bed(0,0,46,18.8f,1.4f,.052f,"c5ad80");
        Bed(0,0,45.25f,18.05f,1.2f,.056f,"adbf88");
        Bed(0,4.7f,43.4f,4.2f,1.3f,.060f,"e3d0a4");
        Bed(0,0,6.2f,19.6f,.5f,.061f,"e3d0a4");
        Bed(-14,-1.2f,10.4f,7.7f,1.1f,.064f,"e7c89c");
        Bed(0,-1.2f,10.4f,7.7f,1.1f,.065f,"d7b8a0");
        Bed(14,-1.2f,10.4f,7.7f,1.1f,.064f,"a1c2bb");
        for(int i=0;i<7;i++)Bed((i-3)*5.6f,4.6f,4.25f,2.12f,.48f,.065f,i%2==0?"e9daba":"e5d4af");
        courtyard.AddChild(new MeshInstance3D{
            Mesh=surface.Commit(),MaterialOverride=new StandardMaterial3D{VertexColorUseAsAlbedo=true,Roughness=1},
            CastShadow=GeometryInstance3D.ShadowCastingSetting.Off
        });
        surface.Dispose();

        var timber=new List<(Transform3D Transform,Color Color)>();
        var foliage=new List<(Transform3D Transform,Color Color)>();
        void Block(Vector3 position,Vector3 size,string tint)=>timber.Add((new(Basis.Identity.Scaled(size),position),new(tint)));
        void Shrub(Vector3 position,Vector3 size,string tint)=>foliage.Add((new(Basis.Identity.Scaled(size),position),new(tint)));
        void Rail(float x,float z,float length,bool eastWest)
        {
            int sections=Math.Max(1,Mathf.CeilToInt(length/3.35f));
            float width=length/sections;
            Vector3 along=eastWest?Vector3.Right:Vector3.Back;
            var center=new Vector3(x,0,z);
            for(int i=0;i<=sections;i++){
                Vector3 post=center+along*(i*width-length/2);
                Block(post+Vector3.Up*.46f,new(.24f,.92f,.24f),"b78b5c");
                Block(post+Vector3.Up*.95f,new(.31f,.12f,.31f),"e1b98d");
            }
            for(int i=0;i<sections;i++){
                Vector3 segment=center+along*((i+.5f)*width-length/2);
                Vector3 size=eastWest?new(width,.16f,.12f):new(.12f,.16f,width);
                Block(segment+Vector3.Up*.65f,size,"dec097");
                Block(segment+Vector3.Up*.32f,size,"d1aa7c");
            }
        }
        foreach(float z in new[]{-8.8f,8.8f}){
            Rail(-15.5f,z,13.5f,true);
            Rail(15.5f,z,13.5f,true);
        }
        foreach(float x in new[]{-22.2f,22.2f}){
            Rail(x,-6.15f,5.2f,false);
            Rail(x,6.15f,5.2f,false);
        }

        Block(new(-6.45f,.95f,7.55f),new(.2f,1.9f,.22f),"ad855e");
        Block(new(-3.35f,.95f,7.55f),new(.2f,1.9f,.22f),"ad855e");
        Block(new(-4.9f,1.74f,7.55f),new(4.3f,.97f,.21f),"8a6a4e");
        Block(new(-4.9f,1.74f,7.67f),new(4.06f,.73f,.045f),"526e5a");
        courtyard.AddChild(new Label3D{
            Text="宠物活动区",Position=new(-4.9f,1.74f,7.71f),FontSize=35,PixelSize=.018f,OutlineSize=0,
            Font=new SystemFont{FontNames=new[]{"Microsoft YaHei","Noto Sans CJK SC"}},
            Modulate=new Color("fff3d5")
        });

        string[] flowers={"e9a294","e7c575","b6a5cb"};
        for(int i=0;i<6;i++){
            float x=i<3?-20f+i*5.2f:9.6f+(i-3)*5.2f;
            float z=i%2==0?-7.65f:7.65f;
            Shrub(new(x,.29f,z),new(1.45f,.62f,.92f),i%2==0?"789766":"6f9270");
            for(int bloom=0;bloom<3;bloom++)
                Shrub(new(x+(bloom-1)*.4f,.58f+(bloom%2)*.14f,z+(bloom%2)*.18f),new(.29f,.25f,.29f),flowers[i%flowers.Length]);
        }

        void Batch(Mesh mesh,List<(Transform3D Transform,Color Color)> parts)
        {
            var multimesh=new MultiMesh{TransformFormat=MultiMesh.TransformFormatEnum.Transform3D,UseColors=true,Mesh=mesh,InstanceCount=parts.Count};
            for(int i=0;i<parts.Count;i++){
                multimesh.SetInstanceTransform(i,parts[i].Transform);
                multimesh.SetInstanceColor(i,parts[i].Color);
            }
            courtyard.AddChild(new MultiMeshInstance3D{
                Multimesh=multimesh,MaterialOverride=new StandardMaterial3D{VertexColorUseAsAlbedo=true,Roughness=1}
            });
        }
        Batch(new BoxMesh{Size=Vector3.One},timber);
        Batch(new SphereMesh{Radius=.5f,Height=1,RadialSegments=7,Rings=4},foliage);
    }
}
