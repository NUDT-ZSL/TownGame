using Godot;
using System;
using System.Collections.Generic;

public partial class Main
{
    // Small low-poly planting clusters make the open lawns feel lived in. They are
    // visual only, so they never block roads, animals, or building placement.
    Node3D CreateGardenDetails()
    {
        var root=new Node3D();
        var groups=new List<Transform3D>[8];for(int i=0;i<groups.Length;i++)groups[i]=new();
        var planters=new List<Transform3D>();
        var town=Game.Rows("town_buildings");
        for(int y=3;y<77;y++)for(int x=3;x<122;x++){
            string land=Data.Root["terrain"]![y]![x]!.GetValue<string>();
            bool estate=land=="estate";
            if(estate||land!="grass"&&land!="zone")continue;
            uint seed=unchecked((uint)(x*73856093^y*19349663^1847));
            seed=unchecked((seed^(seed>>16))*0x7feb352du);
            seed^=seed>>15;
            if(seed%ActiveGraphics.VegetationStep!=0||Data.Roads.ContainsKey(new(x,y)))continue;
            var random=new Random(unchecked((int)seed));
            Vector3 center=WorldData.Cell(new(x,y));
            bool occupied=false;
            foreach(var plot in Data.Snapshot["buildings"]!.AsArray()){
                int id=WorldData.I(plot!,"building_id");
                Vector3 p=WorldData.Expand(new((1314+id%5*230)/12f,0,(870+id/5*88)/12f));
                if(Math.Abs(center.X-p.X)<12&&Math.Abs(center.Z-p.Z)<9){occupied=true;break;}
            }
            if(occupied)continue;
            foreach(var b in town){
                float bx=(WorldData.I(b!,"cx")+WorldData.I(b!,"w")*.5f)*WorldData.GridSize;
                float bz=(WorldData.I(b!,"cy")+WorldData.I(b!,"h")*.5f)*WorldData.GridSize;
                if(Math.Abs(center.X-bx)<WorldData.I(b!,"w")*4+2&&Math.Abs(center.Z-bz)<WorldData.I(b!,"h")*4+2){occupied=true;break;}
            }
            if(occupied)continue;
            int palette=random.Next(4);
            float size=.75f+(float)random.NextDouble()*.5f;
            center+=new Vector3((float)random.NextDouble()*3-1.5f,0,(float)random.NextDouble()*3-1.5f);
            groups[palette].Add(new Transform3D(Basis.Identity.Scaled(new Vector3(size,.55f*size,size)),center+Vector3.Up*.52f*size));
            for(int flower=0;flower<3;flower++){
                float angle=(flower/3f)*Mathf.Tau+(float)random.NextDouble()*.5f;
                float reach=.35f+(float)random.NextDouble()*.25f;
                var bloom=center+new Vector3(Mathf.Cos(angle)*reach,1.04f*size,Mathf.Sin(angle)*reach);
                groups[4+palette].Add(new Transform3D(Basis.Identity.Scaled(Vector3.One*(.34f+(float)random.NextDouble()*.12f)),bloom));
            }
        }
        // Terracotta planters tie completed buildings to the garden.
        foreach(var plot in Data.Snapshot["buildings"]!.AsArray()){
            int id=WorldData.I(plot!,"building_id");
            Vector3 p=WorldData.Expand(new((1314+id%5*230)/12f,0,(870+id/5*88)/12f));
            if(WorldData.S(plot!,"kind")==""){
                int meadow=id%4;
                Vector3 spot=p+new Vector3(id%2==0?-4:4,0,-1.8f);
                groups[meadow].Add(new Transform3D(Basis.Identity.Scaled(new Vector3(1.25f,.62f,1.25f)),spot+Vector3.Up*.55f));
                for(int k=0;k<5;k++){
                    float angle=k*Mathf.Tau/5;
                    var bloom=spot+new Vector3(Mathf.Cos(angle)*.63f,1.21f,Mathf.Sin(angle)*.63f);
                    groups[4+meadow].Add(new Transform3D(Basis.Identity.Scaled(Vector3.One*.39f),bloom));
                }
                continue;
            }
            for(int side=-1;side<=1;side+=2){
                Vector3 spot=p+new Vector3(side*7,0,7.8f);
                int palette=(id+(side+1)/2)%4;
                planters.Add(new Transform3D(Basis.Identity,spot+Vector3.Up*.33f));
                groups[palette].Add(new Transform3D(Basis.Identity.Scaled(new Vector3(1.2f,.78f,1.2f)),spot+Vector3.Up*1.07f));
                for(int k=0;k<5;k++){
                    float angle=k*Mathf.Tau/5;
                    Vector3 bloom=spot+new Vector3(Mathf.Cos(angle)*.6f,1.8f+Mathf.Sin(k*2)*.15f,Mathf.Sin(angle)*.6f);
                    groups[4+palette].Add(new Transform3D(Basis.Identity.Scaled(Vector3.One*.43f),bloom));
                }
            }
        }
        string[] foliage={"6f9e69","7eaa83","7d9d74","739c91"};
        string[] blossom={"f5b6a6","f2d279","bdb5e0","f3efe0"};
        for(int i=0;i<groups.Length;i++){
            if(groups[i].Count==0)continue;
            var sphere=new SphereMesh{Radius=1,Height=2,RadialSegments=7,Rings=4};
            var multi=new MultiMesh{TransformFormat=MultiMesh.TransformFormatEnum.Transform3D,Mesh=sphere,InstanceCount=groups[i].Count};
            for(int j=0;j<groups[i].Count;j++)multi.SetInstanceTransform(j,groups[i][j]);
            root.AddChild(new MultiMeshInstance3D{Multimesh=multi,MaterialOverride=new StandardMaterial3D{AlbedoColor=new Color(i<4?foliage[i]:blossom[i-4]),Roughness=1}});
        }
        if(planters.Count>0){
            var pot=new MultiMesh{TransformFormat=MultiMesh.TransformFormatEnum.Transform3D,Mesh=new CylinderMesh{TopRadius=1,BottomRadius=.8f,Height=.65f,RadialSegments=8},InstanceCount=planters.Count};
            for(int i=0;i<planters.Count;i++)pot.SetInstanceTransform(i,planters[i]);
            root.AddChild(new MultiMeshInstance3D{Multimesh=pot,MaterialOverride=new StandardMaterial3D{AlbedoColor=new Color("c9896e"),Roughness=1}});
        }
        return root;
    }
}
