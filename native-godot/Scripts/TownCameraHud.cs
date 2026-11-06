using Godot;
using System;

public partial class Main
{
    static readonly string[] TownCameraDirections={"北","东北","东","东南","南","西南","西","西北"};
    PanelContainer _townCameraHud=null!;
    VBoxContainer _townCameraHudBody=null!;
    Button _townCameraHudToggle=null!,_townCameraCloser=null!,_townCameraFarther=null!;
    Label _townCameraHudStatus=null!;

    void CreateTownCameraHud(CanvasLayer layer,Theme theme)
    {
        var panel=new PanelContainer{Name="TownCameraHud",Theme=theme,Visible=false};
        panel.AddThemeStyleboxOverride("panel",TownTheme.Box("f5f1e6f4",11,8));
        layer.AddChild(panel);
        _townCameraHud=panel;
        var body=new VBoxContainer();panel.AddChild(body);
        _townCameraHudToggle=MarketButton(body,"镜头工具",()=>{
            _townCameraHudBody.Visible=!_townCameraHudBody.Visible;
            _townCameraHudToggle.Text=_townCameraHudBody.Visible?"收起镜头工具":"镜头工具";
            _townCameraHud.ResetSize();
            RefreshTownCameraHud();
        },expand:false,accent:"地图");
        _townCameraHudToggle.Name="TownCameraHudToggle";
        _townCameraHudToggle.CustomMinimumSize=new(0,28);
        TownTheme.AccentButton(_townCameraHudToggle,"地图",5);
        _townCameraHudBody=new VBoxContainer{Visible=false};
        body.AddChild(_townCameraHudBody);
        _townCameraHudStatus=MarketLabel(_townCameraHudBody,"",14,TownTheme.Ink,wrap:true);
        _townCameraHudStatus.Name="TownCameraHudStatus";
        var row=new HBoxContainer();row.AddThemeConstantOverride("separation",5);_townCameraHudBody.AddChild(row);
        _townCameraCloser=ActionButton("＋","TownCameraHudCloser","向画面中心拉近",()=>Adjust(1));
        _townCameraFarther=ActionButton("−","TownCameraHudFarther","向画面中心拉远",()=>Adjust(-1));
        ActionButton("朝北","TownCameraHudNorth","保持位置并将镜头转向北方",()=>{_yaw=0;UpdateCamera();RefreshTownCameraHud();});
        FitTownHudCard(panel);
        panel.Resized+=PositionTownCameraHud;

        void Adjust(float step){ZoomAt(GetViewport().GetVisibleRect().GetCenter(),TownWheelFactor(step));RefreshTownCameraHud();}

        Button ActionButton(string text,string name,string tooltip,Action run)
        {
            var button=new Button{Name=name,Text=text,TooltipText=tooltip,AccessibilityName=tooltip,CustomMinimumSize=new(34,32)};
            TownTheme.AccentButton(button,"地图",5);
            button.Pressed+=run;
            row.AddChild(button);
            return button;
        }
    }

    void RefreshTownCameraHud()
    {
        _townCameraHud.Visible=!_benchmark&&!InPhotoMode&&_management?.Visible!=true&&_townSearchWindow?.Visible!=true;
        if(!_townCameraHud.Visible)return;
        if(_townCameraHudBody.Visible){
            int direction=(Mathf.RoundToInt(-_yaw/(Mathf.Pi/4))%8+8)%8;
            string mode=_buildCamera?"等距":InPetCamera?"伙伴近景":"自由视角";
            _townCameraHudStatus.Text=$"朝{TownCameraDirections[direction]} · {mode}\n镜头距离 {_distance:0.#}";
            _townCameraCloser.Disabled=_distance<=(InPetCamera?MinimumPortraitDistance():5.5f)+.001f;
            _townCameraFarther.Disabled=_distance>=(InPetCamera?45:900)-.001f;
        }
        PositionTownCameraHud();
    }

    void PositionTownCameraHud()
    {
        var area=GetViewport().GetVisibleRect();
        _townCameraHud.Scale=Vector2.One*UiScale;
        _townCameraHud.Position=area.End-_townCameraHud.Size*UiScale-new Vector2(18,18);
    }
}
