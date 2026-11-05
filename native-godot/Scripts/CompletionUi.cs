using Godot;
using System;
using System.Linq;
using System.Text.Json.Nodes;

public partial class Main
{
    readonly NativePetMesh _petModels=new();int _archivePage,_logPage;string _archiveFilter="全部",_logSearch="";
    public Mesh PetMesh(JsonNode pet,string pose,int frame)=>_petModels.Get(NativeGame.S(pet,"species"),Game.Appearance(pet),NativeGame.B(pet,"rare"),NativeGame.S(pet,"illness_type")!=""||NativeGame.D(pet,"injury")>0,Game.Stage(pet),pose,frame);
    void AppearanceControls()
    {
        var pets=Data.Pets.Where(p=>Game.Active(p!)).ToArray();if(pets.Length==0){Text("庄园暂无可编辑外观的动物。");return;}
        var choices=Options(pets.Select(p=>NativeGame.S(p!,"name")));int selected=Array.FindIndex(pets,p=>NativeGame.S(p!,"pet_id")==_selectedPet);choices.Select(Math.Max(0,selected));
        if(selected<0)_selectedPet=NativeGame.S(pets[0]!,"pet_id");choices.ItemSelected+=index=>{_selectedPet=NativeGame.S(pets[index]!,"pet_id");ShowManagement("外观");};
        var pet=Data.Pets.First(p=>NativeGame.S(p!,"pet_id")==_selectedPet)!;string species=NativeGame.S(pet,"species");int variant=Game.Appearance(pet),draft=variant;
        Text(NativeAppearance.Description(species,variant));
        var container=new SubViewportContainer{CustomMinimumSize=new(420,230),Stretch=true};_managementBody!.AddChild(container);
        var viewport=new SubViewport{Size=new(420,230),RenderTargetUpdateMode=SubViewport.UpdateMode.Always,OwnWorld3D=true,TransparentBg=false};container.AddChild(viewport);
        var model=new MeshInstance3D{Mesh=PetMesh(pet,"idle",0),Scale=Vector3.One*.5f,Rotation=new(0,.5f,0)};viewport.AddChild(model);
        var bounds=model.Mesh.GetAabb();var focus=bounds.GetCenter()*.5f;var camera=new Camera3D{Position=focus+new Vector3(1,.45f,1.6f).Normalized()*Math.Max(3,bounds.Size.Y*.8f),Fov=45,Current=true};viewport.AddChild(camera);camera.LookAt(focus,Vector3.Up);viewport.AddChild(new DirectionalLight3D{RotationDegrees=new(-40,-35,0),LightEnergy=1.5f});
        var groups=species=="兔子"?NativeAppearance.RabbitGroups:NativeAppearance.OtherGroups;
        for(int i=0;i<4;i++){int group=i,step=NativeAppearance.Steps[i];var option=Options(groups[i]);option.Select(variant/step%groups[i].Length);option.ItemSelected+=index=>{draft=draft-draft/step%groups[group].Length*step+(int)index*step;model.Mesh=_petModels.Get(species,draft,NativeGame.B(pet,"rare"),false,Game.Stage(pet),"idle",0);};}
        ActionButton("应用外观",()=>Act("pet_appearance",new JsonObject{{"pet_id",_selectedPet},{"variant",draft}}));
        ActionButton("收藏 / 取消收藏当前预览",()=>Act("favorite_style",new JsonObject{{"species",species},{"variant",draft}}));
        ActionButton("恢复初始外观",()=>Act("reset_appearance",new JsonObject{{"pet_id",_selectedPet}}));
        foreach(var favorite in Data.Meta["style_favorites"]?[species]?.AsArray()??new JsonArray()){int style=favorite!.GetValue<int>();ActionButton("应用收藏："+NativeAppearance.Description(species,style),()=>Act("pet_appearance",new JsonObject{{"pet_id",_selectedPet},{"variant",style}}));}
    }
    void CareControls()
    {
        Text("按需求优先照护；缺资源、寄养中或冷却中的动物会跳过，喂食不会使用腐败物。");
        foreach(var (title,kind) in new[]{("批量喂食","feed"),("批量洗澡","bath"),("批量互动","play"),("批量收取产物","collect")})ActionButton(title,()=>Act("batch_care",new JsonObject{{"kind",kind}}));
        foreach(var pet in Data.Pets.Where(p=>Game.Active(p!)))Text($"{NativeGame.S(pet!,"name")} · 饱食{NativeGame.D(pet!,"hunger"):0} · 清洁{NativeGame.D(pet!,"cleanliness"):0} · {(NativeGame.B(pet!,"fostered")?"寄养暂停照护":NativeGame.D(pet!,"interact_ready_ts")>Game.Now?"互动冷却中":"可互动")}");
    }
    static string Stamp(double ts)=>DateTimeOffset.FromUnixTimeSeconds((long)Math.Clamp(ts,0,253402300799)).UtcDateTime.ToString("yyyy-MM-dd HH:mm");
    void FinanceControls()
    {
        var ledger=Data.Meta["finance_ledger"]!.AsArray();var recent=ledger.Where(r=>NativeGame.D(r!,"ts")>=Game.Now-NativeGame.Day).ToArray();
        Text($"近24游戏小时收入 {recent.Sum(r=>Math.Max(0,NativeGame.I(r!,"delta")))} · 支出 {-recent.Sum(r=>Math.Min(0,NativeGame.I(r!,"delta")))} · 余额 {NativeGame.I(Data.Meta,"coins")}");
        Text($"公共预算 {NativeGame.I(Game.Commons,"funds")} · 个人收支与公共预算分别核算");
        var chart=new FinanceChart{Ledger=ledger,Now=Game.Now,CustomMinimumSize=new(830,230)};_managementBody!.AddChild(chart);
        var period=Options(new[]{"最近7游戏日","最近30游戏日"});period.ItemSelected+=i=>{chart.Days=i==0?7:30;chart.QueueRedraw();};
        Text("收入为绿色，支出为红色；横轴按游戏日统计。");
        var filter=new LineEdit{PlaceholderText="筛选收支原因，回车查看",Text=_financeSearch};_managementBody.AddChild(filter);filter.TextSubmitted+=s=>{_financeSearch=s;_financePage=0;ShowManagement("财务");};
        var rows=ledger.Reverse().Where(r=>NativeGame.S(r!,"reason").Contains(_financeSearch,StringComparison.OrdinalIgnoreCase)).ToArray();
        PageControls(rows.Length,_financePage,p=>{_financePage=p;ShowManagement("财务");});
        foreach(var row in rows.Skip(_financePage*40).Take(40))Text($"{Stamp(NativeGame.D(row!,"ts"))} · {NativeGame.I(row!,"delta"):+0;-0;0} · 余额{NativeGame.I(row!,"balance")} · {NativeGame.S(row!,"reason")}");
    }
    string _financeSearch="";int _financePage;
    void PageControls(int count,int page,Action<int> select)
    {
        int pages=Math.Max(1,(count+39)/40);Text($"共{count}条 · 第{page+1}/{pages}页");if(page>0)ActionButton("上一页",()=>select(page-1));if(page+1<pages)ActionButton("下一页",()=>select(page+1));
    }
    void ArchiveControls()
    {
        var filter=Options(new[]{"全部","在园","离世","出售 / 交付"});filter.Select(Array.IndexOf(new[]{"全部","在园","离世","出售 / 交付"},_archiveFilter));filter.ItemSelected+=i=>{_archiveFilter=filter.GetItemText((int)i);_archivePage=0;ShowManagement("档案");};
        var pets=Data.Pets.Where(p=>_archiveFilter=="全部"||_archiveFilter=="在园"&&Game.Active(p!)||_archiveFilter=="离世"&&NativeGame.D(p!,"death_ts")>0||_archiveFilter=="出售 / 交付"&&NativeGame.S(p!,"departure")!="").ToArray();
        PageControls(pets.Length,_archivePage,p=>{_archivePage=p;ShowManagement("档案");});
        foreach(var p in pets.Skip(_archivePage*40).Take(40))ActionButton($"{NativeGame.S(p!,"name")} · {NativeGame.S(p!,"species")} · {(Game.Active(p!)?"在园":NativeGame.D(p!,"death_ts")>0?"离世":NativeGame.S(p!,"departure"))}",()=>{_selectedPet=NativeGame.S(p!,"pet_id");_petHistoryPage=0;ShowManagement("档案");});
        var pet=Data.Pets.FirstOrDefault(p=>NativeGame.S(p!,"pet_id")==_selectedPet);if(pet==null){ArchiveExtras();return;}
        string Relative(string key){string id=NativeGame.S(pet,key);return Data.Pets.Where(p=>NativeGame.S(p!,"pet_id")==id).Select(p=>NativeGame.S(p!,"name")).FirstOrDefault()??"无";}
        Text($"{NativeGame.S(pet,"name")} · {NativeGame.S(pet,"sex")} · {NativeGame.S(pet,"personality")} · 年龄{Game.PetAge(pet)/86400:0.00}游戏日 · {(NativeGame.B(pet,"rare")?"稀有":"普通")}");
        Text($"健康{NativeGame.D(pet,"health"):0} / 饱食{NativeGame.D(pet,"hunger"):0} / 开心{NativeGame.D(pet,"happy"):0} / 清洁{NativeGame.D(pet,"cleanliness"):0} / 好感{NativeGame.D(pet,"favor"):0}");
        Text($"伴侣 {Relative("mate_id")} · 母亲 {Relative("mother_id")} · 父亲 {Relative("father_id")} · 居所地块{NativeGame.I(pet,"building_id")}");
        Text($"出生 {Stamp(NativeGame.D(pet,"birth_ts"))} · 离世原因 {NativeGame.S(pet,"death_reason")} · 离园 {NativeGame.S(pet,"departure")} · 疾病 {NativeGame.S(pet,"illness_type")}");
        if(NativeGame.D(pet,"pregnancy_due_ts")>0)Text("预产期："+Stamp(NativeGame.D(pet,"pregnancy_due_ts")));
        var history=pet["history"]!.AsArray().Reverse().ToArray();Text("一生的足迹");PageControls(history.Length,_petHistoryPage,p=>{_petHistoryPage=p;ShowManagement("档案");});foreach(var note in history.Skip(_petHistoryPage*40).Take(40))Text(Stamp(NativeGame.D(note!,"ts"))+" · "+NativeGame.S(note!,"text"));
        ArchiveExtras();
    }
    int _archiveRecordsPage;
    void ArchiveExtras()
    {
        Text("物种图鉴");foreach(var species in Game.Rules["SPECIES"]!.AsObject()){var entry=Game.State["codex"]?[species.Key];Text($"{species.Key} · {(entry==null?"未收集":"已收集")} · 稀有{(entry!=null&&NativeGame.B(entry,"rare")?"已发现":"未发现")} · 寿终{(entry!=null&&NativeGame.B(entry,"natural_death")?"已记录":"未记录")}");}
        Text("订单 / 事件历史");var orders=Game.Rows("order_archive").Concat(Game.Rows("orders")).Where(r=>NativeGame.S(r!,"status")!="进行中").Select(r=>(Kind:"订单",Row:r!));
        var events=Game.Rows("event_archive").Concat(Game.Rows("events")).Where(r=>NativeGame.S(r!,"status")!="待处理").Select(r=>(Kind:"事件",Row:r!));
        var records=orders.Concat(events).OrderByDescending(r=>NativeGame.D(r.Row,"created_ts")).ToArray();PageControls(records.Length,_archiveRecordsPage,p=>{_archiveRecordsPage=p;ShowManagement("档案");});
        foreach(var row in records.Skip(_archiveRecordsPage*40).Take(40))Text($"{Stamp(NativeGame.D(row.Row,"created_ts"))} · {row.Kind} · {NativeGame.S(row.Row,row.Kind=="订单"?"npc":"text")} · {NativeGame.S(row.Row,"status")}");
    }
    int _petHistoryPage;
    void LogControls()
    {
        var search=new LineEdit{Text=_logSearch,PlaceholderText="搜索全部日志，回车筛选"};_managementBody!.AddChild(search);search.TextSubmitted+=s=>{_logSearch=s;_logPage=0;ShowManagement("日志");};
        var rows=Game.Rows("log_archive").Concat(Game.Rows("logs")).Reverse().Where(r=>NativeGame.S(r!,"text").Contains(_logSearch,StringComparison.OrdinalIgnoreCase)).ToArray();
        PageControls(rows.Length,_logPage,p=>{_logPage=p;ShowManagement("日志");});foreach(var row in rows.Skip(_logPage*40).Take(40))Text(Stamp(NativeGame.D(row!,"ts"))+" · "+NativeGame.S(row!,"text"));
    }
    void MapControls()
    {
        Text("点击地图定位镜头；白线为连通道路，红线为断开的道路，金色为建筑。绿到红表示地价由低到高。");
        var map=new TownMap{Town=this,CustomMinimumSize=new(830,532)};_managementBody!.AddChild(map);
        var heat=new CheckButton{Text="显示地价热图"};_managementBody.AddChild(heat);heat.Toggled+=on=>{map.Heat=on;map.QueueRedraw();};
        map.Navigate+=point=>{_center=new(point.X,0,point.Y);_distance=100;_management?.Hide();};
    }
    void SaveControls()
    {
        Text("新版存档："+SavePath);Text("可导入旧版v1～v6 SQLite数据库或新版JSON存档。导入前自动备份当前新版存档；旧数据库以只读方式打开。");
        ActionButton("导入旧存档 / JSON存档",()=>{
            var dialog=new FileDialog{FileMode=FileDialog.FileModeEnum.OpenFile,Access=FileDialog.AccessEnum.Filesystem,Filters=new[]{"*.db ; 旧版SQLite存档","*.json ; JSON存档"},Title="选择要导入的存档",Size=new(850,600)};AddChild(dialog);
            dialog.Canceled+=()=>dialog.QueueFree();dialog.FileSelected+=path=>{dialog.QueueFree();
                var confirm=new ConfirmationDialog{DialogText="导入后切换到所选存档，并自动备份当前新版存档。原始数据库保持不变。",Title="导入存档"};AddChild(confirm);confirm.Canceled+=()=>confirm.QueueFree();confirm.Confirmed+=()=>{confirm.QueueFree();try{var state=LegacyImport.Read(path,Data.Root);LegacyImport.SaveImported(state,SavePath);GetTree().ReloadCurrentScene();}catch(Exception ex){_selection="导入失败："+ex.Message;ShowManagement("存档");}};confirm.PopupCentered();
            };dialog.PopupCentered();
        });
        ActionButton("导出新版存档副本",()=>{var dialog=new FileDialog{FileMode=FileDialog.FileModeEnum.SaveFile,Access=FileDialog.AccessEnum.Filesystem,Filters=new[]{"*.json ; JSON存档"},CurrentFile="town-backup.json",Size=new(850,600)};AddChild(dialog);dialog.Canceled+=()=>dialog.QueueFree();dialog.FileSelected+=path=>{dialog.QueueFree();try{Data.Save(path);_selection="存档副本已导出";}catch(Exception ex){_selection=ex.Message;}ShowManagement("存档");};dialog.PopupCentered();});
    }
}

public partial class FinanceChart : Control
{
    public JsonArray Ledger=new();public double Now;public int Days=7;
    Vector2 _hover=new(-1,-1);
    public override void _GuiInput(InputEvent input){if(input is InputEventMouseMotion move){_hover=move.Position;QueueRedraw();}}
    public override void _Draw()
    {
        float w=Size.X,h=Size.Y;DrawRect(new Rect2(Vector2.Zero,Size),new Color("202b35"));float baseline=h*.52f;DrawLine(new(15,baseline),new(w-15,baseline),new Color("74828b"));
        var income=new double[Days];var expense=new double[Days];double start=Math.Floor(Now/86400)-Days+1;
        foreach(var row in Ledger){int day=(int)(Math.Floor(NativeGame.D(row!,"ts")/86400)-start);if(day<0||day>=Days)continue;int delta=NativeGame.I(row!,"delta");if(delta>0)income[day]+=delta;else expense[day]-=delta;}
        double max=Math.Max(1,income.Concat(expense).Max());float step=(w-40)/Days;
        for(int i=0;i<Days;i++){float x=20+i*step;float up=(float)(income[i]/max)*(h*.35f),down=(float)(expense[i]/max)*(h*.3f);DrawRect(new(x,baseline-up,step*.65f,up),new Color("69c69b"));DrawRect(new(x,baseline,step*.65f,down),new Color("e28c7b"));if(Days==7||i%5==0||i==Days-1)DrawString(ThemeDB.FallbackFont,new(x,h-7),i==Days-1?"0":(i-Days+1).ToString(),HorizontalAlignment.Left,-1,12,new Color("d4d8d8"));}
        DrawString(ThemeDB.FallbackFont,new(15,17),$"MAX {max:0}",HorizontalAlignment.Left,-1,12,new Color("d4d8d8"));
        int hover=(int)((_hover.X-20)/step);TooltipText=hover>=0&&hover<Days?$"{Days-1-hover}日前：收入{income[hover]:0}，支出{expense[hover]:0}":$"最近{Days}日收入{income.Sum():0}，支出{expense.Sum():0}";
    }
}

public partial class TownMap : Control
{
    public Main Town=null!;public bool Heat;public event Action<Vector2>? Navigate;double[,]? _values;
    public override void _GuiInput(InputEvent input){if(input is InputEventMouseButton b&&b.Pressed&&b.ButtonIndex==MouseButton.Left)Navigate?.Invoke(new(b.Position.X/Size.X*WorldData.WorldWidth,b.Position.Y/Size.Y*WorldData.WorldDepth));}
    public override void _Draw()
    {
        float sx=Size.X/125,sy=Size.Y/80;var roads=Town.Game.ConnectedRoads;
        if(Heat&&_values==null){_values=new double[125,80];for(int y=0;y<80;y++)for(int x=0;x<125;x++)_values[x,y]=Town.Game.LandValue(new(x,y));}
        for(int y=0;y<80;y++)for(int x=0;x<125;x++){
            string land=Town.Data.Root["terrain"]![y]![x]!.GetValue<string>();Color color=land switch{"water"=>new("579cb0"),"zone"=>new("8fa77e"),"grass"=>new("7b986a"),_=>new("687968")};
            if(Heat&&(land=="grass"||land=="zone"))color=new Color("4c976b").Lerp(new Color("df765c"),(float)_values![x,y]/100);
            DrawRect(new(x*sx,y*sy,sx+.3f,sy+.3f),color);
        }
        foreach(var road in Town.Data.Roads)DrawRect(new(road.Key.X*sx,road.Key.Y*sy,sx,sy),roads.Contains(new(road.Key.X,road.Key.Y))?new Color("edf0de"):new Color("d87062"));
        foreach(var b in Town.Game.Rows("town_buildings"))DrawRect(new(NativeGame.I(b!,"cx")*sx,NativeGame.I(b!,"cy")*sy,NativeGame.I(b!,"w")*sx,NativeGame.I(b!,"h")*sy),new Color("efd295"));
        foreach(var pair in Town.Data.Root["zones"]!.AsObject()){var rect=pair.Value!["rect"]!;var pos=new Vector2((float)rect[0]!.GetValue<double>()/48*sx,(float)rect[1]!.GetValue<double>()/48*sy);DrawString(ThemeDB.FallbackFont,pos+new Vector2(3,14),NativeGame.S(pair.Value,"name"),HorizontalAlignment.Left,-1,13,new Color("263535"));}
        foreach(var actor in Town.Agents.Where(a=>!a.IsCar&&a.Visual.Visible))DrawCircle(new(actor.Position.X/WorldData.WorldWidth*Size.X,actor.Position.Z/WorldData.WorldDepth*Size.Y),2,Colors.White);
    }
}
