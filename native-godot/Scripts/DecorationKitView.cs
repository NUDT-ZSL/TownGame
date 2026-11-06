using Godot;
using System;
using System.Collections.Generic;

public partial class Main
{
    static readonly string[] DecorationKitNames={"全部装饰","庭院花木","晨光市集","公园休憩"};
    readonly Dictionary<string,ArrayMesh> _decorationKitMeshes=new();

    static int DecorationKitGroup(string kind)=>kind switch{
        "kit_garden_planter" or "kit_garden_trellis"=>1,
        "kit_market_stall" or "kit_market_crates"=>2,
        "kit_park_bench" or "kit_park_pergola"=>3,
        _=>0,
    };

    Mesh DecorationKitMesh(string kind)
    {
        if(_decorationKitMeshes.TryGetValue(kind,out var cached))return cached;
        var kit=new DistrictAccentMesh();
        const string wood="9b7652", lightWood="ca9d68", leaf="77995b", sage="578981";
        switch(kind){
            case "kit_garden_planter":
                kit.Box(new(0,.32f,0),new(2.80f,.64f,.92f),wood);
                kit.Box(new(0,.60f,0),new(2.94f,.14f,1.02f),lightWood);
                for(int i=-3;i<=3;i++){
                    float x=i*.37f,z=(i&1)==0?.19f:-.17f;
                    kit.Round(new(x,.77f,z),new(.43f,.38f,.55f),leaf);
                    kit.Round(new(x,1f,z),new(.24f,.21f,.25f),((i+3)%3) switch{0=>"dc8f92",1=>"f1d283",_=>"b5a0ce"});
                }
                break;
            case "kit_garden_trellis":
                foreach(float x in new[]{-1.4f,1.4f})kit.Box(new(x,1.07f,0),new(.16f,2.14f,.22f),wood);
                foreach(float y in new[]{.56f,1.08f,1.60f,2.04f})kit.Box(new(0,y,0),new(3,.11f,.18f),lightWood);
                for(int i=0;i<7;i++){
                    float y=.37f+i*.25f,x=-1.12f+i*.31f;
                    kit.Round(new(x,y,.12f),new(.68f,.51f,.39f),(i&1)==0?leaf:"a1ae6a");
                    kit.Round(new(x-.09f,y+.13f,.31f),new(.23f,.20f,.20f),(i&1)==0?"cf89a2":"e6bdce");
                }
                break;
            case "kit_market_stall":
                foreach(float x in new[]{-1.34f,1.34f})foreach(float z in new[]{-.67f,.67f})
                    kit.Box(new(x,1.19f,z),new(.13f,2.38f,.13f),wood);
                kit.Box(new(0,.45f,0),new(2.74f,.88f,1.32f),"a7734f");
                kit.Box(new(0,.93f,0),new(2.97f,.14f,1.52f),lightWood);
                for(int i=-3;i<=3;i++){
                    string stripe=(i&1)==0?"f4dfb0":"bc665a";
                    kit.Box(new(i*.44f,2.46f,0),new(.45f,.12f,1.90f),stripe,.13f);
                    kit.Box(new(i*.44f,2.27f,.94f),new(.44f,.23f,.09f),stripe);
                    kit.Round(new(i*.36f,1.11f,.23f),new(.25f,.25f,.27f),(i&1)==0?"d78a50":"a7ae5f");
                }
                break;
            case "kit_market_crates":
                foreach(float x in new[]{-.78f,.78f}){
                    kit.Box(new(x,.29f,0),new(1.33f,.58f,1.05f),wood);
                    kit.Box(new(x,.48f,.54f),new(1.37f,.12f,.09f),lightWood);
                    for(int i=-1;i<=1;i++)foreach(float z in new[]{-.25f,.25f})
                        kit.Round(new(x+i*.34f,.65f,z),new(.33f,.32f,.37f),x<0?"d3875a":"a3b867");
                }
                break;
            case "kit_park_bench":
                foreach(float x in new[]{-1.03f,1.03f}){
                    kit.Box(new(x,.30f,0),new(.17f,.60f,.81f),"4c6a60");
                    kit.Box(new(x,.70f,-.34f),new(.13f,1.40f,.14f),"4c6a60");
                }
                for(int i=-1;i<=1;i++)kit.Box(new(0,.56f,i*.24f),new(2.49f,.12f,.22f),sage);
                foreach(float y in new[]{.90f,1.17f})kit.Box(new(0,y,-.33f),new(2.49f,.20f,.10f),sage);
                foreach(float x in new[]{-1.52f,1.52f}){
                    kit.Box(new(x,.23f,-.11f),new(.50f,.46f,.59f),"ba8767");
                    kit.Round(new(x,.55f,-.11f),new(.53f,.43f,.62f),leaf);
                    kit.Round(new(x,.80f,-.11f),new(.27f,.27f,.27f),"edc77e");
                }
                break;
            case "kit_park_pergola":
                foreach(float x in new[]{-1.38f,1.38f})foreach(float z in new[]{-.97f,.97f}){
                    kit.Box(new(x,1.22f,z),new(.15f,2.44f,.18f),"637c68");
                    kit.Box(new(x,.11f,z),new(.26f,.22f,.28f),"b8ae90");
                }
                foreach(float z in new[]{-.97f,.97f})kit.Box(new(0,2.45f,z),new(3.27f,.19f,.19f),sage);
                for(int i=-3;i<=3;i++){
                    float x=i*.46f;
                    kit.Box(new(x,2.60f,0),new(.13f,.15f,2.42f),"8eaa81");
                    kit.Round(new(x,2.72f,(i&1)==0?.60f:-.44f),new(.73f,.33f,.84f),(i&1)==0?leaf:"90a96b");
                    kit.Round(new(x,2.86f,(i&1)==0?.71f:-.48f),new(.24f,.21f,.28f),"b69abe");
                }
                break;
            default:throw new ArgumentException("未知装饰模型",nameof(kind));
        }
        return _decorationKitMeshes[kind]=kit.Finish()!;
    }
}
