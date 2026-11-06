using Godot;
using System.Collections.Generic;

public partial class Main
{
    const float TrafficBucketWidth=8;
    readonly Dictionary<Vector2I,List<RoadAgent>> _trafficBuckets=new();
    readonly Stack<List<RoadAgent>> _trafficBucketPool=new();
    ulong _trafficIndexFrame=ulong.MaxValue;

    static Vector2I TrafficBucket(Vector3 position)=>new(Mathf.FloorToInt(position.X/TrafficBucketWidth),Mathf.FloorToInt(position.Z/TrafficBucketWidth));

    internal IEnumerable<RoadAgent> NearbyTraffic(RoadAgent origin)
    {
        ulong frame=Engine.GetPhysicsFrames();
        if(_trafficIndexFrame!=frame)RefreshTrafficIndex(frame);
        var bucket=TrafficBucket(origin.Position);
        for(int z=-1;z<=1;z++)
            for(int x=-1;x<=1;x++)
                if(_trafficBuckets.TryGetValue(bucket+new Vector2I(x,z),out var group))
                    foreach(var actor in group)
                        if(actor!=origin&&!actor.InsideHome&&actor.Visual.Visible&&actor.ProcessMode!=ProcessModeEnum.Disabled)
                            yield return actor;
    }

    void RefreshTrafficIndex(ulong frame)
    {
        _trafficIndexFrame=frame;
        foreach(var group in _trafficBuckets.Values){group.Clear();_trafficBucketPool.Push(group);}
        _trafficBuckets.Clear();
        foreach(var actor in Agents){
            if(actor.IsQueuedForDeletion()||actor.ProcessMode==ProcessModeEnum.Disabled)continue;
            var cell=TrafficBucket(actor.Position);
            if(!_trafficBuckets.TryGetValue(cell,out var group)){
                group=_trafficBucketPool.TryPop(out var reused)?reused:new List<RoadAgent>();
                _trafficBuckets.Add(cell,group);
            }
            group.Add(actor);
        }
    }
}
