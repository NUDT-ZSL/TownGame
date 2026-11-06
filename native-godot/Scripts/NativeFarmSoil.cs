using System;
using System.Text.Json.Nodes;

public sealed partial class NativeGame
{
    const string FarmSoilKey="native_farm_soil";
    internal readonly record struct FarmSoil(double Moisture,string LastCrop,int Repeats,string NextCrop);

    internal FarmSoil FarmConditions(int plot)
    {
        var soil=Meta[FarmSoilKey]?[plot.ToString()];
        return soil==null?new(72,"",0,""):new(D(soil,"moisture",72),S(soil,"last_crop"),I(soil,"repeats"),S(soil,"next_crop"));
    }

    JsonObject TrackFarmSoil(int plot)
    {
        var soils=(Meta[FarmSoilKey]??=new JsonObject()).AsObject();
        return (soils[plot.ToString()]??=new JsonObject{{"moisture",72d},{"updated",Now}}).AsObject();
    }

    void SyncFarmMoisture()
    {
        if(Meta[FarmSoilKey] is not JsonObject soils)return;
        foreach(var pair in soils){
            if(pair.Value is not JsonObject soil)continue;
            double hours=Math.Max(0,(Now-D(soil,"updated",Now))/Hour);
            if(hours==0)continue;
            double rate=Weather switch{"暴雨"=>14,"小雨"=>8,"雨夹雪"=>4,"阴天"=>-1,_=>-2};
            soil["moisture"]=Math.Clamp(D(soil,"moisture",72)+hours*rate,0,100);
            soil["updated"]=Now;
        }
    }

    internal int ExpectedFarmYield(int plot,string crop,bool fertilized=false)=>ExpectedFarmYield(FarmConditions(plot),crop,fertilized);

    internal int ExpectedFarmYield(FarmSoil soil,string crop,bool fertilized=false)
    {
        int rotation=soil.LastCrop.Length==0?0:soil.LastCrop!=crop?1:soil.Repeats>=2?-1:0;
        double moisture=soil.Moisture<40?.76:1;
        return Math.Max(1,(int)Math.Round(FarmYield(crop)*moisture)+rotation)+(fertilized?1:0);
    }

    internal FarmSoil FarmConditionsAfterHarvest(int plot,string crop)
    {
        var previous=FarmConditions(plot);
        return previous with{LastCrop=crop,Repeats=previous.LastCrop==crop?Math.Min(3,previous.Repeats+1):1};
    }

    void RecordFarmCrop(int plot,string crop)
    {
        var updated=FarmConditionsAfterHarvest(plot,crop);
        var soil=TrackFarmSoil(plot);
        soil["last_crop"]=updated.LastCrop;
        soil["repeats"]=updated.Repeats;
    }
}
