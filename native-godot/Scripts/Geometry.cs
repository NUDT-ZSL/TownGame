using Godot;
using System;

public static class Geometry
{
    public static ArrayMesh Decode(string encoded,Material material)
    {
        var bytes=Convert.FromBase64String(encoded);var floats=new float[bytes.Length/4];Buffer.BlockCopy(bytes,0,floats,0,bytes.Length);
        int count=floats.Length/10;var vertices=new Vector3[count];var normals=new Vector3[count];var colors=new Color[count];var uv=new Vector2[count];
        for(int i=0;i<count;i++) {
            // 坐标反射已将原CCW变为Godot要求的CW，不能再交换顶点。
            int source=i*10;
            vertices[i]=new(floats[source]/12,floats[source+2]/12,floats[source+1]/12);
            normals[i]=new(floats[source+3],floats[source+5],floats[source+4]);
            colors[i]=new(floats[source+6],floats[source+7],floats[source+8],1);
            uv[i]=new(floats[source+9],0);
        }
        var arrays=new Godot.Collections.Array();arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex]=vertices;arrays[(int)Mesh.ArrayType.Normal]=normals;
        arrays[(int)Mesh.ArrayType.Color]=colors;arrays[(int)Mesh.ArrayType.TexUV]=uv;
        var mesh=new ArrayMesh();mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles,arrays);mesh.SurfaceSetMaterial(0,material);return mesh;
    }
    public static readonly string Shader="""
shader_type spatial;
render_mode cull_back;
varying float tag;
void vertex() {
    tag=UV.x;
    if(tag < -0.5 && tag > -1.5) {
        float bend=max(VERTEX.y-2.5,0.0)/9.0;
        VERTEX.x+=sin(TIME*1.3+MODEL_MATRIX[3].x)*bend*bend*.22;
    }
    if(tag < -1.5)VERTEX.y+=sin(VERTEX.x*.42+TIME*1.8)*.04+cos(VERTEX.z*.51-TIME)*.03;
}
void fragment() {
    ALBEDO=COLOR.rgb;ROUGHNESS=.85;
    if(tag>0.0)EMISSION=COLOR.rgb*tag*.7;
    if(tag < -1.5) {
        float wave=sin(length(VERTEX.xz)*1.5-TIME*2.0);
        ALBEDO+=vec3(.06,.1,.12)*smoothstep(.88,1.0,wave);ROUGHNESS=.26;METALLIC=.12;
    }
}
""";
}
