using Godot;
using System;
using System.Linq;

public partial class Main
{
    bool _freightVerification;
    int _freightProbeStage;
    double _freightProbeTime;
    double _freightLoadedViewTime;
    bool _freightLoadedCaptured;
    string _freightProbeDriver="",_freightProbeLastPhase="";
    Vector3 _freightProbeStart,_freightProbeDriverStart;
    bool _freightProbeBoarded,_freightProbeLoaded,_freightProbeDelivered;
    void VerifyFreightScene(double delta)
    {
        try{
            _freightProbeTime+=delta;if(_frames<8||_freightTruck==null)return;
            void Check(bool condition,string message){if(!condition)throw new Exception("FREIGHT_SCENE_FAILED: "+message);}
            if(_freightProbeStage==0){
                VerifyBoulevardGeometry();
                VerifyTrafficStops();
                foreach(string zone in new[]{"fields","ranch","market","industry","city","park"}){
                    var worker=Data.People.FirstOrDefault(p=>NativeGame.S(p!,"role")=="通勤者"&&NativeGame.S(p!,"work_zone")==zone);
                    Check(worker!=null&&Agents.Any(a=>a.Ident==NativeGame.S(worker!,"person_id")&&a.Visual.Visible),zone+"缺少实际可见的白天工作人员");
                }
                Game.Jobs.Clear();foreach(var p in Game.People)p!["job_id"]="";Game.CancelFreight("测试重置");Game.FreightState["next_trip_ts"]=0;
                Game.Meta["sim_ts"]=Math.Floor(Game.Now/86400)*86400+10*3600;Game.Commons["enabled"]=true;Game.FreightState["trips"]=0;Game.RefreshFreight();
                _freightProbeDriver=NativeGame.S(Game.FreightState,"driver_id");Check(_freightProbeDriver!="","未分配真实居民司机");
                foreach(var actor in Agents.Where(a=>a.Ident!=_freightProbeDriver&&a!=_freightTruck)){actor.ProcessMode=ProcessModeEnum.Disabled;actor.CollisionLayer=0;actor.CollisionMask=0;}
                var driver=Agents.First(a=>a.Ident==_freightProbeDriver);_freightProbeDriverStart=driver.Position;_freightProbeStart=_freightTruck.Position;
                AssignRoute(driver);Engine.TimeScale=8;Engine.MaxFps=120;_freightProbeTime=0;_freightProbeStage=1;return;
            }
            if(_freightProbeStage==1){
                string phase=NativeGame.S(Game.FreightState,"phase");
                if(phase is "to_source" or "to_destination" or "to_depot"){
                    var cell=WorldData.Grid(_freightTruck.Position);var neighbors=Data.Neighbors(cell).ToArray();
                    if(neighbors.Length==2&&neighbors[0]+neighbors[1]==cell*2){
                        var center=WorldData.Cell(cell);float lateral=neighbors[0].X==cell.X?_freightTruck.Position.X-center.X:_freightTruck.Position.Z-center.Z;
                        Check(Math.Abs(lateral)>1.02f,"货车跨越了直路中央绿化带");
                    }
                }
                if(phase=="boarding"&&_freightProbeTime>90){var d=Agents.First(a=>a.Ident==_freightProbeDriver);GD.Print($"FREIGHT_BOARD_DEBUG pos={d.Position} path={d.Index}/{d.Path.Count} points={string.Join(';',d.Path)}");Check(false,"司机90秒内未走到车门");}
                if(phase!=_freightProbeLastPhase){GD.Print($"FREIGHT_SCENE_PHASE {phase} t={_freightProbeTime:0.0} pos={_freightTruck.Position}");_freightProbeLastPhase=phase;}
                Check(_freightProbeTime<1000,$"运输超时：{phase}，车位置{_freightTruck.Position}，路径{_freightTruck.Index}/{_freightTruck.Path.Count}，司机{Agents.First(a=>a.Ident==_freightProbeDriver).Position}");
                if(phase=="to_source"&&!_freightProbeBoarded){
                    var driver=Agents.First(a=>a.Ident==_freightProbeDriver);
                    Check(driver.Position.DistanceTo(_freightProbeDriverStart)>2,"司机没有实际步行上车");
                    Check(!driver.Visual.Visible&&driver.CollisionLayer==0&&_freightCabDriver!.Visible,"司机未进入驾驶室或仍占道路碰撞");_freightProbeBoarded=true;
                }
                if(phase=="to_destination"){
                    Check(NativeGame.I(Game.FreightState,"cargo_count")>0&&_freightCargo!.Visible,"装货后车厢没有实际货物");_freightProbeLoaded=true;
                    if(_freightTruck.Position.DistanceTo(_freightProbeStart)>30){_center=_freightTruck.Position;_distance=32;UpdateCamera();}
                    _freightLoadedViewTime+=delta;
                    if(!_freightLoadedCaptured&&_freightLoadedViewTime>1){
                        if(DisplayServer.GetName()!="headless")GetViewport().GetTexture().GetImage().SavePng(ProjectSettings.GlobalizePath("res://../.test-output/native-freight-loaded.png"));
                        _freightLoadedCaptured=true;
                    }
                }
                if(phase=="to_depot"&&NativeGame.I(Game.FreightState,"trips")==1)_freightProbeDelivered=true;
                if(phase!="parked"||!_freightProbeDelivered)return;
                Check(_freightProbeBoarded&&_freightProbeLoaded&&_freightTruck.Position.DistanceTo(_freightProbeStart)<12,"未完成上车、装卸和回库闭环");
                _freightProbeTime=0;_freightProbeStage=2;return;
            }
            if(_freightProbeStage==2&&_freightProbeTime>1){
                var driver=Agents.First(a=>a.Ident==_freightProbeDriver);
                Check(driver.Visual.Visible&&driver.CollisionLayer==2&&!NativeGame.B(Resident(driver.Ident)!,"freight_reserved"),"回库后司机未下车恢复步行");
                _center=_freightTruck.Position;_distance=32;UpdateCamera();_selection=FreightDescription(_freightTruck.Ident);
                _freightProbeTime=0;_freightProbeStage=3;return;
            }
            if(_freightProbeStage==3&&_freightProbeTime>1){
                if(DisplayServer.GetName()!="headless")GetViewport().GetTexture().GetImage().SavePng(ProjectSettings.GlobalizePath("res://../.test-output/native-freight.png"));
                var crossroads=Data.Roads.Keys.Where(c=>Data.Neighbors(c).Count()==4).OrderBy(c=>(c-Data.Portals["estate"]).LengthSquared()).First();
                _center=WorldData.Cell(crossroads);_distance=35;_pitch=1.05f;_clock=4;UpdateCamera();_freightProbeTime=0;_freightProbeStage=4;return;
            }
            if(_freightProbeStage==4&&_freightProbeTime>1){
                if(DisplayServer.GetName()!="headless")GetViewport().GetTexture().GetImage().SavePng(ProjectSettings.GlobalizePath("res://../.test-output/native-boulevard.png"));
                GD.Print("FREIGHT_SCENE_VERIFIED resident_walk_board_drive_load_deliver_return_disembark");Engine.TimeScale=1;GetTree().Quit();_freightProbeStage=5;
            }
        }catch(Exception error){GD.PushError(error.ToString());Engine.TimeScale=1;GetTree().Quit(1);_freightProbeStage=3;}
    }
}
