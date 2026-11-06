using Godot;
using System;
using System.Linq;
using System.Text.Json.Nodes;

public partial class Main
{
    readonly record struct HouseholdMemberRow(string Id,Label Name,Label Status,Label Relations,Button Locate);

    void HouseholdResidenceControls(JsonNode selected)
    {
        string id=NativeGame.S(selected,"building_id");
        var card=MarketCard(_managementBody!,color:"eee7ef");
        _managementFocusTarget=card.GetParent<Control>();
        MarketLabel(card,ResidenceName(selected),19,TownTheme.Leaf);
        var capacity=MarketLabel(card,"",15,TownTheme.Ink,wrap:true);capacity.Name="HouseholdOccupancy";
        var access=MarketLabel(card,"",14,TownTheme.Muted,wrap:true);access.Name="HouseholdEntrance";
        var actions=new HFlowContainer();actions.AddThemeConstantOverride("h_separation",7);card.AddChild(actions);
        var locate=HousingButton(actions,"定位这栋房屋","HouseholdLocate","地图");
        var entrance=HousingButton(actions,"定位实际门口","HouseholdLocateEntrance","地图");
        var map=HousingButton(actions,"查看周边地图","HouseholdEntranceMap","地图");
        var cutaway=HousingButton(actions,"查看室内切面","HouseholdInterior");
        MarketButton(actions,"返回住房总览",()=>ShowManagement("家庭","residence"),expand:false,accent:"家庭");
        var header=Section("家庭成员","每日 20:00 后返家，次日 07:00 出门");
        var rows=new VBoxContainer{Name="HouseholdMemberRows"};_managementBody!.AddChild(rows);
        var rendered=Array.Empty<HouseholdMemberRow>();
        locate.Pressed+=()=>{
            if(Game.FindTownBuilding(id) is not JsonNode home)return;
            FocusHouseholdSite(TownProjectCenter(home),55,$"{ResidenceName(home)} · 已定位住宅");
        };
        entrance.Pressed+=()=>{
            if(Game.FindTownBuilding(id) is not JsonNode home||!CanShowResidenceInterior(home)||Game.HomeRoadEntrance(home) is not NativeGame.Cell cell)return;
            FocusHouseholdSite(ResidenceDoor(home,cell),26,$"{ResidenceName(home)} · 已定位门口\n连通道路格 {cell.X}, {cell.Y}");
        };
        map.Pressed+=()=>{
            if(Game.FindTownBuilding(id) is not JsonNode home)return;
            var cell=Game.HomeRoadEntrance(home)??new NativeGame.Cell(NativeGame.I(home,"cx"),NativeGame.I(home,"cy"));
            ShowManagement("地图",$"cell:{cell.X},{cell.Y}");
        };
        cutaway.Pressed+=()=>{
            if(Game.FindTownBuilding(id) is JsonNode home&&CanShowResidenceInterior(home))
                ShowManagement("家庭","interior:"+id,()=>_selectedResidence=id);
        };
        Refresh();_refreshManagementPage+=Refresh;

        void Refresh()
        {
            var home=Game.FindTownBuilding(id);
            bool present=home!=null,ready=present&&CanShowResidenceInterior(home!);
            var cell=ready?Game.HomeRoadEntrance(home!):null;
            locate.Disabled=map.Disabled=!present;
            cutaway.Visible=ready;
            entrance.Disabled=cell==null;
            entrance.TooltipText=cell==null?"需要住宅完工并接通道路":"镜头移至居民实际使用的入口";
            access.Text=!present?"该住宅已移除。":!ready?"施工完成后可查看住宅入口。":cell is NativeGame.Cell linked
                ?$"道路通达 · 入口连通格 {linked.X}, {linked.Y}":"道路未连通 · 在地图检查附近道路。";
            map.Text=cell==null?"检查周边道路":"查看入口地图";
            var members=present?Game.HomeMembers(id).ToArray():Array.Empty<JsonNode>();
            var inside=ready?ResidenceOccupants(id).Length:0;
            capacity.Text=ready?$"已入住 {members.Length} / {Game.ResidentHomeCapacity(home!)} 床 · 现在 {inside} 位在家":"住宅尚不可入住";
            if(header!=null)header.Text=$"{members.Length} 位成员 · {inside} 位在家";
            if(!rendered.Select(row=>row.Id).SequenceEqual(members.Select(person=>NativeGame.S(person,"person_id")))||rows.GetChildCount()==0){
                ClearHousingRows(rows);
                rendered=members.Select(BuildMember).ToArray();
                if(rendered.Length==0)MarketLabel(rows,"尚无家庭成员入住。",14,TownTheme.Muted,wrap:true);
            }
            for(int i=0;i<rendered.Length;i++){
                var row=rendered[i];var person=members[i];
                var entry=DescribeDirectoryPerson(person,DirectoryActor(row.Id));
                row.Name.Text=$"{entry.Name} · {NativeGame.S(person,"sex")} · {ResidentAge(person)}";
                row.Status.Text=$"{entry.Profession} · {ResidentPlaceCaption(entry.Place)} · {entry.Activity}";
                row.Locate.Disabled=!entry.Position.HasValue;
                row.Locate.TooltipText=row.Locate.Disabled?entry.Activity:$"前往 {ResidentCurrentSite(entry)}";
                row.Relations.Text=HouseholdRelations(person);
                row.Relations.Visible=row.Relations.Text.Length>0;
            }
        }

        HouseholdMemberRow BuildMember(JsonNode person)
        {
            string member=NativeGame.S(person,"person_id");
            var row=MarketCard(rows,color:"f4efe7");
            row.GetParent<PanelContainer>().Name="HouseholdMember_"+member;
            var name=MarketLabel(row,"",16,TownTheme.Ink,wrap:true);
            var status=MarketLabel(row,"",14,TownTheme.Muted,wrap:true);
            var relations=MarketLabel(row,"",14,TownTheme.Muted,wrap:true);
            var buttons=new HFlowContainer();buttons.AddThemeConstantOverride("h_separation",7);row.AddChild(buttons);
            MarketButton(buttons,"查看名册",()=>OpenResidentDirectoryEntry(member),expand:false,accent:"家庭").Name="HouseholdProfile";
            var location=MarketButton(buttons,"定位成员",()=>LocateResident(member),expand:false,accent:"地图");location.Name="HouseholdLocateMember";
            return new(member,name,status,relations,location);
        }

        void FocusHouseholdSite(Vector3 point,float distance,string message)
        {
            ClearSceneResidentSelection();
            FocusSceneInspection("town_building",id);
            FocusResidentSite(point,distance,message);
        }
    }

    string HouseholdRelations(JsonNode person)
    {
        var spouse=Resident(NativeGame.S(person,"partner_id"));
        string partner=spouse==null?"":"配偶 · "+NativeGame.S(spouse,"name");
        string parents=person["parents"] is JsonArray ids?string.Join("、",ids.Where(id=>id!=null).Select(id=>Resident(id!.ToString()))
            .OfType<JsonNode>().Select(parent=>NativeGame.S(parent,"name"))):"";
        return string.Join("  /  ",new[]{partner,parents.Length==0?"":"父母 · "+parents}.Where(value=>value.Length>0));
    }
}
