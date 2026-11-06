using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

public partial class Main
{
    void VerifyTownSettingsExperience(Action<bool,string> check)
    {
        var display=(_displayScale,_displayFont,_sceneQuality,_graphicsProfile,TownTheme.HighContrast,
            _reduceCameraMotion,_reduceWeatherMotion,_reduceInterfaceMotion);
        var camera=_townCameraInput;
        var bindings=new Dictionary<string,TownKey>(_townBindings);
        var audio=(_townAudioVolume,_townAudioMuted);
        try{
            ShowManagement("环境","display");
            var index=_environmentSectionMenu!;
            index.EmitSignal(MenuButton.SignalName.AboutToPopup);
            check(index.Visible&&index.GetPopup().ItemCount==6&&index.GetPopup().GetItemText(2)=="镜头控制"&&
                _environmentSections[2].Target.FindChild("TownCameraZoomStep",true,false)!=null,"设置目录缺少实际章节入口");
            Choose("TownGraphicsProfile",0);
            check(Find<Label>("TownEffectiveDisplay").Text.Contains("30 FPS")&&Find<Label>("TownEffectiveDisplay").Text.Contains("抗锯齿 关闭"),
                "画质摘要未反映实际预设和抗锯齿");
            Choose("TownSceneAntialiasing",1);
            check(Find<Label>("TownEffectiveDisplay").Text.Contains("抗锯齿 2×"),"画质摘要未显示单独调整的抗锯齿");
            Find<Button>("TownDisplayReset").EmitSignal(BaseButton.SignalName.Pressed);
            var confirm=_management!.GetChildren().OfType<ConfirmationDialog>().Last(dialog=>dialog.Visible);
            confirm.Hide();
            confirm.EmitSignal(AcceptDialog.SignalName.Confirmed);
            check(_displayScale==1&&_displayFont==0&&_graphicsProfile==2&&_sceneQuality==2&&!TownTheme.HighContrast,
                "确认后没有恢复显示设置默认值");

            ShowManagement("环境","camera-input");
            _townCameraInput=DefaultTownCameraInput;
            var defaultOrbit=TownOrbitDelta(new(60,40));
            Choose("TownCameraZoomStep",2);
            Choose("TownCameraZoomAnchor",1);
            Choose("TownCameraOrbitRate",2);
            var invert=Find<CheckButton>("TownCameraInvertPitch");
            invert.SetPressedNoSignal(false);
            invert.ButtonPressed=true;
            var tunedOrbit=TownOrbitDelta(new(60,40));
            check(Mathf.IsEqualApprox(TownWheelFactor(1),.74f)&&TownWheelAnchor(new(20,25))==GetViewport().GetVisibleRect().GetCenter()&&
                tunedOrbit.X<defaultOrbit.X&&tunedOrbit.Y<0,"镜头设置未影响实际滚轮和旋转输入参数");
            using(var config=new ConfigFile()){
                WriteTownCameraInput(config);
                check(ReadTownCameraInput(config)==_townCameraInput,"镜头设置无法从快捷键配置读回");
            }
            Find<Button>("TownCameraInputReset").EmitSignal(BaseButton.SignalName.Pressed);
            check(_townCameraInput==DefaultTownCameraInput&&!invert.ButtonPressed,"镜头重置未同步选项");

            _townBindings=TownActions.ToDictionary(action=>action.Id,action=>action.Default);
            ShowManagement("环境","keybindings");
            var change=Find<Button>("TownInput_rotate");
            change.EmitSignal(BaseButton.SignalName.Pressed);
            var capture=KeyDialog();
            capture.EmitSignal(Window.SignalName.WindowInput,new InputEventKey{Pressed=true,Keycode=Key.M});
            check(_townBindings["rotate"]==new TownKey(Key.R)&&capture.FindChild("TownKeySwap",true,false) is Button {Visible:true},
                "快捷键冲突在确认前改动绑定或未提供交换");
            capture.EmitSignal(AcceptDialog.SignalName.CustomAction,"swap");
            check(_townBindings["rotate"]==new TownKey(Key.M)&&_townBindings["map"]==new TownKey(Key.R),"确认后未交换两项实际绑定");
            change.EmitSignal(BaseButton.SignalName.Pressed);
            capture=KeyDialog();
            capture.EmitSignal(AcceptDialog.SignalName.CustomAction,"restore");
            check(capture.Visible&&_townBindings["rotate"]==new TownKey(Key.M),"恢复默认键绕过了冲突检查");
            capture.EmitSignal(AcceptDialog.SignalName.CustomAction,"swap");
            check(_townBindings["rotate"]==new TownKey(Key.R)&&_townBindings["map"]==new TownKey(Key.M),"无法从单项设置恢复默认键");

            ShowManagement("环境","audio");
            int bus=AudioServer.GetBusIndex(TownAudioBus);
            float bird=_birdVolume,rain=_rainVolume,district=_districtSoundVolume,actions=_townActionVolume;
            var volume=Find<HSlider>("TownMasterAudioVolume");
            volume.Value=45;
            Find<CheckButton>("TownMasterAudioMute").ButtonPressed=true;
            check(bus>=0&&AudioServer.IsBusMute(bus)&&Mathf.IsEqualApprox(AudioServer.GetBusVolumeLinear(bus),.45f)&&
                (bird,rain,district,actions)==(_birdVolume,_rainVolume,_districtSoundVolume,_townActionVolume),"总静音未使用独立总线或改动了单项音量");
            RefreshToolbarMenu();
            var quick=_townOverflow!.GetPopup();
            check(quick.GetItemText(quick.GetItemIndex(MasterAudioMenu))=="恢复小镇声音","顶栏未反映总静音状态");
            quick.EmitSignal(PopupMenu.SignalName.IdPressed,(long)MasterAudioMenu);
            check(!AudioServer.IsBusMute(bus)&&Mathf.IsEqualApprox(AudioServer.GetBusVolumeLinear(bus),.45f),"顶栏恢复声音丢失总音量");
            GD.Print("TOWN_SETTINGS_EXPERIENCE_VERIFIED section_index display_reset camera_input key_swap audio_mix");
        }finally{
            (_displayScale,_displayFont,_sceneQuality,_graphicsProfile,TownTheme.HighContrast,
                _reduceCameraMotion,_reduceWeatherMotion,_reduceInterfaceMotion)=display;
            _visualClimate="";
            _townCameraInput=camera;
            _townBindings=bindings;
            (_townAudioVolume,_townAudioMuted)=audio;
            ApplyTownAudioMix();
            ApplyTownGraphicsProfile();
            ApplyTownContrast(TownTheme.HighContrast);
            _management?.Hide();
        }

        T Find<T>(string name) where T:Node=>(T)_managementBody!.FindChild(name,true,false)!;
        void Choose(string name,int index){var option=Find<OptionButton>(name);option.Select(index);option.EmitSignal(OptionButton.SignalName.ItemSelected,index);}
        AcceptDialog KeyDialog()=>_management!.GetChildren().OfType<AcceptDialog>()
            .Last(dialog=>dialog.Visible&&dialog.FindChild("TownKeySwap",true,false)!=null);
    }
}
