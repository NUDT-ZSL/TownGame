using System;
using Godot;
using System.Linq;
using System.Text.Json.Nodes;

public partial class Main
{
    internal readonly record struct MapResident(string Id,string Name,string Profession,string Status,string Activity,Vector2 Position);
    bool _mapShowsResidents;
    string _mapResidentProfession="",_mapResidentStatus="";

    internal MapResident[] MapResidents()
    {
        var workers=Game.Jobs.OfType<JsonNode>().Select(job=>NativeGame.S(job,"worker")).ToHashSet();
        return ResidentDirectoryEntries().Where(entry=>entry.Position.HasValue).Select(entry=>{
            var point=entry.Position!.Value;
            string status=entry.Place switch{
                ResidentPlace.Home=>"在家",ResidentPlace.Freight=>"货运",
                _ when workers.Contains(entry.Id)||entry.Activity is "照料农田" or "照看牲畜" or "营业 / 采购" or "生产 / 装卸" or "上班"=>"工作",
                _=>"在外"
            };
            return new MapResident(entry.Id,entry.Name,entry.Profession,status,entry.Activity,new Vector2(point.X,point.Z));
        }).ToArray();
    }

    Action ResidentMapFilters(HFlowContainer controls,TownMap map,bool visible,Action save)
    {
        controls.Visible=visible;
        string[] professions=new[]{"全部职业"}.Concat(ResidentDirectoryEntries().Select(entry=>entry.Profession).Distinct().Order()).ToArray();
        string[] states={"全部状态","在家","工作","货运","在外"};
        var job=MarketOptions(controls,professions,System.Math.Max(0,System.Array.IndexOf(professions,_mapResidentProfession)));
        job.Name="TownMapResidentProfession";
        var status=MarketOptions(controls,states,System.Math.Max(0,System.Array.IndexOf(states,_mapResidentStatus)));
        status.Name="TownMapResidentStatus";
        job.ItemSelected+=_=>{Apply();save();};
        status.ItemSelected+=_=>{Apply();save();};
        Apply();
        return ()=>{job.Select(0);status.Select(0);Apply();};

        void Apply(){
            _mapResidentProfession=job.Selected<=0?"":professions[job.Selected];
            _mapResidentStatus=status.Selected<=0?"":states[status.Selected];
            map.SetResidentFilter(_mapResidentProfession,_mapResidentStatus);
        }
    }

    void ResidentMapResults(VBoxContainer results,TownMap map,MapResident[] entries)
    {
        foreach(var child in results.GetChildren()){results.RemoveChild(child);child.QueueFree();}
        results.Visible=entries.Length>0;
        if(entries.Length==0)return;
        var header=new HFlowContainer();results.AddChild(header);
        MarketLabel(header,entries.Length==1?"已选村民":$"附近村民 · {entries.Length} 位",18,TownTheme.Leaf);
        MarketButton(header,"清除选中",map.ClearResidentSelection,expand:false,accent:"地图");
        foreach(var entry in entries){
            var row=new HFlowContainer();row.AddThemeConstantOverride("h_separation",6);results.AddChild(row);
            MarketLabel(row,$"{entry.Name} · {entry.Profession} · {entry.Status} · {entry.Activity}",wrap:true);
            MarketButton(row,"定位",()=>LocateResident(entry.Id),expand:false,accent:"地图");
            MarketButton(row,"跟随",()=>StartResidentFollow(entry.Id),expand:false,accent:"地图");
        }
        Callable.From(()=>{
            if(GodotObject.IsInstanceValid(results)&&results.IsVisibleInTree())_managementScroll?.EnsureControlVisible(results);
        }).CallDeferred();
    }
}
