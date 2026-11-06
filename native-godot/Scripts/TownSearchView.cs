using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

public partial class Main
{
    sealed record TownSearchResult(string Kind,string Id,string Title,string Detail,Action Open,
        string Caption="打开详情",string AlternateCaption="",Action? Alternate=null)
    {
        public string Category=>Kind is "居民" or "通勤者"?"村民":Kind;
    }
    const int TownSearchRecentLimit=8;
    const int TownSearchPageSize=7;
    static readonly string[] TownSearchCategories={"全部","村民","伙伴","物品","街区","工程","订单","图钉","页面"};
    static readonly (string Tab,string Target,string Title,string Keywords)[] TownSearchShortcuts={
        ("环境","display","界面与声音设置","画质 缩放 字号 对比 动效 图形 雨声"),
        ("环境","keybindings","快捷键设置","键盘 按键 控制 操作"),
        ("环境","camera-input","镜头控制","缩放 滚轮 旋转 灵敏度 反转"),
        ("环境","camera-bookmarks","镜头机位","保存 视角 镜头收藏"),
        ("环境","audio","小镇总音量","声音 环境音 静音 音效"),
        ("家庭","residents","村民名册","居民 职业 住址 找村民"),
        ("环境","farm-plots","农田地块","种植 浇水 轮作 收获"),
        ("建造","manor","庄园建筑","修建 施工 升级"),
        ("事件","weekend-festivals","周末活动","节庆 集市 展示日 节日")
    };
    readonly List<(string Category,string Id)> _recentTownSearch=new();
    bool _townSearchLoadedRecents;
    Window? _townSearchWindow;

    IEnumerable<TownSearchResult> FindTownSearchMatches(string query)
    {
        var terms=Regex.Split(NormalizeTownSearch(query),@"[\s,、;]+").Where(term=>term.Length>0)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if(terms.Length==0)yield break;
        bool Match(params string[] fields)
        {
            var indexed=fields.Select(NormalizeTownSearch).ToArray();
            return terms.All(term=>indexed.Any(field=>field.Contains(term,StringComparison.OrdinalIgnoreCase)));
        }
        foreach(var entry in ResidentDirectoryEntries()){
            if(!Match(entry.Name,entry.Profession,entry.Role,entry.District,entry.Activity,entry.Id,"村民"))continue;
            bool locatable=entry.Place!=ResidentPlace.Unavailable;
            string action=entry.Place switch{ResidentPlace.Home=>"定位住宅",ResidentPlace.Freight=>"定位货车",
                ResidentPlace.Outdoors=>"定位村民",_=>"查看名册"};
            string identity=string.Join(" · ",new[]{entry.Profession,entry.District,action}.Where(text=>text.Length>0));
            yield return new(entry.Role,entry.Id,entry.Name,identity,()=>{
                if(DirectoryPerson(entry.Id) is JsonNode person&&DescribeDirectoryPerson(person,DirectoryActor(entry.Id)).Place==ResidentPlace.Unavailable)
                    OpenResidentDirectoryEntry(entry.Id);
                else LocateResident(entry.Id);
            },action,locatable?"查看名册":"",locatable?()=>OpenResidentDirectoryEntry(entry.Id):null);
        }
        foreach(var pet in Data.Pets.OfType<JsonNode>().OrderByDescending(Game.Active)){
            string name=NativeGame.S(pet,"name"),species=NativeGame.S(pet,"species"),id=NativeGame.S(pet,"pet_id");
            string place=PetLocationStatus(pet);
            if(!Match(name,species,id,place,"伙伴 宠物"))continue;
            bool locatable=Game.Active(pet)&&FarmPet(id)!=null;
            yield return new("伙伴",id,name,$"{species} · {place}",()=>OpenPetManagement(id),"查看伙伴",
                locatable?"定位到场景":"",locatable?()=>{if(!LocatePet(id))OpenPetManagement(id);}:null);
        }
        foreach(var pair in Data.Root["zones"]!.AsObject()){
            string zone=pair.Key,name=NativeGame.S(pair.Value!,"name");
            if(Match(name,zone,"街区"))yield return new("街区",zone,name,Game.Effective(zone)?"路网已接通":"查看街区与路网",
                ()=>ShowManagement("地图",$"district:{zone}"),"在地图查看");
        }
        foreach(var pair in Game.Rules["ITEMS"]!.AsObject()){
            string item=pair.Key;
            if(Match(item,"物品"))yield return new("物品",item,item,$"可用库存 {Game.Stock(item)} 份",()=>OpenSearchSupply(item),"打开货架");
        }
        foreach(var building in Game.Rows("town_buildings").OfType<JsonNode>()){
            string id=NativeGame.S(building,"building_id"),name=TownProjectName(building);
            string place=TownProjectCell(building);
            if(Match(name,id,place,"工程"))yield return new("工程",id,name,place,()=>OpenTownProject(id),"查看工程","定位到场景",()=>LocateTownProject(id));
        }
        foreach(var order in Game.Rows("orders").OfType<JsonNode>().Where(order=>OrderPending(order,Game.Now))){
            string name=NativeGame.S(order,"npc"),request=NativeGame.OrderRequestCaption(order),id=NativeGame.S(order,"order_id");
            if(Match(name,request,id,"订单"))yield return new("订单",id,name,request,()=>ShowManagement("订单",id),"查看订单");
        }
        foreach(var pin in Game.MapPins())
            if(Match(pin.Name,pin.Note,pin.Id,"图钉"))yield return new("图钉",pin.Id,pin.Name,$"({pin.Cell.X}, {pin.Cell.Y}) · {pin.Note}",
                ()=>LocateMapPin(pin.Id),"定位到场景","在地图查看",()=>ShowManagement("地图","pin:"+pin.Id));
        foreach(var tab in ManagementTabs){
            string group=ManagementGroups.First(entry=>entry.Tabs.Contains(tab)).Name;
            if(Match(tab,group,"页面"))yield return TownSearchPage(tab,tab,"",$"经营手账 · {group}");
        }
        foreach(var (tab,target,title,keywords) in TownSearchShortcuts){
            string id=tab+":"+target;
            if(Match(title,keywords,tab,id,"页面"))yield return TownSearchPage(title,tab,target,$"经营手账 · {tab}");
        }
    }

    static string NormalizeTownSearch(string text)=>text.Normalize(NormalizationForm.FormKC).Trim();

    TownSearchResult TownSearchPage(string title,string tab,string target,string description)=>
        new("页面",target.Length==0?tab:tab+":"+target,title,description,()=>ShowManagement(tab,target),"打开页面");

    TownSearchResult[] TownSearchMatches(string query,string category)
    {
        string normalized=NormalizeTownSearch(query);
        var matches=normalized.Length>0?FindTownSearchMatches(normalized).OrderByDescending(result=>{
            string title=NormalizeTownSearch(result.Title);
            return title.Equals(normalized,StringComparison.OrdinalIgnoreCase)?2:title.StartsWith(normalized,StringComparison.OrdinalIgnoreCase)?1:0;
        }):_recentTownSearch.SelectMany(recent=>FindTownSearchMatches(recent.Id).Where(result=>result.Category==recent.Category&&result.Id==recent.Id));
        return matches.Where(result=>category=="全部"||result.Category==category).ToArray();
    }

    string TownSearchRecentsPath=>$"user://town-search-{_saveSlot}.cfg";

    void LoadTownSearchRecents()
    {
        if(_townSearchLoadedRecents)return;
        _townSearchLoadedRecents=true;
        if(!UseTownPreferences())return;
        using var config=new ConfigFile();
        if(config.Load(TownSearchRecentsPath)!=Error.Ok)return;
        foreach(string value in config.GetValue("search","recents",Array.Empty<string>()).AsStringArray().Take(TownSearchRecentLimit)){
            int separator=value.IndexOf('|');
            if(separator>0&&separator<value.Length-1)_recentTownSearch.Add((value[..separator],value[(separator+1)..]));
        }
    }

    void RecordTownSearchRecent(TownSearchResult result)
    {
        var key=(result.Category,result.Id);
        _recentTownSearch.Remove(key);
        _recentTownSearch.Insert(0,key);
        if(_recentTownSearch.Count>TownSearchRecentLimit)_recentTownSearch.RemoveAt(TownSearchRecentLimit);
        if(!UseTownPreferences())return;
        using var config=new ConfigFile();
        config.SetValue("search","recents",_recentTownSearch.Select(recent=>recent.Category+"|"+recent.Id).ToArray());
        config.Save(TownSearchRecentsPath);
    }

    void OpenSearchSupply(string item)
    {
        _orderSupplyRoute=null;
        _careSupplyRoute=null;
        _marketSection=1;
        _marketFilter=0;
        _marketSearch=item;
        ShowManagement("商店",$"supply:{item}");
    }

    void OpenTownSearch()
    {
        if(GodotObject.IsInstanceValid(_townSearchWindow)&&_townSearchWindow!.Visible)return;
        LoadTownSearchRecents();
        var parent=_management?.Visible==true?_management:GetWindow();
        var previousFocus=parent.GuiGetFocusOwner();
        var window=new Window{Name="TownGlobalSearch",Title="搜索小镇",Transient=true,Exclusive=true,
            Borderless=true,TransparentBg=true,Visible=false,Theme=_panel.Theme};
        _townSearchWindow=window;
        if(_management?.Visible==true)_management.AddChild(window);
        else AddChild(window);
        var frame=new PanelContainer();
        frame.AddThemeStyleboxOverride("panel",TownTheme.Box("fffaf0",14,14));
        window.AddChild(frame);
        frame.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        var body=new VBoxContainer();
        body.AddThemeConstantOverride("separation",8);
        frame.AddChild(body);
        var top=new HBoxContainer();
        body.AddChild(top);
        MarketLabel(top,"搜索小镇",22+ExtraFontSize,TownTheme.Leaf).SizeFlagsHorizontal=Control.SizeFlags.ExpandFill;
        MarketButton(top,"关闭",()=>Close(restoreFocus:true),expand:false,accent:"存档").TooltipText="关闭搜索（Esc）";
        var filterRow=new HBoxContainer();
        body.AddChild(filterRow);
        var input=new LineEdit{Name="TownSearchQuery",PlaceholderText="输入名称、地点或功能",ClearButtonEnabled=true,MaxLength=120,
            SizeFlagsHorizontal=Control.SizeFlags.ExpandFill,AccessibilityName="搜索关键词",
            AccessibilityDescription="可以输入多个关键词，用空格隔开。"};
        filterRow.AddChild(input);
        var category=MarketOptions(filterRow,TownSearchCategories,0);
        category.Name="TownSearchCategory";
        category.AccessibilityName="搜索分类和匹配数量";
        var hint=MarketLabel(body,"",14+ExtraFontSize,TownTheme.Muted,wrap:true);
        hint.Name="TownSearchSummary";
        var layout=new BoxContainer{SizeFlagsVertical=Control.SizeFlags.ExpandFill};
        layout.AddThemeConstantOverride("separation",9);
        body.AddChild(layout);
        var scroll=new ScrollContainer{SizeFlagsVertical=Control.SizeFlags.ExpandFill,SizeFlagsHorizontal=Control.SizeFlags.ExpandFill,
            HorizontalScrollMode=ScrollContainer.ScrollMode.Disabled,VerticalScrollMode=ScrollContainer.ScrollMode.Reserve,
            CustomMinimumSize=new(0,88),AccessibilityName="搜索结果列表"};
        layout.AddChild(scroll);
        var matches=new VBoxContainer{Name="TownSearchResults",SizeFlagsHorizontal=Control.SizeFlags.ExpandFill};
        scroll.AddChild(matches);

        var detailPanel=new PanelContainer{CustomMinimumSize=new(220,0)};
        detailPanel.AddThemeStyleboxOverride("panel",TownTheme.Box("e9efea",10,10));
        layout.AddChild(detailPanel);
        var detail=new VBoxContainer();
        detailPanel.AddChild(detail);
        var detailKind=MarketLabel(detail,"",13+ExtraFontSize,TownTheme.Muted);
        var detailName=MarketLabel(detail,"",18+ExtraFontSize,TownTheme.Ink,wrap:true);
        detailName.Name="TownSearchPreviewName";
        var detailBody=MarketLabel(detail,"",14+ExtraFontSize,TownTheme.Muted,wrap:true);
        detailBody.MaxLinesVisible=4;
        detailBody.TextOverrunBehavior=TextServer.OverrunBehavior.TrimEllipsis;
        var actions=new HFlowContainer();
        detail.AddChild(actions);
        var open=SearchButton(actions,"TownSearchOpen","打开","地图");
        var alternate=SearchButton(actions,"TownSearchAlternate","定位","环境");
        alternate.TooltipText="执行这一项的次要操作（Shift+Enter）";

        var navigation=new HBoxContainer();
        body.AddChild(navigation);
        var previous=SearchButton(navigation,"TownSearchPreviousPage","上一页","地图");
        previous.TooltipText="上一页（Page Up）";
        var position=MarketLabel(navigation,"",14+ExtraFontSize,TownTheme.Muted);
        position.Name="TownSearchPage";
        position.VerticalAlignment=VerticalAlignment.Center;
        var next=SearchButton(navigation,"TownSearchNextPage","下一页","地图");
        next.TooltipText="下一页（Page Down）";

        TownSearchResult[] results=Array.Empty<TownSearchResult>(), visible=Array.Empty<TownSearchResult>();
        Button[] buttons=Array.Empty<Button>();
        int page=0,selection=0;
        int Pages()=>Math.Max(1,(results.Length+TownSearchPageSize-1)/TownSearchPageSize);
        open.Pressed+=()=>Activate(false);
        alternate.Pressed+=()=>Activate(true);
        previous.Pressed+=()=>ChangePage(-1);
        next.Pressed+=()=>ChangePage(1);
        input.TextChanged+=_=>Refresh();
        category.ItemSelected+=_=>{Refresh();input.GrabFocus();};
        input.TextSubmitted+=_=>Activate(false);
        window.CloseRequested+=()=>Close(restoreFocus:true);
        window.WindowInput+=inputEvent=>{
            if(inputEvent is not InputEventKey {Pressed:true,AltPressed:false,MetaPressed:false} key)return;
            if(!key.Echo&&key.CtrlPressed&&MatchTownInput(key,"search")){
                window.SetInputAsHandled();input.GrabFocus();input.SelectAll();return;
            }
            if(key.CtrlPressed)return;
            switch(TownPhysicalKey(key)){
                case Key.Escape:window.SetInputAsHandled();Close(restoreFocus:true);break;
                case Key.Up when input.HasFocus():window.SetInputAsHandled();MoveSelection(-1);break;
                case Key.Down when input.HasFocus():window.SetInputAsHandled();MoveSelection(1);break;
                case Key.Pageup:window.SetInputAsHandled();ChangePage(-1);break;
                case Key.Pagedown:window.SetInputAsHandled();ChangePage(1);break;
                case Key.Enter or Key.KpEnter when key.ShiftPressed&&!key.Echo:
                    window.SetInputAsHandled();Activate(true);break;
            }
        };
        window.SizeChanged+=AdaptLayout;
        Refresh();
        window.PopupCenteredClamped(new(760,500),.94f);
        AdaptLayout();
        input.GrabFocus();

        void Refresh()
        {
            bool typed=NormalizeTownSearch(input.Text).Length>0;
            var all=TownSearchMatches(input.Text,"全部");
            bool recommended=!typed&&all.Length==0;
            if(recommended)all=new[]{TownSearchPage("今日待办","今日待办","","检查待办与周历"),
                TownSearchPage("地图","地图","","查看街区、村民和图钉"),
                TownSearchPage("界面与声音设置","环境","display","调整画质、字号和声音")};
            var counts=all.GroupBy(result=>result.Category).ToDictionary(group=>group.Key,group=>group.Count());
            for(int i=0;i<TownSearchCategories.Length;i++){
                string caption=TownSearchCategories[i];
                int count=i==0?all.Length:counts.GetValueOrDefault(caption);
                category.SetItemText(i,$"{caption} · {count}");
            }
            string filter=TownSearchCategories[category.Selected];
            results=filter=="全部"?all:all.Where(result=>result.Category==filter).ToArray();
            hint.Text=results.Length==0?(typed?"这一分类没有匹配结果。可改用其他关键词或切换分类。":"输入关键词查看结果。可用空格组合多个词。")
                :$"{(typed?"搜索结果":recommended?"常用入口":"最近访问")} · 共 {results.Length} 条 · ↑↓ 选择，Enter 打开";
            page=0;
            RenderPage();
        }

        void RenderPage(int chosen=0)
        {
            foreach(var row in matches.GetChildren()){matches.RemoveChild(row);row.QueueFree();}
            visible=results.Skip(page*TownSearchPageSize).Take(TownSearchPageSize).ToArray();
            buttons=visible.Select((result,index)=>{
                string title=$"{result.Kind} · {result.Title}";
                var button=SearchButton(matches,$"TownSearchResult{index}",title,SearchAccent(result.Category));
                button.Pressed+=()=>OpenResult(result,false);
                button.ToggleMode=true;
                button.FocusMode=Control.FocusModeEnum.None;
                button.ClipText=true;
                button.Alignment=HorizontalAlignment.Left;
                button.AccessibilityName=title;
                button.AccessibilityDescription=result.Detail;
                button.TooltipText=title+"\n"+result.Detail;
                button.GuiInput+=inputEvent=>{if(inputEvent is InputEventMouseMotion&&selection!=index)Choose(index);};
                return button;
            }).ToArray();
            position.Text=$"第 {page+1} / {Pages()} 页";
            navigation.Visible=Pages()>1;
            previous.Disabled=page==0;
            next.Disabled=page+1>=Pages();
            scroll.SetDeferred("scroll_vertical",0);
            Choose(chosen);
            if(chosen>0)RevealSelection();
        }

        void Choose(int index)
        {
            selection=index;
            for(int i=0;i<buttons.Length;i++)buttons[i].SetPressedNoSignal(i==selection);
            var result=visible.Length==0?null:visible[selection];
            detailKind.Text=result==null?"搜索提示":result.Kind;
            detailName.Text=result?.Title??"这里会显示所选条目";
            detailName.TooltipText=result?.Title??"";
            detailBody.Text=result?.Detail??"例如输入「农田 浇水」可打开对应设置。";
            detailBody.TooltipText=result?.Detail??"";
            actions.Visible=result!=null;
            open.Text=result?.Caption??"打开";
            alternate.Visible=result?.Alternate!=null;
            alternate.Text=result?.AlternateCaption??"";
        }

        void MoveSelection(int step)
        {
            if(results.Length==0)return;
            int index=(page*TownSearchPageSize+selection+step+results.Length)%results.Length;
            int nextPage=index/TownSearchPageSize;
            if(nextPage!=page){page=nextPage;RenderPage(index%TownSearchPageSize);return;}
            Choose(index%TownSearchPageSize);
            RevealSelection();
        }

        void RevealSelection()
        {
            var chosen=buttons[selection];
            Callable.From(()=>{if(GodotObject.IsInstanceValid(chosen)&&chosen.IsVisibleInTree())scroll.EnsureControlVisible(chosen);}).CallDeferred();
        }

        void ChangePage(int step)
        {
            int nextPage=Math.Clamp(page+step,0,Pages()-1);
            if(nextPage==page)return;
            page=nextPage;
            RenderPage();
            input.GrabFocus();
        }

        void AdaptLayout()
        {
            layout.Vertical=window.Size.X<620+ExtraFontSize*16;
            detailPanel.CustomMinimumSize=layout.Vertical?Vector2.Zero:new(240+ExtraFontSize*12,0);
            detailName.MaxLinesVisible=layout.Vertical?2:3;
            detailName.TextOverrunBehavior=TextServer.OverrunBehavior.TrimEllipsis;
            detailBody.MaxLinesVisible=layout.Vertical?2:4;
        }

        void Close(bool restoreFocus)
        {
            if(!window.Visible)return;
            window.Hide();
            window.QueueFree();
            _townSearchWindow=null;
            if(restoreFocus&&GodotObject.IsInstanceValid(previousFocus)&&previousFocus!.IsVisibleInTree())previousFocus.GrabFocus();
        }

        void Activate(bool secondary)
        {
            if(visible.Length>0)OpenResult(visible[selection],secondary);
        }

        void OpenResult(TownSearchResult result,bool secondary)
        {
            var command=secondary?result.Alternate:result.Open;
            if(command==null)return;
            RecordTownSearchRecent(result);
            Close(restoreFocus:false);
            command();
        }
    }

    static Button SearchButton(Container parent,string name,string text,string accent)
    {
        var button=new Button{Name=name,Text=text,CustomMinimumSize=new(0,36)};
        TownTheme.AccentButton(button,accent,6);
        parent.AddChild(button);
        return button;
    }

    static string SearchAccent(string category)=>category switch{
        "村民"=>"家庭","伙伴"=>"宠物","物品"=>"商店","工程"=>"建造","订单"=>"订单","页面"=>"环境",_=>"地图"
    };
}
