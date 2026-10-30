using Godot;
using System;
using System.Linq;

public partial class Main
{
    // Run against the same stopping function as CharacterBody traffic. Temporary
    // probe nodes are never inserted into the scene or persisted in the save.
    void VerifyTrafficStops()
    {
        double clockBefore=_clock;var junctionsBefore=_junctions.ToArray();
        var probe=new RoadAgent{Town=this,IsCar=true,Kind="truck",Speed=6};
        Vector2I? removedCell=null;System.Text.Json.Nodes.JsonObject? removedRoad=null;
        try{
            void Check(bool condition,string message){if(!condition)throw new Exception("TRAFFIC_CONTROL_FAILED: "+message);}
            var candidates=Data.Roads.Keys.Where(c=>StreetDirections.All(d=>Data.Roads.ContainsKey(c+d))).ToArray();
            Check(candidates.Length>0,"地图没有可验证的四向路口");
            var cell=candidates[0];var center=WorldData.Cell(cell);_junctions.Clear();_junctions.Add(cell);
            foreach(var gridDirection in StreetDirections){
                Vector3 direction=new(gridDirection.X,0,gridDirection.Y),side=new(-direction.Z,0,direction.X);
                bool ns=Math.Abs(direction.Z)>.5f;
                probe.IsCar=true;probe.Kind="truck";probe.Speed=6;
                probe.Position=center-direction*7.42f+side*WorldData.VehicleLane;
                _clock=ns?14:1;Check(RedLight(probe,direction,.04f),$"货车在{direction}进口未遵守红灯");
                _clock=ns?1:14;Check(!RedLight(probe,direction,.04f),$"货车在{direction}进口绿灯仍不放行");
                _clock=ns?10:22;Check(RedLight(probe,direction,.04f),$"货车在{direction}进口黄灯仍进入路口");
                probe.Position=center-direction*1.5f+side*WorldData.VehicleLane;
                _clock=23.5;Check(!RedLight(probe,direction,.04f),"已进入路口的车辆应继续清空路口");

                probe.IsCar=false;probe.Speed=2.5f;
                probe.Position=center-direction*3.18f+side*WorldData.Sidewalk;
                _clock=1;Check(RedLight(probe,direction,.04f),$"行人在{direction}斑马线未遵守红灯");
                _clock=26;Check(!RedLight(probe,direction,.04f),$"行人在{direction}斑马线绿灯仍不放行");
                // A left-turn walking route can start its second crossing at a
                // pavement corner 3.4 m from centre. Its gate must be inside that.
                probe.Position=center-direction*WorldData.Sidewalk+side*WorldData.Sidewalk;
                _clock=1;Check(RedLight(probe,direction,.12f),"从人行道转角开始过街时漏过停止线");
            }
            removedCell=cell+Vector2I.Right;removedRoad=Data.Roads[removedCell.Value];Data.Roads.Remove(removedCell.Value);
            probe.IsCar=false;probe.Speed=2.5f;probe.Position=center+new Vector3(WorldData.Sidewalk,0,3.18f);_clock=1;
            Check(!RedLight(probe,Vector3.Forward,.04f),"丁字路口没有车道的一侧人行道不应等待过街信号");
            GD.Print("TRAFFIC_CONTROL_VERIFIED four_approach_truck_red_yellow_green_pedestrian_crosswalk_corner_and_t_junction");
        }finally{
            if(removedCell!=null&&removedRoad!=null)Data.Roads[removedCell.Value]=removedRoad;
            _junctions.Clear();_junctions.AddRange(junctionsBefore);_clock=clockBefore;UpdateBoulevardSignals(Phase);
            probe.Visual.Free();probe.Free();
        }
    }
}
