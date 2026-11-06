using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;

public partial class TownMap : Control
{
    internal const string EmptyHoverText="将鼠标移到地图上查看地块、道路或工程信息";
    const int Columns=(int)(WorldData.WorldWidth/WorldData.GridSize);
    const int Rows=(int)(WorldData.WorldDepth/WorldData.GridSize);
    const float MaxZoom=6, ZoomStep=1.25f, DragThreshold=5, MarkerRadius=9, MarkerGap=34, DetailZoom=2.75f;
    static readonly Vector2 WorldSize=new(WorldData.WorldWidth,WorldData.WorldDepth);
    static readonly NativeGame.Cell[] Adjacent={new(1,0),new(-1,0),new(0,1),new(0,-1)};
    static readonly Color RoadInk=new("f5ead3"), BuildingInk=new("f0c780");
    static Color BrokenRoadInk=>new(TownTheme.HighContrast?"782331":"b85140");
    static readonly Color ConstructionInk=new("c46c32"), FreightInk=new("3469a2"), PaperInk=new("fff8e3");
    static readonly Color LowValue=new("4c976b"), HighValue=new("df765c");
    static readonly Color WaterInk=new("639dae"), DistrictInk=new("a7b582"), GrassInk=new("91aa75"), TerrainInk=new("768676");

    readonly record struct ProjectMarker(string Id, string Name, Rect2 Area, bool Underway);
    readonly record struct District(string Id, string Name, Rect2 Area);
    enum MarkerKind { Break, Freight, Construction, Resident }
    readonly record struct Marker(MarkerKind Kind, Vector2 World, int Count=1, ProjectMarker? Project=null,Main.MapResident[]? Residents=null);
    readonly record struct MarkerLayout(Vector2 Center, Vector2 Size, float Zoom, bool Construction, bool Disconnected, Vector2? Freight);

    sealed class MarkerGroup(Marker first)
    {
        public readonly Marker First=first;
        public Vector2 Total=first.World;
        public int Count=1;
        readonly List<Main.MapResident>? _residents=first.Residents?.ToList();

        public void Add(Marker marker)
        {
            Total+=marker.World;Count++;
            if(marker.Residents!=null)_residents?.AddRange(marker.Residents);
        }

        public Marker Result=>Count==1?First:new(First.Kind,Total/Count,Count,Residents:_residents?.ToArray());
    }

    ProjectMarker[] _projects=Array.Empty<ProjectMarker>();
    District[] _districts=Array.Empty<District>();
    int[] _zoneLevels=Array.Empty<int>();
    HashSet<NativeGame.Cell> _roads=new(), _disconnected=new();
    Vector2[] _roadMarkers=Array.Empty<Vector2>();
    MarkerLayout? _markerLayout;
    readonly List<Marker> _visibleMarkers=new();
    JsonObject? _indexedState;
    long _worldRevision=-1;
    int _roadRevision=-1;
    ImageTexture? _terrainTexture, _heatTexture, _roadTexture, _disconnectedTexture;
    bool _heatDirty=true, _roadsDirty=true;
    Vector2[] _freightRoute=Array.Empty<Vector2>(), _screenRoute=Array.Empty<Vector2>();
    Main.MapResident[] _residents=Array.Empty<Main.MapResident>();
    NativeGame.MapPin[] _mapPins=Array.Empty<NativeGame.MapPin>();
    readonly HashSet<string> _selectedResidents=new();
    string _selectedPin="";
    string _residentProfession="",_residentStatus="";
    bool _showResidents;
    Vector2? _freightPosition, _hoverPoint;
    NativeGame.Cell? _repairTarget;
    IReadOnlyList<NativeGame.Cell> _repairCells=Array.Empty<NativeGame.Cell>();
    string _freightStatus="", _hoverText=EmptyHoverText;
    Vector2 _viewCenter=WorldSize/2, _pressPoint, _lastDragPoint;
    float _zoom=1;
    MouseButton _panButton;
    bool _dragging;

    public Main Town=null!;
    public bool Heat, ShowConstruction=true, ShowDisconnected=true, ShowFreight=true;
    internal bool PinPlacementEnabled {get;set;}
    internal bool PinMoveEnabled {get;set;}
    internal int ZoomPercent=>Mathf.RoundToInt(_zoom*100);
    internal bool CanZoomIn=>_zoom<MaxZoom;
    internal bool CanZoomOut=>_zoom>1;
    public event Action<Vector2>? Navigate, DisconnectedRoadSelected;
    public event Action<string>? ProjectSelected, HoverChanged;
    internal event Action<Main.MapResident[]>? ResidentsSelected;
    internal event Action<Vector2>? PinRequested;
    internal event Action<string>? PinSelected;
    internal event Action? ViewChanged;
    internal event Action? LayersChanged;

    internal void SetResidentLayer(bool enabled)
    {
        _showResidents=enabled;
        if(!enabled)ClearResidentSelection();
        RefreshData();
    }

    internal void SetResidentFilter(string profession,string status)
    {
        if(_residentProfession==profession&&_residentStatus==status)return;
        _residentProfession=profession;_residentStatus=status;
        ClearResidentSelection();
        RefreshData();
    }

    float MapScale=>Math.Max(.0001f, Math.Min(Size.X/WorldSize.X,Size.Y/WorldSize.Y)*_zoom);
    internal Vector2 ViewToWorld(Vector2 point)=>_viewCenter+(point-Size/2)/MapScale;
    internal Vector2 WorldToView(Vector2 world)=>Size/2+(world-_viewCenter)*MapScale;

    public override void _Ready()
    {
        MouseForcePassScrollEvents=false;
        TextureFilter=TextureFilterEnum.Nearest;
        FocusMode=FocusModeEnum.All;
        FocusEntered+=QueueRedraw;
        FocusExited+=QueueRedraw;
        Resized+=()=>{ClampCenter();UpdateHover();QueueRedraw();};
        MouseExited+=()=>{_hoverPoint=null;UpdateHover();QueueRedraw();};
        VisibilityChanged+=()=>{
            if(IsVisibleInTree())return;
            _panButton=MouseButton.None;
            _dragging=false;
            _hoverPoint=null;
            UpdateHover();
        };
    }

    internal void ResetView()
    {
        _zoom=1;
        _viewCenter=WorldSize/2;
        FinishViewChange();
    }

    internal void ChangeZoom(int direction)=>ZoomAt(Size/2,direction);
    internal void RefreshPointer()=>UpdateHover();

    void ZoomAt(Vector2 point,float steps)
    {
        var anchor=ViewToWorld(point);
        _zoom=Math.Clamp(_zoom*Mathf.Pow(ZoomStep,steps),1,MaxZoom);
        _viewCenter=anchor-(point-Size/2)/MapScale;
        ClampCenter();
        FinishViewChange();
    }

    void FinishViewChange()
    {
        UpdateHover();
        QueueRedraw();
        ViewChanged?.Invoke();
    }

    internal void FocusMapPin(NativeGame.Cell cell)
    {
        _zoom=Math.Max(_zoom,2.2f);
        _viewCenter=CellCenter(cell);
        ClampCenter();
        FinishViewChange();
    }

    internal bool FocusDistrict(string id)
    {
        RefreshFeatures();
        var district=_districts.FirstOrDefault(zone=>zone.Id==id);
        if(string.IsNullOrEmpty(district.Id))return false;
        var fit=WorldSize/(district.Area.Size*1.5f);
        _zoom=Math.Clamp(Math.Min(fit.X,fit.Y),1.6f,3.5f);
        _viewCenter=district.Area.GetCenter();
        ClampCenter();
        FinishViewChange();
        return true;
    }

    internal void ShowRepairPreview(NativeGame.Cell? target,IReadOnlyList<NativeGame.Cell> cells)
    {
        _repairTarget=target;
        _repairCells=cells;
        QueueRedraw();
    }

    void ClampCenter()
    {
        var half=(Size/(2*MapScale)).Min(WorldSize/2);
        _viewCenter=_viewCenter.Clamp(half,WorldSize-half);
    }

    public override void _GuiInput(InputEvent input)
    {
        if(input is InputEventKey {Pressed:true,AltPressed:false,CtrlPressed:false,MetaPressed:false} key){
            var direction=key.Keycode switch{Key.Left=>Vector2.Left,Key.Right=>Vector2.Right,Key.Up=>Vector2.Up,Key.Down=>Vector2.Down,_=>Vector2.Zero};
            if(direction!=Vector2.Zero){
                _viewCenter+=direction*36/MapScale;
                ClampCenter();
                FinishViewChange();
            }else if(key.Keycode is Key.Plus or Key.Equal or Key.KpAdd)ChangeZoom(1);
            else if(key.Keycode is Key.Minus or Key.KpSubtract)ChangeZoom(-1);
            else if(key.Keycode==Key.Home)ResetView();
            else return;
            AcceptEvent();
            return;
        }
        if(input is InputEventMouseMotion motion){
            _hoverPoint=motion.Position;
            if(_panButton!=MouseButton.None){
                var held=_panButton==MouseButton.Left?MouseButtonMask.Left:MouseButtonMask.Middle;
                if((motion.ButtonMask&held)==0){_panButton=MouseButton.None;_dragging=false;}
                else PanTo(motion.Position);
                AcceptEvent();
            }
            UpdateHover();
            QueueRedraw();
            return;
        }
        if(input is not InputEventMouseButton button)return;
        if(button.ButtonIndex is MouseButton.WheelUp or MouseButton.WheelDown){
            if(!button.Pressed)return;
            float direction=button.ButtonIndex==MouseButton.WheelUp?1:-1;
            float steps=button.Factor>0?button.Factor:1;
            if(_panButton!=MouseButton.None){_lastDragPoint=_pressPoint=button.Position;_dragging=true;}
            _hoverPoint=button.Position;
            AcceptEvent();
            ZoomAt(button.Position,direction*steps);
            return;
        }
        if(button.ButtonIndex is not (MouseButton.Left or MouseButton.Middle))return;
        AcceptEvent();
        _hoverPoint=button.Position;
        if(button.Pressed){
            GrabFocus();
            if(_panButton!=MouseButton.None)return;
            _pressPoint=_lastDragPoint=button.Position;
            _panButton=button.ButtonIndex;
            _dragging=false;
            return;
        }
        if(button.ButtonIndex!=_panButton)return;
        PanTo(button.Position);
        bool click=_panButton==MouseButton.Left&&!_dragging;
        _panButton=MouseButton.None;
        _dragging=false;
        UpdateHover();
        QueueRedraw();
        if(click)SelectAt(button.Position);
    }

    void PanTo(Vector2 point)
    {
        _dragging|=point.DistanceSquaredTo(_pressPoint)>=DragThreshold*DragThreshold;
        if(!_dragging)return;
        var previous=_viewCenter;
        _viewCenter-=(point-_lastDragPoint)/MapScale;
        _lastDragPoint=point;
        ClampCenter();
        if(previous!=_viewCenter)ViewChanged?.Invoke();
    }

    void SelectAt(Vector2 point)
    {
        var world=ViewToWorld(point);
        if(!new Rect2(Vector2.Zero,Size).HasPoint(point))return;
        if(PinPlacementEnabled){
            if(new Rect2(Vector2.Zero,WorldSize).HasPoint(world))PinRequested?.Invoke(world);
            return;
        }
        if(ToolMode!=SurveyMode.Navigate){
            if(new Rect2(Vector2.Zero,WorldSize).HasPoint(world)){
                RefreshFeatures();
                ChooseSurveyCell(CellAt(world));
            }
            return;
        }
        if(PinAt(point) is NativeGame.MapPin pin){PinSelected?.Invoke(pin.Id);return;}
        RefreshFeatures();
        if(ResolveMarker(point,world) is Marker marker){
            if(marker.Residents!=null){
                if(marker.Count>1&&_zoom<DetailZoom){
                    _zoom=DetailZoom;
                    _viewCenter=marker.World;
                    ClampCenter();FinishViewChange();
                }
                _selectedResidents.Clear();
                foreach(var resident in marker.Residents)_selectedResidents.Add(resident.Id);
                QueueRedraw();
                ResidentsSelected?.Invoke(marker.Residents);
                return;
            }
            if(marker.Count>1){
                _zoom=Math.Min(MaxZoom,Math.Max(DetailZoom,_zoom*ZoomStep));
                _viewCenter=marker.World;
                ClampCenter();
                FinishViewChange();
            }else if(marker.Project is ProjectMarker project)ProjectSelected?.Invoke(project.Id);
            else if(marker.Kind==MarkerKind.Break)DisconnectedRoadSelected?.Invoke(marker.World);
            else Navigate?.Invoke(marker.World);
            return;
        }
        if(new Rect2(Vector2.Zero,WorldSize).HasPoint(world))Navigate?.Invoke(world);
    }

    internal void HighlightPin(string id)
    {
        _selectedPin=id;
        QueueRedraw();
    }

    internal void ClearResidentSelection()
    {
        _selectedResidents.Clear();
        ResidentsSelected?.Invoke(Array.Empty<Main.MapResident>());
        QueueRedraw();
    }

    static NativeGame.Cell CellAt(Vector2 world)=>new(Mathf.FloorToInt(world.X/WorldData.GridSize),Mathf.FloorToInt(world.Y/WorldData.GridSize));
    static Vector2 CellCenter(NativeGame.Cell cell)=>new Vector2(cell.X+.5f,cell.Y+.5f)*WorldData.GridSize;
    static Vector2 Ground(Vector3 point)=>new(point.X,point.Z);

    NativeGame.MapPin? PinAt(Vector2 point)
    {
        NativeGame.MapPin? nearest=null;
        float distance=13*13;
        for(int i=_mapPins.Length-1;i>=0;i--){
            float candidate=WorldToView(CellCenter(_mapPins[i].Cell)).DistanceSquaredTo(point);
            if(candidate>=distance)continue;
            distance=candidate;
            nearest=_mapPins[i];
        }
        return nearest;
    }

    Marker? ResolveMarker(Vector2 point,Vector2 world)
    {
        UpdateMarkerLayout();
        Marker? hovered=null;
        float nearest=float.PositiveInfinity;
        for(int index=_visibleMarkers.Count-1;index>=0;index--){
            var marker=_visibleMarkers[index];
            if(!MarkerBounds(marker).HasPoint(point))continue;
            float distance=point.DistanceSquaredTo(WorldToView(marker.World));
            if(distance>=nearest)continue;
            nearest=distance;
            hovered=marker;
        }
        if(hovered is Marker icon&&(icon.Count>1||icon.Kind!=MarkerKind.Break))return icon;
        var cell=CellAt(world);
        if(ShowDisconnected&&_disconnected.Contains(cell))return new Marker(MarkerKind.Break,CellCenter(cell));
        if(hovered!=null)return hovered;
        if(ShowConstruction)
            foreach(var project in _projects)
                if(project.Underway&&project.Area.HasPoint(world))return new Marker(MarkerKind.Construction,project.Area.GetCenter(),Project:project);
        return null;
    }

    void UpdateHover()
    {
        var description=EmptyHoverText;
        MouseDefaultCursorShape=_dragging?CursorShape.Drag:CursorShape.Arrow;
        if(!_dragging&&_hoverPoint is Vector2 point&&new Rect2(Vector2.Zero,Size).HasPoint(point)){
            var world=ViewToWorld(point);
            var marker=ResolveMarker(point,world);
            bool onMap=new Rect2(Vector2.Zero,WorldSize).HasPoint(world);
            if(PinPlacementEnabled&&onMap){
                var cell=CellAt(world);
                description=$"点击{(PinMoveEnabled?"移动":"放置")}图钉 · ({cell.X}, {cell.Y})";
                MouseDefaultCursorShape=CursorShape.Cross;
            }else if(ToolMode!=SurveyMode.Navigate&&onMap){
                var cell=CellAt(world);
                string action=ToolMode==SurveyMode.Inspect?"查看地块":MeasureStart.HasValue&&!MeasureEnd.HasValue?"选择终点 B":"选择起点 A";
                description=$"{action} · ({cell.X}, {cell.Y})";
                MouseDefaultCursorShape=CursorShape.Cross;
            }else if(PinAt(point) is NativeGame.MapPin pin){
                description=pin.Note.Length==0?$"图钉 · {pin.Name} · 点击查看":$"图钉 · {pin.Name} · {pin.Note}";
                MouseDefaultCursorShape=CursorShape.PointingHand;
            }else if(marker!=null||onMap){
                description=DescribeAt(world,marker);
                MouseDefaultCursorShape=CursorShape.PointingHand;
            }
        }
        if(_hoverText==description)return;
        _hoverText=description;
        HoverChanged?.Invoke(description);
    }

    string DescribeAt(Vector2 world,Marker? marker)
    {
        if(marker?.Residents is { } residents){
            if(residents.Length>1)return $"附近 {residents.Length} 位村民 · 点击查看名单";
            var person=residents[0];
            return $"{person.Name} · {person.Profession} · {person.Status} · 点击查看";
        }
        if(marker is { Count:>1 } group)
            return group.Kind==MarkerKind.Construction?$"{group.Count} 个施工项目 · 点击放大查看":$"{group.Count} 处断开路段 · 点击放大查看";
        if(marker?.Project is ProjectMarker project&&Town.Game.FindTownBuilding(project.Id) is JsonNode live)
            return $"{project.Name} · 施工进度 {Town.Game.ConstructionProgress(live):P0} · {Town.Game.ConstructionRoadStatus(live)} · 点击查看工程";
        if(marker is { Kind:MarkerKind.Break } road){
            var at=CellAt(road.World);
            return $"地块 ({at.X}, {at.Y}) · 道路尚未连通庄园 · 点击预览接线";
        }
        if(marker is { Kind:MarkerKind.Freight })return $"居民货车 · {_freightStatus} · 点击定位";
        var cell=CellAt(world);
        string location=$"地块 ({cell.X}, {cell.Y})";
        foreach(var building in _projects)
            if(building.Area.HasPoint(world))return $"{building.Name} · {location} · 点击定位";
        if(_roads.Contains(cell))return $"{location} · 已连通庄园的道路 · 点击定位";
        string district=_districts.FirstOrDefault(zone=>zone.Area.HasPoint(world)).Name??"镇郊";
        string terrain=TerrainAt(cell.X,cell.Y) switch{"water"=>"水域","zone"=>"街区用地","grass"=>"草地",_=>"地形"};
        string value=Heat&&terrain is "街区用地" or "草地"?$" · 地价 {Town.Game.LandValue(cell):0}/100":"";
        return $"{district} · {location} · {terrain}{value} · 点击定位";
    }

    Rect2 MarkerBounds(Marker marker)
    {
        var point=WorldToView(marker.World).Round();
        float countWidth=marker.Count>1?CountWidth(marker.Count):0;
        return new Rect2(point-Vector2.One*MarkerRadius,new Vector2(MarkerRadius*2+countWidth,MarkerRadius*2));
    }

    static float CountWidth(int count)=>count.ToString().Length*8+5;

    void UpdateMarkerLayout()
    {
        Vector2? freight=ShowFreight?_freightPosition:null;
        var layout=new MarkerLayout(_viewCenter,Size,_zoom,ShowConstruction,ShowDisconnected,freight);
        if(_markerLayout==layout)return;
        _markerLayout=layout;
        _visibleMarkers.Clear();
        var viewport=new Rect2(Vector2.Zero,Size).Grow(MarkerRadius);
        if(freight is Vector2 truck&&viewport.HasPoint(WorldToView(truck)))_visibleMarkers.Add(new(MarkerKind.Freight,truck));
        if(ShowDisconnected)AddLayer(_roadMarkers.Select(world=>new Marker(MarkerKind.Break,world)));
        if(ShowConstruction)AddLayer(_projects.Where(project=>project.Underway)
            .Select(project=>new Marker(MarkerKind.Construction,project.Area.GetCenter(),Project:project)));
        if(_showResidents)AddLayer(_residents.Select(resident=>new Marker(MarkerKind.Resident,resident.Position,Residents:new[]{resident})),clusterInDetail:true);

        void AddLayer(IEnumerable<Marker> marks,bool clusterInDetail=false)
        {
            var visible=marks.Where(marker=>viewport.HasPoint(WorldToView(marker.World)));
            if(!clusterInDetail&&_zoom>=DetailZoom){_visibleMarkers.AddRange(visible);return;}
            var buckets=new Dictionary<Vector2I,List<MarkerGroup>>();
            var groups=new List<MarkerGroup>();
            float radius=(clusterInDetail&&_zoom>=DetailZoom?18:MarkerGap)/MapScale;
            foreach(var marker in visible){
                var cell=new Vector2I(Mathf.FloorToInt(marker.World.X/radius),Mathf.FloorToInt(marker.World.Y/radius));
                MarkerGroup? best=null;
                float nearest=radius*radius;
                for(int y=-1;y<=1;y++)
                    for(int x=-1;x<=1;x++)
                        if(buckets.TryGetValue(cell+new Vector2I(x,y),out var nearby))
                            foreach(var group in nearby){
                                float distance=group.First.World.DistanceSquaredTo(marker.World);
                                if(distance>=nearest)continue;
                                nearest=distance;
                                best=group;
                            }
                if(best!=null){
                    best.Add(marker);
                }else{
                    var group=new MarkerGroup(marker);
                    if(!buckets.TryGetValue(cell,out var local))buckets[cell]=local=new();
                    local.Add(group);
                    groups.Add(group);
                }
            }
            foreach(var group in groups)_visibleMarkers.Add(group.Result);
        }
    }

    void RebuildRoadMarkers()
    {
        var pending=_disconnected.ToHashSet();
        var centers=new List<Vector2>();
        var queue=new Queue<NativeGame.Cell>();
        var component=new List<NativeGame.Cell>();
        foreach(var first in _disconnected.OrderBy(cell=>cell.Y).ThenBy(cell=>cell.X)){
            if(!pending.Remove(first))continue;
            component.Clear();
            queue.Enqueue(first);
            Vector2 sum=Vector2.Zero;
            while(queue.TryDequeue(out var cell)){
                component.Add(cell);
                sum+=CellCenter(cell);
                foreach(var delta in Adjacent){
                    var neighbor=new NativeGame.Cell(cell.X+delta.X,cell.Y+delta.Y);
                    if(pending.Remove(neighbor))queue.Enqueue(neighbor);
                }
            }
            Vector2 center=sum/component.Count;
            centers.Add(CellCenter(component.MinBy(cell=>CellCenter(cell).DistanceSquaredTo(center))));
        }
        _roadMarkers=centers.ToArray();
    }

    internal void RefreshData()
    {
        RefreshFeatures();
        _mapPins=Town.Game.MapPins();
        var residents=_showResidents?Town.MapResidents().Where(resident=>(_residentProfession.Length==0||resident.Profession==_residentProfession)
            &&(_residentStatus.Length==0||resident.Status==_residentStatus)).ToArray():Array.Empty<Main.MapResident>();
        if(!_residents.SequenceEqual(residents)){_residents=residents;_markerLayout=null;}
        var freight=Town.Game.FreightState;
        string truckId=NativeGame.S(freight,"vehicle_id");
        var truck=Town.Agents.FirstOrDefault(actor=>actor.IsCar&&actor.Ident==truckId);
        _freightPosition=truck==null?null:Ground(truck.Position);
        _freightStatus=NativeGame.S(freight,"status","待命");
        _freightRoute=truck!=null&&truck.Index<truck.Path.Count
            ?truck.Path.Skip(truck.Index).Select(Ground).Prepend(_freightPosition!.Value).ToArray()
            :Array.Empty<Vector2>();
        if(_screenRoute.Length!=_freightRoute.Length)_screenRoute=new Vector2[_freightRoute.Length];
        UpdateHover();
        QueueRedraw();
        LayersChanged?.Invoke();
    }

    internal (int Projects,int Disconnected,int Freight,int Residents,int Pins) LayerCounts()=>
        (_projects.Count(project=>project.Underway),_roadMarkers.Length,_freightPosition.HasValue?1:0,_residents.Length,_mapPins.Length);

    void RefreshFeatures()
    {
        var game=Town.Game;
        var state=Town.Data.Snapshot;
        int roadRevision=NativeGame.I(Town.Data.Meta,"road_revision");
        bool changedState=!ReferenceEquals(_indexedState,state);
        if(!changedState&&_worldRevision==game.WorldRevision&&_roadRevision==roadRevision)return;
        bool changedRoads=changedState||_roadRevision!=roadRevision;
        if(changedRoads){
            _roads=game.RoadCells();
            var connected=game.ConnectedRoads;
            _disconnected=_roads.Where(cell=>!connected.Contains(cell)).ToHashSet();
            if(MeasureEnd.HasValue)UpdateRoadMeasure();
            RebuildRoadMarkers();
            _roadsDirty=true;
            _heatDirty=true;
        }
        if(_districts.Length==0){
            _districts=Town.Data.Root["zones"]!.AsObject().Select(pair=>{
                var rect=pair.Value!["rect"]!.AsArray();
                var bounds=new Rect2((float)rect[0]!.GetValue<double>(),(float)rect[1]!.GetValue<double>(),
                    (float)rect[2]!.GetValue<double>(),(float)rect[3]!.GetValue<double>());
                return new District(pair.Key,NativeGame.S(pair.Value,"name"),new Rect2(bounds.Position*WorldData.GridSize/48,bounds.Size*WorldData.GridSize/48));
            }).ToArray();
        }
        var projects=game.Rows("town_buildings").OfType<JsonNode>().Select(row=>new ProjectMarker(
            NativeGame.S(row,"building_id"),NativeGame.S(game.Spec("CATALOG",NativeGame.S(row,"catalog_key")),"name"),
            new Rect2(NativeGame.I(row,"cx")*WorldData.GridSize,NativeGame.I(row,"cy")*WorldData.GridSize,
                NativeGame.I(row,"w")*WorldData.GridSize,NativeGame.I(row,"h")*WorldData.GridSize),NativeGame.S(row,"status")=="building")).ToArray();
        var levels=_districts.Select(zone=>game.ZoneLevel(zone.Id)).ToArray();
        _heatDirty|=!_projects.SequenceEqual(projects)||!_zoneLevels.SequenceEqual(levels);
        _projects=projects;
        _zoneLevels=levels;
        _markerLayout=null;
        _indexedState=state;
        _worldRevision=game.WorldRevision;
        _roadRevision=roadRevision;
        if(changedRoads&&MeasureEnd.HasValue)UpdateRoadMeasure();
    }

    string TerrainAt(int x, int y)=>Town.Data.Root["terrain"]![y]![x]!.GetValue<string>();

    void BuildTerrain(ref ImageTexture? texture, bool heat)
    {
        using var image=Image.CreateEmpty(Columns,Rows,false,Image.Format.Rgba8);
        for(int y=0;y<Rows;y++){
            for(int x=0;x<Columns;x++){
                string terrain=TerrainAt(x,y);
                Color ink=terrain switch{"water"=>WaterInk,"zone"=>DistrictInk,"grass"=>GrassInk,_=>TerrainInk};
                if(heat&&terrain is "grass" or "zone")ink=LowValue.Lerp(HighValue,(float)Town.Game.LandValue(new(x,y))/100);
                image.SetPixel(x,y,ink);
            }
        }
        SetTexture(ref texture,image);
    }

    void BuildRoads()
    {
        using var linked=Image.CreateEmpty(Columns,Rows,false,Image.Format.Rgba8);
        using var broken=Image.CreateEmpty(Columns,Rows,false,Image.Format.Rgba8);
        foreach(var road in _roads){
            if(_disconnected.Contains(road))broken.SetPixel(road.X,road.Y,BrokenRoadInk);
            else linked.SetPixel(road.X,road.Y,RoadInk);
        }
        SetTexture(ref _roadTexture,linked);
        SetTexture(ref _disconnectedTexture,broken);
        _roadsDirty=false;
    }

    static void SetTexture(ref ImageTexture? texture, Image image)
    {
        if(texture==null)texture=ImageTexture.CreateFromImage(image);
        else texture.Update(image);
    }

    void DrawDistrictLabels(Rect2 mapArea,Rect2 viewport)
    {
        var visible=mapArea.Intersection(viewport);
        var used=_visibleMarkers.Select(marker=>MarkerBounds(marker).Grow(3)).ToList();
        var font=GetThemeDefaultFont();
        int fontSize=_zoom>=DetailZoom?15:13;
        foreach(var district in _districts){
            var districtBox=new Rect2(WorldToView(district.Area.Position),district.Area.Size*MapScale);
            var room=visible.Intersection(districtBox);
            var size=font.GetStringSize(district.Name,HorizontalAlignment.Left,-1,fontSize).Ceil()+new Vector2(10,4);
            if(room.Size.X<size.X+4||room.Size.Y<size.Y+4)continue;
            var margin=new Vector2(3,3);
            var spots=new[]{
                room.Position+margin,
                new Vector2(room.End.X-size.X-margin.X,room.Position.Y+margin.Y),
                room.GetCenter()-size/2,
                new Vector2(room.Position.X+margin.X,room.End.Y-size.Y-margin.Y),
            };
            foreach(var spot in spots){
                var label=new Rect2(spot.Round(),size);
                if(used.Any(bounds=>bounds.Intersects(label.Grow(2))))continue;
                DrawRect(label,new Color("f9f2dfee"));
                DrawString(font,label.Position+new Vector2(5,2+font.GetAscent(fontSize)),district.Name,
                    HorizontalAlignment.Left,-1,fontSize,TownTheme.Ink);
                used.Add(label);
                break;
            }
        }
    }

    void DrawMapMarker(Marker marker)
    {
        Vector2 point=WorldToView(marker.World).Round();
        if(marker.Residents?.Any(resident=>_selectedResidents.Contains(resident.Id))==true)DrawSelectionRing(point,MarkerRadius+4);
        DrawCircle(point,MarkerRadius,PaperInk,true,-1,true);
        switch(marker.Kind){
            case MarkerKind.Construction:
                DrawColoredPolygon(new[]{point+new Vector2(0,-6),point+new Vector2(6,4),point+new Vector2(-6,4)},ConstructionInk);
                break;
            case MarkerKind.Break:
                DrawColoredPolygon(new[]{point+new Vector2(0,-6),point+new Vector2(6,0),point+new Vector2(0,6),point+new Vector2(-6,0)},BrokenRoadInk);
                DrawLine(point+new Vector2(-3,0),point+new Vector2(3,0),PaperInk,1.3f,true);
                break;
            case MarkerKind.Freight:
                DrawRect(new Rect2(point-new Vector2(5,5),new Vector2(10,10)),FreightInk);
                break;
            case MarkerKind.Resident:
                DrawCircle(point+new Vector2(0,-3),2.6f,new Color("525a88"),true,-1,true);
                DrawArc(point+new Vector2(0,5),4.8f,Mathf.Pi,Mathf.Tau,8,new Color("525a88"),2.4f,true);
                break;
        }
        if(marker.Count<=1)return;
        var countBox=new Rect2(point+new Vector2(7,-7),new Vector2(CountWidth(marker.Count)+2,14));
        DrawRect(countBox,PaperInk);
        DrawString(GetThemeDefaultFont(),countBox.Position+new Vector2(4,11),marker.Count.ToString(),HorizontalAlignment.Left,-1,12,TownTheme.Ink);
    }

    void DrawSelectionRing(Vector2 point,float radius)
    {
        DrawArc(point,radius,0,Mathf.Tau,32,PaperInk,4,true);
        DrawArc(point,radius,0,Mathf.Tau,32,TownTheme.Accent("家庭").Darkened(.3f),2,true);
    }

    public override void _Draw()
    {
        if(Size.X<=0||Size.Y<=0)return;
        RefreshFeatures();
        if(_terrainTexture==null)BuildTerrain(ref _terrainTexture,false);
        if(Heat&&_heatDirty){BuildTerrain(ref _heatTexture,true);_heatDirty=false;}
        if(_roadsDirty)BuildRoads();

        var viewport=new Rect2(Vector2.Zero,Size);
        DrawRect(viewport,new Color("e7ead9"));
        var mapArea=new Rect2(WorldToView(Vector2.Zero),WorldSize*MapScale);
        DrawTextureRect(Heat?_heatTexture!:_terrainTexture!,mapArea,false);
        DrawTextureRect(_roadTexture!,mapArea,false);
        if(ShowDisconnected)DrawTextureRect(_disconnectedTexture!,mapArea,false);
        foreach(var cell in _repairCells){
            var position=WorldToView(new Vector2(cell.X,cell.Y)*WorldData.GridSize);
            float width=WorldData.GridSize*MapScale;
            DrawRect(new Rect2(position,new Vector2(width,width)),new Color("efbb55"));
        }
        if(_repairTarget is NativeGame.Cell selected){
            var position=WorldToView(new Vector2(selected.X+.5f,selected.Y+.5f)*WorldData.GridSize);
            DrawArc(position,7,0,Mathf.Tau,24,new Color("fff1c4"),2,true);
        }

        foreach(var project in _projects){
            var bounds=new Rect2(WorldToView(project.Area.Position),project.Area.Size*MapScale);
            if(!viewport.Intersects(bounds))continue;
            DrawRect(bounds,BuildingInk);
            if(_zoom>1.25f)DrawRect(bounds,new Color("a17b51"),false,1);
        }
        if(ShowFreight&&_freightRoute.Length>1){
            for(int i=0;i<_freightRoute.Length;i++)_screenRoute[i]=WorldToView(_freightRoute[i]);
            DrawPolyline(_screenRoute,PaperInk,5,true);
            DrawPolyline(_screenRoute,FreightInk,2.5f,true);
        }
        DrawSurveyLayer(viewport);
        UpdateMarkerLayout();
        DrawDistrictLabels(mapArea,viewport);
        DrawSceneCamera(viewport);
        foreach(var marker in _visibleMarkers)DrawMapMarker(marker);
        foreach(var pin in _mapPins){
            var point=WorldToView(CellCenter(pin.Cell));
            if(!viewport.Grow(10).HasPoint(point))continue;
            if(pin.Id==_selectedPin)DrawSelectionRing(point,12);
            var color=new Color(TownTheme.HighContrast?"3d214c":"875081");
            DrawCircle(point+new Vector2(0,-3),7,PaperInk,true,-1,true);
            DrawColoredPolygon(new[]{point+new Vector2(-4,-2),point+new Vector2(4,-2),point+new Vector2(0,7)},color);
            DrawCircle(point+new Vector2(0,-4),4.8f,color,true,-1,true);
            DrawCircle(point+new Vector2(0,-4),1.8f,PaperInk,true,-1,true);
        }
        DrawRect(viewport.Grow(HasFocus()?-2:0),HasFocus()?TownTheme.Ink:new Color("8d9d87"),false,HasFocus()?2:1);
    }
}
