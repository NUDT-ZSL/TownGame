using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

internal static class NativeTownBundle
{
    internal sealed record Slot(string Save,string Photos,string Camera);
    internal sealed record Preview(DateTimeOffset Created,int Photos,int RecoveryPoints,bool Camera,JsonObject Snapshot);
    const string BundleFormat="slow-town-slot";
    const string SaveEntry="town.json",ManifestEntry="manifest.json",CameraEntry="camera.cfg";
    const long MaximumEntry=128L*1024*1024,MaximumBundle=2L*1024*1024*1024;
    static readonly Regex PhotoName=new(@"\A[\p{L}\p{N}_ .-]+\.png\z",RegexOptions.IgnoreCase|RegexOptions.CultureInvariant);
    static readonly Regex PetAlbum=new(@"\A[0-9a-f]{64}\z",RegexOptions.IgnoreCase|RegexOptions.CultureInvariant);
    static readonly Regex RecoveryName=new(@"\Arecovery_[0-9]{21}_[0-2]_[0-9a-f]{32}\.json\z",RegexOptions.IgnoreCase|RegexOptions.CultureInvariant);

    static bool IsPhoto(string name)=>name.Length<=150&&PhotoName.IsMatch(name)&&!name.StartsWith('.')&&!name.EndsWith(' ');
    static bool IsLink(FileSystemInfo entry)=>(entry.Attributes&FileAttributes.ReparsePoint)!=0;
    static bool Allowed(string entry)
    {
        if(entry is SaveEntry or ManifestEntry or CameraEntry)return true;
        var parts=entry.Split('/');
        return parts[0] switch{
            "history"=>parts.Length==2&&RecoveryName.IsMatch(parts[1]),
            "photos"=>parts.Length==2&&IsPhoto(parts[1])||parts.Length==4&&parts[1]=="pets"&&PetAlbum.IsMatch(parts[2])&&IsPhoto(parts[3]),
            _=>false
        };
    }

    static IEnumerable<(string Entry,string Path)> Photos(string folder)
    {
        if(!Directory.Exists(folder))yield break;
        var root=new DirectoryInfo(folder);
        if(IsLink(root))yield break;
        foreach(var photo in root.EnumerateFiles("*.png").Where(photo=>!IsLink(photo)&&IsPhoto(photo.Name)))
            yield return ("photos/"+photo.Name,photo.FullName);
        var pets=new DirectoryInfo(Path.Combine(folder,"pets"));
        if(!pets.Exists||IsLink(pets))yield break;
        foreach(var album in pets.EnumerateDirectories().Where(album=>!IsLink(album)&&PetAlbum.IsMatch(album.Name)))
            foreach(var photo in album.EnumerateFiles("*.png").Where(photo=>!IsLink(photo)&&IsPhoto(photo.Name)))
                yield return ($"photos/pets/{album.Name}/{photo.Name}",photo.FullName);
    }

    internal static int CountPhotos(string folder)=>Photos(folder).Count();

    internal static void Export(string path,Slot source,JsonObject snapshot,bool includePhotos,IEnumerable<string> points)
    {
        var files=new List<(string Entry,string Path)>();
        if(includePhotos)files.AddRange(Photos(source.Photos));
        if(File.Exists(source.Camera))files.Add((CameraEntry,source.Camera));
        foreach(string id in points.Distinct(StringComparer.Ordinal).Take(NativeSnapshotHistory.Limit)){
            if(!RecoveryName.IsMatch(id))throw new InvalidDataException("恢复点文件名无效。");
            files.Add(("history/"+id,Path.Combine(source.Save+".history",id)));
        }
        var sizes=files.Select(file=>new FileInfo(file.Path).Length).ToArray();
        if(files.Count>4000||sizes.Any(size=>size>MaximumEntry)||sizes.Sum()>MaximumBundle)
            throw new InvalidDataException("备份内容超出大小上限，可关闭照片后重试。");
        string staged=path+"."+Guid.NewGuid().ToString("N")+".tmp";
        try{
            using(var file=new FileStream(staged,FileMode.CreateNew,FileAccess.Write,FileShare.None))
            using(var zip=new ZipArchive(file,ZipArchiveMode.Create)){
                using(var manifest=zip.CreateEntry(ManifestEntry).Open())
                    JsonSerializer.Serialize(manifest,new{format=BundleFormat,version=1,created=DateTimeOffset.UtcNow});
                using(var save=zip.CreateEntry(SaveEntry).Open())
                    JsonSerializer.Serialize(save,new{version=1,snapshot});
                foreach(var (entry,from) in files)zip.CreateEntryFromFile(from,entry,entry.StartsWith("photos/",StringComparison.Ordinal)?CompressionLevel.NoCompression:CompressionLevel.Optimal);
            }
            File.Move(staged,path,true);
        }finally{WorldData.DeleteSaveStage(staged);}
    }

    internal static Preview Inspect(string path)
    {
        using var zip=ZipFile.OpenRead(path);
        return Inspect(zip);
    }

    static Preview Inspect(ZipArchive zip)
    {
        var names=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long size=0;
        if(zip.Entries.Count>4002)throw new InvalidDataException("迁移包文件过多。");
        foreach(var entry in zip.Entries){
            if(!Allowed(entry.FullName)||!names.Add(entry.FullName))throw new InvalidDataException("迁移包包含未知或重复的文件名。");
            if(entry.Length>MaximumEntry||(size+=entry.Length)>MaximumBundle)throw new InvalidDataException("迁移包超出大小上限。");
        }
        if(zip.GetEntry(ManifestEntry) is not {Length:<=4096} manifest||zip.GetEntry(SaveEntry) is not {} save)
            throw new InvalidDataException("迁移包缺少清单或小镇存档。");
        using var manifestStream=ReadEntry(manifest);
        var header=JsonNode.Parse(manifestStream) as JsonObject;
        if(header==null||NativeGame.S(header,"format")!=BundleFormat||NativeGame.I(header,"version")!=1||
            !DateTimeOffset.TryParse(NativeGame.S(header,"created"),out var created))throw new InvalidDataException("迁移包版本无效。");
        using var saveStream=ReadEntry(save);
        if(JsonNode.Parse(saveStream) is not JsonObject document||NativeGame.I(document,"version")!=1||
            document["snapshot"] is not JsonObject snapshot||snapshot["meta"] is not JsonObject||snapshot["pets"] is not JsonArray)
            throw new InvalidDataException("迁移包中的小镇存档无法读取。");
        document.Remove("snapshot");
        int history=names.Count(name=>name.StartsWith("history/",StringComparison.Ordinal));
        if(history>NativeSnapshotHistory.Limit)throw new InvalidDataException("迁移包包含过多恢复点。");
        return new(created,names.Count(name=>name.StartsWith("photos/",StringComparison.Ordinal)),history,names.Contains(CameraEntry),snapshot);
    }

    internal static string? Import(string archive,Slot target,Action<string> validate)
    {
        string parent=Path.GetDirectoryName(Path.GetFullPath(target.Save))!;
        var photoRoot=new DirectoryInfo(target.Photos);
        if(photoRoot.Exists&&IsLink(photoRoot))throw new InvalidDataException("目标照片目录是链接，无法安全导入。");
        string work=Path.Combine(parent,".town-transfer-"+Guid.NewGuid().ToString("N"));
        string backup=target.Save+".before-transfer-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss")+"-"+Guid.NewGuid().ToString("N");
        Directory.CreateDirectory(work);
        var movedOut=new List<(string Original,string Backup)>();
        var installed=new List<(string Original,string Staged)>();
        try{
            using(var zip=ZipFile.OpenRead(archive)){
                Inspect(zip);
                foreach(var entry in zip.Entries.Where(entry=>entry.FullName!=ManifestEntry)){
                    string destination=Path.GetFullPath(Path.Combine(work,entry.FullName));
                    if(!destination.StartsWith(work+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("迁移包路径无效。");
                    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                    using var output=new FileStream(destination,FileMode.CreateNew,FileAccess.Write,FileShare.None);
                    CopyEntry(entry,output);
                }
            }
            validate(work);
            var parts=new List<(string Name,string Destination)>{(SaveEntry,target.Save),(CameraEntry,target.Camera),
                ("history",target.Save+".history"),("photos/pets",Path.Combine(target.Photos,"pets"))};
            var incomingPhotos=Photos(Path.Combine(work,"photos")).Where(item=>!item.Entry.StartsWith("photos/pets/",StringComparison.Ordinal));
            var outgoingPhotos=Photos(target.Photos).Where(item=>!item.Entry.StartsWith("photos/pets/",StringComparison.Ordinal));
            foreach(var item in incomingPhotos.Concat(outgoingPhotos).DistinctBy(item=>item.Entry,StringComparer.OrdinalIgnoreCase))
                parts.Add((item.Entry,Path.Combine(target.Photos,Path.GetFileName(item.Entry))));
            foreach(var (name,destination) in parts){
                if(!Exists(destination))continue;
                string previous=Path.Combine(backup,name);
                Move(destination,previous);
                movedOut.Add((destination,previous));
            }
            foreach(var (name,destination) in parts){
                string staged=Path.Combine(work,name);
                if(!Exists(staged))continue;
                Move(staged,destination);
                installed.Add((destination,staged));
            }
            return movedOut.Count==0?null:backup;
        }catch(Exception error){
            try{
                foreach(var (destination,staged) in installed.AsEnumerable().Reverse())Move(destination,staged);
                foreach(var (destination,previous) in movedOut.AsEnumerable().Reverse())Move(previous,destination);
            }catch(Exception rollback){throw new IOException($"迁移回滚遇到错误，原文件备份位于 {backup}。",new AggregateException(error,rollback));}
            throw;
        }finally{
            if(Directory.Exists(work)&&Path.GetDirectoryName(Path.GetFullPath(work))==parent)
                try{Directory.Delete(work,true);}catch(IOException){}catch(UnauthorizedAccessException){}
        }

        static bool Exists(string path)=>File.Exists(path)||Directory.Exists(path);
        static void Move(string from,string to)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(to))!);
            if(Directory.Exists(from))Directory.Move(from,to);
            else File.Move(from,to);
        }
    }

    static MemoryStream ReadEntry(ZipArchiveEntry entry)
    {
        var buffer=new MemoryStream();
        try{CopyEntry(entry,buffer);buffer.Position=0;return buffer;}
        catch{buffer.Dispose();throw;}
    }

    static void CopyEntry(ZipArchiveEntry entry,Stream output)
    {
        using var input=entry.Open();
        var buffer=new byte[65536];
        long left=entry.Length;
        int length;
        while((length=input.Read(buffer,0,buffer.Length))>0){
            left-=length;
            if(left<0)throw new InvalidDataException("迁移包文件长度校验失败。");
            output.Write(buffer,0,length);
        }
        if(left!=0)throw new InvalidDataException("迁移包文件不完整。");
    }
}
