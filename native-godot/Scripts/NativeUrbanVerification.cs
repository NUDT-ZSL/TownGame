using System;
using System.Linq;
using System.Collections.Generic;
using System.Text.Json.Nodes;

public static class NativeUrbanVerification
{
    public static int Run(JsonObject source)
    {
        int passed=0;
        JsonObject Fixture(bool migrate=false)
        {
            var root=new JsonObject();foreach(string key in new[]{"version","snapshot","rules","terrain","zones","portals"})root[key]=source[key]!.DeepClone();
            // Retain only landmark meshes; road planning needs their actual wall
            // extents, not thousands of unrelated pet animation vertices.
            var objects=new JsonArray();var meshes=new JsonArray();var remap=new Dictionary<int,int>();
            if(source["objects"] is JsonArray all&&source["meshes"] is JsonArray shapes)foreach(var item in all){
                if(item?["key"] is not JsonArray k||k[0]!.ToString()!="zone")continue;
                var copy=item.DeepClone();int old=NativeGame.I(copy,"mesh");if(!remap.TryGetValue(old,out int id)){id=meshes.Count;remap[old]=id;meshes.Add(shapes[old]!.DeepClone());}copy["mesh"]=id;objects.Add(copy);
            }
            root["objects"]=objects;root["meshes"]=meshes;
            if(migrate)root["snapshot"]!["meta"]!.AsObject().Remove("native_urban_plan_version");return root;
        }
        void Check(bool value,string text){if(!value)throw new Exception("URBAN_TEST_FAILED: "+text);}
        void Test(string name,Action<NativeGame,JsonObject> test){var root=Fixture(true);var g=new NativeGame(root);test(g,root);g.Validate();passed++;Godot.GD.Print("RULE_OK urban_"+name);}
        Test("all_districts_and_landmarks_have_access",(g,r)=>{
            foreach(var zone in r["portals"]!.AsObject())Check(g.Effective(zone.Key),zone.Key+"街区开通且连接庄园");
            var access=g.UrbanAccessPoints();Check(access.Count>=g.Rows("town_buildings").Count,"建筑入口记录未生成");
            foreach(var a in access)Check(a.Entrance!=null&&g.IsRoadConnected(a.Entrance.Value),a.Id+"入口不通路");
            Check(g.Meta["urban_access_pending"]!.AsArray().Count==0,"有未处理的建筑入口");
        });
        Test("replanning_preserves_homes_and_is_idempotent",(g,r)=>{
            string homes=string.Join("|",g.People.Where(p=>NativeGame.S(p!,"role")=="居民").Select(p=>NativeGame.S(p!,"home_building_id")));
            int roads=g.Rows("roads").Count,people=g.People.Count,coins=NativeGame.I(g.Meta,"coins");
            var copy=Fixture();copy["snapshot"]=JsonNode.Parse(g.State.ToJsonString());var again=new NativeGame(copy);
            Check(again.Rows("roads").Count==roads&&again.People.Count==people,"重开重复生成道路或通勤者");
            Check(NativeGame.I(again.Meta,"coins")==coins,"市政迁移重复扣款");
            Check(string.Join("|",again.People.Where(p=>NativeGame.S(p!,"role")=="居民").Select(p=>NativeGame.S(p!,"home_building_id")))==homes,"重规划搬走了原住家庭");
        });
        Test("isolated_legacy_building_gets_frontage",(g,r)=>{
            var copy=Fixture(true);copy["snapshot"]=JsonNode.Parse(g.State.ToJsonString());copy["snapshot"]!["meta"]!.AsObject().Remove("native_urban_plan_version");
            var b=new JsonObject{{"building_id","urban-isolated-home"},{"catalog_key","cream_home"},{"cx",100},{"cy",60},{"w",2},{"h",2},{"orientation",0},{"status","ready"},{"placed_ts",g.Now},{"finish_ts",g.Now}};
            copy["snapshot"]!["town_buildings"]!.AsArray().Add(b);var planned=new NativeGame(copy);
            Check(planned.BuildingRoadEntrance(b)!=null,"远离主干道的旧建筑未补齐支路");
            Check(planned.Rows("town_buildings").Any(h=>NativeGame.S(h!,"building_id")=="urban-isolated-home"),"接路不得删除原建筑");planned.Validate();
        });
        Test("roads_preserve_protected_land_and_lots",(g,r)=>{
            var occupied=g.Rows("town_buildings").SelectMany(b=>Enumerable.Range(NativeGame.I(b!,"cy"),NativeGame.I(b!,"h")).SelectMany(y=>Enumerable.Range(NativeGame.I(b!,"cx"),NativeGame.I(b!,"w")).Select(x=>new NativeGame.Cell(x,y)))).ToHashSet();
            foreach(var road in g.Rows("roads")){var cell=new NativeGame.Cell(NativeGame.I(road!,"cx"),NativeGame.I(road!,"cy"));Check(!occupied.Contains(cell),"道路侵入建筑占地");Check(r["terrain"]![cell.Y]![cell.X]!.GetValue<string>() is "grass" or "zone","道路侵入水体或地标");}
        });
        Test("manor_aisles_serve_every_pet_building",(g,r)=>{
            // Filling all existing lots tests future rows, not just the three
            // starter houses; the previous central path cut through column 2.
            foreach(var b in g.Rows("buildings")){b!["kind"]="饲养小屋";b["level"]=1;b["unlocked"]=true;}
            var access=g.UrbanAccessPoints().Where(a=>a.Id.StartsWith("manor:",StringComparison.Ordinal)).ToArray();
            Check(access.Length==g.Rows("buildings").Count,"部分宠物地块没有步行入口");
            foreach(var a in access){
                Check(a.Entrance!=null&&a.Walkway is {Count:>2},"庄园建筑未接到公共道路/内部步行网");
                var path=a.Walkway!;
                for(int i=1;i<path.Count;i++){
                    var from=path[i-1];var to=path[i];double length=Math.Sqrt(Math.Pow(to.X-from.X,2)+Math.Pow(to.Y-from.Y,2));int steps=Math.Max(1,(int)Math.Ceiling(length/.1));
                    for(int step=0;step<=steps;step++){
                        double x=(from.X+(to.X-from.X)*step/steps)*48,y=(from.Y+(to.Y-from.Y)*step/steps)*48;
                        foreach(var b in g.Rows("buildings")){int id=NativeGame.I(b!,"building_id");double bx=1314+id%5*230,by=870+id/5*88;Check(Math.Abs(x-bx)>128*.44+2||Math.Abs(y-by)>70*.415+2,"庄园步行道穿过宠物建筑实体墙");}
                    }
                }
            }
        });
        Test("daytime_workers_cover_districts_night_goes_home",(g,r)=>{
            g.Meta["sim_ts"]=Math.Floor(g.Now/NativeGame.Day)*NativeGame.Day+10*NativeGame.Hour;g.AssignDaytimeDestinations();
            foreach(string zone in new[]{"fields","ranch","market","industry","city","park"})Check(g.People.Count(p=>NativeGame.S(p!,"destination")==zone)>=2,zone+"白天没有稳定工作人口");
            foreach(var resident in g.People.Where(p=>NativeGame.S(p!,"role")=="居民"&&NativeGame.S(p!,"profession")=="农民"))Check(NativeGame.S(resident!,"destination")=="fields","农民未前往农田");
            g.Meta["sim_ts"]=Math.Floor(g.Now/NativeGame.Day)*NativeGame.Day+20*NativeGame.Hour;g.AssignDaytimeDestinations();
            foreach(var p in g.People.Where(p=>NativeGame.S(p!,"role") is "居民" or "通勤者"))Check(NativeGame.S(p!,"destination")==(NativeGame.S(p!,"role")=="居民"?NativeGame.S(p!,"home_zone"):"station"),"入夜没有回家/下班");
        });
        Test("manual_road_removal_not_undone_on_reload",(g,r)=>{
            var removed=g.Rows("roads")[0]!;int x=NativeGame.I(removed,"cx"),y=NativeGame.I(removed,"cy");g.Execute("remove_road",new JsonObject{{"cx",x},{"cy",y}});
            var copy=Fixture();copy["snapshot"]=JsonNode.Parse(g.State.ToJsonString());var again=new NativeGame(copy);
            Check(!again.RoadCells().Contains(new(x,y)),"加载存档不应撤销玩家拆路操作");
        });
        return passed;
    }
}
