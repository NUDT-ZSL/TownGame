using Godot;
using System;
using System.Linq;
using System.Text.Json.Nodes;

public partial class Main
{
    const string ResidentFollowStopped="已退出村民跟随";
    string _followResidentId="", _followResidentText="", _lastFollowedResidentId="",_followResidentNextStop="";
    double _followResidentRefresh;
    RoadAgent? _followResidentActor;
    ResidentDirectoryEntry _followResidentEntry;

    bool StartResidentFollow(string id)
    {
        if(!LocateResident(id))return false;
        _lastFollowedResidentId=_followResidentId=id;
        _followResidentRefresh=0;
        UpdateResidentFollow(0);
        RefreshScenePetCare();
        return true;
    }

    void StopResidentFollow()
    {
        if(_followResidentId.Length==0)return;
        if(_selection==_followResidentText)_selection=ResidentFollowStopped;
        _followResidentId=_followResidentText=_followResidentNextStop="";
        _followResidentActor=null;
        _followResidentEntry=default;
    }

    void UpdateResidentFollow(double delta)
    {
        if(_followResidentId.Length==0)return;
        _followResidentRefresh-=delta;
        if(_followResidentRefresh<=0){
            _followResidentRefresh=.2;
            var person=DirectoryPerson(_followResidentId);
            if(person==null){StopResidentFollow();return;}
            _followResidentActor=DirectoryActor(_followResidentId);
            _followResidentEntry=DescribeDirectoryPerson(person,_followResidentActor);
            if(!_followResidentEntry.Position.HasValue){
                StopResidentFollow();
                _selection=$"{NativeGame.S(person,"name")} 暂时离开了场景";
                return;
            }
            _followResidentNextStop=ResidentNextStop(person,_followResidentActor,_followResidentEntry);
            _followResidentText=$"跟随 · {_followResidentEntry.Name}\n{_followResidentEntry.Activity} · 下一站 {_followResidentNextStop}";
            _selection=_followResidentText;
        }
        if(_cameraTransition>0)return;
        var point=_followResidentEntry.Position!.Value;
        if(_followResidentEntry.Place==ResidentPlace.Outdoors&&GodotObject.IsInstanceValid(_followResidentActor)&&!_followResidentActor!.IsQueuedForDeletion())
            point=_followResidentActor.Position;
        if(_followResidentEntry.Place==ResidentPlace.Freight&&_freightTruck!=null)point=_freightTruck.Position;
        _center=_center.Lerp(new Vector3(point.X,0,point.Z),1-Mathf.Exp(-8*(float)delta));
    }

    string ResidentNextStop(JsonNode person,RoadAgent? actor,ResidentDirectoryEntry entry)
    {
        if(entry.Place==ResidentPlace.Unavailable)return "暂无可用行程";
        string id=entry.Id;
        if(Game.IsFreightDriver(id)){
            var cargo=Game.FreightState;
            string phase=NativeGame.S(cargo,"phase");
            if(phase=="boarding")return "货车";
            if(phase is "to_source" or "loading")return AutonomyZoneName(NativeGame.S(cargo,"source"));
            if(phase is "to_destination" or "unloading")return AutonomyZoneName(NativeGame.S(cargo,"destination"));
            if(phase=="to_depot")return "庄园货运点";
        }
        if(entry.Place==ResidentPlace.Home&&Game.IsResidentNight)return "住宅门前（07:00 出门）";
        if(entry.Place==ResidentPlace.Home)return "等待出门";
        if(actor?.ReturningHome==true&&entry.Home!=null)return ResidenceName(entry.Home);
        if(actor?.ResidentialActivity.StartsWith("下班前往车站",StringComparison.Ordinal)==true)return "车站";
        if(actor==null||actor.Index>=actor.Path.Count)return "暂无移动计划";
        var target=actor.Path[^1];
        string festival=NativeGame.S(person,"native_festival");
        if(festival is "market" or "showcase")return festival=="market"?"周末集市":"伙伴展示日";
        return ResidentSiteAt(target);
    }
}
