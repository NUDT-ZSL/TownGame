using Godot;
using System;
using System.Linq;
using System.Text.Json.Nodes;

public partial class Main
{
    void VerifyBlueprintExperience(Action<bool,string> check)
    {
        var snapshot=Data.Snapshot;
        var draft=_blueprintDraft.ToArray();
        var history=_blueprintUndo.ToArray();
        var workspace=(_buildWorkspace,_buildX,_buildY,_blueprintSelection);
        var view=(_center,_distance,_pitch,_yaw,_buildCamera,_portraitAnchor,_cameraTransition,_previousCameraSite,_selection);
        try{
            Data.Root["snapshot"]=snapshot.DeepClone();
            Data.Reindex();
            Game.Meta["coins"]=100_000;
            string decoration=Game.Rules["DECORATIONS"]!.AsObject().First().Key;
            var open=(from row in Enumerable.Range(0,80)
                      from column in Enumerable.Range(0,125)
                      let cell=new NativeGame.Cell(column,row)
                      where !Data.Roads.ContainsKey(new Vector2I(column,row))&&Game.RoadReason(cell,"asphalt")==""&&
                            Game.DecorationQuote(decoration,column,row,0).Reason==""
                      select cell).Take(3).ToArray();
            check(open.Length==3,"蓝图体验验收缺少可用地块");
            var road=new NativeGame.BlueprintStep("road_stroke",new JsonObject{
                {"style","asphalt"},{"cells",new JsonArray(new JsonArray(open[0].X,open[0].Y))}});
            NativeGame.BlueprintStep Decor(NativeGame.Cell cell)=>new("decoration",new JsonObject{
                {"kind",decoration},{"operation","place"},{"cx",cell.X},{"cy",cell.Y}});
            var templateRoad=new NativeGame.BlueprintStep("road_stroke",new JsonObject{
                {"style","asphalt"},{"cells",new JsonArray(new JsonArray(5,5),new JsonArray(6,5))}});
            const string templateId="verify-blueprint-template";
            Game.Meta["native_blueprint_templates"]=new JsonArray(new JsonObject{
                {"id",templateId},{"name","测试林间小径"},{"steps",BlueprintCommandRows(new[]{templateRoad})}});
            string before=Data.Snapshot.ToJsonString();
            long revision=Game.WorldRevision;
            _blueprintDraft.Clear();
            _blueprintDraft.AddRange(new[]{road,Decor(open[0]),Decor(open[2])});
            _blueprintUndo.Clear();
            _blueprintSelection=-1;
            _buildWorkspace=4;
            ShowManagement("建造");
            check(Find<Label>("BlueprintStatus0").Text=="通过"&&Find<Label>("BlueprintStatus1").Text.Contains("待修正")&&
                Find<Label>("BlueprintStatus2").Text.Contains("待核对")&&Find<Button>("BlueprintSubmit").Disabled&&
                Find<Label>("BlueprintCapacity").Text.Contains("3 / 12"),"蓝图没有显示施工顺序依赖或草稿容量");

            Press("BlueprintDown0");
            check(_blueprintDraft[0].Action=="decoration"&&_blueprintDraft[1].Action=="road_stroke","下移未更新施工顺序");
            Press("BlueprintUndo");
            check(_blueprintDraft[0].Action=="road_stroke"&&_blueprintUndo.Count==0,"蓝图撤销未恢复原顺序");
            Press("BlueprintEdit1");
            var x=Find<SpinBox>("BlueprintX");var y=Find<SpinBox>("BlueprintY");
            check(x.Value==open[0].X&&y.Value==open[0].Y&&Find<Button>("BlueprintCancelEdit").Visible,"编辑未带回已有地块");
            x.Value=open[1].X;y.Value=open[1].Y;
            Press("BlueprintAppend");
            check(_blueprintDraft.Count==3&&NativeGame.I(_blueprintDraft[1].Args,"cx")==open[1].X&&
                !Find<Button>("BlueprintSubmit").Disabled&&Find<Label>("BlueprintEstimate").Text.Contains("提交后剩余"),
                "编辑未更新项目，或未实时复核报价与余额");

            Press("BlueprintInspect1");
            check(_blueprintSelection==1&&Find<Label>("BlueprintMapHeading").Text.StartsWith("第 2 项"),"看草图未聚焦选中项目");
            Press("BlueprintLocate1");
            var center=new Vector3((open[1].X+.5f)*WorldData.GridSize,0,(open[1].Y+.5f)*WorldData.GridSize);
            check(_management?.Visible==false&&_center.IsEqualApprox(center)&&_placementAction=="","现场勘察没有定位或误进入提交模式");

            ShowManagement("建造");
            x=Find<SpinBox>("BlueprintX");y=Find<SpinBox>("BlueprintY");
            x.Value=124;y.Value=79;
            Press($"BlueprintTemplateInspect_{templateId}");
            check(Find<PanelContainer>("BlueprintTemplatePreview").Visible&&Find<Button>("BlueprintTemplateApply").Disabled&&
                Find<Label>("BlueprintTemplateQuote").Text.Contains("超出小镇地图范围"),"模板越界未在预览阶段阻止载入");
            x.Value=8;y.Value=8;
            check(_blueprintDraft.Count==3&&!Find<Button>("BlueprintTemplateApply").Disabled,"重新选址未更新报价或预览改动了草稿");
            Press("BlueprintClear");
            Press("BlueprintTemplateApply");
            check(_blueprintDraft.Count==1&&BlueprintRoadCell(_blueprintDraft[0].Args["cells"]![0]!)==new NativeGame.Cell(8,8),
                "载入的施工位置与模板预览不一致");
            Press("BlueprintUndo");Press("BlueprintUndo");
            check(_blueprintDraft.Count==3&&Data.Snapshot.ToJsonString()==before&&Game.WorldRevision==revision,
                "撤销未恢复草稿，或编辑预览改变了真实存档");
            GD.Print("BLUEPRINT_EXPERIENCE_VERIFIED edit_reorder undo quote_status map_scene template_preview");
        }finally{
            _management?.Hide();
            Data.Root["snapshot"]=snapshot;
            Data.Reindex();
            _blueprintDraft.Clear();_blueprintDraft.AddRange(draft);
            _blueprintUndo.Clear();_blueprintUndo.AddRange(history);
            (_buildWorkspace,_buildX,_buildY,_blueprintSelection)=workspace;
            (_center,_distance,_pitch,_yaw,_buildCamera,_portraitAnchor,_cameraTransition,_previousCameraSite,_selection)=view;
        }

        T Find<T>(string name) where T:Node=>(T)_managementBody!.FindChild(name,true,false)!;
        void Press(string name)=>Find<Button>(name).EmitSignal(BaseButton.SignalName.Pressed);
    }
}
