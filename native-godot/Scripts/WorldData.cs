using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

public sealed class WorldData
{
    public const float GridSize=8,LayoutScale=2,VehicleLane=1.65f,Sidewalk=3.4f;
    public const float WorldWidth=125*GridSize,WorldDepth=80*GridSize;
    public static Vector3 Expand(Vector3 point)=>new(point.X*LayoutScale,point.Y,point.Z*LayoutScale);
    public JsonObject Root { get; }
    public JsonObject Snapshot => Root["snapshot"]!.AsObject();
    public JsonObject Meta => Snapshot["meta"]!.AsObject();
    public JsonArray People => Snapshot["people"]!.AsArray();
    public JsonArray Pets => Snapshot["pets"]!.AsArray();
    readonly Dictionary<string,JsonNode> _petsById=new();
    const int RoadDestinationCacheLimit=48;
    readonly Dictionary<Vector2I,Dictionary<Vector2I,int>> _routeDistances=new();
    readonly Queue<Vector2I> _routeCacheOrder=new();
    internal int CachedRoadDestinations=>_routeDistances.Count;
    static readonly Vector2I[] RoadDirections={Vector2I.Up,Vector2I.Right,Vector2I.Down,Vector2I.Left};
    JsonArray? _indexedPets;
    int _indexedPetCount;
    public readonly Dictionary<Vector2I,JsonObject> Roads = new();
    public readonly Dictionary<string,Vector2I> Portals = new();
    public WorldData(JsonObject root)
    {
        Root=root;
        Reindex();
        foreach(var p in root["portals"]!.AsObject())Portals[p.Key]=new(p.Value![0]!.GetValue<int>(),p.Value[1]!.GetValue<int>());
    }
    public void Reindex()
    {
        var roads=Snapshot["roads"]!.AsArray();
        bool changed=Roads.Count!=roads.Count||roads.OfType<JsonNode>().Any(row=>!Roads.ContainsKey(new(I(row,"cx"),I(row,"cy"))));
        if(changed){_routeDistances.Clear();_routeCacheOrder.Clear();}
        Roads.Clear();
        foreach(var node in roads) {
            var r=node!.AsObject();Roads[new(I(r,"cx"),I(r,"cy"))]=r;
        }
    }
    public JsonNode? FindPet(string id)
    {
        var pets=Pets;
        if(!ReferenceEquals(_indexedPets,pets)||_indexedPetCount!=pets.Count){
            _petsById.Clear();
            foreach(var pet in pets.OfType<JsonNode>())_petsById[S(pet,"pet_id")]=pet;
            _indexedPets=pets;
            _indexedPetCount=pets.Count;
        }
        return _petsById.GetValueOrDefault(id);
    }
    public static int I(JsonNode node,string key,int fallback=0) => NativeGame.I(node,key,fallback);
    public static double D(JsonNode node,string key,double fallback=0) => NativeGame.D(node,key,fallback);
    public static string S(JsonNode node,string key,string fallback="") => node[key]?.GetValue<string>()??fallback;
    public static Vector3 Position(JsonArray p) => Expand(new((float)p[0]!.GetValue<double>()/12,(float)p[2]!.GetValue<double>()/12,(float)p[1]!.GetValue<double>()/12));
    public static Vector3 Cell(Vector2I p) => new((p.X+.5f)*GridSize,0,(p.Y+.5f)*GridSize);
    public static Vector2I Grid(Vector3 p) => new(Mathf.FloorToInt(p.X/GridSize),Mathf.FloorToInt(p.Z/GridSize));
    public Vector3 PavementPoint(Vector2I cell)
    {
        bool ns=Roads.ContainsKey(cell+Vector2I.Up)||Roads.ContainsKey(cell+Vector2I.Down),ew=Roads.ContainsKey(cell+Vector2I.Left)||Roads.ContainsKey(cell+Vector2I.Right);
        return Cell(cell)+new Vector3(ns?Sidewalk:0,0,ew?Sidewalk:0);
    }
    public IEnumerable<Vector2I> Neighbors(Vector2I c)
    {
        foreach(var d in RoadDirections)if(Roads.ContainsKey(c+d))yield return c+d;
    }
    List<Vector2I> RouteCells(Vector2I start,Vector2I end)
    {
        if(!Roads.ContainsKey(start)||!Roads.ContainsKey(end))return new();
        if(!_routeDistances.TryGetValue(end,out var distances)){
            distances=new(){{end,0}};
            var queue=new Queue<Vector2I>();
            queue.Enqueue(end);
            while(queue.TryDequeue(out var cell)){
                int length=distances[cell]+1;
                foreach(var next in Neighbors(cell))
                    if(distances.TryAdd(next,length))queue.Enqueue(next);
            }
            if(_routeCacheOrder.Count>=RoadDestinationCacheLimit)_routeDistances.Remove(_routeCacheOrder.Dequeue());
            _routeDistances[end]=distances;
            _routeCacheOrder.Enqueue(end);
        }
        if(!distances.TryGetValue(start,out int remaining))return new();
        var cells=new List<Vector2I>(remaining+1){start};
        while(remaining>0){
            var next=Neighbors(cells[^1]).First(cell=>distances.TryGetValue(cell,out int distance)&&distance==remaining-1);
            cells.Add(next);
            remaining--;
        }
        return cells;
    }
    public List<Vector3> Route(Vector2I start,Vector2I end,float offset)
    {
        var cells=RouteCells(start,end);
        var points=new List<Vector3>();
        for(int i=0;i<cells.Count;i++) {
            var incoming=i>0?cells[i]-cells[i-1]:i+1<cells.Count?cells[i+1]-cells[i]:Vector2I.Down;
            var outgoing=i+1<cells.Count?cells[i+1]-cells[i]:incoming;
            Vector3 Side(Vector2I d)=>new(-d.Y,0,d.X);
            var center=Cell(cells[i]);
            if(incoming!=outgoing&&Math.Abs(offset)>2.5f){
                // Stay on the pavement around a corner instead of cutting diagonally across the median.
                points.Add(center+Side(incoming)*offset-new Vector3(incoming.X,0,incoming.Y)*Sidewalk);
                points.Add(center+(Side(incoming)+Side(outgoing))*offset);
                points.Add(center+Side(outgoing)*offset+new Vector3(outgoing.X,0,outgoing.Y)*Sidewalk);
            }else{
                points.Add(center+Side(incoming)*offset);
                if(incoming!=outgoing)points.Add(center+Side(outgoing)*offset);
            }
        }
        return points;
    }
    public List<Vector3> PedestrianRoute(Vector3 origin,Vector2I start,Vector2I end,Vector3 destination)
    {
        var cells=RouteCells(start,end);
        if(cells.Count==0)return new();
        if(cells.Count<2){
            var cellCenter=Cell(start);var delta=destination-cellCenter;
            bool ns=Roads.ContainsKey(start+Vector2I.Up)||Roads.ContainsKey(start+Vector2I.Down);
            bool ew=Roads.ContainsKey(start+Vector2I.Left)||Roads.ContainsKey(start+Vector2I.Right);
            if(ns&&ew)ns=Math.Abs(delta.X)>=Math.Abs(delta.Z);
            float sign=(ns?delta.X:delta.Z)<0?-1:1;
            return new(){ns?new(cellCenter.X+sign*Sidewalk,0,Math.Clamp(origin.Z,cellCenter.Z-3.8f,cellCenter.Z+3.8f)):new(Math.Clamp(origin.X,cellCenter.X-3.8f,cellCenter.X+3.8f),0,cellCenter.Z+sign*Sidewalk)};
        }
        Vector3 Direction(Vector2I a,Vector2I b)=>new(b.X-a.X,0,b.Y-a.Y);
        Vector3 Right(Vector3 d)=>new(-d.Z,0,d.X);
        var first=Direction(cells[0],cells[1]);var last=Direction(cells[^2],cells[^1]);
        float begin=(origin-Cell(start)).Dot(Right(first)),finish=(destination-Cell(end)).Dot(Right(last));
        int endSign=finish<0?-1:1,startSign=Math.Abs(begin)<.3f?endSign:begin<0?-1:1;
        if(startSign==endSign)return Route(start,end,Sidewalk*endSign);
        int crossing=cells.FindIndex(1,c=>Neighbors(c).Count()>=3);
        if(crossing<0)return Route(start,end,Sidewalk*endSign);
        var junction=cells[crossing];var incoming=Direction(cells[crossing-1],junction);
        var outgoing=crossing+1<cells.Count?Direction(junction,cells[crossing+1]):incoming;
        var center=Cell(junction);var result=Route(start,junction,Sidewalk*startSign);
        if(result.Count>0)result.RemoveAt(result.Count-1);
        // Change pavements on the zebra crossing at the incoming edge, never
        // cut diagonally over the landscaped centre of a straight road.
        result.Add(center-incoming*Sidewalk+Right(incoming)*Sidewalk*startSign);
        result.Add(center-incoming*Sidewalk+Right(incoming)*Sidewalk*endSign);
        if(incoming!=outgoing)result.Add(center+(Right(incoming)+Right(outgoing))*Sidewalk*endSign);
        if(junction!=end)result.AddRange(Route(junction,end,Sidewalk*endSign));return result;
    }
    public void Save(string path)
    {
        string staged=StageSave(path,Snapshot);
        try{File.Move(staged,path,true);}
        finally{DeleteSaveStage(staged);}
    }

    internal static string StageSave(string path,JsonObject snapshot)
    {
        string temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";
        try{
            using(var stream=new FileStream(temp,FileMode.CreateNew,System.IO.FileAccess.Write,FileShare.None))
            using(var json=new Utf8JsonWriter(stream)){
                json.WriteStartObject();
                json.WriteNumber("version",1);
                json.WritePropertyName("snapshot");
                snapshot.WriteTo(json);
                json.WriteEndObject();
            }
            return temp;
        }catch{DeleteSaveStage(temp);throw;}
    }

    internal static void DeleteSaveStage(string path)
    {
        try{File.Delete(path);}
        catch(IOException){}
        catch(UnauthorizedAccessException){}
    }
}
