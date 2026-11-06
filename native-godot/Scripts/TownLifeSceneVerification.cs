using Godot;
using System;
using System.Linq;
using System.Text.Json.Nodes;

public partial class Main
{
    void VerifyTownLifeScene(Action<bool,string> check)
    {
        var snapshot=Data.Snapshot.DeepClone();
        bool managementOpen=_management?.Visible==true;
        var tab=_tab;
        var target=_managementEntryTarget;
        int farmPlot=_activeFarmPlot;
        try{
            Game.SetZone("fields",1);
            Game.Meta.Remove("farm_plots");
            Game.Execute("farm_plot",new JsonObject{{"operation","sow"},{"plot",1},{"crop","谷物"}});
            UpdateTownLifeFeatures(1);
            check(_petFacilityScene?.GetChildCount()==5,"活动区没有生成对应偏好的设施模型");
            check(_farmGrowthBeds.Count==NativeGame.FarmPlotCount&&_farmGrowthBeds[0].Sign.Text.Contains("发芽"),"农田未生成与真实种植对应的场景地块");
            check(TrySelectTownLifeFeature("farm_growth","1")&&_tab=="环境"&&_managementEntryTarget=="farm-plots","点击农田未到达正确地块");

            Game.SetZone("market",1);
            Game.Meta["sim_ts"]=Game.FestivalOccurrences(Game.Now).First(festival=>festival.Key=="market").Opens;
            Game.AssignDaytimeDestinations();
            UpdateTownLifeFeatures(1);
            check(_festivalStage!=null&&_festivalStageSign?.Text.StartsWith("周末集市",StringComparison.Ordinal)==true,
                "活动窗口没有生成可见摊位");
            check(TrySelectTownLifeFeature("weekend_festival","market")&&_managementEntryTarget=="weekend-festivals","点击活动未到达正确页面");

            using var fountain=CreateDistrictLoop(DistrictSound.Fountain);
            using var pasture=CreateDistrictLoop(DistrictSound.Pasture);
            using var freight=CreateDistrictLoop(DistrictSound.Freight);
            check(fountain.Data.Any(sample=>sample!=0)&&!fountain.Data.SequenceEqual(pasture.Data)&&
                !pasture.Data.SequenceEqual(freight.Data)&&freight.LoopEnd==3*freight.MixRate,"街区环境声未生成有效循环");
            GD.Print("TOWN_LIFE_SCENE_VERIFIED facilities=5 farm_beds=3 festival audio_sources=3");
        }finally{
            Data.Root["snapshot"]=snapshot;Data.Reindex();
            _activeFarmPlot=farmPlot;
            UpdateTownLifeFeatures(1);
            if(managementOpen)ShowManagement(tab,target);else _management?.Hide();
        }
    }
}
