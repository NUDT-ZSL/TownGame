using Godot;
using System;
using System.Linq;
using System.Text.Json.Nodes;

public partial class Main
{
    const int TownProjectPageSize=8;
    enum TownProjectFilter { All, Active, Complete, Isolated }
    enum TownProjectOrder { Soonest, Newest, Name }
    string _townProjectQuery="";
    TownProjectFilter _townProjectFilter;
    TownProjectOrder _townProjectOrder;
    int _townProjectPage;

    sealed record TownProjectEntry(JsonNode Building,string Id,string Name,string District,NativeGame.ConstructionAccess Access)
    {
        internal bool Underway=>IsTownProjectUnderway(Building);
        internal double Finish=>NativeGame.D(Building,"finish_ts");
        internal double Placed=>NativeGame.D(Building,"placed_ts");
    }

    Control BuildTownExisting(Container workspace)
    {
        var card=BuildCard(workspace,"工程名册");
        card.GetParent().Name="TownProjectDirectory";
        var heading=card.GetChild<Control>(0);
        var totals=BuildText(card,"");
        totals.Name="TownProjectTotals";
        totals.AddThemeColorOverride("font_color",TownTheme.Muted);
        var queueRow=BuildOperations(card);
        var next=BuildText(queueRow,"");
        next.Name="TownProjectNextCompletion";
        next.CustomMinimumSize=new(230,0);
        next.SizeFlagsHorizontal=Control.SizeFlags.ExpandFill;
        var jump=BuildButton(queueRow,"查看即将完工项目");
        jump.Name="TownProjectJumpNext";

        var tools=BuildOperations(card);
        var search=new LineEdit{Name="TownProjectQuery",Text=_townProjectQuery,PlaceholderText="搜索名称、街区或坐标",
            ClearButtonEnabled=true,CustomMinimumSize=new(180,38),SizeFlagsHorizontal=Control.SizeFlags.ExpandFill};
        tools.AddChild(search);
        var filter=BuildChoice(tools,new[]{"全部状态","施工中","已完工","待接路"},(int)_townProjectFilter);
        filter.Name="TownProjectFilter";
        var sort=BuildChoice(tools,new[]{"优先即将完工","最近开工","建筑名称"},(int)_townProjectOrder);
        sort.Name="TownProjectSort";
        var reset=BuildButton(tools,"重置筛选");
        reset.Name="TownProjectReset";
        var picker=BuildChoice(card,Array.Empty<string>(),0);
        picker.Name="TownProjectPicker";
        var navigation=BuildOperations(card);
        var previous=BuildButton(navigation,"上一页");
        previous.Name="TownProjectPrevious";
        var result=BuildText(navigation,"");
        result.Name="TownProjectResults";
        result.AutowrapMode=TextServer.AutowrapMode.Off;
        result.VerticalAlignment=VerticalAlignment.Center;
        var following=BuildButton(navigation,"下一页");
        following.Name="TownProjectFollowing";

        var details=MarketCard(card,color:"fff9ed");
        var detailsPanel=details.GetParent<Control>();
        detailsPanel.Name="TownProjectDetails";
        var name=MarketLabel(details,"",18,TownTheme.Accent("建造"),wrap:true);
        name.Name="TownProjectDetailName";
        var status=BuildText(details,"");status.Name="ConstructionStatus";
        var progress=new ProgressBar{Name="ConstructionProgress",MaxValue=100,Step=0,ShowPercentage=false,CustomMinimumSize=new(0,10)};
        progress.AddThemeStyleboxOverride("background",TownTheme.Box("e0e7df",5,0));
        progress.AddThemeStyleboxOverride("fill",TownTheme.Box("5b9b8c",5,0));
        details.AddChild(progress);
        var timing=BuildText(details,"");timing.Name="ConstructionTiming";
        var road=BuildText(details,"");road.Name="ConstructionRoadStatus";
        var actions=BuildOperations(details);
        var locate=BuildButton(actions,"定位到现场");locate.Name="LocateConstruction";
        var footprint=BuildButton(actions,"地图查看地块");footprint.Name="TownProjectFootprintMap";
        var entrance=BuildButton(actions,"地图查看临街格");entrance.Name="TownProjectEntranceMap";
        var pin=BuildButton(actions,"固定到场景");

        var removalPanel=MarketCard(details,color:"f5ebe1");
        var disclosure=new CheckButton{Name="TownProjectRemovalToggle",Text="拆除与影响"};
        removalPanel.AddChild(disclosure);
        var demolition=new VBoxContainer{Visible=false};
        removalPanel.AddChild(demolition);
        var quote=BuildText(demolition,"");quote.Name="TownProjectDemolitionQuote";
        var remove=BuildButton(BuildOperations(demolition),"核对并拆除",removal:true);
        remove.Name="TownProjectRemove";

        TownProjectEntry[] projects=Array.Empty<TownProjectEntry>(), display=Array.Empty<TownProjectEntry>();
        TownProjectEntry? selected=null, nextProject=null;
        JsonObject? snapshot=null;
        long revision=-1;
        string target=_managementEntryTarget;
        var updateFinish=AddBuildingFinishControls(workspace,()=>selected?.Building,true);

        search.TextChanged+=text=>{_townProjectQuery=text;UpdateDirectory();};
        filter.ItemSelected+=index=>{_townProjectFilter=(TownProjectFilter)index;UpdateDirectory();};
        sort.ItemSelected+=index=>{_townProjectOrder=(TownProjectOrder)index;UpdateDirectory();};
        reset.Pressed+=()=>{ClearFilters();UpdateDirectory();};
        picker.ItemSelected+=index=>SelectProject(display[(int)index]);
        previous.Pressed+=()=>{_townProjectPage--;UpdateDirectory(keepSelection:false);};
        following.Pressed+=()=>{_townProjectPage++;UpdateDirectory(keepSelection:false);};
        jump.Pressed+=()=>{if(nextProject!=null)UpdateDirectory(nextProject.Id);};
        locate.Pressed+=()=>{if(selected!=null)LocateTownProject(selected.Id);};
        footprint.Pressed+=()=>{
            if(selected is not { } project)return;
            var building=project.Building;
            ShowProjectMapCell(new(NativeGame.I(building,"cx"),NativeGame.I(building,"cy")));
        };
        entrance.Pressed+=()=>{
            if(selected is not { } project||project.Access.Road is not NativeGame.Cell cell)return;
            ShowProjectMapCell(cell,project.Access.Connected==0);
        };
        pin.Pressed+=()=>{
            if(selected is not { } project)return;
            FocusSceneInspection("town_building",project.Id,true);
            LocateTownProject(project.Id);
        };
        disclosure.Toggled+=opened=>{demolition.Visible=opened;UpdateRemoval();};
        remove.Pressed+=ConfirmRemoval;

        if(target=="projects"||Game.FindTownBuilding(target)!=null)_managementFocusTarget=heading;
        Refresh();
        _refreshManagementPage+=Refresh;
        card.TreeExiting+=()=>_refreshManagementPage-=Refresh;
        return heading;

        void Refresh()
        {
            if(revision!=Game.WorldRevision||!ReferenceEquals(snapshot,Data.Snapshot)){
                snapshot=Data.Snapshot;
                revision=Game.WorldRevision;
                projects=Game.Rows("town_buildings").OfType<JsonNode>().Select(building=>new TownProjectEntry(
                    building,NativeGame.S(building,"building_id"),TownProjectName(building),
                    ProjectDistrictCaption(Game.ConstructionDistrict(building)),Game.ConstructionRoadAccess(building))).ToArray();
                var underway=projects.Where(project=>project.Underway).OrderBy(project=>project.Finish).ToArray();
                nextProject=underway.FirstOrDefault();
                totals.Text=$"施工中 {underway.Length} 项 · 已完工 {projects.Length-underway.Length} 栋 · 待接路 {projects.Count(project=>project.Access.Connected==0)} 处";
                UpdateDirectory(target);
                target="";
            }else UpdateDetails();
            next.Text=nextProject is { } pending?$"下一项完工 · {pending.Name} · {ConstructionTimeLeft(pending.Finish-Game.Now)}":"暂无进行中的施工";
            jump.Visible=nextProject!=null;
        }

        void UpdateDirectory(string requested="",bool keepSelection=true)
        {
            var found=ProjectMatches(projects);
            if(requested.Length>0&&projects.Any(project=>project.Id==requested)&&!found.Any(project=>project.Id==requested)){
                ClearFilters();
                found=ProjectMatches(projects);
            }
            int position=keepSelection?Array.FindIndex(found,project=>project.Id==(requested.Length>0&&requested!="projects"?requested:_buildTownId)):-1;
            if(position>=0)_townProjectPage=position/TownProjectPageSize;
            int pages=(found.Length+TownProjectPageSize-1)/TownProjectPageSize;
            _townProjectPage=Math.Clamp(_townProjectPage,0,Math.Max(0,pages-1));
            display=found.Skip(_townProjectPage*TownProjectPageSize).Take(TownProjectPageSize).ToArray();
            picker.Clear();
            foreach(var project in display)
                picker.AddItem($"{project.Name} {TownProjectCell(project.Building)} · {project.District} · {(project.Underway?"施工中":"已完工")}");
            int index=display.Length>0?Math.Max(0,position-_townProjectPage*TownProjectPageSize):-1;
            picker.Visible=display.Length>0;
            picker.Select(index);
            previous.Disabled=_townProjectPage<=0;
            following.Disabled=_townProjectPage>=pages-1;
            result.Text=found.Length==0?(projects.Length==0?"暂无城镇工程":"没有匹配的工程"):
                $"{found.Length} / {projects.Length} 项 · 第 {_townProjectPage+1} / {pages} 页";
            reset.Disabled=_townProjectQuery.Length==0&&_townProjectFilter==TownProjectFilter.All;
            SelectProject(index>=0?display[index]:null);
        }

        void ClearFilters()
        {
            search.Text=_townProjectQuery="";
            _townProjectFilter=TownProjectFilter.All;
            filter.Select(0);
        }

        void SelectProject(TownProjectEntry? project)
        {
            if(project?.Id!=selected?.Id)disclosure.ButtonPressed=false;
            selected=project;
            if(project!=null)_buildTownId=project.Id;
            UpdateDetails();
        }

        void UpdateDetails()
        {
            detailsPanel.Visible=selected!=null;
            updateFinish();
            if(selected is not { } project)return;
            var building=project.Building;
            name.Text=$"{project.Name} {TownProjectCell(building)} · {project.District}";
            progress.Value=Game.ConstructionProgress(building)*100;
            status.Text=project.Underway?$"施工中 · {(int)progress.Value}%":"已完工 · 100%";
            timing.Text=project.Underway?$"{ConstructionTimeLeft(project.Finish-Game.Now)} · 预计 {TownProjectClock(project.Finish)} 完工":
                project.Finish>0?$"{TownProjectClock(project.Finish)} 完工":"已完成施工";
            var access=project.Access;
            road.Text=access.Connected>0?"接路 · 已接入庄园路网":access.Adjacent>0?"接路 · 有临街道路，尚未连通庄园":"接路 · 缺少临街道路";
            if(access.Road is NativeGame.Cell cell){
                road.Text+=$"\n{(access.OnDoorSide?"正门侧道路":"其他临街道路")} ({cell.X}, {cell.Y}) · 连通 {access.Connected}/{access.Adjacent} 格";
            }
            entrance.Visible=access.Road.HasValue;
            entrance.Text=access.Connected==0?"查看断路修复方案":"地图查看临街格";
            UpdateRemoval();
        }

        void UpdateRemoval()
        {
            if(!demolition.Visible||selected is not { } project)return;
            var current=ProjectRemovalQuote(project.Building);
            quote.Text=(current.Reason.Length>0?current.Reason:$"预计返还 {current.Refund} 金币")+"\n影响 · "+current.Impact;
            SetBuildAvailability(remove,current.Reason);
        }

        void ConfirmRemoval()
        {
            if(selected is not { } project||Game.FindTownBuilding(project.Id) is not JsonNode building)return;
            var shown=ProjectRemovalQuote(building);
            if(shown.Reason.Length>0){UpdateRemoval();return;}
            ConfirmManagementAction("拆除城镇建筑",$"{project.Name} {TownProjectCell(building)} · 返还 {shown.Refund} 金币\n{shown.Impact}","确认拆除",()=>{
                if(Game.FindTownBuilding(project.Id) is not JsonNode current||ProjectRemovalQuote(current)!=shown){
                    Refresh();
                    SetManagementFeedback("工程状态或拆除报价已变化，请重新核对。",true);
                    return;
                }
                Act("remove_town",new JsonObject{{"building_id",project.Id}});
            });
        }
    }

    TownProjectEntry[] ProjectMatches(TownProjectEntry[] projects)
    {
        string[] words=_townProjectQuery.Split((char[]?)null,StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries);
        var filtered=projects.Where(project=>{
            if(_townProjectFilter==TownProjectFilter.Active&&!project.Underway||_townProjectFilter==TownProjectFilter.Complete&&project.Underway||
                _townProjectFilter==TownProjectFilter.Isolated&&project.Access.Connected>0)return false;
            string coordinates=$"{NativeGame.I(project.Building,"cx")},{NativeGame.I(project.Building,"cy")}";
            string searchable=$"{project.Name} {project.District} {coordinates} {TownProjectCell(project.Building)} {project.Id}";
            return words.All(word=>searchable.Contains(word,StringComparison.OrdinalIgnoreCase));
        });
        var sorted=_townProjectOrder switch{
            TownProjectOrder.Newest=>filtered.OrderByDescending(project=>project.Placed),
            TownProjectOrder.Name=>filtered.OrderBy(project=>project.Name,StringComparer.CurrentCulture).ThenByDescending(project=>project.Placed),
            _=>filtered.OrderByDescending(project=>project.Underway).ThenBy(project=>project.Underway?project.Finish:-project.Finish)
        };
        return sorted.ThenBy(project=>project.Id,StringComparer.Ordinal).ToArray();
    }

    string ProjectDistrictCaption(string zone)=>Data.Root["zones"]?[zone] is JsonNode district?NativeGame.S(district,"name"):"街区外围";

    void ShowProjectMapCell(NativeGame.Cell cell,bool repair=false)=>
        ShowManagement("地图",FormattableString.Invariant($"{(repair?"repair":"cell")}:{cell.X},{cell.Y}"));

    (string Status,int Refund,string Reason,string Impact) ProjectRemovalQuote(JsonNode building)
    {
        var (refund,reason)=Game.TownDemolitionQuote(building);
        return (NativeGame.S(building,"status"),refund,reason,Game.TownDemolitionImpact(building));
    }
}
