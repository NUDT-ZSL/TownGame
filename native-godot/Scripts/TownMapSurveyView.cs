using Godot;
using System;
using System.Globalization;

public partial class Main
{
    TownMap.MapView? _rememberedMapView;
    internal (Vector2 Center,Vector2 Facing) MapSceneCamera=>
        (new(_center.X,_center.Z),new(-Mathf.Sin(_yaw),-Mathf.Cos(_yaw)));

    Control AddMapSurveyTools(Container toolbar,TownMap map,Action<Vector2,float,string> locate)
    {
        var camera=MarketButton(toolbar,"镜头位置",map.CenterOnSceneCamera,expand:false,accent:"地图");
        camera.Name="TownMapSceneCamera";
        var menu=new MenuButton{Name="TownMapSurveyMenu",Text="地图工具",Flat=false,FocusMode=Control.FocusModeEnum.All};
        TownTheme.AccentButton(menu,"地图",6);
        toolbar.AddChild(menu);
        var popup=menu.GetPopup();
        popup.AddItem("普通导航",0);
        popup.AddItem("查看地块与输入坐标",1);
        popup.AddItem("道路测距",2);
        popup.IdPressed+=id=>{if(id is >=0 and <=2)map.SetSurveyMode((TownMap.SurveyMode)id);};

        var card=MarketCard(_managementBody!,color:"e9edf0");
        var panel=card.GetParent<PanelContainer>();
        panel.Name="TownMapSurveyPanel";
        var heading=MarketLabel(card,"",17,TownTheme.Leaf);
        var text=MarketLabel(card,"",14,TownTheme.Muted,wrap:true);
        text.Name="TownMapSurveyText";
        var coordinates=new HFlowContainer();coordinates.AddThemeConstantOverride("h_separation",6);card.AddChild(coordinates);
        var x=Coordinate("TownMapCoordinateX","X 列");
        var y=Coordinate("TownMapCoordinateY","Y 行");
        var lookup=MarketButton(coordinates,"找地块",Lookup,expand:false,accent:"地图");
        lookup.Name="TownMapCoordinateGo";
        var scene=MarketButton(coordinates,"前往场景",()=>{
            if(map.InspectedCell is not NativeGame.Cell cell)return;
            var point=WorldData.Cell(new(cell.X,cell.Y));
            locate(new(point.X,point.Z),62,$"地图地块 · {cell.X}, {cell.Y}");
        },expand:false,accent:"地图");
        scene.Name="TownMapInspectScene";
        var copy=new Button{Name="TownMapCopyCoordinates",Text="复制坐标"};
        TownTheme.AccentButton(copy,"地图",7);coordinates.AddChild(copy);
        copy.Pressed+=()=>{
            if(map.InspectedCell is not NativeGame.Cell cell)return;
            DisplayServer.ClipboardSet(FormattableString.Invariant($"{cell.X}, {cell.Y}"));
            copy.Text="已复制";
        };
        var clear=MarketButton(card,"清空测距",map.ClearMeasure,expand:false,accent:"地图");
        clear.Name="TownMapClearMeasure";
        clear.SizeFlagsHorizontal=Control.SizeFlags.ShrinkBegin;
        x.TextChanged+=_=>RefreshLookup();
        y.TextChanged+=_=>RefreshLookup();
        x.TextSubmitted+=_=>Lookup();
        y.TextSubmitted+=_=>Lookup();
        map.SurveyChanged+=()=>{
            if(map.InspectedCell is NativeGame.Cell cell){
                x.Text=cell.X.ToString(CultureInfo.InvariantCulture);
                y.Text=cell.Y.ToString(CultureInfo.InvariantCulture);
            }
            copy.Text="复制坐标";
            Refresh();
        };
        map.ToolModeChanged+=Refresh;
        map.LayersChanged+=Refresh;
        Refresh();
        return panel;

        LineEdit Coordinate(string id,string caption)
        {
            var input=new LineEdit{Name=id,PlaceholderText=caption,AccessibilityName=caption,MaxLength=5,CustomMinimumSize=new(72,34)};
            coordinates.AddChild(input);
            return input;
        }

        void Lookup()
        {
            if(TownMap.TrySurveyCell(x.Text,y.Text,out var cell))map.InspectMapCell(cell,true);
        }

        void RefreshLookup()=>lookup.Disabled=!TownMap.TrySurveyCell(x.Text,y.Text,out _);

        void Refresh()
        {
            var lens=map.SceneCameraCell;
            camera.TooltipText=$"在地图中找回当前镜头焦点 · {lens.X}, {lens.Y}";
            panel.Visible=map.ToolMode!=TownMap.SurveyMode.Navigate;
            coordinates.Visible=map.ToolMode==TownMap.SurveyMode.Inspect;
            clear.Visible=map.ToolMode==TownMap.SurveyMode.Measure;
            scene.Disabled=copy.Disabled=!map.InspectedCell.HasValue;
            clear.Disabled=!map.MeasureStart.HasValue;
            menu.Text=map.ToolMode switch{TownMap.SurveyMode.Inspect=>"地图工具 · 地块",TownMap.SurveyMode.Measure=>"地图工具 · 测距",_=>"地图工具"};
            RefreshLookup();
            if(!panel.Visible)return;
            if(map.ToolMode==TownMap.SurveyMode.Inspect){
                if(map.InspectedCell is NativeGame.Cell tile){
                    var details=map.InspectTile(tile);
                    heading.Text=$"地块 ({tile.X}, {tile.Y}) · {details.District}";
                    text.Text=$"{details.Terrain} · {details.Use} · {details.Value}";
                }else{
                    heading.Text="查看地块";
                    text.Text="点击地图，或输入 X 列 0–124、Y 行 0–79 查找。";
                }
            }else{
                heading.Text="道路测距";
                text.Text=map.MeasureStart is not NativeGame.Cell first?"先在地图上选起点 A，再选终点 B。":
                    map.MeasureEnd is not NativeGame.Cell second?$"起点 A ({first.X}, {first.Y}) · 请选择终点 B。":
                    map.MeasuredRoad is TownMap.RoadMeasure measure
                        ?$"A ({first.X}, {first.Y}) → B ({second.X}, {second.Y}) · 直线 {measure.Direct:0.#} 米"+
                         (measure.Road is float length?$" · 道路 {length:0.#} 米":"")+"\n"+measure.Note
                        :"请选择两个地块。";
            }
        }
    }
}
