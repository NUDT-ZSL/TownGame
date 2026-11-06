using Godot;
using System;
using System.IO;
using System.IO.Compression;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

public partial class Main
{
    void VerifyTownContinuity(Action<bool,string> check)
    {
        VerifyFarmRouteBudget(check);
        var temp=Directory.CreateTempSubdirectory("town-continuity-");
        try{
            var fixture=new WorldData(NativeContent.Load());
            var source=new NativeTownBundle.Slot(Path.Combine(temp.FullName,"original.json"),Path.Combine(temp.FullName,"photos"),Path.Combine(temp.FullName,"camera.cfg"));
            var target=new NativeTownBundle.Slot(Path.Combine(temp.FullName,"slot2.json"),Path.Combine(temp.FullName,"photos","slot-2"),Path.Combine(temp.FullName,"slot2-camera.cfg"));
            fixture.Meta["coins"]=4400;
            fixture.Save(source.Save);
            string point=new NativeSnapshotHistory(source.Save).Record(DateTimeOffset.UtcNow,NativeSnapshotHistory.SnapshotKind.Manual);
            Directory.CreateDirectory(target.Photos);
            File.WriteAllBytes(Path.Combine(source.Photos,"cover.png"),new byte[]{137,80,78,71});
            File.WriteAllBytes(Path.Combine(target.Photos,"previous.png"),new byte[]{137,80,78,71});
            using(var config=new ConfigFile()){config.SetValue("view_0","center",new Vector3(1,2,3));check(config.Save(source.Camera)==Error.Ok,"无法准备迁移机位夹具");}
            fixture.Meta["coins"]=5500;
            fixture.Save(target.Save);
            fixture.Meta["coins"]=4400;
            string zip=Path.Combine(temp.FullName,"town.zip");
            NativeTownBundle.Export(zip,source,fixture.Snapshot,true,new[]{point});
            var preview=NativeTownBundle.Inspect(zip);
            check(preview is { Photos:1, RecoveryPoints:1, Camera:true }&&NativeGame.I(preview.Snapshot["meta"]!,"coins")==4400,
                "迁移包预览缺少照片、机位、恢复点或正确进度");
            var backup=NativeTownBundle.Import(zip,target,ValidateTownBundle);
            check(backup!=null&&NativeGame.I(ReadTownSlot(Path.Combine(backup,"town.json"))["meta"]!,"coins")==5500&&
                NativeGame.I(ReadTownSlot(target.Save)["meta"]!,"coins")==4400&&File.Exists(Path.Combine(target.Photos,"cover.png"))&&
                File.Exists(Path.Combine(source.Photos,"cover.png"))&&File.Exists(target.Camera)&&new NativeSnapshotHistory(target.Save).List().Length==1,
                "整槽导入没有正确备份或隔离源、目标槽位");

            string invalid=Path.Combine(temp.FullName,"unsafe.zip");
            using(var archive=ZipFile.Open(invalid,ZipArchiveMode.Create))archive.CreateEntry("../escape.txt");
            bool rejected=false;
            try{NativeTownBundle.Import(invalid,target,ValidateTownBundle);}
            catch(InvalidDataException){rejected=true;}
            check(rejected&&!File.Exists(Path.Combine(temp.FullName,"escape.txt"))&&NativeGame.I(ReadTownSlot(target.Save)["meta"]!,"coins")==4400,
                "非法迁移包影响了目标槽或写出了暂存区");

            VerifyAutosaveOrdering(target.Save,fixture,check);
            GD.Print("TOWN_CONTINUITY_VERIFIED shared_farm_queries route_budget staged_autosave bundle_preview_isolation");
        }finally{
            if(Path.GetDirectoryName(temp.FullName)==Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar))temp.Delete(true);
        }
    }

    void VerifyFarmRouteBudget(Action<bool,string> check)
    {
        using var first=new FarmNavigation(this);
        using var second=new FarmNavigation(this);
        var point=new Vector2I(900,550);
        bool clear=IsFarmCellClear(point,first);
        int count=_farmOccupancy.Count;
        check(IsFarmCellClear(point,second)==clear&&_farmOccupancy.Count==count,"多个庄园角色没有复用地格避障缓存");
        var previous=(_farmRouteTick,_farmRoutesUsed);
        _farmRouteTick=ulong.MaxValue;
        try{
            for(int i=0;i<FarmRoutesPerTick;i++)check(TryScheduleFarmRoutes(),"庄园寻路预算过早耗尽");
            check(!TryScheduleFarmRoutes(),"庄园寻路没有限制单帧重算次数");
        }finally{(_farmRouteTick,_farmRoutesUsed)=previous;}
    }

    void VerifyAutosaveOrdering(string destination,WorldData fixture,Action<bool,string> check)
    {
        long generation=_townSaveGeneration;
        var old=_pendingTownSave;
        try{
            string stage=WorldData.StageSave(destination,fixture.Snapshot);
            fixture.Meta["coins"]=6600;
            fixture.Save(destination);
            _townSaveGeneration++;
            _pendingTownSave=new(Task.FromResult(new PreparedTownSave(stage,1)),destination,generation,Game.Now,Game.WorldRevision,0);
            CompleteTownAutoSave();
            check(!File.Exists(stage)&&NativeGame.I(ReadTownSlot(destination)["meta"]!,"coins")==6600,"旧的自动保存覆盖了更新的手动进度");
        }finally{_townSaveGeneration=generation;_pendingTownSave=old;}
    }
}
