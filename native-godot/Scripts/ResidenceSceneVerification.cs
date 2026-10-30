using Godot;
using System;
using System.Linq;
using System.Text.Json.Nodes;

public partial class Main
{
    bool _residenceVerification;
    int _residenceProbeStage;
    double _residenceProbeTime;
    RoadAgent? _residenceProbeActor;
    JsonNode? _residenceProbePerson;
    Vector3 _residenceProbeStart;
    void VerifyResidenceScene(double delta)
    {
        try{
            _residenceProbeTime+=delta;if(_frames<8)return;
            void Check(bool good,string message){if(!good)throw new Exception("RESIDENCE_SCENE_FAILED: "+message);}
            if(_residenceProbeStage==0){
                Game.Commons["enabled"]=false;Game.CancelFreight("住宅场景检查");
                _residenceProbePerson=Data.People.First(p=>NativeGame.S(p!,"role")=="居民");_residenceProbeActor=Agents.First(a=>a.Ident==NativeGame.S(_residenceProbePerson!,"person_id"));
                var home=Game.ResidentHome(_residenceProbePerson!)!;var entry=Game.HomeRoadEntrance(home)!.Value;var goal=new Vector2I(entry.X,entry.Y);
                var nearby=Data.Roads.Keys.Where(c=>(c-goal).LengthSquared()>=4&&(c-goal).LengthSquared()<20&&Data.Route(c,goal,WorldData.Sidewalk).Count>0).OrderBy(c=>(c-goal).LengthSquared()).First();
                foreach(var a in Agents.Where(a=>Resident(a.Ident)==null)){a.ProcessMode=ProcessModeEnum.Disabled;a.CollisionLayer=0;a.CollisionMask=0;}
                _residenceProbeActor.Position=WorldData.Cell(nearby);_residenceProbeStart=_residenceProbeActor.Position;
                // 给一条反方向的旧路线，验证入夜时会立即打断。
                _residenceProbeActor.Path.Clear();_residenceProbeActor.Path.Add(_residenceProbeStart+Vector3.Right*100);_residenceProbeActor.Index=0;
                Game.Meta["sim_ts"]=Math.Floor(Game.Now/86400)*86400+20*3600;_clock=23;Engine.TimeScale=4;Engine.MaxFps=120;
                _residenceProbeTime=0;_residenceProbeStage=1;return;
            }
            if(_residenceProbeStage==1){
                if(_residenceProbeTime>=100){for(int i=0;i<_residenceProbeActor!.GetSlideCollisionCount();i++){var hit=_residenceProbeActor.GetSlideCollision(i);var collider=hit.GetCollider();GD.Print($"BLOCKED {collider} {collider.GetMeta("kind", "none")} {collider.GetMeta("ident", "none")} at {hit.GetPosition()}");}GD.Print("PATH "+string.Join(";",_residenceProbeActor.Path)+$" velocity={_residenceProbeActor.Velocity} phase={Phase} clock={_clock}");}
                Check(_residenceProbeTime<100,$"居民未走到家门前，位置{_residenceProbeActor!.Position}，目标{_residenceProbeActor.HomeDoor}，路径{_residenceProbeActor.Index}/{_residenceProbeActor.Path.Count}");
                if(!Agents.Where(a=>Resident(a.Ident)!=null).All(a=>a.InsideHome))return;
                Check(_residenceProbeStart.DistanceTo(_residenceProbeActor.Position)>2,"未实际步行");
                Check(_residenceProbeActor.Position.DistanceTo(_residenceProbeActor.HomeDoor)<.5f,"未到家门前即入室");
                Check(!_residenceProbeActor.Visual.Visible&&_residenceProbeActor.CollisionLayer==0,"室内居民仍占道路碰撞");
                var house=Game.ResidentHome(_residenceProbePerson!)!;_center=new((NativeGame.I(house,"cx")+NativeGame.I(house,"w")*.5f)*WorldData.GridSize,0,(NativeGame.I(house,"cy")+NativeGame.I(house,"h")*.5f)*WorldData.GridSize);_distance=55;UpdateCamera();
                GetViewport().GuiEmbedSubwindows=true;Select(_camera.UnprojectPosition(_center+Vector3.Up*6));
                Check(_selectedResidence==NativeGame.S(house,"building_id")&&_management!.Visible,"点击住宅未打开家庭面板");
                Check(_familyInformation!.Text.Contains(NativeGame.S(_residenceProbePerson!,"name"))&&_familyInformation.Text.Contains("游戏日")&&_familyInformation.Text.Contains("在家休息"),"家庭资料遗漏成员、年龄或状态");
                GD.Print("RESIDENCE_SCENE_OK all_residents_enter_and_house_pick");_residenceProbeTime=0;_residenceProbeStage=2;return;
            }
            if(_residenceProbeStage==2){
                if(_residenceProbeTime<2)return;
                if(DisplayServer.GetName()!="headless")GetViewport().GetTexture().GetImage().SavePng(ProjectSettings.GlobalizePath("res://../.test-output/native-household.png"));
                _management!.Hide();Game.Meta["sim_ts"]=Math.Floor(Game.Now/86400)*86400+86400+7*3600;_residenceProbeTime=0;_residenceProbeStage=3;return;
            }
            if(_residenceProbeStage==3){
                if(_residenceProbeTime<1)return;
                Check(_residenceProbeTime<20,"同户居民出门排队未完成");
                if(Agents.Where(a=>Resident(a.Ident)!=null).Any(a=>a.InsideHome))return;
                Check(!_residenceProbeActor!.InsideHome&&_residenceProbeActor.Visual.Visible&&_residenceProbeActor.CollisionLayer==2&&!NativeGame.B(_residenceProbePerson!,"at_home"),"早晨未恢复出门和碰撞");
                GD.Print("RESIDENCE_SCENE_VERIFIED return_home_click_family_morning_exit");Engine.TimeScale=1;GetTree().Quit();_residenceProbeStage=4;
            }
        }catch(Exception error){GD.PushError(error.ToString());Engine.TimeScale=1;GetTree().Quit(1);_residenceProbeStage=4;}
    }
}
