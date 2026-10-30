using Godot;
using System;
using System.Linq;
using System.Text.Json.Nodes;

public partial class Main
{
    bool _farmVerification;
    int _farmProbeStage,_farmFeedStock;
    double _farmProbeTime;
    string _farmProbePet="";
    float? _farmCleanHeading;
    Vector3 _farmIdlePosition;
    void VerifyFarmManagerScene(double delta)
    {
        try{
            _farmProbeTime+=delta;if(_frames<8)return;
            void Check(bool good,string message){if(!good)throw new Exception("FARM_SCENE_FAILED: "+message);}
            if(_farmProbeStage==0){
                Game.Commons["enabled"]=false;Game.CancelFreight("管理员场景检查");Game.Meta["caretaker"]=false;
                foreach(var a in Agents){a.ProcessMode=ProcessModeEnum.Disabled;a.CollisionLayer=0;a.CollisionMask=0;}
                Game.Rows("waste").Clear();foreach(var p in Game.Pets)p!["hunger"]=90;
                var pet=Game.Pets[0]!;_farmProbePet=NativeGame.S(pet,"pet_id");pet["hunger"]=50;
                FarmPet(_farmProbePet)!.Position=WorldData.Expand(new(159,0,106));FarmPet(_farmProbePet)!.React("idle");
                Game.Rows("waste").Add(new JsonObject{{"waste_id","scene-pile"},{"pet_id",_farmProbePet},{"building_id",0},{"created_ts",Game.Now-86400},{"quantity",1},{"native_x",113*WorldData.LayoutScale},{"native_z",106*WorldData.LayoutScale}});
                Game.Meta["farm_manager"]=true;Game.Meta["manager_native_x"]=184*WorldData.LayoutScale;Game.Meta["manager_native_z"]=109*WorldData.LayoutScale;Game.Meta["manager_job"]=null;Game.Meta["manager_next_scan_ts"]=Game.Now;Game.Meta["manager_next_wage_ts"]=Game.Now+21600;
                Game.Meta["manager_cleaned"]=0;Game.Meta["manager_fed"]=0;_farmFeedStock=Game.Stock("普通饲料");
                var obstacle=new StaticBody3D{CollisionLayer=1};obstacle.AddChild(new CollisionShape3D{Shape=new BoxShape3D{Size=new(4,2,4)},Position=WorldData.Expand(new(145,1,106))});AddChild(obstacle);
                Engine.TimeScale=4;Engine.MaxFps=120;_center=WorldData.Expand(new(151,0,106));_distance=75;UpdateCamera();_farmProbeStage=1;_farmProbeTime=0;return;
            }
            if(_farmProbeStage==1){
                if(_farmProbeTime<1){var nav=new FarmNavigation(this);bool blocked=!nav.CanInteract(WorldData.Expand(new(142.5f,0,106)),WorldData.Expand(new(147.5f,0,106)));nav.Dispose();Check(blocked,"隔墙作业未被阻止");}
                // Keep this animal available at the test feeding point; the manager must still travel there.
                if(_farmManager?.Activity!="喂食中")FarmPet(_farmProbePet)?.React("idle");
                if(_farmManager?.Activity=="清扫中")_farmCleanHeading=_farmManager.Rotation.Y;
                Check(_farmProbeTime<180,$"任务未完成，管理员{_farmManager?.Position}，状态{NativeGame.S(Game.Meta,"manager_status")}，清扫{NativeGame.I(Game.Meta,"manager_cleaned")}，喂食{NativeGame.I(Game.Meta,"manager_fed")}");
                if(NativeGame.I(Game.Meta,"manager_cleaned")<1||NativeGame.I(Game.Meta,"manager_fed")<1)return;
                Check(_farmManager!.WalkedDistance>40,"未实际移动到多个任务地点");
                Check(_farmManager.CompletedVisits>=2,"未在现场完成两种工作");
                Check(Game.Rows("waste").Count==0&&_wasteViews.Count==0,"清扫后粪便仍留在场景");
                Check(Game.Stock("普通饲料")==_farmFeedStock-1&&NativeGame.D(Game.Pets[0]!,"hunger")==80,"喂食没有正确扣料/回复饱食度");
                Check(_farmCleanHeading.HasValue&&Math.Abs(Mathf.AngleDifference(_farmCleanHeading.Value,_farmManager.Rotation.Y))>.5,"换目标后模型朝向没有改变");
                _farmIdlePosition=_farmManager.Position;LocateFarmManager();UpdateCamera();_farmProbeTime=0;_farmProbeStage=2;return;
            }
            if(_farmProbeStage==2&&_farmProbeTime>4){
                Check(_farmManager!.Position.DistanceTo(_farmIdlePosition)<.05,"无任务时仍在固定小区域转圈");
                _selection="农场管理员：现场清扫、喂食与待命已验证";
                if(DisplayServer.GetName()!="headless")GetViewport().GetTexture().GetImage().SavePng(ProjectSettings.GlobalizePath("res://../.test-output/native-farm-manager.png"));
                GD.Print("FARM_SCENE_VERIFIED actual_walk_avoid_obstacle_no_work_through_wall_clean_feed_turn_idle");Engine.TimeScale=1;GetTree().Quit();_farmProbeStage=3;
            }
        }catch(Exception error){GD.PushError(error.ToString());Engine.TimeScale=1;GetTree().Quit(1);_farmProbeStage=3;}
    }
}
