using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

// Shared meshes keep an eight-metre street section cheap to render. Vehicle and
// pedestrian lanes have real space between them; intersections clear the median.
public partial class Main
{
    public const float VehicleLaneOffset=1.65f,SidewalkOffset=3.4f;
    readonly Dictionary<string,ArrayMesh> _boulevardMeshes=new();
    readonly Dictionary<int,ArrayMesh> _signalBodyMeshes=new();
    readonly Dictionary<string,MultiMeshInstance3D> _signalGroups=new();
    readonly Dictionary<string,Transform3D[]> _signalTransforms=new();
    readonly List<Vector3> _signalFacings=new();
    readonly Dictionary<string,StandardMaterial3D> _signalColors=new();
    public int BoulevardSignalApproachCount=>_signalFacings.Count;
    public IReadOnlyList<Vector3> BoulevardSignalFacingDirections=>_signalFacings;
    static readonly Vector2I[] StreetDirections={Vector2I.Up,Vector2I.Right,Vector2I.Down,Vector2I.Left};

    int StreetMask(Vector2I cell)
    {
        int mask=0;for(int i=0;i<4;i++)if(Data.Roads.ContainsKey(cell+StreetDirections[i]))mask|=1<<i;return mask;
    }
    static int StreetDegree(int mask)=>System.Numerics.BitOperations.PopCount((uint)mask);
    public Mesh RoadMesh(int mask,string style)
    {
        string key=$"{style}:{mask}";if(_boulevardMeshes.TryGetValue(key,out var cached))return cached;
        var m=new StreetMeshBuilder();
        Color asphalt=new(style=="stone"?"929a92":style=="brick"?"9b8775":"65736e"),paving=new("bdc0af"),kerb=new("dde0cc"),white=new("efedcf");
        m.Box(new(0,.015f,0),new(8,.03f,8),paving);
        m.Box(new(0,.034f,0),new(5.6f,.018f,5.6f),asphalt);
        for(int i=0;i<4;i++)if((mask&(1<<i))!=0){
            Vector3 outwards=new(StreetDirections[i].X,0,StreetDirections[i].Y);
            bool ns=i%2==0;
            m.Box(outwards*3.4f+Vector3.Up*.034f,ns?new(5.6f,.018f,1.2f):new(1.2f,.018f,5.6f),asphalt);
        }
        // Raised kerbs mark the outer lane edge, leaving a full 1.2 m pavement.
        for(int i=0;i<4;i++)if((mask&(1<<i))==0){
            Vector3 outwards=new(StreetDirections[i].X,0,StreetDirections[i].Y);
            bool ns=i%2==0;
            float length=mask==5||mask==10?8:5.6f;
            m.Box(outwards*2.83f+Vector3.Up*.055f,ns?new(length,.11f,.08f):new(.08f,.11f,length),kerb);
        }
        if(mask==5||mask==10){
            bool ns=mask==5;
            m.Box(new(0,.085f,0),ns?new(.6f,.17f,8):new(8,.17f,.6f),kerb);
            m.Box(new(0,.18f,0),ns?new(.48f,.04f,8):new(8,.04f,.48f),new("648958"));
            foreach(float at in new[]{-2.5f,0,2.5f})m.Shrub(ns?new(0,.22f,at):new(at,.22f,0),new("547c4d"));
            // Small paving joints make the pedestrian surface read separately.
            foreach(float side in new[]{-3.4f,3.4f})foreach(float at in new[]{-2f,0,2f})
                m.Box(ns?new(side,.033f,at):new(at,.033f,side),ns?new(1.15f,.006f,.025f):new(.025f,.006f,1.15f),new("9da899"));
            foreach(float side in new[]{-2.69f,2.69f})m.Box(ns?new(side,.048f,0):new(0,.048f,side),ns?new(.045f,.012f,8):new(8,.012f,.045f),white);
        }
        if(StreetDegree(mask)>=3){
            for(int i=0;i<4;i++)if((mask&(1<<i))!=0){
                var outward=new Vector3(StreetDirections[i].X,0,StreetDirections[i].Y);var across=new Vector3(-outward.Z,0,outward.X);
                // The zebra band is on the perimeter, matching sidewalk-to-sidewalk crossings.
                for(int stripe=-4;stripe<=4;stripe++)m.Box(outward*3.35f+across*(stripe*.6f)+Vector3.Up*.048f,i%2==0?new(.32f,.012f,.9f):new(.9f,.012f,.32f),white);
                m.Box(outward*3.93f-across*1.55f+Vector3.Up*.048f,i%2==0?new(2.4f,.012f,.09f):new(.09f,.012f,2.4f),white);
            }
        }
        var mesh=m.Commit(_material);_boulevardMeshes[key]=mesh;return mesh;
    }

    static Transform3D SignalTransform(Vector3 position,Vector3 face)=>new(new Basis(Vector3.Up,Mathf.Atan2(face.X,face.Z)),position);
    void EnsureSignalColors()
    {
        if(_signalColors.Count>0)return;
        foreach(var (name,color) in new[]{("red","f16a55"),("yellow","ffd56b"),("green","66e8a0"),("dark","26332c")})
            _signalColors[name]=new StandardMaterial3D{AlbedoColor=new Color(color),EmissionEnabled=name!="dark",Emission=new Color(color),EmissionEnergyMultiplier=name=="dark"?0:1.1f,Roughness=.5f};
    }
    public void CreateBoulevardSignals()
    {
        _signalRoot?.QueueFree();_signalRoot=new Node3D{Name="DirectionalSignals"};AddChild(_signalRoot);
        _junctions.Clear();_lamps.Clear();_signalGroups.Clear();_signalTransforms.Clear();_signalFacings.Clear();_lastPhase="";EnsureSignalColors();
        var placements=new Dictionary<string,List<Transform3D>>();
        void Lens(string group,Transform3D transform){if(!placements.TryGetValue(group,out var list))placements[group]=list=new();list.Add(transform);}
        foreach(var cell in Data.Roads.Keys){
            int mask=StreetMask(cell);if(StreetDegree(mask)<3)continue;
            _junctions.Add(cell);var center=WorldData.Cell(cell);
            if(!_signalBodyMeshes.TryGetValue(mask,out var body)){
                var structure=new StreetMeshBuilder();
                for(int i=0;i<4;i++)if((mask&(1<<i))!=0){
                    Vector3 face=new(StreetDirections[i].X,0,StreetDirections[i].Y),side=new(face.Z,0,-face.X);
                    var pole=face*3.05f+side*3.72f;
                    structure.Box(pole+Vector3.Up*1.95f,new(.12f,3.9f,.12f),new("4b625b"));
                    var head=SignalTransform(pole+Vector3.Up*3.32f,face);
                    structure.Box(Vector3.Zero,new(.48f,1.24f,.3f),new("26352e"),head);
                    structure.Box(new(0,.65f,.1f),new(.6f,.08f,.5f),new("23332a"),head);
                    foreach(float y in new[]{.38f,0,-.38f})structure.Box(new(0,y+.15f,.26f),new(.35f,.045f,.3f),new("23332a"),head);
                    foreach(float sign in new[]{-1f,1f}){
                        var walkPole=face*3.35f+side*(sign*3.78f);
                        var walkHead=SignalTransform(walkPole+Vector3.Up*1.9f,-side*sign);
                        structure.Box(walkPole+Vector3.Up*.95f,new(.08f,1.9f,.08f),new("52665c"));
                        structure.Box(Vector3.Zero,new(.31f,.65f,.23f),new("24372d"),walkHead);
                    }
                }
                body=structure.Commit(_material);_signalBodyMeshes[mask]=body;
            }
            _signalRoot.AddChild(new MeshInstance3D{Mesh=body,Position=center});
            for(int i=0;i<4;i++)if((mask&(1<<i))!=0){
                Vector3 face=new(StreetDirections[i].X,0,StreetDirections[i].Y),side=new(face.Z,0,-face.X);
                _signalFacings.Add(face);
                string axis=i%2==0?"ns":"ew";
                var head=SignalTransform(center+face*3.05f+side*3.72f+Vector3.Up*3.32f,face);
                int n=0;foreach(string aspect in new[]{"red","yellow","green"})Lens(axis+":"+aspect,head*new Transform3D(new Basis(Vector3.Right,Mathf.Pi/2),new(0,.38f-n++*.38f,.165f)));
                foreach(float sign in new[]{-1f,1f}){
                    var walkHead=SignalTransform(center+face*3.35f+side*(sign*3.78f)+Vector3.Up*1.9f,-side*sign);
                    Lens("walk:red",walkHead*new Transform3D(new Basis(Vector3.Right,Mathf.Pi/2),new(0,.16f,.125f)));
                    Lens("walk:green",walkHead*new Transform3D(new Basis(Vector3.Right,Mathf.Pi/2),new(0,-.16f,.125f)));
                }
            }
        }
        foreach(var pair in placements){
            _signalTransforms[pair.Key]=pair.Value.ToArray();
            bool pedestrian=pair.Key.StartsWith("walk:");float radius=pedestrian?.095f:.13f;
            var instances=new MultiMesh{TransformFormat=MultiMesh.TransformFormatEnum.Transform3D,Mesh=new CylinderMesh{TopRadius=radius,BottomRadius=radius,Height=.025f,RadialSegments=12},InstanceCount=pair.Value.Count};
            for(int i=0;i<pair.Value.Count;i++)instances.SetInstanceTransform(i,pair.Value[i]);
            var group=new MultiMeshInstance3D{Multimesh=instances,MaterialOverride=_signalColors["dark"],Name=pair.Key.Replace(':','_')};
            _signalRoot.AddChild(group);_signalGroups[pair.Key]=group;
        }
        UpdateBoulevardSignals(Phase);
    }
    public void UpdateBoulevardSignals(string phase)
    {
        foreach(var pair in _signalGroups){
            var parts=pair.Key.Split(':');string axis=parts[0],aspect=parts[1];
            bool green=phase==axis,yellow=axis!="walk"&&phase==axis+"_yellow";
            bool on=aspect=="green"?green:aspect=="yellow"?yellow:!green&&!yellow;
            pair.Value.MaterialOverride=_signalColors[on?aspect:"dark"];
        }
    }
    public void VerifyBoulevardGeometry()
    {
        if(VehicleLaneOffset<=.3f+.75f||SidewalkOffset<=2.8f||SidewalkOffset+.27f>=4)throw new Exception("车道或人行道间距不够");
        foreach(var direction in _signalFacings)if(Math.Abs(direction.X)+Math.Abs(direction.Z)!=1)throw new Exception("信号灯朝向无效");
        int expected=Data.Roads.Keys.Where(c=>StreetDegree(StreetMask(c))>=3).Sum(c=>StreetDegree(StreetMask(c)));
        if(expected!=BoulevardSignalApproachCount)throw new Exception("路口进口与信号灯数量不一致");
        foreach(var pair in _signalGroups){
            var instances=pair.Value.Multimesh;if(instances.InstanceCount==0)throw new Exception("信号灯未生成");
            for(int i=0;i<instances.InstanceCount;i++){
                // The headless dummy renderer returns identity for every MultiMesh
                // readback. Check submitted transforms there, GPU readback in preview.
                var transform=DisplayServer.GetName()=="headless"?_signalTransforms[pair.Key][i]:instances.GetInstanceTransform(i);
                var normal=transform.Basis.Y.Normalized();
                if(pair.Key.StartsWith("ns:")&&Math.Abs(normal.Z)<.99f||pair.Key.StartsWith("ew:")&&Math.Abs(normal.X)<.99f)throw new Exception($"信号灯灯面没有朝向对应进口: {pair.Key} normal={normal}");
            }
        }
        foreach(var mesh in _boulevardMeshes.Values){var size=mesh.GetAabb().Size;if(Math.Abs(size.X-8)>.01f||Math.Abs(size.Z-8)>.01f)throw new Exception("加宽道路网格尺寸不一致");}
        GD.Print($"BOULEVARD_GEOMETRY_VERIFIED junctions={_junctions.Count} approaches={BoulevardSignalApproachCount} cached_roads={_boulevardMeshes.Count}");
    }
}

// Vertex-coloured box geometry is batched into one surface, instead of creating
// a node/material for each pavement tile, plant or traffic-light pole.
internal sealed class StreetMeshBuilder
{
    readonly List<Vector3> _vertices=new(),_normals=new();readonly List<Color> _colors=new();
    void Quad(Vector3 a,Vector3 b,Vector3 c,Vector3 d,Vector3 normal,Color color,Transform3D transform)
    {
        foreach(var vertex in new[]{a,b,c,a,c,d}){_vertices.Add(transform*vertex);_normals.Add(transform.Basis*normal);_colors.Add(color);}
    }
    public void Box(Vector3 position,Vector3 size,Color color,Transform3D? transformed=null)
    {
        var t=transformed??Transform3D.Identity;var min=position-size*.5f;var max=position+size*.5f;
        Quad(new(min.X,max.Y,min.Z),new(max.X,max.Y,min.Z),new(max.X,max.Y,max.Z),new(min.X,max.Y,max.Z),Vector3.Up,color,t);
        Quad(new(min.X,min.Y,max.Z),new(max.X,min.Y,max.Z),new(max.X,min.Y,min.Z),new(min.X,min.Y,min.Z),Vector3.Down,color,t);
        Quad(new(min.X,min.Y,max.Z),new(min.X,max.Y,max.Z),new(max.X,max.Y,max.Z),new(max.X,min.Y,max.Z),Vector3.Back,color,t);
        Quad(new(max.X,min.Y,min.Z),new(max.X,max.Y,min.Z),new(min.X,max.Y,min.Z),new(min.X,min.Y,min.Z),Vector3.Forward,color,t);
        Quad(new(max.X,min.Y,max.Z),new(max.X,max.Y,max.Z),new(max.X,max.Y,min.Z),new(max.X,min.Y,min.Z),Vector3.Right,color,t);
        Quad(new(min.X,min.Y,min.Z),new(min.X,max.Y,min.Z),new(min.X,max.Y,max.Z),new(min.X,min.Y,max.Z),Vector3.Left,color,t);
    }
    public void Shrub(Vector3 center,Color color)
    {
        Box(center+Vector3.Up*.1f,new(.36f,.24f,.62f),color);
        Box(center+Vector3.Up*.23f,new(.24f,.16f,.42f),color.Lightened(.06f));
    }
    public ArrayMesh Commit(Material material)
    {
        var arrays=new Godot.Collections.Array();arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex]=_vertices.ToArray();arrays[(int)Mesh.ArrayType.Normal]=_normals.ToArray();arrays[(int)Mesh.ArrayType.Color]=_colors.ToArray();arrays[(int)Mesh.ArrayType.TexUV]=new Vector2[_vertices.Count];
        var mesh=new ArrayMesh();mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles,arrays);mesh.SurfaceSetMaterial(0,material);return mesh;
    }
}
