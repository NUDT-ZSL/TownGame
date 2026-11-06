using Godot;
using System.Collections.Generic;

public partial class Main
{
    readonly Dictionary<(string Profession,int Shade,int Frame),ArrayMesh> _residentOutfitBodies=new();
    readonly Dictionary<string,ArrayMesh?> _residentOutfitPieces=new();

    static (Color Shirt,string Accessory) ResidentOutfitPalette(string profession)=>profession switch{
        "农民"=>(new("728b54"),"d2a85e"),"牧民"=>(new("a26751"),"c18c65"),
        "工匠"=>(new("687f96"),"724735"),"建筑工"=>(new("bb824d"),"e3b64f"),
        "司机"=>(new("48618c"),"444f70"),"商人"=>(new("956a8b"),"e8c998"),
        "职员"=>(new("6e8292"),"e2d0b1"),"园丁"=>(new("538c74"),"bfd1a0"),
        "孩童"=>(new("ba7e92"),"ecb690"),_=>(new("628d9d"),"a98462")
    };

    internal Mesh ResidentOutfitBody(string profession,int shade,int frame)
    {
        var key=(profession,shade,frame);
        if(_residentOutfitBodies.TryGetValue(key,out var cached))return cached;
        var source=(ArrayMesh)Prototype($"person-{frame}");
        var arrays=source.SurfaceGetArrays(0);
        var colors=arrays[(int)Mesh.ArrayType.Color].AsColorArray();
        Color shirt=ResidentOutfitPalette(profession).Shirt;
        shirt=shade switch{1=>shirt.Lightened(.13f),2=>shirt.Darkened(.13f),_=>shirt};
        for(int index=0;index<colors.Length;index++){
            var color=colors[index];
            if(color.B>color.R+.15f&&color.G>color.R+.12f)colors[index]=shirt;
            else if(color.R is >.39f and <.42f&&color.G is >.30f and <.34f&&color.B is >.22f and <.26f)
                colors[index]=shade switch{1=>new Color("69472d"),2=>new Color("3e3542"),_=>color};
        }
        arrays[(int)Mesh.ArrayType.Color]=colors;
        var body=new ArrayMesh();
        body.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles,arrays);
        body.SurfaceSetMaterial(0,source.SurfaceGetMaterial(0));
        _residentOutfitBodies[key]=body;
        return body;
    }

    internal ArrayMesh? ResidentOutfitPieces(string profession)
    {
        if(_residentOutfitPieces.TryGetValue(profession,out var cached))return cached;
        string accent=ResidentOutfitPalette(profession).Accessory;
        var mesh=new DistrictAccentMesh();
        switch(profession){
            case "农民":
                Hat(.58f,.19f);break;
            case "牧民":
                Hat(.45f,.22f);
                mesh.Box(new(-.35f,.77f,-.18f),new(.055f,1.27f,.055f),"876747",-.14f);
                break;
            case "工匠":
                mesh.Box(new(0,.90f,.18f),new(.30f,.42f,.045f),accent);
                mesh.Box(new(.02f,.83f,.212f),new(.14f,.10f,.026f),"b68c65");
                mesh.Box(new(-.21f,.82f,.12f),new(.09f,.30f,.08f),"bf9b76");
                break;
            case "建筑工":
                Hat(.41f,.20f);
                mesh.Box(new(0,.89f,.18f),new(.35f,.25f,.05f),"d69658");
                mesh.Box(new(0,.90f,.21f),new(.35f,.05f,.021f),"ead5a5");
                break;
            case "司机":
                mesh.Round(new(0,1.70f,0),new(.36f,.19f,.39f),accent);
                mesh.Box(new(0,1.665f,.23f),new(.30f,.032f,.20f),"6676a0");
                mesh.Box(new(0,.98f,.19f),new(.04f,.28f,.04f),"d6bc80");
                break;
            case "商人":
                mesh.Box(new(.26f,.82f,.10f),new(.21f,.26f,.17f),accent);
                mesh.Box(new(.10f,1.04f,.20f),new(.045f,.31f,.032f),"b4976d",.15f);
                break;
            case "职员":
                mesh.Box(new(0,1.04f,.19f),new(.073f,.24f,.04f),accent);
                mesh.Box(new(.30f,.62f,-.12f),new(.20f,.19f,.105f),"74677e");
                break;
            case "园丁":
                Hat(.47f,.16f);
                mesh.Round(new(.30f,.72f,-.16f),new(.22f,.19f,.26f),"88a6ac");
                break;
            case "孩童":
                mesh.Round(new(0,1.70f,0),new(.38f,.20f,.40f),accent);
                mesh.Box(new(0,1.66f,.18f),new(.29f,.038f,.22f),"db9f87");
                break;
        }
        return _residentOutfitPieces[profession]=mesh.Finish();

        void Hat(float brim,float top){
            mesh.Round(new(0,1.69f,0),new(brim,.065f,brim*.87f),accent);
            mesh.Round(new(0,1.75f,0),new(.35f,top,.34f),accent);
        }
    }

    internal int ResidentOutfitShade(string id)=>(int)(PersonHash(id)%3);
}
