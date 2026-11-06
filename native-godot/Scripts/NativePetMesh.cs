using Godot;
using System;
using System.Collections.Generic;

// 原生程序化动物模型。按外观和姿势缓存，视觉随机数不参与经营模拟。
public sealed class NativePetMesh
{
    readonly Dictionary<string,ArrayMesh> _cache=new();readonly Queue<string> _order=new();
    readonly StandardMaterial3D _material=new(){VertexColorUseAsAlbedo=true,Roughness=.82f};
    static readonly Color[] Coats={new("eddfbf"),new("f6f0df"),new("98705d"),new("a8afb0"),new("cb955c"),new("62666a"),new("c9bb9b"),new("b67b5d")};
    internal static Color BaseCoat(string species,int coat)
    {
        if(species=="兔子")return Coats[coat];
        Color color=species switch{"鹦鹉"=>new("79ad8d"),"小鸭"=>new("efd484"),"孔雀"=>new("579c9c"),"狐狸"=>new("d39a67"),"梅花鹿"=>new("b78c65"),"刺猬"=>new("997b66"),"猫咪"=>new("c3a180"),"小狗"=>new("ba9673"),_=>Coats[0]};
        return coat==0?color:color.Lerp(Coats[coat],.78f);
    }
    public Mesh Get(string species,int variant,bool rare,bool sick,string stage,string pose,int frame)
    {
        string key=$"{species}:{variant}:{rare}:{sick}:{stage}:{pose}:{frame}";if(_cache.TryGetValue(key,out var found))return found;
        var surface=new SurfaceTool();surface.Begin(Mesh.PrimitiveType.Triangles);surface.SetMaterial(_material);
        Color body=BaseCoat(species,variant%8);
        if(rare)body=body.Lerp(new Color("d4c6f5"),.55f);if(sick)body=body.Lerp(new Color("b5b3a6"),.28f);if(stage=="老年")body=body.Lerp(new Color("cfcac0"),.26f);
        Color pale=body.Lerp(new Color("fff9eb"),.72f),dark=body.Darkened(.28f),ear=body.Lerp(new Color("d6a4a4"),.65f),eye=new("333339");
        bool bird=species is "鹦鹉" or "小鸭" or "孔雀",tall=species is "羊驼" or "梅花鹿";
        float width=species=="兔子"?1:new[]{1,.86f,1.14f}[variant/32%3];
        // Coordinates use metres, face +Z; all detail parts are one shared mesh.
        void Ell(Vector3 center,Vector3 radius,Color color,float tilt=0,Transform3D? joint=null){
            center.X*=width;radius.X*=width;
            Vector3 Point(float u,float v){var p=new Vector3(Mathf.Cos(u)*Mathf.Sin(v)*radius.X,Mathf.Cos(v)*radius.Y,Mathf.Sin(u)*Mathf.Sin(v)*radius.Z);return center+p.Rotated(Vector3.Forward,tilt);}
            void Vertex(Vector3 point){
                var local=(point-center).Rotated(Vector3.Forward,-tilt);
                var normal=new Vector3(local.X/(radius.X*radius.X),local.Y/(radius.Y*radius.Y),local.Z/(radius.Z*radius.Z)).Normalized().Rotated(Vector3.Forward,tilt);
                if(joint is Transform3D movement){
                    point=movement*point;
                    normal=movement.Basis*normal;
                }
                surface.SetColor(color);surface.SetNormal(normal);surface.AddVertex(point);
            }
            const int slices=10,rings=6;
            for(int y=0;y<rings;y++)for(int x=0;x<slices;x++){
                float u=x*Mathf.Tau/slices,un=(x+1)*Mathf.Tau/slices,v=y*Mathf.Pi/rings,vn=(y+1)*Mathf.Pi/rings;
                var a=Point(u,v);var b=Point(un,v);var c=Point(u,vn);var d=Point(un,vn);
                if(y>0){Vertex(a);Vertex(c);Vertex(b);}if(y<rings-1){Vertex(b);Vertex(c);Vertex(d);}
            }
        }
        float headY=tall?7.5f:4,headZ=tall?1.92f:1.58f;
        if(pose=="sleep"){headY-=1.25f;headZ+=.4f;}if(pose=="eat"){headY-=.67f+Mathf.Sin(frame*Mathf.Pi/2)*.25f;headZ+=.25f;}
        float sweep=PetAmbientMotion.Sweep(frame), strength=Mathf.Abs(sweep);
        var (headAngles,headShift)=pose switch
        {
            "look" => (new Vector3(-.25f*strength,.65f*sweep,0),Vector3.Up*.16f*strength),
            "graze" => (new Vector3(1.12f*strength,.10f*sweep,0),new Vector3(0,-.75f,.32f)*strength),
            "preen" => (new Vector3(.58f*strength,.87f*sweep,-.32f*sweep),new Vector3(.33f*sweep,-.28f*strength,0)),
            "groom" => (new Vector3(.48f*strength,.52f*sweep,0),Vector3.Down*.32f*strength),
            "soothe" => (new Vector3(.38f*strength,.23f*sweep,.26f*sweep),new Vector3(.12f*sweep,-.31f*strength,.17f*strength)),
            "sniff" => (new Vector3(.23f*strength,.19f*sweep,0),Vector3.Back*.31f*strength),
            "dabble" => (new Vector3(.76f*strength,.1f*sweep,0),new Vector3(0,-.65f,.32f)*strength),
            _ => (Vector3.Zero,Vector3.Zero),
        };
        var headPivot=new Vector3(0,headY-.8f,headZ-.48f);
        Transform3D? headJoint=headAngles==Vector3.Zero?null:Joint(Basis.FromEuler(headAngles),headPivot,headShift);
        void Head(Vector3 center,Vector3 radius,Color color,float tilt=0)=>Ell(center,radius,color,tilt,headJoint);
        Transform3D? tailJoint=pose=="wag"&&sweep!=0?Joint(new Basis(Vector3.Up,.75f*sweep),new Vector3(0,2.9f,-1.58f),Vector3.Zero):null;
        Ell(new(0,tall?3.67f:2.33f,0),new(bird?1.42f:1.67f,1.92f,2.25f),body);
        if(tall)Ell(new(0,5.42f,1.25f),new(.83f,2.33f,.83f),body);
        Head(new(0,headY,headZ),new(1.5f,1.5f,1.42f),body);Head(new(0,headY-.5f,headZ+1),new(.92f,.67f,.58f),pale);
        foreach(int side in new[]{-1,1}){
            Head(new(side*.67f,headY+.25f,headZ+1.17f),new(.19f,pose is "sleep" or "idle_blink"?.06f:.23f,.17f),eye);
            if(pose is not "sleep" and not "idle_blink")Head(new(side*.67f-.04f,headY+.33f,headZ+1.3f),new(.05f,.05f,.05f),Colors.White);
        }
        Head(new(0,headY-.25f,headZ+1.58f),new(.27f,.21f,.23f),dark);
        float stride=pose=="walk"?Mathf.Sin(frame*Mathf.Pi/2)*.42f:0;
        foreach(int side in new[]{-1,1})foreach(int front in new[]{-1,1}){
            float pawLift=(pose=="groom"||pose=="soothe")&&front==1&&sweep*side>0?strength:0;
            Ell(new(side,(tall?1.5f:.83f)+pawLift*1.2f,front*1.42f+stride*side*front+pawLift*.62f),
                new(.58f,tall?1.83f:.83f,.83f),dark.Lerp(body,.65f));
        }
        if(species=="兔子"){
            int ears=variant/32%3;
            foreach(int side in new[]{-1,1}){
                bool floppy=ears==1||ears==2&&side==1;
                float twitch=pose=="look"?sweep*(floppy?.10f:.24f):0;
                float tilt=(floppy?side*.9f:side*.12f)+twitch;
                var center=new Vector3(side*.75f,headY+2,headZ-.17f);
                Head(center,new(.5f,1.83f,.42f),body,tilt);
                Head(center+new Vector3(0,0,.3f),new(.25f,1.42f,.17f),ear,tilt);
            }
            Ell(new(0,2.25f,-2.25f),new(.75f,.75f,.67f),pale);
        }else if(species is "猫咪" or "狐狸" or "梅花鹿"){
            foreach(int side in new[]{-1,1}){Head(new(side,headY+1.25f,headZ-.1f),new(.48f,.95f,.38f),body,side*.2f);if(species=="梅花鹿"){Head(new(side,headY+2.6f,headZ-.3f),new(.17f,1.2f,.17f),dark,side*.25f);Head(new(side*1.45f,headY+2.6f,headZ-.3f),new(.13f,.7f,.13f),dark,side*.6f);}}
            if(species!="梅花鹿")Ell(new(.67f,3.1f,-2.5f),new(.67f,.67f,1.75f),body,.5f,tailJoint);
        }else if(species=="小狗"){
            foreach(int side in new[]{-1,1})Head(new(side*1.42f,headY-.25f,headZ-.1f),new(.58f,1.42f,.67f),ear,side*.2f);Ell(new(0,3,-2.25f),new(.5f,.58f,1.42f),body,.65f,tailJoint);
        }else if(species=="绵羊"){
            for(int i=0;i<8;i++){float a=i*Mathf.Tau/8;Ell(new(Mathf.Cos(a)*1.42f,3,Mathf.Sin(a)*1.83f),new(.92f,1,.92f),pale);}foreach(int side in new[]{-1,1})Head(new(side*1.58f,headY,headZ),new(.75f,.42f,.42f),ear);
        }else if(species=="刺猬"){
            for(int i=0;i<18;i++){float a=i*2.4f;Ell(new(Mathf.Cos(a)*(i%4*.33f+.42f),3.8f+i%3*.17f,Mathf.Sin(a)*(i%4*.33f+.42f)-.58f),new(.2f,.6f,.2f),dark);}
        }else if(bird){
            Head(new(0,headY-.58f,headZ+1.75f),new(.5f,.4f,.75f),new Color("eeae54"));foreach(int side in new[]{-1,1})Ell(new(side*1.42f,2.5f,-.17f),new(.42f,1.17f,1.67f),dark);
            if(species=="孔雀")for(int i=0;i<7;i++){
                float fan=pose=="display"?strength:0;
                float angle=(i-3)*(.25f+.15f*fan);
                var feather=new Vector3(Mathf.Sin(angle)*(4+fan),4.17f+Mathf.Cos(angle)*2.08f+.42f*fan,-2.42f);
                Ell(feather,new(.83f,2.08f,.33f),new Color("529b8b"),-angle);
                Ell(feather+new Vector3(0,.83f,.42f),new(.42f,.5f,.17f),new Color("bdbd62"));
            }
        }else foreach(int side in new[]{-1,1})Head(new(side*1.17f,headY+1,headZ-.17f),new(.58f,.75f,.42f),ear);
        int pattern=variant/8%4;
        if(species=="兔子"&&pattern==1)foreach(int side in new[]{-1,1})Head(new(side*.67f,headY+.25f,headZ+1.125f),new(.5f,.58f,.17f),dark);
        else if(species=="兔子"&&pattern==2)Head(new(0,headY+.92f,headZ+1.17f),new(.33f,.67f,.25f),pale);
        else for(int i=0;i<pattern*2;i++)Ell(new((i%2==0?-1:1)*1.17f,3.67f,-1.25f+i*.5f),new(.42f,.25f,.42f),pattern==1?dark:pale);
        int detail=variant/96%4;
        if(species=="兔子")for(int i=0;i<detail;i++)Head(new((i-(detail-1)/2f)*.42f,headY+1.5f,headZ),new(.25f,.55f,.25f),pale);
        else if(detail>0){Color accent=new("df8e85");for(int i=0;i<(detail==1?2:detail==3?5:1);i++){float a=i*Mathf.Tau/(detail==3?5:2);Head(new(Mathf.Cos(a)*.35f,headY-1.5f+Mathf.Sin(a)*.35f,headZ+1.1f),new(.3f,.3f,.2f),accent);}}
        if(pose=="bath")for(int i=0;i<5;i++)Ell(new(Mathf.Sin(i*1.3f)*2,1.5f+(i*11+frame*7)%48/12f,Mathf.Cos(i*1.3f)*2),Vector3.One*.23f,new Color("bae1e4"));
        if(pose=="eat")foreach(int side in new[]{-1,1})Ell(new(side*.5f,1.5f+frame%2*.25f,headZ+1.58f),Vector3.One*.17f,new Color("d1aa69"));
        var mesh=surface.Commit();surface.Dispose();_cache[key]=mesh;_order.Enqueue(key);while(_order.Count>256)_cache.Remove(_order.Dequeue());return mesh;
    }

    static Transform3D Joint(Basis rotation,Vector3 pivot,Vector3 shift)=>new(rotation,pivot+shift-rotation*pivot);
}
