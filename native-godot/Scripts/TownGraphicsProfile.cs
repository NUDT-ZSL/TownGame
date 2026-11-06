using Godot;
using System;
using System.Linq;

public partial class Main
{
    readonly record struct GraphicsProfile(string Name,int Fps,int ShadowSplits,float ShadowDistance,int VegetationStep,int Rain,int Snow,int Lights,int Aa);
    static readonly GraphicsProfile[] GraphicsProfiles={
        new("省电",30,2,.58f,175,110,45,1,0),
        new("平衡",60,4,.80f,110,230,80,2,1),
        new("清晰",120,4,1,75,360,120,4,2)
    };
    int _graphicsProfile=2;
    GraphicsProfile ActiveGraphics=>GraphicsProfiles[_graphicsProfile];

    void ApplyTownFrameLimit()
    {
        if(UseTownPreferences())Engine.MaxFps=ActiveGraphics.Fps;
    }

    OptionButton AddGraphicsProfileControls(Container row)
    {
        MarketLabel(row,"场景画质").VerticalAlignment=VerticalAlignment.Center;
        var profile=MarketOptions(row,GraphicsProfiles.Select(value=>value.Name).ToArray(),_graphicsProfile);
        profile.Name="TownGraphicsProfile";
        profile.TooltipText="联动阴影范围、野花密度、雨雪、街灯和帧率上限。";
        return profile;
    }

    void ApplyTownGraphicsProfile()
    {
        ApplyTownFrameLimit();
        _townShadowBand=-1;
        UpdateTownShadowProfile();
        if(_rain!=null)_rain.Amount=ActiveGraphics.Rain;
        if(_snow!=null)_snow.Amount=ActiveGraphics.Snow;
        UpdateStreetLighting();
        if(!_dynamic.Remove("garden",out var old))return;
        RemoveChild(old);
        old.QueueFree();
        var garden=CreateGardenDetails();
        AddChild(garden);
        _dynamic["garden"]=garden;
    }
}
