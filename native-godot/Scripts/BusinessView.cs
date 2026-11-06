using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;

public partial class Main
{
    double _simulationAccumulator;
    double _autosaveTime;
    long _businessRevision=-1;
    readonly Dictionary<string,Node3D> _dynamic=new();
    readonly Dictionary<string,string> _sceneLayoutKeys=new();
    readonly Dictionary<string,int> _constructionVisualStages=new();
    int _sceneRoadRevision=-1;
    HashSet<Vector2I>? _sceneRoadCells;
    int _roadTopologyVersion;
    (int Created,int Reused,int Removed) _lastLayoutCounts;

    void Act(string action,JsonObject args)
    {
        bool failed=false;
        bool guideWasActive=action=="feed"&&FirstDayGuideActive;
        try {
            _selection=Game.Execute(action,args,()=>{
                if(action=="feed")RecordFirstDayFeeding();
                if(!_firstDayGuideSuppressWrites){
                    Data.Meta["native_real_anchor"]=DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                    WriteTownProgress();
                }
            });
            if(action is "feed" or "bath" or "play" or "batch_care" or "juvenile_care")foreach(var pet in GetChildren().OfType<PetAgent>().Where(p=>action=="batch_care"?Game.LastBatchCare.Contains(p.Ident):p.Ident==NativeGame.S(args,"pet_id")))pet.React(action=="juvenile_care"?"soothe":action=="feed"||NativeGame.S(args,"kind")=="feed"?"eat":action=="bath"||NativeGame.S(args,"kind")=="bath"?"bath":"pet");
            SyncBusinessWorld();
            PlayTownActionCue(action,args);
        } catch(Exception error){Data.Reindex();_selection=error.Message;failed=true;}
        if(action=="feed"){
            RefreshFirstDayGuide();
            if(!failed&&guideWasActive&&!FirstDayGuideActive)_selection+=" · 首日引导已完成";
        }
        if(_management?.Visible==true){
            if(failed||!RefreshManagementAction(action))
                ShowManagement(_tab,action is "reserve_stock" or "weekend_festival" or "decoration"?_managementEntryTarget:"");
            SetManagementFeedback(_selection,failed,completed:!failed);
        }
        if(!failed&&guideWasActive&&!FirstDayGuideActive)OfferGrowthAfterGuide();
    }
    bool RefreshManagementAction(string action)
    {
        bool update=_tab switch{
            "订单"=>action is "order" or "reserve_stock",
            "照护"=>action is "feed" or "bath" or "play" or "collect" or "batch_care",
            "宠物"=>action is "feed" or "bath" or "play" or "collect" or "juvenile_care",
            "外观"=>action is "pet_appearance" or "reset_appearance" or "favorite_style",
            "环境"=>action is "farm_plot" or "manager_policy",
            "自治"=>action=="freight_priority",
            "地图"=>action=="map_pin",
            "今日待办"=>action is "ack_construction" or "ack_hatch_notice",
            "成长"=>action is "town_commission" or "reserve_stock",
            "建造"=>action=="decoration"&&_buildWorkspace==3,
            "事件"=>action is "event" or "weekend_festival",
            "商店"=>action is "buy" or "sell" or "craft" or "buy_pet" or "adopt" or "workshop_queue" or "expiry_policy" or "supply_minimum",
            _=>false
        };
        if(!update||_refreshManagementPage==null)return false;
        if(action=="batch_care")(_managementBody?.FindChild("BatchCarePreview",true,false) as Control)?.Hide();
        UpdateManagementSummary();
        UpdateJournalBadge();
        _refreshManagementPage.Invoke();
        return true;
    }
    void SyncBusinessWorld()
    {
        if(_businessRevision==Game.WorldRevision){
            RefreshConstructionMeshes();
            return;
        }
        _businessRevision=Game.WorldRevision;Data.Reindex();
        int roadRevision=NativeGame.I(Data.Meta,"road_revision");
        var manorBuildings=Data.Snapshot["buildings"]!.AsArray();
        var townBuildings=Data.Snapshot["town_buildings"]!.AsArray();
        string decorations=Data.Meta["decorations"]!.ToJsonString();
        string zones=Data.Snapshot["town_zones"]!.ToJsonString();
        string signature=roadRevision+"|"+manorBuildings.ToJsonString()+townBuildings.ToJsonString()+decorations+zones;
        if(signature!=_layoutSignature){_layoutSignature=signature;
        string obstacles=BuildingFinish.ObstacleSignature(manorBuildings)+BuildingFinish.ObstacleSignature(townBuildings)+decorations;
        bool obstaclesChanged=_farmObstacleSignature!=obstacles;
        _farmObstacleSignature=obstacles;
        bool roadsChanged=false;
        Vector2I[] removedRoads=Array.Empty<Vector2I>();
        if(roadRevision!=_sceneRoadRevision){
            _sceneRoadRevision=roadRevision;
            roadsChanged=_sceneRoadCells==null||!_sceneRoadCells.SetEquals(Data.Roads.Keys);
            if(roadsChanged){
                removedRoads=_sceneRoadCells?.Where(cell=>!Data.Roads.ContainsKey(cell)).ToArray()??Array.Empty<Vector2I>();
                _sceneRoadCells=Data.Roads.Keys.ToHashSet();
                _roadTopologyVersion++;
                CreateSignals();
            }
        }
        bool zonesChanged=_sceneZoneSignature!=zones;
        bool surroundingsChanged=roadsChanged||obstaclesChanged||zonesChanged;
        _sceneZoneSignature=zones;
        var remaining=_dynamic.Keys.ToHashSet();
        int created=0,reused=0,removed=0;
        var cleared=Data.Meta["town_cleared"]!.AsArray().Select(c=>c!.GetValue<string>()).ToHashSet();foreach(var (cell,node) in _natural)node.Visible=!cleared.Contains($"{cell.X},{cell.Y}");
        bool Keep(string id,string key)
        {
            remaining.Remove(id);
            if(_dynamic.ContainsKey(id)&&_sceneLayoutKeys.GetValueOrDefault(id)==key){reused++;return true;}
            Remove(id);
            _sceneLayoutKeys[id]=key;
            return false;
        }
        void Remove(string id)
        {
            _sceneLayoutKeys.Remove(id);
            _constructionVisualStages.Remove(id);
            if(!_dynamic.Remove(id,out var node))return;
            if(node.GetParent()==this)RemoveChild(node);
            node.QueueFree();
            removed++;
        }
        void Put(string id,Mesh mesh,Vector3 position,float angle=0,float scale=1,bool collision=false,string kind="") {
            var n=new MeshInstance3D{Mesh=mesh,Position=position,Rotation=new(0,angle,0),Scale=(kind=="road"?Vector3.One:new Vector3(WorldData.LayoutScale,1,WorldData.LayoutScale))*scale};AddChild(n);_dynamic[id]=n;created++;
            if(collision){var box=mesh.GetAabb();var size=box.Size.Max(Vector3.One*.05f);var center=box.GetCenter();
                if(kind=="building"){
                    var plot=Game.Rows("buildings").First(b=>NativeGame.I(b!,"building_id").ToString()==id[(id.IndexOf(':')+1)..])!;
                    if(NativeGame.S(plot,"kind")!=""){size.X=128/12f*.88f;size.Z=70/12f*.83f;center.X=0;center.Z=0;}
                }
                if(kind=="town_building"){
                    var house=Game.Rows("town_buildings").First(b=>NativeGame.S(b!,"building_id")==id[(id.IndexOf(':')+1)..])!;
                    (size,center)=TownBuildingCollisionBounds(mesh,house);
                }
                bool emptyPlot=kind=="building"&&NativeGame.S(Game.Rows("buildings").First(b=>NativeGame.I(b!,"building_id").ToString()==id[(id.IndexOf(':')+1)..])!,"kind")=="";
                var body=new StaticBody3D{CollisionLayer=emptyPlot?8u:1u,CollisionMask=0};body.SetMeta("kind",kind);body.SetMeta("ident",id[(id.IndexOf(':')+1)..]);body.AddChild(new CollisionShape3D{Shape=new BoxShape3D{Size=size},Position=center});n.AddChild(body);}
        }
        foreach(var pair in Data.Roads) {
            int mask=0;int bit=0;foreach(var d in new[]{Vector2I.Up,Vector2I.Right,Vector2I.Down,Vector2I.Left}){if(Data.Roads.ContainsKey(pair.Key+d))mask|=1<<bit;bit++;}
            string id="road:"+pair.Key,style=WorldData.S(pair.Value,"road_style","asphalt");
            if(!Keep(id,$"{style}|{mask}"))Put(id,RoadMesh(mask,style),WorldData.Cell(pair.Key),kind:"road");
        }
        foreach(var b in Data.Snapshot["buildings"]!.AsArray()) {
            int id=WorldData.I(b!,"building_id");string kind=WorldData.S(b!,"kind");
            if(Keep("building:"+id,$"{kind}|{WorldData.I(b!,"level")}|{NativeGame.B(b!,"unlocked")}|{BuildingFinish.Read(b!)}"))continue;
            if(kind!=""){
                Put("building:"+id,Prototype($"manor:{kind}:{WorldData.I(b!,"level",1)}"),WorldData.Expand(new((1314+id%5*230)/12f,0,(870+id/5*88)/12f)),collision:true,kind:"building");
                var manor=(MeshInstance3D)_dynamic["building:"+id];
                ApplyDistrictArt(manor,"estate",id);
                manor.Mesh=BuildingFinishMesh(manor.Mesh,b!);
            }
            else {
                var mesh=new BoxMesh{Size=new(10.6f,.08f,6.6f),Material=new StandardMaterial3D{AlbedoColor=NativeGame.B(b!,"unlocked")?new Color("b7d596"):new Color("a7c58b")}};
                Put("building:"+id,mesh,WorldData.Expand(new((1312+id%5*230)/12f,0,(858+id/5*88)/12f)),collision:true,kind:"building");
            }
        }
        foreach(var b in Game.Rows("town_buildings").OfType<JsonNode>()) {
            string key=WorldData.S(b,"catalog_key"), id="town_building:"+WorldData.S(b,"building_id");
            if(Keep(id,$"{key}|{WorldData.I(b,"cx")}|{WorldData.I(b,"cy")}|{WorldData.I(b,"w")}|{WorldData.I(b,"h")}|{WorldData.I(b,"orientation")}|{WorldData.S(b,"status")}|{BuildingFinish.Read(b)}"))continue;
            int stage=Game.ConstructionStage(b);
            Put(id,BuildingFinishMesh(Prototype($"town:{key}:{stage}"),b),TownProjectCenter(b),-WorldData.I(b,"orientation")*Mathf.Pi/2,collision:true,kind:"town_building");
            if(stage<4)_constructionVisualStages[id]=stage;
            if(Game.IsResidentialHome(b)&&stage==4){var marker=new Label3D{Text="住宅 · 点击查看家庭",Font=new SystemFont{FontNames=new[]{"Microsoft YaHei"}},FontSize=32,PixelSize=.018f,Billboard=BaseMaterial3D.BillboardModeEnum.Enabled,NoDepthTest=false,Position=Vector3.Up*15,Modulate=new Color("ffdf9d")};_dynamic[id].AddChild(marker);}
        }
        foreach(var d in Data.Meta["decorations"]!.AsArray()){
            string id="decoration:"+WorldData.S(d!,"id"),kind=WorldData.S(d!,"kind");
            int x=WorldData.I(d!,"cx"),y=WorldData.I(d!,"cy"),orientation=WorldData.I(d!,"orientation");
            if(!Keep(id,$"{kind}|{x}|{y}|{orientation}"))
                Put(id,Prototype("decoration:"+kind),WorldData.Cell(new(x,y)),-orientation*Mathf.Pi/2,kind.StartsWith("tree_")?.8f:1,collision:true,kind:"decoration");
        }
        remaining.Remove("garden");
        remaining.Remove("district:park");
        foreach(var id in remaining)Remove(id);
        if(surroundingsChanged){
            Remove("garden");
            Remove("district:park");
            CreateDistrictWalkways();
            var garden=CreateGardenDetails();AddChild(garden);_dynamic["garden"]=garden;
            CreateParkShelter();
            ScheduleRoadRefresh(removedRoads,obstaclesChanged||zonesChanged);
        }
        _lastLayoutCounts=(created,reused,removed);
        }
        RefreshConstructionMeshes();
        var people=Data.People.Select(p=>WorldData.S(p!,"person_id")).ToHashSet();
        foreach(var a in Agents.Where(a=>!a.IsCar&&!people.Contains(a.Ident)).ToArray()){Agents.Remove(a);a.QueueFree();}
        foreach(var p in Data.People) {
            string id=WorldData.S(p!,"person_id");var actor=Agents.FirstOrDefault(a=>a.Ident==id);
            if(actor==null){actor=new RoadAgent{Town=this,Ident=id,Position=ResidentSpawn(p!)};actor.Visual.Mesh=Prototype("person-0");actor.SetMeta("kind","person");actor.SetMeta("ident",id);AddChild(actor);Agents.Add(actor);AssignRoute(actor);}
            actor.Visual.Scale=Vector3.One*(Game.Now-WorldData.D(p!,"born_ts",-1e20)<7*86400?.6f:1);
        }
        var residentPets=Data.Pets.OfType<JsonNode>().Where(pet=>Game.Active(pet)&&!NativeGame.B(pet,"fostered"))
            .ToDictionary(pet=>WorldData.S(pet,"pet_id"));
        foreach(var id in _farmPets.Keys.Where(id=>!residentPets.ContainsKey(id)).ToArray()){
            _farmPets[id].QueueFree();
            _farmPets.Remove(id);
        }
        foreach(var (id,pet) in residentPets){
            if(FarmPet(id)==null)AddFarmPet(new PetAgent{Town=this,Ident=id,Species=WorldData.S(pet,"species"),
                Position=PetHomePoint(pet)});
        }
    }
    (Vector3 Size,Vector3 Center) TownBuildingCollisionBounds(Mesh mesh,JsonNode building)
    {
        var bounds=mesh.GetAabb();
        var size=bounds.Size.Max(Vector3.One*.05f);
        var center=bounds.GetCenter();
        if(Game.IsResidentialHome(building)){
            // Keep the walking area clear of stairs and roof overhangs.
            var footprint=Game.Spec("CATALOG",NativeGame.S(building,"catalog_key"))["footprint"]!;
            size.X=footprint[0]!.GetValue<int>()*4*.88f;
            size.Z=footprint[1]!.GetValue<int>()*4*.83f;
            center.X=center.Z=0;
        }
        return (size,center);
    }

    void RefreshConstructionMeshes()
    {
        if(_constructionVisualStages.Count==0)return;
        foreach(var building in Game.Rows("town_buildings").OfType<JsonNode>()){
            string id="town_building:"+NativeGame.S(building,"building_id");
            if(!_constructionVisualStages.TryGetValue(id,out int previousStage))continue;
            int stage=Game.ConstructionStage(building);
            if(stage==previousStage)continue;

            var instance=(MeshInstance3D)_dynamic[id];
            string catalog=NativeGame.S(building,"catalog_key");
            var mesh=Prototype($"town:{catalog}:{stage}");
            instance.Mesh=BuildingFinishMesh(mesh,building);
            var collision=instance.GetChild<StaticBody3D>(0).GetChild<CollisionShape3D>(0);
            var (size,center)=TownBuildingCollisionBounds(mesh,building);
            ((BoxShape3D)collision.Shape).Size=size;
            collision.Position=center;
            _constructionVisualStages[id]=stage;
        }
    }

    string _layoutSignature="";
    string _sceneZoneSignature="";
    void ShowManagement(string tab,string entryId="",Action? prepare=null)
    {
        CancelTownPointer();
        bool opening=_management?.Visible!=true;
        RecordManagementVisit(tab,entryId,opening);
        prepare?.Invoke();
        if(tab=="成长")_firstDayGrowthOffer=false;
        if(tab!="商店"){
            _orderSupplyRoute=null;
            _careSupplyRoute=null;
        }
        StopPetFollow();
        StopResidentFollow();
        if(_managementScroll!=null)_managementScrollPositions[_tab]=_managementScroll.ScrollVertical;
        bool pageChanged=_tab!=tab;
        _tab=tab;
        _managementEntryTarget=entryId;
        _managementFocusTarget=null;
        _refreshManagementPage=null;
        if(_management==null){
            CreateManagementWindow();
            _management!.VisibilityChanged+=RefreshFirstDayGuide;
        }
        if(opening||pageChanged)SetManagementFeedback("");
        foreach(var child in _managementBody!.GetChildren()){_managementBody.RemoveChild(child);child.QueueFree();}
        UpdateManagementHeader(tab,pageChanged||opening);
        if(tab=="外观")AppearanceControls();else if(tab=="照护")CareControls();else if(tab=="财务")FinanceControls();else if(tab=="档案")ArchiveControls();else if(tab=="地图")MapControls();else if(tab=="存档")SaveControls();else if(tab=="家庭")FamilyControls();else if(tab=="宠物")PetControls();else if(tab=="商店")ShopControls();else if(tab=="建造")BuildControls();else if(tab=="自治")AutonomyControls();else if(tab=="环境")EnvironmentControls();else if(tab=="事件")EventControls();
        else if(tab=="订单")OrderControls();else if(tab=="日志")LogControls();
        else if(tab=="成长")GrowthControls();
        else if(tab=="今日待办"){
            CalendarControls();
            MapPinJournalControls();
            JournalControls();
            AddFirstDayGuideShortcut(_managementBody);
        }
        if(_firstDayGrowthOffer)AddFirstDayGrowthLink();
        int restoreScroll=_managementRestoredScroll??_managementScrollPositions.GetValueOrDefault(tab);
        _managementScroll!.SetDeferred("scroll_vertical",restoreScroll);
        if(opening)_management!.PopupCentered(ManagementWindowSize());
        if(tab=="订单")RecordFirstDayVisit(GuideOrders);
        if(!_managementRestoredScroll.HasValue&&_managementFocusTarget is Control target){
            if(tab is "环境" or "档案"||(tab=="建造"&&_buildWorkspace==2)||
                entryId is "town-commission" or "tourism" or "workshop-queue" or "pending-events" or "weekend-festivals"
                ||entryId.StartsWith("repair:",StringComparison.Ordinal)||entryId.StartsWith("companion-shop:",StringComparison.Ordinal)
                ||entryId.StartsWith("decor:",StringComparison.Ordinal)||entryId.StartsWith("placed-decor:",StringComparison.Ordinal))
                _=RevealManagementSection(target);
            else Callable.From(()=>{
                if(GodotObject.IsInstanceValid(target)&&target.IsInsideTree()&&_management?.Visible==true)
                    _managementScroll.EnsureControlVisible(target);
            }).CallDeferred();
        }
    }
    enum OrderFilter { Active, Ready, Missing, Challenge, Closed }
    enum OrderListSort { Deadline, Ready, Reward }
    OrderFilter _orderFilter;
    OrderListSort _orderListSort;
    int _orderHistoryPage;
    const int OrderHistoryPageSize=8;

    void OrderControls()
    {
        var summary=Section("邻里订单","进行中 0 单")!;
        var body=_managementBody!;
        var toolbar=new HFlowContainer();
        toolbar.AddThemeConstantOverride("h_separation",8);
        toolbar.AddThemeConstantOverride("v_separation",6);
        body.AddChild(toolbar);
        var filters=new Dictionary<OrderFilter,(Button Button,string Caption)>();
        foreach(var (filter,caption) in new[]{
            (OrderFilter.Active,"日常"),(OrderFilter.Ready,"可交付"),(OrderFilter.Missing,"缺货 / 缺伙伴"),(OrderFilter.Challenge,"挑战"),(OrderFilter.Closed,"已结束")
        }){
            var button=new Button{Name=$"OrderFilter{filter}",ToggleMode=true,CustomMinimumSize=new(0,36)};
            TownTheme.AccentButton(button,filter==OrderFilter.Closed?"档案":"订单",6);
            if(filter==OrderFilter.Missing)button.TooltipText="查看缺少物资或暂无匹配伙伴的订单";
            toolbar.AddChild(button);
            filters.Add(filter,(button,caption));
        }
        var sort=MarketOptions(toolbar,new[]{"按截止时间","可交付优先","奖励从高到低"},(int)_orderListSort);
        sort.Name="OrderListSort";
        TownTheme.AccentButton(sort,"订单",6);
        var pagination=new HBoxContainer();
        pagination.AddThemeConstantOverride("separation",6);
        toolbar.AddChild(pagination);
        var previous=new Button{Name="OrderHistoryPrevious",Text="上一页"};
        TownTheme.AccentButton(previous,"订单",6);
        pagination.AddChild(previous);
        var historyPage=MarketLabel(pagination,"",color:TownTheme.Muted);
        historyPage.VerticalAlignment=VerticalAlignment.Center;
        var next=new Button{Name="OrderHistoryNext",Text="下一页"};
        TownTheme.AccentButton(next,"订单",6);
        pagination.AddChild(next);
        var focusNotice=Text("已定位到当前列表之外的订单");
        focusNotice.Name="OrderFocusNotice";
        focusNotice.AddThemeColorOverride("font_color",TownTheme.Accent("订单").Darkened(.22f));
        AddProcurementChecklist();
        var list=new VBoxContainer();
        list.AddThemeConstantOverride("separation",8);
        body.AddChild(list);
        var empty=Text("");
        var cards=new Dictionary<string,(PanelContainer Panel,Action<JsonNode> Update)>();

        (PanelContainer,Action<JsonNode>) AddOrder(JsonNode order)
        {
            string id=NativeGame.S(order,"order_id"),item=NativeGame.S(order,"item");
            bool itemOrder=NativeGame.S(order,"kind")=="item";
            bool checkIn=NativeGame.S(order,"kind")=="checkin";
            int quantity=NativeGame.I(order,"quantity"),reward=NativeGame.I(order,"reward");
            var card=new PanelContainer{Name=$"OrderCard_{id}"};
            bool highlighted=id==_managementEntryTarget;
            card.AddThemeStyleboxOverride("panel",TownTheme.Box(highlighted?"e3edf1":"f2f0e8",10,10));
            list.AddChild(card);
            var column=new VBoxContainer();card.AddChild(column);
            string request=NativeGame.OrderRequestCaption(order);
            string badge=NativeGame.B(order,"challenge")?"挑战 · ":NativeGame.B(order,"stage_matched")?"阶段委托 · ":"";
            column.AddChild(new Label{Text=$"{badge}{NativeGame.S(order,"npc")} · {request}",AutowrapMode=TextServer.AutowrapMode.WordSmart});
            var publisher=AddOrderPublisherControls(column,order);
            var deadline=new Label();deadline.AddThemeColorOverride("font_color",TownTheme.Muted);column.AddChild(deadline);
            var margin=itemOrder?MarketLabel(column,"",14,TownTheme.Muted,wrap:true):null;
            if(margin!=null)margin.Name="OrderNetValue";
            var readiness=itemOrder?new ProgressBar{Name="OrderReadiness",MaxValue=Math.Max(1,quantity),ShowPercentage=false,
                CustomMinimumSize=new(0,7),MouseFilter=Control.MouseFilterEnum.Ignore}:null;
            if(readiness!=null){
                readiness.AddThemeStyleboxOverride("background",TownTheme.Box("e3dfd5",4,0));
                readiness.AddThemeStyleboxOverride("fill",TownTheme.Box("779d82",4,0));
                column.AddChild(readiness);
            }
            var row=new HFlowContainer();row.AddThemeConstantOverride("h_separation",8);column.AddChild(row);
            var picker=new OptionButton{CustomMinimumSize=new(170,34),FitToLongestItem=false,Visible=false};
            TownTheme.AccentButton(picker,"订单",6);
            row.AddChild(picker);
            var deliver=new Button{Text=itemOrder?"交付物资":checkIn?"提交近况":"交付所选伙伴"};
            TownTheme.AccentButton(deliver,"订单",6);row.AddChild(deliver);
            Button? buySupply=null,craftSupply=null;
            bool canBuy=itemOrder&&Game.Rules["ITEMS"]?[item]!=null&&Game.BuyPrice(item)>0;
            bool canCraft=itemOrder&&Game.Rules["RECIPES"]?[item]!=null;
            if(canBuy){
                buySupply=new Button{Name="OrderBuySupply",Text="前往购买",Visible=false};
                TownTheme.AccentButton(buySupply,"商店",6);
                buySupply.Pressed+=()=>OpenOrderSupply(id,item,useRecipe:false);
                row.AddChild(buySupply);
            }
            if(canCraft){
                craftSupply=new Button{Name="OrderCraftSupply",Text="查看加工配方",Visible=false};
                TownTheme.AccentButton(craftSupply,"商店",6);
                craftSupply.Pressed+=()=>OpenOrderSupply(id,item,useRecipe:true);
                row.AddChild(craftSupply);
            }
            string source=itemOrder&&!canBuy&&!canCraft?OrderSupplySource(item):"";
            var note=new Label{AutowrapMode=TextServer.AutowrapMode.WordSmart};
            if(itemOrder)note.Name="OrderReservationStock";
            note.AddThemeColorOverride("font_color",TownTheme.Muted);column.AddChild(note);
            string owner=NativeGame.OrderReservationOwner(id);
            var reservation=itemOrder?AddStockReservationControls(column,()=>owner,()=>item,"OrderReservation","订单"):null;
            (string Id,string Name)[] candidates=Array.Empty<(string,string)>();
            deliver.Pressed+=()=>{
                if(itemOrder||checkIn){Act("order",new JsonObject{{"order_id",id}});return;}
                if(picker.Selected<0||picker.Selected>=candidates.Length)return;
                var (petId,name)=candidates[picker.Selected];
                ConfirmManagementAction("交付伙伴",$"交付「{name}」可获得 {reward} 金币。这位伙伴将离开庄园。","确认交付",
                    ()=>Act("order",new JsonObject{{"order_id",id},{"pet_id",petId}}));
            };

            void Update(JsonNode current)
            {
                publisher?.Invoke(current);
                bool focused=id==_managementEntryTarget;
                if(focused!=highlighted){
                    highlighted=focused;
                    card.AddThemeStyleboxOverride("panel",TownTheme.Box(highlighted?"e3edf1":"f2f0e8",10,10));
                }
                string status=NativeGame.S(current,"status");
                bool pending=OrderPending(current,Game.Now);
                double remaining=NativeGame.D(current,"expiry_ts")-Game.Now;
                string ended=status switch{"进行中" or "过期"=>"已到期","完成"=>"已交付",_=>status};
                bool soon=pending&&remaining<=12*NativeGame.Hour;
                string sign=soon?"! 临期 · ":TownTheme.HighContrast?(pending?"○ 进行中 · ":status=="完成"?"✓ ":"× "):"";
                deadline.Text=sign+(pending?$"奖励 {reward} 金币 · {DeadlineText(remaining)}":$"{ended} · 原奖励 {reward} 金币");
                var ink=soon?TownTheme.HighContrast?new Color("641d24"):TownTheme.Accent("事件").Darkened(.26f):TownTheme.Muted;
                if(deadline.GetThemeColor("font_color")!=ink)deadline.AddThemeColorOverride("font_color",ink);
                row.Visible=note.Visible=pending;
                if(readiness!=null)readiness.Visible=pending;
                if(margin!=null){margin.Visible=pending;if(pending)margin.Text=OrderValueCaption(current);}
                if(!pending){
                    reservation?.Invoke(default,quantity,false);
                    return;
                }
                if(checkIn){deliver.Disabled=false;note.Text="向邻居登记近况，领取基础奖励。";return;}
                if(itemOrder){
                    var stock=Game.InventoryAvailability(item,owner);
                    int missing=Math.Max(0,quantity-stock.ForOwner);
                    deliver.Disabled=missing>0;
                    readiness!.Value=Math.Min(stock.ForOwner,quantity);
                    deliver.TooltipText=missing>0?$"还需备齐 {missing} 份{item}":"将按订单数量扣除物资并结算奖励";
                    if(buySupply!=null)buySupply.Visible=missing>0;
                    if(craftSupply!=null)craftSupply.Visible=missing>0;
                    note.Text=$"可交付 {Math.Min(stock.ForOwner,quantity)} / {quantity} 份 · {ReservationStockText(stock)}"+
                        (missing>0?$" · 下一步补齐 {missing} 份":" · 现在可交付");
                    if(missing>0&&source.Length>0)note.Text+=$"\n{source}";
                    reservation?.Invoke(stock,quantity,true);
                    return;
                }
                var choices=Data.Pets.OfType<JsonNode>().Where(p=>PetMatchesOrder(p,current))
                    .Select(p=>(NativeGame.S(p,"pet_id"),NativeGame.S(p,"name"))).ToArray();
                if(!candidates.SequenceEqual(choices)){
                    string selected=picker.Selected>=0&&picker.Selected<candidates.Length?candidates[picker.Selected].Id:_selectedPet;
                    candidates=choices;
                    picker.Clear();
                    foreach(var choice in candidates)picker.AddItem(choice.Name);
                    if(candidates.Length>0)picker.Select(Math.Max(0,Array.FindIndex(candidates,choice=>choice.Id==selected)));
                }
                picker.Visible=candidates.Length>0;
                deliver.Disabled=candidates.Length==0;
                deliver.TooltipText=deliver.Disabled?"需有符合要求的在园伙伴":"交付前会再次确认伙伴姓名";
                note.Text=deliver.Disabled?"暂无符合物种、年龄和健康要求的在园伙伴。":$"有 {candidates.Length} 位符合要求 · 伙伴交付后将离开庄园。";
            }
            return (card,Update);
        }

        void UpdateOrders()
        {
            var recent=Game.Rows("orders").OfType<JsonNode>().ToArray();
            var archive=Game.Rows("order_archive");
            var active=recent.Where(order=>OrderPending(order,Game.Now))
                .OrderBy(order=>NativeGame.D(order,"expiry_ts")).ToArray();
            var regular=active.Where(order=>!NativeGame.B(order,"challenge")).ToArray();
            var challenge=active.Where(order=>NativeGame.B(order,"challenge")).ToArray();
            var ready=active.Where(order=>NativeGame.S(order,"kind") switch{
                "item"=>Game.Stock(NativeGame.S(order,"item"),NativeGame.OrderReservationOwner(NativeGame.S(order,"order_id")))>=NativeGame.I(order,"quantity"),
                "checkin"=>true,_=>Data.Pets.OfType<JsonNode>().Any(pet=>PetMatchesOrder(pet,order))
            }).ToHashSet();
            int readyCount=regular.Count(ready.Contains);
            int closedCount=recent.Length-active.Length+archive.Count;
            summary.Text=$"日常 {regular.Length} 单 · 可交付 {readyCount} 单 · 挑战 {challenge.Length} 单";
            foreach(var (filter,entry) in filters){
                int count=filter switch{
                    OrderFilter.Ready=>readyCount,OrderFilter.Missing=>regular.Length-readyCount,
                    OrderFilter.Closed=>closedCount,OrderFilter.Challenge=>challenge.Length,_=>regular.Length
                };
                entry.Button.Text=$"{entry.Caption}  {count}";
                entry.Button.SetPressedNoSignal(filter==_orderFilter);
            }
            int historyPages=Math.Max(1,(closedCount+OrderHistoryPageSize-1)/OrderHistoryPageSize);
            _orderHistoryPage=Math.Clamp(_orderHistoryPage,0,historyPages-1);
            pagination.Visible=_orderFilter==OrderFilter.Closed&&historyPages>1;
            sort.Visible=_orderFilter!=OrderFilter.Closed;
            previous.Disabled=_orderHistoryPage==0;
            next.Disabled=_orderHistoryPage==historyPages-1;
            historyPage.Text=$"第 {_orderHistoryPage+1} / {historyPages} 页";
            var visible=_orderFilter switch{
                OrderFilter.Ready=>regular.Where(ready.Contains).ToArray(),
                OrderFilter.Missing=>regular.Where(order=>!ready.Contains(order)).ToArray(),
                OrderFilter.Challenge=>challenge,
                OrderFilter.Closed=>recent.Where(order=>!OrderPending(order,Game.Now)).Concat(archive.OfType<JsonNode>())
                    .OrderByDescending(order=>NativeGame.D(order,"created_ts"))
                    .Skip(_orderHistoryPage*OrderHistoryPageSize).Take(OrderHistoryPageSize).ToArray(),
                _=>regular,
            };
            if(_orderFilter!=OrderFilter.Closed)visible=_orderListSort switch{
                OrderListSort.Ready=>visible.OrderByDescending(ready.Contains).ToArray(),
                OrderListSort.Reward=>visible.OrderByDescending(order=>NativeGame.I(order,"reward")).ToArray(),
                _=>visible,
            };
            empty.Visible=visible.Length==0;
            empty.Text=_orderFilter switch{
                OrderFilter.Ready=>"当前没有可交付的订单。备齐物资或找到符合条件的伙伴后会显示在这里。",
                OrderFilter.Missing=>"当前没有待备齐的订单。",
                OrderFilter.Challenge=>"当前没有进行中的挑战订单。",
                OrderFilter.Closed=>"完成或到期的订单会收录在这里。",
                _=>"暂时没有进行中的订单。可以稍后回来看看。",
            };
            string target=_managementEntryTarget;
            var pinned=target.Length>0&&!visible.Any(order=>NativeGame.S(order,"order_id")==target)
                ?recent.Concat(archive.OfType<JsonNode>()).FirstOrDefault(order=>NativeGame.S(order,"order_id")==target):null;
            focusNotice.Visible=pinned!=null;
            if(pinned!=null)visible=visible.Prepend(pinned).ToArray();
            var ids=visible.Select(order=>NativeGame.S(order,"order_id")).ToHashSet();
            foreach(string id in cards.Keys.Where(id=>!ids.Contains(id)).ToArray()){
                list.RemoveChild(cards[id].Panel);cards[id].Panel.QueueFree();cards.Remove(id);
            }
            for(int i=0;i<visible.Length;i++){
                var order=visible[i];
                string id=NativeGame.S(order,"order_id");
                if(!cards.TryGetValue(id,out var card))cards[id]=card=AddOrder(order);
                if(card.Panel.GetIndex()!=i)list.MoveChild(card.Panel,i);
                card.Update(order);
            }
            _managementFocusTarget=cards.TryGetValue(target,out var focused)?focused.Panel:null;
        }

        void SelectOrders()
        {
            _managementEntryTarget="";
            UpdateOrders();
            _managementScroll!.SetDeferred("scroll_vertical",0);
        }
        foreach(var (filter,entry) in filters)entry.Button.Pressed+=()=>{_orderFilter=filter;SelectOrders();};
        sort.ItemSelected+=index=>{_orderListSort=(OrderListSort)index;SelectOrders();};
        previous.Pressed+=()=>{_orderHistoryPage--;SelectOrders();};
        next.Pressed+=()=>{_orderHistoryPage++;SelectOrders();};
        UpdateOrders();
        _refreshManagementPage+=UpdateOrders;
    }
    void EnvironmentControls()
    {
        RegisterEnvironmentSection("显示与画质",DisplayPreferencesControls());
        RegisterEnvironmentSection("键盘快捷键",TownInputControls());
        RegisterEnvironmentSection("镜头控制",TownCameraControls());
        CameraBookmarkControls();
        RegisterEnvironmentSection("声音",TownMasterAudioControls());
        AtmosphereSoundControls();
        TownActionSoundControls();
        DistrictSoundControls();
        var timeHeading=TownTimePlannerControls();
        var farmHeading=Section("四季与照护","农场日常")!;
        RegisterEnvironmentSection("农场日常",farmHeading.GetParent<Control>().GetParent<Control>());
        RegisterEnvironmentSection("时间计划",timeHeading);
        Text($"季节：{Game.Season} · 天气：{Game.Weather} · 4游戏小时更新天气");
        _farmManagerInformation=Text(FarmManagerSummary);
        FarmManagerPolicyControls();
        Text("管理员会前往粪便旁清扫、给饱食度低于70的动物喂普通饲料；无任务时原地待命。");
        Text($"饲养员：{(NativeGame.B(Data.Meta,"caretaker")?"工作中":"未雇佣")} · 雇佣{NativeGame.I(Game.Rules,"CARE_HIRE_COST")}金币 · 每班{NativeGame.I(Game.Rules,"CARE_WAGE")}金币");
        ActionButton("雇佣/解雇饲养员",()=>Act("caretaker",new JsonObject()));
        Text($"管理员雇佣{NativeGame.I(Game.Rules,"MANAGER_HIRE_COST")}金币 · 每班{NativeGame.I(Game.Rules,"MANAGER_WAGE")}金币");
        ActionButton("雇佣/解雇农场管理员",()=>Act("farm_manager",new JsonObject()));
        ActionButton("定位农场管理员",LocateFarmManager);
        foreach(var b in Game.Rows("buildings")){int id=NativeGame.I(b!,"building_id");var exposure=Game.Exposure(id);if(exposure.Total>0)Text($"地块{id} · 粪便{exposure.Total}份 / 陈旧{exposure.Stale}份 · 疾病风险×{exposure.Hazard:0.0}");}
        Section("农场清扫与收获");
        ActionButton("清扫全部粪便入库",()=>Act("clean_waste",new JsonObject()));ActionButton("清扫并出售全部粪便（需商店）",()=>Act("sell_waste",new JsonObject()));
        var crops=Data.Meta["field_batch"]!.AsObject();Text("待收作物："+(crops.Count==0?"暂无":string.Join("、",crops.Select(p=>$"{p.Key}×{p.Value}"))));ActionButton("收获农田作物",()=>Act("harvest",new JsonObject()));
        FarmPlotControls();
        Text($"已收集物种{Game.State["codex"]!.AsObject().Count} · 已获成就{Game.State["achievements"]!.AsObject().Count}");
        foreach(var a in Game.State["achievements"]!.AsObject())Text("成就："+Game.Rules["ACHIEVEMENTS"]![a.Key]![0]!.GetValue<string>());
    }
}
