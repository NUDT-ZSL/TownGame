using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;

public partial class Main
{
    CpuParticles3D? _rain,_snow;string _visualClimate="";double _herdRefresh;
    readonly List<HerdAnimal> _herd=new();readonly Dictionary<string,(Node3D Root,string Kind)> _performances=new();
    Vector3? _pasture;string _pastureLayout="";
    Vector3? Pasture(Vector3 near)
    {
        if(_pastureLayout==FarmLayoutSignature)return _pasture;_pastureLayout=FarmLayoutSignature;_pasture=null;
        var rect=Data.Root["zones"]!["ranch"]!["rect"]!;int left=(int)(rect[0]!.GetValue<double>()/48),top=(int)(rect[1]!.GetValue<double>()/48),right=(int)((rect[0]!.GetValue<double>()+rect[2]!.GetValue<double>())/48),bottom=(int)((rect[1]!.GetValue<double>()+rect[3]!.GetValue<double>())/48);
        using var nav=new FarmNavigation(this);
        var candidates=Enumerable.Range(left,Math.Max(0,right-left)).SelectMany(x=>Enumerable.Range(top,Math.Max(0,bottom-top)).Select(y=>WorldData.Cell(new(x,y)))).OrderBy(p=>p.DistanceSquaredTo(near));
        foreach(var center in candidates){bool clear=true;foreach(float x in new[]{-8f,0f,8f})foreach(float z in new[]{-5f,0f,5f}){var point=center+new Vector3(x,0,z);var cell=WorldData.Grid(point);if(Data.Roads.ContainsKey(cell)||!nav.Clear(point)){clear=false;break;}}if(clear){_pasture=center;break;}}
        return _pasture;
    }
    public static float PetWorldScale(string species)=>species switch{"兔子"=>.17f,"猫咪"=>.18f,"小狗"=>.21f,"绵羊"=>.22f,"羊驼"=>.23f,"梅花鹿"=>.24f,"刺猬"=>.11f,"鹦鹉"=>.12f,"小鸭"=>.16f,"孔雀"=>.18f,_=>.18f};
    public Vector3 PetPropPoint(string kind)
    {
        if(PetActivityStations.TryGetValue(kind,out var station))return PetActivityCenter+station.Offset;
        var item=Data.Root["objects"]!.AsArray().FirstOrDefault(n=>n?["key"] is JsonArray key&&key.Count>1&&key[0]!.ToString()=="prop"&&key[1]!.ToString()==kind);
        return item==null?WorldData.Expand(new(150,0,106)):WorldData.Position(item["pos"]!.AsArray());
    }
    void CreateClimate()
    {
        CpuParticles3D Particles(bool snow){var material=new StandardMaterial3D{AlbedoColor=snow?new Color("f7f7f0"):new Color("a7ccd8"),ShadingMode=BaseMaterial3D.ShadingModeEnum.Unshaded,BillboardMode=BaseMaterial3D.BillboardModeEnum.Enabled};
            var particles=new CpuParticles3D{Emitting=false,Amount=snow?ActiveGraphics.Snow:ActiveGraphics.Rain,Lifetime=snow?9:2.4,Preprocess=snow?6:1.5,VisibilityAabb=new Aabb(new(-45,-40,-36),new(90,85,72)),Mesh=new QuadMesh{Size=snow?new(.16f,.16f):new(.045f,.85f),Material=material},EmissionShape=CpuParticles3D.EmissionShapeEnum.Box,EmissionBoxExtents=new(40,3,32),Direction=Vector3.Down,Spread=snow?25:6,Gravity=snow?new(.1f,-.4f,.1f):new(1,-16,1),InitialVelocityMin=snow?1.2f:13,InitialVelocityMax=snow?2.4f:19,ScaleAmountMin=.65f,ScaleAmountMax=1.2f};AddChild(particles);return particles;
        }
        _rain=Particles(false);_snow=Particles(true);
    }
    void UpdateWorldLife(double delta)
    {
        if(_rain==null)CreateClimate();
        string climate=Game.Season+Game.Weather;if(_visualClimate!=climate){_visualClimate=climate;_rain!.Visible=_snow!.Visible=!_reduceWeatherMotion;_rain.Emitting=!_reduceWeatherMotion&&Game.Weather.Contains("雨");_snow.Emitting=!_reduceWeatherMotion&&Game.Weather.Contains("雪");
            _material.SetShaderParameter("season_tint",Game.Season.Contains("秋")?new Vector3(1.2f,.68f,.35f):Game.Season.Contains("冬")?new Vector3(.85f,.92f,1):Game.Season.Contains("夏")?new Vector3(.82f,1.06f,.8f):Vector3.One);
            _material.SetShaderParameter("winter",Game.Season.Contains("冬")?1f:0f);
        }
        _rain!.Position=_center+Vector3.Up*28;_snow!.Position=_center+Vector3.Up*22;_rain.SpeedScale=_snow.SpeedScale=Paused?0:1;
        if(Paused)return;
        _herdRefresh-=delta;if(_herdRefresh<=0){_herdRefresh=1;int count=Game.Effective("ranch")?NativeGame.I(Game.Commons["stock"]!,"herd"):0;
            while(_herd.Count>count){_herd[^1].QueueFree();_herd.RemoveAt(_herd.Count-1);}
            var access=Game.UrbanAccessPoints().FirstOrDefault(a=>a.Zone=="ranch"&&a.Entrance!=null);var origin=access==null?Data.PavementPoint(Data.Portals["ranch"]):new Vector3((float)access.DoorX*WorldData.GridSize,0,(float)access.DoorY*WorldData.GridSize);
            var pasture=Pasture(origin);while(pasture.HasValue&&_herd.Count<Math.Min(12,count)){var animal=new HerdAnimal{Town=this,Number=_herd.Count,Origin=pasture.Value,Position=pasture.Value+new Vector3(_herd.Count%4*2-3,0,_herd.Count/4*2-2)};AddChild(animal);_herd.Add(animal);}
        }
        foreach(var actor in Agents.Where(a=>!a.IsCar)){
            var person=Data.People.FirstOrDefault(p=>NativeGame.S(p!,"person_id")==actor.Ident);
            bool performing=person!=null&&!Game.IsResidentNight&&!actor.InsideHome&&actor.Visual.Visible&&actor.Index>=actor.Path.Count&&actor.ResidentialActivity!="等待道路接通"&&!Game.IsFreightDriver(actor.Ident);
            string zone=person==null?"":NativeGame.S(person,"destination"),kind=performing?zone switch{"fields"=>"plant","ranch"=>"feed","park" or "city"=>"read","industry"=>"work","market"=>"trade",_=>""}:"";
            if(!_performances.TryGetValue(actor.Ident,out var props)||props.Kind!=kind){if(props.Root!=null)props.Root.QueueFree();var root=new Node3D();actor.AddChild(root);_performances[actor.Ident]=(root,kind);CreateWorkProps(root,kind);props=(root,kind);}
            float phase=(float)_clock*2+(PersonHash(actor.Ident)%100)/10f;
            props.Root.Visible=performing&&kind!="";if(kind==""){actor.Visual.Rotation=Vector3.Zero;actor.Visual.Position=Vector3.Zero;continue;}
            actor.Visual.Mesh=Prototype("person-0");actor.Visual.Rotation=new(kind is "plant" or "feed"?.14f+Mathf.Sin(phase)*.09f:0,0,kind=="work"?Mathf.Sin(phase)*.045f:0);
            props.Root.Rotation=new(kind is "plant" or "work"?Mathf.Sin(phase)*.25f:kind=="feed"?Mathf.Sin(phase)*.15f:0,0,kind=="read"?Mathf.Sin(phase*.4f)*.03f:0);
        }
        var alive=Agents.Select(a=>a.Ident).ToHashSet();foreach(string id in _performances.Keys.Where(id=>!alive.Contains(id)).ToArray())_performances.Remove(id);
    }
    static void CreateWorkProps(Node3D root,string kind)
    {
        void Box(Vector3 size,Vector3 position,string color){root.AddChild(new MeshInstance3D{Mesh=new BoxMesh{Size=size},MaterialOverride=new StandardMaterial3D{AlbedoColor=new Color(color)},Position=position});}
        if(kind=="read"){Box(new(.5f,.035f,.3f),new(0,1.12f,.43f),"cf9d70");Box(new(.46f,.03f,.27f),new(0,1.15f,.43f),"f3ead3");Box(new(.012f,.036f,.28f),new(0,1.17f,.43f),"baaa90");}
        if(kind=="plant"){Box(new(.045f,1.2f,.045f),new(.3f,.7f,.55f),"9d7958");Box(new(.45f,.08f,.22f),new(.3f,.12f,.65f),"6c777b");}
        if(kind=="feed"){root.AddChild(new MeshInstance3D{Mesh=new CylinderMesh{TopRadius=.16f,BottomRadius=.11f,Height=.3f},Position=new(.3f,.9f,.5f),MaterialOverride=new StandardMaterial3D{AlbedoColor=new Color("ad9580")}});Box(new(.24f,.03f,.24f),new(.3f,1.055f,.5f),"d9bb73");}
        if(kind=="work"){Box(new(.05f,.5f,.05f),new(.3f,1.1f,.4f),"a17b52");Box(new(.25f,.13f,.13f),new(.3f,1.37f,.4f),"7d8a8b");}
        if(kind=="trade"){Box(new(.55f,.4f,.4f),new(0,1.05f,.5f),"b99161");Box(new(.57f,.05f,.42f),new(0,1.05f,.5f),"d6b882");}
    }
}

public partial class HerdAnimal : CharacterBody3D
{
    public Main Town=null!;public int Number;public Vector3 Origin;readonly MeshInstance3D _visual=new();Vector3 _target;double _timer;FarmNavigation? _nav;List<Vector3> _path=new();int _index;Random _random=null!;JsonObject _state=new();
    public override void _Ready()
    {
        _random=new(3101+Number);CollisionLayer=2;CollisionMask=7;AddChild(new CollisionShape3D{Shape=new CapsuleShape3D{Radius=.3f,Height=1.1f},Position=Vector3.Up*.55f});
        _state=new JsonObject{{"pet_id","public-herd-"+Number},{"species","绵羊"},{"rare",false},{"birth_ts",Town.Game.Now-15*86400},{"lifespan_max",45*86400},{"death_ts",0},{"departure",""}};
        _visual.Mesh=Town.PetMesh(_state,"idle",0);_visual.Scale=Vector3.One*.17f;AddChild(_visual);SetMeta("kind","herd");SetMeta("ident",Number.ToString());_target=Position;_timer=Number*.7;
    }
    public override void _PhysicsProcess(double delta)
    {
        if(Town.Paused)return;_timer-=delta;
        if(_index>=_path.Count){_visual.Mesh=Town.PetMesh(_state,"eat",(int)(Time.GetTicksMsec()/250)%4);if(_timer<=0&&Town.TryScheduleFarmRoutes()){_timer=7+_random.NextDouble()*8;_target=Origin+new Vector3((float)_random.NextDouble()*14-7,0,(float)_random.NextDouble()*8-4);_nav??=new FarmNavigation(Town);_path=_nav.Route(Position,_target,1);_index=0;}return;}
        var diff=_path[_index]-Position;diff.Y=0;if(diff.Length()<.25f){_index++;return;}var direction=diff.Normalized();Velocity=direction*Math.Min(1.5f,diff.Length()/(float)Math.Max(delta,.001))+Vector3.Down*2;MoveAndSlide();Rotation=new(0,Mathf.Atan2(direction.X,direction.Z),0);_visual.Mesh=Town.PetMesh(_state,"walk",(int)(Time.GetTicksMsec()/200)%4);
        if(IsOnWall()){_path.Clear();_timer=1;}
    }
    public override void _ExitTree()=>_nav?.Dispose();
}
