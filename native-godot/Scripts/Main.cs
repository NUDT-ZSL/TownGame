using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;

public partial class Main : Node3D
{
    public NativeGame Game=null!;
    public WorldData Data=null!;
    public bool Paused;
    public readonly List<RoadAgent> Agents=new();
    readonly List<ArrayMesh> _meshes=new();
    readonly List<Vector2I> _junctions=new();
    readonly List<(MeshInstance3D Node,string Axis)> _lamps=new();
    readonly List<double> _samples=new();
    readonly List<(Vector2I Cell,Node3D Node)> _natural=new();
    readonly Random _random=new(42);
    readonly Camera3D _camera=new();
    Vector3 _center=WorldData.Expand(new(135,0,85));
    float _yaw=.48f,_pitch=.72f,_distance=48;
    double _clock,_lastHud;
    string _lastPhase="",_selection=SceneSelectionHint;
    Label _stats=null!,_details=null!;
    Control _panel=null!;
    int _frames;
    long _benchmarkStart,_lastFrameStamp;
    bool _smoke,_benchmark,_businessBenchmark;
    bool _enableTownAutoSave;
    ShaderMaterial _material=null!;
    readonly StandardMaterial3D _red=new(){AlbedoColor=new Color("ed6055"),EmissionEnabled=true,Emission=new Color("9a3028")};
    readonly StandardMaterial3D _green=new(){AlbedoColor=new Color("64e59d"),EmissionEnabled=true,Emission=new Color("288550")};
    string SavePath=>ExplicitSavePath??TownSlotPath(_saveSlot);

    public override void _Ready()=>_=StartTown();

    async System.Threading.Tasks.Task StartTown()
    {
        try {
            _migrationVerification=OS.GetCmdlineUserArgs().Contains("--verify-migration-scene");
            _largeTownVerification=OS.GetCmdlineUserArgs().Contains("--verify-large-town");
            _farmVerification=OS.GetCmdlineUserArgs().Contains("--verify-farm-manager");
            _freightVerification=OS.GetCmdlineUserArgs().Contains("--verify-freight");
            _residenceVerification=OS.GetCmdlineUserArgs().Contains("--verify-residences");
            _smoke=_migrationVerification||_largeTownVerification||OS.GetCmdlineUserArgs().Contains("--smoke");_businessBenchmark=OS.GetCmdlineUserArgs().Contains("--benchmark-simulation");_benchmark=_businessBenchmark||OS.GetCmdlineUserArgs().Contains("--benchmark");
            if(_benchmark)DisplayServer.WindowSetVsyncMode(DisplayServer.VSyncMode.Disabled);
            if(OS.GetCmdlineUserArgs().FirstOrDefault(a=>a.StartsWith("--write-engine-notices=")) is string notices){NativeContent.WriteEngineNotices(notices["--write-engine-notices=".Length..]);GD.Print("ENGINE_NOTICES_WRITTEN");FreeStartupResources();GetTree().Quit();return;}
            if(DisplayServer.GetName()!="headless"&&!_benchmark&&!_smoke&&
                (UseTownPreferences()||OS.GetCmdlineUserArgs().Contains("--town-loading-preview")))BeginTownLoading();
            await TownLoadStage("读取小镇资源",8);
            JsonObject root=NativeContent.Load();
            if(OS.GetCmdlineUserArgs().FirstOrDefault(a=>a.StartsWith("--compact-content=")) is string compact){NativeContent.Compact(root,compact[18..]);GD.Print("NATIVE_ASSETS_PACKED");FreeStartupResources();GetTree().Quit();return;}
            if(OS.GetCmdlineUserArgs().FirstOrDefault(a=>a.StartsWith("--legacy-check=")) is string check){var imported=LegacyImport.Read(check[15..],root);GD.Print($"LEGACY_IMPORT_VERIFIED pets={imported["pets"]!.AsArray().Count} people={imported["people"]!.AsArray().Count}");FreeStartupResources();GetTree().Quit();return;}
            InitializeTownSaveSlot();
            InitializeTownBindings();
            LoadDisplayPreferences();
            _enableTownAutoSave=UseTownPreferences();
            await TownLoadStage("检查存档与设置",25);
            bool hasSavedGame=System.IO.File.Exists(SavePath);
            bool loadSave=hasSavedGame&&!_smoke&&!_benchmark&&!_residenceVerification&&!_farmVerification&&!_freightVerification&&
                !OS.GetCmdlineUserArgs().Contains("--verify-rules")&&!OS.GetCmdlineUserArgs().Contains("--ui-smoke");
            if(!InitializePlayableTown(root,loadSave)){EndTownLoading();return;}
            MigrateSceneCoordinates();
            Data.Reindex();
            if(_businessBenchmark)Data.Meta["native_speed"]=600;
            if(OS.GetCmdlineUserArgs().Contains("--verify-rules")){NativeVerification.Run(root);_camera.Free();_red.Dispose();_green.Dispose();_ghostGood.Dispose();_ghostBad.Dispose();GetTree().Quit();return;}
            _material=new(){Shader=new Shader{Code=Geometry.Shader}};
            // The exported world terrain is the unkeyed mesh rooted at the map origin.
            int groundMesh=root["objects"]!.AsArray().OfType<JsonObject>().Where(record=>record["key"]==null&&
                record["pos"] is JsonArray position&&position.All(axis=>axis!.GetValue<double>()==0))
                .Select(record=>WorldData.I(record,"mesh")).DefaultIfEmpty(-1).First();
            var meshes=root["meshes"]!.AsArray();
            foreach(var mesh in meshes)_meshes.Add(null!);
            var meshOrder=root["objects"]!.AsArray().OfType<JsonNode>()
                .OrderBy(record=>WorldData.Position(record["pos"]!.AsArray()).DistanceSquaredTo(_center))
                .Select(record=>WorldData.I(record,"mesh")).Concat(Enumerable.Range(0,meshes.Count)).Distinct().ToArray();
            for(int i=0;i<meshOrder.Length;i++){
                if(i%20==0)await TownLoadStage($"准备场景网格 · {i}/{meshOrder.Length}",35+45d*i/Math.Max(1,meshOrder.Length));
                int index=meshOrder[i];
                _meshes[index]=Geometry.Decode(meshes[index]!.GetValue<string>(),_material,index==groundMesh);
            }
            await TownLoadStage("布置街区与伙伴",82);
            InitializeTownAudioMix();CreateEnvironment();InitializeAtmosphere();CreateWorld();CreateActors();CreateUi();SyncBusinessWorld();
            await TownLoadStage("结算离线时间",93);
            long openedAt=DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            if(!_smoke&&!_benchmark&&!_residenceVerification&&!_farmVerification&&!_freightVerification)
                ApplyOfflineProgress(openedAt);
            else
                Data.Meta["native_real_anchor"]=openedAt;
            Game.ManagerSceneDriven=true;
            AddChild(_camera);_camera.Current=true;_camera.Near=.2f;_camera.Far=2200;UpdateCamera();
            TreeExiting+=CancelTownPointer;
            TreeExiting+=()=>CompleteTownAutoSave(discard:true);
            InitializeFirstDayGuide(hasSavedGame);
            GetTree().AutoAcceptQuit=false;
            var host=GetWindow();
            host.CloseRequested+=HandleTownClose;
            TreeExiting+=()=>host.CloseRequested-=HandleTownClose;
            EndTownLoading();
            if(OS.GetCmdlineUserArgs().Contains("--ui-smoke")){_selectedPet=WorldData.S(Data.Pets[0]!,"pet_id");foreach(string tab in ManagementTabs)ShowManagement(tab);_management!.Hide();_smoke=true;GD.Print($"NATIVE_UI_VERIFIED tabs={ManagementTabs.Length}");}
            if(OS.GetCmdlineUserArgs().Contains("--ui-preview"))_ = CaptureManagementPreviews();
            if(OS.GetCmdlineUserArgs().Any(arg=>arg is "--world-preview" or "--pet-preview" or "--pet-max-preview" or "--activity-preview"))_ = CaptureWorldPreview();
            GD.Print($"NATIVE_READY meshes={_meshes.Count} objects={root["objects"]!.AsArray().Count} agents={Agents.Count}");
        } catch(Exception error){EndTownLoading();GD.PushError(error.ToString());GetTree().Quit(1);}
    }
    void CreateEnvironment()
    {
        var environment=new Godot.Environment {
            BackgroundMode=Godot.Environment.BGMode.Color,BackgroundColor=new Color("afc9ce"),
            AmbientLightSource=Godot.Environment.AmbientSource.Color,AmbientLightColor=new Color("e2e9dc"),AmbientLightEnergy=.25f,
            TonemapMode=Godot.Environment.ToneMapper.Linear,TonemapExposure=.8f
        };
        _residentialSky=environment;
        AddChild(new WorldEnvironment{Environment=environment});
        _residentialSun=new DirectionalLight3D{RotationDegrees=new(-55,-35,0),LightColor=new Color("fff1d8"),LightEnergy=.65f,ShadowEnabled=true,DirectionalShadowMaxDistance=240};AddChild(_residentialSun);
        var ground=new StaticBody3D{CollisionLayer=1};ground.AddChild(new CollisionShape3D{Shape=new BoxShape3D{Size=new(WorldData.WorldWidth,2,WorldData.WorldDepth)},Position=new(WorldData.WorldWidth/2,-1,WorldData.WorldDepth/2)});AddChild(ground);
    }
    void CreateWorld()
    {
        foreach(var item in Data.Root["objects"]!.AsArray()) {
            if(item!["key"] is JsonArray dynamicKey&&new[]{"road","building","town_building","decoration"}.Contains(dynamicKey[0]!.ToString()))continue;
            var record=item!;var instance=new MeshInstance3D{Mesh=_meshes[WorldData.I(record,"mesh")],Position=WorldData.Position(record["pos"]!.AsArray()),
                Rotation=new(0,-(float)WorldData.D(record,"angle"),0),Scale=new Vector3(WorldData.LayoutScale,1,WorldData.LayoutScale)*(float)WorldData.D(record,"scale",1)};
            AddChild(instance);
            if(record["natural_cell"] is JsonArray natural)_natural.Add((new(natural[0]!.GetValue<int>(),natural[1]!.GetValue<int>()),instance));
            if(record["key"] is JsonArray key&&key.Count==2) {
                instance.SetMeta("kind",key[0]!.ToString());instance.SetMeta("ident",key[1]!.ToString());
                if(key[0]!.ToString()=="prop")PositionPetActivityProp(instance,key[1]!.ToString());
                if(new[]{"building","town_building","zone","prop"}.Contains(key[0]!.ToString())) {
                    var body=new StaticBody3D{CollisionLayer=1};var bounds=instance.Mesh.GetAabb();
                    body.AddChild(new CollisionShape3D{Shape=new BoxShape3D{Size=bounds.Size.Max(new Vector3(.1f,.1f,.1f))},Position=bounds.GetCenter()});
                    body.SetMeta("kind",key[0]!.ToString());body.SetMeta("ident",key[1]!.ToString());instance.AddChild(body);
                }
                if(key[0]!.ToString()=="zone")ApplyDistrictArt(instance,key[1]!.ToString());
            }
        }
        CreatePetActivityArea();
        CreateSignals();
    }
    Node3D? _signalRoot;
    void CreateSignals(){CreateBoulevardSignals();_lastPhase="";}
    public Mesh Prototype(string name)
    {
        const string prefix="decoration:";
        if(name.StartsWith(prefix,StringComparison.Ordinal)&&DecorationKitGroup(name[prefix.Length..])>0)
            return DecorationKitMesh(name[prefix.Length..]);
        return _meshes[Data.Root["prototypes"]![name]!.GetValue<int>()];
    }
    void CreateActors()
    {
        foreach(var p in Data.People) {
            var actor=new RoadAgent{Town=this,Ident=WorldData.S(p!,"person_id"),Position=ResidentSpawn(p!),Speed=2.5f};
            actor.Visual.Mesh=Prototype("person-0");actor.SetMeta("kind","person");actor.SetMeta("ident",actor.Ident);
            AddChild(actor);Agents.Add(actor);AssignRoute(actor);
        }
        foreach(var pet in Data.Pets) {
            if(WorldData.S(pet!,"status")=="死亡"||pet!["fostered"]?.GetValue<bool>()==true)continue;
            var species=WorldData.S(pet!,"species");
            if(!Game.Active(pet!))continue;
            var animal=new PetAgent{Town=this,Ident=WorldData.S(pet!,"pet_id"),Species=species,Position=PetHomePoint(pet!)};
            AddFarmPet(animal);
        }
    }
    public void AssignRoute(RoadAgent actor)
    {
        if(_routeRefreshPending.Contains(actor))return;
        if(TryAssignFreightRoute(actor))return;
        if(TryAssignResidentialRoute(actor))return;
        if(TryAssignDaytimeRoute(actor))return;
        actor.ReturningHome=false;
        if(Data.Roads.Count==0){actor.Path.Clear();actor.Index=0;actor.Wait=3;return;}
        var start=WorldData.Grid(actor.Position);
        if(!Data.Roads.ContainsKey(start))start=Data.Roads.Keys.MinBy(p=>(p-start).LengthSquared());
        var person=Data.People.FirstOrDefault(p=>WorldData.S(p!,"person_id")==actor.Ident);
        var job=Game.Jobs.FirstOrDefault(j=>WorldData.S(j!,"worker")==actor.Ident);
        string destination=person==null?"":WorldData.S(person,"destination","estate");
        var goal=job!=null?Data.Portals[WorldData.S(job,"zone")]:person!=null?Data.Portals[WorldData.S(person,"status")=="离镇"?"station":Data.Portals.ContainsKey(destination)?destination:"estate"]:Data.Portals.Values.ElementAt(_random.Next(Data.Portals.Count));
        if(!actor.IsCar&&start==goal){actor.Path.Clear();actor.Index=0;actor.Wait=job!=null?1:8;return;}
        var route=Data.Route(start,goal,actor.IsCar?WorldData.VehicleLane:WorldData.Sidewalk);
        actor.Path.Clear();actor.Path.AddRange(route);actor.Index=0;actor.Wait=3;
    }
    public string Phase {get {double t=_clock%34;return t<9?"ns":t<11?"ns_yellow":t<12?"clear":t<21?"ew":t<23?"ew_yellow":t<24?"clear":t<31?"walk":"clear";}}
    public bool RedLight(RoadAgent actor,Vector3 direction,float delta)
    {
        if(Phase==(!actor.IsCar?"walk":Math.Abs(direction.X)>Math.Abs(direction.Z)?"ew":"ns"))return false;
        float line=actor.IsCar?(actor.Kind=="bus"?8.3f:actor.Kind=="truck"?7.4f:6.8f):3.15f;
        foreach(var cell in _junctions) {
            if(!actor.IsCar){
                var offset=actor.Position-WorldData.Cell(cell);
                var arm=Math.Abs(direction.Z)>Math.Abs(direction.X)?new Vector2I(offset.X>=0?1:-1,0):new Vector2I(0,offset.Z>=0?1:-1);
                if(!Data.Roads.ContainsKey(cell+arm))continue;
            }
            var diff=WorldData.Cell(cell)-actor.Position;diff.Y=0;
            float ahead=diff.Dot(direction),lateral=(diff-direction*ahead).Length();
            if(lateral<WorldData.GridSize*.5f&&ahead>=line-.08f&&ahead-actor.Speed*delta<line)return true;
        }
        return false;
    }
    void CreateUi()
    {
        var layer=new CanvasLayer();AddChild(layer);_townHudLayer=layer;
        var theme=TownTheme.Create();
        StyleBoxFlat Card(string color)=>new(){BgColor=new Color(color),CornerRadiusTopLeft=14,CornerRadiusTopRight=14,CornerRadiusBottomLeft=14,CornerRadiusBottomRight=14,ContentMarginLeft=14,ContentMarginRight=14,ContentMarginTop=10,ContentMarginBottom=10};
        void Ink(Control control)=>control.AddThemeColorOverride("font_color",new Color("29423d"));
        var top=new PanelContainer{Name="TownHudToolbar",Position=new(18,14),Theme=theme};top.AddThemeStyleboxOverride("panel",Card("f5f1e6f4"));layer.AddChild(top);
        var column=new VBoxContainer();column.AddThemeConstantOverride("separation",7);top.AddChild(column);
        CreateTownStatus(column);
        var row=new HFlowContainer{Name="TownHudActions"};row.AddThemeConstantOverride("h_separation",10);row.AddThemeConstantOverride("v_separation",4);column.AddChild(row);
        var management=Group("TownHudManagement");
        Button(management,"经营管理","家庭",()=>ShowManagement("宠物"));
        _journalButton=Button(management,"今日待办","事件",()=>ShowManagement("今日待办"));
        UpdateJournalBadge();
        var locate=Group("TownHudLocate");
        Button(locate,"庄园","环境",()=>SetCameraPreset(CameraPreset.Manor));
        locate.AddChild(CameraViewPicker());
        Button(locate,"找宠物","宠物",FindPet).TooltipText="定位当前宠物，可用滚轮继续拉近";
        var findResidents=Button(locate,"找村民","家庭",()=>ShowManagement("家庭","residents"));
        findResidents.Name="TownFindResidents";
        findResidents.TooltipText="按姓名或职业查找村民，并定位当前位置";
        var controls=Group("TownHudTimeAndSettings");
        _townPauseButton=Button(controls,"暂停","事件",()=>SetTownPaused(!Paused));
        var speedButton=new MenuButton{Name="TownSpeedPicker",Flat=false,Text=$"速度 ×{WorldData.I(Data.Meta,"native_speed",1)}",TooltipText="选择小镇时间的推进速度"};
        TownTheme.AccentButton(speedButton,"地图",6);controls.AddChild(speedButton);
        var saveButton=Button(controls,"保存","存档",Save);
        var districts=new OptionButton();TownTheme.AccentButton(districts,"建造",6);controls.AddChild(districts);
        foreach(var zone in Data.Root["zones"]!.AsObject())districts.AddItem(WorldData.S(zone.Value!,"name"));
        void GoToDistrict(int index) {
            var selectedZone=Data.Root["zones"]!.AsObject().ElementAt(index);var rect=selectedZone.Value!["rect"]!;
            var destination=WorldData.Expand(new(((float)rect[0]!.GetValue<double>()+(float)rect[2]!.GetValue<double>()/2)/12,0,((float)rect[1]!.GetValue<double>()+(float)rect[3]!.GetValue<double>()/2)/12));
            var sites=Game.UrbanAccessPoints().Where(a=>a.Zone==selectedZone.Key&&a.Entrance!=null).ToArray();
            if(sites.Length>0&&selectedZone.Key!="estate")destination=new((float)sites.Average(a=>a.DoorX)*WorldData.GridSize,0,(float)sites.Average(a=>a.DoorY)*WorldData.GridSize);
            RememberCameraSite(destination);
            StopPetFollow();
            StopResidentFollow();
            _center=destination;
            _distance=selectedZone.Key=="estate"?175:120;
        }
        districts.ItemSelected+=index=>GoToDistrict((int)index);
        CreateTownFastForwardBanner(column);
        _panel=new PanelContainer{Name="TownHudSceneCard",Position=new(18,720),Theme=theme};_panel.AddThemeStyleboxOverride("panel",Card("f5f1e6f4"));layer.AddChild(_panel);
        var body=new VBoxContainer();_panel.AddChild(body);
        _sceneTownHeading=new Label{Text="小镇动态",Visible=false};Ink(_sceneTownHeading);body.AddChild(_sceneTownHeading);
        CreateScenePetCare(body);
        _details=new Label{AutowrapMode=TextServer.AutowrapMode.WordSmart,Visible=false};Ink(_details);body.AddChild(_details);
        CreateResidentSceneTools(body);
        CreateTownFieldGuide(body);
        FitTownHudCard(_panel);
        CreateSceneInteractionUi(layer,theme);
        CreateTownCameraHud(layer,theme);
        CreateFirstDayGuide(layer,theme);
        SetupDisplayPreferences(top,row,controls,speedButton,saveButton,districts,GoToDistrict);
        RefreshScenePetCare();
        RefreshTownStatus();

        HBoxContainer Group(string name){var group=new HBoxContainer{Name=name};group.AddThemeConstantOverride("separation",4);row.AddChild(group);return group;}
        Button Button(Container parent,string caption,string tab,Action action){
            var button=new Button{Text=caption};TownTheme.AccentButton(button,tab,6);
            button.Pressed+=action;parent.AddChild(button);return button;
        }
    }
    string _selectedPet="";

    void FindPet()
    {
        if(LocatePet(_selectedPet))return;
        var pet=GetChildren().OfType<PetAgent>().FirstOrDefault(p=>!p.IsQueuedForDeletion());
        if(pet==null||!LocatePet(pet.Ident))_selection="庄园暂无可查看的宠物";
    }

    bool LocatePet(string id)
    {
        if(id.Length==0||FarmPet(id) is not PetAgent actor)return false;
        var pet=Data.Pets.FirstOrDefault(row=>WorldData.S(row!,"pet_id")==id);
        if(pet==null)return false;
        RememberCameraSite(InPetCamera?actor.Position:null);
        StopPetFollow();
        StopResidentFollow();
        ClearSceneResidentSelection();
        _selectedPet=id;
        FocusSceneInspection("pet",id);
        SetPetCameraFrame(actor,pet);
        _selection="";
        _management?.Hide();
        RecordFirstDayVisit(GuideLocated);
        RefreshScenePetCare();
        return true;
    }

    public override void _UnhandledInput(InputEvent input)
    {
        if(_stats==null||_benchmark||_smoke&&!_migrationInput)return;
        if(input is InputEventMouseButton {Pressed:true} button) {
            if(button.ButtonIndex is MouseButton.WheelUp or MouseButton.WheelDown){
                float step=button.Factor>0?Math.Clamp(button.Factor,.05f,4):1;
                ZoomAt(TownWheelAnchor(button.Position),TownWheelFactor(button.ButtonIndex==MouseButton.WheelUp?step:-step));
            }else if(button.ButtonIndex==MouseButton.Right){
                CancelRoadStroke();
                BeginTownPointer(button);
            }else if(button.ButtonIndex==MouseButton.Middle){
                if(!_strokeActive)BeginTownPointer(button);
            }else if(button.ButtonIndex==MouseButton.Left){
                if(_placementAction is "road" or "remove_road")BeginRoadStroke(button.Position);
                else if(_placementAction.Length>0)Select(button.Position);
                else BeginTownPointer(button);
            }
        }
        if(input is InputEventKey key&&key.Pressed&&!key.Echo) {
            if(MatchTownInput(key,"photo")){TogglePhotoMode();return;}
            if(MatchTownInput(key,"capture")){_=CaptureTownPhoto();return;}
            if(InPhotoMode){
                if(key.Keycode==Key.Escape)LeavePhotoMode();
                return;
            }
            if(HandleCameraHistoryInput(key)){GetViewport().SetInputAsHandled();return;}
            if(MatchTownInput(key,"save"))Save();
            if(MatchTownInput(key,"map"))ShowManagement("地图");
            if(MatchTownInput(key,"search")){CancelTownPointer();OpenTownSearch();}
            if(MatchTownInput(key,"undo"))Act("undo_road",new JsonObject());
            if(MatchTownInput(key,"rotate")&&_placementAction is "build_town" or "decoration")
                _placementArgs["orientation"]=(NativeGame.I(_placementArgs,"orientation")+1)%4;
            if(key.Keycode==Key.Escape){CancelTownPointer();StopPetFollow();StopResidentFollow();EndPlacement();}
        }
    }
    Vector3? GroundUnderCursor(Vector2 screen,float minimumDown=.001f)
    {
        Vector3 origin=_camera.ProjectRayOrigin(screen),direction=_camera.ProjectRayNormal(screen);
        if(direction.Y>=-minimumDown)return null;
        float travel=-origin.Y/direction.Y;
        return travel>0&&travel<_camera.Far?origin+direction*travel:null;
    }
    void ZoomAt(Vector2 screen,float factor)
    {
        float next=Math.Clamp(_distance*factor,InPetCamera?MinimumPortraitDistance():5.5f,900);
        if(Mathf.IsEqualApprox(next,_distance))return;
        if(InPetCamera){_distance=Math.Min(next,45f);UpdateCamera();return;}
        Vector3? before=GroundUnderCursor(screen);
        _distance=next;UpdateCamera();
        Vector3? after=GroundUnderCursor(screen);
        if(before.HasValue&&after.HasValue){_center+=before.Value-after.Value;ClampTownCameraCenter();UpdateCamera();}
    }
    void Select(Vector2 screen)
    {
        if(_placementAction!=""){CommitPlacement(screen);return;}
        if(TryOpenScenePickChooser(screen,ApplyScenePick))return;
        if(PickSceneAt(screen) is ScenePick target)ApplyScenePick(target);
    }
    void ApplyScenePick(ScenePick target)
    {
        TrackSceneInspectorSelection(target);
        string kind=target.Kind,id=target.Id;
        if(kind=="person"&&SelectSceneResident(id))return;
        ClearSceneResidentSelection();
        if(TrySelectTownLifeFeature(kind,id))return;
        if(kind=="road"){OpenSceneRoad(target.Cell);return;}
        if(kind=="pet"){SelectScenePet(id);return;}
        if(kind=="manager"){SelectFarmManager();return;}
        if(kind=="vehicle"){_selectedPet="";_selection=FreightDescription(id);return;}
        if(kind=="herd"){_selectedPet="";_selection=$"公共牧场 · 绵羊{id}\n公共牲畜{NativeGame.I(Game.Commons["stock"]!,"herd")}只 · 饲料{NativeGame.I(Game.Commons["stock"]!,"feed")}份\n牧民通过自治养殖任务消耗饲料并生产食物。";return;}
        if(kind=="prop"){OpenPetActivityStation(id);return;}
        if(kind=="waste"){Act("clean_waste",new JsonObject{{"waste_id",id}});return;}
        if(kind=="town_building"){
            _selectedPet="";
            if(Game.FindTownBuilding(id) is JsonNode building&&Game.IsResidentialHome(building)&&!IsTownProjectUnderway(building))
                SelectResidence(id);
            else OpenTownProject(id);
            return;
        }
        if(kind=="decoration"){_selectedPet="";ShowManagement("建造","placed-decor:"+id);return;}
        if(kind=="building"&&int.TryParse(id,out int plot)){_selectedPet="";_buildPlot=plot;_buildWorkspace=0;ShowManagement("建造","manor");return;}
        if(kind=="zone"){_selectedPet="";_selection="这是街区地标。居民家庭住在带门牌的住宅，进入经营管理 → 家庭可以定位。";return;}
        _selectedPet="";
        var item=Data.People.FirstOrDefault(p=>WorldData.S(p!,"person_id")==id);
        _selection=item==null?$"{kind} / {id}":$"{WorldData.S(item,"name")}\n{WorldData.S(item,"role")} · {WorldData.S(item,"profession")}";
    }
    public override void _Process(double delta)
    {
        if(Data==null||Game==null||_stats==null||!_camera.IsInsideTree())return;
        if(!Paused)_clock+=delta;
        RefreshQueuedRoutes();
        if(!Paused&&!_smoke&&!_residenceVerification&&!_farmVerification&&!_freightVerification&&(!_benchmark||_businessBenchmark)){
            _simulationAccumulator+=_businessBenchmark?1d/60:delta;
            if(_simulationAccumulator>=SimulationInterval){
                try{AdvanceTownClock(_simulationAccumulator);SyncBusinessWorld();}
                catch(Exception error){
                    if(_businessBenchmark){GD.PushError(error.ToString());GetTree().Quit(1);}
                    SetTownPaused(true);
                    if(_fastForward.HasValue)StopFastForward("");
                    _selection="模拟已暂停："+error.Message;
                }
                _simulationAccumulator=0;
            }
        }
        if(_enableTownAutoSave&&!_smoke&&!_benchmark&&!_residenceVerification&&!_farmVerification&&!_freightVerification)TickTownAutoSave(delta);
        float x=(TownMovementPressed("right")?1:0)-(TownMovementPressed("left")?1:0);
        float z=(TownMovementPressed("down")?1:0)-(TownMovementPressed("up")?1:0);
        var forward=new Vector3(-Mathf.Sin(_yaw),0,-Mathf.Cos(_yaw));var right=new Vector3(Mathf.Cos(_yaw),0,-Mathf.Sin(_yaw));
        var move=right*x-forward*z;
        if(!_benchmark&&!_smoke&&!_townCameraDragging&&_management?.Visible!=true&&_townSearchWindow?.Visible!=true&&move.LengthSquared()>0){
            StopPetFollow();
            StopResidentFollow();
            _center+=move.Normalized()*(float)delta*_distance*.55f;
        }
        UpdatePetFollow(delta);
        UpdateResidentFollow(delta);
        ClampTownCameraCenter();UpdateCamera((float)delta);
        if(InPhotoMode)UpdateTownPhotoPose();
        UpdateSelectedActorOcclusion(delta);
        UpdatePlacement();
        UpdateTownShadowProfile();
        UpdateSceneInteraction(delta);
        UpdateResidentialView(delta);
        UpdateFarmView(delta);
        UpdateFreight(delta);
        UpdateWorldLife(delta);
        UpdateTownLifeFeatures(delta);
        if(_lastPhase!=Phase){UpdateBoulevardSignals(Phase);_lastPhase=Phase;}
        _lastHud+=delta;
        bool refreshHud=_lastHud>.2;
        if(refreshHud){RefreshTownStatus();RefreshScenePetCare();RefreshTownCameraHud();_lastHud=0;if(_management?.Visible==true)UpdateManagementSummary();}
        UpdatePetMarker(refreshHud);
        RefreshManagementPage(delta);
        RefreshPetPortraits();
        _frames++;
        if(_residenceVerification)VerifyResidenceScene(delta);
        if(_farmVerification)VerifyFarmManagerScene(delta);
        if(_freightVerification)VerifyFreightScene(delta);
        if(_migrationVerification)VerifyMigrationScene(delta);
        if(_largeTownVerification)_=VerifyLargeTown();
        long stamp=0;
        if(_benchmark){
            stamp=System.Diagnostics.Stopwatch.GetTimestamp();
            if(_frames==300)_benchmarkStart=stamp;
            if(_frames>300)_samples.Add(System.Diagnostics.Stopwatch.GetElapsedTime(_lastFrameStamp,stamp).TotalMilliseconds);
            _lastFrameStamp=stamp;
        }
        if((_smoke&&!_migrationVerification&&!_largeTownVerification&&_frames>=5)||(_benchmark&&_frames>=1800)) {
            if(_benchmark&&_samples.Count>0){_samples.Sort();double elapsed=System.Diagnostics.Stopwatch.GetElapsedTime(_benchmarkStart,stamp).TotalSeconds;
                GD.Print($"BENCHMARK frame_mean_ms={_samples.Average():F3} frame_p95_ms={_samples[(int)(_samples.Count*.95)]:F3} fps={_samples.Count/elapsed:F1} frames={_samples.Count}");
                string prefix=_businessBenchmark?"native-business":"native";
                System.IO.File.WriteAllText(ProjectSettings.GlobalizePath($"res://../.test-output/{prefix}-benchmark.json"),
                    System.Text.Json.JsonSerializer.Serialize(new{frames=_samples.Count,warmup=300,meanFrameMs=_samples.Average(),p95FrameMs=_samples[(int)(_samples.Count*.95)],fps=_samples.Count/elapsed,
                        width=GetViewport().GetVisibleRect().Size.X,height=GetViewport().GetVisibleRect().Size.Y,actors=Agents.Count,scope=_businessBenchmark?"native-economy-five-game-hours":"render-and-basic-movement-migration-preview",vsync=false}));
                GetViewport().GetTexture().GetImage().SavePng(ProjectSettings.GlobalizePath($"res://../.test-output/{prefix}-godot.png"));}
            GetTree().Quit();
        }
    }
    void FreeStartupResources(){_camera.Free();_red.Dispose();_green.Dispose();_ghostGood.Dispose();_ghostBad.Dispose();}
    void Save()=>SaveSnapshot(true);
    bool SaveSnapshot(bool announce)
    {
        var previous=Data.Meta["native_real_anchor"]?.DeepClone();
        try{
            if(announce)CompleteTownAutoSave(discard:true);
            Data.Meta["native_real_anchor"]=DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            WriteTownProgress();
        }catch(Exception error){
            Data.Meta["native_real_anchor"]=previous;
            _saveWriteError=error.Message;_selection="保存失败："+error.Message;return false;
        }
        RecordSnapshotIfDue(announce);
        if(announce)_selection=_snapshotHistoryWarning.Length==0?"游戏已保存":"游戏已保存 · "+_snapshotHistoryWarning;
        return true;
    }
    async System.Threading.Tasks.Task CaptureManagementPreviews()
    {
        try {
            string folder=MigrationOutput;System.IO.Directory.CreateDirectory(folder);
            async System.Threading.Tasks.Task Capture(string tab,string filename,bool includeScene=false,string target=""){
                ShowManagement(tab,target);
                for(int frame=0;frame<3;frame++)await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
                for(int frame=0;frame<16&&!VisiblePortraitsReady();frame++)
                    await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
                _management!.GetTexture().GetImage().SavePng(System.IO.Path.Combine(folder,$"ui-{filename}.png"));
                if(includeScene)GetViewport().GetTexture().GetImage().SavePng(System.IO.Path.Combine(folder,"ui-经营窗口.png"));
            }
            foreach(string tab in ManagementTabs)await Capture(tab,tab,tab=="宠物");
            _management!.Hide();
            OpenTownSearch();
            var search=_townSearchWindow!;
            var query=(LineEdit)search.FindChild("TownSearchQuery",true,false)!;
            query.Text="页面";
            query.EmitSignal(LineEdit.SignalName.TextChanged,query.Text);
            for(int frame=0;frame<3;frame++)await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
            GetViewport().GetTexture().GetImage().SavePng(System.IO.Path.Combine(folder,"ui-搜索小镇.png"));
            search.EmitSignal(Window.SignalName.CloseRequested);
            RefreshTownCameraHud();
            _townCameraHudToggle.EmitSignal(BaseButton.SignalName.Pressed);
            for(int frame=0;frame<3;frame++)await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
            GetViewport().GetTexture().GetImage().SavePng(System.IO.Path.Combine(folder,"ui-镜头工具.png"));
            _townCameraHudToggle.EmitSignal(BaseButton.SignalName.Pressed);
            await Capture("家庭","村民名册",target:"residents");
            await Capture("家庭","家庭-住宅",target:"residence");
            await Capture("环境","环境-镜头",target:"camera-input");
            await Capture("环境","环境-机位",target:"camera-bookmarks");
            await Capture("环境","环境-声音",target:"audio");
            await Capture("环境","环境-时间计划",target:"time-plan");
            await Capture("环境","环境-农田",target:"farm-plots");
            var estate=Data.Portals["estate"];
            await Capture("地图","地图-地块",target:$"cell:{estate.X},{estate.Y}");
            await Capture("成长","成长-委托",target:"town-commission");
            await Capture("事件","事件-周末活动",target:"weekend-festivals");
            await Capture("自治","自治-游览",target:"tourism");
            await Capture("档案","档案-图鉴",target:"codex");
            if(Data.FindPet(_selectedPet)!=null)await Capture("档案","档案-伙伴",target:"companion:"+_selectedPet);
            for(int i=1;i<BuildWorkspaces.Length;i++){
                _buildWorkspace=i;
                await Capture("建造",$"建造-{BuildWorkspaces[i]}");
            }
            _buildWorkspace=2;await Capture("建造","建造-工程名册",target:"projects");
            _buildWorkspace=3;await Capture("建造","建造-装饰目录",target:"decor:kit_market_crates");
            _marketSection=1;await Capture("商店","商店-物资");
            _marketSection=2;await Capture("商店","商店-加工");
            _marketSection=3;await Capture("商店","商店-仓库");
            _petManagementExpanded=true;
            ShowManagement("宠物");
            var sale=_managementBody!.FindChildren("*","Button",true,false).OfType<Button>()
                .FirstOrDefault(button=>button.Text=="出售给新家庭"&&!button.Disabled);
            if(sale!=null){
                string id=_selectedPet;
                sale.EmitSignal(BaseButton.SignalName.Pressed);
                var dialog=_management!.GetChildren().OfType<ConfirmationDialog>().Single();
                for(int frame=0;frame<3;frame++)await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
                GetViewport().GetTexture().GetImage().SavePng(System.IO.Path.Combine(folder,"ui-出售确认.png"));
                dialog.GetCancelButton().EmitSignal(BaseButton.SignalName.Pressed);
                await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
                if((GodotObject.IsInstanceValid(dialog)&&dialog.Visible)||!Data.Pets.Any(p=>NativeGame.S(p!,"pet_id")==id&&Game.Active(p!)))
                    throw new InvalidOperationException("取消出售应保留伙伴");
                GD.Print("NATIVE_UI_SALE_CANCEL_VERIFIED");
            }
            ShowManagement("订单");
            var supply=_managementBody!.FindChildren("OrderBuySupply","Button",true,false).OfType<Button>()
                .FirstOrDefault(button=>button.IsVisibleInTree()&&!button.Disabled);
            if(supply!=null){
                supply.EmitSignal(BaseButton.SignalName.Pressed);
                await Capture("商店","订单补货");
            }
            if(OS.GetCmdlineUserArgs().Any(arg=>arg.StartsWith("--photo-output=",StringComparison.Ordinal)))
                await VerifyRenderedPhotoAlbum(folder);
            StartFirstDayGuide();
            for(int frame=0;frame<3;frame++)await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
            GetViewport().GetTexture().GetImage().SavePng(System.IO.Path.Combine(folder,"ui-首日引导.png"));
            GD.Print("NATIVE_UI_PREVIEW_SAVED tabs="+ManagementTabs.Length);GetTree().Quit();
        }catch(Exception error){GD.PushError(error.ToString());GetTree().Quit(1);}
    }
    async System.Threading.Tasks.Task CaptureWorldPreview()
    {
        bool activity=OS.GetCmdlineUserArgs().Contains("--activity-preview");
        bool maxFocus=OS.GetCmdlineUserArgs().Contains("--pet-max-preview");
        bool petFocus=maxFocus||OS.GetCmdlineUserArgs().Contains("--pet-preview");
        string previewSite=OS.GetCmdlineUserArgs().FirstOrDefault(arg=>arg.StartsWith("--preview-site=",StringComparison.Ordinal))?[15..]??"";
        string previewHour=OS.GetCmdlineUserArgs().FirstOrDefault(arg=>arg.StartsWith("--preview-hour=",StringComparison.Ordinal))?[15..]??"";
        if(int.TryParse(previewHour,out int hour)&&hour>=0&&hour<24){
            Game.Meta["sim_ts"]=Math.Floor(Game.Now/NativeGame.Day)*NativeGame.Day+hour*NativeGame.Hour;
            UpdateAtmosphere();
        }
        if(activity){LocateActivityArea();UpdateCamera();}
        else if(petFocus&&GetChildren().OfType<PetAgent>().FirstOrDefault() is PetAgent pet){
            LocatePet(pet.Ident);UpdateCamera();
            if(maxFocus)for(int i=0;i<8;i++)ZoomAt(GetViewport().GetVisibleRect().Size/2,.84f);
        }else if(Data.Root["zones"]![previewSite]!=null&&GetChildren().OfType<MeshInstance3D>().FirstOrDefault(node=>
            node.HasMeta("kind")&&node.GetMeta("kind").AsString()=="zone"&&node.GetMeta("ident").AsString()==previewSite) is MeshInstance3D site){
            _center=site.GlobalPosition;
            _yaw=.54f;_pitch=.56f;
            _distance=Math.Clamp((site.GetAabb().Size*site.Scale).Length()*1.28f,36,88);
            UpdateCamera();
        }else previewSite="";
        for(int frame=0;frame<5;frame++)await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
        string folder=MigrationOutput;System.IO.Directory.CreateDirectory(folder);
        string file=activity?"pet-activity-preview.png":maxFocus?"pet-max-preview.png":petFocus?"pet-preview.png":
            previewSite.Length==0?"world-preview.png":$"world-{previewSite}-{previewHour}.png";
        GetViewport().GetTexture().GetImage().SavePng(System.IO.Path.Combine(folder,file));
        GD.Print("NATIVE_WORLD_PREVIEW_SAVED");GetTree().Quit();
    }
    public override void _Notification(int what){if(what==NotificationApplicationFocusOut){CancelTownPointer();CancelRoadStroke();}}
}
