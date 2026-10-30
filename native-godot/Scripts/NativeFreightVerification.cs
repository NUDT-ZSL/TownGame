using System;
using System.Linq;
using System.Text.Json.Nodes;

public static class NativeFreightVerification
{
    public static int Run(JsonObject source)
    {
        int passed=0;
        JsonObject Fixture(){var r=new JsonObject();foreach(string key in new[]{"version","snapshot","rules","terrain","zones","portals"})r[key]=source[key]!.DeepClone();return r;}
        void Check(bool condition,string message){if(!condition)throw new Exception("FREIGHT_TEST_FAILED: "+message);}
        void Test(string name,Action<NativeGame,JsonObject> body){var r=Fixture();var g=new NativeGame(r);g.InitializeFreight();g.Jobs.Clear();foreach(var p in g.People)p!["job_id"]="";g.Commons["enabled"]=true;g.Meta["sim_ts"]=Math.Floor(g.Now/86400)*86400+10*3600;g.FreightState["next_trip_ts"]=0;body(g,r);g.Validate();Godot.GD.Print("RULE_OK freight_"+name);passed++;}
        void Assign(NativeGame g){g.RefreshFreight();Check(NativeGame.S(g.FreightState,"driver_id")!="","需要真实成年居民司机");}
        void Load(NativeGame g){Assign(g);Check(g.BoardFreight(),"司机上车后才允许运输");g.FreightState["phase"]="loading";Check(g.LoadFreight(),"装货应扣真实公共库存");}
        Test("resident_reservation_and_boarding",(g,r)=>{
            Assign(g);var id=NativeGame.S(g.FreightState,"driver_id");var driver=g.People.First(p=>NativeGame.S(p!,"person_id")==id)!;
            Check(NativeGame.S(driver,"role")=="居民"&&NativeGame.B(driver,"freight_reserved")&&NativeGame.S(driver,"job_id")=="","司机不能是虚构姓名或已有工作的人");
            Check(!g.LoadFreight(),"未上车未到装卸区不能获得货物");Check(g.BoardFreight()&&!g.BoardFreight(),"重复上车回调不改变阶段");
        });
        Test("real_cargo_and_single_delivery",(g,r)=>{
            var stock=g.Commons["stock"]!;int food=NativeGame.I(stock,"food"),funds=NativeGame.I(g.Commons,"funds");Load(g);int cargo=NativeGame.I(g.FreightState,"cargo_count");
            Check(cargo>0&&NativeGame.I(stock,"food")==food-cargo,"车载货物来自公共仓库");
            Check(!g.LoadFreight()&&!g.DeliverFreight(),"在途中不能重复装货或远程交货");g.FreightState["phase"]="unloading";
            Check(g.DeliverFreight()&&!g.DeliverFreight(),"到达后卸货仅结算一次");Check(NativeGame.I(g.Commons,"funds")==funds+cargo*3&&NativeGame.I(g.FreightState,"cargo_count")==0,"货物卖入商业区并得到公共收入");
        });
        Test("missing_driver_returns_cargo_once",(g,r)=>{
            int original=NativeGame.I(g.Commons["stock"]!,"food");Load(g);string driver=NativeGame.S(g.FreightState,"driver_id");var p=g.People.First(p=>NativeGame.S(p!,"person_id")==driver);g.People.Remove(p);g.RefreshFreight(false);g.RefreshFreight(false);
            Check(NativeGame.I(g.Commons["stock"]!,"food")==original&&NativeGame.I(g.FreightState,"cargo_count")==0,"司机离开取消运输，物资只退回一次");
        });
        Test("saved_loaded_vehicle_preserves_manifest",(g,r)=>{
            Load(g);string driver=NativeGame.S(g.FreightState,"driver_id");int cargo=NativeGame.I(g.FreightState,"cargo_count"),stock=NativeGame.I(g.Commons["stock"]!,"food");
            var copy=Fixture();copy["snapshot"]=JsonNode.Parse(g.State.ToJsonString());var restored=new NativeGame(copy);restored.InitializeFreight();restored.RefreshFreight(false);
            Check(NativeGame.S(restored.FreightState,"driver_id")==driver&&NativeGame.I(restored.FreightState,"cargo_count")==cargo&&NativeGame.I(restored.Commons["stock"]!,"food")==stock,"重载不重新装货、不丢车载物资");
            restored.FreightState["phase"]="unloading";Check(restored.DeliverFreight()&&!restored.DeliverFreight(),"恢复后的货物仍只交付一次");
        });
        Test("night_no_new_driver_and_loaded_return",(g,r)=>{
            g.Meta["sim_ts"]=Math.Floor(g.Now/86400)*86400+21*3600;g.RefreshFreight();Check(NativeGame.S(g.FreightState,"driver_id")=="","夜间不新派司机");
            g.Meta["sim_ts"]=Math.Floor(g.Now/86400)*86400+10*3600;Load(g);g.Meta["sim_ts"]=Math.Floor(g.Now/86400)*86400+21*3600;g.RefreshFreight();
            Check(NativeGame.I(g.FreightState,"cargo_count")>0&&NativeGame.S(g.FreightState,"driver_id")!="","途中入夜保留司机与货物，先完成运输");
            g.FreightState["phase"]="unloading";g.DeliverFreight();g.ReleaseFreight();g.RefreshFreight();Check(NativeGame.S(g.FreightState,"driver_id")=="","卸货回库后司机释放，可回家");
        });
        Test("food_reserve_prevents_emptying_town",(g,r)=>{
            g.Commons["stock"]!["food"]=1;Assign(g);g.BoardFreight();g.FreightState["phase"]="loading";
            Check(!g.LoadFreight()&&NativeGame.I(g.Commons["stock"]!,"food")==1&&NativeGame.I(g.FreightState,"cargo_count")==0,"口粮不足时空车回库，不制造负库存");
        });
        return passed;
    }
}
