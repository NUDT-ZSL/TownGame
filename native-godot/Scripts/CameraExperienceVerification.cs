using Godot;
using System;
using System.Linq;

public partial class Main
{
    void VerifyCameraExperience(Action<bool,string> check)
    {
        var view=(_center,_yaw,_pitch,_distance,_buildCamera,_portraitAnchor,_portraitLift,_portraitRadius,
            _cameraTransition,_cameraFrom,_cameraTo,_previousCameraSite,_selection,_selectedPet);
        var inspection=(_sceneInspection,_pinnedSceneInspection);
        var saved=(CameraSite?[])_cameraBookmarks.Clone();
        var names=(string?[])_cameraBookmarkNames.Clone();
        var past=_cameraPast.ToArray();var forward=_cameraForward.ToArray();
        bool loaded=_cameraBookmarksLoaded,hudOpen=_townCameraHudBody.Visible;
        _management?.Hide();
        try{
            _cameraBookmarksLoaded=true;
            _cameraBookmarks[0]=null;_cameraBookmarkNames[0]=null;
            _center=new(96,0,96);_yaw=.5f;_pitch=.75f;_distance=62;_buildCamera=false;_portraitAnchor=null;
            UpdateCamera();
            ShowManagement("环境","camera-bookmarks");
            var name=(LineEdit)_managementBody!.FindChild("CameraBookmarkName0",true,false)!;
            name.Text="林间入口";
            Press("CameraBookmarkSave0");
            check(_cameraBookmarks[0]?.Center==_center&&CameraBookmarkName(0)=="林间入口","命名机位未保存当前场景镜头");
            using(var config=new ConfigFile()){
                WriteCameraBookmarks(config);
                ReadCameraBookmarks(config);
                check(CameraBookmarkName(0)=="林间入口","机位名称无法从槽位配置读回");
                config.EraseSectionKey("camera-1","name");
                ReadCameraBookmarks(config);
                check(_cameraBookmarks[0].HasValue&&CameraBookmarkName(0)=="机位 1","旧机位文件没有保留位置并填入默认名称");
            }
            name.Text="松林小径";
            Press("CameraBookmarkRename0");
            var picker=(MenuButton)_townToolbar!.FindChild("TownCameraPresets",true,false)!;
            picker.EmitSignal(MenuButton.SignalName.AboutToPopup);
            var menu=picker.GetPopup();
            check(menu.GetItemText(menu.GetItemIndex(RestoreCameraMenu)).Contains("松林小径"),"视角菜单未显示新机位名称");
            Press("CameraBookmarkRemove0");
            var dialog=_management!.GetChildren().OfType<ConfirmationDialog>().Last(item=>item.Visible);
            check(_cameraBookmarks[0].HasValue,"清除机位在确认前改动了位置");
            dialog.Hide();dialog.EmitSignal(AcceptDialog.SignalName.Confirmed);
            check(!_cameraBookmarks[0].HasValue,"确认后未清除镜头机位");

            _management.Hide();
            _cameraPast.Clear();_cameraForward.Clear();_previousCameraSite=null;
            var first=_center;var second=first+Vector3.Right*16;var third=second+Vector3.Back*20;
            RememberCameraSite(second);_center=second;
            RememberCameraSite(third);_center=third;
            HandleCameraHistoryInput(new InputEventKey{Keycode=Key.Left,AltPressed=true});
            check(_center==second&&_cameraForward.Count==1,"Alt+左未返回上一步现场");
            NavigateCameraHistory(false);
            check(_center==first&&_previousCameraSite==null,"连续后退没有按到访顺序恢复位置");
            HandleCameraHistoryInput(new InputEventKey{Keycode=Key.Right,AltPressed=true});
            check(_center==second,"Alt+右未恢复下一步现场");
            RememberCameraSite(third);
            check(_cameraForward.Count==0,"新定位后仍保留已失效的前进记录");
            for(int i=0;i<CameraHistoryLimit+2;i++){
                var next=_center+Vector3.Right*8;
                RememberCameraSite(next);_center=next;
            }
            check(_cameraPast.Count+1==CameraHistoryLimit,"场景后退历史未限制记录数");

            RefreshTownCameraHud();
            if(!_townCameraHudBody.Visible)_townCameraHudToggle.EmitSignal(BaseButton.SignalName.Pressed);
            float priorDistance=_distance;
            _townCameraCloser.EmitSignal(BaseButton.SignalName.Pressed);
            check(_distance<priorDistance&&_townCameraHudStatus.Text.Contains("镜头距离"),"镜头按钮未改变实际缩放或显示距离");
            var north=(Button)_townCameraHud.FindChild("TownCameraHudNorth",true,false)!;
            north.EmitSignal(BaseButton.SignalName.Pressed);
            check(_yaw==0&&_townCameraHudStatus.Text.StartsWith("朝北"),"朝北操作未更新实际镜头方向");

            var pet=FarmPets.First(actor=>Data.FindPet(actor.Ident)!=null);
            FocusSceneInspection("pet",pet.Ident,true);
            FocusSceneInspection("person","验收用无场景位置");
            UpdateSceneInspector(0);
            check(_sceneInspector?.Visible==true&&_sceneInspectorLocate?.Disabled==false,"固定对象没有显示定位操作");
            _sceneInspectorLocate!.EmitSignal(BaseButton.SignalName.Pressed);
            check(_selectedPet==pet.Ident&&_center.IsEqualApprox(pet.Position),"固定卡定位误用了后来选中的对象");

            var truck=Agents.First(actor=>actor.IsCar);
            var visual=pet.GetChildren().OfType<MeshInstance3D>().First();
            var targets=new[]{new ScenePick("pet",pet.Ident,visual,default),new ScenePick("vehicle",truck.Ident,truck.Visual,default)};
            ScenePick? accepted=null;
            ShowScenePickChoices(new(130,120),targets,pick=>accepted=pick);
            check(_scenePickHeading!.Text.Contains("2 项")&&_scenePickButtons[1].Text.Contains("车辆"),"重叠轮选缺少数量或类别");
            HandleScenePickChooserInput(new InputEventKey{Keycode=Key.Key2,Pressed=true});
            check(accepted==targets[1]&&_scenePickChooser?.Visible==false,"数字键未选择指定重叠对象");
            GD.Print("CAMERA_EXPERIENCE_VERIFIED named_slots bounded_history camera_hud pinned_locate numeric_pick");
        }finally{
            _management?.Hide();
            CloseScenePickChooser(true);SetScenePickedOutline(null);
            Array.Copy(saved,_cameraBookmarks,saved.Length);Array.Copy(names,_cameraBookmarkNames,names.Length);
            _cameraBookmarksLoaded=loaded;
            _cameraPast.Clear();_cameraPast.AddRange(past);_cameraForward.Clear();_cameraForward.AddRange(forward);
            (_center,_yaw,_pitch,_distance,_buildCamera,_portraitAnchor,_portraitLift,_portraitRadius,
                _cameraTransition,_cameraFrom,_cameraTo,_previousCameraSite,_selection,_selectedPet)=view;
            (_sceneInspection,_pinnedSceneInspection)=inspection;
            if(_townCameraHudBody.Visible!=hudOpen)_townCameraHudToggle.EmitSignal(BaseButton.SignalName.Pressed);
            UpdateCamera();RefreshTownCameraHud();
        }

        void Press(string id)=>((Button)_managementBody!.FindChild(id,true,false)!).EmitSignal(BaseButton.SignalName.Pressed);
    }
}
