using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;

public partial class Main
{
    const int BlueprintUndoLimit=12;
    readonly List<NativeGame.BlueprintStep> _blueprintDraft=new();
    readonly List<NativeGame.BlueprintStep[]> _blueprintUndo=new();
    int _blueprintSelection=-1;
    static readonly string[] BlueprintCatalogs={"ROAD_STYLES","CATALOG","BUILDINGS","DECORATIONS"};

    void BuildBlueprintWorkspace(Container workspace)
    {
        var editor=BuildCard(workspace,"施工蓝图");
        var instructions=BuildText(editor,"先排好施工顺序，再整张提交。新路可以先为后续建筑接路。");
        var choices=BuildOperations(editor);
        var type=BuildChoice(choices,new[]{"道路","城镇建筑","庄园建筑","装饰"},0);type.Name="BlueprintType";
        var item=BuildChoice(choices,Array.Empty<string>(),0);item.Name="BlueprintItem";
        var facing=BuildChoice(choices,BuildOrientations,0);facing.Name="BlueprintFacing";
        var plotChoice=BuildChoice(choices,Array.Empty<string>(),0);
        var (x,y)=BuildCoordinates(editor);x.Name="BlueprintX";y.Name="BlueprintY";
        var end=BuildOperations(editor);
        var endX=new SpinBox{Name="BlueprintEndX",Prefix="终点 X ",MinValue=0,MaxValue=124,Value=_buildX,CustomMinimumSize=new(128,38)};
        var endY=new SpinBox{Name="BlueprintEndY",Prefix="终点 Y ",MinValue=0,MaxValue=79,Value=_buildY,CustomMinimumSize=new(128,38)};
        end.AddChild(endX);
        end.AddChild(endY);
        var buttons=BuildOperations(editor);
        var append=BuildButton(buttons,"加入蓝图");append.Name="BlueprintAppend";
        var cancelEdit=BuildButton(buttons,"取消编辑");cancelEdit.Name="BlueprintCancelEdit";

        var list=BuildCard(workspace,"施工顺序与报价");
        var capacity=BuildText(list,"");capacity.Name="BlueprintCapacity";
        var summary=BuildText(list,"");summary.Name="BlueprintEstimate";
        var operations=BuildOperations(list);
        var submit=BuildButton(operations,"提交整张蓝图");submit.Name="BlueprintSubmit";
        var undo=BuildButton(operations,"撤销草稿修改");undo.Name="BlueprintUndo";
        var clear=BuildButton(operations,"清空草稿");clear.Name="BlueprintClear";
        var mapTools=BuildOperations(list);
        var mapHeading=BuildText(mapTools,"");mapHeading.Name="BlueprintMapHeading";
        var zoomOut=BuildButton(mapTools,"全镇草图");
        var map=new Control{Name="BlueprintMap",CustomMinimumSize=new(0,176),MouseFilter=Control.MouseFilterEnum.Ignore};
        list.AddChild(map);
        var rows=new VBoxContainer{Name="BlueprintRows"};
        rows.AddThemeConstantOverride("separation",7);
        list.AddChild(rows);

        NativeGame.BlueprintEstimate estimate=new(0,0,"");
        KeyValuePair<string,JsonNode?>[] options=Array.Empty<KeyValuePair<string,JsonNode?>>();
        (int Id,bool Available)[] plots=Array.Empty<(int,bool)>();
        var statuses=new List<Label>();
        int editIndex=-1;
        long quotedRevision=-1;
        int quotedBalance=-1;
        Action refreshTemplates=()=>{};
        refreshTemplates=BuildBlueprintTemplateControls(workspace,x,y,LoadTemplate);
        append.Pressed+=ApplyForm;
        cancelEdit.Pressed+=CancelEdit;
        submit.Pressed+=Submit;
        undo.Pressed+=Undo;
        clear.Pressed+=()=>{RememberBlueprintDraft();_blueprintDraft.Clear();_blueprintSelection=-1;CancelEdit();RefreshDraft();};
        zoomOut.Pressed+=()=>SelectStep(-1);
        type.ItemSelected+=_=>UpdateType();
        map.Draw+=DrawMap;
        map.Resized+=map.QueueRedraw;
        UpdateType();
        RefreshDraft();
        _refreshManagementPage+=RefreshQuoteIfChanged;
        editor.TreeExiting+=()=>_refreshManagementPage-=RefreshQuoteIfChanged;

        void RefreshPlots(int keep=-1)
        {
            int previous=plotChoice.Selected>=0&&plotChoice.Selected<plots.Length?plots[plotChoice.Selected].Id:-1;
            plots=Game.Rows("buildings").OfType<JsonNode>()
                .Select(row=>(Id:NativeGame.I(row,"building_id"),Available:NativeGame.B(row,"unlocked")&&NativeGame.S(row,"kind")==""))
                .Where(plot=>plot.Available||plot.Id==keep).OrderBy(plot=>plot.Id).ToArray();
            plotChoice.Clear();
            foreach(var plot in plots)plotChoice.AddItem($"地块 {plot.Id}"+(plot.Available?"":" · 需修正"));
            int selected=Array.FindIndex(plots,plot=>plot.Id==(keep>=0?keep:previous));
            if(plots.Length>0)plotChoice.Select(Math.Max(0,selected));
        }

        void UpdateType(int originalPlot=-1)
        {
            options=Game.Rules[BlueprintCatalogs[type.Selected]]!.AsObject().ToArray();
            item.Clear();
            foreach(var option in options)item.AddItem(type.Selected switch{
                2=>option.Key,3=>option.Value![0]!.GetValue<string>(),_=>NativeGame.S(option.Value!,"name")});
            if(options.Length>0)item.Select(0);
            bool manor=type.Selected==2;
            if(manor)RefreshPlots(originalPlot);
            x.GetParent<Control>().Visible=!manor;
            plotChoice.Visible=manor;
            end.Visible=type.Selected==0;
            facing.Visible=type.Selected is 1 or 3;
            UpdateEditor();
        }

        void UpdateEditor()
        {
            bool editing=editIndex>=0;
            append.Text=editing?"保存这项修改":"加入蓝图";
            cancelEdit.Visible=editing;
            instructions.Text=editing?$"正在修改第 {editIndex+1} 项。保存后会重新核对整张蓝图。":
                "先排好施工顺序，再整张提交。新路可以先为后续建筑接路。";
            SetBuildAvailability(append,!editing&&_blueprintDraft.Count>=NativeGame.BlueprintLimit
                ?$"每张蓝图最多 {NativeGame.BlueprintLimit} 项":type.Selected==2&&plots.Length==0?"没有已解锁的空地块":
                options.Length==0?"没有可选项目":"");
        }

        void ApplyForm()
        {
            if(append.Disabled||item.Selected<0)return;
            int column=(int)x.Value,line=(int)y.Value;
            string key=options[item.Selected].Key;
            NativeGame.BlueprintStep step;
            if(type.Selected==0){
                var start=new NativeGame.Cell(column,line);
                var finish=new NativeGame.Cell((int)endX.Value,(int)endY.Value);
                var source=editIndex>=0&&_blueprintDraft[editIndex].Action=="road_stroke"
                    ?_blueprintDraft[editIndex].Args["cells"]!.AsArray():null;
                JsonArray cells=source is {Count:>0}&&BlueprintRoadCell(source[0]!)==start&&BlueprintRoadCell(source[^1]!)==finish
                    ?source.DeepClone().AsArray():new(NativeGame.RoadLine(start,finish).Select(cell=>(JsonNode)new JsonArray(cell.X,cell.Y)).ToArray());
                step=new("road_stroke",new JsonObject{{"style",key},{"cells",cells}});
            }else if(type.Selected==2){
                if(plotChoice.Selected<0)return;
                step=new("build_manor",new JsonObject{{"building_id",plots[plotChoice.Selected].Id},{"kind",key}});
            }else{
                bool decoration=type.Selected==3;
                var data=new JsonObject{{"cx",column},{"cy",line},{"orientation",facing.Selected}};
                if(decoration){data["kind"]=key;data["operation"]="place";}
                else data["catalog_key"]=key;
                step=new(decoration?"decoration":"build_town",data);
            }
            RememberBlueprintDraft();
            if(editIndex>=0){_blueprintDraft[editIndex]=step;_blueprintSelection=editIndex;}
            else _blueprintDraft.Add(step);
            CancelEdit();
            RefreshDraft();
        }

        void Edit(int index)
        {
            editIndex=index;
            _blueprintSelection=index;
            var step=_blueprintDraft[index];
            var args=step.Args;
            int category=step.Action switch{"road_stroke"=>0,"build_town"=>1,"build_manor"=>2,_=>3};
            type.Select(category);
            UpdateType(category==2?NativeGame.I(args,"building_id"):-1);
            string key=NativeGame.S(args,category switch{0=>"style",1=>"catalog_key",_=>"kind"});
            int choice=Array.FindIndex(options,option=>option.Key==key);
            if(choice>=0)item.Select(choice);
            if(category==0){
                var cells=args["cells"]!.AsArray();
                var start=BlueprintRoadCell(cells[0]!);
                var finish=BlueprintRoadCell(cells[^1]!);
                x.Value=start.X;y.Value=start.Y;endX.Value=finish.X;endY.Value=finish.Y;
            }else if(category!=2){
                x.Value=NativeGame.I(args,"cx");y.Value=NativeGame.I(args,"cy");
                facing.Select(Mathf.PosMod(NativeGame.I(args,"orientation"),4));
            }
            RefreshMap();
            Reveal(editor);
        }

        void CancelEdit()
        {
            editIndex=-1;
            if(type.Selected==2)RefreshPlots();
            UpdateEditor();
        }

        void ChangeOrder(int index,int next)
        {
            RememberBlueprintDraft();
            (_blueprintDraft[next],_blueprintDraft[index])=(_blueprintDraft[index],_blueprintDraft[next]);
            _blueprintSelection=next;
            CancelEdit();
            RefreshDraft();
        }

        void Undo()
        {
            if(_blueprintUndo.Count==0)return;
            _blueprintDraft.Clear();
            _blueprintDraft.AddRange(_blueprintUndo[^1]);
            _blueprintUndo.RemoveAt(_blueprintUndo.Count-1);
            _blueprintSelection=-1;
            CancelEdit();
            RefreshDraft();
            SetManagementFeedback("已撤销最近一次蓝图修改",completed:true);
        }

        void LoadTemplate(NativeGame.BlueprintStep[] steps)
        {
            RememberBlueprintDraft();
            _blueprintDraft.Clear();
            _blueprintDraft.AddRange(steps);
            _blueprintSelection=-1;
            CancelEdit();
            RefreshDraft();
            Reveal(list);
        }

        void RefreshEstimate()
        {
            quotedRevision=Game.WorldRevision;
            quotedBalance=NativeGame.I(Game.Meta,"coins");
            estimate=Game.PreviewBlueprint(_blueprintDraft);
            int count=_blueprintDraft.Count;
            capacity.Text=$"草稿 {count} / {NativeGame.BlueprintLimit} 项 · 可用金币 {quotedBalance:N0}";
            summary.Text=BlueprintQuoteText(estimate,count,quotedBalance);
            SetBuildAvailability(submit,estimate.Reason);
            for(int i=0;i<statuses.Count;i++){
                var status=statuses[i];
                if(i<estimate.ValidSteps){
                    status.Text="通过";
                    status.AddThemeColorOverride("font_color",TownTheme.Leaf);
                }else if(i==estimate.ValidSteps){
                    status.Text=$"待修正 · {estimate.Reason}";
                    status.AddThemeColorOverride("font_color",new Color("863c3c"));
                }else{
                    status.Text=$"待核对 · 先修正第 {estimate.ValidSteps+1} 项";
                    status.AddThemeColorOverride("font_color",TownTheme.Muted);
                }
            }
            RefreshMap();
        }

        void RefreshDraft()
        {
            clear.Disabled=_blueprintDraft.Count==0;
            undo.Disabled=_blueprintUndo.Count==0;
            undo.TooltipText=undo.Disabled?"暂无可以撤销的草稿修改":$"撤销最近一次蓝图修改 · 还有 {_blueprintUndo.Count} 次";
            refreshTemplates();
            UpdateEditor();
            foreach(Node child in rows.GetChildren()){rows.RemoveChild(child);child.QueueFree();}
            statuses.Clear();
            for(int i=0;i<_blueprintDraft.Count;i++){
                int index=i;
                var row=MarketCard(rows,color:i%2==0?"f5efe4":"e8efe9");
                row.Name=$"BlueprintRow{i}";
                BuildText(row,$"{i+1}. {BlueprintCaption(_blueprintDraft[i])}");
                var status=BuildText(row,"");status.Name=$"BlueprintStatus{i}";statuses.Add(status);
                var rowActions=BuildOperations(row);
                BuildButton(rowActions,"编辑",()=>Edit(index)).Name=$"BlueprintEdit{i}";
                BuildButton(rowActions,"上移",()=>ChangeOrder(index,index-1),i==0?"已经在最前面":"").Name=$"BlueprintUp{i}";
                BuildButton(rowActions,"下移",()=>ChangeOrder(index,index+1),i==_blueprintDraft.Count-1?"已经在最后面":"").Name=$"BlueprintDown{i}";
                BuildButton(rowActions,"看草图",()=>{SelectStep(index);Reveal(map);}).Name=$"BlueprintInspect{i}";
                BuildButton(rowActions,"查看现场",()=>{
                    SelectStep(index);
                    LocateBlueprintStep(_blueprintDraft[index]);
                },BlueprintAreas(_blueprintDraft[i]).Any()?"":"没有可定位的地块").Name=$"BlueprintLocate{i}";
                BuildButton(rowActions,"移除",()=>{
                    RememberBlueprintDraft();
                    _blueprintDraft.RemoveAt(index);
                    _blueprintSelection=-1;
                    CancelEdit();
                    RefreshDraft();
                },removal:true).Name=$"BlueprintRemove{i}";
            }
            RefreshEstimate();
        }

        void Submit()
        {
            if(submit.Disabled)return;
            var steps=BlueprintCommandRows(_blueprintDraft);
            var draft=_blueprintDraft.ToArray();
            var history=_blueprintUndo.ToArray();
            int selected=_blueprintSelection;
            _blueprintDraft.Clear();
            _blueprintUndo.Clear();
            _blueprintSelection=-1;
            long revision=Game.WorldRevision;
            Act("blueprint",new JsonObject{{"steps",steps},{"cost",estimate.Cost}});
            if(Game.WorldRevision!=revision)return;
            _blueprintDraft.AddRange(draft);
            _blueprintUndo.AddRange(history);
            _blueprintSelection=selected;
            ShowManagement("建造");
            SetManagementFeedback(_selection,true);
        }

        void RefreshQuoteIfChanged()
        {
            if(quotedRevision!=Game.WorldRevision||quotedBalance!=NativeGame.I(Game.Meta,"coins"))RefreshEstimate();
        }

        void SelectStep(int index){_blueprintSelection=index;RefreshMap();}

        void RefreshMap()
        {
            bool focused=_blueprintSelection>=0&&_blueprintSelection<_blueprintDraft.Count;
            map.Visible=mapTools.Visible=_blueprintDraft.Count>0;
            zoomOut.Visible=focused;
            mapHeading.Text=focused?$"第 {_blueprintSelection+1} 项 · {BlueprintCaption(_blueprintDraft[_blueprintSelection])}":"全镇草图 · 灰色为现有道路，绿色已通过，红色待修正，棕色待核对";
            map.QueueRedraw();
        }

        void DrawMap()
        {
            var size=map.Size;
            map.DrawRect(new Rect2(Vector2.Zero,size),new Color("e8efe2"));
            var areas=_blueprintDraft.Select(step=>BlueprintAreas(step).ToArray()).ToArray();
            var bounds=new Rect2(0,0,125,80);
            if(_blueprintSelection>=0&&_blueprintSelection<areas.Length&&areas[_blueprintSelection].Length>0)
                bounds=areas[_blueprintSelection].Aggregate((left,right)=>left.Merge(right)).Grow(5).Intersection(bounds);
            float scale=Math.Min((size.X-16)/bounds.Size.X,(size.Y-16)/bounds.Size.Y);
            if(scale<=0)return;
            var origin=(size-bounds.Size*scale)/2-bounds.Position*scale;
            void DrawArea(Rect2 area,Color color,bool fill=true)
            {
                var visible=area.Intersection(bounds);
                if(visible.HasArea())map.DrawRect(new Rect2(origin+visible.Position*scale,visible.Size*scale),color,fill,fill?-1:2);
            }
            foreach(var road in Data.Roads.Keys)DrawArea(new Rect2(road.X,road.Y,1,1),new Color("a6ab9f"));
            for(int i=0;i<areas.Length;i++){
                Color ink=new(i<estimate.ValidSteps?"4f8272":i==estimate.ValidSteps?"ad584b":"b39063");
                foreach(var area in areas[i])DrawArea(area,ink);
            }
            if(_blueprintSelection>=0&&_blueprintSelection<areas.Length)
                foreach(var area in areas[_blueprintSelection])DrawArea(area,new Color("193f5b"),false);
        }

        void Reveal(Control target)=>Callable.From(()=>{
            if(GodotObject.IsInstanceValid(target)&&target.IsVisibleInTree())_managementScroll?.EnsureControlVisible(target);
        }).CallDeferred();
    }

    void RememberBlueprintDraft()
    {
        _blueprintUndo.Add(_blueprintDraft.ToArray());
        if(_blueprintUndo.Count>BlueprintUndoLimit)_blueprintUndo.RemoveAt(0);
    }

    static NativeGame.Cell BlueprintRoadCell(JsonNode cell)=>new(cell[0]!.GetValue<int>(),cell[1]!.GetValue<int>());

    static JsonArray BlueprintCommandRows(IEnumerable<NativeGame.BlueprintStep> steps)=>new(steps.Select(step=>(JsonNode)new JsonObject{
        {"action",step.Action},{"args",step.Args.DeepClone()}}).ToArray());

    static string BlueprintQuoteText(NativeGame.BlueprintEstimate estimate,int count,int balance)
    {
        if(count==0)return "还没有施工项目。可从上方加入项目，或先预览已保存的模板。";
        if(estimate.ValidSteps<count)return $"{estimate.ValidSteps} / {count} 项通过 · 已通过项目小计 {estimate.Cost:N0} 金币\n{estimate.Reason}";
        string budget=balance>=estimate.Cost?$"提交后剩余 {balance-estimate.Cost:N0} 金币":$"还差 {estimate.Cost-balance:N0} 金币";
        return $"{count} 项通过 · 总报价 {estimate.Cost:N0} 金币 · {budget}";
    }

    string BlueprintCaption(NativeGame.BlueprintStep step)
    {
        var args=step.Args;
        if(step.Action=="road_stroke"){
            var cells=args["cells"]!.AsArray();
            var first=BlueprintRoadCell(cells[0]!);var last=BlueprintRoadCell(cells[^1]!);
            string style=NativeGame.S(Game.Spec("ROAD_STYLES",NativeGame.S(args,"style")),"name");
            return $"{style} · ({first.X}, {first.Y}) → ({last.X}, {last.Y}) · {cells.Count} 格";
        }
        if(step.Action=="build_manor")return $"{NativeGame.S(args,"kind")} · 庄园地块 {NativeGame.I(args,"building_id")}";
        string name=step.Action=="build_town"?NativeGame.S(Game.Spec("CATALOG",NativeGame.S(args,"catalog_key")),"name"):
            Game.Rules["DECORATIONS"]![NativeGame.S(args,"kind")]![0]!.GetValue<string>();
        return $"{name} · ({NativeGame.I(args,"cx")}, {NativeGame.I(args,"cy")}) · {BuildOrientations[Mathf.PosMod(NativeGame.I(args,"orientation"),4)]}";
    }

    IEnumerable<Rect2> BlueprintAreas(NativeGame.BlueprintStep step)
    {
        var args=step.Args;
        if(step.Action=="road_stroke"){
            foreach(var cell in args["cells"]!.AsArray()){
                var at=BlueprintRoadCell(cell!);
                yield return new Rect2(at.X,at.Y,1,1);
            }
        }else if(step.Action=="build_manor"){
            if(_dynamic.TryGetValue($"building:{NativeGame.I(args,"building_id")}",out var plot)){
                var cell=WorldData.Grid(plot.Position);
                yield return new Rect2(cell.X,cell.Y,1,1);
            }
        }else{
            int x=NativeGame.I(args,"cx"),y=NativeGame.I(args,"cy");
            int width=1,height=1;
            if(step.Action=="build_town"){
                int facing=Mathf.PosMod(NativeGame.I(args,"orientation"),2);
                var footprint=Game.Spec("CATALOG",NativeGame.S(args,"catalog_key"))["footprint"]!;
                width=footprint[facing]!.GetValue<int>();height=footprint[1-facing]!.GetValue<int>();
            }
            yield return new Rect2(x,y,width,height);
        }
    }

    void LocateBlueprintStep(NativeGame.BlueprintStep step)
    {
        var areas=BlueprintAreas(step).ToArray();
        if(areas.Length==0)return;
        var bounds=areas.Aggregate((left,right)=>left.Merge(right));
        var center=(bounds.Position+bounds.Size/2)*WorldData.GridSize;
        var destination=new Vector3(center.X,0,center.Y);
        if(step.Action=="build_manor"&&_dynamic.TryGetValue($"building:{NativeGame.I(step.Args,"building_id")}",out var plot))destination=plot.Position;
        RememberCameraSite(destination);
        StopPetFollow();StopResidentFollow();EndPlacement();
        _portraitAnchor=null;
        _buildCamera=true;
        _cameraTransition=0;
        _yaw=Mathf.Pi/4;
        _pitch=Mathf.Atan(1/Mathf.Sqrt(2));
        _center=destination;
        _distance=Math.Clamp(34+Math.Max(bounds.Size.X,bounds.Size.Y)*6,40,340);
        _selection=$"蓝图勘察 · {BlueprintCaption(step)}\n可回到经营管理的建造页继续修改。";
        _management?.Hide();
        UpdateCamera();
    }
}
