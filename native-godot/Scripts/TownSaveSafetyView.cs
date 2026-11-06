using Godot;
using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

public partial class Main
{
    string _saveWriteError="";
    DateTimeOffset? _lastTownSave;
    ConfirmationDialog? _closeSaveWarning;
    bool _townSlotReloadRequired;

    bool InitializePlayableTown(JsonObject root,bool loadSave)
    {
        var start=root["snapshot"];
        try{
            if(loadSave)root["snapshot"]=ReadTownSlot(SavePath);
            Data=new WorldData(root);
            Game=new NativeGame(root);
            if(loadSave){
                Game.Validate();
                LegacyImport.ValidateReferences(Game);
                _lastWrittenSimulation=Game.Now;
                _lastWrittenRevision=Game.WorldRevision;
                _lastTownSave=new DateTimeOffset(File.GetLastWriteTimeUtc(SavePath));
            }
            return true;
        }catch(Exception error)when(loadSave&&DisplayServer.GetName()!="headless"&&IsSaveLoadError(error)){
            if(!ReferenceEquals(root["snapshot"],start))root["snapshot"]=start;
            ShowStartupRecovery(root,error.Message);
            return false;
        }
    }

    static bool IsSaveLoadError(Exception error)=>error is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or
        InvalidOperationException or ArgumentException or NullReferenceException or FormatException or OverflowException or System.Collections.Generic.KeyNotFoundException;

    void WriteTownProgress()
    {
        if(_townSlotReloadRequired)throw new IOException("小镇已导入，请重新启动后继续。");
        _townSaveGeneration++;
        try{
            Data.Save(SavePath);
            TrackTownSave(Game.Now,Game.WorldRevision);
        }catch(Exception error){_saveWriteError=error.Message;throw;}
    }

    string TownSaveStatus=>_saveWriteError.Length>0?"保存失败 · "+_saveWriteError:
        _lastTownSave is DateTimeOffset time?$"最近成功保存 {time.LocalDateTime:HH:mm:ss}":"尚未写入存档";

    void HandleTownClose()
    {
        if(_townSlotReloadRequired||_smoke||_benchmark||_residenceVerification||_farmVerification||_freightVerification||SaveSnapshot(true)){
            GetTree().Quit();
            return;
        }
        if(GodotObject.IsInstanceValid(_closeSaveWarning)&&_closeSaveWarning!.Visible)return;
        var warning=new ConfirmationDialog{Title="当前进度保存失败",DialogText=TownSaveStatus+"\n\n可以重试或另存副本后退出。",
            OkButtonText="重试保存并退出",CancelButtonText="留在游戏",DialogAutowrap=true,Transient=true,Exclusive=true,Theme=_panel.Theme};
        _closeSaveWarning=warning;
        Node owner=_management?.Visible==true?_management:this;
        owner.AddChild(warning);
        warning.AddButton("另存副本并退出",true,"export");
        warning.Confirmed+=()=>{DisposeWarning();HandleTownClose();};
        warning.Canceled+=DisposeWarning;
        warning.CustomAction+=_=>{
            DisposeWarning();
            ChooseSaveCopy(owner,_panel.Theme,"另存小镇进度","town-progress.json",path=>{
                try{EnsureSaveCopyPath(path);Data.Save(path);GetTree().Quit();}
                catch(Exception error){_selection="另存失败 · "+error.Message;ShowTownSaveNotice(owner,_panel.Theme,_selection);}
            });
        };
        warning.PopupCentered(new(530,195));

        void DisposeWarning(){warning.Hide();warning.QueueFree();_closeSaveWarning=null;}
    }

    void ShowStartupRecovery(JsonObject assets,string issue)
    {
        var host=GetWindow();
        GetTree().AutoAcceptQuit=false;
        void Quit()=>GetTree().Quit();
        host.CloseRequested+=Quit;
        TreeExiting+=()=>{host.CloseRequested-=Quit;FreeStartupResources();};

        var layer=new CanvasLayer();AddChild(layer);
        var backdrop=new ColorRect{Color=new Color("e5e9df")};layer.AddChild(backdrop);
        backdrop.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        var center=new CenterContainer();backdrop.AddChild(center);
        center.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        var theme=TownTheme.Create();
        var card=new PanelContainer{Theme=theme,CustomMinimumSize=new(610,0)};
        card.AddThemeStyleboxOverride("panel",TownTheme.Box("fff9ee",16,18));
        center.AddChild(card);
        var body=new VBoxContainer();body.AddThemeConstantOverride("separation",9);card.AddChild(body);
        MarketLabel(body,"小镇存档救援",25,TownTheme.Leaf);
        MarketLabel(body,ExplicitSavePath==null?$"存档 {_saveSlot} 无法读取。可选一个已通过检查的恢复点继续游玩。":
            "指定存档无法读取。可选一个已通过检查的恢复点继续游玩。",wrap:true);
        var feedback=MarketLabel(body,issue,14,TownTheme.Muted,wrap:true);
        feedback.CustomMinimumSize=new(560,0);
        feedback.TextOverrunBehavior=TextServer.OverrunBehavior.TrimEllipsis;
        feedback.MaxLinesVisible=3;
        var scroller=new ScrollContainer{CustomMinimumSize=new(0,252),HorizontalScrollMode=ScrollContainer.ScrollMode.Disabled};
        body.AddChild(scroller);
        var rows=new VBoxContainer{SizeFlagsHorizontal=Control.SizeFlags.ExpandFill};
        scroller.AddChild(rows);
        var history=new NativeSnapshotHistory(SavePath);
        int recoverable=0;
        try{
            foreach(var point in history.List().Where(point=>point.Valid)){
                try{history.ReadRecovery(point.Id,assets,DateTimeOffset.UtcNow);}
                catch(Exception error)when(IsSaveLoadError(error)){continue;}
                recoverable++;
                var line=MarketCard(rows,color:"e9efe4");
                MarketLabel(line,$"{point.Created.LocalDateTime:yyyy-MM-dd HH:mm} · 第 {point.GameDay} 天 · 金币 {point.Coins} · 伙伴 {point.Pets} 位",wrap:true);
                MarketButton(line,"恢复此进度",()=>Confirm(point),expand:false,accent:"存档");
            }
        }catch(Exception error)when(IsSaveLoadError(error)){feedback.Text="恢复点无法读取 · "+error.Message;}
        if(recoverable==0)MarketLabel(rows,"暂无可用恢复点。你可以导出当前文件副本。",color:TownTheme.Muted,wrap:true);
        var actions=new HBoxContainer();actions.AddThemeConstantOverride("separation",8);body.AddChild(actions);
        MarketButton(actions,"导出当前存档文件",()=>ChooseSaveCopy(this,theme,"导出原存档","town-original.json",path=>{
            try{EnsureSaveCopyPath(path);File.Copy(SavePath,path,true);feedback.Text="原存档文件已导出。";}
            catch(Exception error){feedback.Text="导出失败 · "+error.Message;}
        }),expand:false,accent:"存档");
        MarketButton(actions,"退出游戏",Quit,expand:false,accent:"地图");
        GD.Print($"TOWN_SAVE_RECOVERY_READY points={recoverable}");

        void Confirm(NativeSnapshotHistory.Point point)
        {
            var confirmation=new ConfirmationDialog{Title="恢复小镇进度",DialogText="恢复前会保留当前文件的独立副本。",OkButtonText="恢复并进入",
                CancelButtonText="取消",Transient=true,Exclusive=true,Theme=theme};
            AddChild(confirmation);
            confirmation.Canceled+=confirmation.QueueFree;
            confirmation.Confirmed+=()=>{
                confirmation.QueueFree();
                try{
                    RecoverTownAtStartup(history,point.Id,assets,SavePath);
                    if(GetTree().ReloadCurrentScene()!=Error.Ok)feedback.Text="已恢复存档。重新启动游戏即可进入。";
                }catch(Exception error){feedback.Text="恢复失败 · "+error.Message;}
            };
            confirmation.PopupCentered(new(440,150));
        }
    }

    static string RecoverTownAtStartup(NativeSnapshotHistory history,string id,JsonObject assets,string savePath)
    {
        var valid=history.ReadRecovery(id,assets,DateTimeOffset.UtcNow);
        string backup=savePath+$".unreadable-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.json";
        File.Copy(savePath,backup);
        history.RestoreValidated(valid);
        return backup;
    }

    void EnsureSaveCopyPath(string path)
    {
        if(string.Equals(Path.GetFullPath(path),Path.GetFullPath(SavePath),StringComparison.OrdinalIgnoreCase))
            throw new IOException("请选择与主存档不同的文件名。");
    }

    void ChooseSaveCopy(Node owner,Theme theme,string title,string name,Action<string> selected)
    {
        var dialog=new FileDialog{Title=title,Theme=theme,Transient=true,Exclusive=true,FileMode=FileDialog.FileModeEnum.SaveFile,
            Access=FileDialog.AccessEnum.Filesystem,CurrentFile=name,Filters=new[]{"*.json ; JSON 文件"}};
        owner.AddChild(dialog);
        dialog.Canceled+=dialog.QueueFree;
        dialog.FileSelected+=path=>{dialog.Hide();dialog.QueueFree();selected(path);};
        dialog.PopupCenteredClamped(new(770,510),.93f);
    }

    static void ShowTownSaveNotice(Node owner,Theme theme,string message)
    {
        var dialog=new AcceptDialog{Title="保存小镇",DialogText=message,DialogAutowrap=true,Theme=theme,Transient=true,Exclusive=true};
        owner.AddChild(dialog);
        dialog.Confirmed+=dialog.QueueFree;
        dialog.Canceled+=dialog.QueueFree;
        dialog.PopupCentered(new(495,150));
    }

    void VerifyTownCloseFailure(Action<bool,string> check)
    {
        if(ExplicitSavePath is not string path||!Directory.Exists(path))return;
        bool smoke=_smoke;
        var anchor=Data.Meta["native_real_anchor"]?.DeepClone();
        try{
            _smoke=false;
            HandleTownClose();
            check(_closeSaveWarning is { Visible:true }&&_saveWriteError.Length>0,"写入失败时关闭窗口未提供重试和另存副本");
            _closeSaveWarning!.EmitSignal(ConfirmationDialog.SignalName.Canceled);
            check(_closeSaveWarning==null,"选择留在游戏时未收起保存错误对话框");
            GD.Print("TOWN_SAVE_CLOSE_FAILURE_VERIFIED");
        }finally{
            _smoke=smoke;
            Data.Meta["native_real_anchor"]=anchor;
            _saveWriteError="";
        }
    }
}
