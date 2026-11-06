using Godot;
using System.Collections.Generic;
using System.Linq;

public partial class Main
{
    readonly Queue<RoadAgent> _routeRefreshQueue=new();
    readonly HashSet<RoadAgent> _routeRefreshPending=new();
    const int RoutesPerFrame=4;
    internal int RoadTopologyVersion=>_roadTopologyVersion;

    void ScheduleRoadRefresh(IEnumerable<Vector2I> removed,bool all)
    {
        var cut=removed.ToHashSet();
        foreach(var actor in Agents){
            if(actor.InsideHome)continue;
            bool affected=all||actor.Index>=actor.Path.Count||actor.Path.Skip(actor.Index)
                .Any(point=>cut.Contains(WorldData.Grid(point)));
            if(!affected||!_routeRefreshPending.Add(actor))continue;
            if(actor==_freightTruck){_freightHasRoute=false;_freightRetry=0;}
            actor.Path.Clear();
            actor.Index=0;
            actor.Wait=1;
            actor.Velocity=Vector3.Zero;
            _routeRefreshQueue.Enqueue(actor);
        }
    }

    void RefreshQueuedRoutes()
    {
        for(int count=0;count<RoutesPerFrame&&_routeRefreshQueue.TryDequeue(out var actor);count++){
            _routeRefreshPending.Remove(actor);
            if(GodotObject.IsInstanceValid(actor)&&!actor.IsQueuedForDeletion()&&Agents.Contains(actor))AssignRoute(actor);
        }
    }
}
