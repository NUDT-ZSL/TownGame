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
    Vector3 _center=WorldData.Expand(new(150,0,89));
    float _yaw=.56f,_pitch=.85f,_distance=170;
    double _clock,_lastHud;
    string _lastPhase="",_selection="点击人物或宠物查看资料";
    Label _stats=null!,_details=null!;
    Control _panel=null!;
    int _frames;
    long _benchmarkStart,_lastFrameStamp;
    bool _smoke,_benchmark,_businessBenchmark;
    ShaderMaterial _material=null!;
    readonly StandardMaterial3D _red=new(){AlbedoColor=new Color("ed6055"),EmissionEnabled=true,Emission=new Color("9a3028")};
    readonly StandardMaterial3D _green=new(){AlbedoColor=new Color("64e59d"),EmissionEnabled=true,Emission=new Color("288550")};
    string SavePath=>ProjectSettings.GlobalizePath("user://native-save.json");

    public override void _Ready()
    {
        try {
            _farmVerification=OS.GetCmdlineUserArgs().Contains("--verify-farm-manager");
            _freightVerification=OS.GetCmdlineUserArgs().Contains("--verify-freight");
            _residenceVerification=OS.GetCmdlineUserArgs().Contains("--verify-residences");
            _smoke=OS.GetCmdlineUserArgs().Contains("--smoke");_businessBenchmark=OS.GetCmdlineUserArgs().Contains("--benchmark-simulation");_benchmark=_businessBenchmark||OS.GetCmdlineUserArgs().Contains("--benchmark");
            if(_benchmark)DisplayServer.WindowSetVsyncMode(DisplayServer.VSyncMode.Disabled);
            var file=ProjectSettings.GlobalizePath("res://Content/world.json");
            if(!System.IO.File.Exists(file))throw new Exception("缺少Content/world.json，请先运行tools/export_godot.py");
            JsonObject root=JsonNode.Parse(System.IO.File.ReadAllText(file))!.AsObject();
            if(System.IO.File.Exists(SavePath)&&!_smoke&&!_benchmark&&!_residenceVerification&&!_farmVerification&&!_freightVerification&&!OS.GetCmdlineUserArgs().Contains("--verify-rules")&&!OS.GetCmdlineUserArgs().Contains("--ui-smoke")) {
                var saved=JsonNode.Parse(System.IO.File.ReadAllText(SavePath))!.AsObject();
                if(WorldData.I(saved,"version")==1&&saved["snapshot"] is JsonObject)root["snapshot"]=saved["snapshot"]!.DeepClone();
            }
            Data=new(root);
            Game=new(root);
            MigrateSceneCoordinates();
            Data.Reindex();
            if(_businessBenchmark)Data.Meta["native_speed"]=600;
            if(OS.GetCmdlineUserArgs().Contains("--verify-rules")){NativeVerification.Run(root);_camera.Free();_red.Dispose();_green.Dispose();_ghostGood.Dispose();_ghostBad.Dispose();GetTree().Quit();return;}
            _material=new(){Shader=new Shader{Code=Geometry.Shader}};
            foreach(var mesh in root["meshes"]!.AsArray())_meshes.Add(Geometry.Decode(mesh!.GetValue<string>(),_material));
            CreateEnvironment();CreateWorld();CreateActors();CreateUi();SyncBusinessWorld();
            if(!_smoke&&!_benchmark&&!_residenceVerification&&!_farmVerification&&!_freightVerification&&Data.Meta["native_real_anchor"]!=null) {
                double offline=Math.Max(0,DateTimeOffset.UtcNow.ToUnixTimeSeconds()-WorldData.D(Data.Meta,"native_real_anchor"));
                Game.Advance(offline*WorldData.D(Data.Meta,"native_speed",1));SyncBusinessWorld();
            }
            Data.Meta["native_real_anchor"]=DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            Game.ManagerSceneDriven=true;
            AddChild(_camera);_camera.Current=true;_camera.Near=.8f;_camera.Far=2200;UpdateCamera();
            GetTree().AutoAcceptQuit=false;GetWindow().CloseRequested+=()=>{if(!_smoke&&!_benchmark&&!_residenceVerification&&!_farmVerification&&!_freightVerification)Save();GetTree().Quit();};
            if(OS.GetCmdlineUserArgs().Contains("--ui-smoke")){_selectedPet=WorldData.S(Data.Pets[0]!,"pet_id");foreach(string tab in ManagementTabs)ShowManagement(tab);_management!.Hide();_smoke=true;GD.Print($"NATIVE_UI_VERIFIED tabs={ManagementTabs.Length}");}
            GD.Print($"NATIVE_READY meshes={_meshes.Count} objects={root["objects"]!.AsArray().Count} agents={Agents.Count}");
        } catch(Exception error){GD.PushError(error.ToString());GetTree().Quit(1);}
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
                if(new[]{"building","town_building","zone","prop"}.Contains(key[0]!.ToString())) {
                    var body=new StaticBody3D{CollisionLayer=1};var bounds=instance.Mesh.GetAabb();
                    body.AddChild(new CollisionShape3D{Shape=new BoxShape3D{Size=bounds.Size.Max(new Vector3(.1f,.1f,.1f))},Position=bounds.GetCenter()});
                    body.SetMeta("kind",key[0]!.ToString());body.SetMeta("ident",key[1]!.ToString());instance.AddChild(body);
                }
            }
        }
        CreateSignals();
    }
    Node3D? _signalRoot;
    void CreateSignals(){CreateBoulevardSignals();_lastPhase="";}
    public Mesh Prototype(string name)=>_meshes[Data.Root["prototypes"]![name]!.GetValue<int>()];
    void CreateActors()
    {
        foreach(var p in Data.People) {
            var actor=new RoadAgent{Town=this,Ident=WorldData.S(p!,"person_id"),Position=ResidentSpawn(p!),Speed=2.5f};
            actor.Visual.Mesh=Prototype("person-0");actor.SetMeta("kind","person");actor.SetMeta("ident",actor.Ident);
            AddChild(actor);Agents.Add(actor);AssignRoute(actor);
        }
        int petIndex=0;
        foreach(var pet in Data.Pets) {
            if(WorldData.S(pet!,"status")=="死亡"||pet!["fostered"]?.GetValue<bool>()==true)continue;
            var species=WorldData.S(pet!,"species");
            if(Data.Root["prototypes"]![species+":idle:0"] is null)continue;
            var animal=new PetAgent{Town=this,Ident=WorldData.S(pet!,"pet_id"),Species=species,Position=WorldData.Expand(new(130+petIndex*4,0,99))};
            animal.SetMeta("kind","pet");animal.SetMeta("ident",animal.Ident);AddChild(animal);petIndex++;
        }
    }
    public void AssignRoute(RoadAgent actor)
    {
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
        var layer=new CanvasLayer();AddChild(layer);
        var theme=new Theme{DefaultFont=new SystemFont{FontNames=new[]{"Microsoft YaHei","Noto Sans CJK SC"}},DefaultFontSize=18};
        var top=new PanelContainer{Position=new(18,16),CustomMinimumSize=new(1020,112),Theme=theme};layer.AddChild(top);
        var column=new VBoxContainer();top.AddChild(column);
        var row=new HBoxContainer();column.AddChild(row);
        row.AddChild(new Label{Text="  慢时光 · Godot / C#  "});
        Button Button(string text,Action action){var button=new Button{Text=text};button.Pressed+=action;row.AddChild(button);return button;}
        Button("暂停/继续",()=>Paused=!Paused);Button("回到庄园",()=>_center=WorldData.Expand(new(150,0,89)));Button("经营管理",()=>ShowManagement("宠物"));Button("速度×1/120/600",()=>{int speed=WorldData.I(Data.Meta,"native_speed",1);Data.Meta["native_speed"]=speed==1?120:speed==120?600:1;});Button("保存",Save);
        var districts=new OptionButton();row.AddChild(districts);
        foreach(var zone in Data.Root["zones"]!.AsObject())districts.AddItem(WorldData.S(zone.Value!,"name"));
        districts.ItemSelected+=index=> {
            var selectedZone=Data.Root["zones"]!.AsObject().ElementAt((int)index);var rect=selectedZone.Value!["rect"]!;
            _center=WorldData.Expand(new(((float)rect[0]!.GetValue<double>()+(float)rect[2]!.GetValue<double>()/2)/12,0,((float)rect[1]!.GetValue<double>()+(float)rect[3]!.GetValue<double>()/2)/12));
            var sites=Game.UrbanAccessPoints().Where(a=>a.Zone==selectedZone.Key&&a.Entrance!=null).ToArray();
            if(sites.Length>0&&selectedZone.Key!="estate")_center=new((float)sites.Average(a=>a.DoorX)*WorldData.GridSize,0,(float)sites.Average(a=>a.DoorY)*WorldData.GridSize);
            _distance=selectedZone.Key=="estate"?170:120;
        };
        _stats=new Label();column.AddChild(_stats);
        column.AddChild(new Label{Text="  WASD 平移 · 右键拖动环绕 · 滚轮缩放 · F5保存 · F12截图"});
        _panel=new PanelContainer{Position=new(18,145),CustomMinimumSize=new(480,180),Theme=theme};layer.AddChild(_panel);
        var body=new VBoxContainer();_panel.AddChild(body);body.AddChild(new Label{Text="  C#原生经营版本 · 迁移中"});
        _details=new Label{Text=_selection,AutowrapMode=TextServer.AutowrapMode.WordSmart,CustomMinimumSize=new(450,65)};body.AddChild(_details);
        body.AddChild(new Label{Text="照护、商店、建造、自治与家庭已接入C#\n打开经营管理查看资源和任务。"});
        var feed=new Button{Text="给选中的宠物喂普通饲料"};feed.Pressed+=FeedSelected;body.AddChild(feed);
    }
    string _selectedPet="";
    void FeedSelected()
    {
        Act("feed",new JsonObject{{"pet_id",_selectedPet},{"item","普通饲料"}});
    }
    public override void _UnhandledInput(InputEvent input)
    {
        if(_benchmark||_smoke)return;
        if(input is InputEventMouseButton button) {
            if(button.ButtonIndex==MouseButton.Right)Input.MouseMode=button.Pressed?Input.MouseModeEnum.Captured:Input.MouseModeEnum.Visible;
            if(button.Pressed&&button.ButtonIndex==MouseButton.WheelUp)_distance=Math.Max(20,_distance*.9f);
            if(button.Pressed&&button.ButtonIndex==MouseButton.WheelDown)_distance=Math.Min(900,_distance*1.1f);
            if(button.Pressed&&button.ButtonIndex==MouseButton.Left)Select(button.Position);
        }
        if(input is InputEventMouseMotion motion&&Input.MouseMode==Input.MouseModeEnum.Captured) {
            _yaw-=motion.Relative.X*.004f;_pitch=Math.Clamp(_pitch+motion.Relative.Y*.003f,.25f,1.4f);
        }
        if(input is InputEventKey key&&key.Pressed&&!key.Echo) {
            if(key.Keycode==Key.F5)Save();
            if(key.Keycode==Key.F12)_=Screenshot();
            if(key.Keycode==Key.Escape){Input.MouseMode=Input.MouseModeEnum.Visible;_placementAction="";_ghost?.Hide();}
        }
    }
    void Select(Vector2 screen)
    {
        if(_placementAction!=""){var cell=PlacementCell(screen);var args=_placementArgs.DeepClone().AsObject();args["cx"]=cell.X;args["cy"]=cell.Y;Act(_placementAction,args);return;}
        var from=_camera.ProjectRayOrigin(screen);var query=PhysicsRayQueryParameters3D.Create(from,from+_camera.ProjectRayNormal(screen)*1500);
        var result=GetWorld3D().DirectSpaceState.IntersectRay(query);if(result.Count==0)return;
        var collider=result["collider"].AsGodotObject();if(!collider.HasMeta("kind"))return;
        string kind=collider.GetMeta("kind").AsString(),id=collider.GetMeta("ident").AsString();
        if(kind=="manager"){SelectFarmManager();return;}
        if(kind=="vehicle"){_selectedPet="";_selection=FreightDescription(id);return;}
        if(kind=="waste"){Act("clean_waste",new JsonObject{{"waste_id",id}});return;}
        if(kind=="town_building"){SelectResidence(id);return;}
        if(kind=="building"){_selection="这是庄园宠物建筑。居民家庭住在带门牌的住宅，进入经营管理 → 家庭可以定位。";return;}
        if(kind=="zone"){_selection="这是街区地标。居民家庭住在带门牌的住宅，进入经营管理 → 家庭可以定位。";return;}
        _selectedPet=kind=="pet"?id:"";
        var item=(kind=="pet"?Data.Pets:Data.People).FirstOrDefault(p=>WorldData.S(p!,kind=="pet"?"pet_id":"person_id")==id);
        _selection=item==null?$"{kind} / {id}":WorldData.S(item,"name")+"\n"+(kind=="pet"?$"{WorldData.S(item,"species")} · 饱食 {WorldData.D(item,"hunger"):0}":$"{WorldData.S(item,"role")} · {WorldData.S(item,"profession")}");
        if(kind=="person"&&item!=null&&NativeGame.S(item,"role")=="居民")_selection=ResidentDescription(item);
    }
    public override void _Process(double delta)
    {
        if(Data==null||Game==null||_stats==null||!_camera.IsInsideTree())return;
        if(!Paused)_clock+=delta;
        if(!Paused&&!_smoke&&!_residenceVerification&&!_farmVerification&&!_freightVerification&&(!_benchmark||_businessBenchmark)){_simulationAccumulator+=_businessBenchmark?1d/60:delta;if(_simulationAccumulator>=1){try{Game.Advance(_simulationAccumulator*WorldData.D(Data.Meta,"native_speed",1));SyncBusinessWorld();}catch(Exception error){if(_businessBenchmark){GD.PushError(error.ToString());GetTree().Quit(1);}Paused=true;_selection="模拟已暂停："+error.Message;}_simulationAccumulator=0;}}
        if(!_smoke&&!_benchmark&&!_residenceVerification&&!_farmVerification&&!_freightVerification){_autosaveTime+=delta;if(_autosaveTime>=15){Save();_autosaveTime=0;}}
        float x=(Input.IsPhysicalKeyPressed(Key.D)?1:0)-(Input.IsPhysicalKeyPressed(Key.A)?1:0);
        float z=(Input.IsPhysicalKeyPressed(Key.S)?1:0)-(Input.IsPhysicalKeyPressed(Key.W)?1:0);
        var forward=new Vector3(-Mathf.Sin(_yaw),0,-Mathf.Cos(_yaw));var right=new Vector3(Mathf.Cos(_yaw),0,-Mathf.Sin(_yaw));
        var move=right*x-forward*z;if(!_benchmark&&!_smoke&&(_management==null||!_management.Visible)&&move.LengthSquared()>0)_center+=move.Normalized()*(float)delta*_distance*.55f;
        _center.X=Math.Clamp(_center.X,0,WorldData.WorldWidth);_center.Z=Math.Clamp(_center.Z,0,WorldData.WorldDepth);UpdateCamera();
        UpdatePlacement();
        UpdateResidentialView(delta);
        UpdateFarmView(delta);
        UpdateFreight(delta);
        UpdateDistrictActivity(delta);
        if(_lastPhase!=Phase){UpdateBoulevardSignals(Phase);_lastPhase=Phase;}
        _lastHud+=delta;
        if(_lastHud>.2){_stats.Text=$"  FPS {Engine.GetFramesPerSecond()} · {GameClockText} · 金币 {WorldData.I(Data.Meta,"coins")} · ×{WorldData.I(Data.Meta,"native_speed",1)} · {Game.Season} / {Game.Weather} · 宠物 {Data.Pets.Count(p=>Game.Active(p!))} · 任务 {Game.Jobs.Count}";_details.Text=_selection;_lastHud=0;}
        _frames++;
        if(_residenceVerification)VerifyResidenceScene(delta);
        if(_farmVerification)VerifyFarmManagerScene(delta);
        if(_freightVerification)VerifyFreightScene(delta);
        long stamp=System.Diagnostics.Stopwatch.GetTimestamp();
        if(_frames==300)_benchmarkStart=stamp;
        if(_frames>300)_samples.Add(System.Diagnostics.Stopwatch.GetElapsedTime(_lastFrameStamp,stamp).TotalMilliseconds);
        _lastFrameStamp=stamp;
        if((_smoke&&_frames>=5)||(_benchmark&&_frames>=1800)) {
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
    void UpdateCamera(){_camera.Position=_center+new Vector3(Mathf.Sin(_yaw)*Mathf.Cos(_pitch),Mathf.Sin(_pitch),Mathf.Cos(_yaw)*Mathf.Cos(_pitch))*_distance;_camera.LookAt(_center,Vector3.Up);}
    void Save(){try{Data.Meta["native_real_anchor"]=DateTimeOffset.UtcNow.ToUnixTimeSeconds();Data.Save(SavePath);_selection="新版本存档已保存；原SQLite存档保持原样。";}catch(Exception error){_selection="保存失败："+error.Message;}}
    async System.Threading.Tasks.Task Screenshot(){await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);GetViewport().GetTexture().GetImage().SavePng(ProjectSettings.GlobalizePath("user://native-preview.png"));}
    public override void _Notification(int what){if(what==NotificationApplicationFocusOut)Input.MouseMode=Input.MouseModeEnum.Visible;}
}
