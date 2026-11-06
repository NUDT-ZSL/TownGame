using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;

public sealed partial class NativeGame
{
    internal enum FarmGrowthStage { Empty, Seedling, Growing, Ripe, Harvested }
    internal readonly record struct FarmVisualCrop(string Crop,FarmGrowthStage Stage);

    internal FarmVisualCrop FarmVisualPlot(int plot)
    {
        double interval=Math.Max(1,Rule("TOWN_FIELD_INTERVAL"));
        double recent=Meta["native_farm_harvest"] is JsonObject cuts?D(cuts,plot.ToString()):0;
        if(FarmPlan(plot) is { } plan)return new(plan.Crop,plan.Ready?FarmGrowthStage.Ripe:
            plan.Mature-Now>interval*.65?FarmGrowthStage.Seedling:FarmGrowthStage.Growing);
        if(recent>0&&Now-recent<interval)return new("",FarmGrowthStage.Harvested);
        if(FarmPlotsEnabled||!Effective("fields"))return new("",FarmGrowthStage.Empty);
        string crop=FarmCrops[(plot-1)%FarmCrops.Length];
        if(D(Meta,"field_ready_ts")>0&&D(Meta,"field_expiry_ts")>Now)return new(crop,FarmGrowthStage.Ripe);
        double next=D(Meta,"next_field_ts");
        return next>Now?new(crop,next-Now>interval*.65?FarmGrowthStage.Seedling:FarmGrowthStage.Growing):new("",FarmGrowthStage.Empty);
    }

    void MarkFarmVisualHarvest(int plot)
    {
        var recent=(Meta["native_farm_harvest"]??=new JsonObject()).AsObject();
        if(plot>0)recent[plot.ToString()]=Now;
        else for(int number=1;number<=FarmPlotCount;number++)recent[number.ToString()]=Now;
    }
}

public partial class Main
{
    readonly List<(Node3D Root,MeshInstance3D Mesh,Label3D Sign,NativeGame.FarmVisualCrop? Crop)> _farmGrowthBeds=new();
    readonly Dictionary<NativeGame.FarmVisualCrop,ArrayMesh> _farmGrowthMeshes=new();
    string _farmGrowthLayout="";

    static string FarmGrowthCaption(NativeGame.FarmVisualCrop crop)=>crop.Stage switch{
        NativeGame.FarmGrowthStage.Empty=>"空地",NativeGame.FarmGrowthStage.Harvested=>"已收割",
        NativeGame.FarmGrowthStage.Seedling=>$"{crop.Crop} · 发芽",NativeGame.FarmGrowthStage.Growing=>$"{crop.Crop} · 生长",
        _=>$"{crop.Crop} · 成熟可收"
    };

    void UpdateFarmGrowthView()
    {
        string layout=_layoutSignature+"|"+Game.ZoneLevel("fields");
        if(_farmGrowthLayout!=layout){
            _farmGrowthLayout=layout;
            foreach(var bed in _farmGrowthBeds)bed.Root.QueueFree();
            _farmGrowthBeds.Clear();
            if(Game.ZoneLevel("fields")<=0){RefreshFarmScenePanel();return;}
            var candidates=OpenDistrictCells("fields");
            var cells=new List<Vector2I>();
            foreach(var cell in candidates){
                if(cells.Any(other=>(cell-other).LengthSquared()<4))continue;
                cells.Add(cell);
                if(cells.Count==NativeGame.FarmPlotCount)break;
            }
            // A dense town may leave only adjoining plots. Each still occupies one grid cell.
            cells.AddRange(candidates.Where(cell=>!cells.Contains(cell)).Take(NativeGame.FarmPlotCount-cells.Count));
            for(int index=0;index<cells.Count;index++){
                var root=new Node3D{Name=$"FarmGrowthPlot{index+1}",Position=WorldData.Cell(cells[index])};AddChild(root);
                var mesh=new MeshInstance3D();root.AddChild(mesh);
                var sign=TownLifeSign("",1.63f);root.AddChild(sign);
                var pick=new StaticBody3D{CollisionLayer=8};pick.SetMeta("kind","farm_growth");pick.SetMeta("ident",(index+1).ToString());
                pick.AddChild(new CollisionShape3D{Shape=new BoxShape3D{Size=new(7.05f,.5f,6.45f)},Position=Vector3.Up*.23f});root.AddChild(pick);
                _farmGrowthBeds.Add((root,mesh,sign,null));
            }
        }
        for(int index=0;index<_farmGrowthBeds.Count;index++){
            var bed=_farmGrowthBeds[index];
            var crop=Game.FarmVisualPlot(index+1);
            if(bed.Crop==crop)continue;
            if(!_farmGrowthMeshes.TryGetValue(crop,out var mesh))_farmGrowthMeshes[crop]=mesh=CreateFarmGrowthMesh(crop);
            bed.Mesh.Mesh=mesh;
            bed.Sign.Text=$"{index+1}号地 · {FarmGrowthCaption(crop)}";
            _farmGrowthBeds[index]=(bed.Root,bed.Mesh,bed.Sign,crop);
        }
        RefreshFarmScenePanel();
    }

    static ArrayMesh CreateFarmGrowthMesh(NativeGame.FarmVisualCrop crop)
    {
        var model=new DistrictAccentMesh();
        model.Box(new(0,.09f,0),new(7,.16f,6.4f),"9c7952");
        for(int row=0;row<5;row++){
            float z=(row-2)*1.15f;
            model.Box(new(0,.18f,z),new(6.65f,.08f,.65f),row%2==0?"78573d":"866146");
            for(int column=0;column<7;column++){
                float x=(column-3)*.88f;
                if(crop.Stage==NativeGame.FarmGrowthStage.Empty)continue;
                if(crop.Stage==NativeGame.FarmGrowthStage.Harvested){
                    model.Box(new(x,.28f,z),new(.105f,.15f,.11f),"cfad70");continue;
                }
                float height=crop.Stage switch{NativeGame.FarmGrowthStage.Seedling=>.24f,NativeGame.FarmGrowthStage.Growing=>.65f,_=>1.08f};
                height*=.88f+((row+column*3)%5)*.065f;
                bool grain=crop.Crop=="谷物",ripe=crop.Stage==NativeGame.FarmGrowthStage.Ripe;
                string leaf=grain&&ripe?"bf9b58":grain?"8ba35f":"6eaa64";
                model.Box(new(x,.23f+height*.5f,z),new(.10f,height,.11f),leaf);
                model.Box(new(x-.105f,.23f+height*.46f,z),new(.26f,.05f,.12f),leaf,.3f);
                if(grain&&ripe)model.Round(new(x,.24f+height,z),new(.19f,.32f,.18f),"e9c576");
                else if(height>.4f)model.Box(new(x+.08f,.24f+height*.72f,z),new(.31f,.055f,.13f),leaf,-.22f);
            }
        }
        return model.Finish()!;
    }
}
