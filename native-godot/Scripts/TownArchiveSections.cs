using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;

public partial class Main
{
    string _archiveSpeciesQuery="",_archiveSpecies="",_archiveRecordQuery="",_archiveOpenRecord="";
    int _archiveSpeciesFilter,_archiveRecordKind,_archiveRecordStatus,_archiveRecordPage;

    void ArchiveCodex()
    {
        var summary=Section("物种图鉴","收集进度")!;
        var toolbar=ArchiveToolbar(_managementBody!);
        var search=ArchiveSearch(toolbar,"ArchiveSpeciesQuery","搜索物种",_archiveSpeciesQuery);
        var filter=MarketOptions(toolbar,new[]{"全部物种","已收集","待收集","发现稀有","寿终记录"},_archiveSpeciesFilter);
        filter.Name="ArchiveSpeciesFilter";
        var detail=MarketCard(_managementBody!,color:"eee9ef");
        var title=MarketLabel(detail,"",18,TownTheme.Accent("档案"),wrap:true);title.Name="ArchiveSpeciesTitle";
        var facts=MarketLabel(detail,"",14,TownTheme.Muted,wrap:true);facts.Name="ArchiveSpeciesFacts";
        var actions=ArchiveToolbar(detail);
        string example="";
        var seePet=MarketButton(actions,"查看这位伙伴",()=>OpenPetArchive(example),expand:false,accent:"档案");
        seePet.Name="ArchiveSpeciesPet";
        var shop=MarketButton(actions,"查看商店伙伴",()=>ShowManagement("商店","companion-shop:"+_archiveSpecies,
            ()=>_marketSection=0),expand:false,accent:"商店");
        shop.Name="ArchiveSpeciesShop";
        var empty=MarketLabel(_managementBody!,"没有符合条件的物种。",color:TownTheme.Muted);
        var grid=ManagementCardGrid(_managementBody!);
        var choices=new Dictionary<string,Button>(StringComparer.Ordinal);
        (string Name,bool Known,bool Rare,bool Longevity)[]? shown=null;
        search.TextChanged+=value=>{_archiveSpeciesQuery=value;Render();};
        filter.ItemSelected+=value=>{_archiveSpeciesFilter=(int)value;Render();};
        Render();
        _refreshManagementPage+=Render;

        void Render()
        {
            var catalog=Game.Rules["SPECIES"]!.AsObject();
            var codex=Game.State["codex"]!.AsObject();
            var species=catalog.Select(spec=>{
                var entry=codex[spec.Key];
                return (Name:spec.Key,Known:entry!=null,Rare:entry!=null&&NativeGame.B(entry,"rare"),
                    Longevity:entry!=null&&NativeGame.B(entry,"natural_death"));
            }).ToArray();
            summary.Text=$"已收集 {species.Count(s=>s.Known)} / {species.Length} · 稀有 {species.Count(s=>s.Rare)} · 寿终 {species.Count(s=>s.Longevity)}";
            var results=species.Where(s=>ArchiveMatches(_archiveSpeciesQuery,s.Name)&&(_archiveSpeciesFilter switch{
                1=>s.Known,2=>!s.Known,3=>s.Rare,4=>s.Longevity,_=>true})).ToArray();
            if(!results.Any(s=>s.Name==_archiveSpecies))_archiveSpecies=results.FirstOrDefault().Name??"";
            empty.Visible=results.Length==0;
            detail.GetParent<Control>().Visible=results.Length>0;
            if(shown==null||!shown.SequenceEqual(results)){
                shown=results;
                ClearArchiveRows(grid);
                choices.Clear();
                foreach(var speciesRow in results){
                    var card=MarketCard(grid,164,speciesRow.Known?"eef1e6":"f4ede4");
                    card.AddChild(AnimalBadge(speciesRow.Name,138,78));
                    var open=MarketButton(card,speciesRow.Known?"已收集 · 查看":"待收集 · 查看",()=>{
                        _archiveSpecies=speciesRow.Name;Render();
                        _=RevealManagementSection(detail.GetParent<Control>());
                    },accent:"档案");
                    open.Name="ArchiveSpecies_"+speciesRow.Name;
                    open.ToggleMode=true;
                    choices.Add(speciesRow.Name,open);
                }
            }
            foreach(var (name,button) in choices)button.SetPressedNoSignal(name==_archiveSpecies);
            if(results.Length==0)return;
            var selected=results.First(s=>s.Name==_archiveSpecies);
            var spec=catalog[selected.Name]!;
            var pets=Data.Pets.OfType<JsonNode>().Where(p=>NativeGame.S(p,"species")==selected.Name).ToArray();
            var first=pets.OrderByDescending(Game.Active).ThenByDescending(p=>NativeGame.D(p,"birth_ts")).FirstOrDefault();
            example=first==null?"":NativeGame.S(first,"pet_id");
            title.Text=selected.Name+(selected.Known?" · 已收集":" · 待收集");
            facts.Text=$"适配居所 · {NativeGame.S(spec,"building")} · 在园 {pets.Count(Game.Active)} · 档案 {pets.Length}"
                +$"\n稀有外观{(selected.Rare?"已发现":"待发现")} · 寿终记录{(selected.Longevity?"已收录":"暂无")}";
            seePet.Visible=example.Length>0;
            if(first!=null)seePet.Text="查看 "+NativeGame.S(first,"name");
        }
    }

    readonly record struct ArchiveRecord(string Id,string Kind,string Status,string Title,string Details,double Time,string DateLabel);

    void ArchiveTownRecords()
    {
        Section("小镇记录","已结束的订单和事件 · 游戏时间 UTC");
        var toolbar=ArchiveToolbar(_managementBody!);
        var search=ArchiveSearch(toolbar,"ArchiveRecordQuery","搜索委托人、物资或事件",_archiveRecordQuery);
        var kind=MarketOptions(toolbar,new[]{"全部类别","订单","事件"},_archiveRecordKind);kind.Name="ArchiveRecordKind";
        var status=MarketOptions(toolbar,new[]{"全部状态","完成","过期"},_archiveRecordStatus);status.Name="ArchiveRecordStatus";
        var pager=ArchivePager("ArchiveRecords");
        var list=new VBoxContainer{Name="ArchiveRecordRows"};
        _managementBody!.AddChild(list);
        ArchiveRecord[]? shown=null;
        search.TextChanged+=value=>{_archiveRecordQuery=value;_archiveRecordPage=0;Render();};
        kind.ItemSelected+=value=>{_archiveRecordKind=(int)value;_archiveRecordPage=0;Render();};
        status.ItemSelected+=value=>{_archiveRecordStatus=(int)value;_archiveRecordPage=0;Render();};
        pager.Previous.Pressed+=()=>{_archiveRecordPage--;Render();};
        pager.Next.Pressed+=()=>{_archiveRecordPage++;Render();};
        Render();
        _refreshManagementPage+=Render;

        void Render()
        {
            var records=Game.Rows("order_archive").Concat(Game.Rows("orders")).OfType<JsonObject>()
                .Where(row=>NativeGame.S(row,"status")!="进行中").Select(row=>ReadArchiveRecord(row,true))
                .Concat(Game.Rows("event_archive").Concat(Game.Rows("events")).OfType<JsonObject>()
                    .Where(row=>NativeGame.S(row,"status")!="待处理").Select(row=>ReadArchiveRecord(row,false)));
            var results=records.Where(row=>(_archiveRecordKind==0||row.Kind==kind.GetItemText(_archiveRecordKind))&&
                (_archiveRecordStatus==0||row.Status==status.GetItemText(_archiveRecordStatus))&&
                ArchiveMatches(_archiveRecordQuery,row.Title+" "+row.Details))
                .OrderByDescending(row=>row.Time).ThenBy(row=>row.Id,StringComparer.Ordinal).ToArray();
            _archiveRecordPage=pager.Update(results.Length,_archiveRecordPage);
            var page=results.Skip(_archiveRecordPage*ArchivePageSize).Take(ArchivePageSize).ToArray();
            if(shown!=null&&shown.SequenceEqual(page))return;
            shown=page;
            ClearArchiveRows(list);
            (Button Button,Label Details)? expanded=null;
            if(page.Length==0)MarketLabel(list,"没有符合条件的小镇记录。",color:TownTheme.Muted);
            foreach(var record in page){
                var card=MarketCard(list,color:record.Kind=="订单"?"eaf0e7":"f2eae3");
                MarketLabel(card,$"{record.Kind} · {record.Status} · {record.DateLabel}",14,TownTheme.Muted,wrap:true);
                MarketLabel(card,record.Title,16,TownTheme.Ink,wrap:true);
                var toggle=new Button{Name="ArchiveRecord_"+record.Id,Text="查看明细",ToggleMode=true,
                    CustomMinimumSize=new(0,38),SizeFlagsHorizontal=Control.SizeFlags.ShrinkBegin};
                TownTheme.AccentButton(toggle,"档案");card.AddChild(toggle);
                toggle.SetPressedNoSignal(_archiveOpenRecord==record.Id);
                var details=MarketLabel(card,record.Details,14,TownTheme.Ink,wrap:true);
                details.Name="ArchiveRecordDetails_"+record.Id;
                details.Visible=toggle.ButtonPressed;
                if(toggle.ButtonPressed){expanded=(toggle,details);toggle.Text="收起明细";}
                toggle.Toggled+=on=>{
                    if(on&&expanded is { } previous&&previous.Button!=toggle){
                        previous.Button.SetPressedNoSignal(false);
                        previous.Button.Text="查看明细";
                        previous.Details.Hide();
                    }
                    _archiveOpenRecord=on?record.Id:"";
                    expanded=on?(toggle,details):null;
                    details.Visible=on;
                    toggle.Text=on?"收起明细":"查看明细";
                };
            }
        }
    }

    static ArchiveRecord ReadArchiveRecord(JsonObject row,bool order)
    {
        string state=NativeGame.S(row,"status","已结束");
        double created=NativeGame.D(row,"created_ts"),resolved=NativeGame.D(row,"resolved_ts"),expiry=NativeGame.D(row,"expiry_ts");
        double date=resolved>0?resolved:state=="过期"&&expiry>0?expiry:created;
        string caption=date<=0?"日期未记录":(resolved>0?"结算 ":state=="过期"&&expiry>0?"截止 ":"发布 ")+Stamp(date);
        string title=order?$"{NativeGame.S(row,"npc","邻里")} · {NativeGame.OrderRequestCaption(row)}":NativeGame.S(row,"text","小镇事件");
        string details=order?(state=="完成"?$"订单奖励 {NativeGame.I(row,"reward")} 金币 · {NativeGame.I(row,"reputation")} 声望":"订单未交付"):
            "处理方式 · "+ReadEventChoice(row);
        if(created>0&&date!=created)details+="\n发布 · "+Stamp(created);
        if(resolved<=0&&state=="完成")details+="\n结算时间未记录";
        string id=NativeGame.S(row,order?"order_id":"event_id");
        return new((order?"order_":"event_")+id,order?"订单":"事件",state,title,details,date,caption);
    }

    static string ReadEventChoice(JsonObject row)
    {
        int index=NativeGame.I(row,"resolved_option",-1);
        if(row["options"] is not JsonArray options||index<0||index>=options.Count)return "未记录";
        string choice=options[index]?.GetValue<string>()??"未记录";
        return NativeGame.S(row,"status")=="过期"?"到期自动 · "+choice:choice;
    }
}
