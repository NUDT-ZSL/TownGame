using Godot;
using System;
using System.Buffers.Binary;

public partial class Main
{
    const string DistrictSoundSettings="user://town-district-audio.cfg";
    enum DistrictSound { Fountain, Pasture, Freight }
    readonly AudioStreamPlayer3D?[] _districtSoundPlayers=new AudioStreamPlayer3D?[3];
    readonly float[] _districtSoundGains=new float[3];
    Node3D? _districtFountain;
    string _districtFountainLayout="";
    bool _districtSoundLoaded;
    float _districtSoundVolume=.55f;
    double _townLifeRefresh;

    void UpdateTownLifeFeatures(double delta)
    {
        if(_petFacilityScene==null)CreatePetFacilities();
        _townLifeRefresh-=delta;
        if(_townLifeRefresh<=0){
            _townLifeRefresh=.35;
            UpdateFestivalStage();
            UpdateFarmGrowthView();
            UpdateDistrictFountain();
            RefreshIncubatorModel();
            RefreshNeighborhoodResults();
        }
        if(DisplayServer.GetName()!="headless"&&!_benchmark)UpdateDistrictSound(delta);
    }

    bool TrySelectTownLifeFeature(string kind,string id)
    {
        if(kind=="incubator"){
            _orderSupplyRoute=null;
            _careSupplyRoute=null;
            _marketSection=0;
            ShowManagement("商店");
            return true;
        }
        if(kind=="weekend_festival"){
            ShowManagement("事件","weekend-festivals");
            return true;
        }
        if(kind!="farm_growth"||!int.TryParse(id,out int plot)||plot<1||plot>NativeGame.FarmPlotCount)return false;
        _activeFarmPlot=plot;
        ShowManagement("环境","farm-plots");
        return true;
    }

    void UpdateDistrictFountain()
    {
        string layout=_layoutSignature+"|"+Game.ZoneLevel("park");
        if(_districtFountainLayout==layout)return;
        _districtFountainLayout=layout;
        _districtFountain?.QueueFree();_districtFountain=null;
        if(Game.ZoneLevel("park")==0)return;
        var candidates=OpenDistrictCells("park");
        if(candidates.Length<2)return;
        var origin=WorldData.Cell(candidates[1]);
        var model=new DistrictAccentMesh();
        model.Round(new(0,.23f,0),new(3.9f,.46f,3.9f),"c2ae88");
        model.Round(new(0,.42f,0),new(3.28f,.19f,3.28f),"7ab0b4");
        model.Round(new(0,.83f,0),new(.65f,1.29f,.65f),"bda987");
        model.Round(new(0,1.45f,0),new(1.5f,.25f,1.5f),"cfbb94");
        model.Round(new(0,1.62f,0),new(.37f,.25f,.37f),"94c3c7");
        for(int i=0;i<5;i++){
            float angle=i*Mathf.Tau/5;
            model.Round(new(.46f*Mathf.Sin(angle),1.20f,.46f*Mathf.Cos(angle)),new(.09f,.42f,.09f),"a4d1d0");
        }
        _districtFountain=new MeshInstance3D{Name="ParkFountain",Mesh=model.Finish(),Position=origin};
        AddChild(_districtFountain);
    }

    void LoadDistrictSoundSettings()
    {
        if(_districtSoundLoaded)return;
        _districtSoundLoaded=true;
        if(!UseTownPreferences())return;
        using var config=new ConfigFile();
        if(config.Load(DistrictSoundSettings)==Error.Ok){
            float stored=config.GetValue("district","volume",_districtSoundVolume).AsSingle();
            if(float.IsFinite(stored))_districtSoundVolume=Math.Clamp(stored,0,1);
        }
    }

    void DistrictSoundControls()
    {
        LoadDistrictSoundSettings();
        var card=MarketCard(_managementBody!,color:"e5efe9");
        MarketLabel(card,"街区环境声",18,TownTheme.Accent("环境"));
        MarketLabel(card,"接近公园喷泉、牧场或行驶中的货车时逐渐听到对应声音。",color:TownTheme.Muted,wrap:true);
        var row=new HBoxContainer();row.AddThemeConstantOverride("separation",9);card.AddChild(row);
        var volume=new HSlider{Name="TownDistrictSoundVolume",MinValue=0,MaxValue=100,Step=1,Value=_districtSoundVolume*100,
            SizeFlagsHorizontal=Control.SizeFlags.ExpandFill,SizeFlagsVertical=Control.SizeFlags.ShrinkCenter};
        row.AddChild(volume);
        var label=MarketLabel(row,$"{volume.Value:0}%",color:TownTheme.Muted);
        bool dragging=false;
        volume.DragStarted+=()=>dragging=true;
        volume.DragEnded+=changed=>{dragging=false;if(changed)Save();};
        volume.ValueChanged+=value=>{_districtSoundVolume=(float)value/100;label.Text=$"{value:0}%";if(!dragging)Save();};

        void Save()
        {
            if(!UseTownPreferences())return;
            using var config=new ConfigFile();
            config.SetValue("district","volume",_districtSoundVolume);
            if(config.Save(DistrictSoundSettings)!=Error.Ok)SetManagementFeedback("街区环境声音量未能保存。",true);
        }
    }

    void UpdateDistrictSound(double delta)
    {
        LoadDistrictSoundSettings();
        SetSource(DistrictSound.Fountain,_districtFountain?.GlobalPosition,36,.63f);
        SetSource(DistrictSound.Pasture,_herd.Count>0?_pasture:null,42,.53f);
        var truck=_freightTruck;
        SetSource(DistrictSound.Freight,!Paused&&truck!=null&&truck.Visible&&truck.Velocity.LengthSquared()>.25f?truck.GlobalPosition:null,43,.52f);

        void SetSource(DistrictSound sound,Vector3? location,float range,float peak)
        {
            int index=(int)sound;
            float distance=location?.DistanceTo(_camera.GlobalPosition)??range;
            float target=peak*_districtSoundVolume*(1-Mathf.SmoothStep(range*.26f,range,distance));
            float gain=_districtSoundGains[index]=Mathf.Lerp(_districtSoundGains[index],target,1-Mathf.Exp(-(float)delta*5));
            var player=_districtSoundPlayers[index];
            if(gain<.001f){if(player?.Playing==true)player.Stop();return;}
            if(player==null){
                player=new AudioStreamPlayer3D{Name=$"DistrictSound{sound}",Stream=CreateDistrictLoop(sound),Bus=TownAudioBus,
                    AttenuationModel=AudioStreamPlayer3D.AttenuationModelEnum.Disabled,VolumeDb=-60};
                _districtSoundPlayers[index]=player;AddChild(player);
            }
            if(location is Vector3 position)player.GlobalPosition=position+Vector3.Up*.7f;
            player.VolumeDb=Mathf.LinearToDb(gain);
            if(!player.Playing)player.Play();
        }
    }

    static AudioStreamWav CreateDistrictLoop(DistrictSound sound)
    {
        const int rate=22050,seconds=3;
        const int frames=rate*seconds;
        var bytes=new byte[frames*sizeof(short)];
        var random=new Random(410+(int)sound);
        double filtered=0;
        for(int index=0;index<frames;index++){
            double t=(double)index/rate;
            filtered=filtered*.86+(random.NextDouble()*2-1)*.14;
            double sample=sound switch{
                DistrictSound.Fountain=>filtered*.20+Math.Sin(Math.Tau*(156*t+Math.Sin(Math.Tau*2*t)*.21))*.024,
                DistrictSound.Pasture=>Pasture(t,filtered),
                _=>(Math.Sin(Math.Tau*49*t)+.32*Math.Sin(Math.Tau*98*t)+.14*Math.Sin(Math.Tau*147*t))*.11+
                    filtered*.047
            };
            double edge=Math.Min(1,Math.Min(t,seconds-t)*48);
            BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(index*sizeof(short)),(short)(sample*edge*short.MaxValue));
        }
        return new AudioStreamWav{Format=AudioStreamWav.FormatEnum.Format16Bits,MixRate=rate,Data=bytes,
            LoopMode=AudioStreamWav.LoopModeEnum.Forward,LoopEnd=frames};

        static double Pasture(double t,double noise)
        {
            double bleat=Math.Max(0,1-Math.Abs(t-1.33)*3.4);
            return noise*.069+bleat*(Math.Sin(Math.Tau*(176*t+Math.Sin(Math.Tau*4*t)*.41))*.073+
                Math.Sin(Math.Tau*348*t)*.02);
        }
    }
}
