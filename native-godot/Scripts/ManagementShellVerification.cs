using Godot;
using System;
using System.Linq;

public partial class Main
{
    void VerifyManagementShell(Action<bool,string> check)
    {
        var oldFavorites=_managementFavorites.ToArray();
        try{
            _management?.Hide();
            _managementFavorites.Clear();
            ShowManagement("家庭","residents");
            UpdateManagementDensity(true);
            check(UsesCompactManagement(new(800,500),new(800,500))&&_managementTabs.Count==17&&
                _managementGroupControls.Values.Count(control=>control.Toggle.Visible)==4&&
                _managementTabs["家庭"].Visible&&!_managementTabs["订单"].Visible&&_animalHeaderSlot?.Visible==false,
                "小窗口未收起其他导航分组或保留完整导航");
            _managementGroupControls["经营"].Toggle.EmitSignal(BaseButton.SignalName.Pressed);
            check(_managementTabs["订单"].Visible&&!_managementTabs["家庭"].Visible&&_tab=="家庭","展开分组误切换页面");
            FocusCurrentManagementTab();
            check(_managementTabs["家庭"].Visible,"F6 未能重新显示当前页面导航");
            if(DisplayServer.GetName()!="headless"){
                check(_management!.GuiGetFocusOwner()==_managementTabs["家庭"],"F6 未聚焦当前页面按钮");
                MoveManagementRailFocus(_managementTabs["家庭"],new InputEventKey{Pressed=true,Keycode=Key.Down});
                check(_management.GuiGetFocusOwner()==_managementTabs["外观"],"方向键未移动导航焦点");
            }

            _managementFavoriteToggle!.EmitSignal(BaseButton.SignalName.Pressed);
            check(_managementFavoriteToggle.ButtonPressed&&_managementFavoritesMenu!.GetPopup().GetItemText(0)=="家庭","星标未添加常用页面");
            ShowManagement("订单");
            check(_managementTabs["订单"].ButtonPressed&&_managementTabs["订单"].Visible&&
                _managementBack?.Text.Contains("村民名册",StringComparison.Ordinal)==true,"深链未显示对应菜单或返回去向");
            _managementFavoritesMenu!.GetPopup().EmitSignal(PopupMenu.SignalName.IdPressed,0);
            check(_tab=="家庭","常用菜单未打开收藏页面");
            _managementFavoriteToggle.EmitSignal(BaseButton.SignalName.Pressed);
            check(!_managementFavoriteToggle.ButtonPressed&&_managementFavoritesMenu.GetPopup().IsItemDisabled(0),"星标未移除收藏页面");
            using var config=new ConfigFile();
            config.SetValue("management","favorites",new[]{"无效","宠物","宠物","地图","订单","商店","成长"});
            check(ReadManagementFavorites(config).SequenceEqual(new[]{"宠物","地图","订单","商店"}),"常用配置没有过滤无效、重复或超量项目");

            SetManagementFeedback("反馈检查",true);
            check(_managementFeedback?.Visible==true&&_managementFeedbackKind?.Text=="未完成"&&
                _managementFeedbackLabel?.TooltipText=="反馈检查","错误反馈没有状态或完整信息");
            _managementFeedbackDismiss!.EmitSignal(BaseButton.SignalName.Pressed);
            check(_managementFeedback?.Visible==false,"收起反馈没有清除显示");
            SetManagementFeedback("完成检查",completed:true);
            check(_managementFeedback?.Visible==true&&_managementFeedbackKind?.Text=="已完成","完成反馈没有独立状态");
            GD.Print("MANAGEMENT_SHELL_VERIFIED compact_rail keyboard favorites breadcrumb feedback");
        }finally{
            _managementFavorites.Clear();
            _managementFavorites.AddRange(oldFavorites);
            RefreshManagementFavorites();
            RefreshManagementFavoriteToggle();
            SetManagementFeedback("");
            OnManagementHostResized();
            _management?.Hide();
        }
    }
}
