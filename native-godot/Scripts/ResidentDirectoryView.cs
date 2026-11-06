using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;

public partial class Main
{
    enum ResidentPlace { Outdoors, Freight, Home, Unavailable }
    readonly record struct ResidentDirectoryEntry(string Id,string Name,string Profession,string Role,string Activity,
        string District,ResidentPlace Place,Vector3? Position,JsonNode? Home);
    readonly record struct ResidentDirectoryRow(Label Name,Label Role,Label Place,Label Activity,Label Route,
        Button Favorite,Button Locate,Button Follow,Button Family);
    const int ResidentDirectoryPageSize=6;
    const int ResidentFavoritesFilter=5;
    static readonly string[] ResidentFilterNames={"全部村民","在户外","在家","驾驶货车","暂不可定位","已收藏"};
    bool _residentDirectoryOpen;
    string _residentDirectoryQuery="";
    int _residentDirectoryFilter,_residentDirectoryPage;

    static bool IsDirectoryPerson(JsonNode person)=>NativeGame.S(person,"role") is "居民" or "通勤者";
    JsonNode? DirectoryPerson(string id)=>Data.People.OfType<JsonNode>().FirstOrDefault(person=>
        IsDirectoryPerson(person)&&NativeGame.S(person,"person_id")==id);
    RoadAgent? DirectoryActor(string id)=>Agents.FirstOrDefault(agent=>!agent.IsCar&&agent.Ident==id&&!agent.IsQueuedForDeletion());

    void OpenResidentDirectoryEntry(string id)
    {
        if(DirectoryPerson(id) is not JsonNode person)return;
        ShowManagement("家庭","residents:"+id,()=>{
            _residentDirectoryQuery=NativeGame.S(person,"name");
            _residentDirectoryFilter=_residentDirectoryPage=0;
        });
    }

    ResidentDirectoryEntry DescribeDirectoryPerson(JsonNode person,RoadAgent? actor)
    {
        string id=NativeGame.S(person,"person_id"),name=NativeGame.S(person,"name"),role=NativeGame.S(person,"role");
        string profession=NativeGame.S(person,"profession"),zone=NativeGame.S(person,"destination");
        bool inside=actor?.InsideHome??NativeGame.B(person,"at_home");
        JsonNode? home=Game.ResidentHome(person);
        var place=ResidentPlace.Unavailable;
        Vector3? position=null;
        string activity=actor?.ResidentialActivity??"暂不在场";

        if(NativeGame.S(person,"status")=="离镇")activity="已离镇";
        else if(FreightAboard(id)){
            if(_freightTruck is { } truck&&!truck.IsQueuedForDeletion()){
                place=ResidentPlace.Freight;
                position=truck.Position;
                activity=FreightActivity(id);
            }else activity="正在货运，等待货车位置更新";
        }else if(inside){
            string actualHome=actor?.InsideHomeBuildingId??"";
            if(actualHome.Length==0)actualHome=NativeGame.S(person,"inside_home_building_id");
            if(actualHome.Length>0&&Game.FindTownBuilding(actualHome) is JsonNode building&&Game.IsResidentialHome(building))home=building;
            if(home!=null){
                place=ResidentPlace.Home;
                position=TownProjectCenter(home);
            }
            activity=actor?.ResidentialActivity??"在家休息";
            zone=NativeGame.S(person,"home_zone","homes");
        }else if(actor is { } visible&&IsVisibleSceneResident(visible)){
            place=ResidentPlace.Outdoors;
            position=visible.Position;
        }else if(role=="通勤者"&&Game.IsResidentNight)activity="已下班离镇";

        string district=zone.Length==0?"":AutonomyZoneName(zone);
        return new(id,name,profession.Length==0?"暂无职业":profession,role,activity,district,place,position,home);
    }

    ResidentDirectoryEntry[] ResidentDirectoryEntries()
    {
        var actors=Agents.Where(actor=>!actor.IsCar&&!actor.IsQueuedForDeletion()).ToDictionary(actor=>actor.Ident);
        return Data.People.OfType<JsonNode>().Where(IsDirectoryPerson)
            .Select(person=>DescribeDirectoryPerson(person,actors.GetValueOrDefault(NativeGame.S(person,"person_id"))))
            .OrderBy(entry=>entry.Place==ResidentPlace.Unavailable).ThenBy(entry=>entry.Role!="居民")
            .ThenBy(entry=>entry.Name,StringComparer.Ordinal).ToArray();
    }

    static bool ResidentMatchesQuery(ResidentDirectoryEntry entry,string query)=>query.Length==0||query=="村民"||
        entry.Name.Contains(query,StringComparison.OrdinalIgnoreCase)||entry.Profession.Contains(query,StringComparison.OrdinalIgnoreCase)||
        entry.Role.Contains(query,StringComparison.OrdinalIgnoreCase)||entry.District.Contains(query,StringComparison.OrdinalIgnoreCase)||
        entry.Id.Equals(query,StringComparison.OrdinalIgnoreCase);

    bool LocateResident(string id)
    {
        var person=DirectoryPerson(id);
        if(person==null){
            _selection="这位村民已不在名册中。";
            SetManagementFeedback(_selection,true);
            return false;
        }
        var actor=DirectoryActor(id);
        var entry=DescribeDirectoryPerson(person,actor);
        if(entry.Position is not Vector3 position){
            string notice=$"无法定位 {entry.Name} · {entry.Activity}";
            SetManagementFeedback(notice,true);
            _selection=notice;
            return false;
        }

        if(entry.Place==ResidentPlace.Home&&entry.Home!=null)
            _selectedResidence=NativeGame.S(entry.Home,"building_id");
        string description=$"{entry.Name} · {ResidentLocateCaption(entry.Place)}\n{ResidentCurrentSite(entry)} · {entry.Activity}\n下一站 · {ResidentNextStop(person,actor,entry)}";
        FocusResidentSite(position,entry.Place==ResidentPlace.Home?48:entry.Place==ResidentPlace.Freight?31:21,description);
        _residentLocatedText=description;
        MarkLocatedResident(id);
        FocusSceneInspection("person",id);
        RefreshScenePetCare();
        return true;
    }

    void FocusResidentSite(Vector3 position,float distance,string description)
    {
        StopResidentFollow();
        var start=_shownCamera;
        RememberCameraSite(position);
        CancelTownPointer();
        StopPetFollow();
        EndPlacement();
        _selectedPet="";
        _portraitAnchor=null;
        _buildCamera=false;
        _center=new(position.X,0,position.Z);
        _pitch=.7f;
        _distance=distance;
        _cameraFrom=start;
        _cameraTo=DesiredCameraFrame();
        _cameraTransition=CameraTransitionTime;
        _selection=description;
        _management?.Hide();
        RefreshScenePetCare();
        UpdatePetMarker(false);
    }

    bool AddFamilyViewChoice()
    {
        if(_managementEntryTarget=="residents"||_managementEntryTarget.StartsWith("residents:",StringComparison.Ordinal))_residentDirectoryOpen=true;
        if(_managementEntryTarget=="residence"||_managementEntryTarget.StartsWith("residence:",StringComparison.Ordinal)||
            _managementEntryTarget.StartsWith("interior:",StringComparison.Ordinal))_residentDirectoryOpen=false;
        var tabs=new HBoxContainer();
        tabs.AddThemeConstantOverride("separation",8);
        _managementBody!.AddChild(tabs);
        if(_managementEntryTarget=="residence")_managementFocusTarget=tabs;
        foreach(var (caption,target,selected) in new[]{("村民名册","residents",_residentDirectoryOpen),
            ("家庭住宅","residence",!_residentDirectoryOpen)}){
            var button=MarketButton(tabs,caption,()=>ShowManagement("家庭",target),accent:"家庭");
            button.ToggleMode=true;
            button.SetPressedNoSignal(selected);
        }
        return _residentDirectoryOpen;
    }

    void ResidentDirectoryControls()
    {
        LoadResidentFavorites();
        var body=_managementBody!;
        var overview=MarketCard(body,color:"e8eee3");
        var heading=MarketLabel(overview,"村民名册",19,TownTheme.Leaf);
        MarketLabel(overview,"按姓名、职业或街区查找，或按村民当前所在位置筛选。",color:TownTheme.Muted,wrap:true);
        var filters=new HFlowContainer();
        filters.AddThemeConstantOverride("h_separation",8);
        overview.AddChild(filters);
        var search=new LineEdit{Name="ResidentDirectoryQuery",Text=_residentDirectoryQuery,PlaceholderText="搜索姓名、职业或街区",
            ClearButtonEnabled=true,SizeFlagsHorizontal=Control.SizeFlags.ExpandFill,CustomMinimumSize=new(175,34),
            AccessibilityName="搜索村民"};
        filters.AddChild(search);
        var filter=MarketOptions(filters,ResidentFilterNames,_residentDirectoryFilter);
        filter.Name="ResidentDirectoryFilter";
        filter.AccessibilityName="村民位置与收藏筛选";
        var pager=new HFlowContainer();
        pager.AddThemeConstantOverride("h_separation",8);
        overview.AddChild(pager);
        var count=MarketLabel(pager,"",14,TownTheme.Muted);
        count.Name="ResidentDirectoryCount";
        count.VerticalAlignment=VerticalAlignment.Center;
        count.SizeFlagsHorizontal=Control.SizeFlags.ExpandFill;
        var previous=new Button{Text="上一页",CustomMinimumSize=new(0,38)};
        var next=new Button{Text="下一页",CustomMinimumSize=new(0,38)};
        foreach(var button in new[]{previous,next}){
            TownTheme.AccentButton(button,"家庭");
            pager.AddChild(button);
        }
        var resume=MarketButton(pager,"继续跟随",()=>StartResidentFollow(_lastFollowedResidentId),expand:false,accent:"地图");
        resume.Name="ResidentDirectoryResume";
        var empty=MarketCard(body,color:"f2ebdf");
        var emptyMessage=MarketLabel(empty,"",color:TownTheme.Muted,wrap:true);
        var rows=new VBoxContainer{Name="ResidentDirectoryRows"};
        rows.AddThemeConstantOverride("separation",6);
        body.AddChild(rows);
        var controls=new Dictionary<string,ResidentDirectoryRow>();
        string exactId=_managementEntryTarget.StartsWith("residents:",StringComparison.Ordinal)?_managementEntryTarget[10..]:"";
        string[] currentIds=Array.Empty<string>();
        ResidentDirectoryEntry[] visible=Array.Empty<ResidentDirectoryEntry>();
        var reset=MarketButton(empty,"清除搜索与筛选",Reset,expand:false,accent:"家庭");
        reset.Name="ResidentDirectoryReset";

        previous.Pressed+=()=>ChangePage(-1);
        next.Pressed+=()=>ChangePage(1);
        search.TextChanged+=value=>{exactId="";_residentDirectoryQuery=value;_residentDirectoryPage=0;Refresh();};
        search.TextSubmitted+=_=>{
            if(visible.FirstOrDefault(entry=>entry.Position.HasValue) is { Id:{Length:>0} } match)LocateResident(match.Id);
        };
        filter.ItemSelected+=index=>{_residentDirectoryFilter=(int)index;_residentDirectoryPage=0;Refresh();};
        if(_managementEntryTarget=="residents"||exactId.Length>0){
            _managementFocusTarget=heading;
            Callable.From(()=>{
                if(GodotObject.IsInstanceValid(search)&&search.IsVisibleInTree())search.GrabFocus();
            }).CallDeferred();
        }
        Refresh();
        _refreshManagementPage+=Refresh;

        void Reset()
        {
            exactId="";
            _residentDirectoryQuery=search.Text="";
            _residentDirectoryFilter=_residentDirectoryPage=0;
            filter.Select(0);
            Refresh();
            search.GrabFocus();
        }

        void ChangePage(int direction){_residentDirectoryPage+=direction;Refresh();}
        void Refresh()
        {
            string query=_residentDirectoryQuery.Trim();
            var entries=ResidentDirectoryEntries();
            var queried=entries.Where(entry=>exactId.Length>0?entry.Id==exactId:ResidentMatchesQuery(entry,query)).ToArray();
            for(int i=0;i<ResidentFilterNames.Length;i++){
                int found=queried.Count(entry=>MatchesFilter(entry,i));
                string caption=$"{ResidentFilterNames[i]} · {found}";
                if(filter.GetItemText(i)!=caption)filter.SetItemText(i,caption);
            }
            var matches=queried.Where(entry=>MatchesFilter(entry,_residentDirectoryFilter)).ToArray();
            int pageCount=Math.Max(1,(matches.Length+ResidentDirectoryPageSize-1)/ResidentDirectoryPageSize);
            _residentDirectoryPage=Math.Clamp(_residentDirectoryPage,0,pageCount-1);
            count.Text=$"{matches.Length} / {entries.Length} 位 · 第 {_residentDirectoryPage+1} / {pageCount} 页";
            previous.Disabled=_residentDirectoryPage==0;
            next.Disabled=_residentDirectoryPage>=pageCount-1;
            empty.GetParent<Control>().Visible=matches.Length==0;
            emptyMessage.Text=_residentDirectoryFilter==ResidentFavoritesFilter&&query.Length==0
                ?"没有已收藏的村民。返回全部村民后，点名字旁的星标即可收藏。"
                :"没有村民符合当前搜索与筛选。";
            var last=entries.FirstOrDefault(entry=>entry.Id==_lastFollowedResidentId);
            resume.Visible=last.Id is {Length:>0};
            if(resume.Visible){
                resume.Text=$"继续跟随 {last.Name}";
                resume.Disabled=!last.Position.HasValue;
                resume.TooltipText=resume.Disabled?last.Activity:"恢复上一位村民的镜头跟随";
            }
            visible=matches.Skip(_residentDirectoryPage*ResidentDirectoryPageSize).Take(ResidentDirectoryPageSize).ToArray();
            var ids=visible.Select(entry=>entry.Id).ToArray();
            if(!ids.SequenceEqual(currentIds)){
                foreach(var row in rows.GetChildren()){rows.RemoveChild(row);row.QueueFree();}
                controls.Clear();
                foreach(string id in ids)controls[id]=AddRow(id);
                currentIds=ids;
            }
            foreach(var entry in visible){
                var row=controls[entry.Id];
                row.Name.Text=entry.Name;
                row.Role.Text=$"{entry.Role} · {entry.Profession}";
                string site=ResidentCurrentSite(entry);
                row.Place.Text=entry.Position.HasValue?$"{ResidentPlaceCaption(entry.Place)} · {site}":ResidentPlaceCaption(entry.Place);
                row.Activity.Text=$"活动 · {entry.Activity}";
                row.Route.Text=$"下一站 · {ResidentDirectoryNextStop(entry)}";
                row.Locate.Text=entry.Place switch{ResidentPlace.Home=>"定位住宅",ResidentPlace.Freight=>"定位货车",_=>"定位村民"};
                row.Locate.Disabled=!entry.Position.HasValue;
                row.Locate.TooltipText=row.Locate.Disabled?entry.Activity:$"前往 {site}";
                row.Follow.Disabled=row.Locate.Disabled;
                row.Follow.TooltipText=row.Locate.Disabled?entry.Activity:"持续跟随这位村民的当前场景位置";
                row.Family.Visible=entry.Home!=null;
                bool favorite=_residentFavorites.Contains(entry.Id);
                row.Favorite.SetPressedNoSignal(favorite);
                row.Favorite.Text=favorite?"★":"☆";
                row.Favorite.AccessibilityName=row.Favorite.TooltipText=favorite?$"取消收藏 {entry.Name}":$"收藏 {entry.Name}";
            }
        }

        bool MatchesFilter(ResidentDirectoryEntry entry,int index)=>index switch{
            1=>entry.Place==ResidentPlace.Outdoors,
            2=>entry.Place==ResidentPlace.Home,
            3=>entry.Place==ResidentPlace.Freight,
            4=>entry.Place==ResidentPlace.Unavailable,
            ResidentFavoritesFilter=>_residentFavorites.Contains(entry.Id),
            _=>true
        };

        ResidentDirectoryRow AddRow(string id)
        {
            var card=MarketCard(rows,color:"f1ebe4");
            card.GetParent<Control>().Name="ResidentDirectory_"+id;
            var header=new HBoxContainer();
            card.AddChild(header);
            var text=new VBoxContainer{SizeFlagsHorizontal=Control.SizeFlags.ExpandFill};
            header.AddChild(text);
            var name=MarketLabel(text,"",18,TownTheme.Leaf,wrap:true);
            var role=MarketLabel(text,"",14,TownTheme.Muted,wrap:true);
            var favorite=MarketButton(header,"☆",()=>{
                ToggleResidentFavorite(id);
                Refresh();
                if(_residentDirectoryFilter==ResidentFavoritesFilter)filter.GrabFocus();
            },expand:false,accent:"商店");
            favorite.Name="ResidentFavorite";
            favorite.ToggleMode=true;
            favorite.SizeFlagsVertical=Control.SizeFlags.ShrinkBegin;
            var place=MarketLabel(card,"",15,TownTheme.Accent("家庭").Darkened(.3f),wrap:true);
            var tracking=new HBoxContainer();
            tracking.AddThemeConstantOverride("separation",8);
            card.AddChild(tracking);
            var activity=MarketLabel(tracking,"",14,TownTheme.Muted,wrap:true);
            var route=MarketLabel(tracking,"",14,TownTheme.Ink,wrap:true);
            activity.SizeFlagsHorizontal=route.SizeFlagsHorizontal=Control.SizeFlags.ExpandFill;
            place.Name="ResidentCurrentPlace";
            route.Name="ResidentNextStop";
            var actions=new HFlowContainer();
            actions.AddThemeConstantOverride("h_separation",6);
            card.AddChild(actions);
            var locate=MarketButton(actions,"定位村民",()=>LocateResident(id),expand:false,accent:"地图");
            locate.Name="LocateDirectoryResident";
            var follow=MarketButton(actions,"跟随",()=>StartResidentFollow(id),expand:false,accent:"地图");
            follow.Name="FollowDirectoryResident";
            var family=MarketButton(actions,"查看家庭",()=>{
                if(DirectoryPerson(id) is JsonNode person&&Game.ResidentHome(person) is JsonNode home)SelectResidence(NativeGame.S(home,"building_id"));
            },expand:false,accent:"家庭");
            return new(name,role,place,activity,route,favorite,locate,follow,family);
        }
    }

    void VerifyResidentDirectory(Action<bool,string> check)
    {
        var person=Data.People.OfType<JsonNode>().First(row=>NativeGame.S(row,"role")=="居民"&&Game.ResidentHome(row)!=null&&
            Agents.Any(agent=>!agent.IsCar&&agent.Ident==NativeGame.S(row,"person_id")));
        string id=NativeGame.S(person,"person_id"),name=NativeGame.S(person,"name");
        var actor=Agents.First(agent=>!agent.IsCar&&agent.Ident==id);
        var home=Game.ResidentHome(person)!;
        var actorState=(actor.InsideHome,actor.InsideHomeBuildingId,actor.Visual.Visible,actor.ResidentialActivity,actor.Position);
        string previouslyLocated=_selectedSceneResident;
        var viewState=(_center,_pitch,_distance,_cameraFrom,_cameraTo,_cameraTransition,_portraitAnchor,_buildCamera,_selectedPet,
            _selectedResidence,_previousCameraSite,_selection);
        try{
            actor.InsideHome=false;
            actor.Visual.Show();
            var toolbarButton=(Button)_townToolbar!.FindChild("TownFindResidents",true,false)!;
            toolbarButton.EmitSignal(BaseButton.SignalName.Pressed);
            var search=(LineEdit)_managementBody!.FindChild("ResidentDirectoryQuery",true,false)!;
            search.Text=id;
            search.EmitSignal(LineEdit.SignalName.TextChanged,id);
            var result=_managementBody.FindChild("ResidentDirectory_"+id,true,false);
            check(result!=null&&_managementFocusTarget!=null,"村民名册未定位到搜索结果");
            var locate=result!.FindChild("LocateDirectoryResident",true,false) as Button;
            check(locate is {Disabled:false,Text:"定位村民"},"在场村民无法直接定位");
            var found=FindTownSearchMatches(name).First(match=>match.Kind=="居民"&&match.Title==name);
            found.Open();
            check(_management?.Visible==false&&_center.IsEqualApprox(new(actor.Position.X,0,actor.Position.Z)),"小镇搜索没有前往村民实际位置");

            ShowManagement("家庭","residents");
            var follow=(Button)_managementBody.FindChild("FollowDirectoryResident",true,false)!;
            check(!follow.Disabled,"在场村民无法跟随");
            follow.EmitSignal(BaseButton.SignalName.Pressed);
            _cameraTransition=0;
            actor.Position+=Vector3.Right*3;
            UpdateResidentFollow(1);
            check(_followResidentId==id&&_center.DistanceTo(new Vector3(actor.Position.X,0,actor.Position.Z))<.05f&&_selection.Contains("下一站"),"村民跟随没有更新目标和下一站");
            _UnhandledInput(new InputEventKey{Pressed=true,Keycode=Key.Escape});
            check(_followResidentId.Length==0,"Esc 没有退出村民跟随");
            VerifyResidentMapAndOutfits(actor,check);

            actor.InsideHome=true;
            actor.InsideHomeBuildingId=NativeGame.S(home,"building_id");
            actor.ResidentialActivity="在家休息";
            actor.Visual.Hide();
            ShowManagement("家庭","residents");
            locate=_managementBody.FindChild("LocateDirectoryResident",true,false) as Button;
            check(locate is {Disabled:false,Text:"定位住宅"},"屋内村民没有提供住宅定位");
            locate!.EmitSignal(BaseButton.SignalName.Pressed);
            check(_center.IsEqualApprox(TownProjectCenter(home))&&_selection.Contains("已定位住宅"),"住宅定位没有使用村民实际所在的住宅");
            GD.Print("RESIDENT_DIRECTORY_VERIFIED search_name_and_identity visible_actor home_fallback");
        }finally{
            StopResidentFollow();
            (actor.InsideHome,actor.InsideHomeBuildingId,actor.Visual.Visible,actor.ResidentialActivity,actor.Position)=actorState;
            (_center,_pitch,_distance,_cameraFrom,_cameraTo,_cameraTransition,_portraitAnchor,_buildCamera,_selectedPet,
                _selectedResidence,_previousCameraSite,_selection)=viewState;
            _residentDirectoryQuery="";
            _residentDirectoryPage=0;
            _residentDirectoryOpen=false;
            MarkLocatedResident(previouslyLocated);
            _management?.Hide();
        }
    }
}
