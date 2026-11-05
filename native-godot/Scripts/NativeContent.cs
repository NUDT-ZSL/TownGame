using Godot;
using System;
using System.IO;
using System.IO.Compression;
using System.Text.Json.Nodes;
using System.Linq;

public static class NativeContent
{
    public static void WriteEngineNotices(string path)
    {
        var text=new System.Text.StringBuilder(Engine.GetLicenseText());
        text.AppendLine("\nThird-party copyrights\n").AppendLine(Godot.Json.Stringify(Engine.GetCopyrightInfo(),"\t"));
        foreach(var license in Engine.GetLicenseInfo())text.AppendLine("\n"+license.Key+"\n").AppendLine(license.Value.ToString());
        File.WriteAllText(path,text.ToString());
    }
    public static JsonObject Load()
    {
        const string packed="res://Content/world.json.gz",editable="res://Content/world.json";
        JsonObject root;
        if(Godot.FileAccess.FileExists(editable))root=JsonNode.Parse(Godot.FileAccess.GetFileAsString(editable))!.AsObject();
        else{
            if(!Godot.FileAccess.FileExists(packed))throw new FileNotFoundException("缺少Content/world.json.gz，请恢复新版自带的资源包。");
            using var bytes=new MemoryStream(Godot.FileAccess.GetFileAsBytes(packed));using var gzip=new GZipStream(bytes,CompressionMode.Decompress);root=JsonNode.Parse(gzip)!.AsObject();
        }
        if(Godot.FileAccess.FileExists("res://Content/rules.json"))root["rules"]=JsonNode.Parse(Godot.FileAccess.GetFileAsString("res://Content/rules.json"));return root;
    }
    public static void Compact(JsonObject root,string path)
    {
        var prototypes=root["prototypes"]!.AsObject();var species=root["rules"]!["SPECIES"]!.AsObject().Select(p=>p.Key+":").ToArray();
        foreach(string key in prototypes.Select(p=>p.Key).Where(k=>k.StartsWith("road:")||species.Any(k.StartsWith)).ToArray())prototypes.Remove(key);
        var objects=root["objects"]!.AsArray();foreach(var node in objects.Where(n=>n?["key"] is JsonArray key&&new[]{"road","building","town_building","decoration"}.Contains(key[0]!.ToString())).ToArray())objects.Remove(node);
        var used=prototypes.Select(p=>p.Value!.GetValue<int>()).Concat(objects.Select(n=>NativeGame.I(n!,"mesh"))).Distinct().Order().ToArray();var remap=used.Select((old,index)=>(old,index)).ToDictionary(p=>p.old,p=>p.index);var oldMeshes=root["meshes"]!.AsArray();
        root["meshes"]=new JsonArray(used.Select(i=>oldMeshes[i]!.DeepClone()).ToArray());foreach(string key in prototypes.Select(p=>p.Key).ToArray())prototypes[key]=remap[prototypes[key]!.GetValue<int>()];foreach(var node in objects)node!["mesh"]=remap[NativeGame.I(node!,"mesh")];
        string rulesPath=Path.Combine(Path.GetDirectoryName(Path.GetFullPath(path))!,"rules.json");if(!File.Exists(rulesPath))File.WriteAllText(rulesPath,root["rules"]!.ToJsonString(new System.Text.Json.JsonSerializerOptions{WriteIndented=true,Encoder=System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping}));
        root["native_assets_version"]=1;using var file=File.Create(path);using var gzip=new GZipStream(file,CompressionLevel.Optimal);using var writer=new System.Text.Json.Utf8JsonWriter(gzip);root.WriteTo(writer);
    }
}
