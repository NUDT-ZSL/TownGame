using Godot;
using System;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;

public partial class Main
{
    bool _migrationVerification;int _migrationStage,_migrationTab;double _migrationTimer;long _visualRng;
    int _migrationCapturedTab=-1;System.Threading.Tasks.Task? _migrationCapture;
    bool _migrationInput;int _migrationRoadCoins;Vector2I _migrationRoadCell;
    string MigrationOutput=>OS.GetCmdlineUserArgs().FirstOrDefault(a=>a.StartsWith("--test-output="))?[14..]??ProjectSettings.GlobalizePath("res://../.test-output");
    async System.Threading.Tasks.Task MigrationShot(string name)
    {
        if(DisplayServer.GetName()=="headless")return;await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);Directory.CreateDirectory(MigrationOutput);GetViewport().GetTexture().GetImage().SavePng(Path.Combine(MigrationOutput,name+".png"));
    }
    void VerifyMigrationScene(double delta)
    {
        try{
            void Check(bool good,string why){if(!good)throw new Exception("MIGRATION_SCENE_FAILED: "+why);}
            _migrationTimer+=delta;if(_frames<8)return;
            if(_migrationStage==0){
                _visualRng=(long)NativeGame.D(Data.Meta,"native_rng");_selectedPet=NativeGame.S(Data.Pets[0]!,"pet_id");
                Game.Execute("buy",new JsonObject{{"item","清水"},{"quantity",3}});Game.Execute("sell",new JsonObject{{"item","清水"},{"quantity",1}});
                foreach(var species in Game.Rules["SPECIES"]!.AsObject())foreach(string pose in new[]{"idle","walk","sleep","eat","bath","pet"}){
                    var mesh=(ArrayMesh)_petModels.Get(species.Key,383,true,false,"成年",pose,1);var array=mesh.SurfaceGetArrays(0);var points=array[(int)Mesh.ArrayType.Vertex].AsVector3Array();var normals=array[(int)Mesh.ArrayType.Normal].AsVector3Array();
                    Check(points.Length>500&&mesh.GetAabb().Size.Y>0,"原生动物模型缺失");
                    for(int i=0;i<points.Length;i+=3){var cross=(points[i+1]-points[i]).Cross(points[i+2]-points[i]);if(cross.LengthSquared()<1e-10)continue;Check(cross.Dot(normals[i])<.0001,"动物模型三角形朝内");break;}
                }
                _migrationStage=1;_migrationTimer=0;return;
            }
            if(_migrationStage==1){
                if(_migrationTimer<.16)return;
                if(_migrationCapture!=null&&!_migrationCapture.IsCompleted)return;
                if(_migrationTab>0&&_migrationCapturedTab!=_migrationTab&&ManagementTabs[_migrationTab-1] is "外观" or "财务" or "地图" or "档案"){_migrationCapturedTab=_migrationTab;_migrationCapture=MigrationShot("native-migrated-"+_migrationTab);return;}
                if(_migrationTab<ManagementTabs.Length){ShowManagement(ManagementTabs[_migrationTab++]);_migrationTimer=0;return;}
                _management?.Hide();Game.Commons["enabled"]=false;Game.CancelFreight("迁移场景检查");Game.Jobs.Clear();
                double noon=Math.Floor(Game.Now/86400)*86400+12*3600;if(noon<Game.Now)noon+=86400;Game.Meta["sim_ts"]=noon;Game.Meta["start_ts"]=noon-21*86400;Game.Meta["weather"]=Game.Rules["WEATHERS"]!.AsObject().First(p=>p.Key.Contains("雪")&&!p.Key.Contains("雨")).Key;Game.SetZone("ranch",1);Game.Commons["stock"]!["herd"]=6;
                string[] zones={"fields","ranch","park","industry","market","city"};int n=0;
                foreach(var actor in Agents.Where(a=>!a.IsCar).Take(6)){var person=Data.People.First(p=>NativeGame.S(p!,"person_id")==actor.Ident)!;person["destination"]=zones[n++];person["job_id"]="";actor.Path.Clear();actor.Index=0;actor.InsideHome=false;actor.Visual.Show();actor.Position=DistrictWorkPosition(person);actor.ResidentialActivity="现场工作";}
                _center=FarmPet(_selectedPet)!.Position;_distance=12;UpdateCamera();_migrationStage=2;_migrationTimer=0;return;
            }
            if(_migrationStage==2&&_migrationTimer>2){
                Check(_snow!.Emitting&&!_rain!.Emitting&&Game.Season.Contains("冬"),"雨雪或季节切换失败");Check(_herd.Count==6,"公共牲畜未匹配真实数量");
                Check(_performances.Values.Any(p=>p.Kind=="read")&&_performances.Values.Any(p=>p.Kind=="plant")&&_performances.Values.Any(p=>p.Kind=="feed"),"居民工作道具未启用");
                Check((long)NativeGame.D(Data.Meta,"native_rng")==_visualRng,"视觉行为消耗了经营随机数");
                _=MigrationShot("native-migrated-life");Game.Meta["weather"]=Game.Rules["WEATHERS"]!.AsObject().First(p=>p.Key.Contains("雨")).Key;_migrationStage=3;_migrationTimer=0;return;
            }
            if(_migrationStage==3&&_migrationTimer>1){
                Check(_rain!.Emitting&&!_snow!.Emitting,"雨雪粒子不能切换");
                var road=Data.Roads.Keys.First(c=>Data.Roads.ContainsKey(c+Vector2I.Right)&&NativeGame.S(Data.Roads[c],"road_style")!="stone"&&NativeGame.S(Data.Roads[c+Vector2I.Right],"road_style")!="stone"&&Game.RoadReason(new(c.X,c.Y),"stone")==""&&Game.RoadReason(new(c.X+1,c.Y),"stone")=="");
                var from=WorldData.Cell(road);_center=from;_distance=45;UpdateCamera();ArmPlacement("road",new JsonObject{{"style","stone"}});BeginRoadStroke(_camera.UnprojectPosition(from));SampleStroke(road+Vector2I.Right);UpdatePlacement();Check(_strokeCells.Count>=2,"拖拽未连接道路格");
                CancelRoadStroke();_migrationRoadCell=road;_migrationRoadCoins=NativeGame.I(Data.Meta,"coins");_migrationInput=true;
                var start=_camera.UnprojectPosition(from);var end=_camera.UnprojectPosition(WorldData.Cell(road+Vector2I.Right));
                GetViewport().PushInput(new InputEventMouseMotion{Position=start,GlobalPosition=start},true);
                GetViewport().PushInput(new InputEventMouseButton{Position=start,GlobalPosition=start,ButtonIndex=MouseButton.Left,ButtonMask=MouseButtonMask.Left,Pressed=true},true);
                _migrationStage=4;_migrationTimer=0;return;
            }
            if(_migrationStage==4&&_migrationTimer>.2){
                Check(_strokeActive,$"鼠标按下没有开始笔画，界面={GetViewport().GuiGetHoveredControl()?.Name}，提示={_selection}");
                var start=_camera.UnprojectPosition(WorldData.Cell(_migrationRoadCell));var end=_camera.UnprojectPosition(WorldData.Cell(_migrationRoadCell+Vector2I.Right));
                GetViewport().PushInput(new InputEventMouseMotion{Position=end,GlobalPosition=end,Relative=end-start,ButtonMask=MouseButtonMask.Left},true);
                _migrationStage=5;_migrationTimer=0;return;
            }
            if(_migrationStage==5&&_migrationTimer>.2){
                var end=_camera.UnprojectPosition(WorldData.Cell(_migrationRoadCell+Vector2I.Right));
                GetViewport().PushInput(new InputEventMouseButton{Position=end,GlobalPosition=end,ButtonIndex=MouseButton.Left,Pressed=false},true);
                _migrationStage=6;_migrationTimer=0;return;
            }
            if(_migrationStage==6&&_migrationTimer>.2){
                Check(NativeGame.S(Data.Roads[_migrationRoadCell],"road_style")=="stone"&&NativeGame.S(Data.Roads[_migrationRoadCell+Vector2I.Right],"road_style")=="stone","实际鼠标笔画没有提交："+_selection);Check(NativeGame.I(Data.Meta,"coins")==_migrationRoadCoins-28,"实际鼠标笔画费用错误");
                Act("undo_road",new JsonObject());Check(NativeGame.I(Data.Meta,"coins")==_migrationRoadCoins,"实际撤销费用错误");_migrationInput=false;_placementAction="";_ghost?.Hide();CancelRoadStroke();
                GD.Print($"MIGRATION_SCENE_VERIFIED tabs={ManagementTabs.Length} models=72 weather_seasons_herd_work_props_mouse_road_undo_rng");_migrationStage=7;GetTree().Quit();
            }
        }catch(Exception ex){GD.PushError(ex.ToString());_migrationStage=7;GetTree().Quit(1);}
    }
}
