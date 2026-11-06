using Godot;
using System;

public static class Geometry
{
    public static ArrayMesh Decode(string encoded,Material material,bool terrain=false)
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
        arrays[(int)Mesh.ArrayType.Color]=colors;
        if(terrain){
            var edgeUv=new Vector2[count];
            for(int i=0;i+5<count;i+=6){
                var color=colors[i];
                if(normals[i].Y<.99f||normals[i+5].Y<.99f||vertices[i].Y>.09f)continue;
                float surface=color.G>color.R&&color.G>color.B+.05f?1:color.R>color.B+.12f?2:3;
                var low=vertices[i];var high=low;
                for(int j=i+1;j<i+6;j++){low=low.Min(vertices[j]);high=high.Max(vertices[j]);}
                float width=high.X-low.X,depth=high.Z-low.Z;
                if(width<.22f||depth<.22f)continue;
                for(int j=i;j<i+6;j++){
                    uv[j].Y=surface;
                    edgeUv[j]=new(width>depth*3?.5f:(vertices[j].X-low.X)/width,
                        depth>width*3?.5f:(vertices[j].Z-low.Z)/depth);
                }
            }
            arrays[(int)Mesh.ArrayType.TexUV2]=edgeUv;
        }
        arrays[(int)Mesh.ArrayType.TexUV]=uv;
        var mesh=new ArrayMesh();mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles,arrays);mesh.SurfaceSetMaterial(0,material);return mesh;
    }
    public static readonly string Shader="""
shader_type spatial;
render_mode cull_back;
uniform vec3 season_tint=vec3(1.0);
uniform float winter=0.0;
uniform float window_glow=0.0;
uniform vec4 actor_occlusion_point=vec4(0.0);
uniform float actor_occlusion_aspect=1.0;
varying float tag;
varying vec2 ground_position;
varying vec2 ground_edge_uv;
varying float ground_surface;
float ground_hash(vec2 point) {
    return fract(sin(dot(point,vec2(127.1,311.7)))*43758.5453);
}
float ground_noise(vec2 point) {
    vec2 cell=floor(point), part=fract(point);
    vec2 blend=part*part*(3.0-2.0*part);
    return mix(mix(ground_hash(cell),ground_hash(cell+vec2(1.0,0.0)),blend.x),
        mix(ground_hash(cell+vec2(0.0,1.0)),ground_hash(cell+vec2(1.0,1.0)),blend.x),blend.y);
}
void vertex() {
    tag=UV.x;
    ground_surface=UV.y;
    ground_position=vec2(0.0);
    ground_edge_uv=UV2;
    if(ground_surface>0.5)ground_position=(MODEL_MATRIX*vec4(VERTEX,1.0)).xz;
    if(tag < -0.5 && tag > -1.5) {
        float bend=max(VERTEX.y-2.5,0.0)/9.0;
        VERTEX.x+=sin(TIME*1.3+MODEL_MATRIX[3].x)*bend*bend*.22;
    }
    if(tag < -1.5)VERTEX.y+=sin(VERTEX.x*.42+TIME*1.8)*.04+cos(VERTEX.z*.51-TIME)*.03;
}
void fragment() {
    if(actor_occlusion_point.w>0.5){
        vec4 subject=PROJECTION_MATRIX*VIEW_MATRIX*vec4(actor_occlusion_point.xyz,1.0);
        vec4 sample_clip=PROJECTION_MATRIX*vec4(VERTEX,1.0);
        vec2 delta=(sample_clip.xy/sample_clip.w-subject.xy/subject.w)*vec2(actor_occlusion_aspect,1.0)*.5;
        float opening=1.0-smoothstep(.055,.135,length(delta));
        float pattern=fract(dot(floor(FRAGCOORD.xy),vec2(.754877666,.569840296)));
        if(pattern<opening*.80)discard;
    }
    ALBEDO=COLOR.rgb;ROUGHNESS=.85;
    if(COLOR.g>COLOR.r*1.06 && COLOR.g>COLOR.b*1.05 && tag>-1.5) {
        ALBEDO*=season_tint;
        ALBEDO=mix(ALBEDO,vec3(.86,.9,.91),winter*.65);
    }
    if(ground_surface>0.5){
        float distance_fade=1.0-smoothstep(20.0,70.0,length(VERTEX));
        if(distance_fade>0.0){
            float patch=ground_noise(ground_position*.38)-.5;
            float grain=ground_noise(ground_position*5.0)-.5;
            float contrast=patch*.10+grain*.10;
            if(ground_surface<1.5){
                ALBEDO+=vec3(.045,.065,.017)*contrast*8.0*distance_fade;
            }else if(ground_surface<2.5){
                vec2 border=min(ground_edge_uv,1.0-ground_edge_uv);
                float worn=1.0-smoothstep(.01,.15+patch*.12,min(border.x,border.y));
                ALBEDO*=1.0+distance_fade*(contrast-worn*.09);
            }else{
                vec2 tile=ground_position*vec2(1.8,2.45);
                vec2 width=max(fwidth(tile),vec2(.002));
                tile.x+=mod(floor(tile.y),2.0)*.5;
                vec2 joint=abs(fract(tile-.5)-.5);
                vec2 seam=1.0-smoothstep(vec2(.025),vec2(.025)+width,joint);
                float clarity=1.0-smoothstep(.07,.35,max(width.x,width.y));
                ALBEDO*=1.0+distance_fade*(contrast*.5-max(seam.x,seam.y)*.14*clarity);
            }
        }
    }
    if(tag>0.0 && COLOR.g>COLOR.r) {
        ALBEDO=mix(ALBEDO,vec3(1.0,.71,.34),window_glow);
        EMISSION=mix(COLOR.rgb*.08,vec3(1.0,.60,.23)*.9,window_glow)*tag;
    } else if(tag>0.0)EMISSION=COLOR.rgb*tag*.7;
    if(tag < -1.5) {
        float wave=sin(length(VERTEX.xz)*1.5-TIME*2.0);
        ALBEDO+=vec3(.06,.1,.12)*smoothstep(.88,1.0,wave);ROUGHNESS=.26;METALLIC=.12;
    }
}
""";
}
