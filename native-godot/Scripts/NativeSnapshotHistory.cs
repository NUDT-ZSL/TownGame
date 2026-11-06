using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

internal sealed class NativeSnapshotHistory
{
    internal enum SnapshotKind { Automatic, Manual, BeforeRestore }
    internal sealed record Point(string Id,DateTimeOffset Created,SnapshotKind Kind,int GameDay,int Coins,int Pets,bool Valid);
    sealed record SnapshotFile(string Id,DateTimeOffset Created,SnapshotKind Kind);
    internal const int Limit=8;
    const string StampFormat="yyyyMMddHHmmssfffffff";
    readonly string _savePath;
    internal string DirectoryPath=>_savePath+".history";

    internal NativeSnapshotHistory(string savePath)=>_savePath=Path.GetFullPath(savePath);

    internal int CountFiles()=>Math.Min(Files().Length,Limit);

    internal string Record(DateTimeOffset now,SnapshotKind kind,string? keep=null)
    {
        Directory.CreateDirectory(DirectoryPath);
        string id=$"recovery_{now.UtcDateTime.ToString(StampFormat,CultureInfo.InvariantCulture)}_{(int)kind}_{Guid.NewGuid():N}.json";
        string destination=Path.Combine(DirectoryPath,id);
        string pending=destination+".tmp";
        try{
            File.Copy(_savePath,pending);
            File.Move(pending,destination);
        }finally{TryDelete(pending);}
        var older=Files().Where(file=>file.Id!=id&&file.Id!=keep);
        foreach(var file in older.Skip(keep==null?Limit-1:Limit-2))
            TryDelete(Path.Combine(DirectoryPath,file.Id));
        return id;
    }

    internal Point[] List()=>Files().Take(Limit).Select(file=>{
        try{
            var summary=Summarize(ReadSnapshot(file.Id));
            return new Point(file.Id,file.Created,file.Kind,summary.Day,summary.Coins,summary.Pets,true);
        }catch(Exception error)when(error is IOException or UnauthorizedAccessException or JsonException or InvalidDataException or InvalidOperationException or FormatException){
            return new Point(file.Id,file.Created,file.Kind,0,0,0,false);
        }
    }).ToArray();

    internal JsonObject ReadRecovery(string id,JsonObject assets,DateTimeOffset now)
    {
        var snapshot=ReadSnapshot(id);
        Summarize(snapshot);
        var root=new JsonObject{{"snapshot",snapshot}};
        foreach(string key in new[]{"version","rules","terrain","zones","portals"})
            if(assets[key] is JsonNode asset)root[key]=asset.DeepClone();
        var game=new NativeGame(root);
        game.Validate();
        LegacyImport.ValidateReferences(game);
        game.Meta["native_real_anchor"]=now.ToUnixTimeSeconds();
        return snapshot;
    }

    internal void RestoreValidated(JsonObject snapshot)
    {
        string pending=_savePath+$".restore-{Guid.NewGuid():N}.tmp";
        try{
            using(var stream=new FileStream(pending,FileMode.CreateNew,FileAccess.Write,FileShare.None)){
                using var writer=new Utf8JsonWriter(stream);
                writer.WriteStartObject();
                writer.WriteNumber("version",1);
                writer.WritePropertyName("snapshot");
                snapshot.WriteTo(writer);
                writer.WriteEndObject();
                writer.Flush();
                stream.Flush(flushToDisk:true);
            }
            File.Move(pending,_savePath,overwrite:true);
        }finally{TryDelete(pending);}
    }

    SnapshotFile[] Files()
    {
        if(!Directory.Exists(DirectoryPath))return Array.Empty<SnapshotFile>();
        return Directory.EnumerateFiles(DirectoryPath,"recovery_*.json",SearchOption.TopDirectoryOnly)
            .Select(path=>ParseFileName(Path.GetFileName(path))).OfType<SnapshotFile>()
            .OrderByDescending(file=>file.Created).ThenByDescending(file=>file.Id,StringComparer.Ordinal).ToArray();
    }

    static SnapshotFile? ParseFileName(string id)
    {
        if(id!=Path.GetFileName(id)||!id.EndsWith(".json",StringComparison.Ordinal))return null;
        var parts=id[..^5].Split('_');
        if(parts.Length!=4||parts[0]!="recovery"||!Guid.TryParseExact(parts[3],"N",out _)
            ||!int.TryParse(parts[2],NumberStyles.None,CultureInfo.InvariantCulture,out int kind)
            ||!Enum.IsDefined(typeof(SnapshotKind),kind)
            ||!DateTimeOffset.TryParseExact(parts[1],StampFormat,CultureInfo.InvariantCulture,DateTimeStyles.AssumeUniversal,out var created))
            return null;
        return new SnapshotFile(id,created,(SnapshotKind)kind);
    }

    JsonObject ReadSnapshot(string id)
    {
        if(ParseFileName(id)==null)throw new InvalidDataException("恢复点文件名无效。");
        using var stream=File.OpenRead(Path.Combine(DirectoryPath,id));
        if(JsonNode.Parse(stream) is not JsonObject file||NativeGame.I(file,"version")!=1||file["snapshot"] is not JsonObject snapshot)
            throw new InvalidDataException("恢复点版本或内容无效。");
        file.Remove("snapshot");
        return snapshot;
    }

    static (int Day,int Coins,int Pets) Summarize(JsonObject snapshot)
    {
        if(snapshot["meta"] is not JsonObject meta||snapshot["pets"] is not JsonArray pets)
            throw new InvalidDataException("恢复点缺少时间或伙伴资料。");
        double sim=NativeGame.D(meta,"sim_ts",-1),start=NativeGame.D(meta,"start_ts",-1);
        int coins=NativeGame.I(meta,"coins",-1);
        if(!double.IsFinite(sim)||!double.IsFinite(start)||sim<start||start<0||sim>253402300799||coins<0)
            throw new InvalidDataException("恢复点时间或金币无效。");
        int active=pets.OfType<JsonObject>().Count(pet=>NativeGame.D(pet,"death_ts")==0&&NativeGame.S(pet,"departure")=="");
        return ((int)((sim-start)/NativeGame.Day)+1,coins,active);
    }

    static void TryDelete(string file)
    {
        try{File.Delete(file);}
        catch(IOException){}
        catch(UnauthorizedAccessException){}
    }
}
