using Godot;
using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

public partial class Main
{
    sealed record PreparedTownSave(string Path,double Milliseconds);
    sealed record PendingTownSave(Task<PreparedTownSave> Write,string Destination,long Generation,double Simulation,long Revision,long Anchor);
    PendingTownSave? _pendingTownSave;
    long _townSaveGeneration,_lastWrittenRevision=-1;
    double _lastWrittenSimulation=double.NaN,_lastAutoSaveMs;

    void TrackTownSave(double simulation,long revision)
    {
        _lastWrittenSimulation=simulation;
        _lastWrittenRevision=revision;
        _lastTownSave=DateTimeOffset.Now;
        _saveWriteError="";
        _autosaveTime=0;
    }

    void TickTownAutoSave(double delta)
    {
        CompleteTownAutoSave();
        _autosaveTime+=delta;
        if(_autosaveTime<15||_pendingTownSave!=null)return;
        _autosaveTime=0;
        if(Game.Now==_lastWrittenSimulation&&Game.WorldRevision==_lastWrittenRevision)return;
        string destination=SavePath;
        long anchor=DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var snapshot=Data.Snapshot.DeepClone().AsObject();
        snapshot["meta"]!["native_real_anchor"]=anchor;
        _pendingTownSave=new(Task.Run(()=>PrepareTownAutoSave(destination,snapshot)),destination,
            _townSaveGeneration,Game.Now,Game.WorldRevision,anchor);
    }

    static PreparedTownSave PrepareTownAutoSave(string path,JsonObject snapshot)
    {
        long started=Stopwatch.GetTimestamp();
        var stage=WorldData.StageSave(path,snapshot);
        return new(stage,Stopwatch.GetElapsedTime(started).TotalMilliseconds);
    }

    void CompleteTownAutoSave(bool discard=false)
    {
        if(_pendingTownSave is not { } pending||!discard&&!pending.Write.IsCompleted)return;
        _pendingTownSave=null;
        PreparedTownSave? prepared=null;
        bool current=!discard&&pending.Generation==_townSaveGeneration;
        try{
            prepared=pending.Write.GetAwaiter().GetResult();
            if(!current)return;
            long started=Stopwatch.GetTimestamp();
            File.Move(prepared.Path,pending.Destination,true);
            _lastAutoSaveMs=prepared.Milliseconds+Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            Data.Meta["native_real_anchor"]=pending.Anchor;
            TrackTownSave(pending.Simulation,pending.Revision);
            RecordSnapshotIfDue(false);
        }catch(Exception error){
            if(current){_saveWriteError=error.Message;_selection="自动保存失败 · "+error.Message;}
        }finally{if(prepared!=null)WorldData.DeleteSaveStage(prepared.Path);}
    }
}
