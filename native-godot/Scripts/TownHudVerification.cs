using Godot;
using System;
using System.Linq;
using System.Text.Json.Nodes;

public partial class Main
{
    void VerifyTownHudRefresh(Action<bool,string> check)
    {
        _management?.Hide();
        string selected=_selectedPet,feedback=_selection;
        var speed=Data.Meta["native_speed"]?.DeepClone();
        bool paused=Paused,guide=_townFieldGuideExpanded;
        var forward=_fastForward;
        var lastForward=_lastFastForward;
        try{
            _selectedPet="";
            _selection=SceneSelectionHint;
            _townFieldGuideExpanded=false;
            RefreshTownStatus();
            RefreshScenePetCare();
            check(_stats.Text.Contains(GameClockText)&&_townWeatherChip.Text.Contains(Game.Weather)&&_townCoinsChip.Text.StartsWith("金币"),
                "顶栏状态没有分别显示日期、天气和金币");
            check(!_details.Visible&&!_townFieldGuide.Visible&&_townFieldGuideToggle.Visible,"默认场景仍显示整块操作说明");
            _townFieldGuideToggle.EmitSignal(BaseButton.SignalName.Pressed);
            check(_townFieldGuide.Visible&&_townFieldGuideKeys.Text.Contains(TownBindingText("search")),"展开的速查没有使用实际快捷键");
            _townFieldGuideToggle.EmitSignal(BaseButton.SignalName.Pressed);
            check(!_townFieldGuide.Visible,"操作速查无法收起");

            var picker=(MenuButton)_speedControl!;
            picker.EmitSignal(MenuButton.SignalName.AboutToPopup);
            var menu=picker.GetPopup();
            check(TownSpeedChoices.Select((_,i)=>menu.GetItemIndex(SpeedMenu+i)).All(index=>index>=0),"时间菜单未提供三个独立倍率");
            menu.EmitSignal(PopupMenu.SignalName.IdPressed,(long)SpeedMenu+2);
            check(NativeGame.I(Data.Meta,"native_speed")==600&&picker.Text.Contains("600"),"倍率菜单未直接切换到所选速度");
            SetTownPaused(true);
            check(_stats.Text.Contains("已暂停")&&_townPauseButton?.Text=="继续","暂停提示未同步");
            _fastForward=FindFastForwardTarget(FastForwardGoal.Morning);
            RefreshSpeedControl();
            check(_townTravelBanner.Visible&&_townTravelCaption.Text.Contains("07:00"),"目标快进没有单独的停止操作区");
            _townTravelBanner.FindChild("TownFastForwardStop",true,false)!.EmitSignal(BaseButton.SignalName.Pressed);
            check(!_townTravelBanner.Visible&&NativeGame.I(Data.Meta,"native_speed")==1,"顶栏无法停止快进并恢复正常倍率");

            var pet=Data.Pets.OfType<JsonNode>().First(pet=>Game.Active(pet)&&FarmPet(NativeGame.S(pet,"pet_id"))!=null);
            var hunger=pet["hunger"]?.DeepClone();
            try{
                _selectedPet=NativeGame.S(pet,"pet_id");
                pet["hunger"]=18;
                RefreshScenePetCare();
                var need=_scenePetNeeds.First(entry=>entry.Key=="hunger");
                check(need.Bar.IsVisibleInTree()&&need.Bar.Value==18&&need.Label.Text.StartsWith("! 饱食"),"场景照护未显示真实数值和低值提示");
            }finally{pet["hunger"]=hunger;}
            GD.Print("TOWN_HUD_REFRESH_VERIFIED status responsive_groups speed_picker field_guide pet_meters");
        }finally{
            _selectedPet=selected;
            _selection=feedback;
            Data.Meta["native_speed"]=speed;
            _fastForward=forward;
            _lastFastForward=lastForward;
            _townFieldGuideExpanded=guide;
            SetTownPaused(paused);
            RefreshSpeedControl();
            RefreshScenePetCare();
        }
    }
}
