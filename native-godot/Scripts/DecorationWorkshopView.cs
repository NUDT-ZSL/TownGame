using Godot;
using System;
using System.Linq;
using System.Text.Json.Nodes;

public partial class Main
{
    string _decorationCatalogQuery="";
    int _decorationBudgetFilter;

    void BuildDecorationWorkspace(Container workspace)
    {
        var card=BuildCard(workspace,"装饰目录");
        var heading=card.GetChild<Control>(0);
        var catalog=Game.Rules["DECORATIONS"]!.AsObject().Select(item=>item.Key).ToArray();
        string target=_managementEntryTarget.StartsWith("decor:",StringComparison.Ordinal)?_managementEntryTarget[6..]:"";
        if(catalog.Contains(target,StringComparer.Ordinal)){
            _buildDecorKind=target;
            _decorationCatalogQuery="";
            _buildDecorTheme=_decorationBudgetFilter=0;
            _managementFocusTarget=heading;
        }
        if(catalog.Length==0){BuildText(card,"暂无可摆放的装饰。");return;}

        var summary=BuildText(card,"");summary.Name="DecorationCatalogSummary";
        var filters=BuildOperations(card);
        var query=new LineEdit{Name="DecorationCatalogQuery",Text=_decorationCatalogQuery,PlaceholderText="搜索装饰名称或主题",
            AccessibilityName="搜索装饰目录",ClearButtonEnabled=true,CustomMinimumSize=new(168,38),SizeFlagsHorizontal=Control.SizeFlags.ExpandFill};
        filters.AddChild(query);
        var themeIds=Enumerable.Range(0,DecorationKitNames.Length)
            .Where(group=>group==0||catalog.Any(kind=>DecorationKitGroup(kind)==group)).ToArray();
        var theme=BuildChoice(filters,themeIds.Select(group=>DecorationKitNames[group]),Array.IndexOf(themeIds,_buildDecorTheme));
        theme.Name="DecorationKitTheme";
        var budget=BuildChoice(filters,new[]{"不限费用","预算内","有活动奖励"},_decorationBudgetFilter);
        budget.Name="DecorationCatalogBudget";
        var selector=BuildChoice(card,Array.Empty<string>(),0);selector.Name="DecorationKitItem";

        var showcase=BuildOperations(card);
        var (preview,renderPreview)=AddDecorationPreview(showcase);
        var details=new VBoxContainer{CustomMinimumSize=new(190,0),SizeFlagsHorizontal=Control.SizeFlags.ExpandFill};
        details.AddThemeConstantOverride("separation",6);
        showcase.AddChild(details);
        var name=MarketLabel(details,"",18,TownTheme.Leaf,wrap:true);name.Name="DecorationPreviewTitle";
        var price=BuildText(details,"");price.Name="DecorationPreviewPrice";
        BuildText(details,"占地 1 × 1 格 · 可按四个方向摆放");
        var facing=BuildChoice(details,BuildOrientations,_buildOrientation);facing.Name="DecorationPreviewFacing";
        var scene=BuildButton(BuildOperations(details),"在场景中摆放");scene.Name="DecorationPlaceInScene";

        var precise=BuildCard(workspace,"按格摆放或移动");
        var (x,y)=BuildCoordinates(precise);
        var status=BuildText(precise,"");status.Name="DecorationPlaceQuote";
        var actions=BuildOperations(precise);
        var place=BuildButton(actions,"摆放到这一格");place.Name="DecorationPlaceAtCell";
        var undo=BuildButton(actions,"撤销装饰操作",()=>Act("decoration",new JsonObject{{"operation","undo"}}));undo.Name="DecorationUndo";

        string[] matches=Array.Empty<string>();
        string[] options=Array.Empty<string>();
        Action? refreshPlaced=null;
        refreshPlaced=BuildPlacedDecorationDirectory(workspace,UseExistingDesign,()=>facing.Selected);
        query.TextChanged+=value=>{_decorationCatalogQuery=value;Refresh();};
        theme.ItemSelected+=index=>{_buildDecorTheme=themeIds[(int)index];Refresh();};
        budget.ItemSelected+=index=>{_decorationBudgetFilter=(int)index;Refresh();};
        selector.ItemSelected+=index=>{_buildDecorKind=matches[(int)index];RefreshSelection();};
        facing.ItemSelected+=index=>{_buildOrientation=(int)index;RefreshSelection();refreshPlaced();};
        x.ValueChanged+=_=>{RefreshSelection();refreshPlaced();};
        y.ValueChanged+=_=>{RefreshSelection();refreshPlaced();};
        scene.Pressed+=()=>{if(matches.Length>0)ArmPlacement("decoration",PlacementArgs());};
        place.Pressed+=()=>{if(matches.Length>0)Act("decoration",PlacementArgs());};
        Refresh();
        _refreshManagementPage+=Refresh;
        card.TreeExiting+=()=>_refreshManagementPage-=Refresh;

        JsonObject PlacementArgs()=>new(){{"operation","place"},{"kind",_buildDecorKind},{"orientation",facing.Selected},
            {"cx",_buildX},{"cy",_buildY}};

        void Refresh()
        {
            int coins=NativeGame.I(Game.Meta,"coins");
            var rows=catalog.Where(kind=>{
                int group=DecorationKitGroup(kind);
                if(_buildDecorTheme!=0&&group!=_buildDecorTheme)return false;
                if(_decorationBudgetFilter==1&&Game.DecorationPlacementPrice(kind)>coins)return false;
                if(_decorationBudgetFilter==2&&Game.FestivalDecorationCredits(kind)<=0)return false;
                return DecorationSearchMatches(_decorationCatalogQuery,$"{Game.DecorationName(kind)} {DecorationKitNames[group]} {kind}");
            }).ToArray();
            var captions=rows.Select(kind=>{
                int credits=Game.FestivalDecorationCredits(kind);
                return credits>0?$"{Game.DecorationName(kind)} · 活动奖励 ×{credits}":$"{Game.DecorationName(kind)} · {Game.DecorationCatalogPrice(kind)} 金币";
            }).ToArray();
            if(!matches.SequenceEqual(rows)||!options.SequenceEqual(captions)){
                matches=rows;
                options=captions;
                selector.Clear();
                foreach(string caption in captions)selector.AddItem(caption);
            }
            if(matches.Length>0){
                int selected=Math.Max(0,Array.IndexOf(matches,_buildDecorKind));
                selector.Select(selected);
                _buildDecorKind=matches[selected];
            }
            summary.Text=matches.Length==0?"没有符合条件的装饰。":$"显示 {matches.Length} / {catalog.Length} 件 · 当前金币 {coins}";
            selector.Visible=showcase.Visible=matches.Length>0;
            SetBuildAvailability(undo,Game.Meta["decoration_undo"] is JsonArray{Count:>0}?"":"暂无可撤销操作");
            RefreshSelection();
            refreshPlaced?.Invoke();
        }

        void RefreshSelection()
        {
            if(matches.Length==0){status.Text="请先从目录选择装饰。";SetBuildAvailability(place,status.Text);return;}
            string kind=_buildDecorKind;
            int coins=Game.DecorationPlacementPrice(kind),credits=Game.FestivalDecorationCredits(kind);
            name.Text=Game.DecorationName(kind);
            price.Text=credits>0?$"本次 0 金币 · 使用 1 份活动奖励\n剩余 {credits} 份 · 常规价格 {Game.DecorationCatalogPrice(kind)} 金币":$"摆放价格 {coins} 金币";
            var quote=Game.DecorationQuote(kind,_buildX,_buildY,facing.Selected);
            status.Text=BuildQuoteText(credits>0?"摆放使用 1 份活动奖励":$"摆放费用 {quote.Cost} 金币",quote.Reason==""?"此处可以摆放":quote.Reason);
            SetBuildAvailability(place,quote.Reason);
            SetBuildAvailability(scene,BuildAffordability(coins));
            renderPreview(kind,facing.Selected);
        }

        void UseExistingDesign(string kind,int orientation)
        {
            _buildDecorKind=kind;
            query.Text=_decorationCatalogQuery="";
            _buildDecorTheme=_decorationBudgetFilter=0;
            theme.Select(0);budget.Select(0);
            facing.Select(_buildOrientation=orientation);
            Refresh();
            _=RevealManagementSection(heading);
            SetManagementFeedback($"已复制「{Game.DecorationName(kind)}」与朝向。选择新位置即可摆放。",completed:true);
        }
    }

    (SubViewportContainer View,Action<string,int> Render) AddDecorationPreview(Container parent)
    {
        var view=new SubViewportContainer{Name="DecorationPreview",CustomMinimumSize=new(212,145),Stretch=true,
            SizeFlagsHorizontal=Control.SizeFlags.ExpandFill,MouseFilter=Control.MouseFilterEnum.Ignore};
        parent.AddChild(view);
        var viewport=new SubViewport{Size=new(224,145),OwnWorld3D=true,Msaa3D=SceneAntialiasing,RenderTargetUpdateMode=SubViewport.UpdateMode.Once};
        view.AddChild(viewport);
        var lighting=new Godot.Environment{BackgroundMode=Godot.Environment.BGMode.Color,BackgroundColor=new Color("e3ebe1"),
            AmbientLightSource=Godot.Environment.AmbientSource.Color,AmbientLightColor=new Color("fff5e8"),AmbientLightEnergy=.76f};
        viewport.AddChild(new WorldEnvironment{Environment=lighting});
        var pivot=new Node3D();viewport.AddChild(pivot);
        var model=new MeshInstance3D{Name="DecorationPreviewModel"};pivot.AddChild(model);
        var camera=new Camera3D{Position=new(13,8,15),Projection=Camera3D.ProjectionType.Orthogonal,Current=true};
        viewport.AddChild(camera);camera.LookAt(Vector3.Zero,Vector3.Up);
        viewport.AddChild(new DirectionalLight3D{RotationDegrees=new(-42,-30,0),LightEnergy=1.2f});
        string shown="";
        int facing=-1;
        float frame=1;
        view.Resized+=Fit;
        return (view,Render);

        void Render(string kind,int orientation)
        {
            if(shown==kind&&facing==orientation)return;
            if(shown!=kind){
                shown=kind;
                var mesh=Prototype("decoration:"+kind);
                model.Mesh=mesh;
                var scale=new Vector3(WorldData.LayoutScale,1,WorldData.LayoutScale)*(kind.StartsWith("tree_",StringComparison.Ordinal)?.8f:1);
                var bounds=mesh.GetAabb();
                model.Scale=scale;
                model.Position=-bounds.GetCenter()*scale;
                frame=Math.Max(1,(bounds.Size*scale).Length()*1.04f);
            }
            facing=orientation;
            pivot.Rotation=new(0,-orientation*Mathf.Pi/2,0);
            Fit();
        }

        void Fit()
        {
            float aspect=view.Size.X/Math.Max(view.Size.Y,1);
            camera.Size=frame/Math.Clamp(aspect,.1f,1);
            viewport.RenderTargetUpdateMode=SubViewport.UpdateMode.Once;
        }
    }

    static bool DecorationSearchMatches(string query,string text)=>query.Split((char[]?)null,StringSplitOptions.RemoveEmptyEntries)
        .All(word=>text.Contains(word,StringComparison.OrdinalIgnoreCase));
}
