using Godot;
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;

internal static class TownPhotoAlbumFiles
{
    const string FavoritesFile="favorites.json";
    const string CaptionsFile="captions.json";
    const int MetadataLimit=1024*1024;
    internal const int CaptionLimit=96;
    static readonly byte[] PngSignature={137,80,78,71,13,10,26,10};
    internal enum Orientation { All, Landscape, Square, Portrait }
    internal enum SortOrder { Newest, Oldest, Favorite }

    internal sealed record Entry(string Path,DateTime Taken,Vector2I Pixels)
    {
        public string FileName=>System.IO.Path.GetFileName(Path);
        public Orientation Shape=>Pixels.X>Pixels.Y?Orientation.Landscape:Pixels.X<Pixels.Y?Orientation.Portrait:Orientation.Square;
        public string Description=>$"{Taken:yyyy-MM-dd HH:mm:ss} · {Pixels.X} × {Pixels.Y} 像素";
    }

    internal static FileInfo[] Photos(string folder)
    {
        if(!Directory.Exists(folder))return Array.Empty<FileInfo>();
        var album=new DirectoryInfo(folder);
        if(!SafeAlbum(album))return Array.Empty<FileInfo>();
        return album.GetFiles("*.png").Where(photo=>!IsLink(photo)).ToArray();
    }

    internal static Entry[] ReadEntries(FileInfo[] files)=>files.Select(ReadPhoto).OfType<Entry>()
        .OrderByDescending(photo=>photo.Taken).ThenByDescending(photo=>photo.FileName,StringComparer.Ordinal).ToArray();

    internal static Entry[] SelectEntries(Entry[] photos,Orientation shape,bool favoritesOnly,SortOrder order,string query,
        IReadOnlySet<string> favorites,IReadOnlyDictionary<string,string> captions)
    {
        var words=query.Split((char[]?)null,StringSplitOptions.RemoveEmptyEntries);
        var entries=photos.Where(photo=>(shape==Orientation.All||photo.Shape==shape)&&
            (!favoritesOnly||favorites.Contains(photo.FileName))&&Matches(photo));
        return (order switch{
            SortOrder.Oldest=>entries.OrderBy(photo=>photo.Taken).ThenBy(photo=>photo.FileName,StringComparer.Ordinal),
            SortOrder.Favorite=>entries.OrderByDescending(photo=>favorites.Contains(photo.FileName))
                .ThenByDescending(photo=>photo.Taken).ThenByDescending(photo=>photo.FileName,StringComparer.Ordinal),
            _=>entries.OrderByDescending(photo=>photo.Taken).ThenByDescending(photo=>photo.FileName,StringComparer.Ordinal)
        }).ToArray();

        bool Matches(Entry photo)
        {
            if(words.Length==0)return true;
            captions.TryGetValue(photo.FileName,out string? caption);
            string search=$"{photo.FileName} {photo.Taken.ToString("yyyy-MM-dd HH:mm:ss",CultureInfo.InvariantCulture)} {caption}";
            return words.All(word=>search.Contains(word,StringComparison.OrdinalIgnoreCase));
        }
    }

    static Entry? ReadPhoto(FileInfo file)
    {
        try{
            if(IsLink(file))return null;
            using var stream=file.OpenRead();
            Span<byte> header=stackalloc byte[24];
            stream.ReadExactly(header);
            if(!header[..8].SequenceEqual(PngSignature)||BinaryPrimitives.ReadInt32BigEndian(header[8..12])!=13||
                !header[12..16].SequenceEqual("IHDR"u8))return null;
            int width=BinaryPrimitives.ReadInt32BigEndian(header[16..20]),height=BinaryPrimitives.ReadInt32BigEndian(header[20..24]);
            if(width<=0||height<=0||(long)width*height>67_108_864)return null;
            var name=System.IO.Path.GetFileNameWithoutExtension(file.Name);
            var taken=name.Length>=19&&DateTime.TryParseExact(name.AsSpan(0,19),"yyyyMMdd-HHmmss-fff",CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeLocal,out var date)?date:file.LastWriteTime;
            return new(file.FullName,taken,new(width,height));
        }catch(Exception error)when(error is IOException or UnauthorizedAccessException){return null;}
    }

    internal static HashSet<string> ReadFavorites(string folder)
    {
        string path=System.IO.Path.Combine(folder,FavoritesFile);
        var file=new FileInfo(path);
        if(!file.Exists)return new(StringComparer.OrdinalIgnoreCase);
        if(IsLink(file)||file.Length>MetadataLimit)throw new InvalidDataException("照片收藏文件不可读取。");
        var names=JsonSerializer.Deserialize<string[]>(File.ReadAllText(path))??Array.Empty<string>();
        return names.Where(name=>!string.IsNullOrWhiteSpace(name)&&name==System.IO.Path.GetFileName(name))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    internal static void WriteFavorites(string folder,HashSet<string> favorites)
    {
        WriteMetadata(folder,FavoritesFile,JsonSerializer.SerializeToUtf8Bytes(favorites.OrderBy(name=>name,StringComparer.OrdinalIgnoreCase)));
    }

    internal static Dictionary<string,string> ReadCaptions(string folder)
    {
        var captions=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        var file=new FileInfo(System.IO.Path.Combine(folder,CaptionsFile));
        if(!file.Exists)return captions;
        if(!SafeAlbum(file.Directory!)||IsLink(file)||file.Length>MetadataLimit)
            throw new InvalidDataException("照片说明文件不可读取。");
        var stored=JsonSerializer.Deserialize<Dictionary<string,string?>>(File.ReadAllBytes(file.FullName));
        if(stored==null)return captions;
        foreach(var (name,caption) in stored)
            if(ValidPhotoName(name)&&!string.IsNullOrWhiteSpace(caption))captions[name]=NormalizeCaption(caption);
        return captions;
    }

    internal static string SetCaption(string folder,Entry photo,string text)
    {
        if(!ValidPhotoName(photo.FileName)||!string.Equals(System.IO.Path.GetDirectoryName(photo.Path),
            new DirectoryInfo(folder).FullName,StringComparison.OrdinalIgnoreCase))
            throw new IOException("照片与当前相册不匹配。");
        var file=new FileInfo(photo.Path);
        if(!file.Exists||IsLink(file))throw new IOException("这张照片已无法读取。");
        var captions=ReadCaptions(folder);
        string caption=NormalizeCaption(text);
        if(caption.Length==0)captions.Remove(photo.FileName);
        else captions[photo.FileName]=caption;
        WriteMetadata(folder,CaptionsFile,JsonSerializer.SerializeToUtf8Bytes(captions));
        return caption;
    }

    internal static void ExportOriginal(Entry photo,string target)
    {
        string destination=System.IO.Path.GetFullPath(target);
        if(!destination.EndsWith(".png",StringComparison.OrdinalIgnoreCase))throw new IOException("请选择 PNG 文件名。");
        if(File.Exists(destination)||Directory.Exists(destination))throw new IOException("目标已存在，请换一个文件名。");
        var source=new FileInfo(photo.Path);
        if(!source.Exists||IsLink(source)||!SafeAlbum(source.Directory!))throw new IOException("原图暂时无法读取。");
        string stage=destination+"."+Guid.NewGuid().ToString("N")+".tmp";
        try{
            File.Copy(source.FullName,stage);
            File.Move(stage,destination);
        }finally{WorldData.DeleteSaveStage(stage);}
    }

    static bool ValidPhotoName(string name)=>!string.IsNullOrWhiteSpace(name)&&name==System.IO.Path.GetFileName(name)&&
        name.EndsWith(".png",StringComparison.OrdinalIgnoreCase);

    static string NormalizeCaption(string caption)
    {
        string clean=caption.Replace('\r',' ').Replace('\n',' ').Trim();
        if(clean.Length<=CaptionLimit)return clean;
        int length=char.IsHighSurrogate(clean[CaptionLimit-1])?CaptionLimit-1:CaptionLimit;
        return clean[..length].TrimEnd();
    }

    static void WriteMetadata(string folder,string name,byte[] content)
    {
        if(!SafeAlbum(new DirectoryInfo(folder)))throw new IOException("照片相册位置已变化。");
        if(content.Length>MetadataLimit)throw new IOException("相册资料已达大小上限。");
        string path=System.IO.Path.Combine(folder,name);
        var file=new FileInfo(path);
        if(file.Exists&&IsLink(file))throw new IOException("照片资料位置已变化。");
        string stage=path+"."+Guid.NewGuid().ToString("N")+".tmp";
        try{
            using(var stream=new FileStream(stage,FileMode.CreateNew,System.IO.FileAccess.Write,FileShare.None))stream.Write(content);
            File.Move(stage,path,true);
        }finally{WorldData.DeleteSaveStage(stage);}
    }

    static bool SafeAlbum(DirectoryInfo album)=>album.Exists&&!IsLink(album)&&
        (album.Parent is not { } parent||!IsLink(parent)&&(parent.Parent is not { } root||!IsLink(root)));

    static bool IsLink(FileSystemInfo file)=>(file.Attributes&FileAttributes.ReparsePoint)!=0;
}
