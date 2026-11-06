using Godot;
using System;

public partial class Main
{
    const string TownAudioBus="Town Mix";
    const string TownAudioMixPath="user://town-audio-mix.cfg";
    float _townAudioVolume=1;
    bool _townAudioMuted;

    void InitializeTownAudioMix()
    {
        if(UseTownPreferences()){
            using var config=new ConfigFile();
            if(config.Load(TownAudioMixPath)==Error.Ok){
                float volume=config.GetValue("mix","volume",1f).AsSingle();
                _townAudioVolume=float.IsFinite(volume)?Math.Clamp(volume,0,1):1;
                _townAudioMuted=config.GetValue("mix","muted",false).AsBool();
            }
        }
        if(AudioServer.GetBusIndex(TownAudioBus)<0){
            AudioServer.AddBus();
            AudioServer.SetBusName(AudioServer.BusCount-1,TownAudioBus);
        }
        ApplyTownAudioMix();
    }

    void ApplyTownAudioMix()
    {
        int bus=AudioServer.GetBusIndex(TownAudioBus);
        if(bus<0)return;
        AudioServer.SetBusVolumeLinear(bus,Math.Max(_townAudioVolume,.0001f));
        AudioServer.SetBusMute(bus,_townAudioMuted||_townAudioVolume==0);
    }

    void SetTownAudioMuted(bool muted)
    {
        _townAudioMuted=muted;
        ApplyTownAudioMix();
        SaveTownAudioMix();
    }

    void SaveTownAudioMix()
    {
        if(!UseTownPreferences())return;
        using var config=new ConfigFile();
        config.SetValue("mix","volume",_townAudioVolume);
        config.SetValue("mix","muted",_townAudioMuted);
        if(config.Save(TownAudioMixPath)!=Error.Ok){
            string message="总音量已调整，设置暂时无法写入。";
            if(_management?.Visible==true)SetManagementFeedback(message,true);
            else _selection=message;
        }
    }

    Control TownMasterAudioControls()
    {
        var card=MarketCard(_managementBody!,color:"e7efe8");
        MarketLabel(card,"小镇总音量",19,TownTheme.Leaf);
        var row=new HBoxContainer();
        row.AddThemeConstantOverride("separation",8);
        card.AddChild(row);
        var gain=new HSlider{Name="TownMasterAudioVolume",MinValue=0,MaxValue=100,Step=1,Value=Math.Round(_townAudioVolume*100),
            SizeFlagsHorizontal=Control.SizeFlags.ExpandFill,SizeFlagsVertical=Control.SizeFlags.ShrinkCenter,
            AccessibilityName="小镇总音量"};
        row.AddChild(gain);
        var valueLabel=MarketLabel(row,$"{gain.Value:0}%",color:TownTheme.Muted);
        valueLabel.CustomMinimumSize=new(45,0);
        var mute=new CheckButton{Name="TownMasterAudioMute",Text="静音",ButtonPressed=_townAudioMuted};
        TownTheme.AccentButton(mute,"环境",5);
        row.AddChild(mute);
        MarketLabel(card,"同时控制环境音、街区声和动作提示。恢复声音后会沿用各项音量。",color:TownTheme.Muted,wrap:true);
        bool dragging=false;
        gain.DragStarted+=()=>dragging=true;
        gain.DragEnded+=changed=>{dragging=false;if(changed)SaveTownAudioMix();};
        gain.ValueChanged+=value=>{
            _townAudioVolume=(float)value/100;
            valueLabel.Text=$"{value:0}%";
            ApplyTownAudioMix();
            if(!dragging)SaveTownAudioMix();
        };
        mute.Toggled+=SetTownAudioMuted;
        _refreshManagementPage+=()=>mute.SetPressedNoSignal(_townAudioMuted);
        var section=card.GetParent<Control>();
        if(_managementEntryTarget=="audio")_managementFocusTarget=section;
        return section;
    }
}
