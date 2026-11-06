using Godot;
using System;
using System.Collections.Generic;

public partial class Main
{
    string CameraBookmarkPath=>TownCameraSlotPath(_saveSlot);
    static string TownCameraSlotPath(int slot)=>slot==1?"user://town-camera.cfg":$"user://town-camera-slot-{slot}.cfg";
    const int ReturnCameraMenu=100,ForwardCameraMenu=101,CameraBookmarkManagerMenu=102,RestoreCameraMenu=110,SaveCameraMenu=120;
    const int CameraHistoryLimit=8, CameraBookmarkNameLimit=20;
    readonly record struct CameraSite(Vector3 Center,float Yaw,float Pitch,float Distance,bool Build,float Lift,float Radius);
    readonly CameraSite?[] _cameraBookmarks=new CameraSite?[3];
    readonly string?[] _cameraBookmarkNames=new string?[3];
    readonly List<CameraSite> _cameraPast=new(),_cameraForward=new();
    CameraSite? _previousCameraSite;
    bool _cameraBookmarksLoaded;

    CameraSite CurrentCameraSite()=>new(_center,_yaw,_pitch,_distance,_buildCamera,
        InPetCamera?_portraitLift:0,InPetCamera?_portraitRadius:0);

    void RememberCameraSite(Vector3? destination=null)
    {
        if(!_cameraFrameReady||destination is Vector3 target&&target.IsEqualApprox(_center))return;
        PushCameraBack(CurrentCameraSite());
        _cameraForward.Clear();
    }

    void PushCameraBack(CameraSite site)
    {
        if(_previousCameraSite==site)return;
        if(_previousCameraSite is CameraSite previous)PushCameraHistory(_cameraPast,previous,CameraHistoryLimit-1);
        _previousCameraSite=site;
    }

    static void PushCameraHistory(List<CameraSite> history,CameraSite site,int limit)
    {
        if(history.Count>0&&history[^1]==site)return;
        if(history.Count>=limit)history.RemoveAt(0);
        history.Add(site);
    }

    static CameraSite? PopCameraHistory(List<CameraSite> history)
    {
        if(history.Count==0)return null;
        var site=history[^1];
        history.RemoveAt(history.Count-1);
        return site;
    }

    void NavigateCameraHistory(bool forward)
    {
        var current=CurrentCameraSite();
        if(forward){
            if(PopCameraHistory(_cameraForward) is not CameraSite next)return;
            PushCameraBack(current);
            RestoreCameraSite(next,"已前往下一个现场");
        }else{
            if(_previousCameraSite is not CameraSite previous)return;
            PushCameraHistory(_cameraForward,current,CameraHistoryLimit);
            _previousCameraSite=PopCameraHistory(_cameraPast);
            RestoreCameraSite(previous,"已返回上个现场");
        }
    }

    bool HandleCameraHistoryInput(InputEventKey key)
    {
        if(!key.AltPressed||key.CtrlPressed||key.ShiftPressed||key.MetaPressed||
            key.Keycode is not (Key.Left or Key.Right)||_management?.Visible==true||_townSearchWindow?.Visible==true||
            _placementAction.Length>0||_scenePickChooser?.Visible==true)return false;
        NavigateCameraHistory(key.Keycode==Key.Right);
        return true;
    }

    void RestoreCameraSite(CameraSite site,string notice)
    {
        var start=_shownCamera;
        StopPetFollow();
        StopResidentFollow();
        EndPlacement();
        _center=site.Center;
        _yaw=site.Yaw;_pitch=site.Pitch;_distance=site.Distance;
        _buildCamera=site.Build;
        _portraitAnchor=site.Radius>0?site.Center:null;
        _portraitLift=site.Lift;_portraitRadius=site.Radius;
        _cameraFrom=start;
        _cameraTo=DesiredCameraFrame();
        _cameraTransition=CameraTransitionTime;
        _management?.Hide();
        _selection=notice;
    }

    void RefreshCameraBookmarkMenu(PopupMenu menu)
    {
        EnsureCameraBookmarksLoaded();
        while(menu.ItemCount>3)menu.RemoveItem(menu.ItemCount-1);
        menu.AddSeparator("现场历史");
        menu.AddItem("返回上个现场（Alt+←）",ReturnCameraMenu);
        menu.SetItemDisabled(menu.ItemCount-1,_previousCameraSite==null);
        menu.AddItem("前往下个现场（Alt+→）",ForwardCameraMenu);
        menu.SetItemDisabled(menu.ItemCount-1,_cameraForward.Count==0);
        menu.AddSeparator("机位");
        menu.AddItem("管理机位",CameraBookmarkManagerMenu);
        for(int slot=0;slot<_cameraBookmarks.Length;slot++){
            if(_cameraBookmarks[slot]==null)menu.AddItem($"保存到机位 {slot+1}",SaveCameraMenu+slot);
            else menu.AddItem($"前往 · {CameraBookmarkName(slot)}",RestoreCameraMenu+slot);
        }
    }

    void HandleCameraBookmarkMenu(long id)
    {
        EnsureCameraBookmarksLoaded();
        if(id==ReturnCameraMenu)NavigateCameraHistory(false);
        else if(id==ForwardCameraMenu)NavigateCameraHistory(true);
        else if(id==CameraBookmarkManagerMenu)ShowManagement("环境","camera-bookmarks");
        else if(id>=RestoreCameraMenu&&id<RestoreCameraMenu+_cameraBookmarks.Length)VisitCameraBookmark((int)id-RestoreCameraMenu);
        else if(id>=SaveCameraMenu&&id<SaveCameraMenu+_cameraBookmarks.Length){
            int slot=(int)id-SaveCameraMenu;
            if(_cameraBookmarks[slot]==null)StoreCameraBookmark(slot,CurrentCameraSite(),null);
        }
    }

    string CameraBookmarkName(int slot)=>_cameraBookmarkNames[slot]??$"机位 {slot+1}";

    void VisitCameraBookmark(int slot)
    {
        if(_cameraBookmarks[slot] is not CameraSite site)return;
        if(site!=CurrentCameraSite())RememberCameraSite();
        RestoreCameraSite(site,$"已前往「{CameraBookmarkName(slot)}」");
    }

    void StoreCameraBookmark(int slot,CameraSite? site,string? name)
    {
        string previous=CameraBookmarkName(slot);
        _cameraBookmarks[slot]=site;
        _cameraBookmarkNames[slot]=site.HasValue?CleanCameraBookmarkName(name??""):null;
        string message=site.HasValue?$"已保存「{CameraBookmarkName(slot)}」":$"已清除「{previous}」";
        bool failed=false;
        if(UseTownPreferences()){
            using var config=new ConfigFile();
            WriteCameraBookmarks(config);
            failed=config.Save(CameraBookmarkPath)!=Error.Ok;
            if(failed)message="机位保留在本次游戏中，写入设置失败。";
        }
        if(_management?.Visible==true)SetManagementFeedback(message,failed,completed:!failed);
        else _selection=message;
    }

    static string? CleanCameraBookmarkName(string value)
    {
        string name=value.Replace('\r',' ').Replace('\n',' ').Trim();
        if(name.Length>CameraBookmarkNameLimit)name=name[..CameraBookmarkNameLimit];
        if(name.Length>0&&char.IsHighSurrogate(name[^1]))name=name[..^1];
        return string.IsNullOrWhiteSpace(name)?null:name.TrimEnd();
    }

    void CameraBookmarkControls()
    {
        EnsureCameraBookmarksLoaded();
        var card=MarketCard(_managementBody!,color:"e8eef0");
        MarketLabel(card,"镜头机位",19,TownTheme.Leaf);
        MarketLabel(card,"每个小镇保存三个机位。保存前先在场景中调好视角，名称会显示在视角菜单中。",color:TownTheme.Muted,wrap:true);
        for(int slot=0;slot<_cameraBookmarks.Length;slot++)AddSlot(slot);
        if(_managementEntryTarget=="camera-bookmarks")_managementFocusTarget=card.GetParent<Control>();

        void AddSlot(int slot)
        {
            var row=MarketCard(card,color:"f6f5ed");
            var title=MarketLabel(row,"",16,TownTheme.Ink);
            var detail=MarketLabel(row,"",14,TownTheme.Muted,wrap:true);
            var actions=new HFlowContainer();actions.AddThemeConstantOverride("h_separation",6);row.AddChild(actions);
            var name=new LineEdit{Name=$"CameraBookmarkName{slot}",MaxLength=CameraBookmarkNameLimit,PlaceholderText=$"机位 {slot+1}",
                CustomMinimumSize=new(150,34),SizeFlagsHorizontal=Control.SizeFlags.ExpandFill,AccessibilityName=$"机位 {slot+1} 名称"};
            actions.AddChild(name);
            var visit=AddButton("前往",$"CameraBookmarkVisit{slot}");
            var rename=AddButton("改名",$"CameraBookmarkRename{slot}");
            var save=AddButton("保存当前镜头",$"CameraBookmarkSave{slot}");
            var remove=AddButton("清除",$"CameraBookmarkRemove{slot}");
            visit.Pressed+=()=>VisitCameraBookmark(slot);
            rename.Pressed+=()=>{if(_cameraBookmarks[slot] is CameraSite site)Apply(site,name.Text);};
            name.TextSubmitted+=_=>{if(_cameraBookmarks[slot] is CameraSite site)Apply(site,name.Text);};
            save.Pressed+=()=>{
                var site=CurrentCameraSite();
                string chosen=name.Text;
                if(!_cameraBookmarks[slot].HasValue){Apply(site,chosen);return;}
                ConfirmManagementAction("更新镜头机位",$"用当前视角替换「{CameraBookmarkName(slot)}」保存的视角。","更新机位",()=>Apply(site,chosen));
            };
            remove.Pressed+=()=>{
                if(!_cameraBookmarks[slot].HasValue)return;
                ConfirmManagementAction("清除镜头机位",$"清除「{CameraBookmarkName(slot)}」保存的位置和名称。","清除机位",()=>Apply(null,null));
            };
            Refresh();

            Button AddButton(string caption,string id)
            {
                var button=new Button{Name=id,Text=caption,CustomMinimumSize=new(0,34)};
                TownTheme.AccentButton(button,id.StartsWith("CameraBookmarkRemove",StringComparison.Ordinal)?"事件":"地图",6);
                actions.AddChild(button);
                return button;
            }

            void Apply(CameraSite? site,string? caption){StoreCameraBookmark(slot,site,caption);Refresh();}

            void Refresh()
            {
                bool stored=_cameraBookmarks[slot] is CameraSite;
                title.Text=$"{slot+1:00} · {CameraBookmarkName(slot)}";
                name.Text=_cameraBookmarkNames[slot]??"";
                visit.Disabled=rename.Disabled=remove.Disabled=!stored;
                save.Text=stored?"用当前镜头更新":"保存当前镜头";
                detail.Text=stored&&_cameraBookmarks[slot] is CameraSite site?CameraBookmarkDetail(site):"空机位";
            }
        }
    }

    static string CameraBookmarkDetail(CameraSite site)
    {
        var cell=WorldData.Grid(site.Center);
        string mode=site.Build?"等距":site.Radius>0?"伙伴特写":"自由视角";
        return $"{mode} · 地图格 {cell.X}, {cell.Y} · 镜头距离 {site.Distance:0.#}";
    }

    void EnsureCameraBookmarksLoaded()
    {
        if(_cameraBookmarksLoaded)return;
        _cameraBookmarksLoaded=true;
        if(!UseTownPreferences())return;
        using var config=new ConfigFile();
        if(config.Load(CameraBookmarkPath)==Error.Ok)ReadCameraBookmarks(config);
    }

    void WriteCameraBookmarks(ConfigFile config)
    {
        for(int slot=0;slot<_cameraBookmarks.Length;slot++){
            if(_cameraBookmarks[slot] is not CameraSite site)continue;
            string section=$"camera-{slot+1}";
            config.SetValue(section,"center",site.Center);
            config.SetValue(section,"frame",new Vector3(site.Yaw,site.Pitch,site.Distance));
            config.SetValue(section,"build",site.Build);
            config.SetValue(section,"portrait",new Vector2(site.Lift,site.Radius));
            if(_cameraBookmarkNames[slot] is string name)config.SetValue(section,"name",name);
        }
    }

    void ReadCameraBookmarks(ConfigFile config)
    {
        for(int slot=0;slot<_cameraBookmarks.Length;slot++){
            string section=$"camera-{slot+1}";
            _cameraBookmarks[slot]=null;
            _cameraBookmarkNames[slot]=null;
            if(!config.HasSectionKey(section,"center")||!config.HasSectionKey(section,"frame"))continue;
            var centerValue=config.GetValue(section,"center");
            var frameValue=config.GetValue(section,"frame");
            if(centerValue.VariantType!=Variant.Type.Vector3||frameValue.VariantType!=Variant.Type.Vector3)continue;
            var center=centerValue.AsVector3();var frame=frameValue.AsVector3();
            var portraitValue=config.GetValue(section,"portrait",Vector2.Zero);
            var portrait=portraitValue.VariantType==Variant.Type.Vector2?portraitValue.AsVector2():Vector2.Zero;
            if(!center.IsFinite()||!frame.IsFinite()||!portrait.IsFinite()||frame.Y is <.24f or >1.41f||frame.Z is <3f or >1000f)continue;
            center.X=Math.Clamp(center.X,0,WorldData.WorldWidth);
            center.Z=Math.Clamp(center.Z,0,WorldData.WorldDepth);
            center.Y=Math.Clamp(center.Y,-15,45);
            _cameraBookmarks[slot]=new(center,frame.X,frame.Y,frame.Z,config.GetValue(section,"build",false).AsBool(),
                Math.Clamp(portrait.X,0,25),Math.Clamp(portrait.Y,0,25));
            _cameraBookmarkNames[slot]=CleanCameraBookmarkName(config.GetValue(section,"name","").AsString());
        }
    }
}
