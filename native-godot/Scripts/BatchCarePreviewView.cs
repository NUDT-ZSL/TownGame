using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;

public partial class Main
{
    Action<string,string> CreateBatchCarePreview()
    {
        var panel=new PanelContainer{Name="BatchCarePreview",Visible=false};
        panel.AddThemeStyleboxOverride("panel",TownTheme.Box("eaf0e6",12,10));
        _managementBody!.AddChild(panel);
        var body=new VBoxContainer();
        body.AddThemeConstantOverride("separation",6);
        panel.AddChild(body);
        var title=MarketLabel(body,"批量照护预览",18,TownTheme.Leaf);
        var summary=MarketLabel(body,"",color:TownTheme.Muted,wrap:true);
        var scope=new CheckButton{Name="BatchCareChooseCompanions",Text="只处理勾选的伙伴"};
        TownTheme.AccentButton(scope,"照护",5);
        body.AddChild(scope);
        var chooser=new VBoxContainer{Visible=false};
        body.AddChild(chooser);
        var shortcuts=new HFlowContainer();
        chooser.AddChild(shortcuts);
        var selectAll=new Button{Text="全选",Name="BatchCareSelectAll"};
        var selectNone=new Button{Text="清空选择",Name="BatchCareSelectNone"};
        TownTheme.AccentButton(selectAll,"照护",5);
        TownTheme.AccentButton(selectNone,"存档",5);
        shortcuts.AddChild(selectAll);
        shortcuts.AddChild(selectNone);
        var scroll=new ScrollContainer{CustomMinimumSize=new(0,96),HorizontalScrollMode=ScrollContainer.ScrollMode.Disabled};
        chooser.AddChild(scroll);
        var candidates=new VBoxContainer{SizeFlagsHorizontal=Control.SizeFlags.ExpandFill};
        scroll.AddChild(candidates);
        var selected=new HashSet<string>(StringComparer.Ordinal);
        var choices=new Dictionary<string,CheckButton>(StringComparer.Ordinal);
        var rows=new RichTextLabel{Name="BatchCarePreviewList",CustomMinimumSize=new(0,132),FitContent=false};
        rows.AddThemeFontSizeOverride("normal_font_size",16);
        rows.AddThemeColorOverride("default_color",TownTheme.Ink);
        body.AddChild(rows);
        var actions=new HFlowContainer();
        actions.AddThemeConstantOverride("h_separation",8);
        body.AddChild(actions);
        NativeGame.BatchCarePlan? current=null;
        string kind="";
        var confirm=MarketButton(actions,"确认照护",()=>{
            if(current is not { } plan||!plan.Rows.Any(row=>row.Skip.Length==0))return;
            var args=new JsonObject{{"kind",plan.Kind},{"plan",plan.Token}};
            if(scope.ButtonPressed)args["pet_ids"]=new JsonArray(plan.Rows.Select(row=>(JsonNode?)JsonValue.Create(row.Id)).ToArray());
            Act("batch_care",args);
        },expand:false,accent:"照护");
        confirm.Name="ConfirmBatchCare";
        MarketButton(actions,"刷新预览",()=>{RefreshCandidates(false);RefreshPlan();},expand:false,accent:"照护");
        MarketButton(actions,"取消",panel.Hide,expand:false,accent:"存档");
        scope.Toggled+=enabled=>{chooser.Visible=enabled;RefreshPlan();};
        selectAll.Pressed+=()=>SelectAll(true);
        selectNone.Pressed+=()=>SelectAll(false);
        return Show;

        void Show(string label,string action)
        {
            kind=action;
            scope.SetPressedNoSignal(false);
            chooser.Hide();
            RefreshCandidates(true);
            title.Text=$"{label}预览";
            panel.Show();
            RefreshPlan();
            Callable.From(()=>{
                if(GodotObject.IsInstanceValid(title)&&title.IsVisibleInTree())_managementScroll?.EnsureControlVisible(title);
            }).CallDeferred();
        }

        void RefreshCandidates(bool initial)
        {
            foreach(var child in candidates.GetChildren()){candidates.RemoveChild(child);child.QueueFree();}
            choices.Clear();
            var pets=Data.Pets.OfType<JsonNode>().Where(Game.Active).ToArray();
            if(initial)selected.Clear();
            else selected.IntersectWith(pets.Select(pet=>NativeGame.S(pet,"pet_id")));
            foreach(var pet in pets){
                string id=NativeGame.S(pet,"pet_id");
                if(initial)selected.Add(id);
                var choice=new CheckButton{Name=$"BatchCarePet_{id}",ButtonPressed=selected.Contains(id),
                    Text=$"{NativeGame.S(pet,"name")} · {PetLocationStatus(pet)}"};
                TownTheme.AccentButton(choice,"照护",4);
                choices[id]=choice;
                candidates.AddChild(choice);
                choice.Toggled+=pressed=>{
                    if(pressed)selected.Add(id);else selected.Remove(id);
                    RefreshPlan();
                };
            }
        }

        void SelectAll(bool included)
        {
            selected.Clear();
            if(included)selected.UnionWith(choices.Keys);
            foreach(var choice in choices.Values)choice.SetPressedNoSignal(included);
            RefreshPlan();
        }

        void RefreshPlan()
        {
            current=Game.PreviewBatchCare(kind,scope.ButtonPressed?selected:null);
            int ready=current.Rows.Count(row=>row.Skip.Length==0);
            var cost=current.Supplies.Count>0?" · 消耗 "+string.Join("、",current.Supplies.Select(item=>$"{item.Key} ×{item.Value}")):"";
            var yield=current.Products.Count>0?" · 入库 "+string.Join("、",current.Products.Select(item=>$"{item.Key} ×{item.Value}")):"";
            summary.Text=$"{(scope.ButtonPressed?$"已选 {current.Rows.Length} 只 · ":"")}预计处理 {ready} 只 · 跳过 {current.Rows.Length-ready} 只{cost}{yield}";
            rows.Text=current.Rows.Length==0?"请至少选择一位伙伴。":string.Join("\n",current.Rows.Select(row=>row.Skip.Length>0
                ?$"跳过 · {row.Name} · {row.Skip}"
                :$"处理 · {row.Name}"+(row.Item.Length>0?$" · {row.Item} ×{row.Quantity}":"")));
            confirm.Disabled=ready==0;
        }
    }
}
