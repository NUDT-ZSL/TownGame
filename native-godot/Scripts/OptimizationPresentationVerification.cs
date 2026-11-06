using Godot;
using System;
using System.Linq;
using System.Text.Json.Nodes;

public partial class Main
{
    void VerifyOptimizationPresentation(Action<bool,string> check)
    {
        VerifyCameraPresentation(check);
        VerifyDistrictPresentation(check);
        VerifyAtmospherePresentation(check);
        VerifyAmbientPetMeshes(check);
        VerifyDisplayPresentation(check);
        VerifyTownVisualFinishes(check);
    }

    void VerifyCameraPresentation(Action<bool,string> check)
    {
        var pose=(_center,_yaw,_pitch,_distance,_buildCamera,_portraitAnchor,_portraitLift,_portraitRadius);
        var transition=(_cameraTransition,_shownCamera,_cameraFrom,_cameraTo);
        string selected=_selectedPet, selection=_selection;
        bool guideSession=_firstDayGuideSession;
        _firstDayGuideSession=false;
        var pet=Data.Pets.OfType<JsonNode>().First(row=>Game.Active(row)&&FarmPet(NativeGame.S(row,"pet_id"))!=null);
        var actor=FarmPet(NativeGame.S(pet,"pet_id"))!;
        var actorPosition=actor.Position;
        try{
            var viewPicker=_townToolbar!.FindChild("TownCameraPresets",true,false) as MenuButton;
            check(viewPicker?.GetPopup().ItemCount==3,"视角菜单没有三个可用预设");

            SetCameraPreset(CameraPreset.Build);
            UpdateCamera(CameraTransitionTime);
            check(_camera.Projection==Camera3D.ProjectionType.Orthogonal&&_distance==58,"等距建造视角未切换到正交构图");
            SetCameraPreset(CameraPreset.Manor);
            UpdateCamera(CameraTransitionTime/2);
            check(_shownCamera.Distance>58&&_shownCamera.Distance<102,"切换庄园全景时没有经过平滑过渡");
            UpdateCamera(CameraTransitionTime);
            check(_camera.Projection==Camera3D.ProjectionType.Perspective&&_shownCamera.Distance==102
                &&_center==WorldData.Expand(new(135,0,85)),"庄园全景没有落到庄园中心");

            // Use an open patch so the portrait fit is verified independently of wall avoidance.
            actor.Position=new(WorldData.WorldWidth-28,0,WorldData.WorldDepth-26);
            _selectedPet=actor.Ident;
            SetCameraPreset(CameraPreset.Pet);
            UpdateCamera(CameraTransitionTime);
            check(InPetCamera&&_portraitAnchor==actor.Position&&_distance is >=4.2f and <=12,"宠物特写没有按伙伴体型定位");
            var meshBounds=PetMesh(pet,"idle",0).GetAabb();
            float ageScale=Game.Stage(pet) switch{"幼崽"=>.72f,"少年"=>.86f,"老年"=>.94f,_=>1};
            float modelScale=PetWorldScale(actor.Species)*ageScale;
            for(int step=0;step<8;step++)ZoomAt(GetViewport().GetVisibleRect().GetCenter(),.84f);
            var safeFrame=GetViewport().GetVisibleRect().Grow(-8);
            for(int corner=0;corner<8;corner++){
                var point=actor.GlobalTransform*(meshBounds.GetEndpoint(corner)*modelScale);
                check(!_camera.IsPositionBehind(point)&&safeFrame.HasPoint(_camera.UnprojectPosition(point)),"宠物特写仍裁掉模型边缘");
            }

            var building=_dynamic.Values.OfType<MeshInstance3D>().First(mesh=>mesh.GetNodeOrNull<MeshInstance3D>("DistrictArt")!=null
                &&mesh.GetChild(0) is StaticBody3D);
            var wall=building.GetChild<StaticBody3D>(0).GetChild<CollisionShape3D>(0);
            float width=((BoxShape3D)wall.Shape).Size.X*building.Scale.X;
            var offset=Vector3.Right*(width+10);
            var start=wall.GlobalPosition-Vector3.Right*(width/2+5);
            check(CameraClearance(start,offset)<offset.Length()-.5f,"自由镜头没有检测到建筑遮挡");
        }finally{
            actor.Position=actorPosition;
            (_center,_yaw,_pitch,_distance,_buildCamera,_portraitAnchor,_portraitLift,_portraitRadius)=pose;
            (_cameraTransition,_shownCamera,_cameraFrom,_cameraTo)=transition;
            _selectedPet=selected;
            _selection=selection;
            _firstDayGuideSession=guideSession;
            UpdateCamera();
            RefreshScenePetCare();
            RefreshFirstDayGuide();
        }
    }

    void VerifyDistrictPresentation(Action<bool,string> check)
    {
        var landmarks=GetChildren().OfType<MeshInstance3D>().Where(mesh=>mesh.HasMeta("kind")&&mesh.GetMeta("kind").AsString()=="zone").ToArray();
        var market=landmarks.Single(mesh=>mesh.GetMeta("ident").AsString()=="market");
        var industry=landmarks.Where(mesh=>mesh.GetMeta("ident").AsString()=="industry").ToArray();
        var ranch=landmarks.Where(mesh=>mesh.GetMeta("ident").AsString()=="ranch").ToArray();
        var manor=_dynamic.Where(entry=>entry.Key.StartsWith("building:",StringComparison.Ordinal)).Select(entry=>entry.Value)
            .OfType<MeshInstance3D>().First(mesh=>mesh.GetNodeOrNull<MeshInstance3D>("DistrictArt")!=null);
        check(industry.Length>1&&market.GetNodeOrNull<Label3D>("MarketSign")!=null,"街区缺少市集招牌或工业地标");
        check(ranch.Length>0&&ranch.All(mesh=>mesh.GetNodeOrNull<MeshInstance3D>("DistrictArt")!=null)
            &&_dynamic.TryGetValue("district:park",out var park)&&park is MeshInstance3D {Mesh:ArrayMesh},"牧场或公园缺少街区装饰");
        Color[] ReadColors(MeshInstance3D landmark)
        {
            var accent=landmark.GetNodeOrNull<MeshInstance3D>("DistrictArt");
            check(accent?.Mesh is ArrayMesh&&accent.GetChildCount()==0,"街区装饰为空或添加了交互碰撞");
            return ((ArrayMesh)accent!.Mesh).SurfaceGetArrays(0)[(int)Mesh.ArrayType.Color].AsColorArray();
        }
        var marketColors=ReadColors(market).ToHashSet();
        var manorColors=ReadColors(manor).ToHashSet();
        check(marketColors.Count>=4&&manorColors.Count>=4&&!marketColors.SetEquals(manorColors),"市集和庄园装饰仍使用相同配色");
        foreach(var landmark in industry.Prepend(market)){
            var collision=landmark.GetChild<StaticBody3D>(0).GetChild<CollisionShape3D>(0);
            var bounds=landmark.Mesh.GetAabb();
            check(collision.Shape is BoxShape3D box&&box.Size.IsEqualApprox(bounds.Size)
                &&collision.Position.IsEqualApprox(bounds.GetCenter()),"街区美术改变了原来的建筑碰撞范围");
            var colors=ReadColors(landmark);
            if(landmark!=market)check(!marketColors.SetEquals(colors)
                &&landmark.GetNode<MeshInstance3D>("DistrictArt").Mesh.GetAabb().End.Y>bounds.End.Y+.5f,"工业区缺少不同配色或屋顶轮廓");
        }
    }

    void VerifyAtmospherePresentation(Action<bool,string> check)
    {
        var sim=Game.Meta["sim_ts"]!.DeepClone();
        var weather=Game.Meta["weather"]!.DeepClone();
        long rng=(long)NativeGame.D(Game.Meta,"native_rng");
        var center=_center;
        double day=Math.Floor(Game.Now/NativeGame.Day)*NativeGame.Day;
        try{
            check(StreetLampCount>0&&_nearbyStreetLights.Count>0,"场景没有生成路灯");
            _center=_streetLampPositions[0];
            Game.Meta["weather"]="晴天";
            Game.Meta["sim_ts"]=day+12*NativeGame.Hour;
            UpdateAtmosphere();
            var clearSky=_townSky!.SkyTopColor;
            float clearSun=_residentialSun!.LightEnergy, clearFog=_residentialSky!.FogDensity;
            check(TownWindowGlow<.05f&&_nearbyStreetLights.All(light=>!light.Visible),"白天窗户或路灯仍然发光");
            Game.Meta["sim_ts"]=day+22*NativeGame.Hour;
            UpdateAtmosphere();
            check(TownWindowGlow>.9f&&_nearbyStreetLights.Any(light=>light.Visible)
                &&_townSky.SkyTopColor!=clearSky&&_residentialSun.LightEnergy<clearSun,"入夜时天空、光照或夜间灯光没有切换");
            Game.Meta["sim_ts"]=day+12*NativeGame.Hour;
            Game.Meta["weather"]="小雨";
            UpdateAtmosphere();
            check(_residentialSky.FogDensity>clearFog&&_residentialSun.LightEnergy<clearSun,"下雨时雾和阳光没有跟随天气调整");
            Game.Meta["weather"]="晴天";
            UpdateAtmosphere();
            check(TownWindowGlow<.05f&&_townSky.SkyTopColor==clearSky
                &&Mathf.IsEqualApprox(_residentialSky.FogDensity,clearFog)&&Mathf.IsEqualApprox(_residentialSun.LightEnergy,clearSun)
                &&(long)NativeGame.D(Game.Meta,"native_rng")==rng,"恢复晴天后残留前一个天气或消耗了经营随机数");
            using var birds=MakeBirdLoop();
            using var rain=MakeRainLoop();
            check(birds.LoopMode==AudioStreamWav.LoopModeEnum.Forward&&rain.LoopMode==AudioStreamWav.LoopModeEnum.Forward
                &&birds.Data.Any(sample=>sample!=0)&&rain.Data.Any(sample=>sample!=0)&&!birds.Data.SequenceEqual(rain.Data),
                "鸟鸣或雨声没有生成可循环的音频");
        }finally{
            Game.Meta["sim_ts"]=sim;
            Game.Meta["weather"]=weather;
            _center=center;
            UpdateAtmosphere();
        }
    }

    void VerifyAmbientPetMeshes(Action<bool,string> check)
    {
        int count=0;
        foreach(var species in Game.Rules["SPECIES"]!.AsObject()){
            var profile=PetAmbientMotion.ForSpecies(species.Key);
            check(profile.HasValue,$"{species.Key}没有专属环境动作");
            Vector3[] ReadPose(string pose,int frame)=>((ArrayMesh)_petModels.Get(species.Key,0,false,false,"成年",pose,frame))
                .SurfaceGetArrays(0)[(int)Mesh.ArrayType.Vertex].AsVector3Array();
            var idle=ReadPose("idle",0);
            var rest=ReadPose(profile!.Value.Pose,0);
            var active=ReadPose(profile.Value.Pose,2);
            check(active.Length==idle.Length&&idle.SequenceEqual(rest)
                &&idle.Zip(active).Any(pair=>pair.First.DistanceSquaredTo(pair.Second)>.01f),$"{species.Key}环境动作缺少形变或无法平滑回到待机");
            count++;
        }
        check(count==12,"环境动作没有覆盖全部 12 个物种");
    }

    void VerifyDisplayPresentation(Action<bool,string> check)
    {
        var host=GetWindow();
        var originalSize=host.Size;
        int scale=_displayScale, font=_displayFont, sceneQuality=_sceneQuality;
        int graphicsProfile=_graphicsProfile;
        bool highContrast=TownTheme.HighContrast;
        var motion=(_reduceCameraMotion,_reduceWeatherMotion,_reduceInterfaceMotion);
        float birdVolume=_birdVolume, rainVolume=_rainVolume, actionVolume=_townActionVolume;
        string tab=_tab, target=_managementEntryTarget;
        bool visible=_management?.Visible==true;
        try{
            ShowManagement("环境","display");
            var scaleMenu=_managementBody!.FindChild("TownInterfaceScale",true,false) as OptionButton;
            var fontMenu=_managementBody.FindChild("TownInterfaceFont",true,false) as OptionButton;
            var aaMenu=_managementBody.FindChild("TownSceneAntialiasing",true,false) as OptionButton;
            check(scaleMenu?.ItemCount==3&&fontMenu?.ItemCount==3&&_managementFocusTarget!=null,"界面设置入口没有定位到缩放和字号");
            check(aaMenu?.ItemCount==3&&host.Msaa3D==Viewport.Msaa.Msaa4X,"默认画面没有启用 4× 抗锯齿");
            var profile=(OptionButton)_managementBody.FindChild("TownGraphicsProfile",true,false)!;
            var contrast=(CheckButton)_managementBody.FindChild("TownHighContrast",true,false)!;
            profile.Select(0);
            profile.EmitSignal(OptionButton.SignalName.ItemSelected,0);
            check(ActiveGraphics.Fps==30&&host.Msaa3D==Viewport.Msaa.Disabled&&_rain!.Amount==ActiveGraphics.Rain
                &&_residentialSun!.DirectionalShadowMode==DirectionalLight3D.ShadowMode.Parallel2Splits,"省电画质未联动调整雨量、阴影和抗锯齿");
            profile.Select(graphicsProfile);
            profile.EmitSignal(OptionButton.SignalName.ItemSelected,graphicsProfile);
            contrast.ButtonPressed=true;
            check(TownTheme.HighContrast&&TownTheme.Ink==new Color("14251d"),"高对比样式未生效");
            contrast.ButtonPressed=highContrast;
            var cameraMotion=(CheckButton)_managementBody.FindChild("TownReduceCameraMotion",true,false)!;
            var weatherMotion=(CheckButton)_managementBody.FindChild("TownReduceWeatherMotion",true,false)!;
            var interfaceMotion=(CheckButton)_managementBody.FindChild("TownReduceInterfaceMotion",true,false)!;
            cameraMotion.ButtonPressed=weatherMotion.ButtonPressed=interfaceMotion.ButtonPressed=true;
            _cameraTransition=CameraTransitionTime;
            UpdateCamera();
            UpdateWorldLife(0);
            check(_cameraTransition==0&&!_rain!.Visible&&!_snow!.Visible&&TownReducedInterfaceMotion,
                "低动效设置没有分别停止飞镜、粒子和移动提示");
            cameraMotion.ButtonPressed=weatherMotion.ButtonPressed=interfaceMotion.ButtonPressed=false;
            aaMenu!.Select(1);
            aaMenu.EmitSignal(OptionButton.SignalName.ItemSelected,1);
            check(host.Msaa3D==Viewport.Msaa.Msaa2X&&SceneAntialiasing==Viewport.Msaa.Msaa2X,"抗锯齿设置没有更新场景和头像渲染配置");
            var birds=(HSlider)_managementBody.FindChild("BirdAmbienceVolume",true,false)!;
            var rain=(HSlider)_managementBody.FindChild("RainAmbienceVolume",true,false)!;
            birds.Value=0;
            check(_birdVolume==0&&Mathf.IsEqualApprox(_rainVolume,rainVolume),"关闭鸟鸣时同时修改了雨声音量");
            rain.Value=73;
            check(_birdVolume==0&&Mathf.IsEqualApprox(_rainVolume,.73f),"雨声音量没有独立应用");
            var actions=(HSlider)_managementBody.FindChild("TownActionSoundVolume",true,false)!;
            actions.Value=0;
            check(_townActionVolume==0&&_birdVolume==0&&Mathf.IsEqualApprox(_rainVolume,.73f),"静音动作提示时影响了环境声");
            scaleMenu!.Select(2);
            scaleMenu.EmitSignal(OptionButton.SignalName.ItemSelected,2);
            fontMenu!.Select(2);
            fontMenu.EmitSignal(OptionButton.SignalName.ItemSelected,2);
            check(_townToolbar!.Scale.IsEqualApprox(Vector2.One*1.15f)&&_panel.Scale.IsEqualApprox(_townToolbar.Scale)
                &&_townToolbar.Theme.DefaultFontSize==20&&Mathf.IsEqualApprox(_management!.ContentScaleFactor,1.15f),
                "界面缩放和字号没有同时应用到场景界面与经营窗口");

            host.Size=new Vector2I(1024,640);
            ApplyDisplayPreferences();
            if(host.Size==new Vector2I(1024,640))
                check(host.ContentScaleSize==Vector2I.Zero&&host.GetVisibleRect().Size.IsEqualApprox(new Vector2(1024,640)),
                    "1024×640 窗口没有按实际像素显示界面");
            if(host.Size.X<1180){
                RefreshToolbarMenu();
                var popup=_townOverflow!.GetPopup();
                check(_compactToolbar&&_wideToolbarControls.All(control=>!control.Visible)&&_townOverflow.Text=="更多"
                    &&popup.GetItemIndex(SpeedMenu)>=0&&popup.GetItemIndex(SaveMenu)>=0&&popup.GetItemIndex(DistrictMenu)>=0,
                    "小窗口没有把速度、保存和街区入口收入更多菜单");
            }
            string originalPet=_selectedPet;
            try{
                _selectedPet=NativeGame.S(Data.Pets.OfType<JsonNode>().First(Game.Active),"pet_id");
                ShowManagement("外观");
                var preview=_management!.FindChildren("TownPetPreview*","SubViewport",true,false).OfType<SubViewport>().Single();
                check(preview.Msaa3D==Viewport.Msaa.Msaa2X,"新建的外观预览没有沿用当前抗锯齿设置");
            }finally{_selectedPet=originalPet;}
        }finally{
            _displayScale=scale;
            _displayFont=font;
            _sceneQuality=sceneQuality;
            _graphicsProfile=graphicsProfile;
            (_reduceCameraMotion,_reduceWeatherMotion,_reduceInterfaceMotion)=motion;
            _visualClimate="";
            _birdVolume=birdVolume;
            _rainVolume=rainVolume;
            _townActionVolume=actionVolume;
            host.Size=originalSize;
            if(TownTheme.HighContrast!=highContrast)ApplyTownContrast(highContrast);
            ApplyTownGraphicsProfile();
            ApplyDisplayPreferences();
            if(visible)ShowManagement(tab,target);
            else{
                _management?.Hide();
                _tab=tab;
                _managementEntryTarget=target;
            }
        }
    }
}
