using Godot;
using System;
using System.Linq;
using System.Text.Json.Nodes;

public partial class Main
{
    static (Control Panel,Label District,Label Site,Label Host,Label Admission) AddFestivalChecklist(Control parent)
    {
        var card=MarketCard(parent,color:"f0ece0");
        var panel=card.GetParent<Control>();
        panel.Name="TownFestivalChecklist";
        return (panel,Add("District"),Add("Site"),Add("Host"),Add("Admission"));

        Label Add(string name)
        {
            var label=MarketLabel(card,"",14,TownTheme.Muted,wrap:true);
            label.Name="TownFestivalCheck"+name;
            return label;
        }
    }

    static void SetFestivalCheck(Label label,string caption,bool ready,string detail)
    {
        label.Text=$"{(ready?"✓":"○")} {caption} · {detail}";
        label.AddThemeColorOverride("font_color",ready?TownTheme.Leaf:TownTheme.Muted);
    }

    (Action<bool> Refresh,Func<string> SelectedId,Func<string> SelectedBlock,OptionButton Picker,CheckButton Toggle)
        AddFestivalCandidates(Control parent)
    {
        var card=MarketCard(parent,color:"ebe8ef");
        var toggle=new CheckButton{Name="TownFestivalCandidateToggle",Text="查看展示候选",ButtonPressed=_festivalShowCandidates};
        TownTheme.AccentButton(toggle,"外观",6);card.AddChild(toggle);
        var content=new VBoxContainer();card.AddChild(content);
        var summary=MarketLabel(content,"",16,TownTheme.Ink,wrap:true);
        summary.Name="TownFestivalCandidateSummary";
        var row=FestivalActions(content);
        var chooser=MarketOptions(row,Array.Empty<string>(),-1);
        chooser.Name="TownFestivalCandidates";
        chooser.AccessibilityName="选择展示伙伴，查看报名条件";
        chooser.FitToLongestItem=false;
        chooser.TextOverrunBehavior=TextServer.OverrunBehavior.TrimEllipsis;
        chooser.CustomMinimumSize=new(205,36);
        chooser.SizeFlagsHorizontal=Control.SizeFlags.ExpandFill;
        var inspect=MarketButton(row,"查看伙伴",()=>{},expand:false,accent:"宠物");
        inspect.Name="TownFestivalCandidateProfile";
        var condition=MarketLabel(content,"",14,TownTheme.Muted,wrap:true);
        condition.Name="TownFestivalCandidateReason";
        (string Id,string Caption,string Block)[] candidates=Array.Empty<(string,string,string)>();
        toggle.Toggled+=enabled=>_festivalShowCandidates=enabled;
        inspect.Pressed+=()=>{
            if(Data.FindPet(SelectedId()) is not JsonNode pet)return;
            string focus=Game.IsPetInHospital(pet)||Game.NeedsPetTreatment(pet)?"clinic":"";
            OpenPetManagement(NativeGame.S(pet,"pet_id"),focus);
        };
        return (Refresh,SelectedId,SelectedBlock,chooser,toggle);

        bool IsSelected()=>chooser.Selected>=0&&chooser.Selected<candidates.Length;
        string SelectedId()=>IsSelected()?candidates[chooser.Selected].Id:"";
        string SelectedBlock()=>IsSelected()?candidates[chooser.Selected].Block:"暂无可选伙伴";

        void Refresh(bool liveShowcase)
        {
            toggle.Visible=!liveShowcase;
            content.Visible=liveShowcase||_festivalShowCandidates;
            if(!content.Visible)return;
            var options=Data.Pets.OfType<JsonNode>().Where(Game.Active).Select(pet=>{
                string reason=Game.FestivalShowPetBlock(pet);
                return (Id:NativeGame.S(pet,"pet_id"),Caption:$"{NativeGame.S(pet,"name")} · {NativeGame.S(pet,"species")} · {(reason.Length==0?"可参加":"待准备")}",Block:reason);
            }).OrderBy(pet=>pet.Block.Length>0).ToArray();
            if(!candidates.SequenceEqual(options)){
                string previous=IsSelected()?SelectedId():_selectedPet;
                candidates=options;
                chooser.Clear();
                foreach(var candidate in candidates)chooser.AddItem(candidate.Caption);
                if(candidates.Length>0)chooser.Select(Math.Max(0,Array.FindIndex(candidates,candidate=>candidate.Id==previous)));
            }
            summary.Text=$"展示候选 · {candidates.Count(candidate=>candidate.Block.Length==0)} / {candidates.Length} 位符合条件";
            row.Visible=candidates.Length>0;
            string block=SelectedBlock();
            condition.Text=block.Length==0?"已满足报名条件，开场后仍需到场登记。":block;
            condition.AddThemeColorOverride("font_color",block.Length==0?TownTheme.Leaf:TownTheme.Muted);
        }
    }

    Action AddFestivalRewards(Control parent)
    {
        var card=MarketCard(parent,color:"edf0e3");
        MarketLabel(card,"活动奖励",17,TownTheme.Leaf);
        MarketLabel(card,"每次完成活动可获得一次对应装饰的免费摆放机会。",14,TownTheme.Muted,wrap:true);
        var balances=NativeGame.FestivalDecorationKinds.Select(kind=>{
            var row=FestivalActions(card);
            var stock=MarketLabel(row,"",15,TownTheme.Ink,wrap:true);
            stock.Name="TownFestivalCredit_"+kind;
            stock.VerticalAlignment=VerticalAlignment.Center;
            var open=MarketButton(row,"查看装饰",()=>ShowManagement("建造","decor:"+kind,()=>_buildWorkspace=3),expand:false,accent:"建造");
            open.Name="TownFestivalOpenDecoration_"+kind;
            return (Kind:kind,Stock:stock,Open:open);
        }).ToArray();
        var toggle=new CheckButton{Name="TownFestivalHistoryToggle",Text="最近活动记录",ButtonPressed=_festivalShowHistory};
        TownTheme.AccentButton(toggle,"事件",6);card.AddChild(toggle);
        var history=new VBoxContainer{Name="TownFestivalHistory",Visible=_festivalShowHistory};card.AddChild(history);
        NativeGame.FestivalCompletion[]? shown=null;
        toggle.Toggled+=enabled=>{_festivalShowHistory=enabled;history.Visible=enabled;Refresh();};
        return Refresh;

        void Refresh()
        {
            foreach(var (kind,stock,open) in balances){
                stock.Text=$"{Game.DecorationName(kind)} · 可免费摆放 {Game.FestivalDecorationCredits(kind)} 件";
                open.Disabled=Game.Rules["DECORATIONS"]?[kind]==null;
            }
            if(!history.Visible)return;
            var records=Game.FestivalCompletions();
            if(shown!=null&&shown.SequenceEqual(records))return;
            shown=records;
            foreach(var child in history.GetChildren()){history.RemoveChild(child);child.QueueFree();}
            if(records.Length==0)MarketLabel(history,"暂无带明细的完成记录。",14,TownTheme.Muted);
            foreach(var entry in records){
                var item=MarketCard(history,color:"f7f3e8");
                MarketLabel(item,entry.Title+" · "+TownProjectClock(entry.At),16,TownTheme.Ink,wrap:true);
                MarketLabel(item,entry.Contribution+" · 获得 "+Game.DecorationName(entry.Decoration),14,TownTheme.Muted,wrap:true);
            }
        }
    }

}
