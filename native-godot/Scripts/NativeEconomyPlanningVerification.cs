using System;
using System.Linq;
using System.Text.Json.Nodes;

internal static class NativeEconomyPlanningVerification
{
    internal static int Run(JsonObject source)
    {
        int passed=0;
        void Check(bool okay,string reason){if(!okay)throw new InvalidOperationException("ECONOMY_PLANNING_FAILED: "+reason);}
        void Test(string name,Action<NativeGame,JsonObject> verify)
        {
            var root=new JsonObject();
            foreach(string key in new[]{"version","snapshot","rules","terrain","zones","portals"})root[key]=source[key]!.DeepClone();
            var game=new NativeGame(root);
            game.Commons["enabled"]=false;
            game.Meta["coins"]=10000;
            game.Rows("inventory").Clear();
            game.Rows("orders").Clear();
            verify(game,root);
            game.Validate();
            passed++;
            Godot.GD.Print("RULE_OK "+name);
        }

        Test("inventory_floor_uses_committed_outflow_and_reload",(game,root)=>{
            game.Add("清水",8);
            game.Execute("supply_minimum",new JsonObject{{"item","清水"},{"minimum",7}});
            game.Take("清水",2);
            try{game.Take("清水",7);}catch(InvalidOperationException){}
            var restored=new NativeGame(root.DeepClone().AsObject());
            var status=restored.SupplyStatus("清水");
            Check(status is {Available:6,Minimum:7,OutflowPerDay:2,Runway:3}&&restored.SupplyShortages().Single().Item=="清水",
                "库存下限未使用实际出库或未随存档保留");
            restored.Execute("supply_minimum",new JsonObject{{"item","清水"},{"minimum",0}});
            Check(!restored.SupplyShortages().Any(),"关闭库存下限后仍提醒");
        });
        Test("order_quote_separates_cash_and_stock_value",(game,root)=>{
            var shop=game.Rows("buildings").OfType<JsonNode>().FirstOrDefault(building=>NativeGame.S(building,"kind")=="加工坊")??
                game.Rows("buildings").OfType<JsonNode>().First(building=>NativeGame.S(building,"kind")=="");
            shop["kind"]="加工坊";shop["level"]=1;
            game.Add("高级饲料",1);game.Add("谷物",3);game.Add("清水",1);
            var order=new JsonObject{{"kind","item"},{"item","高级饲料"},{"quantity",3},{"reward",60},{"order_id","test-margin"}};
            string before=game.State.ToJsonString();
            var value=game.QuoteOrderValue(order);
            Check(value.Reward==60&&value.Missing==2&&value.OnHandValue>0&&value.Buy?.Purchase==2*game.BuyPrice("高级饲料")&&
                value.Craft is {Purchase:0,Fee:3,MaterialValue:>0}&&game.State.ToJsonString()==before,
                "订单报价未按现货抵扣、区分加工费，或在预览时改变库存");
        });
        Test("stage_orders_guarantee_feasible_and_separate_challenges",(game,root)=>{
            game.Add("普通饲料",3);
            game.Meta["next_order_ts"]=game.Now+1;
            game.Advance(1);
            var matched=game.Rows("orders").OfType<JsonNode>().Single(order=>NativeGame.B(order,"stage_matched"));
            Check(!NativeGame.B(matched,"challenge")&&NativeGame.S(matched,"item")=="普通饲料"&&NativeGame.I(matched,"quantity")==3&&
                game.Rows("orders").OfType<JsonNode>().Any(order=>NativeGame.B(order,"challenge")&&NativeGame.S(order,"kind")=="pet"),
                "阶段订单缺少现实可交付选项或挑战未单列");
            game.Rows("orders").Clear();game.Rows("inventory").Clear();game.Meta["coins"]=0;
            foreach(var pet in game.Pets.OfType<JsonNode>()){pet["product_ready_ts"]=0;pet["product_expiry_ts"]=0;pet["next_product_ts"]=game.Now+NativeGame.Day;}
            game.Meta["next_order_ts"]=game.Now+1;
            game.Advance(1);
            var checkin=game.Rows("orders").OfType<JsonNode>().Single(order=>NativeGame.B(order,"stage_matched"));
            Check(NativeGame.S(checkin,"kind")=="checkin","无库存与预算时未提供基础委托");
            game.Execute("order",new JsonObject{{"order_id",NativeGame.S(checkin,"order_id")}});
            Check(NativeGame.S(checkin,"status")=="完成"&&NativeGame.I(game.Meta,"coins")==8,"基础委托无法实际交付");
        });
        Test("farm_rotation_irrigation_and_next_crop_persist",(game,root)=>{
            game.Rows("town_buildings").Clear();game.Rows("roads").Clear();game.Meta["decorations"]=new JsonArray();
            game.SetZone("fields",1);
            foreach(var cell in NativeGame.RoadLine(game.Portal("estate"),game.Portal("fields")))
                game.Rows("roads").Add(new JsonObject{{"cx",cell.X},{"cy",cell.Y},{"road_style","asphalt"}});
            game.Meta["weather"]="晴天";
            game.Meta["next_weather_ts"]=game.Now+NativeGame.Day;
            game.Add("清水",2);
            void Plot(string action,string crop="")=>game.Execute("farm_plot",new JsonObject{{"operation",action},{"plot",1},{"crop",crop}});
            Plot("sow","谷物");
            double initial=game.FarmConditions(1).Moisture;
            game.Advance(NativeGame.Hour);
            Check(game.FarmConditions(1).Moisture<initial,"晴天未蒸发水分");
            Plot("irrigate");
            Check(game.FarmConditions(1).Moisture==100&&game.Stock("清水")==1,"浇水未消耗实物或提升湿度");
            game.Advance(4*NativeGame.Hour);
            double beforeRain=game.FarmConditions(1).Moisture;
            game.Meta["weather"]="小雨";
            game.Advance(NativeGame.Hour);
            Check(game.FarmConditions(1).Moisture>beforeRain&&game.Stock("清水")==1,"雨天未自动补充湿度");
            Plot("plan","鲜草");
            game=new NativeGame(root.DeepClone().AsObject());
            game.Advance(game.FarmPlan(1)!.Mature-game.Now);
            Plot("harvest");
            Check(game.FarmPlan(1) is {Crop:"鲜草",Ready:false}&&game.FarmConditions(1) is {LastCrop:"谷物",NextCrop:""}&&
                game.FarmPlan(1)!.Yield==game.FarmYield("鲜草")+1,"轮作未保存下一茬或收获后未续种并增产");
        });
        return passed;
    }
}
