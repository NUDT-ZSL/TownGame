using System;
using System.Linq;
using System.Text.Json.Nodes;

internal static class NativeTownLifeNextVerification
{
    internal static int Run(JsonObject source)
    {
        int passed=0;
        void Check(bool condition,string message)
        {
            if(!condition)throw new InvalidOperationException("TOWN_LIFE_NEXT_TEST_FAILED: "+message);
        }
        void Test(string name,Action<NativeGame,JsonObject> run)
        {
            var fixture=source.DeepClone().AsObject();
            var game=new NativeGame(fixture);
            game.Commons["enabled"]=false;
            run(game,fixture);
            game.Validate();
            Godot.GD.Print("RULE_OK "+name);
            passed++;
        }

        Test("pet_daily_health_observation_and_risks",(game,fixture)=>{
            var pet=game.Pets.OfType<JsonNode>().First(game.Active);
            string id=NativeGame.S(pet,"pet_id");
            pet["hunger"]=19;
            pet["cleanliness"]=20;
            Check(game.PetCareRisks(pet).Any(risk=>risk.Contains("饱食"))&&game.PetCareRisks(pet).Any(risk=>risk.Contains("清洁")),"实际照护风险未识别");
            pet["hunger"]=98;pet["cleanliness"]=98;
            game.Advance(NativeGame.Day+30);
            var loaded=new NativeGame(fixture.DeepClone().AsObject());
            var current=loaded.Pets.OfType<JsonNode>().Single(row=>NativeGame.S(row,"pet_id")==id);
            var history=loaded.PetHealthTimeline(current);
            Check(history.Length>=2&&history[0].Day<history[^1].Day&&history[0].Hunger>=history[^1].Hunger,"健康历史未随时间和存档保留");
        });

        Test("incubation_reserves_home_and_hatches_after_reload",(game,fixture)=>{
            string species=game.Rules["SPECIES"]!.AsObject().First(pair=>pair.Value!["egg_available"]==null||NativeGame.B(pair.Value!,"egg_available")).Key;
            string kind=NativeGame.S(game.Spec("SPECIES",species),"building");
            var plots=game.Rows("buildings").OfType<JsonNode>().Where(plot=>NativeGame.S(plot,"kind")=="").Take(2).ToArray();
            plots[0]["kind"]="商店";plots[0]["level"]=1;plots[0]["unlocked"]=true;
            plots[1]["kind"]=kind;plots[1]["level"]=1;plots[1]["unlocked"]=true;
            game.Meta["coins"]=100000;
            int before=game.Pets.Count;
            game.Execute("buy_pet",new JsonObject{{"species",species},{"sex","雌"},{"egg",true}});
            var egg=game.IncubatorEggs().Single();
            Check(game.Pets.Count==before&&egg.Due>game.Now&&game.ManorDemolitionQuote(game.Rows("buildings").OfType<JsonNode>().First(plot=>NativeGame.I(plot,"building_id")==egg.Home)).Reason.Contains("预留"),"购买宠物蛋应先预留床位而不直接生成伙伴");
            game.Advance(egg.Due-game.Now-10);
            Check(game.Pets.Count==before&&game.IncubatorEggs().Length==1,"宠物蛋提前破壳");
            var loaded=new NativeGame(fixture.DeepClone().AsObject());
            loaded.Advance(10);
            var baby=loaded.Pets.Last()!;
            Check(loaded.Pets.Count==before+1&&loaded.IncubatorEggs().Length==0&&NativeGame.I(baby,"building_id")==egg.Home
                &&loaded.RecentHatches().Length==1&&loaded.Stage(baby)=="幼崽","重载后未在预定时刻孵化并入住");
            loaded.Rows("inventory").Clear();
            loaded.Add("清水",2);
            baby["cleanliness"]=20;
            loaded.Execute("juvenile_care",new JsonObject{{"pet_id",NativeGame.S(baby,"pet_id")}});
            Check(NativeGame.D(baby,"cleanliness")==38&&loaded.Stock("清水")==1&&loaded.JuvenileCareReason(baby).Contains("休息"),"幼崽护理未应用效果或冷却");
        });

        Test("tourist_spend_requires_open_actual_arrival",(game,fixture)=>{
            var visitor=new JsonObject{{"person_id","test-tourist"},{"name","旅人"},{"role","游客"},{"destination","estate"},{"status","游览"},
                {"leave_ts",game.Now+NativeGame.Day},{"exit_ts",game.Now+2*NativeGame.Day},{"spend_ts",game.Now+5}};
            game.People.Add(visitor);
            game.Advance(10);
            Check(!NativeGame.B(visitor,"spent"),"旧存档计时字段触发了未到达的消费");
            Check(NativeGame.S(visitor,"destination")=="estate","居民日程覆盖了游客目的地");
            int balance=NativeGame.I(game.Meta,"coins");
            Check(!game.ReportTouristArrival(visitor,"market")&&NativeGame.I(game.Meta,"coins")==balance,"游客到达其他景点时提前结算");
            Check(game.ReportTouristArrival(visitor,"estate")&&NativeGame.I(game.Meta,"coins")>balance,"游客到达计划景点时未结算");
            balance=NativeGame.I(game.Meta,"coins");
            Check(!game.ReportTouristArrival(visitor,"estate")&&NativeGame.I(game.Meta,"coins")==balance,"景点收入重复结算");
            var loaded=new NativeGame(fixture.DeepClone().AsObject());
            Check(loaded.TourismReports().Single(report=>report.Zone=="estate").Arrived==1,"游客到达记录未随存档保留");
        });

        Test("neighbor_story_result_persists",(game,fixture)=>{
            const string publisher="test-story";
            var order=new JsonObject{{"order_id","story-finale"},{"kind","item"},{"status","进行中"},{"item","清水"},{"quantity",1},
                {"expiry_ts",game.Now+NativeGame.Day},{"reward",10},{"publisher_id",publisher},{"publisher_work","ranch"},{"story_step",3},{"npc","牧民"}};
            game.Rows("inventory").Clear();
            game.Add("清水",1);
            game.Rows("orders").Add(order);
            game.Execute("order",new JsonObject{{"order_id","story-finale"}});
            var loaded=new NativeGame(fixture.DeepClone().AsObject());
            var result=loaded.NeighborhoodResults().Single();
            Check(result.Publisher==publisher&&result.Kind=="walk"&&loaded.NeighborhoodProgress(publisher).StoryStep==3,"邻里故事成果未持久化");
        });
        return passed;
    }
}
