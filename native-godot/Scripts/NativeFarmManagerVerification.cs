using System;
using System.Linq;
using System.Text.Json.Nodes;

// 使用导出规则的独立内存存档，验证管理员任务不会提前结算或丢失资源。
public static class NativeFarmManagerVerification
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
        void Check(bool condition,string message){if(!condition)throw new Exception("MANAGER_TEST_FAILED: "+message);}
        void Test(string name,Action<NativeGame,JsonObject> body)
        {
            var root=Fixture();var game=new NativeGame(root);
            game.Commons["enabled"]=false;game.Jobs.Clear();
            game.Meta["farm_manager"]=false;game.Meta["caretaker"]=false;game.Meta["manager_job"]=null;
            game.Meta["manager_last_kind"]="";game.Meta["manager_deferred"]=new JsonObject();
            game.Meta["manager_cleaned"]=0;game.Meta["manager_fed"]=0;game.Meta["coins"]=100000;
            game.Rules["EVENT_CHANCE"]=0;game.Rules["FIGHT_CHANCE"]=0;
            foreach(string key in new[]{"next_visitor_ts","next_town_arrival_ts","next_fight_ts","next_rent_ts"})game.Meta[key]=game.Now+100*NativeGame.Day;
            game.Rows("waste").Clear();game.Rows("inventory").Clear();game.Add("普通饲料",10);
            foreach(var pet in game.Pets){pet!["hunger"]=100;pet["illness_budget"]=1000000;pet["next_excretion_ts"]=game.Now+100*NativeGame.Day;}
            body(game,root);game.Validate();passed++;Godot.GD.Print("RULE_OK manager_"+name);
        }
        JsonObject Waste(NativeGame game,string id,int quantity=1)
        {
            var pet=game.Pets[0]!;
            var waste=new JsonObject{{"waste_id",id},{"pet_id",NativeGame.S(pet,"pet_id")},{"building_id",NativeGame.I(pet,"building_id")},{"quantity",quantity},{"created_ts",game.Now},{"u",.3},{"v",.6}};
            game.Rows("waste").Add(waste);return waste;
        }
        void Hire(NativeGame game,bool live=true){game.ManagerSceneDriven=live;game.Execute("farm_manager",new JsonObject());game.RefreshManagerTasks();}
        string JobId(NativeGame game,string kind)
        {
            var job=game.CurrentManagerJob;Check(job!=null&&NativeGame.S(job,"kind")==kind,"应分配"+kind+"任务");
            string id=NativeGame.S(job!,"job_id");Check(id!="","任务有独立身份");return id;
        }
        void Finish(NativeGame game,string id){Check(game.ArriveManagerTask(id),"到达当前任务");Check(game.CompleteManagerTask(id),"到达后完成当前任务");}
        Test("live_clean_waits_for_arrival",(g,r)=>{
            Waste(g,"arrival",2);Hire(g);string id=JobId(g,"clean");
            g.Advance(120);
            Check(g.Rows("waste").Count==1&&g.Stock("粪便")==0,"实时场景时间推进不能远程清扫");
            Check(!g.CompleteManagerTask(id),"未到达前不可提交任务");
            Check(!g.ArriveManagerTask("stale-job")&&!g.CompleteManagerTask("stale-job"),"过期任务回调不得清扫当前目标");
            Finish(g,id);Check(g.Rows("waste").Count==0&&g.Stock("粪便")==2&&NativeGame.I(g.Meta,"manager_cleaned")==2,"抵达后完整入库并计数");
            Check(!g.CompleteManagerTask(id)&&g.Stock("粪便")==2,"重复完成不得重复获得资源");
        });
        Test("feed_consumes_real_food",(g,r)=>{
            var pet=g.Pets[0]!;pet["hunger"]=20;Hire(g);string id=JobId(g,"feed");int feed=g.Stock("普通饲料");
            Check(NativeGame.S(g.CurrentManagerJob!,"pet_id")==NativeGame.S(pet,"pet_id"),"喂食绑定实际饥饿动物");
            Check(!g.CompleteManagerTask(id)&&NativeGame.D(pet,"hunger")==20&&g.Stock("普通饲料")==feed,"到达前不改变饱食度和饲料");
            Finish(g,id);Check(NativeGame.D(pet,"hunger")==50&&g.Stock("普通饲料")==feed-1&&NativeGame.I(g.Meta,"manager_fed")==1,"每次喂食实际消耗一份普通饲料并回复饱食");
            Check(!g.CompleteManagerTask(id)&&g.Stock("普通饲料")==feed-1,"重复回调不能重复扣粮");
        });
        Test("urgent_hunger_before_cleaning",(g,r)=>{
            Waste(g,"urgent-waste");g.Pets[0]!["hunger"]=20;Hire(g);JobId(g,"feed");
            Check(g.Rows("waste").Count==1,"急需喂食优先时保留待清扫粪便");
        });
        Test("normal_duties_do_not_starve_each_other",(g,r)=>{
            Waste(g,"duty-a");Waste(g,"duty-b");g.Pets[0]!["hunger"]=50;Hire(g);
            Finish(g,JobId(g,"clean"));g.RefreshManagerTasks();Finish(g,JobId(g,"feed"));g.RefreshManagerTasks();Finish(g,JobId(g,"clean"));
            Check(g.Rows("waste").Count==0&&NativeGame.D(g.Pets[0]!,"hunger")==80&&g.Stock("粪便")==2&&g.Stock("普通饲料")==9,"普通饥饿与多堆粪便同时存在时两种工作都得到处理");
        });
        Test("blocked_target_does_not_stop_other_work",(g,r)=>{
            Waste(g,"blocked-a");Waste(g,"reachable-b");Hire(g);string old=JobId(g,"clean"),target=NativeGame.S(g.CurrentManagerJob!,"waste_id");
            g.RetryManagerTask(old,"道路暂时阻挡");g.RefreshManagerTasks();string next=JobId(g,"clean");
            Check(next!=old&&NativeGame.S(g.CurrentManagerJob!,"waste_id")!=target,"暂时受阻的目标让位给其他可做的任务");
            Finish(g,next);Check(g.Rows("waste").Count==1&&NativeGame.S(g.Rows("waste")[0]!,"waste_id")==target,"受阻的粪便保留，其他目标成功清扫");
        });
        Test("removed_target_cancels_cleaning",(g,r)=>{
            Waste(g,"manual-clean");Hire(g);string id=JobId(g,"clean");
            g.Execute("clean_waste",new JsonObject{{"waste_id","manual-clean"}});g.RefreshManagerTasks();
            Check(!g.ArriveManagerTask(id)&&!g.CompleteManagerTask(id),"玩家先清理后取消旧任务");
            Check(g.Stock("粪便")==1&&NativeGame.I(g.Meta,"manager_cleaned")==0,"人工清理不会被管理员重复计数");
        });
        Test("departed_animal_cancels_feeding",(g,r)=>{
            var pet=g.Pets[0]!;pet["hunger"]=20;Hire(g);string id=JobId(g,"feed");int stock=g.Stock("普通饲料");
            pet["departure"]="测试离园";g.RefreshManagerTasks();
            Check(!g.CompleteManagerTask(id)&&g.Stock("普通饲料")==stock&&NativeGame.D(pet,"hunger")==20,"已离园目标取消，饲料不扣除");
        });
        Test("resource_loss_during_travel",(g,r)=>{
            var pet=g.Pets[0]!;pet["hunger"]=20;Hire(g);string id=JobId(g,"feed");
            g.Take("普通饲料",g.Stock("普通饲料"));g.ArriveManagerTask(id);
            Check(!g.CompleteManagerTask(id)&&g.Stock("普通饲料")==0&&NativeGame.D(pet,"hunger")==20,"途中饲料用尽时不能免费喂食");
            Check(NativeGame.I(g.Meta,"manager_fed")==0,"失败的喂食不增加统计");
        });
        Test("full_storage_preserves_ground_waste",(g,r)=>{
            Waste(g,"full",2);Hire(g);string id=JobId(g,"clean");
            g.Add("清水",g.Storage-g.Rows("inventory").Sum(b=>NativeGame.I(b!,"quantity")));g.ArriveManagerTask(id);
            Check(!g.CompleteManagerTask(id)&&g.Rows("waste").Sum(w=>NativeGame.I(w!,"quantity"))==2&&g.Stock("粪便")==0,"途中仓库变满时整堆粪便保留地面");
            Check(NativeGame.I(g.Meta,"manager_cleaned")==0,"入库失败不能增加清扫统计");
        });
        Test("fostered_animals_are_not_targets",(g,r)=>{
            foreach(var pet in g.Pets){pet!["hunger"]=10;pet["fostered"]=true;}Hire(g);
            Check(g.CurrentManagerJob==null&&g.Stock("普通饲料")==10,"寄养动物不在场，不安排喂食");
        });
        Test("wage_failure_stops_pending_job",(g,r)=>{
            Waste(g,"unpaid");Hire(g);string id=JobId(g,"clean");g.Meta["coins"]=0;g.Meta["manager_next_wage_ts"]=g.Now+1;g.Advance(2);
            Check(!NativeGame.B(g.Meta,"farm_manager")&&g.CurrentManagerJob==null,"工资不足停止雇佣并取消任务");
            Check(!g.CompleteManagerTask(id)&&g.Rows("waste").Count==1,"停工后旧回调不能继续工作");
        });
        Test("pending_task_survives_save_reload",(g,r)=>{
            Waste(g,"saved-job");Hire(g);string id=JobId(g,"clean");Check(g.ArriveManagerTask(id),"保存时正在目标旁工作");var copy=Fixture();copy["snapshot"]=JsonNode.Parse(g.State.ToJsonString());
            var restored=new NativeGame(copy){ManagerSceneDriven=true};restored.RefreshManagerTasks();
            Check(JobId(restored,"clean")==id,"重开保留目标与任务身份");
            restored.Advance(60);Check(restored.Rows("waste").Count==1&&!restored.CompleteManagerTask(id),"恢复到实时场景后仍等待行走，不能沿用上次到达标记");
            Finish(restored,id);Check(restored.Stock("粪便")==1&&!restored.CompleteManagerTask(id),"恢复后的任务只结算一次");restored.Validate();
        });
        Test("offline_continuity",(g,r)=>{
            Waste(g,"offline",2);g.Pets[0]!["hunger"]=20;Hire(g,false);
            var copy=Fixture();copy["snapshot"]=JsonNode.Parse(g.State.ToJsonString());var restored=new NativeGame(copy);
            g.Advance(12*NativeGame.Hour);for(int i=0;i<24;i++)restored.Advance(NativeGame.Hour/2);
            Check(NativeGame.I(g.Meta,"manager_cleaned")==2&&NativeGame.I(g.Meta,"manager_fed")>0,"离线推进仍结算清扫和真实喂食");
            Check(NativeGame.I(g.Meta,"manager_cleaned")==NativeGame.I(restored.Meta,"manager_cleaned")&&NativeGame.I(g.Meta,"manager_fed")==NativeGame.I(restored.Meta,"manager_fed")&&g.Stock("普通饲料")==restored.Stock("普通饲料")&&g.Stock("粪便")==restored.Stock("粪便"),"离线与分段推进管理员资源结果一致");
            Check(Math.Abs(NativeGame.D(g.Pets[0]!,"hunger")-NativeGame.D(restored.Pets[0]!,"hunger"))<.001,"离线喂食时间不随调用频率漂移");restored.Validate();
        });
        return passed;
    }
}
