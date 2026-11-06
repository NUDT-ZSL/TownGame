using Godot;
using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

public partial class Main
{
    bool _largeTownVerification;
    readonly record struct LargeTownTiming(double Work,double ToDraw);

    async Task VerifyLargeTown()
    {
        if(_frames!=8)return;
        try{
            Game.Meta["coins"]=100000;
            var fresh=Enumerable.Range(0,125*80).Select(index=>new NativeGame.Cell(index%125,index/125))
                .Where(cell=>(cell.Y%4==0||cell.X%16==0)&&!Game.RoadCells().Contains(cell)&&Game.RoadReason(cell,"stone")=="")
                .Take(500).ToArray();
            Require(fresh.Length==500,"压力测试无法建立 500 格道路");
            Game.Execute("road_stroke",new JsonObject{{"style","stone"},{"cells",new JsonArray(
                fresh.Select(cell=>(JsonNode)new JsonArray(cell.X,cell.Y)).ToArray())}});
            int beforeBuildings=Game.Rows("town_buildings").Count;
            var candidates=Enumerable.Range(0,125*80).Select(index=>new Vector2I(index%125,index/125)).ToArray();
            for(int i=0;i<24;i++){
                var at=candidates.First(cell=>Game.PlacementReason("bus_stop",cell.X,cell.Y)=="");
                Game.PlaceTown("bus_stop",at.X,at.Y,true);
            }
            var original=Data.Pets.OfType<JsonNode>().First(Game.Active);
            for(int i=0;i<36;i++){
                var pet=original.DeepClone();
                pet["pet_id"]=$"large-town-{i}";
                pet["name"]=$"验收伙伴 {i+1}";
                pet["mate_id"]="";
                pet["pregnancy_due_ts"]=0;
                pet["fostered"]=false;
                Data.Pets.Add(pet);
            }
            SyncBusinessWorld();
            Require(_farmPets.Count>=36&&Game.Rows("town_buildings").Count==beforeBuildings+24,"压力测试没有同步所有伙伴和建筑");
            bool graphical=DisplayServer.GetName()!="headless";
            if(graphical)
                for(int frame=0;frame<8;frame++)await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);

            string unchanged="road:"+new Vector2I(fresh[1].X,fresh[1].Y);
            var retained=_dynamic[unchanged];
            var edited=fresh[0];
            var road=await TimedFrame(()=>{
                Game.Execute("road",new JsonObject{{"cx",edited.X},{"cy",edited.Y},{"style","garden"}});
                SyncBusinessWorld();
            });
            Require(ReferenceEquals(retained,_dynamic[unchanged])&&_lastLayoutCounts.Created==1
                &&_lastLayoutCounts.Reused>500,"重铺一格后其余道路或建筑没有复用");
            _selectedPet=NativeGame.S(original,"pet_id");
            var pages=new System.Collections.Generic.List<LargeTownTiming>();
            foreach(var (label,tab) in new[]{("care","照护"),("orders","订单"),("map","地图")}){
                var cold=await TimedFrame(()=>ShowManagement(tab));
                if(graphical){
                    for(int frame=0;frame<24&&!VisiblePortraitsReady();frame++)
                        await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
                    Require(VisiblePortraitsReady(),$"{tab}的可见头像未在 24 帧内加载");
                }
                int cached=_portraitTextures.Count;
                var warm=await TimedFrame(()=>ShowManagement(tab));
                if(graphical)Require(VisiblePortraitsReady()&&_portraitTextures.Count==cached,$"重新打开{tab}没有复用头像");
                pages.Add(cold);
                pages.Add(warm);
                GD.Print(FormattableString.Invariant($"TOWN_PAGE_TIMING page={label} cold_ms={cold.Work:F2} cold_to_draw_ms={cold.ToDraw:F2} warm_ms={warm.Work:F2} warm_to_draw_ms={warm.ToDraw:F2} portraits={cached}"));
            }

            string savePath=Path.Combine(Path.GetTempPath(),$"TownGame-scale-{Guid.NewGuid():N}.json");
            LargeTownTiming save;
            try{
                save=await TimedFrame(()=>Data.Save(savePath));
                var snapshot=JsonNode.Parse(File.ReadAllText(savePath))!["snapshot"];
                Require(JsonNode.DeepEquals(snapshot,Data.Snapshot),"压力存档往返后内容发生变化");
            }finally{File.Delete(savePath);}
            GD.Print(FormattableString.Invariant(
                $"LARGE_TOWN_VERIFIED roads={Data.Roads.Count} buildings={Game.Rows("town_buildings").Count} pets={_farmPets.Count} road_nodes_created={_lastLayoutCounts.Created} scene_nodes_reused={_lastLayoutCounts.Reused} edit_ms={road.Work:F2} edit_to_draw_ms={road.ToDraw:F2} max_page_ms={pages.Max(page=>page.Work):F2} max_page_to_draw_ms={pages.Max(page=>page.ToDraw):F2} save_ms={save.Work:F2} save_to_draw_ms={save.ToDraw:F2} graphical={graphical}"));
            await VerifyDenseTraffic();
            VerifyNightFarmBurst();
            GetTree().Quit();
        }catch(Exception error){
            GD.PushError(error.ToString());
            GetTree().Quit(1);
        }

        static void Require(bool condition,string message)
        {
            if(!condition)throw new InvalidOperationException("LARGE_TOWN_FAILED: "+message);
        }
        async Task<LargeTownTiming> TimedFrame(Action action)
        {
            long start=Stopwatch.GetTimestamp();
            action();
            double work=Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            if(DisplayServer.GetName()!="headless")
                await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
            return new(work,Stopwatch.GetElapsedTime(start).TotalMilliseconds);
        }

        async Task VerifyDenseTraffic()
        {
            _management?.Hide();
            var junctions=_junctions.Take(6).ToArray();
            Require(junctions.Length==6,"密集交通测试缺少路口");
            var sampled=new System.Collections.Generic.List<RoadAgent>();
            foreach(var junction in junctions){
                var center=WorldData.Cell(junction);
                for(int lane=0;lane<12;lane++){
                    bool car=lane%4==0;
                    var actor=new RoadAgent{Town=this,Ident=$"traffic-probe-{sampled.Count}",Kind=car?"truck":"person",IsCar=car,
                        Speed=car?5:2.5f,Position=center+new Vector3((lane%4-1.5f)*2,0,(lane/4-1)*3)};
                    actor.Visual.Mesh=Prototype(car?"truck":"person-0");
                    actor.Path.Add(actor.Position+Vector3.Back*14);
                    actor.Path.Add(actor.Position+Vector3.Right*12);
                    AddChild(actor);Agents.Add(actor);sampled.Add(actor);
                }
            }
            RefreshTrafficIndex(Engine.GetPhysicsFrames());
            int maxCandidates=0;
            foreach(var actor in sampled){
                var neighbors=NearbyTraffic(actor).ToHashSet();
                maxCandidates=Math.Max(maxCandidates,neighbors.Count);
                Require(Agents.Where(other=>other!=actor&&!other.InsideHome&&other.Visual.Visible&&other.ProcessMode!=ProcessModeEnum.Disabled
                    &&other.Position.DistanceSquaredTo(actor.Position)<=6.2f*6.2f).All(neighbors.Contains),"交通索引漏掉相邻角色");
            }
            Require(maxCandidates<Agents.Count/2,"密集路口仍在扫描大半个小镇的角色");
            if(DisplayServer.GetName()=="headless"){
                GD.Print($"DENSE_TRAFFIC_VERIFIED actors={Agents.Count} max_candidates={maxCandidates}");
                return;
            }
            _center=WorldData.Cell(junctions[1]);_distance=70;_pitch=.82f;_buildCamera=false;UpdateCamera();
            var screen=GetViewport().GetVisibleRect().GetCenter();
            var durations=new double[42];
            for(int frame=-6;frame<durations.Length;frame++){
                var timing=await TimedFrame(()=>{
                    _center+=TownGroundDrag(screen,screen+new Vector2(frame%12<6?8:-8,2));
                    ClampTownCameraCenter();UpdateCamera();
                });
                if(frame>=0)durations[frame]=timing.ToDraw;
            }
            Array.Sort(durations);
            GD.Print(FormattableString.Invariant($"DENSE_TRAFFIC_VERIFIED actors={Agents.Count} max_candidates={maxCandidates} drag_frames={durations.Length} drag_median_ms={durations[durations.Length/2]:F2} drag_p95_ms={durations[(int)(durations.Length*.95)]:F2}"));
        }

        void VerifyNightFarmBurst()
        {
            var pets=FarmPets.Where(pet=>pet.Ident.StartsWith("large-town-",StringComparison.Ordinal)).Take(16).ToArray();
            Require(pets.Length==16,"夜间返家测试缺少伙伴");
            Game.Meta["sim_ts"]=Math.Floor(Game.Now/NativeGame.Day)*NativeGame.Day+23*NativeGame.Hour;
            _farmRouteTick=ulong.MaxValue;
            foreach(var (pet,index) in pets.Select((pet,index)=>(pet,index))){
                pet.Position=PetHomePoint(Data.FindPet(pet.Ident)!)+new Vector3(index%4*2-3,0,10+index/4*2);
                pet._PhysicsProcess(1d/60);
            }
            Require(_farmRoutesUsed==FarmRoutesPerTick,"多个伙伴同时返家时没有按帧限制实际寻路");
            GD.Print($"NIGHT_FARM_BURST_VERIFIED pets={pets.Length} first_tick_routes={_farmRoutesUsed} shared_cells={_farmOccupancy.Count}");
        }
    }
}
