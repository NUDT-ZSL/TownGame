using Godot;
using System.Text.Json.Nodes;

public partial class Main
{
    VBoxContainer _residentSceneTools=null!;
    Label _residentSceneTitle=null!,_residentSceneStatus=null!,_residentSceneRoute=null!;
    Button _residentSceneFollow=null!,_residentSceneInterior=null!;
    string _residentLocatedText="",_residentSceneId="",_residentSceneHomeId="";

    static string ResidentPlaceCaption(ResidentPlace place)=>place switch{
        ResidentPlace.Outdoors=>"在户外",ResidentPlace.Home=>"在家",ResidentPlace.Freight=>"驾驶货车",_=>"暂不可定位"
    };

    static string ResidentLocateCaption(ResidentPlace place)=>place switch{
        ResidentPlace.Home=>"已定位住宅",ResidentPlace.Freight=>"已定位货车",ResidentPlace.Outdoors=>"已定位村民",_=>"无法定位"
    };

    string ResidentCurrentSite(ResidentDirectoryEntry entry)=>entry.Place switch{
        ResidentPlace.Home when entry.Home!=null=>ResidenceName(entry.Home),
        ResidentPlace.Freight when entry.Position.HasValue=>"居民货车 · "+ResidentSiteAt(entry.Position.Value),
        ResidentPlace.Outdoors when entry.Position.HasValue=>ResidentSiteAt(entry.Position.Value),
        _=>"暂无场景位置"
    };

    string ResidentSiteAt(Vector3 point)
    {
        var position=new Vector2(point.X,point.Z);
        var cell=WorldData.Grid(point);
        const float scale=WorldData.GridSize/48;
        foreach(var zone in Data.Root["zones"]!.AsObject()){
            var rect=zone.Value!["rect"]!.AsArray();
            var bounds=new Rect2((float)rect[0]!.GetValue<double>()*scale,(float)rect[1]!.GetValue<double>()*scale,
                (float)rect[2]!.GetValue<double>()*scale,(float)rect[3]!.GetValue<double>()*scale);
            if(bounds.HasPoint(position))return $"{NativeGame.S(zone.Value!,"name")} · {cell.X},{cell.Y}";
        }
        return $"小镇道路 · {cell.X},{cell.Y}";
    }

    string ResidentDirectoryNextStop(ResidentDirectoryEntry entry)=>DirectoryPerson(entry.Id) is JsonNode person
        ?ResidentNextStop(person,DirectoryActor(entry.Id),entry):"暂无可用行程";

    void CreateResidentSceneTools(VBoxContainer parent)
    {
        _residentSceneTools=new VBoxContainer{Name="ResidentSceneTools",Visible=false};
        parent.AddChild(_residentSceneTools);
        _residentSceneTitle=MarketLabel(_residentSceneTools,"",17,TownTheme.Leaf,wrap:true);
        _residentSceneTitle.Name="ResidentSceneTitle";
        _residentSceneStatus=MarketLabel(_residentSceneTools,"",14,TownTheme.Muted,wrap:true);
        _residentSceneRoute=MarketLabel(_residentSceneTools,"",14,TownTheme.Ink,wrap:true);
        _residentSceneRoute.Name="ResidentSceneRoute";
        var actions=new HFlowContainer();
        actions.AddThemeConstantOverride("h_separation",5);
        actions.AddThemeConstantOverride("v_separation",4);
        _residentSceneTools.AddChild(actions);
        _residentSceneFollow=Add("ResidentSceneFollowAction","跟随村民",()=>{
            if(_followResidentId.Length>0){StopResidentFollow();RefreshScenePetCare();}
            else StartResidentFollow(_residentSceneId);
        },"地图");
        Add("ResidentSceneOpenProfile","查看名册",()=>OpenResidentDirectoryEntry(_residentSceneId),"家庭");
        _residentSceneInterior=Add("ResidentSceneOpenInterior","查看室内",()=>{
            string homeId=_residentSceneHomeId;
            if(Game.FindTownBuilding(homeId) is not JsonNode home||!CanShowResidenceInterior(home))return;
            ShowManagement("家庭","interior:"+homeId,()=>_selectedResidence=homeId);
        },"家庭");

        Button Add(string name,string title,System.Action run,string accent)
        {
            var button=MarketButton(actions,title,run,expand:false,accent:accent);
            button.Name=name;
            button.CustomMinimumSize=new(0,30);
            TownTheme.AccentButton(button,accent,5);
            return button;
        }
    }

    bool RefreshResidentSceneTools(bool allowed)
    {
        bool following=_followResidentId.Length>0;
        bool stopped=!following&&_selection==ResidentFollowStopped;
        bool located=_residentLocatedText.Length>0&&_selection==_residentLocatedText;
        string id=following?_followResidentId:stopped?_lastFollowedResidentId:located?_selectedSceneResident:"";
        if(!allowed||id.Length==0||DirectoryPerson(id) is not JsonNode person){
            _residentSceneTools.Hide();
            return false;
        }

        var entry=following?_followResidentEntry:DescribeDirectoryPerson(person,DirectoryActor(id));
        _residentSceneId=id;
        _residentSceneHomeId=entry.Place==ResidentPlace.Home&&entry.Home!=null&&CanShowResidenceInterior(entry.Home)
            ?NativeGame.S(entry.Home,"building_id"):"";
        _residentSceneTitle.Text=$"{(following?"正在跟随":stopped?"跟随已停止":ResidentLocateCaption(entry.Place))} · {entry.Name}";
        _residentSceneStatus.Text=$"{ResidentCurrentSite(entry)} · {entry.Activity}";
        _residentSceneRoute.Text="下一站 · "+(following?_followResidentNextStop:ResidentDirectoryNextStop(entry));
        _residentSceneFollow.Text=following?"停止跟随":stopped?"继续跟随":"跟随村民";
        _residentSceneFollow.Disabled=!following&&!entry.Position.HasValue;
        _residentSceneFollow.TooltipText=following?"也可以按 Esc 或拖动镜头退出":_residentSceneFollow.Disabled?entry.Activity:"让镜头持续跟随该村民";
        _residentSceneInterior.Visible=_residentSceneHomeId.Length>0;
        _residentSceneTools.Show();
        return true;
    }
}
