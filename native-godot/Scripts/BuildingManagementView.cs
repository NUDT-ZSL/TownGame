using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;

public partial class Main
{
    static readonly string[] BuildWorkspaces={"庄园建筑","道路","城镇建筑","装饰","蓝图"};
    static readonly string[] BuildWorkspaceColors={"建造","地图","商店","环境","地图"};
    static readonly string[] BuildOrientations={"朝北","朝东","朝南","朝西"};
    int _buildWorkspace, _buildPlot, _buildManorType, _buildRoadStyle, _buildTownType, _buildDecorTheme, _buildDistrict;
    int _buildX=20, _buildY=20, _buildOrientation;
    string _buildTownId="", _buildDecorationId="", _buildDecorKind="";
    string _buildCatalogQuery="";
    int _buildCatalogCategory;
    bool _buildCatalogBudgetOnly;

    void BuildControls()
    {
        if(_managementEntryTarget.StartsWith("decor:",StringComparison.Ordinal)||
           _managementEntryTarget.StartsWith("placed-decor:",StringComparison.Ordinal))_buildWorkspace=3;
        var navigation=new HFlowContainer();
        navigation.AddThemeConstantOverride("h_separation",8);
        navigation.AddThemeConstantOverride("v_separation",5);
        _managementBody!.AddChild(navigation);
        var workspace=new VBoxContainer();
        workspace.AddThemeConstantOverride("separation",10);
        _managementBody.AddChild(workspace);
        var buttons=new Button[BuildWorkspaces.Length];

        void ShowWorkspace()
        {
            for(int i=0;i<buttons.Length;i++)buttons[i].SetPressedNoSignal(i==_buildWorkspace);
            foreach(Node child in workspace.GetChildren()){
                workspace.RemoveChild(child);
                child.QueueFree();
            }
            switch(_buildWorkspace){
                case 0: BuildManorWorkspace(workspace); break;
                case 1: BuildRoadWorkspace(workspace); break;
                case 2: BuildTownWorkspace(workspace); break;
                case 3: BuildDecorationWorkspace(workspace); break;
                case 4: BuildBlueprintWorkspace(workspace); break;
            }
        }

        for(int i=0;i<buttons.Length;i++){
            int index=i;
            var button=new Button{Text=BuildWorkspaces[i],ToggleMode=true,SizeFlagsHorizontal=Control.SizeFlags.ExpandFill};
            TownTheme.AccentButton(button,BuildWorkspaceColors[i]);
            button.Pressed+=()=>{
                _buildWorkspace=index;
                ShowWorkspace();
                _managementScroll!.SetDeferred("scroll_vertical",0);
            };
            navigation.AddChild(button);
            buttons[i]=button;
        }
        ShowWorkspace();
    }

    VBoxContainer BuildCard(Container parent,string title)
    {
        var accent=TownTheme.Accent(BuildWorkspaceColors[_buildWorkspace]);
        var card=new PanelContainer();
        card.AddThemeStyleboxOverride("panel",TownTheme.Box(accent.Lerp(new Color("fffcf4"),.91f).ToHtml(),11,12));
        parent.AddChild(card);
        var body=new VBoxContainer();
        body.AddThemeConstantOverride("separation",8);
        card.AddChild(body);
        var heading=BuildText(body,title);
        heading.AddThemeColorOverride("font_color",accent.Darkened(.15f));
        heading.AddThemeFontSizeOverride("font_size",18);
        return body;
    }

    static Label BuildText(Container parent,string text)
    {
        var label=new Label{Text=text,AutowrapMode=TextServer.AutowrapMode.WordSmart};
        parent.AddChild(label);
        return label;
    }

    static HFlowContainer BuildOperations(Container parent)
    {
        var row=new HFlowContainer();
        row.AddThemeConstantOverride("h_separation",8);
        row.AddThemeConstantOverride("v_separation",6);
        parent.AddChild(row);
        return row;
    }

    Button BuildButton(Container parent,string text,Action? execute=null,string reason="",bool removal=false)
    {
        var button=new Button{Text=text};
        TownTheme.AccentButton(button,removal?"事件":BuildWorkspaceColors[_buildWorkspace]);
        SetBuildAvailability(button,reason);
        if(execute!=null)button.Pressed+=execute;
        parent.AddChild(button);
        return button;
    }

    static void SetBuildAvailability(Button button,string reason)
    {
        button.Disabled=reason!="";
        button.TooltipText=button.Disabled?reason:button.Text;
    }

    OptionButton BuildChoice(Container parent,IEnumerable<string> items,int selected)
    {
        var option=new OptionButton{
            FitToLongestItem=false,
            TextOverrunBehavior=TextServer.OverrunBehavior.TrimEllipsis,
            SizeFlagsHorizontal=Control.SizeFlags.ExpandFill,
            CustomMinimumSize=new(142,38)
        };
        TownTheme.AccentButton(option,BuildWorkspaceColors[_buildWorkspace]);
        foreach(string item in items)option.AddItem(item);
        if(option.ItemCount>0)option.Select(Math.Clamp(selected,0,option.ItemCount-1));
        parent.AddChild(option);
        return option;
    }

    (SpinBox X,SpinBox Y) BuildCoordinates(Container parent)
    {
        var fields=BuildOperations(parent);
        var x=new SpinBox{Prefix="列 X",MinValue=0,MaxValue=124,Value=_buildX,CustomMinimumSize=new(108,38)};
        var y=new SpinBox{Prefix="行 Y",MinValue=0,MaxValue=79,Value=_buildY,CustomMinimumSize=new(108,38)};
        fields.AddChild(x);
        fields.AddChild(y);
        x.ValueChanged+=value=>_buildX=(int)value;
        y.ValueChanged+=value=>_buildY=(int)value;
        BuildButton(fields,"使用镜头所在格",()=>{
            var cell=WorldData.Grid(_center);
            x.Value=Math.Clamp(cell.X,0,124);
            y.Value=Math.Clamp(cell.Y,0,79);
        });
        return (x,y);
    }

    string BuildAffordability(int cost)=>NativeGame.I(Game.Meta,"coins")<cost?"金币不足":"";
    static string BuildQuoteText(string quote,string reason)=>reason==""?quote:$"{quote} · {reason}";

    void BuildManorWorkspace(Container workspace)
    {
        var card=BuildCard(workspace,"庄园地块");
        if(_managementEntryTarget=="manor")_managementFocusTarget=card.GetParent<Control>();
        var plots=Game.Rows("buildings").Select(row=>row!).OrderBy(row=>NativeGame.I(row,"building_id")).ToArray();
        string PlotTitle(JsonNode plot)
        {
            string kind=NativeGame.S(plot,"kind");
            string title=kind!=""?$"{kind} · {NativeGame.I(plot,"level")}级":NativeGame.B(plot,"unlocked")?"可建空地":"未解锁";
            return $"地块 {NativeGame.I(plot,"building_id")} · {title}";
        }
        if(plots.Length==0){
            BuildText(card,"暂无庄园地块。");
            return;
        }
        var plotSelector=BuildChoice(card,plots.Select(PlotTitle),Array.FindIndex(plots,plot=>NativeGame.I(plot,"building_id")==_buildPlot));
        var details=new VBoxContainer();
        details.AddThemeConstantOverride("separation",8);
        card.AddChild(details);

        void ShowPlot()
        {
            foreach(Node child in details.GetChildren()){
                details.RemoveChild(child);
                child.QueueFree();
            }
            var plot=plots[plotSelector.Selected];
            int id=NativeGame.I(plot,"building_id");
            _buildPlot=id;
            string kind=NativeGame.S(plot,"kind");
            if(!NativeGame.B(plot,"unlocked")){
                int price=Game.ManorUnlockPrice();
                string reason=Game.HasOpenManorNeighbor(id)?BuildAffordability(price):"需与开放地块相邻";
                BuildText(details,BuildQuoteText($"解锁费用 {price} 金币",reason));
                BuildButton(BuildOperations(details),"解锁地块",()=>Act("unlock",new JsonObject{{"building_id",id}}),reason);
                return;
            }
            if(kind==""){
                var specs=Game.Rules["BUILDINGS"]!.AsObject().ToArray();
                var selector=BuildChoice(details,specs.Select(spec=>$"{spec.Key} · {NativeGame.I(spec.Value!,"cost")} 金币"),_buildManorType);
                var description=BuildText(details,"");
                var build=BuildButton(BuildOperations(details),"建造所选建筑",()=>Act("build_manor",new JsonObject{{"building_id",id},{"kind",specs[selector.Selected].Key}}));
                void UpdateQuote()
                {
                    _buildManorType=selector.Selected;
                    var spec=specs[selector.Selected];
                    int cost=NativeGame.I(spec.Value!,"cost");
                    int capacity=NativeGame.I(spec.Value!,"capacity");
                    string effect=capacity>0?$"可容纳 {capacity} 只伙伴":spec.Key=="仓库"?$"增加 {NativeGame.I(Game.Rules,"STORAGE_PER_LEVEL",100)} 格仓储":"";
                    string reason=BuildAffordability(cost);
                    description.Text=BuildQuoteText(BuildQuoteText($"建造费用 {cost} 金币",effect),reason);
                    SetBuildAvailability(build,reason);
                }
                selector.ItemSelected+=_=>UpdateQuote();
                UpdateQuote();
                return;
            }
            int level=NativeGame.I(plot,"level");
            var (refund,demolitionReason)=Game.ManorDemolitionQuote(plot);
            int upgradePrice=Game.ManorUpgradePrice(plot);
            string upgradeReason=level>=5?"已达最高等级":BuildAffordability(upgradePrice);
            string upgrade=level>=5?upgradeReason:BuildQuoteText($"升至 {level+1} 级需 {upgradePrice} 金币",upgradeReason);
            BuildText(details,upgrade);
            if(level<5)BuildText(details,Game.ManorUpgradeImpact(plot));
            BuildText(details,demolitionReason==""?$"拆除返还 {refund} 金币":$"拆除受限 · {demolitionReason}");
            string removalImpact=Game.ManorDemolitionImpact(plot);
            BuildText(details,"拆除影响 · "+removalImpact);
            var actions=BuildOperations(details);
            BuildButton(actions,"升级建筑",()=>Act("upgrade_manor",new JsonObject{{"building_id",id}}),upgradeReason);
            BuildButton(actions,"拆除建筑",()=>ConfirmManagementAction("拆除庄园建筑",
                $"地块 {id} 的 {level} 级{kind}将被拆除。\n返还 {refund} 金币 · {removalImpact}", "确认拆除",
                ()=>Act("demolish_manor",new JsonObject{{"building_id",id}})),demolitionReason,removal:true);
            AddBuildingFinishControls(details,()=>Game.Rows("buildings").FirstOrDefault(row=>NativeGame.I(row!,"building_id")==id),false);
        }
        plotSelector.ItemSelected+=_=>ShowPlot();
        ShowPlot();
    }

    void BuildRoadWorkspace(Container workspace)
    {
        var tools=BuildCard(workspace,"在场景中施工");
        var styles=Game.Rules["ROAD_STYLES"]!.AsObject().ToArray();
        var selector=BuildChoice(tools,styles.Select(style=>$"{NativeGame.S(style.Value!,"name")} · {NativeGame.I(style.Value!,"cost")} 金币 / 格"),_buildRoadStyle);
        string Style()=>styles[selector.Selected].Key;
        var gestures=BuildOperations(tools);
        BuildButton(gestures,"拖拽铺路",()=>ArmPlacement("road",new JsonObject{{"style",Style()}}));
        BuildButton(gestures,"自动接入主路",()=>ArmPlacement("road",new JsonObject{{"style",Style()},{"auto_connect",true}}));
        BuildButton(gestures,"拖拽拆路",()=>ArmPlacement("remove_road",new JsonObject{{"style",Style()}}));
        int history=Game.Meta["native_road_history"]!.AsArray().Count;
        bool legacyUndo=Game.Meta["native_road_undo"] is JsonNode old&&NativeGame.I(old,"revision")==NativeGame.I(Game.Meta,"road_revision");
        BuildButton(gestures,"撤销道路操作",()=>Act("undo_road",new JsonObject()),history>0||legacyUndo?"":"暂无可撤销操作");
        BuildText(tools,$"在场景中拖拽并松开左键提交，施工前会显示整笔费用。Esc 退出，{TownBindingText("undo")} 撤销。");

        var precise=BuildCard(workspace,"按格施工");
        var (x,y)=BuildCoordinates(precise);
        var quote=BuildText(precise,"");
        var actions=BuildOperations(precise);
        JsonObject RoadArgs()=>new(){{"cx",_buildX},{"cy",_buildY},{"style",Style()}};
        var pave=BuildButton(actions,"铺设 / 改铺",()=>Act("road",RoadArgs()));
        var remove=BuildButton(actions,"拆除这一格",()=>Act("remove_road",RoadArgs()),removal:true);
        void UpdateQuote()
        {
            _buildRoadStyle=selector.Selected;
            var cell=new NativeGame.Cell(_buildX,_buildY);
            var (cost,reason)=Game.RoadQuote(new[]{cell},Style());
            var (removalCost,_)=Game.RoadQuote(new[]{cell},Style(),true);
            bool hasRoad=Game.RoadCells().Contains(cell);
            if(hasRoad&&reason==""&&cost==0&&NativeGame.I(styles[selector.Selected].Value!,"cost")>0)reason="该格已是这种路面";
            string paveInfo=reason==""||reason=="金币不足"?BuildQuoteText($"铺路 {cost} 金币",reason):reason;
            quote.Text=hasRoad?$"{paveInfo}    拆除返还 {-removalCost} 金币":paveInfo;
            SetBuildAvailability(pave,reason);
            SetBuildAvailability(remove,hasRoad?"":"该格没有道路");
        }
        x.ValueChanged+=_=>UpdateQuote();
        y.ValueChanged+=_=>UpdateQuote();
        selector.ItemSelected+=_=>UpdateQuote();
        UpdateQuote();
    }

    void BuildTownWorkspace(Container workspace)
    {
        var project=BuildCard(workspace,"选择施工项目");
        var catalog=Game.Rules["CATALOG"]!.AsObject().ToArray();
        var categories=catalog.Select(item=>NativeGame.S(item.Value!,"category")).Distinct().ToArray();
        _buildCatalogCategory=Math.Clamp(_buildCatalogCategory,0,categories.Length);
        var filters=BuildOperations(project);
        var search=new LineEdit{Name="TownCatalogSearch",Text=_buildCatalogQuery,PlaceholderText="搜索建筑名称或用途",
            ClearButtonEnabled=true,CustomMinimumSize=new(165,38),SizeFlagsHorizontal=Control.SizeFlags.ExpandFill};
        filters.AddChild(search);
        var category=BuildChoice(filters,new[]{"全部用途"}.Concat(categories),_buildCatalogCategory);
        category.Name="TownCatalogCategory";
        var budget=new CheckButton{Name="TownCatalogBudget",Text="预算内",ButtonPressed=_buildCatalogBudgetOnly};
        filters.AddChild(budget);
        var reset=BuildButton(filters,"清除筛选");reset.Name="TownCatalogReset";
        var matches=BuildText(project,"");matches.Name="TownCatalogMatches";

        var fields=BuildOperations(project);
        var selector=BuildChoice(fields,Array.Empty<string>(),0);selector.Name="TownCatalogPicker";
        var facing=BuildChoice(fields,BuildOrientations,_buildOrientation);
        facing.SizeFlagsHorizontal=Control.SizeFlags.ShrinkBegin;
        var summary=BuildText(project,"");
        int[] visible=Array.Empty<int>();
        int quotedCoins=int.MinValue;
        bool HasSelection()=>selector.Selected>=0&&selector.Selected<visible.Length;
        JsonObject ProjectArgs()=>new(){{"catalog_key",catalog[visible[selector.Selected]].Key},{"orientation",facing.Selected}};
        var projectActions=BuildOperations(project);
        var scene=BuildButton(projectActions,"在场景中选址",()=>{if(HasSelection())ArmPlacement("build_town",ProjectArgs());});
        scene.Name="TownCatalogScene";
        var snap=new CheckButton{Name="TownPlacementRoadSnap",Text="正门吸附到路边",ButtonPressed=_snapPlacementToRoad,
            TooltipText="找不到正门相邻的连通道路时，尝试挪到一格以内的合法位置。"};
        project.AddChild(snap);
        snap.Toggled+=enabled=>_snapPlacementToRoad=enabled;

        var directory=BuildTownExisting(workspace);
        BuildButton(projectActions,"查看工程名册",()=>_=RevealManagementSection(directory));

        var precise=BuildCard(workspace,"按格选址");
        var (x,y)=BuildCoordinates(precise);
        var placement=BuildText(precise,"");
        var start=BuildButton(BuildOperations(precise),"在这一格开工",()=>{
            if(!HasSelection())return;
            var args=ProjectArgs();
            args["cx"]=_buildX;
            args["cy"]=_buildY;
            _buildTownId="";
            Act("build_town",args);
        });
        void UpdateProject()
        {
            _buildOrientation=facing.Selected;
            string empty="筛选后没有可用建筑";
            SetBuildAvailability(scene,HasSelection()?"":empty);
            if(!HasSelection()){
                summary.Text="请换个关键词或清除筛选。";
                placement.Text=empty;
                SetBuildAvailability(start,empty);
                return;
            }
            var item=catalog[visible[selector.Selected]];
            var spec=item.Value!;
            var size=spec["footprint"]!;
            int cost=NativeGame.I(spec,"cost");
            string priceReason=BuildAffordability(cost);
            summary.Text=BuildQuoteText($"{cost} 金币 · 占地 {size[facing.Selected%2]}×{size[1-facing.Selected%2]} 格 · 工期 {NativeGame.D(spec,"build_hours"):0.##} 小时",priceReason)
                +"\n"+NativeGame.S(spec,"description");
            string reason=Game.PlacementReason(item.Key,_buildX,_buildY,facing.Selected);
            if(reason=="")reason=priceReason;
            placement.Text=reason==""?"此处可开工。":reason;
            SetBuildAvailability(start,reason);
        }
        void FilterCatalog()
        {
            quotedCoins=NativeGame.I(Game.Meta,"coins");
            string term=_buildCatalogQuery.Trim();
            string usage=_buildCatalogCategory==0?"":categories[_buildCatalogCategory-1];
            var matching=Enumerable.Range(0,catalog.Length).Where(i=>{
                var spec=catalog[i].Value!;
                string kind=NativeGame.S(spec,"category");
                return (usage==""||usage==kind)&&(!_buildCatalogBudgetOnly||NativeGame.I(spec,"cost")<=quotedCoins)&&
                    (NativeGame.S(spec,"name").Contains(term,StringComparison.OrdinalIgnoreCase)||
                     kind.Contains(term,StringComparison.OrdinalIgnoreCase)||
                     NativeGame.S(spec,"description").Contains(term,StringComparison.OrdinalIgnoreCase));
            }).ToArray();
            if(!visible.SequenceEqual(matching)){
                visible=matching;
                selector.Clear();
                foreach(int i in visible){
                    var spec=catalog[i].Value!;
                    selector.AddItem($"{NativeGame.S(spec,"name")} · {NativeGame.S(spec,"category")} · {NativeGame.I(spec,"cost")} 金币");
                }
                selector.Select(visible.Length>0?Math.Max(0,Array.IndexOf(visible,_buildTownType)):-1);
            }
            selector.Disabled=visible.Length==0;
            if(visible.Length==0)selector.Text="无匹配项目";
            matches.Text=$"找到 {visible.Length} / {catalog.Length} 项 · 当前预算 {quotedCoins} 金币";
            reset.Disabled=term.Length==0&&_buildCatalogCategory==0&&!_buildCatalogBudgetOnly;
            UpdateProject();
        }
        x.ValueChanged+=_=>UpdateProject();
        y.ValueChanged+=_=>UpdateProject();
        selector.ItemSelected+=index=>{_buildTownType=visible[(int)index];UpdateProject();};
        facing.ItemSelected+=_=>UpdateProject();
        search.TextChanged+=text=>{_buildCatalogQuery=text;FilterCatalog();};
        category.ItemSelected+=index=>{_buildCatalogCategory=(int)index;FilterCatalog();};
        budget.Toggled+=on=>{_buildCatalogBudgetOnly=on;FilterCatalog();};
        reset.Pressed+=()=>{
            search.Text=_buildCatalogQuery="";
            _buildCatalogCategory=0;category.Select(0);
            _buildCatalogBudgetOnly=false;budget.SetPressedNoSignal(false);
            FilterCatalog();
        };
        void RefreshBudget(){if(quotedCoins!=NativeGame.I(Game.Meta,"coins"))FilterCatalog();}
        _refreshManagementPage+=RefreshBudget;
        project.TreeExiting+=()=>_refreshManagementPage-=RefreshBudget;
        FilterCatalog();

        BuildDistricts(workspace);
    }

    static bool IsTownProjectUnderway(JsonNode building)=>NativeGame.S(building,"status")=="building";
    string TownProjectName(JsonNode building)=>NativeGame.S(Game.Spec("CATALOG",NativeGame.S(building,"catalog_key")),"name");
    static string TownProjectCell(JsonNode building)=>$"({NativeGame.I(building,"cx")}, {NativeGame.I(building,"cy")})";
    static Vector3 TownProjectCenter(JsonNode building)=>new(
        (NativeGame.I(building,"cx")+NativeGame.I(building,"w")*.5f)*WorldData.GridSize,0,
        (NativeGame.I(building,"cy")+NativeGame.I(building,"h")*.5f)*WorldData.GridSize);

    string TownProjectClock(double time)
    {
        int day=1+(int)Math.Max(0,(time-NativeGame.D(Game.Meta,"start_ts"))/NativeGame.Day);
        int hour=(int)(time%NativeGame.Day/NativeGame.Hour);
        int minute=(int)(time%NativeGame.Hour/60);
        return $"第 {day} 天 {hour:00}:{minute:00}";
    }

    static string ConstructionTimeLeft(double seconds)
    {
        int minutes=(int)Math.Ceiling(Math.Max(0,seconds)/60);
        if(minutes==0)return "正在完工";
        int hours=minutes/60;
        return hours==0?$"剩余 {minutes} 分钟":$"剩余 {hours} 小时 {minutes%60} 分钟";
    }

    void OpenTownProject(string id)
    {
        _buildWorkspace=2;
        ShowManagement("建造",id);
    }

    void LocateTownProject(string id)
    {
        var building=Game.FindTownBuilding(id);
        if(building==null){
            SetManagementFeedback("该城镇建筑已移除。",true);
            return;
        }
        RememberCameraSite(TownProjectCenter(building));
        StopPetFollow();
        EndPlacement();
        _buildTownId=id;
        FocusSceneInspection("town_building",id);
        _center=TownProjectCenter(building);
        _pitch=.72f;
        int size=Math.Max(NativeGame.I(building,"w"),NativeGame.I(building,"h"));
        _distance=Math.Clamp(26+size*6,36,78);
        _selection=$"{TownProjectName(building)} · {(IsTownProjectUnderway(building)?ConstructionTimeLeft(NativeGame.D(building,"finish_ts")-Game.Now):"已完工")}";
        _management?.Hide();
    }

    void BuildDistricts(Container workspace)
    {
        var card=BuildCard(workspace,"街区发展");
        if(_managementEntryTarget=="district")_managementFocusTarget=card.GetParent<Control>();
        var zones=Data.Root["zones"]!.AsObject().Where(zone=>zone.Key!="estate").ToArray();
        var selector=BuildChoice(card,zones.Select(zone=>NativeGame.S(zone.Value!,"name")),_buildDistrict);
        var quote=BuildText(card,"");
        var upgrade=BuildButton(BuildOperations(card),"开放街区",()=>Act("open_zone",new JsonObject{{"zone",zones[selector.Selected].Key}}));
        void UpdateZone()
        {
            _buildDistrict=selector.Selected;
            string key=zones[selector.Selected].Key;
            int level=Game.ZoneLevel(key),cost=Game.DistrictUpgradePrice(key);
            string reason=level>=3?"已达最高等级":BuildAffordability(cost);
            upgrade.Text=level==0?"开放街区":"升级街区";
            quote.Text=level>=3?reason:BuildQuoteText($"{(level==0?"未开放":$"当前 {level} 级")} · {upgrade.Text}需 {cost} 金币",reason);
            SetBuildAvailability(upgrade,reason);
        }
        selector.ItemSelected+=_=>UpdateZone();
        UpdateZone();
    }

}
