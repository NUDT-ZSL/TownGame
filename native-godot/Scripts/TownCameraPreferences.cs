using Godot;
using System;

public partial class Main
{
    readonly record struct TownCameraInput(int Zoom,bool Centered,int Orbit,bool Invert);
    static readonly TownCameraInput DefaultTownCameraInput=new(1,false,1,false);
    static readonly float[] TownZoomSteps={.92f,.84f,.74f};
    static readonly float[] TownOrbitRates={.65f,1,1.45f};
    TownCameraInput _townCameraInput=DefaultTownCameraInput;

    static TownCameraInput ReadTownCameraInput(ConfigFile config)=>new(
        Math.Clamp(config.GetValue("camera","zoom",DefaultTownCameraInput.Zoom).AsInt32(),0,TownZoomSteps.Length-1),
        config.GetValue("camera","centered",false).AsBool(),
        Math.Clamp(config.GetValue("camera","orbit",DefaultTownCameraInput.Orbit).AsInt32(),0,TownOrbitRates.Length-1),
        config.GetValue("camera","invert",false).AsBool());

    void WriteTownCameraInput(ConfigFile config)
    {
        config.SetValue("camera","zoom",_townCameraInput.Zoom);
        config.SetValue("camera","centered",_townCameraInput.Centered);
        config.SetValue("camera","orbit",_townCameraInput.Orbit);
        config.SetValue("camera","invert",_townCameraInput.Invert);
    }

    Vector2 TownWheelAnchor(Vector2 pointer)=>_townCameraInput.Centered?GetViewport().GetVisibleRect().GetCenter():pointer;
    float TownWheelFactor(float step)=>Mathf.Pow(TownZoomSteps[_townCameraInput.Zoom],step);

    Vector2 TownOrbitDelta(Vector2 drag)
    {
        float rate=640/Math.Max(640,GetViewport().GetVisibleRect().Size.Y)*TownOrbitRates[_townCameraInput.Orbit];
        return new(-drag.X*.004f*rate,drag.Y*.003f*rate*(_townCameraInput.Invert?-1:1));
    }

    Control TownCameraControls()
    {
        var card=MarketCard(_managementBody!,color:"e8edf1");
        MarketLabel(card,"镜头控制",19,TownTheme.Leaf);
        MarketLabel(card,"选择滚轮缩放的幅度和中心，并调整右键拖动旋转。",color:TownTheme.Muted,wrap:true);
        var row=new HFlowContainer();
        row.AddThemeConstantOverride("h_separation",8);
        row.AddThemeConstantOverride("v_separation",6);
        card.AddChild(row);
        var zoom=AddChoice("TownCameraZoomStep","滚轮步长",new[]{"细调","标准","大步"},_townCameraInput.Zoom);
        var anchor=AddChoice("TownCameraZoomAnchor","缩放中心",new[]{"鼠标位置","屏幕中心"},_townCameraInput.Centered?1:0);
        var orbit=AddChoice("TownCameraOrbitRate","旋转速度",new[]{"柔和","标准","快速"},_townCameraInput.Orbit);
        var invert=new CheckButton{Name="TownCameraInvertPitch",Text="反转纵向旋转",ButtonPressed=_townCameraInput.Invert};
        TownTheme.AccentButton(invert,"地图",5);
        card.AddChild(invert);
        var reset=MarketButton(card,"恢复默认镜头操作",()=>{
            _townCameraInput=DefaultTownCameraInput;
            zoom.Select(_townCameraInput.Zoom);anchor.Select(0);orbit.Select(_townCameraInput.Orbit);
            invert.SetPressedNoSignal(false);
            SaveTownBindings();
        },expand:false,accent:"地图");
        reset.Name="TownCameraInputReset";
        reset.SizeFlagsHorizontal=Control.SizeFlags.ShrinkBegin;
        zoom.ItemSelected+=index=>{_townCameraInput=_townCameraInput with{Zoom=(int)index};SaveTownBindings();};
        anchor.ItemSelected+=index=>{_townCameraInput=_townCameraInput with{Centered=index==1};SaveTownBindings();};
        orbit.ItemSelected+=index=>{_townCameraInput=_townCameraInput with{Orbit=(int)index};SaveTownBindings();};
        invert.Toggled+=value=>{_townCameraInput=_townCameraInput with{Invert=value};SaveTownBindings();};
        var section=card.GetParent<Control>();
        if(_managementEntryTarget=="camera-input")_managementFocusTarget=section;
        return section;

        OptionButton AddChoice(string id,string caption,string[] choices,int selected)
        {
            var group=new HBoxContainer();
            row.AddChild(group);
            MarketLabel(group,caption).VerticalAlignment=VerticalAlignment.Center;
            var input=MarketOptions(group,choices,selected);
            input.Name=id;
            input.AccessibilityName=caption;
            return input;
        }
    }
}
