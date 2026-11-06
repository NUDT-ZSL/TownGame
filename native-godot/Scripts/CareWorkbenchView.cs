using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;

public partial class Main
{
    readonly record struct CareWorkRow(string Id,string Name,int Category,string Detail,string Target,string Action,double Severity);
    bool _careWorkbenchClinic;
    int _careTriageFilter,_careClinicFilter;

    void AddCareWorkbench()
    {
        var card=MarketCard(_managementBody!,color:"e8f0e8");
        var panel=card.GetParent<PanelContainer>();
        panel.Name="CareWorkbench";
        if(_managementEntryTarget=="clinic-board")_careWorkbenchClinic=true;
        if(_managementEntryTarget=="care-worklist")_careWorkbenchClinic=false;
        if(_managementEntryTarget is "clinic-board" or "care-worklist")_managementFocusTarget=panel;
        var tabs=new HFlowContainer();
        tabs.AddThemeConstantOverride("h_separation",6);
        card.AddChild(tabs);
        var work=new Button{Name="CareWorkbenchTriage",Text="优先照护",ToggleMode=true};
        var clinic=new Button{Name="CareWorkbenchClinic",Text="诊疗台",ToggleMode=true};
        TownTheme.AccentButton(work,"照护",6);
        TownTheme.AccentButton(clinic,"事件",6);
        tabs.AddChild(work);tabs.AddChild(clinic);

        var triage=new VBoxContainer{Name="CareTriageBoard",Visible=!_careWorkbenchClinic};
        card.AddChild(triage);
        MarketLabel(triage,"按健康、饱食和清洁找出需要优先处理的伙伴。",14,TownTheme.Muted,wrap:true);
        var refreshTriage=AddCareWorkList(triage,false,new[]{"全部","健康与伤病","需喂食","需洗澡"},CareTriageRows,
            "这个分类暂无优先照护事项。");

        var medical=new VBoxContainer{Name="CareClinicBoard",Visible=_careWorkbenchClinic};
        card.AddChild(medical);
        var beds=MarketLabel(medical,"",14,TownTheme.Muted,wrap:true);
        beds.Name="CareClinicBeds";
        var build=MarketButton(medical,"查看庄园建筑",()=>ShowManagement("建造","manor"),expand:false,accent:"建造");
        build.SizeFlagsHorizontal=Control.SizeFlags.ShrinkBegin;
        var refreshClinic=AddCareWorkList(medical,true,new[]{"全部","待接诊","住院治疗","待出院"},CareClinicRows,
            "这个分类暂无诊疗记录。");
        work.Pressed+=()=>SelectBoard(false);
        clinic.Pressed+=()=>SelectBoard(true);
        SelectBoard(_careWorkbenchClinic);
        _refreshManagementPage+=Refresh;

        void SelectBoard(bool showClinic)
        {
            _careWorkbenchClinic=showClinic;
            work.SetPressedNoSignal(!showClinic);
            clinic.SetPressedNoSignal(showClinic);
            triage.Visible=!showClinic;
            medical.Visible=showClinic;
            Refresh();
        }

        void Refresh()
        {
            if(!medical.Visible){refreshTriage();return;}
            var hospitals=Game.PetHospitalBeds();
            build.Visible=hospitals.Length==0;
            if(hospitals.Length==0)beds.Text="庄园暂无宠物医院，可在建造页新增。";
            else{
                var occupants=Data.Pets.OfType<JsonNode>().Where(Game.Active).ToLookup(pet=>NativeGame.I(pet,"building_id"));
                string header=$"空闲床位 {hospitals.Sum(hospital=>Math.Max(0,hospital.Total-hospital.Used))} / {hospitals.Sum(hospital=>hospital.Total)}";
                beds.Text=header+"\n"+string.Join("\n",hospitals.Select(hospital=>{
                    var names=occupants[hospital.Plot].Select(pet=>NativeGame.S(pet,"name")).ToArray();
                    return $"地块 {hospital.Plot} · {hospital.Level} 级 · 已占 {hospital.Used} / {hospital.Total}"+
                        (names.Length==0?" · 暂无住院伙伴":" · "+string.Join("、",names));
                }));
            }
            refreshClinic();
        }
    }

    Action AddCareWorkList(VBoxContainer parent,bool clinic,string[] categories,Func<IEnumerable<CareWorkRow>> entries,string emptyText)
    {
        string prefix=clinic?"CareClinic":"CareTriage";
        const int pageSize=3;
        int page=0;
        var bar=new HFlowContainer();
        bar.AddThemeConstantOverride("h_separation",6);
        parent.AddChild(bar);
        int filterValue=Math.Clamp(clinic?_careClinicFilter:_careTriageFilter,0,categories.Length-1);
        var filter=MarketOptions(bar,categories,filterValue);
        filter.Name=prefix+"Filter";
        var previous=new Button{Name=prefix+"Previous",Text="上一页"};
        var next=new Button{Name=prefix+"Next",Text="下一页"};
        TownTheme.AccentButton(previous,"照护",5);
        TownTheme.AccentButton(next,"照护",5);
        bar.AddChild(previous);
        var count=MarketLabel(bar,"",14,TownTheme.Muted);
        count.VerticalAlignment=VerticalAlignment.Center;
        count.Name=prefix+"Count";
        bar.AddChild(next);
        var empty=MarketLabel(parent,emptyText,14,TownTheme.Muted,wrap:true);
        var slots=Enumerable.Range(0,pageSize).Select(AddRow).ToArray();
        filter.ItemSelected+=value=>{
            filterValue=(int)value;
            if(clinic)_careClinicFilter=filterValue;else _careTriageFilter=filterValue;
            page=0;Refresh();
        };
        previous.Pressed+=()=>{page--;Refresh();};
        next.Pressed+=()=>{page++;Refresh();};
        return Refresh;

        Action<CareWorkRow?> AddRow(int index)
        {
            var card=MarketCard(parent,color:"fffdf7");
            var panel=card.GetParent<PanelContainer>();
            var row=new HFlowContainer();
            row.AddThemeConstantOverride("h_separation",6);
            card.AddChild(row);
            var text=MarketLabel(row,"",14,TownTheme.Ink,wrap:true);
            text.SizeFlagsHorizontal=Control.SizeFlags.ExpandFill;
            text.CustomMinimumSize=new(230,0);
            text.VerticalAlignment=VerticalAlignment.Center;
            var open=new Button{Name=prefix+"Open"+index,Text="查看",CustomMinimumSize=new(0,32)};
            TownTheme.AccentButton(open,"照护",6);
            row.AddChild(open);
            CareWorkRow? current=null;
            open.Pressed+=()=>{if(current is { } entry)OpenPetManagement(entry.Id,entry.Target);};
            return value=>{
                current=value;
                panel.Visible=value.HasValue;
                if(value is not { } entry)return;
                text.Text=$"{entry.Name} · {entry.Detail}";
                open.Text=entry.Action;
            };
        }

        void Refresh()
        {
            var all=entries().OrderBy(row=>row.Category).ThenBy(row=>row.Severity).ToArray();
            var chosen=filterValue==0?all:all.Where(row=>row.Category==filterValue).ToArray();
            for(int i=0;i<categories.Length;i++)filter.SetItemText(i,$"{categories[i]} · {(i==0?all.Length:all.Count(row=>row.Category==i))}");
            int pages=Math.Max(1,(chosen.Length+pageSize-1)/pageSize);
            page=Math.Clamp(page,0,pages-1);
            count.Text=$"{chosen.Length} 项 · {page+1} / {pages} 页";
            previous.Visible=next.Visible=pages>1;
            previous.Disabled=page==0;
            next.Disabled=page+1>=pages;
            empty.Visible=chosen.Length==0;
            for(int i=0;i<slots.Length;i++){
                int position=page*pageSize+i;
                slots[i](position<chosen.Length?chosen[position]:null);
            }
        }
    }

    IEnumerable<CareWorkRow> CareTriageRows()
    {
        foreach(var pet in Data.Pets.OfType<JsonNode>().Where(pet=>Game.PetCarePriority(pet).HasValue)){
            string id=NativeGame.S(pet,"pet_id"),name=NativeGame.S(pet,"name");
            double health=NativeGame.D(pet,"health"),hunger=NativeGame.D(pet,"hunger"),clean=NativeGame.D(pet,"cleanliness");
            if(Game.NeedsPetTreatment(pet))yield return new(id,name,1,$"伤病 · 健康 {health:0}","clinic","查看诊疗",health);
            else if(health<60)yield return new(id,name,1,$"健康偏低 · {health:0}","care:health","查看记录",health);
            if(hunger<=45)yield return new(id,name,2,WithReason($"饱食 {hunger:0}",PetCareBlockReason(pet,"feed")),"care:feed","前往喂食",hunger);
            if(clean<=40)yield return new(id,name,3,WithReason($"清洁 {clean:0}",PetCareBlockReason(pet,"bath")),"care:bath","前往洗澡",clean);
        }
    }

    IEnumerable<CareWorkRow> CareClinicRows()
    {
        foreach(var pet in Data.Pets.OfType<JsonNode>().Where(Game.Active)){
            bool inpatient=Game.IsPetInHospital(pet),needs=Game.NeedsPetTreatment(pet);
            if(!inpatient&&(!needs||NativeGame.B(pet,"fostered")))continue;
            string id=NativeGame.S(pet,"pet_id"),name=NativeGame.S(pet,"name");
            double health=NativeGame.D(pet,"health");
            if(!inpatient){
                var admission=Game.PetHospitalAdmission(pet);
                string detail=admission.Destination is { } hospital?$"可接诊 · 地块 {NativeGame.I(hospital,"building_id")}":
                    admission.HasHospital?$"待空床 · 当前空闲 {admission.FreeBeds} 张":"暂无宠物医院";
                yield return new(id,name,1,detail,"clinic","查看入院",health);
            }else if(needs){
                string reason=PetCareBlockReason(pet,"treat");
                yield return new(id,name,2,WithReason($"地块 {NativeGame.I(pet,"building_id")} · 住院中",reason.Length==0?"可治疗":reason),
                    "care:treat","查看治疗",health);
            }else{
                var home=Game.PetDischargeHome(pet);
                string detail=NativeGame.B(pet,"fostered")?"已康复 · 先接回寄养伙伴":home==null?"已康复 · 等待适配居所":
                    $"可出院 · 将入住地块 {NativeGame.I(home,"building_id")}";
                yield return new(id,name,3,detail,"clinic","查看出院",health);
            }
        }
    }

    static string WithReason(string value,string reason)=>reason.Length==0?value:$"{value} · {reason}";
}
