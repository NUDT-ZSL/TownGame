using System;
using System.Linq;
using System.Text.Json.Nodes;

internal static class NativeTownLifeVerification
{
    internal static int Run(JsonObject source)
    {
        void Check(bool passed,string reason)
        {
            if(!passed)throw new InvalidOperationException("TOWN_LIFE_TEST_FAILED: "+reason);
        }
        NativeGame Fixture()
        {
            var fixture=new JsonObject();
            foreach(string key in new[]{"version","snapshot","rules","terrain","zones","portals"})fixture[key]=source[key]!.DeepClone();
            var game=new NativeGame(fixture);
            game.Commons["enabled"]=false;
            return game;
        }

        var market=Fixture();
        market.SetZone("market",1);
        Check(market.Effective("market"),"集市道路夹具未接通");
        var weekend=market.FestivalOccurrences(market.Now).First(eventDay=>eventDay.Key=="market");
        market.Meta["sim_ts"]=weekend.Opens;
        market.AssignDaytimeDestinations();
        var resident=market.People.OfType<JsonNode>().FirstOrDefault(person=>NativeGame.S(person,"native_festival")=="market");
        Check(market.CurrentFestival?.Key=="market"&&resident!=null&&NativeGame.S(resident,"destination")=="market","周末集市没有分配真实居民");
        market.Add("谷物",2);
        int stock=market.Stock("谷物");
        var attend=new JsonObject{{"festival","market"},{"resident_id",NativeGame.S(resident!,"person_id")}};
        market.Execute("weekend_festival",attend);
        Check(market.Stock("谷物")==stock-2&&market.FestivalClaimed(weekend)&&market.FestivalDecorationCredits(weekend.Decoration)==1,"集市交付未结算到库存或装饰券");
        bool duplicate=false;
        try{market.Execute("weekend_festival",attend);}catch(InvalidOperationException){duplicate=true;}
        Check(duplicate&&market.Stock("谷物")==stock-2,"同一场活动可重复领取奖励");
        var site=Enumerable.Range(0,125*80).Select(i=>(X:i%125,Y:i/125)).First(cell=>market.DecorationQuote(weekend.Decoration,cell.X,cell.Y,0).Reason=="");
        int coins=NativeGame.I(market.Meta,"coins");
        market.Execute("decoration",new JsonObject{{"operation","place"},{"kind",weekend.Decoration},{"cx",site.X},{"cy",site.Y}});
        Check(market.FestivalDecorationCredits(weekend.Decoration)==0&&NativeGame.I(market.Meta,"coins")==coins,"免费摆放未准确使用奖励");
        market.Execute("decoration",new JsonObject{{"operation","undo"}});
        Check(market.FestivalDecorationCredits(weekend.Decoration)==1,"撤销未退回活动装饰券");
        Godot.GD.Print("RULE_OK recurring_festival_attendees_and_decoration");

        var farm=Fixture();
        farm.SetZone("fields",1);
        farm.Execute("farm_plot",new JsonObject{{"operation","sow"},{"plot",1},{"crop","谷物"}});
        Check(farm.FarmVisualPlot(1).Stage==NativeGame.FarmGrowthStage.Seedling&&farm.FarmVisualPlot(2).Stage==NativeGame.FarmGrowthStage.Empty,"播种阶段不匹配地块");
        double maturity=farm.FarmPlan(1)!.Mature;
        double half=(maturity-farm.Now)/2;
        farm.Advance(half);
        Check(farm.FarmVisualPlot(1).Stage==NativeGame.FarmGrowthStage.Growing,"生长阶段没有更新");
        farm.Advance(maturity-farm.Now);
        Check(farm.FarmVisualPlot(1).Stage==NativeGame.FarmGrowthStage.Ripe,"成熟阶段没有更新");
        farm.Execute("farm_plot",new JsonObject{{"operation","harvest"},{"plot",1}});
        Check(farm.FarmVisualPlot(1).Stage==NativeGame.FarmGrowthStage.Harvested,"收获后没有留下可识别状态");
        Godot.GD.Print("RULE_OK farm_plot_visual_growth_and_harvest");

        var manor=Fixture();
        var pet=manor.Pets.OfType<JsonNode>().First(manor.Active);
        string id=NativeGame.S(pet,"pet_id");
        Check(NativeGame.PreferredPetFacility(new JsonObject{{"species","猫咪"}})==NativeGame.PetFacility.Climb&&
            NativeGame.PreferredPetFacility(new JsonObject{{"species","小鸭"}})==NativeGame.PetFacility.Splash&&
            NativeGame.PersonalityPetFacility(new JsonObject{{"personality","胆小"}})==NativeGame.PetFacility.Shelter,"物种或性格偏好缺失");
        for(int visit=0;visit<8;visit++)manor.RecordPetFacilityUse(id,NativeGame.PetFacility.Dig);
        Check(manor.PetFacilityUseCount(id,NativeGame.PetFacility.Dig)==8&&manor.RecentPetFacilityUses(id).Length==6,"设施累计或最近记录不正确");
        pet["fostered"]=true;
        manor.RecordPetFacilityUse(id,NativeGame.PetFacility.Dig);
        Check(manor.PetFacilityUseCount(id,NativeGame.PetFacility.Dig)==8,"不在场的伙伴错误生成设施记录");
        Godot.GD.Print("RULE_OK pet_facility_preferences_and_activity_records");
        return 3;
    }
}
