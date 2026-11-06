using Godot;
using System;
using System.Linq;

public partial class Main
{
    Label _townWeatherChip=null!,_townCoinsChip=null!,_townActivityChip=null!;
    PanelContainer _townTravelBanner=null!;
    Label _townTravelCaption=null!;
    Button _townTravelPause=null!;
    ProgressBar _townTravelProgress=null!;
    Button _townFieldGuideToggle=null!;
    VBoxContainer _townFieldGuide=null!;
    Label _townFieldGuideKeys=null!;
    bool _townFieldGuideExpanded;

    static void FitTownHudCard(Control card)
    {
        // Wrapped text reports its final height after the container assigns its width.
        card.MinimumSizeChanged+=()=>card.Size=card.GetCombinedMinimumSize();
    }

    void CreateTownStatus(VBoxContainer column)
    {
        var masthead=new HFlowContainer{Name="TownHudStatus"};
        masthead.AddThemeConstantOverride("h_separation",7);
        masthead.AddThemeConstantOverride("v_separation",4);
        column.AddChild(masthead);
        var identity=new HBoxContainer();
        identity.AddThemeConstantOverride("separation",6);
        masthead.AddChild(identity);
        identity.AddChild(new TownHudEmblem());
        MarketLabel(identity,"慢时光",19,TownTheme.Leaf).VerticalAlignment=VerticalAlignment.Center;
        _stats=Chip("TownHudClock","e8e8d5","环境");
        _townWeatherChip=Chip("TownHudWeather","e4ecea","地图");
        _townCoinsChip=Chip("TownHudCoins","f2e7d1","商店");
        _townActivityChip=Chip("TownHudActivity","efe4e6","家庭");

        Label Chip(string name,string tint,string accent)
        {
            var panel=new PanelContainer{MouseFilter=Control.MouseFilterEnum.Ignore,SizeFlagsVertical=Control.SizeFlags.ShrinkCenter};
            var style=TownTheme.Box(tint,8,7);
            style.ContentMarginTop=3;
            style.ContentMarginBottom=3;
            panel.AddThemeStyleboxOverride("panel",style);
            masthead.AddChild(panel);
            var label=MarketLabel(panel,"",color:TownTheme.Accent(accent).Darkened(.34f));
            label.RemoveThemeFontSizeOverride("font_size");
            label.Name=name;
            return label;
        }
    }

    void CreateTownFastForwardBanner(VBoxContainer column)
    {
        _townTravelBanner=new PanelContainer{Name="TownHudFastForward",Visible=false};
        _townTravelBanner.AddThemeStyleboxOverride("panel",TownTheme.Box("e3ebef",8,5));
        column.AddChild(_townTravelBanner);
        var content=new VBoxContainer();content.AddThemeConstantOverride("separation",4);
        _townTravelBanner.AddChild(content);
        var row=new HFlowContainer();row.AddThemeConstantOverride("h_separation",6);content.AddChild(row);
        _townTravelCaption=MarketLabel(row,"",color:TownTheme.Ink);
        _townTravelCaption.RemoveThemeFontSizeOverride("font_size");
        _townTravelCaption.VerticalAlignment=VerticalAlignment.Center;
        _townTravelPause=MarketButton(row,"暂停",()=>SetTownPaused(!Paused),expand:false,accent:"地图");
        _townTravelPause.Name="TownFastForwardPause";
        MarketButton(row,"时间计划",OpenTownTimePlanner,expand:false,accent:"环境").Name="TownFastForwardPlan";
        MarketButton(row,"停止快进",()=>StopFastForward("已停止快进 · 已恢复正常速度"),expand:false,accent:"地图").Name="TownFastForwardStop";
        _townTravelProgress=new ProgressBar{Name="TownFastForwardProgress",MaxValue=100,ShowPercentage=false,
            CustomMinimumSize=new(0,6),MouseFilter=Control.MouseFilterEnum.Ignore};
        _townTravelProgress.AddThemeStyleboxOverride("background",TownTheme.Box("d6e1e5",3,0));
        _townTravelProgress.AddThemeStyleboxOverride("fill",TownTheme.Box("5b8d88",3,0));
        content.AddChild(_townTravelProgress);
    }

    void RefreshTownStatus()
    {
        int day=1+(int)Math.Max(0,(Game.Now-NativeGame.D(Data.Meta,"start_ts"))/NativeGame.Day);
        _stats.Text=$"第 {day} 天 · {GameClockText}"+(Paused?" · 已暂停":"");
        _townWeatherChip.Text=$"{Game.Season}季 · {Game.Weather}";
        _townCoinsChip.Text=$"金币 {NativeGame.I(Data.Meta,"coins"):N0}";
        _townActivityChip.Text=$"伙伴 {Data.Pets.Count(pet=>Game.Active(pet!))} · 任务 {Game.Jobs.Count}";
        if(_saveControl!=null)_saveControl.TooltipText=$"{TownSaveStatus}\n{TownBindingText("save")} 快速保存";
        _townTravelBanner.Visible=_fastForward.HasValue;
        if(_fastForward is FastForwardTarget target){
            _townTravelCaption.Text=$"{target.Label} · {(Paused?"已暂停":FastForwardEta(target.At))}";
            _townTravelPause.Text=Paused?"继续快进":"暂停快进";
            double elapsed=Game.Now-_fastForwardBegan,duration=Math.Max(1e-6,target.At-_fastForwardBegan);
            _townTravelProgress.Value=100*Math.Clamp(elapsed/duration,0,1);
        }
    }

    void CreateTownFieldGuide(VBoxContainer parent)
    {
        _townFieldGuideToggle=MarketButton(parent,"展开操作速查",ToggleTownFieldGuide,expand:false,accent:"环境");
        _townFieldGuideToggle.Name="TownFieldGuideToggle";
        _townFieldGuideToggle.CustomMinimumSize=new(0,28);
        _townFieldGuideToggle.SizeFlagsHorizontal=Control.SizeFlags.ShrinkBegin;
        _townFieldGuideToggle.TooltipText="查看镜头、搜索和常用按键";
        TownTheme.AccentButton(_townFieldGuideToggle,"环境",5);
        _townFieldGuide=new VBoxContainer{Name="TownFieldGuide",Visible=false};
        parent.AddChild(_townFieldGuide);
        MarketLabel(_townFieldGuide,"左键或中键拖动平移 · 右键拖动旋转 · 滚轮缩放",14,TownTheme.Ink,wrap:true);
        _townFieldGuideKeys=MarketLabel(_townFieldGuide,"",14,TownTheme.Muted,wrap:true);
        MarketButton(_townFieldGuide,"调整快捷键",()=>ShowManagement("环境","keybindings"),expand:false,accent:"地图");
    }

    void ToggleTownFieldGuide()
    {
        _townFieldGuideExpanded=!_townFieldGuideExpanded;
        RefreshScenePetCare();
        if(_townFieldGuideExpanded&&_townFieldGuideToggle.Visible)_townFieldGuideToggle.GrabFocus();
    }

    void RefreshTownFieldGuide(bool canShow)
    {
        _townFieldGuideToggle.Visible=canShow;
        _townFieldGuideToggle.Text=_townFieldGuideExpanded?"收起操作速查":"展开操作速查";
        _townFieldGuide.Visible=canShow&&_townFieldGuideExpanded;
        if(_townFieldGuide.Visible)
            _townFieldGuideKeys.Text=$"{TownMovementText} 移动镜头\n{TownBindingText("map")} 地图 · {TownBindingText("search")} 搜索 · {TownBindingText("photo")} 拍照";
    }

    sealed partial class TownHudEmblem : Control
    {
        public TownHudEmblem()
        {
            CustomMinimumSize=new(29,29);
            SizeFlagsVertical=SizeFlags.ShrinkCenter;
            MouseFilter=MouseFilterEnum.Ignore;
        }

        public override void _Draw()
        {
            DrawCircle(new(14.5f,14.5f),14,new Color("e8e7cc"),true,-1,true);
            DrawCircle(new(20,9),3.1f,new Color("c28a4c"),true,-1,true);
            DrawLine(new(14,23),new(14,13),new Color("536c4b"),1.5f,true);
            DrawColoredPolygon(new Vector2[]{new(14,18),new(8,17),new(5,12),new(11,12),new(14,15)},new Color("719066"));
            DrawColoredPolygon(new Vector2[]{new(14,20),new(17,15),new(23,14),new(21,19),new(16,21)},new Color("4d785e"));
        }
    }
}
