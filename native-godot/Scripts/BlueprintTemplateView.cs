using Godot;
using System;
using System.Linq;
using System.Text.Json.Nodes;

public partial class Main
{
    Action BuildBlueprintTemplateControls(Container workspace,SpinBox x,SpinBox y,Action<NativeGame.BlueprintStep[]> loadDraft)
    {
        var card=BuildCard(workspace,"施工模板");
        BuildText(card,"模板保存在当前小镇。预览会按上方 X/Y 和所选庄园起始地块重新计算位置与报价。");
        var form=BuildOperations(card);
        var name=new LineEdit{PlaceholderText="给当前蓝图命名",MaxLength=24,CustomMinimumSize=new(180,36)};
        form.AddChild(name);
        var save=BuildButton(form,"保存草稿为模板",()=>Act("blueprint_template",new JsonObject{{"operation","save"},{"name",name.Text},
            {"steps",BlueprintCommandRows(_blueprintDraft)}}));
        var saved=Game.SavedBlueprintTemplates();
        if(saved.Length==0)BuildText(card,"暂无模板。可先排好常用施工组合，再输入名称保存。");
        var destinations=BuildOperations(card);
        var plots=Game.Rows("buildings").OfType<JsonNode>()
            .Where(plot=>NativeGame.B(plot,"unlocked")&&NativeGame.S(plot,"kind")=="")
            .Select(plot=>NativeGame.I(plot,"building_id")).Order().ToArray();
        var plotChoice=BuildChoice(destinations,plots.Select(plot=>$"庄园起始地块 {plot}"),0);
        destinations.Visible=saved.Any(template=>template.Steps.Any(step=>step.Action=="build_manor"));
        if(destinations.Visible&&plots.Length==0){
            plotChoice.Hide();
            BuildText(destinations,"没有已解锁的庄园空地块。");
        }

        var templates=new VBoxContainer();templates.AddThemeConstantOverride("separation",7);card.AddChild(templates);
        var preview=MarketCard(card,color:"e8efe7");
        var previewPanel=preview.GetParent<PanelContainer>();previewPanel.Name="BlueprintTemplatePreview";previewPanel.Hide();
        var title=BuildText(preview,"");title.Name="BlueprintTemplatePreviewTitle";
        title.AddThemeFontSizeOverride("font_size",18);
        title.AddThemeColorOverride("font_color",TownTheme.Leaf);
        var quote=BuildText(preview,"");quote.Name="BlueprintTemplateQuote";
        var entries=new VBoxContainer();entries.AddThemeConstantOverride("separation",4);preview.AddChild(entries);
        var actions=BuildOperations(preview);
        var apply=BuildButton(actions,"载入这份草稿");apply.Name="BlueprintTemplateApply";
        BuildButton(actions,"收起预览",previewPanel.Hide);

        NativeGame.BlueprintTemplate? selected=null;
        NativeGame.BlueprintStep[]? moved=null;
        (string Id,int X,int Y,int Plot,long Revision,int Balance)? last=null;
        foreach(var template in saved){
            var tile=MarketCard(templates,color:"f3eee3");
            BuildText(tile,$"{template.Name} · {template.Steps.Length} 项");
            string breakdown=string.Join(" · ",template.Steps.GroupBy(step=>step.Action).Select(group=>
                $"{BlueprintStepKind(group.Key)} {group.Count()} 项"));
            var detail=BuildText(tile,breakdown);detail.AddThemeColorOverride("font_color",TownTheme.Muted);
            var buttons=BuildOperations(tile);
            var inspect=BuildButton(buttons,"预览落点与报价",()=>{
                selected=template;
                previewPanel.Show();
                Refresh();
                Callable.From(()=>{
                    if(GodotObject.IsInstanceValid(previewPanel)&&previewPanel.IsVisibleInTree())_managementScroll?.EnsureControlVisible(previewPanel);
                }).CallDeferred();
            });
            inspect.Name=$"BlueprintTemplateInspect_{template.Id}";
            BuildButton(buttons,"删除模板",()=>ConfirmManagementAction("删除施工模板",$"移除模板「{template.Name}」。","删除",
                ()=>Act("blueprint_template",new JsonObject{{"operation","delete"},{"id",template.Id}})),removal:true);
        }
        apply.Pressed+=Apply;
        name.TextChanged+=_=>Refresh();
        x.ValueChanged+=_=>Refresh();
        y.ValueChanged+=_=>Refresh();
        plotChoice.ItemSelected+=_=>Refresh();
        _refreshManagementPage+=Refresh;
        card.TreeExiting+=()=>_refreshManagementPage-=Refresh;
        return Refresh;

        void Refresh()
        {
            save.Disabled=_blueprintDraft.Count==0||string.IsNullOrWhiteSpace(name.Text);
            if(!previewPanel.Visible||selected==null)return;
            int plot=plotChoice.Selected>=0&&plotChoice.Selected<plots.Length?plots[plotChoice.Selected]:-1;
            var key=(Id:selected.Id,X:(int)x.Value,Y:(int)y.Value,Plot:plot,Revision:Game.WorldRevision,Balance:NativeGame.I(Game.Meta,"coins"));
            if(last==key)return;
            last=key;
            foreach(var child in entries.GetChildren()){entries.RemoveChild(child);child.QueueFree();}
            title.Text=$"预览 · {selected.Name}";
            try{
                moved=Game.MoveBlueprintTemplate(selected,new NativeGame.Cell(key.X,key.Y),key.Plot);
                var estimate=Game.PreviewBlueprint(moved);
                quote.Text=BlueprintQuoteText(estimate,moved.Length,key.Balance);
                for(int i=0;i<moved.Length;i++)BuildText(entries,$"{i+1}. {BlueprintCaption(moved[i])}");
                apply.Text=estimate.Reason.Length>0?"载入草稿修正":"载入这份草稿";
                SetBuildAvailability(apply,"");
            }catch(InvalidOperationException error){
                moved=null;
                quote.Text=error.Message;
                apply.Text="载入这份草稿";
                SetBuildAvailability(apply,error.Message);
            }
        }

        void Apply()
        {
            Refresh();
            if(apply.Disabled||moved==null||selected==null)return;
            var steps=moved;
            string templateName=selected.Name;
            if(_blueprintDraft.Count==0){Load();return;}
            ConfirmManagementAction("载入施工模板",$"载入「{templateName}」会替换当前草稿，可以撤销这次替换。","替换草稿",Load);

            void Load()
            {
                previewPanel.Hide();
                loadDraft(steps);
                SetManagementFeedback($"已载入「{templateName}」并重新核对施工报价",completed:true);
            }
        }
    }

    static string BlueprintStepKind(string action)=>action switch{
        "road_stroke"=>"道路","build_town"=>"城镇建筑","build_manor"=>"庄园建筑",_=>"装饰"
    };
}
