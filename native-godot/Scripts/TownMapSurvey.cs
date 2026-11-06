using Godot;
using System;
using System.Globalization;
using System.Linq;

public partial class TownMap
{
    internal enum SurveyMode { Navigate, Inspect, Measure }
    internal readonly record struct MapView(Vector2 Center,float Zoom);
    internal readonly record struct TileReport(string District,string Terrain,string Use,string Value);
    internal readonly record struct RoadMeasure(float Direct,float? Road,string Note);
    internal SurveyMode ToolMode {get;private set;}
    internal NativeGame.Cell? InspectedCell {get;private set;}
    internal NativeGame.Cell? MeasureStart {get;private set;}
    internal NativeGame.Cell? MeasureEnd {get;private set;}
    internal RoadMeasure? MeasuredRoad {get;private set;}
    Vector2[] _surveyRoad=Array.Empty<Vector2>();
    internal event Action? SurveyChanged;
    internal event Action? ToolModeChanged;

    internal MapView CurrentMapView=>new(_viewCenter,_zoom);

    internal void RestoreMapView(MapView view)
    {
        _zoom=Math.Clamp(view.Zoom,1,MaxZoom);
        _viewCenter=view.Center;
        ClampCenter();
        FinishViewChange();
    }

    internal static bool TrySurveyCell(string x,string y,out NativeGame.Cell cell)
    {
        cell=default;
        if(!int.TryParse(x,NumberStyles.Integer,CultureInfo.InvariantCulture,out int column)||
            !int.TryParse(y,NumberStyles.Integer,CultureInfo.InvariantCulture,out int row)||
            column<0||column>=Columns||row<0||row>=Rows)return false;
        cell=new(column,row);
        return true;
    }

    internal NativeGame.Cell SceneCameraCell
    {
        get{
            var center=Town.MapSceneCamera.Center.Clamp(Vector2.Zero,WorldSize-new Vector2(.01f,.01f));
            return CellAt(center);
        }
    }

    internal void CenterOnSceneCamera()=>RestoreMapView(new(Town.MapSceneCamera.Center,Math.Max(_zoom,1.65f)));

    internal void SetSurveyMode(SurveyMode mode)
    {
        if(ToolMode==mode)return;
        ToolMode=mode;
        InspectedCell=null;
        if(mode!=SurveyMode.Navigate)ClearResidentSelection();
        ClearMeasure();
        UpdateHover();
        ToolModeChanged?.Invoke();
    }

    internal void InspectMapCell(NativeGame.Cell cell,bool focus)
    {
        SetSurveyMode(SurveyMode.Inspect);
        InspectedCell=cell;
        if(focus)FocusMapPin(cell);
        QueueRedraw();
        SurveyChanged?.Invoke();
    }

    internal TileReport InspectTile(NativeGame.Cell cell)
    {
        RefreshFeatures();
        var point=CellCenter(cell);
        string district=_districts.FirstOrDefault(area=>area.Area.HasPoint(point)).Name??"镇郊";
        string terrainCode=TerrainAt(cell.X,cell.Y);
        string terrain=terrainCode switch{"water"=>"水域","zone"=>"街区用地","grass"=>"草地",_=>"受保护地形"};
        string use;
        if(_roads.Contains(cell)){
            string surface="道路";
            if(Town.Data.Roads.TryGetValue(new Vector2I(cell.X,cell.Y),out var road)){
                string style=NativeGame.S(road,"road_style","asphalt");
                if(Town.Game.Rules["ROAD_STYLES"]?[style] is { } spec)surface=NativeGame.S(spec,"name","道路");
            }
            use=$"{surface} · {(_disconnected.Contains(cell)?"未连通庄园":"已连通庄园")}";
        }else if(_projects.FirstOrDefault(project=>project.Area.HasPoint(point)) is {Name:not null} project){
            use=$"{project.Name} · {(project.Underway?"施工中":"已完工")}";
        }else if(Town.Game.DecorationAt(cell) is { } decoration){
            use=$"装饰 · {Town.Game.DecorationName(NativeGame.S(decoration,"kind"))}";
        }else use=Town.Game.BuildCellConflicts(cell)?"已有占用或属保护区域":"暂无已记录的占用冲突";
        string value=terrainCode is "grass" or "zone"?$"地价 {Town.Game.LandValue(cell):0}/100":"地价未计入";
        return new(district,terrain,use,value);
    }

    void ChooseSurveyCell(NativeGame.Cell cell)
    {
        if(ToolMode==SurveyMode.Inspect){InspectMapCell(cell,false);return;}
        if(MeasureStart==null||MeasureEnd!=null){MeasureStart=cell;MeasureEnd=null;}
        else MeasureEnd=cell;
        UpdateRoadMeasure();
        QueueRedraw();
        SurveyChanged?.Invoke();
    }

    internal void ClearMeasure()
    {
        MeasureStart=MeasureEnd=null;
        MeasuredRoad=null;
        _surveyRoad=Array.Empty<Vector2>();
        QueueRedraw();
        SurveyChanged?.Invoke();
    }

    void UpdateRoadMeasure()
    {
        _surveyRoad=Array.Empty<Vector2>();
        MeasuredRoad=null;
        if(MeasureStart is not NativeGame.Cell first||MeasureEnd is not NativeGame.Cell second)return;
        float direct=CellCenter(first).DistanceTo(CellCenter(second));
        if(!_roads.Contains(first)||!_roads.Contains(second)){
            MeasuredRoad=new(direct,null,"在两个道路格上选点即可测量道路距离。");
            return;
        }
        _surveyRoad=Town.Data.Route(new(first.X,first.Y),new(second.X,second.Y),0).Select(Ground).ToArray();
        if(_surveyRoad.Length==0){MeasuredRoad=new(direct,null,"这两个道路格之间没有连通的路线。");return;}
        float length=0;
        for(int i=1;i<_surveyRoad.Length;i++)length+=_surveyRoad[i-1].DistanceTo(_surveyRoad[i]);
        MeasuredRoad=new(direct,length,"沿现有道路中心测量。");
    }

    void DrawSurveyLayer(Rect2 viewport)
    {
        if(_surveyRoad.Length>1){
            var points=_surveyRoad.Select(WorldToView).ToArray();
            DrawPolyline(points,PaperInk,5,true);
            DrawPolyline(points,new Color("764568"),2.4f,true);
        }else if(MeasureStart is NativeGame.Cell a&&MeasureEnd is NativeGame.Cell b){
            DrawDashedLine(WorldToView(CellCenter(a)),WorldToView(CellCenter(b)),new Color("764568"),2,5);
        }
        if(InspectedCell is NativeGame.Cell tile){
            var bounds=new Rect2(WorldToView(new Vector2(tile.X,tile.Y)*WorldData.GridSize),Vector2.One*WorldData.GridSize*MapScale);
            if(viewport.Intersects(bounds)){
                DrawRect(bounds,new Color("fff5c755"));
                DrawRect(bounds,TownTheme.Ink,false,2);
                DrawSelectionRing(bounds.GetCenter(),Math.Max(5,bounds.Size.X/2));
            }
        }
        if(MeasureStart is NativeGame.Cell start)DrawPoint(start,"A");
        if(MeasureEnd is NativeGame.Cell end)DrawPoint(end,"B");

        void DrawPoint(NativeGame.Cell cell,string label)
        {
            var point=WorldToView(CellCenter(cell));
            if(!viewport.Grow(10).HasPoint(point))return;
            DrawCircle(point,9,PaperInk,true,-1,true);
            DrawString(GetThemeDefaultFont(),point+new Vector2(-4,4),label,HorizontalAlignment.Left,-1,12,new Color("764568"));
        }
    }

    void DrawSceneCamera(Rect2 viewport)
    {
        var camera=Town.MapSceneCamera;
        var point=WorldToView(camera.Center);
        if(!viewport.Grow(-10).HasPoint(point))return;
        var side=new Vector2(-camera.Facing.Y,camera.Facing.X);
        DrawCircle(point,8,PaperInk,true,-1,true);
        DrawColoredPolygon(new[]{point+camera.Facing*8,point-camera.Facing*5+side*5,point-camera.Facing*2,
            point-camera.Facing*5-side*5},new Color("245f60"));
    }
}
