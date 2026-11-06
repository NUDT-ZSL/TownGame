using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

public partial class Main
{
    const string ManagementFavoritesPath="user://town-management.cfg";
    const int ManagementFavoriteLimit=4;
    PanelContainer? _managementRail;
    ScrollContainer? _managementNavScroll;
    MenuButton? _managementFavoritesMenu;
    Button? _managementFavoriteToggle;
    readonly List<string> _managementFavorites=new();
    readonly Dictionary<string,(Label Caption,Button Toggle)> _managementGroupControls=new();
    bool _compactManagement;
    int _managementDensityFont=-1;
    string _expandedManagementGroup="";

    static bool UsesCompactManagement(Vector2I host,Vector2 available)=>
        host.X<1060||host.Y<675||available.X<1000||available.Y<630;

    void AddManagementNavigation(Container layout)
    {
        _managementRail=new PanelContainer{Name="ManagementRail"};
        layout.AddChild(_managementRail);
        var stack=new VBoxContainer();
        stack.AddThemeConstantOverride("separation",7);
        _managementRail.AddChild(stack);
        var journal=new Label{Text="慢时光手账",TooltipText="F6 回到页面导航"};
        journal.AddThemeColorOverride("font_color",TownTheme.Leaf);
        journal.AddThemeFontSizeOverride("font_size",18);
        stack.AddChild(journal);

        if(UseTownPreferences()){
            using var config=new ConfigFile();
            if(config.Load(ManagementFavoritesPath)==Error.Ok)_managementFavorites.AddRange(ReadManagementFavorites(config));
        }
        _managementFavoritesMenu=new MenuButton{Name="ManagementFavorites",Flat=false,FocusMode=Control.FocusModeEnum.All,
            TooltipText="用标题旁的星标添加当前页面"};
        TownTheme.AccentButton(_managementFavoritesMenu,"存档",4);
        _managementFavoritesMenu.GetPopup().IdPressed+=id=>{
            if(id>=0&&id<_managementFavorites.Count)ShowManagement(_managementFavorites[(int)id]);
        };
        stack.AddChild(_managementFavoritesMenu);
        RefreshManagementFavorites();

        _managementNavScroll=new ScrollContainer{Name="ManagementNavScroll",HorizontalScrollMode=ScrollContainer.ScrollMode.Disabled,
            VerticalScrollMode=ScrollContainer.ScrollMode.Reserve,SizeFlagsVertical=Control.SizeFlags.ExpandFill,FollowFocus=true,
            AccessibilityName="经营管理导航",AccessibilityDescription="F6 回到当前页面。方向键浏览，回车打开。"};
        stack.AddChild(_managementNavScroll);
        _managementMenu=new VBoxContainer{SizeFlagsHorizontal=Control.SizeFlags.ExpandFill};
        _managementNavScroll.AddChild(_managementMenu);
        foreach(var group in ManagementGroups){
            var caption=new Label{Text=group.Name};
            caption.AddThemeFontSizeOverride("font_size",14);
            caption.AddThemeColorOverride("font_color",TownTheme.Muted);
            _managementMenu.AddChild(caption);
            var toggle=new Button{ToggleMode=true,Alignment=HorizontalAlignment.Left,AccessibilityName=group.Name+"分组"};
            TownTheme.AccentButton(toggle,group.Tabs[0],3);
            toggle.Pressed+=()=>{
                _expandedManagementGroup=group.Name;
                UpdateManagementGroups();
            };
            toggle.GuiInput+=input=>MoveManagementRailFocus(toggle,input);
            _managementMenu.AddChild(toggle);
            _managementGroupControls[group.Name]=(caption,toggle);
            foreach(string name in group.Tabs){
                var button=new Button{ToggleMode=true,Alignment=HorizontalAlignment.Left,AccessibilityName=name};
                button.Pressed+=()=>ShowManagement(name);
                button.GuiInput+=input=>MoveManagementRailFocus(button,input);
                _managementMenu.AddChild(button);
                _managementTabs[name]=button;
            }
        }
    }

    void UpdateManagementDensity(bool compact)
    {
        if(compact==_compactManagement&&_managementDensityFont==ExtraFontSize)return;
        _managementDensityFont=ExtraFontSize;
        _compactManagement=compact;
        _managementRail!.CustomMinimumSize=new(compact?124:151,0);
        _managementRail.AddThemeStyleboxOverride("panel",TownTheme.Box("eee9da",12,compact?7:10));
        _managementMenu!.AddThemeConstantOverride("separation",compact?3:4);
        if(_animalHeaderSlot!=null)_animalHeaderSlot.Visible=!compact;
        if(_managementTitle!=null)_managementTitle.AddThemeFontSizeOverride("font_size",(compact?22:25)+ExtraFontSize/2);
        if(_managementSummary!=null)_managementSummary.AddThemeFontSizeOverride("font_size",(compact?14:16)+ExtraFontSize);
        foreach(var (page,button) in _managementTabs){
            button.CustomMinimumSize=new(0,compact?26:28);
            button.AddThemeFontSizeOverride("font_size",(compact?15:16)+ExtraFontSize);
            TownTheme.AccentButton(button,page,compact?2:4);
        }
        UpdateManagementNavigation(_tab);
    }

    void UpdateManagementNavigation(string tab)
    {
        foreach(var (name,button) in _managementTabs){
            bool active=name==tab;
            button.Text=active?"› "+name:name;
            button.SetPressedNoSignal(active);
            button.AccessibilityDescription=active?"当前页面":"回车打开页面";
        }
        _expandedManagementGroup=ManagementGroups.FirstOrDefault(group=>group.Tabs.Contains(tab)).Name??"";
        UpdateManagementGroups();
        RefreshManagementFavoriteToggle();
        if(!_managementTabs.TryGetValue(tab,out var selected))return;
        Callable.From(()=>{
            if(_management?.Visible==true&&_tab==tab&&selected.IsVisibleInTree())
                _managementNavScroll!.EnsureControlVisible(selected);
        }).CallDeferred();
    }

    void UpdateManagementGroups()
    {
        foreach(var group in ManagementGroups){
            var (caption,toggle)=_managementGroupControls[group.Name];
            bool expanded=group.Name==_expandedManagementGroup;
            bool active=group.Tabs.Contains(_tab);
            caption.Visible=!_compactManagement;
            toggle.Visible=_compactManagement;
            toggle.Text=(expanded?"▾ ":"› ")+group.Name;
            toggle.AccessibilityDescription=(expanded?"已展开":"已收起")+(active?"，包含当前页面":"");
            toggle.SetPressedNoSignal(expanded);
            foreach(string name in group.Tabs)_managementTabs[name].Visible=!_compactManagement||expanded;
        }
    }

    void MoveManagementRailFocus(Button current,InputEvent input)
    {
        if(input is not InputEventKey{Pressed:true,AltPressed:false,CtrlPressed:false,MetaPressed:false} key||
            key.Keycode is not (Key.Up or Key.Down or Key.Home or Key.End))return;
        var visible=_managementMenu!.GetChildren().OfType<Button>().Where(button=>button.IsVisibleInTree()).ToArray();
        int index=Array.IndexOf(visible,current);
        if(index<0)return;
        current.AcceptEvent();
        int next=key.Keycode switch{Key.Home=>0,Key.End=>visible.Length-1,Key.Up=>Math.Max(0,index-1),_=>Math.Min(visible.Length-1,index+1)};
        visible[next].GrabFocus();
    }

    void FocusCurrentManagementTab()
    {
        if(!_managementTabs.TryGetValue(_tab,out var button))return;
        UpdateManagementNavigation(_tab);
        button.GrabFocus();
    }

    void AddManagementFavoriteToggle(Container header)
    {
        _managementFavoriteToggle=new Button{Name="ManagementFavoriteToggle",ToggleMode=true,
            CustomMinimumSize=new(34,34),SizeFlagsVertical=Control.SizeFlags.ShrinkBegin};
        _managementFavoriteToggle.AddThemeFontSizeOverride("font_size",20);
        TownTheme.AccentButton(_managementFavoriteToggle,"存档",4);
        _managementFavoriteToggle.Pressed+=ToggleManagementFavorite;
        header.AddChild(_managementFavoriteToggle);
    }

    void ToggleManagementFavorite()
    {
        if(!_managementFavorites.Remove(_tab)){
            if(_managementFavorites.Count>=ManagementFavoriteLimit){
                RefreshManagementFavoriteToggle();
                SetManagementFeedback($"已收藏 {ManagementFavoriteLimit} 个页面，请先移除一个。",true);
                return;
            }
            _managementFavorites.Add(_tab);
        }
        RefreshManagementFavoriteToggle();
        RefreshManagementFavorites();
        if(!UseTownPreferences())return;
        using var config=new ConfigFile();
        config.SetValue("management","favorites",_managementFavorites.ToArray());
        if(config.Save(ManagementFavoritesPath)!=Error.Ok)SetManagementFeedback("常用页面设置未能保存。",true);
    }

    static string[] ReadManagementFavorites(ConfigFile config)=>config.GetValue("management","favorites",Array.Empty<string>())
        .AsStringArray().Where(ManagementTabs.Contains).Distinct(StringComparer.Ordinal).Take(ManagementFavoriteLimit).ToArray();

    void RefreshManagementFavorites()
    {
        var menu=_managementFavoritesMenu!;
        menu.Text=$"常用 {_managementFavorites.Count} ▾";
        menu.AccessibilityName=$"常用页面，已收藏 {_managementFavorites.Count} 个";
        var popup=menu.GetPopup();
        popup.Clear();
        for(int index=0;index<_managementFavorites.Count;index++)popup.AddItem(_managementFavorites[index],index);
        if(_managementFavorites.Count>0)return;
        popup.AddItem("点击页面标题旁的星标添加常用页面");
        popup.SetItemDisabled(0,true);
    }

    void RefreshManagementFavoriteToggle()
    {
        if(_managementFavoriteToggle==null)return;
        bool active=_managementFavorites.Contains(_tab);
        _managementFavoriteToggle.Text=active?"★":"☆";
        _managementFavoriteToggle.SetPressedNoSignal(active);
        _managementFavoriteToggle.TooltipText=active?$"从常用页面移除「{_tab}」":$"把「{_tab}」加入常用页面";
        _managementFavoriteToggle.AccessibilityName=_managementFavoriteToggle.TooltipText;
    }
}
