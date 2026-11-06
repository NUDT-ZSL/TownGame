using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;

internal static class NativeManagementExpansionVerification
{
    internal static int Run(JsonObject source)
    {
        int passed=0;
        void Check(bool valid,string message)
        {
            if(!valid)throw new InvalidOperationException("MANAGEMENT_EXPANSION_TEST_FAILED: "+message);
        }
        void Test(string name,Action<NativeGame> test)
        {
            var assets=new JsonObject();
            foreach(string key in new[]{"version","snapshot","rules","terrain","zones","portals"})assets[key]=source[key]!.DeepClone();
            var game=new NativeGame(assets);
            game.Meta["coins"]=10_000;
            game.Commons["enabled"]=false;
            game.Rows("inventory").Clear();
            game.Rows("orders").Clear();
            test(game);
            game.Validate();
            passed++;
            Godot.GD.Print("RULE_OK "+name);
        }
        void Order(NativeGame game,string id,string item,int quantity,double expires)
        {
            game.Rows("orders").Add(new JsonObject{
                {"order_id",id},{"kind","item"},{"status","进行中"},{"item",item},{"quantity",quantity},
                {"npc","测试邻居"},{"created_ts",game.Now},{"expiry_ts",expires},{"reward",25}
            });
        }
        JsonObject CommitBlueprint(int cost,params NativeGame.BlueprintStep[] steps)=>new()
        {
            ["cost"]=cost,
            ["steps"]=new JsonArray(steps.Select(step=>(JsonNode)new JsonObject{
                {"action",step.Action},{"args",step.Args.DeepClone()}
            }).ToArray())
        };

        Test("blueprint_atomic_validation_and_save",game=>{
            string decor=game.Rules["DECORATIONS"]!.AsObject().First().Key;
            var cell=(from y in Enumerable.Range(0,80)
                      from x in Enumerable.Range(0,125)
                      let candidate=new NativeGame.Cell(x,y)
                      where game.DecorationQuote(decor,x,y,0).Reason==""&&game.RoadReason(candidate,"asphalt")==""
                      select candidate).First();
            var road=new NativeGame.BlueprintStep("road_stroke",new JsonObject{
                {"style","asphalt"},{"cells",new JsonArray(new JsonArray(cell.X,cell.Y))}
            });
            NativeGame.BlueprintStep Decoration(NativeGame.Cell at)=>new("decoration",new JsonObject{
                {"kind",decor},{"operation","place"},{"cx",at.X},{"cy",at.Y}
            });
            var other=(from y in Enumerable.Range(0,80)
                       from x in Enumerable.Range(0,125)
                       where (x!=cell.X||y!=cell.Y)&&game.DecorationQuote(decor,x,y,0).Reason==""
                       select new NativeGame.Cell(x,y)).First();
            var snapshot=game.State;
            string before=snapshot.ToJsonString();
            long revision=game.WorldRevision;
            Check(game.PreviewBlueprint(new[]{road,Decoration(cell)}) is {ValidSteps:1,Reason.Length:>0},
                "蓝图未检查前序道路的占位");
            Check(ReferenceEquals(snapshot,game.State)&&game.State.ToJsonString()==before&&game.WorldRevision==revision,
                "预览改变了真实存档");
            var steps=new[]{road,Decoration(other)};
            var estimate=game.PreviewBlueprint(steps);
            Check(estimate.ValidSteps==2&&estimate.Cost>0&&estimate.Reason=="","合法蓝图报价错误");
            bool rejected=false;
            try{game.Execute("blueprint",CommitBlueprint(estimate.Cost-1,steps));}
            catch(InvalidOperationException){rejected=true;}
            Check(rejected&&game.State.ToJsonString()==before,"旧报价提交未回滚");
            bool saveFailed=false;
            try{game.Execute("blueprint",CommitBlueprint(estimate.Cost,steps),()=>throw new IOException("测试存档失败"));}
            catch(IOException){saveFailed=true;}
            Check(saveFailed&&game.State.ToJsonString()==before,"蓝图存档失败后未整体回滚");
            game.Execute("blueprint",CommitBlueprint(estimate.Cost,steps));
            Check(game.RoadCells().Contains(cell)&&game.Meta["decorations"]!.AsArray().Any(row=>
                NativeGame.I(row!,"cx")==other.X&&NativeGame.I(row!,"cy")==other.Y)&&
                NativeGame.I(game.Meta,"coins")==10_000-estimate.Cost,"提交未统一应用全部操作和预算");
        });

        Test("building_preview_uses_actual_storage",game=>{
            var plot=game.Rows("buildings").OfType<JsonNode>().First();
            plot["kind"]="仓库";
            plot["level"]=2;
            int storage=game.Storage,growth=NativeGame.I(game.Rules,"STORAGE_PER_LEVEL",100);
            Check(game.ManorUpgradeImpact(plot).Contains($"{storage} → {storage+growth}",StringComparison.Ordinal)&&
                game.ManorDemolitionImpact(plot).Contains($"{storage} → {storage-2*growth}",StringComparison.Ordinal),
                "仓储升级或拆除影响未使用实际容量规则");
        });

        Test("procurement_respects_other_orders",game=>{
            const string item="谷物";
            foreach(string id in new[]{"first","second","other"})Order(game,id,item,4,game.Now+NativeGame.Day);
            game.Add(item,8);
            void Reserve(string id,int amount)=>game.Execute("reserve_stock",new JsonObject{
                {"owner",NativeGame.OrderReservationOwner(id)},{"item",item},{"quantity",amount}
            });
            Reserve("first",3);
            Reserve("other",2);
            var selected=new HashSet<string>{"first","second"};
            Check(game.OrderProcurement(selected).Single() is {Required:8,ReservedForOrders:3,Free:3,OtherReserved:2,Missing:2},
                "合并采购重复计算可用库存或用了外部预留");
            game.Rows("orders")[1]!["status"]="过期";
            Check(game.OrderProcurement(selected).Single() is {Required:4,Missing:0},"已结束订单仍计入合并采购");
        });

        Test("expense_forecast_is_separate",game=>{
            game.Meta["farm_manager"]=true;
            game.Meta["manager_next_wage_ts"]=game.Now+6*NativeGame.Hour;
            game.Meta["caretaker"]=true;
            game.Meta["next_care_ts"]=game.Now+3*NativeGame.Hour;
            game.Commons["funds"]=160;
            var worker=game.People.OfType<JsonNode>().First();
            game.Jobs.Clear();
            game.Jobs.Add(new JsonObject{
                {"kind","build"},{"due",game.Now+NativeGame.Hour},{"worker",NativeGame.S(worker,"person_id")},
                {"cost",new JsonObject{{"funds",40},{"wood",2}}}
            });
            var forecast=game.UpcomingExpenses();
            Check(forecast.Wages is [{Role:"饲养员",Shifts:8},{Role:"农场管理员",Shifts:4}]&&
                forecast.PrivateRemaining==10_000-forecast.ScheduledPrivate&&forecast.PublicBalance==160&&
                forecast.PublicCommitted==40&&forecast.Commitments[0].Materials.Contains("木材",StringComparison.Ordinal),
                "支出预报混用了个人工资或公共预算");
        });

        Test("farm_plot_routes_fresh_harvest_only",game=>{
            game.Rows("town_buildings").Clear();
            game.Meta["decorations"]=new JsonArray();
            game.Rows("roads").Clear();
            game.SetZone("fields",1);
            foreach(var cell in NativeGame.RoadLine(game.Portal("estate"),game.Portal("fields")))
                game.Rows("roads").Add(new JsonObject{{"cx",cell.X},{"cy",cell.Y},{"road_style","asphalt"}});
            const string item="谷物",id="farm-supply";
            double maturity=game.FarmMaturity;
            Order(game,id,item,20,maturity+NativeGame.Day);
            game.Add(item,2);
            game.Execute("farm_plot",new JsonObject{
                {"operation","sow"},{"plot",1},{"crop",item},{"order_id",id}
            });
            var estimate=game.FarmPlan(1);
            Check(estimate is {Ready:false}&&estimate.Mature==maturity&&estimate.Yield>0,"分地种植未建立成熟计划");
            var harvest=new JsonObject{{"operation","harvest"},{"plot",1}};
            bool rejected=false;
            try{game.Execute("farm_plot",harvest);}
            catch(InvalidOperationException){rejected=true;}
            Check(rejected&&game.Stock(item)==2,"未成熟作物被提前收取");
            game.Advance(maturity-game.Now);
            var ready=game.FarmPlan(1);
            Check(ready is {Ready:true},"到时未成熟");
            game.Execute("farm_plot",harvest);
            Check(game.Stock(item)==2&&game.InventoryAvailability(item,NativeGame.OrderReservationOwner(id)).ReservedForOwner==ready!.Yield&&
                game.FarmPlan(1)==null,"收获没有只把新增批次留给所选订单");
            game.Execute("farm_plot",new JsonObject{{"operation","resume"}});
            Check(!game.FarmPlotsEnabled,"收获完毕无法恢复自动混种");
        });
        return passed;
    }
}
