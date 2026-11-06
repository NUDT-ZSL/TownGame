using Godot;
using System;
using System.Linq;
using System.Text.Json.Nodes;

public partial class Main
{
    void VerifyMapEconomyOptimizations(Action<bool,string> check)
    {
        bool open=_management?.Visible==true, constructionLayer=_mapShowsConstruction;
        string tab=_tab, entry=_managementEntryTarget, building=_buildTownId, residence=_selectedResidence, pet=_selectedPet;
        int workspace=_buildWorkspace;
        try{
            VerifyConstructionMapClusters(check);
            VerifyAutonomyDiagnosis(check);
        }finally{
            _mapShowsConstruction=constructionLayer;
            _buildTownId=building;
            _buildWorkspace=workspace;
            _selectedResidence=residence;
            _selectedPet=pet;
            if(open)ShowManagement(tab,entry);
            else _management?.Hide();
        }
    }

    void VerifyConstructionMapClusters(Action<bool,string> check)
    {
        const string catalog="bus_stop";
        var spec=Game.Spec("CATALOG",catalog);
        int width=spec["footprint"]![0]!.GetValue<int>(),depth=spec["footprint"]![1]!.GetValue<int>();
        var rows=Game.Rows("town_buildings");
        var existing=rows.OfType<JsonNode>().Where(IsTownProjectUnderway).Select(TownProjectCenter).ToArray();
        Vector2I[] offsets={new(width,0),new(0,depth),new(width+1,0),new(0,depth+1)};
        bool Valid(Vector2I cell)=>existing.All(site=>site.DistanceTo(WorldData.Cell(cell))>80)
            &&Game.PlacementReason(catalog,cell.X,cell.Y,0)=="";
        var (firstCell,secondCell)=(from y in Enumerable.Range(14,50)
            from x in Enumerable.Range(16,91)
            let first=new Vector2I(x,y)
            where Valid(first)
            from offset in offsets
            let second=first+offset
            where Valid(second)
            select (first,second)).FirstOrDefault();
        check(firstCell!=Vector2I.Zero,"地图聚合验证缺少相邻施工用地");
        JsonObject Create(string id,Vector2I cell)=>new(){
            ["building_id"]=id,["catalog_key"]=catalog,["cx"]=cell.X,["cy"]=cell.Y,["w"]=width,["h"]=depth,
            ["orientation"]=0,["status"]="building",["placed_ts"]=Game.Now,["finish_ts"]=Game.Now+NativeGame.Hour
        };
        const string firstId="verify_map_cluster_a",secondId="verify_map_cluster_b";
        var firstProject=Create(firstId,firstCell);
        var secondProject=Create(secondId,secondCell);
        rows.Add(firstProject);
        rows.Add(secondProject);
        try{
            _mapShowsConstruction=true;
            ShowManagement("地图");
            var body=_managementBody!;
            var map=(TownMap)body.FindChild("TownNavigationMap",true,false)!;
            var legend=body.FindChild("TownMapLegend",true,false);
            var symbols=legend?.GetChildren().OfType<Label>().Select(label=>label.Text).ToArray()??Array.Empty<string>();
            check(new[]{"▲ 施工","◆ 断路","■ 货车"}.All(key=>symbols.Any(symbol=>symbol.StartsWith(key,StringComparison.Ordinal))),
                "地图图例没有用不同形状区分施工、断路和货车");
            map.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.TopLeft);
            map.Size=new(620,400);
            map.ResetView();
            var firstCenter=TownProjectCenter(firstProject);
            var secondCenter=TownProjectCenter(secondProject);
            Vector2 first=new(firstCenter.X,firstCenter.Z),second=new(secondCenter.X,secondCenter.Z);
            var overview=map.WorldToView((first+second)*.5f);
            float before=map.WorldToView(first).DistanceTo(map.WorldToView(second));
            var details=(Label)body.FindChild("TownMapDetails",true,false)!;
            map._GuiInput(new InputEventMouseMotion{Position=overview});
            check(details.Text.Contains("2 个施工项目")&&details.Text.Contains("点击放大"),"相邻施工没有在缩小时聚合");
            Click(overview);
            check(_tab=="地图"&&map.WorldToView(first).DistanceTo(map.WorldToView(second))>2*before,
                "点击聚合标记没有在地图内放大并展开项目");
            var selected=map.WorldToView(first);
            map._GuiInput(new InputEventMouseMotion{Position=selected});
            check(details.Text.Contains("施工进度")&&!details.Text.Contains("点击放大"),"放大后缺少单项工程提示");
            Click(selected);
            check(_tab=="建造"&&_managementEntryTarget==firstId,"展开后点击没有打开准确的施工项目");

            void Click(Vector2 point)
            {
                map._GuiInput(new InputEventMouseButton{Position=point,ButtonIndex=MouseButton.Left,Pressed=true});
                map._GuiInput(new InputEventMouseButton{Position=point,ButtonIndex=MouseButton.Left,Pressed=false});
            }
        }finally{
            rows.Remove(firstProject);
            rows.Remove(secondProject);
        }
    }

    void VerifyAutonomyDiagnosis(Action<bool,string> check)
    {
        var original=Data.Snapshot;
        try{
            Data.Root["snapshot"]=original.DeepClone();
            Data.Reindex();
            Game.Meta["sim_ts"]=Math.Floor(Game.Now/NativeGame.Day)*NativeGame.Day+12*NativeGame.Hour;
            Game.Commons["enabled"]=true;
            var state=Game.FreightState;
            Park();
            var residents=Data.People.OfType<JsonNode>().Where(person=>NativeGame.S(person,"role")=="居民").ToArray();
            var housedDriver=residents.FirstOrDefault(person=>Game.ResidentAgeDays(person)>=7&&Game.ResidentHome(person)!=null);
            check(housedDriver!=null&&Game.Effective("market"),"自治诊断验证缺少成人司机或商业区道路");
            foreach(var person in residents)person["job_id"]="verify-freight-busy";
            Game.Jobs.Add(new JsonObject{
                ["id"]="verify-autonomy-crop",["kind"]="crop",["zone"]="fields",
                ["worker"]=NativeGame.S(housedDriver!,"person_id"),["due"]=Game.Now+NativeGame.Hour,
                ["cost"]=new JsonObject{["seed"]=1}
            });
            ShowManagement("自治");
            check(LabelAt("TownFreightBottleneck").Text.Contains("没有空闲成年居民"),"自治诊断没有识别司机全员忙碌");
            check(LabelAt("TownDistrictStatus_market").Text.Contains("道路连通"),"自治诊断没有读取市场实际道路状态");
            check(LabelAt("TownAutonomyJobs").Text.Contains("播种")&&LabelAt("TownAutonomyJobs").Text.Contains("已预留 种子 1"),
                "自治诊断没有显示实际任务及预留的公共资源");

            string source=Game.PlannedFreightSource();
            check(source.Length>0,"自治诊断验证缺少已经连通的生产区");
            housedDriver!["job_id"]="";
            state["driver_id"]=NativeGame.S(housedDriver,"person_id");
            state["source"]=source;
            state["phase"]="loading";
            state["status"]="司机在生产区装货";
            var availability=Game.FreightCargoAvailability(source);
            var stock=Game.Commons["stock"]!;
            stock[availability.Item]=0;
            RefreshManagementPage(1);
            check(LabelAt("TownFreightPhase").Text.Contains("装车")&&LabelAt("TownFreightDriver").Text.Contains(NativeGame.S(housedDriver,"name"))
                &&LabelAt("TownFreightBottleneck").Text.Contains("没有可装载"),"自治诊断未原地更新装车、司机或库存不足");
            stock[availability.Item]=availability.Reserve+3;
            RefreshManagementPage(1);
            check(LabelAt("TownFreightCargo").Text.Contains("本趟可装 3 份")
                &&!LabelAt("TownFreightBottleneck").Text.Contains("没有可装载"),"公共库存补足后没有按当前装车规则更新");
            string home=NativeGame.S(Game.ResidentHome(housedDriver)!,"building_id");
            Press("TownFreightHomeLink");
            check(_tab=="家庭"&&_selectedResidence==home,"司机住所入口没有打开对应住宅");

            Park();
            var broken=(JsonObject)Data.Snapshot.DeepClone();
            var roads=broken["roads"]!.AsArray();
            var portal=Game.Portal("market");
            var gate=roads.OfType<JsonNode>().FirstOrDefault(road=>NativeGame.I(road,"cx")==portal.X&&NativeGame.I(road,"cy")==portal.Y);
            check(gate!=null,"自治诊断验证找不到商业区入口道路");
            roads.Remove(gate);
            Data.Root["snapshot"]=broken;
            Data.Reindex();
            ShowManagement("自治");
            check(LabelAt("TownFreightBottleneck").Text.Contains("道路中断")&&LabelAt("TownDistrictStatus_market").Text.Contains("道路中断"),
                "货运与生产街区未识别实际道路中断");
            Press("TownFreightMapLink");
            check(_tab=="地图"&&_managementEntryTarget=="district:market"&&_managementFocusTarget!=null,
                "货运道路入口没有打开并定位商业区地图");
            ShowManagement("自治");
            Press("TownAutonomyStockLink");
            check(_tab=="自治"&&_managementEntryTarget=="public-stock"&&LabelAt("TownPublicStock").GetParent()==_managementFocusTarget,
                "公共仓库入口没有定位到实时库存");

            void Park()
            {
                state["driver_id"]="";
                state["phase"]="parked";
                state["cargo_count"]=0;
                state["next_trip_ts"]=0;
                state["status"]="货车停靠中";
            }
            Label LabelAt(string name)=>(Label)_managementBody!.FindChild(name,true,false)!;
            void Press(string name)
            {
                var button=_managementBody!.FindChild(name,true,false) as Button;
                check(button is { Visible:true,Disabled:false },$"{name} 入口不可用");
                button!.EmitSignal(BaseButton.SignalName.Pressed);
            }
        }finally{
            Data.Root["snapshot"]=original;
            Data.Reindex();
        }
    }
}
