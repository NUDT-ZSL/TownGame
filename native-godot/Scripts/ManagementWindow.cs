using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

public partial class Main
{
    Window? _management;
    VBoxContainer? _managementBody, _managementMenu;
    ScrollContainer? _managementScroll;
    PanelContainer? _managementHeader, _managementFeedback;
    Label? _managementTitle, _managementSummary, _managementFeedbackLabel, _managementFeedbackKind;
    Button? _managementFeedbackDismiss;
    HBoxContainer? _animalHeaderSlot;
    readonly Dictionary<string,Button> _managementTabs=new();
    readonly Dictionary<string,int> _managementScrollPositions=new();
    Action? _refreshManagementPage;
    double _managementRefreshElapsed;
    Control? _managementFocusTarget;
    string _managementEntryTarget="";
    string _tab="宠物";

    static readonly (string Name,string[] Tabs)[] ManagementGroups={
        ("总览",new[]{"今日待办","成长"}),
        ("伙伴",new[]{"宠物","家庭","外观","照护","档案"}),
        ("经营",new[]{"商店","订单","事件","财务","日志"}),
        ("小镇",new[]{"建造","地图","自治","环境","存档"})
    };
    static readonly string[] ManagementTabs={"今日待办","成长","宠物","家庭","外观","照护","商店","建造","地图","自治","环境","财务","订单","事件","档案","日志","存档"};

    void CreateManagementWindow()
    {
        var window=new Window{
            Title="慢时光 · 经营管理",Borderless=true,Transient=true,Visible=false,TransparentBg=true,Theme=(Theme)_panel.Theme.Duplicate(),
            ContentScaleMode=Window.ContentScaleModeEnum.CanvasItems,ContentScaleAspect=Window.ContentScaleAspectEnum.Expand
        };
        _management=window;
        AddChild(window);
        window.CloseRequested+=window.Hide;
        window.WindowInput+=input=>{
            if(input is InputEventKey {Pressed:true,Echo:false,AltPressed:true,Keycode:Key.Left}){
                window.SetInputAsHandled();
                GoBackInManagement();
                return;
            }
            if(input is InputEventKey { Pressed:true, Echo:false, Keycode:Key.Escape }){
                window.SetInputAsHandled();
                window.Hide();
                return;
            }
            if(input is InputEventKey {Pressed:true,Echo:false} key&&MatchTownInput(key,"search")&&
                (key.CtrlPressed||window.GuiGetFocusOwner() is not (LineEdit or TextEdit))){
                window.SetInputAsHandled();
                OpenTownSearch();
                return;
            }
            if(input is InputEventKey{Pressed:true,Echo:false,AltPressed:false,CtrlPressed:false,MetaPressed:false,Keycode:Key.F6}){
                window.SetInputAsHandled();
                FocusCurrentManagementTab();
            }
        };
        var host=GetWindow();
        host.SizeChanged+=OnManagementHostResized;
        TreeExiting+=()=>host.SizeChanged-=OnManagementHostResized;

        var page=new PanelContainer();
        page.AddThemeStyleboxOverride("panel",TownTheme.Box("f7f2e6",16,12));
        window.AddChild(page);
        page.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        var layout=new HBoxContainer();
        layout.AddThemeConstantOverride("separation",12);
        page.AddChild(layout);

        AddManagementNavigation(layout);

        var workspace=new VBoxContainer{SizeFlagsHorizontal=Control.SizeFlags.ExpandFill};
        workspace.AddThemeConstantOverride("separation",8);
        layout.AddChild(workspace);
        _managementHeader=new PanelContainer();
        workspace.AddChild(_managementHeader);
        var header=new HBoxContainer();
        header.AddThemeConstantOverride("separation",12);
        _managementHeader.AddChild(header);
        AddManagementBackButton(header);
        var identity=new VBoxContainer{SizeFlagsHorizontal=Control.SizeFlags.ExpandFill,SizeFlagsVertical=Control.SizeFlags.ShrinkCenter};
        header.AddChild(identity);
        _managementTitle=new Label();
        _managementTitle.AddThemeFontSizeOverride("font_size",25);
        identity.AddChild(_managementTitle);
        _managementSummary=new Label{AutowrapMode=TextServer.AutowrapMode.WordSmart,SizeFlagsHorizontal=Control.SizeFlags.ExpandFill};
        _managementSummary.AddThemeColorOverride("font_color",TownTheme.Muted);
        identity.AddChild(_managementSummary);
        _animalHeaderSlot=new HBoxContainer();
        header.AddChild(_animalHeaderSlot);
        AddEnvironmentSectionMenu(header);
        AddManagementFavoriteToggle(header);
        var close=new Button{Text="×",CustomMinimumSize=new(34,34),SizeFlagsVertical=Control.SizeFlags.ShrinkBegin,TooltipText="关闭经营管理（Esc）"};
        close.AccessibilityName="关闭经营管理";
        close.AddThemeFontSizeOverride("font_size",20);
        TownTheme.AccentButton(close,"宠物",4);
        close.Pressed+=window.Hide;
        header.AddChild(close);

        AddManagementFeedback(workspace);
        var content=new PanelContainer{SizeFlagsVertical=Control.SizeFlags.ExpandFill};
        content.AddThemeStyleboxOverride("panel",TownTheme.Box("fffcf3",12,14));
        workspace.AddChild(content);
        _managementScroll=new ScrollContainer{SizeFlagsVertical=Control.SizeFlags.ExpandFill,HorizontalScrollMode=ScrollContainer.ScrollMode.Auto,FollowFocus=true};
        _managementScroll.AccessibilityName="经营管理页面内容";
        content.AddChild(_managementScroll);
        _managementBody=new VBoxContainer{SizeFlagsHorizontal=Control.SizeFlags.ExpandFill};
        _managementBody.AddThemeConstantOverride("separation",10);
        _managementScroll.AddChild(_managementBody);
    }

    Vector2I ManagementWindowSize()
    {
        var host=GetWindow();
        var available=host.GuiEmbedSubwindows?(Vector2I)host.GetVisibleRect().Size:host.Size;
        bool compact=UsesCompactManagement(host.Size,(Vector2)available/UiScale);
        var size=new Vector2I(Math.Min(1360,Math.Max(1,(int)(available.X*(compact?.95f:.88f)))),
            Math.Min(880,Math.Max(1,(int)(available.Y*(compact?.95f:.90f)))));
        float width=compact?790:840,height=compact?430:500;
        float scale=Math.Min(1f,Math.Min(size.X/(width*UiScale),size.Y/(height*UiScale)));
        _management!.Theme.DefaultFontSize=(host.Size.X<1180||host.Size.Y<740?18:17)+ExtraFontSize;
        _management.ContentScaleFactor=UiScale;
        _management.ContentScaleSize=scale<1f
            ?new((int)Math.Ceiling(size.X/scale),(int)Math.Ceiling(size.Y/scale))
            :Vector2I.Zero;
        UpdateManagementDensity(compact);
        return size;
    }

    void OnManagementHostResized()
    {
        if(_management?.Visible!=true)return;
        var host=GetWindow();
        var size=ManagementWindowSize();
        _management.Size=size;
        _management.Position=_management.IsEmbedded()
            ?(Vector2I)(host.GetVisibleRect().GetCenter()-(Vector2)size/2)
            :host.Position+(host.Size-size)/2;
    }

    void UpdateManagementHeader(string tab,bool refreshPortrait)
    {
        _environmentSections.Clear();
        _environmentSectionMenu!.Visible=tab=="环境";
        UpdateManagementNavigation(tab);
        UpdateManagementBack();
        _managementTitle!.Text=tab;
        Color accent=TownTheme.Accent(tab);
        _managementTitle.AddThemeColorOverride("font_color",accent.Darkened(.28f));
        var style=TownTheme.Box(accent.Lerp(new Color("fffaf0"),.89f).ToHtml(),12,10);
        style.BorderWidthLeft=4;
        style.BorderColor=accent;
        _managementHeader!.AddThemeStyleboxOverride("panel",style);
        UpdateManagementSummary();
        if(!refreshPortrait)return;
        _managementScroll!.SetDeferred("scroll_horizontal",0);
        foreach(var child in _animalHeaderSlot!.GetChildren()){
            _animalHeaderSlot.RemoveChild(child);
            child.QueueFree();
        }
        _animalHeaderSlot.AddChild(AnimalHeader(tab));
    }

    void UpdateManagementSummary()
    {
        if(_managementSummary==null)return;
        int stored=Data.Snapshot["inventory"]!.AsArray().Sum(row=>WorldData.I(row!,"quantity"));
        int day=1+(int)((Game.Now-NativeGame.D(Data.Meta,"start_ts"))/86400);
        _managementSummary.Text=$"金币 {WorldData.I(Data.Meta,"coins")}    仓库 {stored}/{Game.Storage}    第{day}天 {GameClockText}";
    }

    void RefreshManagementPage(double delta)
    {
        _managementRefreshElapsed+=delta;
        if(_managementRefreshElapsed<.75)return;
        _managementRefreshElapsed=0;
        UpdateJournalBadge();
        if(_management?.Visible==true)_refreshManagementPage?.Invoke();
    }

    void AddManagementFeedback(Container workspace)
    {
        _managementFeedback=new PanelContainer{Name="ManagementFeedback",Visible=false};
        workspace.AddChild(_managementFeedback);
        var row=new HBoxContainer();
        row.AddThemeConstantOverride("separation",8);
        _managementFeedback.AddChild(row);
        _managementFeedbackKind=new Label{VerticalAlignment=VerticalAlignment.Center};
        row.AddChild(_managementFeedbackKind);
        _managementFeedbackLabel=new Label{AutowrapMode=TextServer.AutowrapMode.WordSmart,MaxLinesVisible=2,
            MouseFilter=Control.MouseFilterEnum.Pass,
            TextOverrunBehavior=TextServer.OverrunBehavior.TrimEllipsis,SizeFlagsHorizontal=Control.SizeFlags.ExpandFill};
        row.AddChild(_managementFeedbackLabel);
        var copy=new Button{Text="复制",TooltipText="复制完整反馈",AccessibilityName="复制完整反馈",
            SizeFlagsVertical=Control.SizeFlags.ShrinkCenter};
        TownTheme.AccentButton(copy,"照护",4);
        copy.Pressed+=()=>DisplayServer.ClipboardSet(_managementFeedbackLabel.Text);
        row.AddChild(copy);
        _managementFeedbackDismiss=new Button{Name="ManagementFeedbackDismiss",Text="×",TooltipText="收起反馈",
            AccessibilityName="收起反馈",SizeFlagsVertical=Control.SizeFlags.ShrinkCenter,CustomMinimumSize=new(28,28)};
        TownTheme.AccentButton(_managementFeedbackDismiss,"环境",4);
        _managementFeedbackDismiss.Pressed+=()=>{
            bool focused=_managementFeedbackDismiss.HasFocus();
            SetManagementFeedback("");
            if(focused)FocusCurrentManagementTab();
        };
        row.AddChild(_managementFeedbackDismiss);
    }

    void SetManagementFeedback(string message,bool failed=false,bool completed=false)
    {
        if(_managementFeedback==null||_managementFeedbackLabel==null||_managementFeedbackKind==null)return;
        _managementFeedback.Visible=message.Length>0;
        _managementFeedbackKind.Text=failed?"未完成":completed?"已完成":"提示";
        _managementFeedbackLabel.Text=message;
        _managementFeedbackLabel.TooltipText=message;
        _managementFeedback.AccessibilityName=_managementFeedbackKind.Text;
        _managementFeedback.AccessibilityDescription=message;
        var color=new Color(failed?"71382e":completed?"2d6045":"4d5038");
        _managementFeedbackKind.AddThemeColorOverride("font_color",color);
        _managementFeedbackLabel.AddThemeColorOverride("font_color",TownTheme.Ink);
        var style=TownTheme.Box(failed?"f5ded5":completed?"e1eee0":"f0eadb",8,8);
        style.BorderWidthLeft=3;
        style.BorderColor=color;
        _managementFeedback.AddThemeStyleboxOverride("panel",style);
    }

    void ConfirmManagementAction(string title,string details,string confirmLabel,Action execute)
    {
        var dialog=new ConfirmationDialog{
            Title=title,Borderless=true,DialogText=$"{title}\n\n{details}",OkButtonText=confirmLabel,DialogAutowrap=true,
            CancelButtonText="取消",Transient=true,Exclusive=true,Theme=_management?.Theme??_panel.Theme
        };
        (_management?.Visible==true?_management:GetWindow()).AddChild(dialog);
        TownTheme.AccentButton(dialog.GetOkButton(),"事件",6);
        TownTheme.AccentButton(dialog.GetCancelButton(),"照护",6);
        dialog.Canceled+=()=>{dialog.Hide();dialog.QueueFree();};
        dialog.Confirmed+=()=>{dialog.Hide();dialog.QueueFree();execute();};
        dialog.PopupCenteredClamped(new Vector2I(430,150),.9f);
        dialog.GetCancelButton().GrabFocus();
    }

    Label Text(string text)
    {
        var label=new Label{Text=text,AutowrapMode=TextServer.AutowrapMode.WordSmart};
        _managementBody!.AddChild(label);
        return label;
    }

    Label? Section(string title,string detail="")
    {
        var panel=new PanelContainer();
        Color accent=TownTheme.Accent(_tab);
        panel.AddThemeStyleboxOverride("panel",TownTheme.Box(accent.Lerp(new Color("fffaf0"),.88f).ToHtml(),9,9));
        _managementBody!.AddChild(panel);
        var row=new HBoxContainer();
        row.AddThemeConstantOverride("separation",12);
        panel.AddChild(row);
        var label=new Label{Text=title,SizeFlagsHorizontal=Control.SizeFlags.ExpandFill};
        label.AddThemeFontSizeOverride("font_size",18);
        label.AddThemeColorOverride("font_color",accent.Darkened(.28f));
        row.AddChild(label);
        if(detail.Length==0)return null;
        var note=new Label{Text=detail,AutowrapMode=TextServer.AutowrapMode.WordSmart,HorizontalAlignment=HorizontalAlignment.Right,SizeFlagsHorizontal=Control.SizeFlags.ExpandFill};
        note.AddThemeColorOverride("font_color",TownTheme.Muted);
        row.AddChild(note);
        return note;
    }

    Button ActionButton(string text,Action action,string unavailable="")
    {
        var button=new Button{
            Text=unavailable.Length==0?text:$"{text} · {unavailable}",Disabled=unavailable.Length>0,
            TooltipText=unavailable.Length==0?text:unavailable,SizeFlagsHorizontal=Control.SizeFlags.ShrinkBegin,
            CustomMinimumSize=new(160,36)
        };
        TownTheme.AccentButton(button,_tab,6);
        button.Pressed+=action;
        _managementBody!.AddChild(button);
        return button;
    }

    OptionButton Options(IEnumerable<string> values)
    {
        var option=new OptionButton{SizeFlagsHorizontal=Control.SizeFlags.ShrinkBegin,CustomMinimumSize=new(240,36),FitToLongestItem=false,TextOverrunBehavior=TextServer.OverrunBehavior.TrimEllipsis};
        TownTheme.AccentButton(option,_tab,6);
        foreach(string value in values)option.AddItem(value);
        _managementBody!.AddChild(option);
        return option;
    }
}
