using Godot;
using System;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;

public partial class Main
{
    void VerifyOrderedTownCore(Action<bool,string> check)
    {
        var roads=new JsonArray(new JsonObject{{"cx",0},{"cy",0}},new JsonObject{{"cx",1},{"cy",0}},
            new JsonObject{{"cx",2},{"cy",0}},new JsonObject{{"cx",2},{"cy",1}});
        var fixture=new WorldData(new JsonObject{{"snapshot",new JsonObject{{"roads",roads}}},{"portals",new JsonObject()}});
        var goal=new Vector2I(2,1);
        var route=fixture.Route(Vector2I.Zero,goal,0);
        fixture.Route(Vector2I.Right,goal,WorldData.Sidewalk);
        check(route.Count>=4&&fixture.CachedRoadDestinations==1,"相同道路终点没有复用寻路缓存");
        roads[0]!["road_style"]="stone";
        fixture.Reindex();
        check(fixture.CachedRoadDestinations==1&&fixture.Route(Vector2I.Zero,goal,0).SequenceEqual(route),"换路面错误地清除了道路拓扑缓存");
        roads.RemoveAt(1);
        fixture.Reindex();
        check(fixture.CachedRoadDestinations==0&&fixture.Route(Vector2I.Zero,goal,0).Count==0,"拆路后仍在使用旧路网");

        var pet=Data.Pets.OfType<JsonNode>().First(row=>Game.Active(row)&&FarmPet(NativeGame.S(row,"pet_id"))!=null);
        string petId=NativeGame.S(pet,"pet_id");
        var otherPlot=pet.DeepClone();
        otherPlot["building_id"]=NativeGame.I(pet,"building_id")+1;
        check(PetHomePoint(pet).DistanceTo(PetHomePoint(otherPlot))>20,"不同伙伴居所使用了同一场景入口");
        var actor=FarmPet(petId)!;
        var originalPosition=actor.Position;
        var originalClock=Game.Meta["sim_ts"]!.DeepClone();
        try{
            actor.Position=PetHomePoint(pet);
            using var navigation=new FarmNavigation(this);
            check(navigation.Clear(actor.Position),"伙伴居所入口落在建筑碰撞体内");
            Game.Meta["sim_ts"]=Math.Floor(Game.Now/NativeGame.Day)*NativeGame.Day+23*NativeGame.Hour;
            actor._PhysicsProcess(3);
            actor._PhysicsProcess(.1);
            check(actor.Activity is "在自己的居所休息" or "在诊疗区休息","伙伴夜间未进入实际居所休息状态");
        }finally{
            actor.Position=originalPosition;
            Game.Meta["sim_ts"]=originalClock;
        }

        OpenTownSearch();
        var query=(LineEdit)_townSearchWindow!.FindChild("TownSearchQuery",true,false)!;
        query.Text=petId;
        query.EmitSignal(LineEdit.SignalName.TextChanged,query.Text);
        query.EmitSignal(LineEdit.SignalName.TextSubmitted,query.Text);
        check(_tab=="宠物"&&_selectedPet==petId,"搜索未定位匹配的伙伴");
        FindTownSearchMatches("清水").First(result=>result.Kind=="物品").Open();
        check(_tab=="商店"&&_managementEntryTarget=="supply:清水"&&_marketSearch=="清水","物品搜索未定位商店货架");
        ShowManagement("环境","keybindings");
        check(_managementFocusTarget!=null&&_managementBody!.FindChild("TownInput_search",true,false) is Button,
            "快捷键入口未定位到设置卡片");
        check(MatchTownInput(new InputEventKey{Keycode=Key.F,CtrlPressed=true},"search")&&
            !MatchTownInput(new InputEventKey{Keycode=Key.F},"search")&&!ValidTownKey(new(Key.Escape),false),
            "快捷键修饰键匹配错误或允许覆盖退出键");
        var change=(Button)_managementBody!.FindChild("TownInput_rotate",true,false)!;
        var previous=_townBindings["rotate"];
        try{
            change.EmitSignal(BaseButton.SignalName.Pressed);
            var dialog=_management!.GetChildren().OfType<AcceptDialog>().Last();
            dialog.EmitSignal(Window.SignalName.WindowInput,new InputEventKey{Keycode=Key.M,Pressed=true});
            check(dialog.Visible&&_townBindings["rotate"]==previous,"快捷键重复时没有保留原键");
            dialog.EmitSignal(Window.SignalName.WindowInput,new InputEventKey{Keycode=Key.K,Pressed=true});
            check(!dialog.Visible&&MatchTownInput(new InputEventKey{Keycode=Key.K},"rotate"),"重新绑定没有更新实际按键");
        }finally{_townBindings["rotate"]=previous;}

        string primary=TownSlotPath(1),second=TownSlotPath(2);
        check(Path.GetFileName(primary)=="native-save.json"&&primary!=second&&
            new NativeSnapshotHistory(primary).DirectoryPath!=new NativeSnapshotHistory(second).DirectoryPath,
            "存档槽位和历史恢复点没有按小镇隔离");
        string temporary=Path.Combine(Path.GetTempPath(),$"TownGame-slot-{Guid.NewGuid():N}.json");
        try{
            Data.Save(temporary);
            var loaded=ReadTownSlot(temporary);
            check(JsonNode.DeepEquals(Data.Snapshot,loaded),"槽位读写后内容发生变化");
        }finally{File.Delete(temporary);}
        _management?.Hide();
    }
}
