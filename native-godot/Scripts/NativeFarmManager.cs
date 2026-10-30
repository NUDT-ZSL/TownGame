using System;
using System.Linq;
using System.Text.Json.Nodes;

public sealed partial class NativeGame
{
    // Runtime-only: offline catch-up uses simulation deadlines; a running scene
    // must report physical arrival and finish its work animation before settling.
    bool _managerSceneDriven;
    public bool ManagerSceneDriven {
        get=>_managerSceneDriven;
        set {
            if(value&&!_managerSceneDriven&&CurrentManagerJob is JsonObject job)job["arrived"]=false;
            _managerSceneDriven=value;
        }
    }
    public JsonObject? CurrentManagerJob=>Meta["manager_job"] as JsonObject;

    void InitializeFarmManager()
    {
        Meta["manager_fed"]??=0;
        Meta["manager_deferred"]??=new JsonObject();
        if(CurrentManagerJob is JsonObject old) {
            old["kind"]??="clean";
            old["job_id"]??=NextId("manager");
            old["arrived"]??=false;
            if(old["building_id"]==null)old["building_id"]=I(Rows("waste").FirstOrDefault(w=>S(w!,"waste_id")==S(old,"waste_id"))??new JsonObject(),"building_id",-1);
        }
    }

    JsonNode? ManagerPet(JsonNode job)=>Pets.FirstOrDefault(p=>S(p!,"pet_id")==S(job,"pet_id")&&Active(p!)&&!B(p!,"fostered")&&I(p!,"building_id")==I(job,"building_id"));
    JsonNode? ManagerWaste(JsonNode job)=>Rows("waste").FirstOrDefault(w=>S(w!,"waste_id")==S(job,"waste_id"));
    bool ManagerTargetValid(JsonNode job)=>S(job,"kind")=="feed"
        ?ManagerPet(job) is JsonNode pet&&D(pet,"hunger")<Rule("CARE_FEED_THRESHOLD",70)
        :S(job,"kind")=="clean"&&ManagerWaste(job) is JsonNode waste&&I(waste,"quantity")>0;
    string ManagerTargetKey(JsonNode job)=>S(job,"kind")+":"+S(job,S(job,"kind")=="feed"?"pet_id":"waste_id");
    bool ManagerDeferred(string key)=>D(Meta["manager_deferred"]!,key)>Now;

    void ClearManagerJob(string status,double retry=0)
    {
        Meta["manager_job"]=null;Meta["manager_status"]=status;Meta["manager_next_scan_ts"]=Now+retry;
    }

    public void RefreshManagerTasks()
    {
        if(!B(Meta,"farm_manager"))return;
        while(D(Meta,"manager_next_wage_ts")<=Now) {
            if(I(Meta,"coins")<I(Rules,"MANAGER_WAGE")) {
                Meta["farm_manager"]=false;ClearManagerJob("工资不足，已停工");return;
            }
            Pay(I(Rules,"MANAGER_WAGE"));
            Meta["manager_next_wage_ts"]=Math.Max(1,D(Meta,"manager_next_wage_ts"))+Math.Max(1,Rule("MANAGER_WAGE_INTERVAL",6*Hour));
        }
        if(CurrentManagerJob is JsonObject current) {
            if(!ManagerTargetValid(current))ClearManagerJob("任务目标已变化，重新安排");
            else if(!ManagerSceneDriven&&D(current,"end_ts")<=Now)CompleteManagerTask(S(current,"job_id"));
        }
        if(CurrentManagerJob!=null||!ManagerSceneDriven&&D(Meta,"manager_next_scan_ts")>Now)return;

        var deferred=Meta["manager_deferred"]!.AsObject();
        foreach(string key in deferred.Where(pair=>pair.Value!.GetValue<double>()<=Now).Select(pair=>pair.Key).ToArray())deferred.Remove(key);
        int free=Storage-Rows("inventory").Sum(b=>I(b!,"quantity"));
        var hungry=Pets.Where(p=>Active(p!)&&!B(p!,"fostered")&&D(p!,"hunger")<Rule("CARE_FEED_THRESHOLD",70)).OrderBy(p=>D(p!,"hunger")).ThenBy(p=>S(p!,"pet_id"),StringComparer.Ordinal).ToArray();
        var pet=Stock("普通饲料")>0?hungry.FirstOrDefault(p=>!ManagerDeferred("feed:"+S(p!,"pet_id"))):null;
        var waste=Rows("waste").Where(w=>I(w!,"quantity")>0&&I(w!,"quantity")<=free&&!ManagerDeferred("clean:"+S(w!,"waste_id"))).OrderBy(w=>D(w!,"created_ts")).ThenBy(w=>S(w!,"waste_id"),StringComparer.Ordinal).FirstOrDefault();
        // A critical hunger need takes priority. Otherwise alternate both duties
        // so a dirty enclosure cannot indefinitely postpone feeding, or vice versa.
        bool feed=pet!=null&&(waste==null||D(pet,"hunger")<35||S(Meta,"manager_last_kind")=="clean");
        JsonNode? target=feed?pet:waste;
        if(target==null) {
            string status=hungry.Length>0&&Stock("普通饲料")==0?"普通饲料不足，等待补充":Rows("waste").Any(w=>I(w!,"quantity")>free)?"仓库已满，等待腾空":deferred.Count>0?"任务暂时无法到达，稍后重试":"动物已吃饱，环境整洁，休息待命";
            ClearManagerJob(status,Rule("MANAGER_SCAN_INTERVAL",Hour));return;
        }
        string kind=feed?"feed":"clean";
        Meta["manager_job"]=new JsonObject {
            {"job_id",NextId("manager")},{"kind",kind},{feed?"pet_id":"waste_id",S(target,feed?"pet_id":"waste_id")},
            {"building_id",I(target,"building_id")},{"start_ts",Now},
            {"end_ts",Now+Math.Max(1,Rule("MANAGER_TRAVEL_SECONDS",6))+Math.Max(1,Rule("MANAGER_SWEEP_SECONDS",8))},{"arrived",false}
        };
        Meta["manager_status"]=feed?"前往喂食 · "+S(pet!,"name"):"前往清扫动物粪便";
    }

    public bool ArriveManagerTask(string jobId)
    {
        if(!B(Meta,"farm_manager")||CurrentManagerJob is not JsonObject job||S(job,"job_id")!=jobId)return false;
        if(!ManagerTargetValid(job)){ClearManagerJob("任务目标已变化，重新安排");return false;}
        job["arrived"]=true;job["arrived_ts"]=Now;
        Meta["manager_status"]=S(job,"kind")=="feed"?"正在喂食 · "+S(ManagerPet(job)!,"name"):"正在清扫动物粪便";
        return true;
    }

    public bool CompleteManagerTask(string jobId)
    {
        if(!B(Meta,"farm_manager")||CurrentManagerJob is not JsonObject job||S(job,"job_id")!=jobId)return false;
        if(ManagerSceneDriven?!B(job,"arrived"):D(job,"end_ts")>Now)return false;
        if(!ManagerTargetValid(job)){ClearManagerJob("任务目标已变化，重新安排");return false;}
        string kind=S(job,"kind");
        if(kind=="feed"&&Stock("普通饲料")==0){ClearManagerJob("普通饲料不足，等待补充",Rule("MANAGER_RETRY_INTERVAL",Hour/2));return false;}
        if(kind=="clean"&&Rows("inventory").Sum(b=>I(b!,"quantity"))+I(ManagerWaste(job)!,"quantity")>Storage){ClearManagerJob("仓库已满，等待腾空",Rule("MANAGER_RETRY_INTERVAL",Hour/2));return false;}
        var before=State.DeepClone();long revision=WorldRevision;
        try {
            if(kind=="clean")CleanWaste(new JsonObject{{"waste_id",S(job,"waste_id")}},manager:true);
            else {
                var pet=ManagerPet(job)!;
                Take("普通饲料",1);
                foreach(var effect in Spec("ITEMS","普通饲料")["feed"]!.AsObject())Gain(pet,effect.Key,effect.Value!.GetValue<double>());
                Meta["manager_fed"]=I(Meta,"manager_fed")+1;Meta["manager_last_feed_ts"]=Now;
                Log("农场管理员给"+S(pet,"name")+"喂了普通饲料");
            }
            Meta["manager_last_kind"]=kind;ClearManagerJob(kind=="clean"?"清扫完成，检查下一项工作":"喂食完成，检查下一项工作");Validate();return true;
        }catch {root["snapshot"]=before;WorldRevision=revision;throw;}
    }

    public void RetryManagerTask(string jobId,string reason)
    {
        if(CurrentManagerJob is not JsonObject job||S(job,"job_id")!=jobId)return;
        Meta["manager_deferred"]![ManagerTargetKey(job)]=Now+Math.Max(30,Math.Min(120,Rule("MANAGER_RETRY_INTERVAL",60)));
        ClearManagerJob(reason); // Other reachable work can be taken immediately.
    }
}
