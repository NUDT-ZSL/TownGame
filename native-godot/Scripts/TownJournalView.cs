using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;

public partial class Main
{
    sealed record JournalEntry(string Id,string Page,string Title,string Detail,int Priority,double Due,bool Urgent=false,bool Notice=false);
    enum JournalView { Pending, Urgent, Reviewed }
    const int JournalPageSize=8;
    Button? _journalButton;
    JournalEntry[] _journalEntries=Array.Empty<JournalEntry>();
    string _journalFilter="";
    int _journalPage;
    JournalView _journalView;

    static string DeadlineText(double seconds)
    {
        if(seconds<=0)return "已到期";
        if(seconds<NativeGame.Hour)return $"剩余 {Math.Max(1,Math.Ceiling(seconds/60))} 游戏分钟";
        return $"剩余 {Math.Ceiling(seconds/NativeGame.Hour)} 游戏小时";
    }

    static bool OrderPending(JsonNode order,double now)=>
        NativeGame.S(order,"status")=="进行中"&&NativeGame.D(order,"expiry_ts")>now;

    bool PetMatchesOrder(JsonNode pet,JsonNode order)=>
        Game.Active(pet)&&!NativeGame.B(pet,"fostered")&&Game.OrderPetFits(pet,order);

    void UpdateJournalBadge()
    {
        var entries=new List<JournalEntry>();
        var pets=Data.Pets.OfType<JsonNode>().Where(p=>Game.Active(p)&&!NativeGame.B(p,"fostered")).ToArray();
        foreach(var pet in pets){
            string id=NativeGame.S(pet,"pet_id"),name=NativeGame.S(pet,"name");
            double health=NativeGame.D(pet,"health"),hunger=NativeGame.D(pet,"hunger"),clean=NativeGame.D(pet,"cleanliness");
            int? priority=Game.PetCarePriority(pet);
            bool inpatient=Game.IsPetInHospital(pet);
            if(Game.NeedsPetTreatment(pet))
                entries.Add(new(id,"宠物",$"{name}需要治疗",$"{(inpatient?"已住院":"待送医")} · 健康 {health:0}",0,health,Urgent:true));
            else if(inpatient)
                entries.Add(new(id,"宠物",$"{name}的出院安排",$"{(Game.PetDischargeHome(pet)!=null?"可出院":"等待适配居所空位")} · 健康 {health:0}",1,health));
            else if(priority==0)
                entries.Add(new(id,"宠物",$"{name}需要查看健康状况",$"健康 {health:0} · 饱食 {hunger:0}",0,health,Urgent:true));
            else if(priority==1)
                entries.Add(new(id,"宠物",$"{name}需要照护",$"饱食 {hunger:0} · 清洁 {clean:0}",1,Math.Min(hunger,clean)));
        }
        foreach(var hatch in Game.RecentHatches()){
            string id=NativeGame.S(hatch,"pet_id");
            double when=NativeGame.D(hatch,"when");
            if(!NativeGame.B(hatch,"journal_read")&&Game.Now-when<NativeGame.Day&&pets.Any(pet=>NativeGame.S(pet,"pet_id")==id)&&
                !entries.Any(entry=>entry.Page=="宠物"&&entry.Id==id))
                entries.Add(new(id,"宠物",$"{NativeGame.S(hatch,"name")}已破壳","已入住预留居所，可查看幼崽照护",2,when,Notice:true));
        }
        foreach(var entry in Game.Rows("events").OfType<JsonNode>()){
            double due=NativeGame.D(entry,"expiry_ts");
            if(NativeGame.S(entry,"status")=="待处理"&&due>Game.Now)
                entries.Add(new(NativeGame.S(entry,"event_id"),"事件",NativeGame.S(entry,"text"),DeadlineText(due-Game.Now),2,due,
                    Urgent:due-Game.Now<=2*NativeGame.Hour));
        }
        foreach(var order in Game.Rows("orders").OfType<JsonNode>().Where(o=>OrderPending(o,Game.Now))){
            double due=NativeGame.D(order,"expiry_ts");
            bool itemOrder=NativeGame.S(order,"kind")=="item";
            string item=NativeGame.S(order,"item");
            int quantity=NativeGame.I(order,"quantity");
            bool ready=itemOrder?Game.Stock(item,NativeGame.OrderReservationOwner(NativeGame.S(order,"order_id")))>=quantity:
                NativeGame.S(order,"kind")=="checkin"||pets.Any(p=>PetMatchesOrder(p,order));
            if(!ready&&due-Game.Now>12*NativeGame.Hour)continue;
            string request=NativeGame.OrderRequestCaption(order);
            string state=ready?"可交付":"即将到期";
            entries.Add(new(NativeGame.S(order,"order_id"),"订单",$"{NativeGame.S(order,"npc")} · {request}",
                $"{state} · {DeadlineText(due-Game.Now)}",due-Game.Now<=12*NativeGame.Hour?3:4,due,Urgent:due-Game.Now<=12*NativeGame.Hour));
        }
        foreach(var shortage in Game.SupplyShortages())
            entries.Add(new(shortage.Item,"商店",$"{shortage.Item}低于库存下限",
                $"可用 {shortage.Available} 份 · 下限 {shortage.Minimum} 份",3,Game.Now));
        foreach(var building in Game.UnreadConstructionProjects()){
            double finish=NativeGame.D(building,"finish_ts");
            entries.Add(new(NativeGame.S(building,"building_id"),"建造",$"{TownProjectName(building)}已完工",
                $"{TownProjectCell(building)} · {TownProjectClock(finish)} 完工",4,finish,Notice:true));
        }
        _journalEntries=entries.OrderBy(entry=>entry.Priority).ThenBy(entry=>entry.Due).ToArray();
        string label=_journalEntries.Length==0?"今日待办":$"今日待办 {_journalEntries.Length}";
        if(_journalButton!=null&&_journalButton.Text!=label)_journalButton.Text=label;
    }

    void JournalControls()
    {
        var summary=Section("小镇待办","当前 0 项")!;
        var overview=new HFlowContainer();
        overview.AddThemeConstantOverride("h_separation",8);
        overview.AddThemeConstantOverride("v_separation",6);
        _managementBody!.AddChild(overview);
        var scope=MarketOptions(overview,new[]{"当前待办","优先处理","已读消息"},(int)_journalView);
        scope.Name="JournalScope";
        scope.TooltipText="优先处理显示紧急照护、两游戏小时内截止的事件和十二游戏小时内到期的订单。";
        var urgentButton=MarketButton(overview,"查看首项优先待办",()=>{
            var entry=_journalEntries.FirstOrDefault(candidate=>candidate.Urgent);
            if(entry!=null)OpenJournalEntry(entry.Page,entry.Id);
        },expand:false,accent:"事件");
        urgentButton.Name="JournalOpenUrgent";
        var filters=new Dictionary<string,(Button Button,string Title)>();
        foreach(var (page,title) in new[]{("","全部"),("宠物","伙伴照护"),("事件","小镇事件"),("订单","邻里订单"),("建造","工程完工"),("商店","库存补货")}){
            var filter=new Button{Name=page.Length==0?"JournalFilterAll":$"JournalFilter_{page}",ToggleMode=true,CustomMinimumSize=new(0,36)};
            TownTheme.AccentButton(filter,page,6);
            overview.AddChild(filter);
            filters.Add(page,(filter,title));
        }
        var empty=Text("现在没有急需处理的事项，可以看看伙伴，或去小镇逛一逛。");
        var cards=new VBoxContainer();
        cards.AddThemeConstantOverride("separation",8);
        _managementBody!.AddChild(cards);
        var items=new Dictionary<(string Page,string Id),(PanelContainer Card,Label Title,Label Detail,Label State,Button Open,Button? Locate,Button? Dismiss)>();
        var paging=new HBoxContainer();
        paging.AddThemeConstantOverride("separation",6);
        overview.AddChild(paging);
        var previous=new Button{Name="JournalPreviousPage",Text="上一页"};
        TownTheme.AccentButton(previous,"今日待办",6);
        paging.AddChild(previous);
        var pageCount=MarketLabel(paging,"",color:TownTheme.Muted);
        pageCount.VerticalAlignment=VerticalAlignment.Center;
        var next=new Button{Name="JournalNextPage",Text="下一页"};
        TownTheme.AccentButton(next,"今日待办",6);
        paging.AddChild(next);
        previous.Pressed+=()=>ChangePage(-1);
        next.Pressed+=()=>ChangePage(1);
        foreach(var (page,filter) in filters)filter.Button.Pressed+=()=>{
            _journalFilter=page;
            _journalPage=0;
            Render();
        };
        var shortcuts=new HFlowContainer();
        shortcuts.AddThemeConstantOverride("h_separation",8);
        _managementBody.AddChild(shortcuts);
        foreach(var (label,page) in new[]{("查看伙伴","宠物"),("批量照护","照护"),("查看全部订单","订单")} ){
            var button=new Button{Text=label};
            TownTheme.AccentButton(button,page,6);
            button.Pressed+=()=>ShowManagement(page);
            shortcuts.AddChild(button);
        }

        void ChangePage(int step)
        {
            _journalPage+=step;
            Render();
            _managementScroll!.SetDeferred("scroll_vertical",0);
        }

        void Render()
        {
            int urgentCount=_journalEntries.Count(entry=>entry.Urgent);
            var reviewed=Game.ReviewedJournalNotices();
            var shown=_journalView switch{
                JournalView.Urgent=>_journalEntries.Where(entry=>entry.Urgent).ToArray(),
                JournalView.Reviewed=>reviewed.Select(entry=>new JournalEntry(entry.Id,entry.Page,entry.Title,
                    $"{entry.Detail} · {TownProjectClock(entry.ReviewedAt)} 已读",4,entry.ReviewedAt,Notice:true)).ToArray(),
                _=>_journalEntries
            };
            scope.SetItemText(0,$"当前待办 · {_journalEntries.Length}");
            scope.SetItemText(1,$"优先处理 · {urgentCount}");
            scope.SetItemText(2,$"已读消息 · {reviewed.Length}");
            urgentButton.Visible=_journalView!=JournalView.Reviewed&&urgentCount>0;
            var filtered=_journalFilter.Length==0?shown:shown.Where(entry=>entry.Page==_journalFilter).ToArray();
            int pages=Math.Max(1,(filtered.Length+JournalPageSize-1)/JournalPageSize);
            _journalPage=Math.Clamp(_journalPage,0,pages-1);
            string heading=_journalFilter.Length>0?filters[_journalFilter].Title:_journalView switch{
                JournalView.Reviewed=>"已读消息",JournalView.Urgent=>"优先处理",_=>"当前待办"};
            summary.Text=$"{heading} · {filtered.Length} 项";
            foreach(var (page,filter) in filters){
                int count=page.Length==0?shown.Length:shown.Count(entry=>entry.Page==page);
                filter.Button.Text=$"{filter.Title}  {count}";
                filter.Button.SetPressedNoSignal(page==_journalFilter);
                filter.Button.Visible=_journalView!=JournalView.Reviewed||page is "" or "建造" or "宠物";
            }
            empty.Text=_journalView switch{
                JournalView.Reviewed=>"这里保留最近标记已读的孵化和完工消息。",
                JournalView.Urgent=>"当前筛选下没有优先事项，可切回当前待办。",
                _=>_journalFilter.Length==0?"现在没有急需处理的事项，可以看看伙伴，或去小镇逛一逛。":$"目前没有{filters[_journalFilter].Title}待办。"
            };
            empty.Visible=filtered.Length==0;
            paging.Visible=pages>1;
            previous.Disabled=_journalPage==0;
            next.Disabled=_journalPage==pages-1;
            pageCount.Text=$"第 {_journalPage+1} / {pages} 页";
            var visible=filtered.Skip(_journalPage*JournalPageSize).Take(JournalPageSize).ToArray();
            var keys=visible.Select(entry=>(entry.Page,entry.Id)).ToHashSet();
            foreach(var key in items.Keys.Where(key=>!keys.Contains(key)).ToArray()){
                cards.RemoveChild(items[key].Card);
                items[key].Card.QueueFree();
                items.Remove(key);
            }
            for(int i=0;i<visible.Length;i++){
                var entry=visible[i];
                var key=(entry.Page,entry.Id);
                if(!items.TryGetValue(key,out var controls)){
                    var card=new PanelContainer{Name=$"JournalCard_{entry.Page}_{entry.Id}"};
                    Color accent=TownTheme.Accent(entry.Page);
                    card.AddThemeStyleboxOverride("panel",TownTheme.Box(accent.Lerp(new Color("fffcf4"),.88f).ToHtml(),10,10));
                    var column=new VBoxContainer();
                    var title=new Label{AutowrapMode=TextServer.AutowrapMode.WordSmart};
                    var detail=new Label{AutowrapMode=TextServer.AutowrapMode.WordSmart};
                    detail.AddThemeColorOverride("font_color",TownTheme.Muted);
                    var state=new Label{Name="JournalEntryState"};
                    state.AddThemeFontSizeOverride("font_size",14);
                    var row=new HFlowContainer();row.AddThemeConstantOverride("h_separation",6);row.AddThemeConstantOverride("v_separation",5);
                    var open=new Button{Name="JournalOpen",Text=entry.Page switch{"宠物"=>"查看伙伴","建造"=>"查看项目",_=>$"查看{entry.Page}"},SizeFlagsVertical=Control.SizeFlags.ShrinkCenter};
                    TownTheme.AccentButton(open,entry.Page,6);
                    open.Pressed+=()=>OpenJournalEntry(key.Page,key.Id);
                    cards.AddChild(card);card.AddChild(column);
                    column.AddChild(title);column.AddChild(state);column.AddChild(detail);column.AddChild(row);row.AddChild(open);
                    Button? locate=null,dismiss=null;
                    if(key.Page=="建造"){
                        locate=new Button{Name="LocateCompletedConstruction",Text="定位现场",SizeFlagsVertical=Control.SizeFlags.ShrinkCenter};
                        TownTheme.AccentButton(locate,"建造",6);
                        locate.Pressed+=()=>LocateTownProject(key.Id);
                        row.AddChild(locate);
                    }
                    if(key.Page is "建造" or "宠物"){
                        bool building=key.Page=="建造";
                        dismiss=new Button{Name=building?"DismissConstructionNotice":"DismissHatchNotice",Text="标记已读",SizeFlagsVertical=Control.SizeFlags.ShrinkCenter};
                        TownTheme.AccentButton(dismiss,key.Page,6);
                        dismiss.Pressed+=()=>Act(building?"ack_construction":"ack_hatch_notice",
                            new JsonObject{{building?"building_id":"pet_id",key.Id}});
                        row.AddChild(dismiss);
                    }
                    items[key]=controls=(card,title,detail,state,open,locate,dismiss);
                }
                if(controls.Card.GetIndex()!=i)cards.MoveChild(controls.Card,i);
                if(controls.Title.Text!=entry.Title)controls.Title.Text=entry.Title;
                if(controls.Detail.Text!=entry.Detail)controls.Detail.Text=entry.Detail;
                controls.State.Text=_journalView==JournalView.Reviewed?"已读消息":entry.Urgent?"优先处理":entry.Notice?"小镇消息":"待处理";
                var ink=entry.Urgent?new Color(TownTheme.HighContrast?"671e20":"8c4141"):TownTheme.Muted;
                if(controls.State.GetThemeColor("font_color")!=ink)controls.State.AddThemeColorOverride("font_color",ink);
                bool targetExists=_journalView!=JournalView.Reviewed||
                    (entry.Page=="建造"?Game.FindTownBuilding(entry.Id)!=null:Data.FindPet(entry.Id) is JsonNode pet&&Game.Active(pet));
                controls.Open.Disabled=!targetExists;
                controls.Open.TooltipText=targetExists?"":"此消息的关联对象已离开或移除。";
                if(controls.Locate!=null)controls.Locate.Disabled=!targetExists;
                if(controls.Dismiss!=null)controls.Dismiss.Visible=entry.Notice&&_journalView!=JournalView.Reviewed;
            }
        }
        scope.ItemSelected+=index=>{
            _journalView=(JournalView)index;
            _journalFilter="";
            _journalPage=0;
            Render();
        };
        UpdateJournalBadge();
        Render();
        _refreshManagementPage+=Render;
    }

    void OpenJournalEntry(string page,string id)
    {
        if(page=="宠物"){
            var pet=Data.FindPet(id);
            if(pet!=null&&Game.Active(pet))OpenPetManagement(id,Game.NeedsPetTreatment(pet)||Game.IsPetInHospital(pet)?"clinic":"");
            else SetManagementFeedback("这位伙伴已离园，可在档案中查看。",true);
        }
        else if(page=="建造")OpenTownProject(id);
        else if(page=="商店")OpenSupplyWatch(id);
        else ShowManagement(page,id);
    }
}
