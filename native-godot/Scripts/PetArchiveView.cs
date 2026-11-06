using Godot;
using System;
using System.Linq;
using System.Text.Json.Nodes;

public partial class Main
{
    const int ArchivePageSize=8;
    string _archiveQuery="",_petTimelineQuery="";
    int _archiveStatus,_archiveSort,_archivePage,_petTimelinePage;

    void OpenPetArchive(string id)
    {
        string target="companion:"+id;
        if(_managementEntryTarget!=target){_petTimelineQuery="";_petTimelinePage=0;}
        ShowManagement("档案",target);
    }

    void ArchiveControls()
    {
        string target=_managementEntryTarget;
        var pet=target.StartsWith("companion:",StringComparison.Ordinal)?Data.FindPet(target[10..]):null;
        string section=target is "codex" or "records"?target:"companions";
        var tabs=ArchiveToolbar(_managementBody!);
        _managementFocusTarget=tabs;
        foreach(var (key,label) in new[]{("companions","伙伴档案"),("codex","物种图鉴"),("records","小镇记录")}){
            var button=MarketButton(tabs,label,()=>ShowManagement("档案",key),accent:"档案");
            button.Name="ArchiveSection_"+key;
            button.ToggleMode=true;
            button.SetPressedNoSignal(section==key);
        }

        switch(section){
            case "codex": ArchiveCodex(); break;
            case "records": ArchiveTownRecords(); break;
            default:
                if(pet==null)ArchivePetDirectory();else ArchivePetProfile(pet);
                break;
        }
    }

    void ArchivePetDirectory()
    {
        var summary=Section("伙伴档案","全部伙伴")!;
        var tools=ArchiveToolbar(_managementBody!);
        var search=ArchiveSearch(tools,"ArchivePetQuery","搜索名字或物种",_archiveQuery);
        var status=MarketOptions(tools,new[]{"全部状态","在园","离世","离园"},_archiveStatus);
        status.Name="ArchivePetStatus";
        var sort=MarketOptions(tools,new[]{"出生时间倒序","按名字"},_archiveSort);
        sort.Name="ArchivePetSort";
        var pager=ArchivePager("ArchivePets");
        var list=new VBoxContainer{Name="ArchivePetList"};
        _managementBody!.AddChild(list);
        (string Id,string Name,string Species,int State,string Status,double Birth)[]? shown=null;

        search.TextChanged+=value=>{_archiveQuery=value;_archivePage=0;Render();};
        status.ItemSelected+=value=>{_archiveStatus=(int)value;_archivePage=0;Render();};
        sort.ItemSelected+=value=>{_archiveSort=(int)value;_archivePage=0;Render();};
        pager.Previous.Pressed+=()=>{_archivePage--;Render();};
        pager.Next.Pressed+=()=>{_archivePage++;Render();};
        Render();
        _refreshManagementPage+=Render;

        void Render()
        {
            var pets=Data.Pets.OfType<JsonNode>().Select(row=>
                (Id:NativeGame.S(row,"pet_id"),Name:NativeGame.S(row,"name"),Species:NativeGame.S(row,"species"),
                 State:Game.Active(row)?1:NativeGame.D(row,"death_ts")>0?2:3,
                 Status:PetLocationStatus(row),Birth:NativeGame.D(row,"birth_ts"))).ToArray();
            int active=pets.Count(p=>p.State==1);
            int dead=pets.Count(p=>p.State==2);
            summary.Text=$"在园 {active} · 离世 {dead} · 离园 {pets.Length-active-dead}";
            var results=pets.Where(p=>(_archiveStatus==0||p.State==_archiveStatus)&&
                ArchiveMatches(_archiveQuery,$"{p.Name} {p.Species}"));
            var sorted=(_archiveSort==1?results.OrderBy(p=>p.Name,StringComparer.CurrentCulture):results.OrderByDescending(p=>p.Birth))
                .ThenBy(p=>p.Id,StringComparer.Ordinal).ToArray();
            _archivePage=pager.Update(sorted.Length,_archivePage);
            var page=sorted.Skip(_archivePage*ArchivePageSize).Take(ArchivePageSize).ToArray();
            if(shown!=null&&shown.SequenceEqual(page))return;
            shown=page;
            ClearArchiveRows(list);
            if(page.Length==0)MarketLabel(list,"没有符合当前条件的伙伴。",color:TownTheme.Muted);
            foreach(var pet in page){
                var row=MarketCard(list,color:pet.State==2?"eee9ef":"f3ede6");
                var open=MarketButton(row,$"{pet.Name} · {pet.Species} · {pet.Status}",()=>OpenPetArchive(pet.Id),accent:"档案");
                open.Name="ArchivePet_"+pet.Id;
                open.Alignment=HorizontalAlignment.Left;
                open.TextOverrunBehavior=TextServer.OverrunBehavior.TrimEllipsis;
                open.TooltipText=open.Text;
            }
        }
    }

    void ArchivePetProfile(JsonNode pet)
    {
        string id=NativeGame.S(pet,"pet_id"),species=NativeGame.S(pet,"species");
        var actions=ArchiveToolbar(_managementBody!);
        MarketButton(actions,"返回伙伴名册",()=>ShowManagement("档案","companions"),expand:false,accent:"档案");
        if(Game.Active(pet))MarketButton(actions,"管理这位伙伴",()=>OpenPetManagement(id),expand:false,accent:"宠物");
        if(Game.Rules["SPECIES"]?[species]!=null)MarketButton(actions,"查看物种图鉴",()=>ShowManagement("档案","codex",()=>{
            _archiveSpecies=species;_archiveSpeciesQuery="";_archiveSpeciesFilter=0;
        }),expand:false,accent:"档案");

        var profile=MarketCard(_managementBody!,color:"eee9ef");
        var details=ArchiveToolbar(profile);
        if(Game.Rules["SPECIES"]?[species]!=null)details.AddChild(AnimalBadge(species,116,80,pet));
        var facts=new VBoxContainer{SizeFlagsHorizontal=Control.SizeFlags.ExpandFill,CustomMinimumSize=new(240,0)};
        details.AddChild(facts);
        MarketLabel(facts,NativeGame.S(pet,"name"),22,TownTheme.Accent("档案"),wrap:true);
        var attributes=MarketLabel(facts,"",15,TownTheme.Ink,wrap:true);
        var condition=MarketLabel(facts,"",14,TownTheme.Muted,wrap:true);
        var relatives=ArchiveToolbar(profile);
        foreach(var (caption,key) in new[]{("伴侣","mate_id"),("母亲","mother_id"),("父亲","father_id")}){
            string relativeId=NativeGame.S(pet,key);
            if(Data.FindPet(relativeId) is not JsonNode relative)continue;
            MarketButton(relatives,$"{caption} · {NativeGame.S(relative,"name")}",()=>OpenPetArchive(relativeId),expand:false,accent:"档案");
        }
        RefreshProfile();
        _refreshManagementPage+=RefreshProfile;

        ArchivePetTimeline(pet);
        AddPetPhotoAlbum(id);
        PetFacilityArchive(pet);
        AddPetRelationships(_managementBody!,id,archive:true);

        void RefreshProfile()
        {
            var current=Data.FindPet(id)??pet;
            attributes.Text=$"{species} · {NativeGame.S(current,"sex")} · {NativeGame.S(current,"personality")} · {PetLocationStatus(current)}"
                +$"\n{Game.PetAge(current)/NativeGame.Day:0.00} 游戏日 · {(NativeGame.B(current,"rare")?"稀有":"普通")}";
            condition.Text=$"健康 {NativeGame.D(current,"health"):0} · 饱食 {NativeGame.D(current,"hunger"):0} · 开心 {NativeGame.D(current,"happy"):0}"
                +$" · 清洁 {NativeGame.D(current,"cleanliness"):0} · 好感 {NativeGame.D(current,"favor"):0}";
            string illness=NativeGame.S(current,"illness_type");
            if(illness.Length>0)condition.Text+="\n疾病 · "+illness;
            double due=NativeGame.D(current,"pregnancy_due_ts");
            if(due>0)condition.Text+="\n预产期 · "+Stamp(due);
            int home=NativeGame.I(current,"building_id");
            if(home>0)condition.Text+=$"\n居所地块 {home}";
        }
    }

    void ArchivePetTimeline(JsonNode pet)
    {
        Section("一生的足迹","出生、成长与生活记录 · 游戏时间 UTC");
        var search=ArchiveSearch(_managementBody!,"ArchiveTimelineQuery","搜索足迹中的文字",_petTimelineQuery);
        var pager=ArchivePager("ArchiveTimeline");
        var list=new VBoxContainer{Name="ArchiveTimelineRows"};
        _managementBody!.AddChild(list);
        string id=NativeGame.S(pet,"pet_id");
        (double Time,string Text)[]? shown=null;
        search.TextChanged+=value=>{_petTimelineQuery=value;_petTimelinePage=0;Render();};
        pager.Previous.Pressed+=()=>{_petTimelinePage--;Render();};
        pager.Next.Pressed+=()=>{_petTimelinePage++;Render();};
        Render();
        _refreshManagementPage+=Render;

        void Render()
        {
            var current=Data.FindPet(id)??pet;
            var notes=(current["history"] as JsonArray)?.OfType<JsonObject>()
                .Select(note=>(Time:NativeGame.D(note,"ts"),Text:NativeGame.S(note,"text")))
                .Where(note=>note.Text.Length>0).ToList()??new();
            double birth=NativeGame.D(current,"birth_ts");
            if(birth>0)notes.Add((birth,"出生"));
            double death=NativeGame.D(current,"death_ts");
            if(death>0&&!notes.Any(note=>note.Time==death&&note.Text.StartsWith("离世",StringComparison.Ordinal)))
                notes.Add((death,"离世 · "+NativeGame.S(current,"death_reason","原因未记录")));
            string departure=NativeGame.S(current,"departure");
            if(departure.Length>0)notes.Add((NativeGame.D(current,"departure_ts"),"离园 · "+departure));
            var results=notes.Where(note=>ArchiveMatches(_petTimelineQuery,note.Text))
                .OrderByDescending(note=>note.Time).ToArray();
            _petTimelinePage=pager.Update(results.Length,_petTimelinePage);
            var page=results.Skip(_petTimelinePage*ArchivePageSize).Take(ArchivePageSize).ToArray();
            if(shown!=null&&shown.SequenceEqual(page))return;
            shown=page;
            ClearArchiveRows(list);
            if(page.Length==0)MarketLabel(list,"没有符合条件的足迹。",color:TownTheme.Muted);
            foreach(var (time,caption) in page){
                var row=MarketCard(list,color:"f6f0e7");
                MarketLabel(row,time>0?Stamp(time):"时间未记录",13,TownTheme.Muted);
                MarketLabel(row,caption,15,TownTheme.Ink,wrap:true);
            }
        }
    }

    static HFlowContainer ArchiveToolbar(Control parent)
    {
        var bar=new HFlowContainer();
        bar.AddThemeConstantOverride("h_separation",7);
        bar.AddThemeConstantOverride("v_separation",5);
        parent.AddChild(bar);
        return bar;
    }

    static LineEdit ArchiveSearch(Control parent,string id,string hint,string value)
    {
        var field=new LineEdit{Name=id,Text=value,PlaceholderText=hint,AccessibilityName=hint,
            ClearButtonEnabled=true,CustomMinimumSize=new(205,36),SizeFlagsHorizontal=Control.SizeFlags.ExpandFill};
        parent.AddChild(field);
        return field;
    }

    (Button Previous,Button Next,Func<int,int,int> Update) ArchivePager(string id)
    {
        var bar=ArchiveToolbar(_managementBody!);
        var back=PageButton("上一页",id+"Previous");
        var label=MarketLabel(bar,"",14,TownTheme.Muted);label.Name=id+"Page";
        label.VerticalAlignment=VerticalAlignment.Center;
        var forward=PageButton("下一页",id+"Next");
        return (back,forward,Update);

        int Update(int total,int page)
        {
            int pages=(total+ArchivePageSize-1)/ArchivePageSize;
            page=Math.Clamp(page,0,Math.Max(0,pages-1));
            label.Text=total==0?"0 条 · 0 / 0":$"{total} 条 · {page+1} / {pages}";
            back.Disabled=page==0;
            forward.Disabled=page+1>=pages;
            return page;
        }

        Button PageButton(string caption,string name)
        {
            var button=new Button{Text=caption,Name=name};
            TownTheme.AccentButton(button,"档案");
            bar.AddChild(button);
            return button;
        }
    }

    static bool ArchiveMatches(string query,string text)=>query.Split((char[]?)null,StringSplitOptions.RemoveEmptyEntries)
        .All(part=>text.Contains(part,StringComparison.OrdinalIgnoreCase));

    static void ClearArchiveRows(Control container)
    {
        foreach(Node child in container.GetChildren()){
            container.RemoveChild(child);
            child.QueueFree();
        }
    }
}
