using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;

// 真实导出地图上的住房/家庭回归；不依赖画面或随机抽中某种自治任务。
public static class NativeHousingVerification
{
    public static int Run(JsonObject source)
    {
        int passed=0;
        JsonObject Fixture()
        {
            var root=new JsonObject();
            foreach(string key in new[]{"version","snapshot","rules","terrain","zones","portals"})root[key]=source[key]!.DeepClone();
            return root;
        }
        void Check(bool condition,string message){if(!condition)throw new Exception("HOUSING_TEST_FAILED: "+message);}
        void Test(string name,Action<NativeGame,JsonObject> body)
        {
            var root=Fixture();var game=new NativeGame(root);body(game,root);game.Validate();
            passed++;Godot.GD.Print("RULE_OK housing_"+name);
        }
        JsonNode[] Residents(NativeGame g)=>g.People.Where(p=>NativeGame.S(p!,"role")=="居民").Select(p=>p!).ToArray();
        string Assignment(NativeGame g)=>string.Join("|",Residents(g).OrderBy(p=>NativeGame.S(p,"person_id")).Select(p=>$"{NativeGame.S(p,"person_id")}:{NativeGame.S(p,"home_building_id")}:{NativeGame.S(p,"household_id")}"));
        JsonObject SavedFixture(NativeGame g)
        {
            var copy=Fixture();
            // 真正穿过JSON序列化，而不是共享原状态中的对象引用。
            copy["snapshot"]=JsonNode.Parse(g.State.ToJsonString());
            return copy;
        }
        void CheckHomes(NativeGame g)
        {
            foreach(var p in Residents(g)){
                var home=g.ResidentHome(p);Check(home!=null,"每位现有居民都拥有真实住宅");
                Check(NativeGame.S(home!,"status")=="ready"&&g.IsResidentialHome(home!),"固定家必须是已完工住宅");
                Check(NativeGame.S(p,"household_id")!="","家庭身份必须持久化");
                Check(g.HomeMembers(NativeGame.S(home!,"building_id")).Any(m=>NativeGame.S(m,"person_id")==NativeGame.S(p,"person_id")),"房屋成员查询能找到住户");
                Check(NativeGame.S(p,"sex") is "男" or "女","家庭性别资料有效");
                Check(g.ResidentAgeDays(p)>=0,"年龄不能为负");
            }
            foreach(var home in g.Rows("town_buildings").Where(b=>g.IsResidentialHome(b!)))
                Check(g.HomeMembers(NativeGame.S(home!,"building_id")).Count()<=g.ResidentHomeCapacity(home!),"住户不能超出住宅容量");
        }
        Test("fixed_real_homes",(g,r)=>{g.EnsureResidentHomes();CheckHomes(g);foreach(var p in Residents(g))Check(g.HomeRoadEntrance(g.ResidentHome(p)!)!=null,"初始住宅必须有连通的临街入口");});
        Test("reload_is_idempotent",(g,r)=>{
            string assignment=Assignment(g);int count=g.Rows("town_buildings").Count;
            g.EnsureResidentHomes();g.EnsureResidentHomes();Check(Assignment(g)==assignment&&g.Rows("town_buildings").Count==count,"重复同步不搬家、不生成重复住宅");
            var restored=new NativeGame(SavedFixture(g));restored.EnsureResidentHomes();
            Check(Assignment(restored)==assignment&&restored.Rows("town_buildings").Count==count,"存档恢复保持住宅与家庭身份");CheckHomes(restored);
        });
        Test("legacy_family_migration",(g,r)=>{
            var legacy=Fixture();var snapshot=legacy["snapshot"]!;snapshot["meta"]!.AsObject().Remove("native_housing_version");
            foreach(var home in snapshot["town_buildings"]!.AsArray().Where(h=>NativeGame.B(h!,"starter_home")).ToArray())snapshot["town_buildings"]!.AsArray().Remove(home);
            var people=snapshot["people"]!.AsArray();var adults=people.Where(p=>NativeGame.S(p!,"role")=="居民").Select(p=>p!).ToArray();
            foreach(var p in adults){p.AsObject().Remove("home_building_id");p.AsObject().Remove("household_id");p["partner_id"]="";p["pregnant_until"]=0;}
            var mother=adults[0];var father=adults[1];mother["sex"]="女";father["sex"]="男";mother["partner_id"]=NativeGame.S(father,"person_id");father["partner_id"]=NativeGame.S(mother,"person_id");
            var child=mother.DeepClone();child["person_id"]="housing-migration-child";child["name"]="迁移测试儿童";child["born_ts"]=g.Now-NativeGame.Day;child["partner_id"]="";child["parents"]=new JsonArray(NativeGame.S(mother,"person_id"),NativeGame.S(father,"person_id"));child["profession"]="孩童";child["job_id"]="";people.Add(child);
            var migrated=new NativeGame(legacy);CheckHomes(migrated);
            string homeId=NativeGame.S(mother,"home_building_id");
            Check(homeId==NativeGame.S(father,"home_building_id")&&homeId==NativeGame.S(child,"home_building_id"),"旧档迁移时伴侣与未成年孩子同住");
            Check(NativeGame.S(mother,"household_id")==NativeGame.S(child,"household_id"),"孩子保留家庭关联");
            var again=new NativeGame(SavedFixture(migrated));Check(Assignment(migrated)==Assignment(again),"迁移后重开保持家庭分配");
        });
        Test("night_boundaries",(g,r)=>{
            double day=Math.Floor(g.Now/NativeGame.Day)*NativeGame.Day;
            foreach(var (hour,night) in new[]{(6.999,true),(7.0,false),(19.999,false),(20.0,true),(23.99,true),(0.0,true)}){
                g.Meta["sim_ts"]=day+hour*NativeGame.Hour;Check(g.IsResidentNight==night,$"{hour}点的回家/出门边界");
            }
            g.Meta["sim_ts"]=day+8*NativeGame.Hour;
        });
        Test("birth_inherits_home",(g,r)=>{
            var residents=Residents(g);var mother=residents.First(p=>NativeGame.S(p,"sex")=="女");
            var father=residents.First(p=>NativeGame.S(p,"sex")=="男"&&p!=mother);
            mother["partner_id"]=NativeGame.S(father,"person_id");father["partner_id"]=NativeGame.S(mother,"person_id");g.EnsureResidentHomes();
            string home=NativeGame.S(mother,"home_building_id"),family=NativeGame.S(mother,"household_id");
            mother["pregnant_until"]=g.Now+NativeGame.Hour;mother["family_cooldown"]=g.Now+100*NativeGame.Day;g.Commons["stock"]!["food"]=1000;
            var previousIds=residents.Select(p=>NativeGame.S(p,"person_id")).ToHashSet();g.Advance(NativeGame.Hour);
            var newborn=Residents(g).FirstOrDefault(p=>!previousIds.Contains(NativeGame.S(p,"person_id"))&&p["parents"] is JsonArray parents&&parents.Any(id=>id!.GetValue<string>()==NativeGame.S(mother,"person_id")));
            Check(newborn!=null,"有空床的家庭按预产时间迎来孩子");
            Check(NativeGame.S(newborn!,"home_building_id")==home&&NativeGame.S(newborn!,"household_id")==family,"新生儿继承母亲住宅和家庭");CheckHomes(g);
        });
        Test("occupied_demolition_rejected",(g,r)=>{
            var house=g.ResidentHome(Residents(g)[0])!;string before=g.State.ToJsonString();bool rejected=false;
            try{g.Execute("remove_town",new JsonObject{{"building_id",NativeGame.S(house,"building_id")}});}catch(InvalidOperationException){rejected=true;}
            Check(rejected&&g.State.ToJsonString()==before,"有人居住的房屋禁止拆除，失败不改变住房和金币");
        });
        Test("new_house_does_not_move_existing_family",(g,r)=>{
            string assignments=Assignment(g);g.Meta["coins"]=100000;
            foreach(var zone in g.Rows("town_zones"))zone!["level"]=3;
            NativeGame.Cell? site=null;
            for(int y=0;y<80&&site==null;y++)for(int x=0;x<125;x++)if(g.PlacementReason("cream_home",x,y)==""){site=new(x,y);break;}
            Check(site!=null,"真实地图存在可用临街选址");g.Execute("build_town",new JsonObject{{"catalog_key","cream_home"},{"cx",site!.Value.X},{"cy",site.Value.Y}});
            var added=g.Rows("town_buildings").Last()!;added["status"]="ready";added["finish_ts"]=g.Now;g.EnsureResidentHomes();
            Check(Assignment(g)==assignments,"新的空房不触发现有家庭任意搬家");
        });
        Test("disconnected_home_keeps_ownership",(g,r)=>{
            var house=g.ResidentHome(Residents(g)[0])!;string id=NativeGame.S(house,"building_id"),assignments=Assignment(g);
            int x=NativeGame.I(house,"cx"),y=NativeGame.I(house,"cy"),w=NativeGame.I(house,"w"),h=NativeGame.I(house,"h");
            var fronts=new HashSet<NativeGame.Cell>();for(int xx=x;xx<x+w;xx++){fronts.Add(new(xx,y-1));fronts.Add(new(xx,y+h));}for(int yy=y;yy<y+h;yy++){fronts.Add(new(x-1,yy));fronts.Add(new(x+w,yy));}
            foreach(var road in g.RoadCells().Where(fronts.Contains).ToArray())g.Execute("remove_road",new JsonObject{{"cx",road.X},{"cy",road.Y}});
            g.EnsureResidentHomes();Check(g.HomeRoadEntrance(house)==null,"断路房屋没有虚构可通行入口");
            Check(Assignment(g)==assignments&&g.HomeMembers(id).Any(),"断路不剥夺归属或搬往其他家庭");
            var restored=new NativeGame(SavedFixture(g));Check(Assignment(restored)==assignments,"断路后的存档重开也保持归属");
        });
        Test("offline_family_continuity",(g,r)=>{
            var restored=new NativeGame(SavedFixture(g));g.Advance(2*NativeGame.Day);
            for(int i=0;i<48;i++)restored.Advance(NativeGame.Hour);
            Check(Assignment(g)==Assignment(restored),"离线两天与逐小时推进保持相同家庭和住宅");
            Check(g.Commons.ToJsonString()==restored.Commons.ToJsonString(),"夜间休息后的工作预约/生产结果保持一致");CheckHomes(g);CheckHomes(restored);
        });
        Test("night_work_pauses_then_resumes",(g,r)=>{
            g.Commons["enabled"]=false;
            double untilEvening=(20*NativeGame.Hour-g.Now%NativeGame.Day+NativeGame.Day)%NativeGame.Day;
            if(untilEvening>0)g.Advance(untilEvening);
            Check(g.IsResidentNight,"工作暂停测试从20点开始");g.Commons["enabled"]=true;
            var worker=Residents(g).First(p=>g.ResidentAgeDays(p)>=7);worker["job_id"]="housing-night-job";
            var job=new JsonObject{{"id","housing-night-job"},{"kind","gather"},{"zone","estate"},{"worker",NativeGame.S(worker,"person_id")},{"start",g.Now},{"arrive",g.Now+.5*NativeGame.Hour},{"due",g.Now+2*NativeGame.Hour},{"cost",new JsonObject()},{"payload",new JsonObject()}};
            g.Jobs.Add(job);g.Advance(11*NativeGame.Hour);
            var pending=g.Jobs.FirstOrDefault(j=>NativeGame.S(j!,"id")=="housing-night-job");
            Check(!g.IsResidentNight&&pending!=null,"整夜休息后工作仍保留并在7点继续");
            Check(Math.Abs(NativeGame.D(pending!,"due")-(g.Now+2*NativeGame.Hour))<.001,"11小时夜休不吞掉或增加白天工作量");
            g.Advance(2*NativeGame.Hour);Check(!g.Jobs.Any(j=>NativeGame.S(j!,"id")=="housing-night-job"),"次日完成剩余工作");
        });
        return passed;
    }
}
