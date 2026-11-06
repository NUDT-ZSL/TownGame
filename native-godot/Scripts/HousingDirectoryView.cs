using Godot;
using System;
using System.Linq;
using System.Text.Json.Nodes;

public partial class Main
{
    const int HousingPageSize=6;
    static readonly string[] HousingFilters={"全部住宅","已入住","可分配空屋","道路未连通","施工中"};
    string _housingQuery="";
    int _housingFilter,_housingPage;
    bool _housingWaitOpen;

    readonly record struct HousingEntry(string Id,string Name,string District,string Members,int Occupied,int Beds,bool Ready,bool Connected)
    {
        public bool Vacant=>Ready&&Connected&&Occupied==0;
        public string Status=>!Ready?"施工中":!Connected?"道路未连通":Occupied>0?"已入住":"可分配空屋";
    }

    HousingEntry[] HousingDirectoryEntries()
    {
        var occupants=Data.People.OfType<JsonNode>().Where(person=>NativeGame.S(person,"role")=="居民")
            .ToLookup(person=>NativeGame.S(person,"home_building_id"),StringComparer.Ordinal);
        return Game.Rows("town_buildings").OfType<JsonNode>().Where(Game.IsResidentialHome).Select(home=>{
            string id=NativeGame.S(home,"building_id");
            var members=occupants[id].ToArray();
            int capacity=Game.ResidentHomeCapacity(home);
            string district=Game.ResidentHomeZone(home);
            return new HousingEntry(id,ResidenceName(home),district.Length==0?"街区外围":AutonomyZoneName(district),
                string.Join("、",members.Select(person=>NativeGame.S(person,"name"))),members.Length,capacity,capacity>0,
                capacity>0&&Game.HomeRoadEntrance(home).HasValue);
        }).ToArray();
    }

    void HousingDirectoryControls()
    {
        var card=MarketCard(_managementBody!,color:"ede7ef");
        MarketLabel(card,"住房总览",19,TownTheme.Leaf);
        var overview=MarketLabel(card,"",15,TownTheme.Ink,wrap:true);
        overview.Name="HousingDirectorySummary";
        MarketLabel(card,"已入住住宅的空床由该家庭使用。安置新家庭需要一栋空置、道路通达且容量足够的住宅。",14,TownTheme.Muted,wrap:true);
        var filters=new HFlowContainer();filters.AddThemeConstantOverride("h_separation",8);card.AddChild(filters);
        var query=new LineEdit{Name="HousingDirectoryQuery",Text=_housingQuery,PlaceholderText="搜索住宅、街区或住户",
            ClearButtonEnabled=true,MaxLength=60,CustomMinimumSize=new(162,34),SizeFlagsHorizontal=Control.SizeFlags.ExpandFill};
        filters.AddChild(query);
        var filter=MarketOptions(filters,HousingFilters,_housingFilter);filter.Name="HousingDirectoryFilter";
        var reset=HousingButton(filters,"重置筛选","HousingDirectoryReset");
        var navigation=new HFlowContainer();navigation.AddThemeConstantOverride("h_separation",8);card.AddChild(navigation);
        var previous=HousingButton(navigation,"上一页","HousingDirectoryPrevious");
        var page=MarketLabel(navigation,"",14,TownTheme.Muted);page.Name="HousingDirectoryPage";page.VerticalAlignment=VerticalAlignment.Center;
        var next=HousingButton(navigation,"下一页","HousingDirectoryNext");
        var wait=AddHousingWaitlist();
        var rows=new VBoxContainer{Name="HousingDirectoryRows"};_managementBody!.AddChild(rows);
        var empty=MarketLabel(rows,"",15,TownTheme.Muted,wrap:true);
        var entries=new VBoxContainer();rows.AddChild(entries);
        HousingEntry[] rendered=Array.Empty<HousingEntry>();
        query.TextChanged+=value=>{_housingQuery=value;_housingPage=0;Refresh();};
        filter.ItemSelected+=index=>{_housingFilter=(int)index;_housingPage=0;Refresh();};
        reset.Pressed+=()=>{query.Text=_housingQuery="";_housingFilter=_housingPage=0;filter.Select(0);Refresh();};
        previous.Pressed+=()=>{_housingPage--;Refresh();};
        next.Pressed+=()=>{_housingPage++;Refresh();};
        Refresh();_refreshManagementPage+=Refresh;

        void Refresh()
        {
            var homes=HousingDirectoryEntries();
            var vacant=homes.Where(home=>home.Vacant).ToArray();
            overview.Text=$"已完工 {homes.Count(home=>home.Ready)} 栋 · 已入住 {homes.Where(home=>home.Ready).Sum(home=>home.Occupied)} / {homes.Sum(home=>home.Beds)} 床\n"+
                $"可分配空屋 {vacant.Length} 栋 · {vacant.Sum(home=>home.Beds)} 床 · 道路未连通 {homes.Count(home=>home.Ready&&!home.Connected)} 栋";
            string term=_housingQuery.Trim();
            var queried=homes.Where(home=>term.Length==0||new[]{home.Name,home.District,home.Members}
                .Any(value=>value.Contains(term,StringComparison.OrdinalIgnoreCase))).ToArray();
            for(int i=0;i<HousingFilters.Length;i++)filter.SetItemText(i,$"{HousingFilters[i]} · {queried.Count(home=>Match(home,i))}");
            var matching=queried.Where(home=>Match(home,_housingFilter)).ToArray();
            int pages=Math.Max(1,(matching.Length+HousingPageSize-1)/HousingPageSize);
            _housingPage=Math.Clamp(_housingPage,0,pages-1);
            page.Text=$"{matching.Length} / {homes.Length} 栋 · 第 {_housingPage+1} / {pages} 页";
            previous.Disabled=_housingPage==0;next.Disabled=_housingPage==pages-1;
            reset.Disabled=term.Length==0&&_housingFilter==0;
            empty.Visible=matching.Length==0;
            empty.Text=homes.Length==0?"小镇暂无住宅，可从待安置家庭查看建造入口。":"没有符合条件的住宅，可清除关键词或切换筛选。";
            wait();
            var shown=matching.Skip(_housingPage*HousingPageSize).Take(HousingPageSize).ToArray();
            if(rendered.SequenceEqual(shown))return;
            rendered=shown;ClearHousingRows(entries);
            foreach(var home in rendered){
                var row=MarketCard(entries,color:home.Id==_selectedResidence?"e7e5ed":"f4efe7");
                row.GetParent<PanelContainer>().Name="HousingDirectory_"+home.Id;
                MarketLabel(row,$"{home.Name} · {home.District}",16,TownTheme.Ink,wrap:true);
                string occupied=home.Ready?$" · {home.Occupied} / {home.Beds} 床":"";
                string people=home.Members.Length==0?"":$" · {home.Members}";
                MarketLabel(row,$"{home.Status}{occupied}{people}",14,TownTheme.Muted,wrap:true);
                MarketButton(row,"查看住宅",()=>SelectResidence(home.Id),expand:false,accent:"家庭").SizeFlagsHorizontal=Control.SizeFlags.ShrinkBegin;
            }
        }

        static bool Match(HousingEntry home,int filter)=>filter switch{
            1=>home.Occupied>0,2=>home.Vacant,3=>home.Ready&&!home.Connected,4=>!home.Ready,_=>true
        };
    }

    Action AddHousingWaitlist()
    {
        var card=MarketCard(_managementBody!,color:"f1ebdf");
        var toggle=HousingButton(card,"待安置家庭","HousingWaitlistToggle");
        toggle.ToggleMode=true;
        toggle.SetPressedNoSignal(_housingWaitOpen);
        var details=new VBoxContainer{Visible=_housingWaitOpen};card.AddChild(details);
        var picker=new OptionButton{Name="HousingWaitlistFamily",SizeFlagsHorizontal=Control.SizeFlags.ShrinkBegin};
        TownTheme.AccentButton(picker,"家庭",6);details.AddChild(picker);
        var summary=MarketLabel(details,"",15,TownTheme.Ink,wrap:true);summary.Name="HousingWaitlistSummary";
        var actions=new HFlowContainer();actions.AddThemeConstantOverride("h_separation",8);details.AddChild(actions);
        var member=HousingButton(actions,"查看成员名册","HousingWaitlistMember");
        MarketButton(actions,"查看可建住宅",OpenHousingCatalog,expand:false,accent:"建造").Name="HousingWaitlistBuild";
        var matches=new VBoxContainer{Name="HousingWaitlistHomes"};details.AddChild(matches);
        var families=Array.Empty<NativeGame.ResidentHousingWaitGroup>();
        string selectedId="";
        string? signature=null;
        toggle.Toggled+=visible=>{_housingWaitOpen=details.Visible=visible;Refresh();};
        picker.ItemSelected+=index=>{
            if(index>=0&&index<families.Length)selectedId=MemberId(families[(int)index]);
            Render();
        };
        member.Pressed+=()=>{if(selectedId.Length>0)OpenResidentDirectoryEntry(selectedId);};
        return Refresh;

        static string MemberId(NativeGame.ResidentHousingWaitGroup group)=>NativeGame.S(group.Members[0],"person_id");

        void Refresh()
        {
            int waiting=NativeGame.I(Game.Commons,"housing_waiting_families");
            if(!_housingWaitOpen){toggle.Text=$"待安置家庭 · {waiting} 户";return;}
            families=Game.ResidentHousingWaitlist();
            toggle.Text=$"待安置家庭 · {families.Length} 户";
            string next=string.Join("|",families.Select(group=>$"{MemberId(group)}:{string.Join(',',group.Members.Select(person=>NativeGame.S(person,"name")))}:"+
                string.Join(',',group.VacantHomes.Select(home=>NativeGame.S(home,"building_id")))));
            if(signature==next)return;
            signature=next;
            picker.Clear();
            foreach(var group in families)picker.AddItem($"{NativeGame.S(group.Members[0],"name")}一家 · {group.Members.Length} 人");
            int selected=Array.FindIndex(families,group=>MemberId(group)==selectedId);
            if(families.Length>0){selected=Math.Max(0,selected);picker.Select(selected);selectedId=MemberId(families[selected]);}
            else selectedId="";
            Render();
        }

        void Render()
        {
            ClearHousingRows(matches);
            var group=families.FirstOrDefault(row=>MemberId(row)==selectedId);
            picker.Visible=member.Visible=group!=null;
            if(group==null){summary.Text="目前没有等待住宅的家庭。";return;}
            string names=string.Join("、",group.Members.Select(person=>NativeGame.S(person,"name")));
            summary.Text=$"{names} · 需要 {group.Members.Length} 张床 · {group.VacantHomes.Length} 栋空屋符合条件";
            if(group.VacantHomes.Length==0){MarketLabel(matches,"需要一栋能容纳全家且与庄园路网相通的空屋。",14,TownTheme.Muted,wrap:true);return;}
            var options=MarketOptions(matches,group.VacantHomes.Select(home=>$"{ResidenceName(home)} · {Game.ResidentHomeCapacity(home)} 床").ToArray(),0);
            options.Name="HousingWaitlistVacancies";
            MarketButton(matches,"查看所选空屋",()=>{
                if(options.Selected>=0&&options.Selected<group.VacantHomes.Length)SelectResidence(NativeGame.S(group.VacantHomes[options.Selected],"building_id"));
            },expand:false,accent:"家庭").SizeFlagsHorizontal=Control.SizeFlags.ShrinkBegin;
        }
    }

    void OpenHousingCatalog()=>ShowManagement("建造","",()=>{
        _buildWorkspace=2;
        _buildTownId=_buildCatalogQuery="";
        _buildCatalogBudgetOnly=false;
        _buildCatalogCategory=Array.IndexOf(Game.Rules["CATALOG"]!.AsObject()
            .Select(item=>NativeGame.S(item.Value!,"category")).Distinct().ToArray(),"住宅")+1;
    });

    static Button HousingButton(Container parent,string caption,string name,string accent="家庭")
    {
        var button=new Button{Name=name,Text=caption,CustomMinimumSize=new(0,34)};
        TownTheme.AccentButton(button,accent,6);
        parent.AddChild(button);
        return button;
    }

    static void ClearHousingRows(Container rows)
    {
        foreach(var child in rows.GetChildren()){rows.RemoveChild(child);child.QueueFree();}
    }
}
