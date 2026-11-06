using Godot;
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;

public partial class Main
{
    const string TownActionSoundPath="user://town-action-audio.cfg";
    enum TownActionCue { Feed, Harvest, Road, Delivery }
    readonly Dictionary<TownActionCue,AudioStreamWav> _townActionSounds=new();
    AudioStreamPlayer? _townActionSoundPlayer;
    float _townActionVolume=.65f;
    bool _townActionSoundLoaded;

    static TownActionCue? TownActionCueFor(string action,JsonObject args)=>action switch{
        "feed"=>TownActionCue.Feed,
        "batch_care" when NativeGame.S(args,"kind")=="feed"=>TownActionCue.Feed,
        "harvest" or "collect"=>TownActionCue.Harvest,
        "farm_plot" when NativeGame.S(args,"operation")=="harvest"=>TownActionCue.Harvest,
        "road" or "edit_road"=>TownActionCue.Road,
        "road_stroke" when !NativeGame.B(args,"remove")=>TownActionCue.Road,
        "blueprint" when args["steps"] is JsonArray steps&&steps.OfType<JsonObject>().Any(step=>NativeGame.S(step,"action")=="road_stroke")=>TownActionCue.Road,
        "order"=>TownActionCue.Delivery,
        "town_commission" when NativeGame.S(args,"step")=="deliver"=>TownActionCue.Delivery,
        _=>null,
    };

    void PlayTownActionCue(string action,JsonObject args)
    {
        if(action=="batch_care"&&Game.LastBatchCare.Count==0)return;
        if(TownActionCueFor(action,args) is TownActionCue cue)PlayTownActionSound(cue);
    }

    void LoadTownActionSound()
    {
        if(_townActionSoundLoaded)return;
        _townActionSoundLoaded=true;
        if(!UseTownPreferences())return;
        using var settings=new ConfigFile();
        if(settings.Load(TownActionSoundPath)!=Error.Ok)return;
        float stored=settings.GetValue("volume","actions",_townActionVolume).AsSingle();
        if(float.IsFinite(stored))_townActionVolume=Math.Clamp(stored,0,1);
    }

    void PlayTownActionSound(TownActionCue cue)
    {
        if(!UseTownPreferences())return;
        LoadTownActionSound();
        if(_townActionVolume<=0)return;
        if(!_townActionSounds.TryGetValue(cue,out var stream))_townActionSounds[cue]=stream=CreateTownActionSound(cue);
        if(_townActionSoundPlayer==null){
            _townActionSoundPlayer=new AudioStreamPlayer{Name="TownActionAudio",Bus=TownAudioBus};
            AddChild(_townActionSoundPlayer);
        }
        _townActionSoundPlayer.Stream=stream;
        _townActionSoundPlayer.VolumeLinear=_townActionVolume;
        _townActionSoundPlayer.Play();
    }

    void TownActionSoundControls()
    {
        LoadTownActionSound();
        Section("动作提示音","喂食、收获、铺路和交付各有提示");
        var panel=MarketCard(_managementBody!,color:"e8efe5");
        var row=new HBoxContainer();panel.AddChild(row);
        MarketLabel(row,"动作音量").CustomMinimumSize=new(128,0);
        var volume=new HSlider{Name="TownActionSoundVolume",MinValue=0,MaxValue=100,Step=1,
            Value=Math.Round(_townActionVolume*100),SizeFlagsHorizontal=Control.SizeFlags.ExpandFill,
            SizeFlagsVertical=Control.SizeFlags.ShrinkCenter,TooltipText="独立调节动作提示音，0 为静音"};
        row.AddChild(volume);
        var number=MarketLabel(row,$"{volume.Value:0}%",color:TownTheme.Muted);
        number.CustomMinimumSize=new(46,0);
        var preview=new Button{Text="试听",Name="TownActionSoundPreview",Disabled=volume.Value==0};
        TownTheme.AccentButton(preview,"环境");row.AddChild(preview);
        int nextCue=0;
        preview.Pressed+=()=>{
            PlayTownActionSound((TownActionCue)nextCue);
            nextCue=(nextCue+1)%4;
        };
        bool dragging=false;
        volume.DragStarted+=()=>dragging=true;
        volume.DragEnded+=changed=>{dragging=false;if(changed)SaveVolume();};
        volume.ValueChanged+=value=>{
            _townActionVolume=(float)value/100;
            number.Text=$"{value:0}%";
            preview.Disabled=value==0;
            if(_townActionSoundPlayer!=null){
                _townActionSoundPlayer.VolumeLinear=_townActionVolume;
                if(value==0)_townActionSoundPlayer.Stop();
            }
            if(!dragging)SaveVolume();
        };

        void SaveVolume()
        {
            if(!UseTownPreferences())return;
            using var settings=new ConfigFile();
            settings.SetValue("volume","actions",_townActionVolume);
            if(settings.Save(TownActionSoundPath)!=Error.Ok)SetManagementFeedback("动作音量已调整，写入设置失败。",true);
        }
    }

    static AudioStreamWav CreateTownActionSound(TownActionCue cue)
    {
        (double Start,double Duration,double Hz,double Harmonic)[] notes=cue switch{
            TownActionCue.Feed=>new[]{(0.0,.095,460.0,.30),(.079,.15,610.0,.24)},
            TownActionCue.Harvest=>new[]{(0.0,.12,550.0,.14),(.081,.14,690.0,.15),(.164,.19,880.0,.16)},
            TownActionCue.Road=>new[]{(0.0,.078,145.0,.65),(.102,.108,195.0,.54)},
            _=>new[]{(0.0,.21,523.25,.20),(.125,.23,784.0,.21),(.148,.20,659.25,.14)},
        };
        const int rate=22050;
        var samples=new byte[(int)(.4*rate)*sizeof(short)];
        for(int i=0;i<samples.Length/sizeof(short);i++){
            double sample=0,time=(double)i/rate;
            foreach(var (start,duration,hz,harmonic) in notes){
                double elapsed=time-start;
                if(elapsed<=0||elapsed>=duration)continue;
                double progress=elapsed/duration;
                double envelope=Math.Min(1,elapsed*230)*(1-progress)*(1-progress);
                double phase=Math.Tau*hz*elapsed;
                sample+=.14*envelope*(Math.Sin(phase)+harmonic*Math.Sin(phase*2.03));
            }
            BinaryPrimitives.WriteInt16LittleEndian(samples.AsSpan(i*sizeof(short)),(short)(sample*short.MaxValue));
        }
        return new AudioStreamWav{Format=AudioStreamWav.FormatEnum.Format16Bits,Data=samples,MixRate=rate};
    }
}
