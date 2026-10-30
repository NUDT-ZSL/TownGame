using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;

public sealed partial class NativeGame
{
    // Door positions use grid units, so presentation can widen streets without
    // changing a save's integer lots, ownership or road identities.
    public sealed record UrbanAccess(string Id,string Zone,Cell? Entrance,double DoorX,double DoorY,IReadOnlyList<(double X,double Y)>? Walkway=null);
    sealed record Landmark(string Id,string Zone,double MinX,double MaxX,double MinY,double MaxY);
    List<Landmark>? _urbanLandmarks;
    HashSet<Cell>? _urbanBlocked;
    static readonly string[] EmploymentZones={"fields","ranch","market","industry","city","park"};
    public bool IsRoadConnected(Cell cell)=>Connected().Contains(cell);
    public Cell? BuildingRoadEntrance(JsonNode building)=>HomeRoadEntrance(building);
    public bool IsDaytimeWorker(JsonNode person)=>(S(person,"role") is "居民" or "通勤者")&&ResidentAgeDays(person)>=7;

    List<Landmark> UrbanLandmarks()
    {
        if(_urbanLandmarks!=null)return _urbanLandmarks;
        _urbanLandmarks=new();var bounds=new Dictionary<int,(double,double,double,double)>();
        if(root["objects"] is not JsonArray objects||root["meshes"] is not JsonArray meshes)return _urbanLandmarks;
        int index=0;
        foreach(var item in objects){
            if(item?["key"] is not JsonArray key||key.Count<2||key[0]!.ToString()!="zone")continue;
            int mesh=I(item,"mesh");
            if(!bounds.TryGetValue(mesh,out var b)){
                byte[] data=Convert.FromBase64String(meshes[mesh]!.GetValue<string>());
                double minX=double.MaxValue,maxX=double.MinValue,minY=double.MaxValue,maxY=double.MinValue;
                for(int offset=0;offset<data.Length;offset+=40){double x=BitConverter.ToSingle(data,offset),y=BitConverter.ToSingle(data,offset+4);minX=Math.Min(minX,x);maxX=Math.Max(maxX,x);minY=Math.Min(minY,y);maxY=Math.Max(maxY,y);}
                b=(minX,maxX,minY,maxY);bounds[mesh]=b;
            }
            var pos=item["pos"]!;double px=pos[0]!.GetValue<double>(),py=pos[1]!.GetValue<double>(),scale=D(item,"scale",1),angle=D(item,"angle");
            var corners=new[]{(b.Item1,b.Item3),(b.Item1,b.Item4),(b.Item2,b.Item3),(b.Item2,b.Item4)}.Select(c=>(X:px+scale*(c.Item1*Math.Cos(angle)-c.Item2*Math.Sin(angle)),Y:py+scale*(c.Item1*Math.Sin(angle)+c.Item2*Math.Cos(angle)))).ToArray();
            _urbanLandmarks.Add(new("landmark-"+index++,key[1]!.ToString(),corners.Min(c=>c.X)/48,corners.Max(c=>c.X)/48,corners.Min(c=>c.Y)/48,corners.Max(c=>c.Y)/48));
        }
        return _urbanLandmarks;
    }
    HashSet<Cell> UrbanObstacles()
    {
        if(_urbanBlocked!=null)return _urbanBlocked;_urbanBlocked=new();
        foreach(var b in UrbanLandmarks())for(int y=(int)Math.Floor(b.MinY);y<Math.Ceiling(b.MaxY);y++)for(int x=(int)Math.Floor(b.MinX);x<Math.Ceiling(b.MaxX);x++)_urbanBlocked.Add(new(x,y));
        return _urbanBlocked;
    }
    IEnumerable<Cell> LandmarkEntrances(Landmark b)
    {
        int center=(int)Math.Floor((b.MinX+b.MaxX)/2),south=(int)Math.Ceiling(b.MaxY);
        // Some landmarks include a protected forecourt beyond their mesh (the
        // station square). Keep that apron pedestrian-only and stop the public
        // street at its first legal edge instead of paving through the square.
        for(int step=0;step<7;step++){
            var row=new[]{new Cell(center,south+step),new Cell(center-1,south+step),new Cell(center+1,south+step)}.Where(UrbanRoadAllowed).ToArray();
            if(row.Length==0)continue;foreach(var cell in row)yield return cell;yield break;
        }
    }
    public IReadOnlyList<UrbanAccess> UrbanAccessPoints()
    {
        var linked=Connected();var result=new List<UrbanAccess>();
        foreach(var b in UrbanLandmarks()){
            var candidates=LandmarkEntrances(b).Where(linked.Contains).ToArray();Cell? entry=candidates.Length==0?null:candidates[0];
            result.Add(new(b.Id,b.Zone,entry,(b.MinX+b.MaxX)/2,b.MaxY+.12));
        }
        foreach(var b in Rows("town_buildings")){
            var entry=BuildingRoadEntrance(b!);double x=I(b!,"cx"),y=I(b!,"cy"),w=I(b!,"w"),h=I(b!,"h");
            double dx=x+w/2,dy=y+h+.1;
            if(entry is Cell e){if(e.Y<y){dy=y-.1;}else if(e.X<x){dx=x-.1;dy=y+h/2;}else if(e.X>=x+w){dx=x+w+.1;dy=y+h/2;}}
            string zone=Zone(new((int)x,(int)y));result.Add(new(S(b!,"building_id"),zone==""?"estate":zone,entry,dx,dy));
        }
        // The compact pet lots are served by pedestrian aisles, not an eight-
        // metre street through every enclosure. Keep a clear north/south aisle
        // between columns 2 and 3, with branches in the gap between wall rows.
        var gate=Portal("estate");Cell? manorEntrance=linked.Contains(gate)?gate:null;
        foreach(var b in Rows("buildings").Where(b=>S(b!,"kind")!="")){
            int id=I(b!,"building_id"),row=id/5;double x=1314+id%5*230,doorY=905+row*88,corridorY=914+row*88;
            var walkway=new List<(double X,double Y)>{(gate.X+.5,gate.Y+.5),(1800d/48,1368d/48),(1800d/48,1280d/48),(1889d/48,1280d/48),(1889d/48,corridorY/48),(x/48,corridorY/48),(x/48,doorY/48)};
            result.Add(new("manor:"+id,"estate",manorEntrance,x/48,doorY/48,walkway));
        }
        return result;
    }
    public string AccessRoadStats
    {
        get {var access=UrbanAccessPoints();int linked=access.Count(a=>a.Entrance!=null),zones=root["portals"]!.AsObject().Count(p=>Connected().Contains(Portal(p.Key)));return $"道路连通：街区 {zones}/{root["portals"]!.AsObject().Count}，建筑入口 {linked}/{access.Count}";}
    }
    bool UrbanRoadAllowed(Cell c)=>Allowed(c)&&!UrbanObstacles().Contains(c);
    List<Cell>? UrbanRoadPlan(IEnumerable<Cell> targets)
    {
        var connected=Connected();var roads=RoadCells();
        var queue=new PriorityQueue<Cell,int>();var dist=new Dictionary<Cell,int>();var parents=new Dictionary<Cell,Cell?>();
        foreach(var c in targets)if(UrbanRoadAllowed(c)){
            if(connected.Contains(c))return new();dist[c]=0;parents[c]=null;queue.Enqueue(c,0);
        }
        Cell? goal=null;
        while(queue.TryDequeue(out var c,out int priority)){
            if(priority!=dist[c])continue;if(connected.Contains(c)){goal=c;break;}
            foreach(var d in Directions){var n=new Cell(c.X+d.X,c.Y+d.Y);if(!UrbanRoadAllowed(n))continue;
                int value=priority+(roads.Contains(n)?1:10);if(value>=dist.GetValueOrDefault(n,int.MaxValue))continue;dist[n]=value;parents[n]=c;queue.Enqueue(n,value);
            }
        }
        if(goal==null)return null;var path=new List<Cell>();
        while(goal!=null){if(!roads.Contains(goal.Value))path.Add(goal.Value);goal=parents[goal.Value];}return path;
    }
    void InitializeUrbanPlanning()
    {
        if(I(Meta,"native_urban_plan_version")<1){
            // One migration of municipal infrastructure; existing homes and paid
            // player lots are retained. Later manual demolition remains meaningful.
            foreach(var zone in Rows("town_zones"))if(I(zone!,"level")==0)zone!["level"]=1;
            WorldRevision++;
            var pending=new List<string>();int added=0;
            void Connect(string name,IEnumerable<Cell> targets){
                var path=UrbanRoadPlan(targets);if(path==null){pending.Add(name);return;}
                foreach(var c in path){AppendRoad(c,0);added++;}ClearGround(path);
            }
            foreach(string zone in root["portals"]!.AsObject().Select(p=>p.Key))Connect(zone,new[]{Portal(zone)});
            foreach(var building in Rows("town_buildings"))Connect(S(building!,"building_id"),Frontage(building!));
            foreach(var landmark in UrbanLandmarks())Connect(landmark.Id,LandmarkEntrances(landmark));
            Meta["native_urban_plan_version"]=1;Meta["urban_roads_added"]=added;
            Meta["urban_access_pending"]=new JsonArray(pending.Select(p=>(JsonNode)JsonValue.Create(p)!).ToArray());
            Meta["urban_plan_status"]=pending.Count==0?"街区与建筑入口已接入公共路网":"部分入口被占用，保留建筑并等待修复";
        }
        EnsureCommuters();AssignDaytimeDestinations();
    }
    void EnsureCommuters()
    {
        var surnames=new[]{"周","许","沈","陆","叶","程"};
        for(int i=0;i<EmploymentZones.Length;i++){
            string zone=EmploymentZones[i];if(!Effective(zone))continue;
            string profession=zone switch{"fields"=>"农民","ranch"=>"牧民","market"=>"商人","industry"=>"工匠","city"=>"职员",_=>"园丁"};
            for(int j=0;j<2;j++){
                string id=$"commuter-{zone}-{j}";if(People.Any(p=>S(p!,"person_id")==id))continue;
                if(People.Count>=I(Rules,"TOWN_MAX_PEOPLE",80))return;
                People.Add(new JsonObject{{"person_id",id},{"name",surnames[i]+(j==0?"晨":"安")},{"role","通勤者"},{"profession",profession},{"work_zone",zone},{"home_zone","station"},{"destination",zone},{"sex",j==0?"男":"女"},{"born_ts",Now-(20+j)*Day},{"entered_ts",Now},{"status","通勤"},{"job_id",""},{"native_commuter",true},{"color_index",i}});WorldRevision++;
            }
        }
    }
    public string DistrictDestination(JsonNode person)
    {
        if(IsResidentNight)return S(person,"role")=="通勤者"?"station":S(person,"home_zone","estate");
        string preferred=S(person,"work_zone");
        if(preferred=="")preferred=S(person,"profession") switch{"农民"=>"fields","牧民"=>"ranch","商人"=>"market","工匠"=>"industry","建筑工"=>"city","园丁"=>"park","孩童"=>"park",_=>"market"};
        if(Now%Day>=17*Hour&&S(person,"role")=="居民")preferred=S(person,"profession")=="农民"?"market":"park";
        return Effective(preferred)?preferred:"estate";
    }
    public void AssignDaytimeDestinations()
    {
        foreach(var p in People.Where(p=>S(p!,"role") is "居民" or "通勤者")){
            p!["destination"]=DistrictDestination(p!);
            if(S(p,"role")=="通勤者")p["status"]=IsResidentNight?"下班离镇":"在岗";
        }
        foreach(var p in People.Where(p=>S(p!,"role")=="游客"&&S(p!,"status")!="离镇")){
            string[] choices={"market","park","city","ranch"};uint hash=2166136261;foreach(char c in S(p!,"person_id")){hash^=c;hash*=16777619;}
            string zone=choices[hash%choices.Length];p!["destination"]=Effective(zone)?zone:"estate";
        }
    }
}
