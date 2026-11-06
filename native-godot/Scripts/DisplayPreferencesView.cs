using Godot;
using System;
using System.Linq;

public partial class Main
{
    const string DisplayPreferencesPath="user://town-display.cfg";
    static readonly Vector2I MinimumCanvasSize=new(1024,640);
    static readonly int[] DisplayScales={90,100,115};
    static readonly int[] DisplayFontSteps={0,2,4};
    static readonly Viewport.Msaa[] SceneSampleModes={Viewport.Msaa.Disabled,Viewport.Msaa.Msaa2X,Viewport.Msaa.Msaa4X};
    int _displayScale=1, _displayFont, _sceneQuality=2;
    bool _reduceCameraMotion,_reduceWeatherMotion,_reduceInterfaceMotion;
    internal bool TownReducedInterfaceMotion=>_reduceInterfaceMotion;
    PanelContainer? _townToolbar;
    HFlowContainer? _townToolbarRow;
    MenuButton? _townOverflow;
    Control[] _wideToolbarControls=Array.Empty<Control>();
    bool _compactToolbar;
    Action<int>? _goToToolbarDistrict;
    Button? _speedControl,_saveControl;
    const int SpeedMenu=11, SaveMenu=2, DisplayMenu=3, PhotoMenu=4, DistrictMenu=20;
    const int FastMorningMenu=5, FastConstructionMenu=6, FastCommissionMenu=7, FastStopMenu=8;
    const int GlobalSearchMenu=9, InputPreferencesMenu=10, MapMenu=14, MasterAudioMenu=15;
    const int TimePlannerMenu=16;
    static readonly int[] TownSpeedChoices={1,120,600};

    static bool UseTownPreferences()=>DisplayServer.GetName()!="headless"&&!OS.GetCmdlineUserArgs().Any(arg=>
        arg is "--smoke" or "--ui-smoke"||arg.StartsWith("--verify-",StringComparison.Ordinal)||
        arg.StartsWith("--benchmark",StringComparison.Ordinal)||arg.EndsWith("-preview",StringComparison.Ordinal));

    void LoadDisplayPreferences()
    {
        TownTheme.HighContrast=false;
        using var config=new ConfigFile();
        if(UseTownPreferences()&&config.Load(DisplayPreferencesPath)==Error.Ok){
            _displayScale=Math.Clamp(config.GetValue("ui","scale",1).AsInt32(),0,DisplayScales.Length-1);
            _displayFont=Math.Clamp(config.GetValue("ui","font",0).AsInt32(),0,DisplayFontSteps.Length-1);
            _sceneQuality=Math.Clamp(config.GetValue("graphics","antialiasing",2).AsInt32(),0,SceneSampleModes.Length-1);
            TownTheme.HighContrast=config.GetValue("ui","contrast",false).AsBool();
            _reduceCameraMotion=config.GetValue("motion","camera",false).AsBool();
            _reduceWeatherMotion=config.GetValue("motion","weather",false).AsBool();
            _reduceInterfaceMotion=config.GetValue("motion","interface",false).AsBool();
            _graphicsProfile=Math.Clamp(config.GetValue("graphics","profile",2).AsInt32(),0,GraphicsProfiles.Length-1);
        }
        ApplyTownFrameLimit();
    }

    void SetupDisplayPreferences(PanelContainer top,HFlowContainer row,Container controls,MenuButton speed,Button save,OptionButton districts,Action<int> goToDistrict)
    {
        _townToolbar=top;
        _townToolbarRow=row;
        _wideToolbarControls=new Control[]{speed,save,districts};
        _goToToolbarDistrict=goToDistrict;
        _speedControl=speed;
        _saveControl=save;
        speed.AboutToPopup+=()=>{
            var menu=speed.GetPopup();
            menu.Clear();
            AddTownSpeedChoices(menu);
            menu.AddItem("打开时间计划",TimePlannerMenu);
            if(_fastForward.HasValue)menu.AddItem("停止快进",FastStopMenu);
        };
        speed.GetPopup().IdPressed+=id=>{
            if(id==FastStopMenu)StopFastForward("已停止快进 · 已恢复正常速度");
            else if(id==TimePlannerMenu)OpenTownTimePlanner();
            else SetTownSpeedFromMenu(id);
        };
        _townOverflow=new MenuButton{Name="TownToolbarMore",Text="设置",Flat=false};
        TownTheme.AccentButton(_townOverflow,"环境",6);
        controls.AddChild(_townOverflow);
        _townOverflow.AboutToPopup+=RefreshToolbarMenu;
        _townOverflow.GetPopup().IdPressed+=id=>{
            if(id>=SpeedMenu&&id<SpeedMenu+TownSpeedChoices.Length)SetTownSpeedFromMenu(id);
            else if(id==SaveMenu)Save();
            else if(id==DisplayMenu)ShowManagement("环境","display");
            else if(id==MapMenu)ShowManagement("地图");
            else if(id==PhotoMenu)TogglePhotoMode();
            else if(id==FastMorningMenu)BeginFastForward(FastForwardGoal.Morning);
            else if(id==FastConstructionMenu)BeginFastForward(FastForwardGoal.Construction);
            else if(id==FastCommissionMenu)BeginFastForward(FastForwardGoal.Commission);
            else if(id==FastStopMenu)StopFastForward("已停止快进 · 已恢复正常速度");
            else if(id==TimePlannerMenu)OpenTownTimePlanner();
            else if(id==GlobalSearchMenu)OpenTownSearch();
            else if(id==InputPreferencesMenu)ShowManagement("环境","keybindings");
            else if(id==MasterAudioMenu)SetTownAudioMuted(!_townAudioMuted);
            else if(id>=DistrictMenu&&id<DistrictMenu+districts.ItemCount){
                int index=(int)id-DistrictMenu;
                districts.Select(index);
                _goToToolbarDistrict?.Invoke(index);
            }
        };
        var host=GetWindow();
        host.SizeChanged+=ApplyDisplayPreferences;
        TreeExiting+=()=>host.SizeChanged-=ApplyDisplayPreferences;
        top.Resized+=PositionTownCards;
        _panel.Resized+=PositionTownCards;
        ApplyDisplayPreferences();
    }

    void AddTownSpeedChoices(PopupMenu menu)
    {
        int current=WorldData.I(Data.Meta,"native_speed",1);
        for(int i=0;i<TownSpeedChoices.Length;i++){
            int speed=TownSpeedChoices[i];
            menu.AddRadioCheckItem(speed==1?"正常速度 · ×1":$"快速推进 · ×{speed}",SpeedMenu+i);
            menu.SetItemChecked(menu.ItemCount-1,!_fastForward.HasValue&&current==speed);
        }
    }

    void SetTownSpeedFromMenu(long id)
    {
        if(id<SpeedMenu||id>=SpeedMenu+TownSpeedChoices.Length)return;
        if(_fastForward.HasValue)StopFastForward("已停止目标快进");
        Data.Meta["native_speed"]=TownSpeedChoices[(int)id-SpeedMenu];
        RefreshSpeedControl();
    }

    void RefreshToolbarMenu()
    {
        var menu=_townOverflow!.GetPopup();
        menu.Clear();
        menu.AddSeparator("小镇工具");
        menu.AddItem($"打开地图（{TownBindingText("map")}）",MapMenu);
        menu.AddItem($"搜索小镇（{TownBindingText("search")}）",GlobalSearchMenu);
        menu.AddItem($"拍照模式（{TownBindingText("photo")}）",PhotoMenu);
        menu.AddItem("界面与声音设置",DisplayMenu);
        menu.AddItem("快捷键设置",InputPreferencesMenu);
        menu.AddItem(_townAudioMuted?"恢复小镇声音":"静音小镇",MasterAudioMenu);
        menu.AddSeparator("智能快进");
        menu.AddItem("打开时间计划",TimePlannerMenu);
        if(_fastForward.HasValue)menu.AddItem("停止快进",FastStopMenu);
        AddGoal("快进到次日 07:00",FastMorningMenu,FastForwardGoal.Morning);
        AddGoal("快进到下一项完工",FastConstructionMenu,FastForwardGoal.Construction);
        AddGoal("快进到季节委托交付时段",FastCommissionMenu,FastForwardGoal.Commission);
        if(!_compactToolbar)return;
        menu.AddSeparator("时间与进度");
        AddTownSpeedChoices(menu);
        menu.AddItem(_saveWriteError.Length>0?"重试保存":"保存",SaveMenu);
        menu.AddSeparator("前往街区");
        int index=0;
        foreach(var zone in Data.Root["zones"]!.AsObject())menu.AddItem(NativeGame.S(zone.Value!,"name"),DistrictMenu+index++);

        void AddGoal(string label,int id,FastForwardGoal goal){
            menu.AddItem(label,id);
            menu.SetItemDisabled(menu.GetItemIndex(id),_fastForward.HasValue||FindFastForwardTarget(goal)==null);
        }
    }

    float UiScale=>DisplayScales[_displayScale]/100f;
    int ExtraFontSize=>DisplayFontSteps[_displayFont];
    internal Viewport.Msaa SceneAntialiasing=>SceneSampleModes[_sceneQuality];

    void ApplyDisplayPreferences()
    {
        if(_townToolbar==null||_townToolbarRow==null||_townOverflow==null)return;
        var host=GetWindow();
        var size=host.Size;
        var canvasSize=size.X<MinimumCanvasSize.X||size.Y<MinimumCanvasSize.Y?MinimumCanvasSize:Vector2I.Zero;
        if(host.ContentScaleSize!=canvasSize)host.ContentScaleSize=canvasSize;
        ApplySceneAntialiasing(host);
        var visible=host.GetVisibleRect().Size;
        _townToolbar.Theme.DefaultFontSize=16+ExtraFontSize;
        _townToolbar.Scale=_panel.Scale=Vector2.One*UiScale;
        if(_firstDayGuidePanel!=null)_firstDayGuidePanel.Scale=Vector2.One*UiScale;
        float limit=Math.Max(200,(visible.X-36)/UiScale);
        _compactToolbar=size.X<1180||ToolbarWidth(includeWide:true)>limit;
        foreach(var control in _wideToolbarControls)control.Visible=!_compactToolbar;
        _townOverflow.Text=_compactToolbar?"更多":"设置";
        _townToolbar.Size=new(Math.Min(ToolbarWidth(includeWide:!_compactToolbar),limit),_townToolbar.GetCombinedMinimumSize().Y);
        PositionTownCards();
        OnManagementHostResized();

        float ToolbarWidth(bool includeWide)
        {
            var groups=_townToolbarRow.GetChildren().OfType<HBoxContainer>().ToArray();
            return groups.Sum(group=>{
                var controls=group.GetChildren().OfType<Control>().Where(control=>includeWide||!_wideToolbarControls.Contains(control)).ToArray();
                return controls.Sum(control=>control.GetCombinedMinimumSize().X)+(controls.Length-1)*group.GetThemeConstant("separation");
            })+(groups.Length-1)*_townToolbarRow.GetThemeConstant("h_separation")+30;
        }
    }

    void ApplySceneAntialiasing(Viewport viewport)
    {
        var samples=SceneAntialiasing;
        if(viewport.Msaa3D==samples)return;
        viewport.Msaa3D=samples;
        if(_management==null)return;
        foreach(var preview in _management.FindChildren("TownPetPreview*","SubViewport",true,false).OfType<SubViewport>()){
            preview.Msaa3D=samples;
            if(preview.RenderTargetUpdateMode==SubViewport.UpdateMode.Disabled)
                preview.RenderTargetUpdateMode=SubViewport.UpdateMode.Once;
        }
    }

    void PositionTownCards()
    {
        if(_townToolbar==null)return;
        _panel.Position=new(18,Math.Max(18,GetViewport().GetVisibleRect().Size.Y-_panel.Size.Y*UiScale-18));
        if(_firstDayGuidePanel!=null)_firstDayGuidePanel.Position=new(18,_townToolbar.Position.Y+_townToolbar.Size.Y*UiScale+12);
    }

    Control DisplayPreferencesControls()
    {
        var card=MarketCard(_managementBody!,color:"e9efe6");
        MarketLabel(card,"界面显示",19,TownTheme.Leaf);
        var row=new HFlowContainer();
        row.AddThemeConstantOverride("h_separation",8);
        card.AddChild(row);
        MarketLabel(row,"界面缩放").VerticalAlignment=VerticalAlignment.Center;
        var scale=MarketOptions(row,DisplayScales.Select(value=>$"{value}%").ToArray(),_displayScale);
        scale.Name="TownInterfaceScale";
        scale.ItemSelected+=value=>{_displayScale=(int)value;SaveDisplayPreferences();};
        MarketLabel(row,"字号").VerticalAlignment=VerticalAlignment.Center;
        var font=MarketOptions(row,new[]{"标准","较大","大号"},_displayFont);
        font.Name="TownInterfaceFont";
        font.ItemSelected+=value=>{_displayFont=(int)value;SaveDisplayPreferences();};
        var contrast=new CheckButton{Name="TownHighContrast",Text="高对比模式",ButtonPressed=TownTheme.HighContrast};
        contrast.TooltipText="加深文字和选中样式，并给照护、订单与地图状态增加辨识符号。";
        row.AddChild(contrast);
        var picture=new HFlowContainer();
        picture.AddThemeConstantOverride("h_separation",8);
        card.AddChild(picture);
        var profile=AddGraphicsProfileControls(picture);
        MarketLabel(picture,"3D 抗锯齿").VerticalAlignment=VerticalAlignment.Center;
        var antialiasing=MarketOptions(picture,new[]{"关闭","平衡 · 2×","清晰 · 4×"},_sceneQuality);
        antialiasing.Name="TownSceneAntialiasing";
        antialiasing.TooltipText="平滑建筑、道路和动物的边缘。帧率偏低时可切换到平衡。";
        var status=MarketLabel(card,"",14,TownTheme.Muted,wrap:true);
        status.Name="TownEffectiveDisplay";
        var reset=MarketButton(card,"恢复默认显示",()=>ConfirmManagementAction("恢复默认显示",
            "将界面缩放、字号、画质、高对比和减少动态效果恢复为初始值。","恢复显示设置",()=>{
                _displayScale=1;_displayFont=0;_graphicsProfile=2;_sceneQuality=ActiveGraphics.Aa;
                _reduceCameraMotion=_reduceWeatherMotion=_reduceInterfaceMotion=false;
                _visualClimate="";
                ApplyTownGraphicsProfile();
                ApplyTownContrast(false);
                ShowManagement("环境","display");
                SetManagementFeedback("已恢复默认显示设置。",completed:true);
            }),expand:false,accent:"环境");
        reset.Name="TownDisplayReset";
        reset.SizeFlagsHorizontal=Control.SizeFlags.ShrinkBegin;
        contrast.Toggled+=enabled=>{ApplyTownContrast(enabled);RefreshDisplaySummary();};
        profile.ItemSelected+=selected=>{
            _graphicsProfile=(int)selected;
            _sceneQuality=ActiveGraphics.Aa;
            antialiasing.Select(_sceneQuality);
            ApplyTownGraphicsProfile();
            SaveDisplayPreferences();
            RefreshDisplaySummary();
        };
        antialiasing.ItemSelected+=value=>{
            _sceneQuality=(int)value;
            SaveDisplayPreferences();
            RefreshDisplaySummary();
        };
        if(_managementEntryTarget=="display")_managementFocusTarget=card;
        var motion=MarketCard(_managementBody!,color:"eeeaf0");
        MarketLabel(motion,"减少动态效果",19,TownTheme.Leaf);
        MarketLabel(motion,"分别调整镜头、雨雪粒子和界面提示的位置。天气与照护结算照常进行。",color:TownTheme.Muted,wrap:true);
        Motion("TownReduceCameraMotion","定位直接切换",_reduceCameraMotion,value=>{_reduceCameraMotion=value;if(value)_cameraTransition=0;});
        Motion("TownReduceWeatherMotion","隐藏雨雪粒子",_reduceWeatherMotion,value=>{_reduceWeatherMotion=value;_visualClimate="";});
        Motion("TownReduceInterfaceMotion","悬停提示固定位置",_reduceInterfaceMotion,value=>_reduceInterfaceMotion=value);
        RefreshDisplaySummary();
        return card.GetParent<Control>();

        void Motion(string name,string caption,bool current,Action<bool> apply)
        {
            var toggle=new CheckButton{Name=name,Text=caption,ButtonPressed=current};
            toggle.Toggled+=value=>{apply(value);SaveDisplayPreferences();RefreshDisplaySummary();};
            motion.AddChild(toggle);
        }

        void RefreshDisplaySummary()
        {
            string samples=_sceneQuality switch{0=>"关闭",1=>"2×",_=>"4×"};
            string plants=_graphicsProfile switch{0=>"低",1=>"中",_=>"高"};
            string weather=_reduceWeatherMotion?"雨雪粒子已隐藏":$"雨雪粒子 {plants}密度";
            status.Text=$"当前生效 · {ActiveGraphics.Name} · 上限 {ActiveGraphics.Fps} FPS · 抗锯齿 {samples}\n植被 {plants}密度 · {weather}";
        }
    }

    void SaveDisplayPreferences()
    {
        ApplyDisplayPreferences();
        if(!UseTownPreferences())return;
        using var config=new ConfigFile();
        config.SetValue("ui","scale",_displayScale);
        config.SetValue("ui","font",_displayFont);
        config.SetValue("ui","contrast",TownTheme.HighContrast);
        config.SetValue("graphics","antialiasing",_sceneQuality);
        config.SetValue("graphics","profile",_graphicsProfile);
        config.SetValue("motion","camera",_reduceCameraMotion);
        config.SetValue("motion","weather",_reduceWeatherMotion);
        config.SetValue("motion","interface",_reduceInterfaceMotion);
        if(config.Save(DisplayPreferencesPath)!=Error.Ok)SetManagementFeedback("界面设置未能保存。",true);
    }

    void ApplyTownContrast(bool enabled)
    {
        TownTheme.HighContrast=enabled;
        using var refreshed=TownTheme.Create();
        _panel.Theme.MergeWith(refreshed);
        _management?.Theme?.MergeWith(refreshed);
        foreach(var button in FindChildren("*","BaseButton",true,false).OfType<BaseButton>())
            if(button.HasMeta("town_accent"))TownTheme.AccentButton(button,button.GetMeta("town_accent").AsString(),button.GetMeta("town_accent_padding").AsInt32());
        SaveDisplayPreferences();
        RefreshScenePetCare();
    }
}
