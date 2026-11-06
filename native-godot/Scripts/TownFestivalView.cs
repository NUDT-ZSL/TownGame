using Godot;
using System;
using System.Linq;
using System.Text.Json.Nodes;

public partial class Main
{
    Node3D? _festivalStage;
    Label3D? _festivalStageSign;
    string _festivalStageKey="",_festivalSitesLayout="";
    bool _festivalShowCandidates,_festivalShowHistory;
    readonly System.Collections.Generic.Dictionary<string,Vector2I[]> _festivalSites=new();

    // Reuse the placement rules so temporary stalls and soil beds never occupy a building or a road.
    Vector2I[] OpenDistrictCells(string zone)
    {
        if(_festivalSitesLayout!=_layoutSignature){_festivalSitesLayout=_layoutSignature;_festivalSites.Clear();}
        if(_festivalSites.TryGetValue(zone,out var cells))return cells;
        var rect=Data.Root["zones"]![zone]!["rect"]!.AsArray();
        var portal=Data.Portals[zone];
        int left=Math.Max(0,(int)Math.Ceiling(rect[0]!.GetValue<double>()/48-.5));
        int top=Math.Max(0,(int)Math.Ceiling(rect[1]!.GetValue<double>()/48-.5));
        int right=Math.Min(124,(int)Math.Floor((rect[0]!.GetValue<double>()+rect[2]!.GetValue<double>())/48-.5));
        int bottom=Math.Min(79,(int)Math.Floor((rect[1]!.GetValue<double>()+rect[3]!.GetValue<double>())/48-.5));
        var linked=Game.ConnectedRoads;
        var natural=_natural.Where(entry=>entry.Node.Visible).Select(entry=>entry.Cell).ToHashSet();
        var landmarks=GetChildren().OfType<MeshInstance3D>().Where(mesh=>mesh.HasMeta("kind")&&mesh.GetMeta("kind").AsString()=="zone"&&mesh.Mesh!=null)
            .Select(mesh=>mesh.GlobalTransform*mesh.Mesh!.GetAabb()).ToArray();
        if(_dynamic.TryGetValue("district:park",out var shelter))natural.Add(WorldData.Grid(shelter.Position));
        Vector2I[] offsets={Vector2I.Up,Vector2I.Down,Vector2I.Left,Vector2I.Right};
        cells=Enumerable.Range(left,Math.Max(0,right-left+1)).SelectMany(x=>Enumerable.Range(top,Math.Max(0,bottom-top+1)).Select(y=>new Vector2I(x,y)))
            .Where(cell=>!natural.Contains(cell)&&!landmarks.Any(bounds=>bounds.Intersects(new Aabb(WorldData.Cell(cell)-new Vector3(3.6f,0,3.6f),new(7.2f,2.2f,7.2f)))))
            .Where(cell=>Game.DecorationQuote("kit_market_crates",cell.X,cell.Y,0).Reason is "" or "金币不足")
            .OrderByDescending(cell=>offsets.Any(step=>linked.Contains(new NativeGame.Cell(cell.X+step.X,cell.Y+step.Y))))
            .ThenBy(cell=>(cell-portal).LengthSquared()).ThenBy(cell=>cell.Y).ThenBy(cell=>cell.X).ToArray();
        return _festivalSites[zone]=cells;
    }

    Vector3? FestivalSite(string zone)=>OpenDistrictCells(zone) is { Length:>0 } sites?WorldData.Cell(sites[0]):null;

    Vector3 FestivalVisitPoint(JsonNode resident,Vector3 center)
    {
        float slot=(PersonHash(NativeGame.S(resident,"person_id"))%7-3f)*.9f;
        return center+new Vector3(slot,0,2.25f);
    }

    RoadAgent? ArrivedFestivalGuest(NativeGame.TownFestival festival,Vector3 site)
    {
        foreach(var person in Data.People.OfType<JsonNode>().Where(row=>NativeGame.S(row,"native_festival")==festival.Key)){
            var actor=Agents.FirstOrDefault(agent=>agent.Ident==NativeGame.S(person,"person_id"));
            if(actor!=null&&!actor.InsideHome&&actor.Visual.Visible&&actor.Index>=actor.Path.Count&&
                actor.Position.DistanceSquaredTo(FestivalVisitPoint(person,site))<4)return actor;
        }
        return null;
    }

    void UpdateFestivalStage()
    {
        var festival=Game.CurrentFestival;
        Vector3? position=festival==null?null:FestivalSite(festival.Zone);
        string key=festival==null||position==null?"":$"{festival.Key}:{position.Value}";
        if(_festivalStageKey!=key){
            _festivalStageKey=key;
            _festivalStage?.QueueFree();
            _festivalStage=null;
            _festivalStageSign=null;
            if(festival!=null&&position is Vector3 site){
                _festivalStage=CreateFestivalStage(festival);
                _festivalStage.Position=site;
                AddChild(_festivalStage);
            }
        }
        if(_festivalStageSign!=null&&festival!=null&&position is Vector3 at)
            _festivalStageSign.Text=festival.Title+(ArrivedFestivalGuest(festival,at)!=null?" · 点击参加":" · 等待居民到场");
    }

    Node3D CreateFestivalStage(NativeGame.TownFestival festival)
    {
        var stage=new Node3D{Name="WeekendFestival"};
        var model=new DistrictAccentMesh();
        model.Box(new(0,.11f,0),new(6,.22f,3),"d8ba84");
        string accent=festival.Key=="market"?"bd6b5f":"7c95ad";
        for(int index=0;index<9;index++)model.Box(new(-2.4f+index*.6f,2.52f,-.3f),new(.64f,.13f,1.9f),index%2==0?accent:"f3ddab");
        foreach(float x in new[]{-2.55f,2.55f}){
            model.Box(new(x,1.29f,-.9f),new(.15f,2.58f,.15f),"a28158");
            model.Box(new(x,.5f,.65f),new(.85f,.85f,.68f),accent);
        }
        if(festival.Key=="market"){
            model.Box(new(0,.58f,.12f),new(4,1.08f,.85f),"bb925a");
            for(int i=-3;i<=3;i++)model.Round(new(i*.45f,1.18f,.1f),new(.33f,.3f,.32f),i%2==0?"e3a754":"92aa70");
        }else{
            model.Box(new(0,.37f,.12f),new(3.6f,.38f,1.4f),"92ad92");
            for(int i=-2;i<=2;i++)model.Round(new(i*.57f,1.33f,-.8f),new(.35f,.38f,.16f),i%2==0?"e8a1a2":"ecd089");
        }
        stage.AddChild(new MeshInstance3D{Mesh=model.Finish()});
        var body=new StaticBody3D{CollisionLayer=8};
        body.SetMeta("kind","weekend_festival");body.SetMeta("ident",festival.Key);
        body.AddChild(new CollisionShape3D{Shape=new BoxShape3D{Size=new(5.8f,2.45f,2.6f)},Position=new(0,1.22f,0)});
        stage.AddChild(body);
        _festivalStageSign=TownLifeSign(festival.Title,3.15f);
        stage.AddChild(_festivalStageSign);
        return stage;
    }

    static Label3D TownLifeSign(string title,float height)=>new(){Text=title,Position=Vector3.Up*height,
        Billboard=BaseMaterial3D.BillboardModeEnum.Enabled,FontSize=36,PixelSize=.025f,OutlineSize=7,
        Modulate=new("fff4dc"),OutlineModulate=new("47554e"),VisibilityRangeEnd=80,VisibilityRangeEndMargin=15,
        Font=new SystemFont{FontNames=new[]{"Microsoft YaHei","Noto Sans CJK SC"}}};

    void FestivalControls()
    {
        var card=MarketCard(_managementBody!,color:"f4ebdc");
        MarketLabel(card,"周末活动",18,TownTheme.Accent("事件"));
        var live=MarketCard(card,color:"f9f2e6");
        live.GetParent<PanelContainer>().Name="TownFestivalLive";
        var title=MarketLabel(live,"",17,TownTheme.Ink,wrap:true);
        var when=MarketLabel(live,"",14,TownTheme.Muted,wrap:true);
        var status=MarketLabel(live,"",color:TownTheme.Muted,wrap:true);
        status.Name="TownFestivalParticipation";
        var checklist=AddFestivalChecklist(live);
        var row=FestivalActions(live);
        var visit=MarketButton(row,"前往活动现场",()=>{
            if(Game.CurrentFestival is { } activity)LocateFestivalSite(activity,false);
        },expand:false,accent:"地图");
        visit.Name="TownFestivalVisit";
        var supply=MarketButton(row,"去采购缺少的谷物",()=>{
            _marketBuyQuantity=Math.Max(1,2-Game.Stock("谷物"));
            OpenSearchSupply("谷物");
        },expand:false,accent:"商店");
        supply.Name="TownFestivalSupply";
        var reward=new Button{Name="TownFestivalParticipate",Text="完成现场活动",CustomMinimumSize=new(0,38)};
        TownTheme.AccentButton(reward,"事件");row.AddChild(reward);
        var candidates=AddFestivalCandidates(card);
        var upcoming=MarketCard(card,color:"f6efdf");
        upcoming.GetParent<PanelContainer>().Name="TownFestivalUpcoming";
        MarketLabel(upcoming,"即将开场",17,TownTheme.Ink);
        var schedule=new VBoxContainer{Name="TownFestivalSchedule"};upcoming.AddChild(schedule);
        var wait=MarketButton(upcoming,"快进至下场活动",()=>BeginFastForward(FastForwardGoal.Festival),expand:false,accent:"事件");
        wait.Name="TownFestivalWait";
        var refreshRewards=AddFestivalRewards(card);
        NativeGame.TownFestival[] shown=Array.Empty<NativeGame.TownFestival>();
        Label[] scheduleStatus=Array.Empty<Label>();

        void RefreshUpcoming(NativeGame.TownFestival[] occurrences)
        {
            var next=occurrences.Where(activity=>activity.Opens>Game.Now).Take(2).ToArray();
            if(!shown.SequenceEqual(next)){
                shown=next;
                foreach(var child in schedule.GetChildren()){schedule.RemoveChild(child);child.QueueFree();}
                scheduleStatus=new Label[shown.Length];
                for(int i=0;i<shown.Length;i++){
                    var activity=shown[i];
                    var details=MarketCard(schedule,color:"fff9ef");
                    MarketLabel(details,$"{activity.Title} · {AutonomyZoneName(activity.Zone)}",16,TownTheme.Ink,wrap:true);
                    scheduleStatus[i]=MarketLabel(details,"",14,TownTheme.Muted,wrap:true);
                    var preview=MarketButton(details,"预览活动地点",()=>LocateFestivalSite(activity,true),expand:false,accent:"地图");
                    preview.Name=$"TownFestivalPreview_{activity.Key}_{i}";
                }
            }
            for(int i=0;i<shown.Length;i++){
                var activity=shown[i];
                string blocked=FestivalDistrictBlock(activity);
                scheduleStatus[i].Text=$"{FestivalTimeRange(activity)} · {DeadlineText(activity.Opens-Game.Now)}开场"+
                    (blocked.Length>0?$"\n开场条件 · {blocked}":"");
            }
            var target=FindFastForwardTarget(FastForwardGoal.Festival);
            wait.Visible=target.HasValue;
            if(target is { } at)wait.Text=$"快进至{at.Label}";
        }

        void Claim()
        {
            if(Game.CurrentFestival is not { } current||FestivalSite(current.Zone) is not Vector3 site||
                _center.DistanceSquaredTo(site)>55*55||ArrivedFestivalGuest(current,site) is not RoadAgent host||
                Game.FestivalClaimed(current)||current.Key=="market"&&Game.Stock("谷物")<2)return;
            string pet=candidates.SelectedId();
            if(current.Key=="showcase"&&(Data.FindPet(pet) is not JsonNode candidate||!Game.FestivalShowPetEligible(candidate)))return;
            Act("weekend_festival",new JsonObject{{"festival",current.Key},{"resident_id",host.Ident},{"pet_id",pet}});
        }
        void Refresh()
        {
            var occurrences=Game.FestivalOccurrences(Game.Now).ToArray();
            RefreshUpcoming(occurrences);
            var scheduled=occurrences.FirstOrDefault(activity=>activity.Opens<=Game.Now);
            var current=Game.CurrentFestival;
            Vector3? site=current==null?null:FestivalSite(current.Zone);
            bool claimed=current!=null&&Game.FestivalClaimed(current);
            bool nearby=site.HasValue&&_center.DistanceSquaredTo(site.Value)<=55*55;
            var guest=current!=null&&site.HasValue?ArrivedFestivalGuest(current,site.Value):null;
            visit.Visible=site.HasValue;
            wait.Visible=wait.Visible&&current==null;
            candidates.Refresh(scheduled?.Key=="showcase"&&!claimed);
            refreshRewards();
            int grain=scheduled?.Key=="market"?Game.Stock("谷物"):0;
            string petBlock=candidates.SelectedBlock();
            title.Text=scheduled==null?"当前没有进行中的周末活动":$"{(current==null?"尚未开放":"进行中")} · {scheduled.Title} · {AutonomyZoneName(scheduled.Zone)}";
            when.Visible=scheduled!=null;
            when.Text=scheduled==null?"":$"{FestivalTimeRange(scheduled)} · {DeadlineText(scheduled.Closes-Game.Now)}结束";
            bool suppliesReady=scheduled?.Key=="market"?grain>=2:petBlock.Length==0;
            bool ready=current!=null&&!claimed&&nearby&&guest!=null&&suppliesReady;
            string district=scheduled!=null&&current==null?FestivalDistrictBlock(scheduled):"";
            status.Text=scheduled==null?"查看下方日程，或提前预览活动地点。":district.Length>0?district:
                claimed?"本场已完成，奖励计入下方兑换余额。":ready?"已满足参加条件。":"核对下方的参加条件。";
            checklist.Panel.Visible=scheduled!=null&&!claimed;
            if(scheduled!=null){
                string admission=scheduled.Key=="market"?$"可用谷物 {grain} / 2 份":petBlock.Length==0?"已选符合条件的伙伴":petBlock;
                SetFestivalCheck(checklist.District,"活动与街区",current!=null,current!=null?"已开场，街区道路连通":district);
                SetFestivalCheck(checklist.Site,"前往现场",nearby,!site.HasValue?"现场暂无可用地块":nearby?"镜头已在活动地点附近":"请先前往活动现场");
                SetFestivalCheck(checklist.Host,"居民到场",guest!=null,guest==null?"等待居民实际到达":$"{ResidentFestivalGuestName(guest)} 已到场");
                SetFestivalCheck(checklist.Admission,scheduled.Key=="market"?"准备谷物":"选择伙伴",suppliesReady,admission);
            }
            reward.Text=current?.Key=="market"?"交付谷物":"登记伙伴";
            reward.Visible=site.HasValue&&!claimed;
            reward.Disabled=!ready;
            reward.TooltipText=ready?"完成本场活动并领取装饰兑换机会。":"请先满足参加条件。";
            supply.Visible=scheduled?.Key=="market"&&!claimed&&!suppliesReady&&Game.BuyPrice("谷物")>0;
        }
        reward.Pressed+=Claim;
        candidates.Picker.ItemSelected+=_=>Refresh();
        candidates.Toggle.Toggled+=_=>Refresh();
        Refresh();_refreshManagementPage+=Refresh;
        card.TreeExiting+=()=>_refreshManagementPage-=Refresh;
    }

    string ResidentFestivalGuestName(RoadAgent guest)=>Data.People.OfType<JsonNode>()
        .Where(person=>NativeGame.S(person,"person_id")==guest.Ident).Select(person=>NativeGame.S(person,"name","居民"))
        .FirstOrDefault()??"居民";

    string FestivalDistrictBlock(NativeGame.TownFestival activity)=>Game.ZoneLevel(activity.Zone)==0?"先开放活动所在街区。":
        !Game.Effective(activity.Zone)?"活动街区尚未接通庄园道路。":"";

    string FestivalTimeRange(NativeGame.TownFestival activity)=>
        $"{CalendarDayTitle(activity.Opens)} {(int)(activity.Opens%NativeGame.Day/NativeGame.Hour):00}:00—{(int)(activity.Closes%NativeGame.Day/NativeGame.Hour):00}:00";

    void LocateFestivalSite(NativeGame.TownFestival activity,bool preview)
    {
        var site=FestivalSite(activity.Zone);
        if(!site.HasValue&&!preview){SetManagementFeedback("暂时无法定位活动现场。",true);return;}
        var point=site??WorldData.Cell(Data.Portals[activity.Zone]);
        RememberCameraSite(point);StopPetFollow();StopResidentFollow();EndPlacement();_portraitAnchor=null;_buildCamera=false;
        _center=point;_distance=preview?58:28;_pitch=.69f;
        _selection=preview?$"{activity.Title} · {AutonomyZoneName(activity.Zone)} · {FestivalTimeRange(activity)}"+(!site.HasValue?" · 街区入口":" · 预计地点"):
            $"{activity.Title} · 点击现场摊位参加";
        _management?.Hide();
    }
}
