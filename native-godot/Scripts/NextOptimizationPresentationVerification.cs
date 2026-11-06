using Godot;
using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

public partial class Main
{
    void VerifyTownPresentationOptimizations(Action<bool,string> check)
    {
        VerifyTownCameraGestures(check);
        VerifyShadowAndScenePicking(check);
        VerifyHistoricalRecovery(check);
    }

    void VerifyTownCameraGestures(Action<bool,string> check)
    {
        var pose=(_center,_yaw,_pitch,_distance,_buildCamera,_portraitAnchor);
        var transition=(_cameraTransition,_shownCamera,_cameraFrom,_cameraTo);
        bool open=_management?.Visible==true;
        _management?.Hide();
        StopPetFollow();
        try{
            _center=new(WorldData.WorldWidth-145,0,WorldData.WorldDepth-145);
            _yaw=.45f;_pitch=.82f;_distance=60;_portraitAnchor=null;_buildCamera=false;
            UpdateCamera();
            var start=GetViewport().GetVisibleRect().GetCenter();
            var end=start+new Vector2(62,32);
            var initial=_center;
            var anchor=GroundUnderCursor(start);
            Press(MouseButton.Left,start);
            Move(start+new Vector2(2,1),MouseButtonMask.Left);
            check(_center==initial&&!_townCameraDragging,"左键轻微移动被识别为拖动");
            Move(end,MouseButtonMask.Left);
            var held=GroundUnderCursor(end);
            check(_townCameraDragging&&_center.DistanceTo(initial)>1&&anchor.HasValue&&held.HasValue&&anchor.Value.DistanceTo(held.Value)<.1f,
                "拖动平移没有让同一地面位置跟随光标");
            var panned=_center;
            Release(MouseButton.Left,new Vector2(-1,-1));
            check(_center==panned&&_townPointerButton==MouseButton.None&&_management?.Visible!=true,"在场景外释放后仍继续拖动或误打开了管理页");

            _buildCamera=true;
            UpdateCamera();
            Press(MouseButton.Middle,start);
            Move(end,MouseButtonMask.Middle);
            check(_center.DistanceTo(panned)>1&&_camera.Projection==Camera3D.ProjectionType.Orthogonal,
                "等距视角未保留中键平移");
            Move(end,0);
            check(!_townCameraDragging&&_townPointerButton==MouseButton.None,"丢失鼠标按键后拖动仍卡住");

            float yaw=_yaw,pitch=_pitch;
            panned=_center;
            Press(MouseButton.Right,start);
            Move(end,MouseButtonMask.Right);
            Release(MouseButton.Right,end);
            check(_yaw<yaw&&_pitch>pitch&&_center==panned&&!_townCameraDragging&&Input.MouseMode!=Input.MouseModeEnum.Captured,
                "右键没有只旋转镜头或释放后仍捕获光标");

            _cameraFrom=_shownCamera;
            _center-=Vector3.Right*150;
            _cameraTo=DesiredCameraFrame();
            _cameraTransition=CameraTransitionTime;
            UpdateCamera(CameraTransitionTime/4);
            var shown=_shownCamera.Target;
            Press(MouseButton.Middle,start);
            Move(start+Vector2.Right*12,MouseButtonMask.Middle);
            check(_cameraTransition==0&&_center.DistanceTo(shown)<5,"定位动画中拖动时镜头跳到了终点");
            Release(MouseButton.Middle,start+Vector2.Right*12);
        }finally{
            CancelTownPointer();
            (_center,_yaw,_pitch,_distance,_buildCamera,_portraitAnchor)=pose;
            (_cameraTransition,_shownCamera,_cameraFrom,_cameraTo)=transition;
            if(open)ShowManagement(_tab,_managementEntryTarget);
            UpdateCamera();
        }

        void Press(MouseButton button,Vector2 point)=>GetViewport().PushInput(new InputEventMouseButton{Position=point,ButtonIndex=button,Pressed=true},true);
        void Move(Vector2 point,MouseButtonMask mask)=>GetViewport().PushInput(new InputEventMouseMotion{Position=point,ButtonMask=mask},true);
        void Release(MouseButton button,Vector2 point)=>GetViewport().PushInput(new InputEventMouseButton{Position=point,ButtonIndex=button},true);
    }

    void VerifyShadowAndScenePicking(Action<bool,string> check)
    {
        var pose=(_center,_yaw,_pitch,_distance,_buildCamera,_portraitAnchor,_portraitLift,_portraitRadius);
        var transition=(_cameraTransition,_shownCamera,_cameraFrom,_cameraTo);
        string selected=_selectedPet,selection=_selection;
        string page=_tab,entry=_managementEntryTarget;
        bool managementVisible=_management?.Visible==true;
        _management?.Hide();
        StopPetFollow();
        try{
            _buildCamera=false;
            _portraitAnchor=null;
            _center=new Vector3(WorldData.WorldWidth-34,0,WorldData.WorldDepth-30);
            _yaw=.5f;
            _pitch=.7f;
            _distance=8;
            UpdateCamera();
            UpdateTownShadowProfile();
            check(TownShadowDistance<120&&_residentialSun!.DirectionalShadowBlendSplits,"近景没有聚焦方向光阴影分区");
            float near=TownShadowDistance;
            _distance=23;
            UpdateCamera();
            UpdateTownShadowProfile();
            check(TownShadowDistance==near,"镜头在近景分界附近立即切换阴影分区");
            _distance=102;
            UpdateCamera();
            UpdateTownShadowProfile();
            check(TownShadowDistance>near*2,"庄园全景没有恢复远距离阴影覆盖");

            var pets=GetChildren().OfType<PetAgent>().Where(actor=>!actor.IsQueuedForDeletion()).ToArray();
            PetAgent? picked=null;
            foreach(var pet in pets){
                _center=pet.GlobalPosition;
                _distance=6;
                _pitch=1.34f;
                UpdateCamera();
                var point=_camera.UnprojectPosition(pet.GlobalPosition+Vector3.Up*.36f);
                if(PickSceneAt(point) is not ScenePick target||target.Kind!="pet"||target.Id!=pet.Ident||target.Mesh?.Mesh==null)
                    continue;
                picked=pet;
                UpdateSceneHover(point);
                check(_sceneHoverCard.Visible&&_sceneHoverLabel.Text.Contains("选中伙伴")
                    &&target.Mesh.MaterialOverlay==_sceneOutline,"场景没有给真实命中的宠物显示提示和描边");
                ClearSceneHover();
                check(!_sceneHoverCard.Visible&&target.Mesh.MaterialOverlay!=_sceneOutline,"离开对象后没有清除场景提示和描边");
                _selectedPet="";
                _UnhandledInput(new InputEventMouseButton{Position=point,ButtonIndex=MouseButton.Left,Pressed=true});
                check(_selectedPet.Length==0,"鼠标还没松开就触发了场景选择");
                _Input(new InputEventMouseButton{Position=point,ButtonIndex=MouseButton.Left});
                check(_selectedPet==pet.Ident,"短按松开没有保留场景选择");
                break;
            }
            check(picked!=null,"无法用当前场景碰撞拾取宠物");
            _selectedPet=picked!.Ident;
            UpdatePetMarker(true);
            var plate=_sceneNameplates.FirstOrDefault(item=>item.Pet==picked);
            check(plate?.Button.Visible==true&&plate.Button.MouseFilter==Control.MouseFilterEnum.Stop,"选中的伙伴缺少可点击名字牌");
            _selectedPet="";
            plate!.Button.EmitSignal(BaseButton.SignalName.Pressed);
            check(_selectedPet==picked.Ident,"从名字牌选中时没有定位到正确伙伴");
        }finally{
            ClearSceneHover();
            (_center,_yaw,_pitch,_distance,_buildCamera,_portraitAnchor,_portraitLift,_portraitRadius)=pose;
            (_cameraTransition,_shownCamera,_cameraFrom,_cameraTo)=transition;
            _selectedPet=selected;
            _selection=selection;
            if(managementVisible)ShowManagement(page,entry);
            UpdateCamera();
            UpdateTownShadowProfile();
            UpdatePetMarker(true);
            RefreshScenePetCare();
        }
    }

    void VerifyHistoricalRecovery(Action<bool,string> check)
    {
        var folder=Directory.CreateTempSubdirectory("town-recovery-");
        string save=Path.Combine(folder.FullName,"native.json");
        var history=new NativeSnapshotHistory(save);
        try{
            var fixture=NativeContent.Load();
            var game=new NativeGame(fixture);
            var data=new WorldData(fixture);
            var now=DateTimeOffset.UtcNow;
            data.Meta["coins"]=6123;
            data.Save(save);
            string original=history.Record(now,NativeSnapshotHistory.SnapshotKind.Manual);
            var point=history.List().Single();
            check(point.Valid&&point.Id==original&&point.GameDay>=1&&point.Coins==6123
                &&point.Pets==game.Pets.OfType<JsonNode>().Count(game.Active),"恢复点没有正确预览游戏日、金币和伙伴数量");

            data.Meta["coins"]=7345;
            data.Save(save);
            var anchor=now.AddMinutes(2);
            string corrupt=history.Record(now.AddMinutes(1),NativeSnapshotHistory.SnapshotKind.Automatic);
            File.WriteAllText(Path.Combine(history.DirectoryPath,corrupt),"{invalid");
            bool rejected=false;
            try{history.ReadRecovery(corrupt,fixture,anchor);}
            catch(JsonException){rejected=true;}
            check(rejected&&history.List().Any(entry=>entry.Id==corrupt&&!entry.Valid)
                &&NativeGame.I(JsonNode.Parse(File.ReadAllText(save))!["snapshot"]!["meta"]!,"coins")==7345,
                "损坏恢复点影响了当前存档或没有在列表中禁用");

            var target=history.ReadRecovery(original,fixture,anchor);
            string safety=history.Record(anchor,NativeSnapshotHistory.SnapshotKind.BeforeRestore,keep:original);
            history.RestoreValidated(target);
            var restored=JsonNode.Parse(File.ReadAllText(save))!["snapshot"]!["meta"]!;
            check(NativeGame.I(restored,"coins")==6123&&NativeGame.D(restored,"native_real_anchor")==anchor.ToUnixTimeSeconds(),
                "历史进度没有正确恢复或未重置离线时间锚点");
            check(history.List().Any(entry=>entry.Id==safety&&entry.Coins==7345
                &&entry.Kind==NativeSnapshotHistory.SnapshotKind.BeforeRestore),"恢复前没有保留当前进度");
            for(int index=0;index<NativeSnapshotHistory.Limit+2;index++)
                history.Record(anchor.AddMinutes(index+1),NativeSnapshotHistory.SnapshotKind.Automatic);
            check(history.List().Length==NativeSnapshotHistory.Limit&&Directory.GetFiles(history.DirectoryPath).Length==NativeSnapshotHistory.Limit,
                "历史恢复点超过保留上限");
            var available=history.List().First(entry=>entry.Valid);
            const string damaged="{broken-primary-save";
            File.WriteAllText(save,damaged);
            string copy=RecoverTownAtStartup(history,available.Id,fixture,save);
            check(File.ReadAllText(copy)==damaged&&NativeGame.I(ReadTownSlot(save)["meta"]!,"coins")==available.Coins,
                "启动救援未保留损坏原档，或恢复到了错误的进度");
            File.Delete(copy);
        }finally{
            if(Directory.Exists(history.DirectoryPath)){
                foreach(string file in Directory.EnumerateFiles(history.DirectoryPath))File.Delete(file);
                Directory.Delete(history.DirectoryPath);
            }
            File.Delete(save);
            folder.Delete();
        }
    }
}
