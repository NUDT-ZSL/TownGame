using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;

public partial class Main
{
    double _simulationAccumulator;
    double _autosaveTime;
    long _businessRevision=-1;
    Window? _management;
    VBoxContainer? _managementBody;
    string _tab="宠物";
    readonly Dictionary<string,Node3D> _dynamic=new();

    void Act(string action,JsonObject args)
    {
        try {
            _selection=Game.Execute(action,args,()=>{Data.Meta["native_real_anchor"]=DateTimeOffset.UtcNow.ToUnixTimeSeconds();Data.Save(SavePath);});
            if(action is "feed" or "bath" or "play")foreach(var pet in GetChildren().OfType<PetAgent>().Where(p=>p.Ident==NativeGame.S(args,"pet_id")))pet.React(action=="feed"?"eat":"pet");
            SyncBusinessWorld();
        } catch(Exception error){Data.Reindex();_selection=error.Message;}
        if(_management?.Visible==true)ShowManagement(_tab);
    }
    void SyncBusinessWorld()
    {
        if(_businessRevision==Game.WorldRevision)return;
        _businessRevision=Game.WorldRevision;Data.Reindex();
        string signature=NativeGame.I(Data.Meta,"road_revision")+Data.Snapshot["buildings"]!.ToJsonString()+Data.Snapshot["town_buildings"]!.ToJsonString()+Data.Meta["decorations"]!.ToJsonString();
        if(signature!=_layoutSignature){_layoutSignature=signature;
        CreateSignals();
        foreach(var n in _dynamic.Values)n.QueueFree();_dynamic.Clear();
        var cleared=Data.Meta["town_cleared"]!.AsArray().Select(c=>c!.GetValue<string>()).ToHashSet();foreach(var (cell,node) in _natural)node.Visible=!cleared.Contains($"{cell.X},{cell.Y}");
        void Put(string id,Mesh mesh,Vector3 position,float angle=0,float scale=1,bool collision=false,string kind="") {
            var n=new MeshInstance3D{Mesh=mesh,Position=position,Rotation=new(0,angle,0),Scale=(kind=="road"?Vector3.One:new Vector3(WorldData.LayoutScale,1,WorldData.LayoutScale))*scale};AddChild(n);_dynamic[id]=n;
            if(collision){var box=mesh.GetAabb();var size=box.Size.Max(Vector3.One*.05f);var center=box.GetCenter();
                if(kind=="building"){
                    var plot=Game.Rows("buildings").First(b=>NativeGame.I(b!,"building_id").ToString()==id[(id.IndexOf(':')+1)..])!;
                    if(NativeGame.S(plot,"kind")!=""){size.X=128/12f*.88f;size.Z=70/12f*.83f;center.X=0;center.Z=0;}
                }
                if(kind=="town_building"){
                    var house=Game.Rows("town_buildings").First(b=>NativeGame.S(b!,"building_id")==id[(id.IndexOf(':')+1)..])!;
                    if(Game.IsResidentialHome(house)){
                        // 台阶和屋檐不能扩张整栋房子的行人碰撞盒，否则它会堵住门前道路。
                        var footprint=Game.Rules["CATALOG"]![NativeGame.S(house,"catalog_key")]!["footprint"]!;
                        size.X=footprint[0]!.GetValue<int>()*4*.88f;size.Z=footprint[1]!.GetValue<int>()*4*.83f;center.X=0;center.Z=0;
                    }
                }
                bool emptyPlot=kind=="building"&&NativeGame.S(Game.Rows("buildings").First(b=>NativeGame.I(b!,"building_id").ToString()==id[(id.IndexOf(':')+1)..])!,"kind")=="";
                var body=new StaticBody3D{CollisionLayer=emptyPlot?8u:1u,CollisionMask=0};body.SetMeta("kind",kind);body.SetMeta("ident",id[(id.IndexOf(':')+1)..]);body.AddChild(new CollisionShape3D{Shape=new BoxShape3D{Size=size},Position=center});n.AddChild(body);}
        }
        foreach(var pair in Data.Roads) {
            int mask=0;int bit=0;foreach(var d in new[]{Vector2I.Up,Vector2I.Right,Vector2I.Down,Vector2I.Left}){if(Data.Roads.ContainsKey(pair.Key+d))mask|=1<<bit;bit++;}
            Put("road:"+pair.Key,RoadMesh(mask,WorldData.S(pair.Value,"road_style","asphalt")),WorldData.Cell(pair.Key),kind:"road");
        }
        foreach(var b in Data.Snapshot["buildings"]!.AsArray()) {
            int id=WorldData.I(b!,"building_id");string kind=WorldData.S(b!,"kind");
            if(kind!="")Put("building:"+id,Prototype($"manor:{kind}:{WorldData.I(b!,"level",1)}"),WorldData.Expand(new((1314+id%5*230)/12f,0,(870+id/5*88)/12f)),collision:true,kind:"building");
            else {
                var mesh=new BoxMesh{Size=new(10.6f,.08f,6.6f),Material=new StandardMaterial3D{AlbedoColor=NativeGame.B(b!,"unlocked")?new Color("a7bd7b"):new Color("879b6a")}};
                Put("building:"+id,mesh,WorldData.Expand(new((1312+id%5*230)/12f,0,(858+id/5*88)/12f)),collision:true,kind:"building");
            }
        }
        foreach(var b in Data.Snapshot["town_buildings"]!.AsArray()) {
            string key=WorldData.S(b!,"catalog_key");double progress=(Game.Now-WorldData.D(b!,"placed_ts"))/Math.Max(1,WorldData.D(b!,"finish_ts")-WorldData.D(b!,"placed_ts"));
            int stage=WorldData.S(b!,"status")=="ready"?4:Math.Clamp((int)(progress*4),0,3);
            Put("town_building:"+WorldData.S(b!,"building_id"),Prototype($"town:{key}:{stage}"),new((WorldData.I(b!,"cx")+WorldData.I(b!,"w")*.5f)*WorldData.GridSize,0,(WorldData.I(b!,"cy")+WorldData.I(b!,"h")*.5f)*WorldData.GridSize),-WorldData.I(b!,"orientation")*Mathf.Pi/2,collision:true,kind:"town_building");
            if(Game.IsResidentialHome(b!)&&stage==4){var marker=new Label3D{Text="住宅 · 点击查看家庭",Font=new SystemFont{FontNames=new[]{"Microsoft YaHei"}},FontSize=32,PixelSize=.018f,Billboard=BaseMaterial3D.BillboardModeEnum.Enabled,NoDepthTest=false,Position=Vector3.Up*15,Modulate=new Color("ffdf9d")};_dynamic["town_building:"+WorldData.S(b!,"building_id")].AddChild(marker);}
        }
        foreach(var d in Data.Meta["decorations"]!.AsArray())Put("decoration:"+WorldData.S(d!,"id"),Prototype("decoration:"+WorldData.S(d!,"kind")),WorldData.Cell(new(WorldData.I(d!,"cx"),WorldData.I(d!,"cy"))),-WorldData.I(d!,"orientation")*Mathf.Pi/2,WorldData.S(d!,"kind").StartsWith("tree_")?.8f:1,collision:true,kind:"decoration");
        CreateDistrictWalkways();
        foreach(var a in Agents)AssignRoute(a);
        }
        var people=Data.People.Select(p=>WorldData.S(p!,"person_id")).ToHashSet();
        foreach(var a in Agents.Where(a=>!a.IsCar&&!people.Contains(a.Ident)).ToArray()){Agents.Remove(a);a.QueueFree();}
        foreach(var p in Data.People) {
            string id=WorldData.S(p!,"person_id");var actor=Agents.FirstOrDefault(a=>a.Ident==id);
            if(actor==null){actor=new RoadAgent{Town=this,Ident=id,Position=ResidentSpawn(p!)};actor.Visual.Mesh=Prototype("person-0");actor.SetMeta("kind","person");actor.SetMeta("ident",id);AddChild(actor);Agents.Add(actor);AssignRoute(actor);}
            actor.Visual.Scale=Vector3.One*(Game.Now-WorldData.D(p!,"born_ts",-1e20)<7*86400?.6f:1);
        }
        foreach(var pet in GetChildren().OfType<PetAgent>())if(Game.Pets.Any(p=>WorldData.S(p!,"pet_id")==pet.Ident&&(!Game.Active(p!)||NativeGame.B(p!,"fostered"))))pet.QueueFree();
        foreach(var pet in Data.Pets) {
            string id=WorldData.S(pet!,"pet_id");bool present=GetChildren().OfType<PetAgent>().Any(p=>p.Ident==id&&!p.IsQueuedForDeletion());
            if(Game.Active(pet!)&&!NativeGame.B(pet!,"fostered")&&!present){var actor=new PetAgent{Town=this,Ident=id,Species=WorldData.S(pet!,"species"),Position=WorldData.Expand(new(130+GetChildren().OfType<PetAgent>().Count()*3,0,99))};actor.SetMeta("kind","pet");actor.SetMeta("ident",id);AddChild(actor);}
        }
    }
    string _layoutSignature="";
    void ShowManagement(string tab)
    {
        _tab=tab;
        if(_management==null) {
            _management=new Window{Title="慢时光 · 经营管理",Size=new(900,720),Transient=true,Theme=_panel.Theme};AddChild(_management);_management.CloseRequested+=()=>_management.Hide();
            var scroll=new ScrollContainer();_management.AddChild(scroll);scroll.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
            scroll.OffsetLeft=16;scroll.OffsetTop=12;scroll.OffsetRight=-16;scroll.OffsetBottom=-12;
            _managementBody=new VBoxContainer{SizeFlagsHorizontal=Control.SizeFlags.ExpandFill};scroll.AddChild(_managementBody);
        }
        foreach(var child in _managementBody!.GetChildren()){_managementBody.RemoveChild(child);child.QueueFree();}
        var tabs=new HBoxContainer();_managementBody.AddChild(tabs);
        foreach(string name in ManagementTabs){var button=new Button{Text=name};tabs.AddChild(button);button.Pressed+=()=>ShowManagement(name);}
        Text($"金币 {WorldData.I(Data.Meta,"coins")} · 仓库 {Data.Snapshot["inventory"]!.AsArray().Sum(b=>WorldData.I(b!,"quantity"))}/{Game.Storage} · 第{1+(int)((Game.Now-NativeGame.D(Data.Meta,"start_ts"))/86400)}天 {Game.Now%86400/3600:00}时");
        if(tab!="家庭")Text(_selection);
        if(tab=="家庭")FamilyControls();else if(tab=="宠物")PetControls();else if(tab=="商店")ShopControls();else if(tab=="建造")BuildControls();else if(tab=="自治")AutonomyControls();else if(tab=="环境")EnvironmentControls();else if(tab=="事件")EventControls();
        else if(tab=="订单")foreach(var order in Data.Snapshot["orders"]!.AsArray()) {
            Text($"{WorldData.S(order!,"npc")} · {WorldData.S(order!,"item")}×{WorldData.I(order!,"quantity")} · {WorldData.S(order!,"status")}");
            if(WorldData.S(order!,"status")!="进行中")continue;
            if(WorldData.S(order!,"kind")=="item")ActionButton("交付订单",()=>Act("order",new JsonObject{{"order_id",WorldData.S(order!,"order_id")}}));
            else{Text($"需要：{WorldData.S(order!,"species")} · {WorldData.S(order!,"stage")} · 奖励{WorldData.I(order!,"reward")}金币");ActionButton("交付当前选中宠物（需健康且非孕期）",()=>Act("order",new JsonObject{{"order_id",WorldData.S(order!,"order_id")},{"pet_id",_selectedPet}}));}
        } else if(tab=="日志")foreach(var row in Data.Snapshot["logs"]!.AsArray().Reverse().Take(80))Text(WorldData.S(row!,"text"));
        if(!_management.Visible)_management.PopupCentered();
    }
    static readonly string[] ManagementTabs={"宠物","家庭","商店","建造","自治","环境","订单","事件","日志"};
    Label Text(string text){var label=new Label{Text=text,AutowrapMode=TextServer.AutowrapMode.WordSmart,CustomMinimumSize=new(840,0)};_managementBody!.AddChild(label);return label;}
    void ActionButton(string text,Action action){var button=new Button{Text=text};button.Pressed+=action;_managementBody!.AddChild(button);}
    OptionButton Options(IEnumerable<string> values){var option=new OptionButton();foreach(string value in values)option.AddItem(value);_managementBody!.AddChild(option);return option;}
    void PetControls()
    {
        foreach(var p in Data.Pets)ActionButton($"{WorldData.S(p!,"name")} · {WorldData.S(p!,"species")} · {Game.Stage(p!)} · 饱食{WorldData.D(p!,"hunger"):0} / 健康{WorldData.D(p!,"health"):0}",()=>{_selectedPet=WorldData.S(p!,"pet_id");ShowManagement("宠物");});
        var pet=Data.Pets.FirstOrDefault(p=>WorldData.S(p!,"pet_id")==_selectedPet);if(pet==null)return;
        Text($"当前：{WorldData.S(pet,"name")} · {WorldData.S(pet,"sex")} · {WorldData.S(pet,"illness_type")} · 伤势{WorldData.D(pet,"injury"):0} · 寄养{NativeGame.B(pet,"fostered")}");
        if(!Game.Active(pet))return;
        var rename=new LineEdit{Text=WorldData.S(pet,"name"),MaxLength=16};_managementBody!.AddChild(rename);ActionButton("保存名字",()=>Act("rename",new JsonObject{{"pet_id",_selectedPet},{"name",rename.Text}}));
        foreach(var (label,action) in new[]{("喂普通饲料","feed"),("洗澡","bath"),("互动","play"),("收取产物","collect"),("送医住院","hospitalize"),("治疗（需先住院）","treat")})ActionButton(label,()=>Act(action,new JsonObject{{"pet_id",_selectedPet}}));
        var homes=Data.Snapshot["buildings"]!.AsArray().Where(b=>WorldData.S(b!,"kind")!="").ToArray();var home=Options(homes.Select(b=>$"地块{WorldData.I(b!,"building_id")} · {WorldData.S(b!,"kind")}"));
        if(homes.Length>0)ActionButton("迁入所选居所 / 寄养",()=>Act("move",new JsonObject{{"pet_id",_selectedPet},{"building_id",WorldData.I(homes[home.Selected]!,"building_id")}}));
        ActionButton("出售给新家庭（需要商店）",()=>Act("sell_pet",new JsonObject{{"pet_id",_selectedPet}}));
        if(WorldData.S(pet,"mate_id")!="")ActionButton("解除伴侣",()=>Act("unpair",new JsonObject{{"pet_id",_selectedPet}}));
        var mates=Data.Pets.Where(p=>p!=pet&&Game.Active(p!)&&WorldData.S(p!,"species")==WorldData.S(pet,"species")&&WorldData.S(p!,"sex")!=WorldData.S(pet,"sex")).ToArray();
        if(mates.Length>0){var mate=Options(mates.Select(p=>WorldData.S(p!,"name")));ActionButton("结为伴侣",()=>Act("pair",new JsonObject{{"pet_id",_selectedPet},{"mate_id",WorldData.S(mates[mate.Selected]!,"pet_id")}}));ActionButton("与所选伙伴繁育（需先同居）",()=>Act("breed",new JsonObject{{"pet_id",_selectedPet},{"mate_id",WorldData.S(mates[mate.Selected]!,"pet_id")}}));}
    }
    void ShopControls()
    {
        foreach(var pair in Game.Rules["ITEMS"]!.AsObject()) {
            string item=pair.Key;Text($"{item} · 库存{Game.Stock(item)} · 买价{Game.BuyPrice(item)} / 售价{NativeGame.I(pair.Value!,"sell")}");
            if(NativeGame.I(pair.Value!,"price")>0)ActionButton("买入5份 "+item,()=>Act("buy",new JsonObject{{"item",item},{"quantity",5}}));
            if(Game.Stock(item)>0)ActionButton("卖出1份 "+item,()=>Act("sell",new JsonObject{{"item",item},{"quantity",1}}));
        }
        foreach(var recipe in Game.Rules["RECIPES"]!.AsObject())ActionButton("加工 "+recipe.Key,()=>Act("craft",new JsonObject{{"item",recipe.Key}}));
        Text("新伙伴性别：");var sex=Options(new[]{"雌","雄"});
        foreach(var spec in Game.Rules["SPECIES"]!.AsObject()){
            ActionButton("购买幼崽 "+spec.Key,()=>Act("buy_pet",new JsonObject{{"species",spec.Key},{"sex",sex.GetItemText(sex.Selected)}}));
            if(spec.Value!["egg_available"]==null||NativeGame.B(spec.Value,"egg_available"))ActionButton("购买并孵化宠物蛋 "+spec.Key,()=>Act("buy_pet",new JsonObject{{"species",spec.Key},{"sex",sex.GetItemText(sex.Selected)},{"egg",true}}));
        }
        ActionButton("公益领养（一天一次，需有适配空位）",()=>Act("adopt",new JsonObject()));
    }
    void BuildControls()
    {
        var slots=Data.Snapshot["buildings"]!.AsArray().ToArray();var slot=Options(slots.Select(b=>$"地块{WorldData.I(b!,"building_id")} · {WorldData.S(b!,"kind","空地")}"));
        var kinds=Game.Rules["BUILDINGS"]!.AsObject().Select(p=>p.Key).ToArray();var kind=Options(kinds);
        ActionButton("解锁地块",()=>Act("unlock",new JsonObject{{"building_id",WorldData.I(slots[slot.Selected]!,"building_id")}}));
        ActionButton("建造庄园建筑",()=>Act("build_manor",new JsonObject{{"building_id",WorldData.I(slots[slot.Selected]!,"building_id")},{"kind",kinds[kind.Selected]}}));
        ActionButton("升级所选建筑",()=>Act("upgrade_manor",new JsonObject{{"building_id",WorldData.I(slots[slot.Selected]!,"building_id")}}));
        ActionButton("拆除所选庄园建筑（需先迁走伙伴）",()=>Act("demolish_manor",new JsonObject{{"building_id",WorldData.I(slots[slot.Selected]!,"building_id")}}));
        Text("城镇建造：格坐标，x=0～124 / y=0～79，需临街且街区开放。");
        var x=new SpinBox{MinValue=0,MaxValue=124,Value=20};var y=new SpinBox{MinValue=0,MaxValue=79,Value=20};_managementBody!.AddChild(x);_managementBody.AddChild(y);
        var keys=Game.Rules["CATALOG"]!.AsObject().Select(p=>p.Key).ToArray();var catalog=Options(keys.Select(k=>NativeGame.S(Game.Rules["CATALOG"]![k]!,"name")));
        var orientation=Options(new[]{"朝北","朝东","朝南","朝西"});
        var styles=Game.Rules["ROAD_STYLES"]!.AsObject().Select(p=>p.Key).ToArray();var style=Options(styles.Select(k=>NativeGame.S(Game.Rules["ROAD_STYLES"]![k]!,"name")));
        ActionButton("在该格铺路/改铺",()=>Act("road",new JsonObject{{"cx",(int)x.Value},{"cy",(int)y.Value},{"style",styles[style.Selected]}}));
        ActionButton("鼠标铺路（Esc退出）",()=>ArmPlacement("road",new JsonObject{{"style",styles[style.Selected]}}));
        ActionButton("拆除该格道路",()=>Act("remove_road",new JsonObject{{"cx",(int)x.Value},{"cy",(int)y.Value}}));
        ActionButton("撤销最近一笔道路操作",()=>Act("undo_road",new JsonObject()));
        ActionButton("在该格开工建筑",()=>Act("build_town",new JsonObject{{"cx",(int)x.Value},{"cy",(int)y.Value},{"catalog_key",keys[catalog.Selected]},{"orientation",orientation.Selected}}));
        ActionButton("鼠标放置所选建筑（Esc退出）",()=>ArmPlacement("build_town",new JsonObject{{"catalog_key",keys[catalog.Selected]},{"orientation",orientation.Selected}}));
        foreach(var b in Game.Rows("town_buildings"))ActionButton("拆除 "+NativeGame.S(Game.Rules["CATALOG"]![NativeGame.S(b!,"catalog_key")]!,"name")+$" ({NativeGame.I(b!,"cx")},{NativeGame.I(b!,"cy")})",()=>Act("remove_town",new JsonObject{{"building_id",NativeGame.S(b!,"building_id")}}));
        var decorations=Game.Rules["DECORATIONS"]!.AsObject().Select(p=>p.Key).ToArray();var decoration=Options(decorations.Select(k=>Game.Rules["DECORATIONS"]![k]![0]!.GetValue<string>()));
        ActionButton("在该格摆放装饰",()=>Act("decoration",new JsonObject{{"operation","place"},{"kind",decorations[decoration.Selected]},{"cx",(int)x.Value},{"cy",(int)y.Value},{"orientation",orientation.Selected}}));
        ActionButton("撤销最近装饰操作",()=>Act("decoration",new JsonObject{{"operation","undo"}}));
        foreach(var d in Data.Meta["decorations"]!.AsArray()){
            Text(Game.Rules["DECORATIONS"]![NativeGame.S(d!,"kind")]![0]!.GetValue<string>()+$" ({NativeGame.I(d!,"cx")},{NativeGame.I(d!,"cy")})");
            ActionButton("移动/旋转到所选格",()=>Act("decoration",new JsonObject{{"operation","move"},{"id",NativeGame.S(d!,"id")},{"cx",(int)x.Value},{"cy",(int)y.Value},{"orientation",orientation.Selected}}));ActionButton("拆除此装饰",()=>Act("decoration",new JsonObject{{"operation","remove"},{"id",NativeGame.S(d!,"id")}}));
        }
        foreach(var zone in Data.Root["zones"]!.AsObject())if(zone.Key!="estate")ActionButton($"开放/升级 {WorldData.S(zone.Value!,"name")} · 当前Lv{Game.ZoneLevel(zone.Key)}",()=>Act("open_zone",new JsonObject{{"zone",zone.Key}}));
    }
    void AutonomyControls()
    {
        Text(Game.AccessRoadStats);Text(NativeGame.S(Game.Meta,"urban_plan_status"));
        Text(FreightDescription(NativeGame.S(Game.FreightState,"vehicle_id")));
        ActionButton("定位货车与司机",()=>{if(_freightTruck!=null){_center=_freightTruck.Position;_distance=35;_management?.Hide();_selection=FreightDescription(_freightTruck.Ident);}});
        var s=Game.Commons;Text($"自治：{(NativeGame.B(s,"enabled")?"运行中":"暂停")} · 公共预算{NativeGame.I(s,"funds")} · 住房{Game.PopulationCapacity} · 新生儿{NativeGame.I(s,"births")}");
        foreach(var pair in s["stock"]!.AsObject())Text($"{pair.Key}：{pair.Value}");ActionButton("暂停/开启自治",()=>Act("autonomy",new JsonObject()));
        foreach(var job in Game.Jobs)Text($"{WorldData.S(job!,"kind")} · {WorldData.S(job!,"zone")} · 剩余{Math.Max(0,(WorldData.D(job!,"due")-Game.Now)/3600):0.0}小时");
        Text(WorldData.S(s,"blocked"));
    }
    void EnvironmentControls()
    {
        Text($"季节：{Game.Season} · 天气：{Game.Weather} · 4游戏小时更新天气");
        _farmManagerInformation=Text(FarmManagerSummary);
        Text("管理员会前往粪便旁清扫、给饱食度低于70的动物喂普通饲料；无任务时原地待命。");
        Text($"饲养员：{(NativeGame.B(Data.Meta,"caretaker")?"工作中":"未雇佣")} · 雇佣{NativeGame.I(Game.Rules,"CARE_HIRE_COST")}金币 · 每班{NativeGame.I(Game.Rules,"CARE_WAGE")}金币");
        ActionButton("雇佣/解雇饲养员",()=>Act("caretaker",new JsonObject()));
        Text($"管理员雇佣{NativeGame.I(Game.Rules,"MANAGER_HIRE_COST")}金币 · 每班{NativeGame.I(Game.Rules,"MANAGER_WAGE")}金币");
        ActionButton("雇佣/解雇农场管理员",()=>Act("farm_manager",new JsonObject()));
        ActionButton("定位农场管理员",LocateFarmManager);
        foreach(var b in Game.Rows("buildings")){int id=NativeGame.I(b!,"building_id");var exposure=Game.Exposure(id);if(exposure.Total>0)Text($"地块{id} · 粪便{exposure.Total}份 / 陈旧{exposure.Stale}份 · 疾病风险×{exposure.Hazard:0.0}");}
        ActionButton("清扫全部粪便入库",()=>Act("clean_waste",new JsonObject()));ActionButton("清扫并出售全部粪便（需商店）",()=>Act("sell_waste",new JsonObject()));
        Text("待收作物："+Data.Meta["field_batch"]!.ToJsonString());ActionButton("收获农田作物",()=>Act("harvest",new JsonObject()));
        Text($"已收集物种{Game.State["codex"]!.AsObject().Count} · 已获成就{Game.State["achievements"]!.AsObject().Count}");
        foreach(var a in Game.State["achievements"]!.AsObject())Text("成就："+Game.Rules["ACHIEVEMENTS"]![a.Key]![0]!.GetValue<string>());
    }
    void EventControls()
    {
        foreach(var e in Game.Rows("events").Reverse().Take(30)){
            Text($"{WorldData.S(e!,"text")} · {WorldData.S(e!,"status")}");if(WorldData.S(e!,"status")!="待处理")continue;
            var effect=Game.Rules["EVENT_EFFECTS"]![WorldData.S(e!,"kind")]!;
            var names=new Dictionary<string,string>{{"feed","普通饲料消耗"},{"rep","声望奖励"},{"discount","采购价格倍率"},{"cost","金币消耗"},{"reward","金币奖励"},{"passive_reward","自然接待收入"},{"count","产物份数"},{"gift_rep","赠礼声望"},{"health","户外健康变化"},{"cleanliness","户外清洁变化"},{"medicine","药品消耗"},{"chance","感染概率"},{"mult","涨价倍率"},{"snacks","零食消耗"},{"happy","开心变化"}};
            Text(string.Join(" · ",effect.AsObject().Select(p=>$"{names.GetValueOrDefault(p.Key,p.Key)} {p.Value}")));
            for(int i=0;i<2;i++){int option=i;ActionButton(e!["options"]![i]!.GetValue<string>(),()=>Act("event",new JsonObject{{"event_id",WorldData.S(e,"event_id")},{"option",option}}));}
        }
        Text("超时事件会自动选择第二项；采购折扣与涨价持续时间随游戏时钟结算。");
    }
}
