using System;
using System.Linq;
using System.Text.Json.Nodes;

internal static class NativeOperationsVerification
{
    internal static int Run(JsonObject source)
    {
        int passed=0;
        void Check(bool condition,string message){if(!condition)throw new InvalidOperationException("OPERATIONS_TEST_FAILED: "+message);}
        void Test(string name,Action<NativeGame,JsonObject> run)
        {
            var root=new JsonObject();
            foreach(string key in new[]{"version","snapshot","rules","terrain","zones","portals"})root[key]=source[key]!.DeepClone();
            var game=new NativeGame(root);
            game.Commons["enabled"]=false;
            game.Meta["coins"]=10000;
            game.Rows("inventory").Clear();
            game.Rows("orders").Clear();
            run(game,root);
            game.Validate();
            passed++;
            Godot.GD.Print("RULE_OK "+name);
        }
        void AddOrder(NativeGame game,string item,int count,double due)=>game.Rows("orders").Add(new JsonObject{
            {"order_id","operations-test"},{"kind","item"},{"item",item},{"quantity",count},{"npc","邻居"},
            {"status","进行中"},{"created_ts",game.Now},{"expiry_ts",due},{"reward",10}
        });

        Test("weekly_calendar_known_events",(game,root)=>{
            double orderDue=game.Now+30*NativeGame.Hour;
            AddOrder(game,"谷物",1,orderDue);
            game.Meta["farm_manager"]=true;
            game.Meta["manager_next_wage_ts"]=game.Now+6*NativeGame.Hour;
            var entries=game.CalendarEntries(game.Now+7*NativeGame.Day);
            Check(entries.Count(entry=>entry.Category=="财务")>=27&&entries.Any(entry=>entry.Target=="operations-test"&&entry.Due==orderDue),
                "周历没有显示数日后的工资或委托截止");
        });
        Test("blueprint_template_reloads_and_moves",(game,root)=>{
            var road=new JsonObject{{"style","asphalt"},{"cells",new JsonArray(new JsonArray(8,7),new JsonArray(9,7))}};
            game.Execute("blueprint_template",new JsonObject{{"operation","save"},{"name","林间小径"},{"steps",new JsonArray(new JsonObject{{"action","road_stroke"},{"args",road}})}});
            var restored=new NativeGame(root.DeepClone().AsObject());
            var template=restored.SavedBlueprintTemplates().Single();
            var relocated=restored.MoveBlueprintTemplate(template,new NativeGame.Cell(17,19),-1).Single();
            var cells=relocated.Args["cells"]!.AsArray();
            Check(template.Name=="林间小径"&&cells[0]!.ToJsonString()=="[17,19]"&&cells[1]!.ToJsonString()=="[18,19]"&&
                template.Steps[0].Args["cells"]![0]!.ToJsonString()=="[8,7]","模板重载或重定位改变了相对施工布局");
            _=restored.PreviewBlueprint(new[]{relocated});
        });
        Test("workshop_reserves_cancels_and_finishes",(game,root)=>{
            var workshop=game.Rows("buildings").OfType<JsonObject>().FirstOrDefault(building=>NativeGame.S(building,"kind")=="加工坊")??
                game.Rows("buildings").OfType<JsonObject>().First(building=>NativeGame.S(building,"kind")=="");
            workshop["kind"]="加工坊";workshop["unlocked"]=true;workshop["level"]=1;
            game.Add("谷物",8);game.Add("清水",8);
            game.Execute("workshop_queue",new JsonObject{{"operation","enqueue"},{"item","高级饲料"},{"batches",2}});
            Check(game.Stock("谷物")==2&&game.WorkshopOrders().Length==2,"加工没有预留两批真实材料");
            game.Execute("workshop_queue",new JsonObject{{"operation","cancel"},{"id",game.WorkshopOrders()[1].Id}});
            Check(game.Stock("谷物")==5&&NativeGame.I(game.Meta,"coins")==9997,"取消未开工批次没有返还材料与加工费");
            var restored=new NativeGame(root.DeepClone().AsObject());
            restored.Advance(2*NativeGame.Hour);
            Check(restored.WorkshopOrders().Length==0&&restored.Stock("高级饲料")==2&&restored.Stock("谷物")==5,"重载后加工未实际结算一次");
        });
        Test("expiry_compost_protects_reserved_stock",(game,root)=>{
            const string item="鲜草";
            AddOrder(game,item,2,game.Now+NativeGame.Day);
            game.Add(item,5);
            game.Rows("inventory")[0]!["expiry_ts"]=game.Now+3*NativeGame.Hour;
            string owner=NativeGame.OrderReservationOwner("operations-test");
            game.Execute("reserve_stock",new JsonObject{{"owner",owner},{"item",item},{"quantity",2}});
            game.Execute("expiry_policy",new JsonObject{{"item",item},{"mode","compost"}});
            game.Advance(NativeGame.Hour+1);
            Check(game.FarmCompost==3&&game.Stock(item)==0&&game.InventoryAvailability(item,owner).ReservedForOwner==2,
                "堆肥占用了订单预留或没有处理未预留份数");
        });
        Test("manager_feed_cap_uses_actual_usage",(game,root)=>{
            var pet=game.Pets.OfType<JsonObject>().First(game.Active);
            pet["hunger"]=20;
            game.Rows("waste").Clear();
            game.Add("普通饲料",4);
            int plot=NativeGame.I(pet,"building_id");
            game.Execute("manager_policy",new JsonObject{{"plot",plot},{"feed_limit",1},{"feed",true}});
            game.ManagerSceneDriven=true;
            game.Execute("farm_manager",new JsonObject());
            game.RefreshManagerTasks();
            string id=NativeGame.S(game.CurrentManagerJob!,"job_id");
            Check(game.ManagerPlotPriority(plot,"feed")&&game.ArriveManagerTask(id)&&game.CompleteManagerTask(id)&&game.ManagerFedToday==1,
                "管理员未在真实喂食后计入当天用量");
            game.RefreshManagerTasks();
            Check(game.CurrentManagerJob==null&&game.Stock("普通饲料")==3&&NativeGame.S(game.Meta,"manager_status").Contains("上限",StringComparison.Ordinal),
                "达到上限仍继续安排喂食");
        });
        Test("freight_priority_uses_actual_source_stock",(game,root)=>{
            game.Rows("town_buildings").Clear();
            game.Meta["decorations"]=new JsonArray();
            foreach(string zone in new[]{"industry","market"}){
                game.SetZone(zone,1);
                var roads=game.RoadCells();
                foreach(var cell in NativeGame.RoadLine(game.Portal("estate"),game.Portal(zone)))
                    if(roads.Add(cell))game.Rows("roads").Add(new JsonObject{{"cx",cell.X},{"cy",cell.Y},{"road_style","asphalt"}});
            }
            game=new NativeGame(root);
            game.Commons["stock"]!["wood"]=12;
            game.Execute("freight_priority",new JsonObject{{"priority","wood"}});
            var route=game.FreightDispatchQueue().First();
            Check(route is {Source:"industry",Destination:"market",Ready:4,Reason:""}&&game.PlannedFreightSource()=="industry",
                "木材优先调度未使用实际连通生产区和可运数量");
        });
        return passed;
    }
}
