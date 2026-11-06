using Godot;
using System;
using System.Linq;
using System.Text.Json.Nodes;

public partial class Main
{
    void VerifyTownVisualFinishes(Action<bool,string> check)
    {
        static Vector2[] TextureCoordinates(Mesh mesh)=>mesh.SurfaceGetArrays(0)[(int)Mesh.ArrayType.TexUV].AsVector2Array();
        check(_meshes.Any(mesh=>TextureCoordinates(mesh).Any(uv=>uv.Y==1)&&TextureCoordinates(mesh).Any(uv=>uv.Y==2)),
            "原生地形缺少草地或土路的材质标记");
        check(TextureCoordinates(RoadMesh(5,"stone")).Any(uv=>uv.Y==3),"青石路未标记石板接缝");

        var kits=Game.Rules["DECORATIONS"]!.AsObject().Select(entry=>entry.Key).Where(kind=>DecorationKitGroup(kind)>0).ToArray();
        check(kits.Length==6&&kits.GroupBy(DecorationKitGroup).All(group=>group.Count()==2),"装饰套件分类或商品缺失");
        foreach(string kind in kits){
            var mesh=Prototype("decoration:"+kind);
            var size=mesh.GetAabb().Size;
            check(size is {X:>0 and <4,Y:>.4f,Z:>0 and <4}&&ReferenceEquals(mesh,Prototype("decoration:"+kind)),
                "套件装饰模型缺失、超过单格或没有复用");
        }

        var fixture=new JsonObject();
        foreach(string key in new[]{"version","snapshot","rules","terrain","zones","portals"})fixture[key]=Data.Root[key]!.DeepClone();
        var game=new NativeGame(fixture);
        var building=game.Rows("buildings").OfType<JsonNode>().First(row=>NativeGame.S(row,"kind")=="饲养小屋");
        int buildingId=NativeGame.I(building,"building_id"),coins=NativeGame.I(game.Meta,"coins");
        var original=Prototype($"manor:饲养小屋:{NativeGame.I(building,"level",1)}");
        var args=new JsonObject{{"building_id",buildingId},{"wall",2},{"roof",1}};
        string footprint=BuildingFinish.ObstacleSignature(game.Rows("buildings"));
        game.Execute("paint_building",args);
        var painted=BuildingFinishMesh(original,building);
        check(painted.GetAabb()==original.GetAabb()&&!ReferenceEquals(painted,original)&&NativeGame.I(game.Meta,"coins")==coins&&
            BuildingFinish.ObstacleSignature(game.Rows("buildings"))==footprint,"建筑换色影响占地、费用或未生成新材质");
        var colors=painted.SurfaceGetArrays(0)[(int)Mesh.ArrayType.Color].AsColorArray();
        var originalColors=original.SurfaceGetArrays(0)[(int)Mesh.ArrayType.Color].AsColorArray();
        check(!colors.SequenceEqual(originalColors)&&colors.Where((color,index)=>color==originalColors[index]).Any(),"建筑换色没有改变外墙和屋顶，或覆盖了全部装饰");
        var restored=new NativeGame(JsonNode.Parse(fixture.ToJsonString())!.AsObject());
        var restoredBuilding=restored.Rows("buildings").OfType<JsonNode>().First(row=>NativeGame.I(row,"building_id")==buildingId);
        check(BuildingFinish.Read(restoredBuilding)==(2,1),"建筑配色没有随存档保留");

        var savedSite=CurrentCameraSite();
        var previous=_previousCameraSite;
        var past=_cameraPast.ToArray();
        var forward=_cameraForward.ToArray();
        var bookmarks=(CameraSite?[])_cameraBookmarks.Clone();
        var names=(string?[])_cameraBookmarkNames.Clone();
        bool bookmarksLoaded=_cameraBookmarksLoaded;
        string selection=_selection;
        try{
            _cameraBookmarksLoaded=true;
            _cameraBookmarks[0]=savedSite;
            using var preferences=new ConfigFile();
            WriteCameraBookmarks(preferences);
            using var readback=new ConfigFile();
            check(readback.Parse(preferences.EncodeToText())==Error.Ok,"机位配置文本无法重新读入");
            ReadCameraBookmarks(readback);
            check(_cameraBookmarks[0] is CameraSite site&&site.Center.IsEqualApprox(savedSite.Center)&&
                Mathf.IsEqualApprox(site.Distance,savedSite.Distance)&&site.Build==savedSite.Build,"机位存储没有恢复构图参数");
            RememberCameraSite();
            _center+=Vector3.Right*6;
            HandleCameraBookmarkMenu(ReturnCameraMenu);
            check(_center==savedSite.Center&&_cameraForward[^1].Center==savedSite.Center+Vector3.Right*6,"返回上个现场未保留可前进的机位");
        }finally{
            Array.Copy(bookmarks,_cameraBookmarks,bookmarks.Length);
            Array.Copy(names,_cameraBookmarkNames,names.Length);
            _cameraBookmarksLoaded=bookmarksLoaded;
            _previousCameraSite=previous;
            _cameraPast.Clear();_cameraPast.AddRange(past);
            _cameraForward.Clear();_cameraForward.AddRange(forward);
            _selection=selection;
            _cameraTransition=0;
            UpdateCamera();
        }

        var sounds=Enum.GetValues<TownActionCue>().Select(cue=>{
            using var sound=CreateTownActionSound(cue);
            return Convert.ToBase64String(sound.Data);
        }).ToArray();
        check(sounds.Distinct().Count()==4&&sounds.All(sound=>sound.Length>100),"四类动作没有各自的短提示音");
        check(TownActionCueFor("road_stroke",new JsonObject{{"remove",true}})==null&&
            TownActionCueFor("town_commission",new JsonObject{{"step","deliver"}})==TownActionCue.Delivery,"动作提示音触发条件错误");
        GD.Print("TOWN_FINISHES_VERIFIED kits=6 bookmarks=3 cues=4");
    }
}
