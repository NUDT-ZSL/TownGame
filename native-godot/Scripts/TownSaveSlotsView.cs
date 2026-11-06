using Godot;
using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;

public partial class Main
{
    const int TownSlotCount=3;
    const string TownSlotsPath="user://town-slots.cfg";
    int _saveSlot=1;
    const int TownSlotLabelLimit=18;
    readonly string[] _townSlotLabels=new string[TownSlotCount];
    static string? ExplicitSavePath=>OS.GetCmdlineUserArgs().FirstOrDefault(arg=>arg.StartsWith("--save-path=",StringComparison.Ordinal))?[12..];
    static string TownSlotPath(int slot)=>ProjectSettings.GlobalizePath(slot==1?"user://native-save.json":$"user://native-save-slot-{slot}.json");

    void InitializeTownSaveSlot()
    {
        if(!UseTownPreferences()||ExplicitSavePath!=null)return;
        using var config=new ConfigFile();
        if(config.Load(TownSlotsPath)!=Error.Ok)return;
        _saveSlot=Math.Clamp(config.GetValue("town","slot",1).AsInt32(),1,TownSlotCount);
        for(int slot=1;slot<=TownSlotCount;slot++)_townSlotLabels[slot-1]=ReadTownSlotLabel(config,slot);
    }

    void SaveSlotControls()
    {
        var heading=Section("小镇存档",ExplicitSavePath==null?$"正在游玩 · {TownSlotName(_saveSlot)}":"独立运行存档");
        AddLiveSaveStatus();
        if(ExplicitSavePath!=null)return;
        Text("每个存档拥有自己的小镇、照片和历史恢复点。切换时会先保存当前小镇。");
        for(int slot=1;slot<=TownSlotCount;slot++){
            int target=slot;
            string path=TownSlotPath(target);
            bool active=target==_saveSlot;
            bool empty=!File.Exists(path);
            bool valid=true;
            string summary;
            try{
                if(active)summary="当前进度 · "+DescribeSaveProgress(Data.Snapshot).Progress;
                else if(empty)summary="空存档 · 可以开始一座新小镇";
                else{
                    summary=DescribeSaveProgress(ReadTownSlot(path)).Progress;
                }
            }catch(Exception){summary="存档无法读取";valid=false;}
            var card=MarketCard(_managementBody!,color:active?"e7efe2":"f2ebdf");
            var title=MarketLabel(card,Title(),17,TownTheme.Leaf,wrap:true);
            title.Name=$"TownSlotName{target}";
            var details=MarketLabel(card,summary,14,TownTheme.Muted,wrap:true);
            var files=MarketLabel(card,"",14,TownTheme.Muted,wrap:true);
            files.Name=$"SaveSlotFiles{target}";
            void RefreshFiles()
            {
                try{files.Text=DescribeSlotFiles(path,TownPhotoSlotPath(target));}
                catch(Exception error) when(error is IOException or UnauthorizedAccessException){files.Text="槽位文件信息暂时无法读取。";}
            }
            if(UseTownPreferences())RefreshFiles();
            else files.Visible=false;
            var actions=new HFlowContainer();actions.AddThemeConstantOverride("h_separation",8);card.AddChild(actions);
            var rename=MarketButton(actions,"修改槽位名称",()=>ShowTownSlotLabelEditor(target,()=>{
                title.Text=Title();
                if(active&&heading!=null)heading.Text=$"正在游玩 · {TownSlotName(_saveSlot)}";
            }),expand:false,accent:"存档");
            rename.Name=$"RenameTownSlot{target}";
            rename.Disabled=!UseTownPreferences();
            if(active){
                var shownWrite=_lastTownSave;
                _refreshManagementPage+=()=>{
                    details.Text="当前进度 · "+DescribeSaveProgress(Data.Snapshot).Progress;
                    if(UseTownPreferences()&&shownWrite!=_lastTownSave){shownWrite=_lastTownSave;RefreshFiles();}
                };
                continue;
            }
            var button=MarketButton(actions,empty?"开始新小镇":"载入小镇",()=>ConfirmManagementAction($"切换到{TownSlotName(target)}",
                $"{summary}\n\n切换前会保存{TownSlotName(_saveSlot)}的当前进度。",empty?"开始新小镇":"载入小镇",()=>SwitchTownSaveSlot(target)),
                expand:false,accent:"存档");
            button.Name=$"SaveSlot{target}";
            button.Disabled=!valid||!UseTownPreferences();
            string Title()=>TownSlotName(target)+(active?" · 当前小镇":"");
        }
    }

    string TownSlotName(int slot)=>_townSlotLabels[slot-1] is {Length:>0} label?$"存档 {slot} · {label}":$"存档 {slot}";

    static string NormalizeTownSlotLabel(string value)=>string.Concat(value.EnumerateRunes()
        .Where(rune=>!Rune.IsControl(rune)).SkipWhile(Rune.IsWhiteSpace).Take(TownSlotLabelLimit).Select(rune=>rune.ToString())).Trim();

    static string ReadTownSlotLabel(ConfigFile config,int slot)=>NormalizeTownSlotLabel(config.GetValue("labels",$"slot_{slot}","").AsString());

    void ShowTownSlotLabelEditor(int slot,Action renamed)
    {
        if(!UseTownPreferences()||ExplicitSavePath!=null)return;
        var dialog=new ConfirmationDialog{Name="TownSlotLabelEditor",Title=$"命名存档 {slot}",Borderless=true,
            Transient=true,Exclusive=true,Theme=_management!.Theme,OkButtonText="保存名称",CancelButtonText="取消"};
        _management.AddChild(dialog);
        var form=new VBoxContainer();dialog.AddChild(form);
        MarketLabel(form,"名称只用于本机。留空即可恢复默认名称。",color:TownTheme.Muted,wrap:true);
        var field=new LineEdit{Name="TownSlotLabelInput",Text=_townSlotLabels[slot-1]??"",PlaceholderText="例如 林间庄园",
            ClearButtonEnabled=true,MaxLength=TownSlotLabelLimit};
        form.AddChild(field);
        dialog.RegisterTextEnter(field);
        TownTheme.AccentButton(dialog.GetOkButton(),"存档",6);
        TownTheme.AccentButton(dialog.GetCancelButton(),"地图",6);
        dialog.Canceled+=dialog.QueueFree;
        dialog.Confirmed+=()=>{
            dialog.Hide();dialog.QueueFree();
            try{
                string label=NormalizeTownSlotLabel(field.Text);
                UpdateTownSlotConfig(ProjectSettings.GlobalizePath(TownSlotsPath),config=>config.SetValue("labels",$"slot_{slot}",label));
                _townSlotLabels[slot-1]=label;
                renamed();
                SetManagementFeedback("槽位名称已保存。");
            }catch(Exception error)when(IsSaveLoadError(error)){SetManagementFeedback($"槽位名称保存失败 · {error.Message}",true);}
        };
        dialog.PopupCenteredClamped(new(430,155),.9f);
        field.GrabFocus();field.SelectAll();
    }

    void AddLiveSaveStatus()
    {
        var card=MarketCard(_managementBody!,color:"edf1e6");
        MarketLabel(card,"当前写入状态",17,TownTheme.Leaf);
        var status=MarketLabel(card,"",14,TownTheme.Ink,wrap:true);
        status.Name="TownSaveWriteStatus";
        var detail=MarketLabel(card,"",14,TownTheme.Muted,wrap:true);
        detail.Name="TownSaveWriteDetail";
        var save=MarketButton(card,"立即保存",()=>{
            bool written=SaveSnapshot(announce:true);
            SetManagementFeedback(written?_selection:TownSaveStatus,!written||_snapshotHistoryWarning.Length>0);
            Refresh();
        },expand:false,accent:"存档");
        save.Name="TownSaveWriteNow";
        save.SizeFlagsHorizontal=Control.SizeFlags.ShrinkBegin;
        save.Disabled=!UseTownPreferences()||_townSlotReloadRequired;
        Refresh();
        _refreshManagementPage+=()=>{
            Refresh();
            save.Disabled=!UseTownPreferences()||_townSlotReloadRequired;
        };

        void Refresh()
        {
            bool preparing=_pendingTownSave!=null;
            bool dirty=Game.Now!=_lastWrittenSimulation||Game.WorldRevision!=_lastWrittenRevision;
            status.Text=TownSaveStatus;
            detail.Text=_townSlotReloadRequired?"需重新启动才能继续":preparing?"自动存档正在准备写入":
                dirty?"当前游玩进度待保存":"当前进度已写入";
            if(_lastAutoSaveMs>0)detail.Text+=$" · 最近自动写入 {_lastAutoSaveMs:0.0} 毫秒";
        }
    }

    static string DescribeSlotFiles(string path,string photos)
    {
        var file=new FileInfo(path);
        string primary=file.Exists?$"主档写于 {file.LastWriteTime:yyyy-MM-dd HH:mm} · {file.Length/1024d:0.0} KB":"尚未写入主档";
        int recovery=new NativeSnapshotHistory(path).CountFiles();
        return $"{primary} · 恢复点文件 {recovery} 个 · 照片 {NativeTownBundle.CountPhotos(photos)} 张";
    }

    static JsonObject ReadTownSlot(string path)
    {
        using var stream=File.OpenRead(path);
        if(JsonNode.Parse(stream) is not JsonObject file||NativeGame.I(file,"version")!=1||
            file["snapshot"] is not JsonObject snapshot||snapshot["meta"] is not JsonObject||snapshot["pets"] is not JsonArray)
            throw new InvalidDataException("存档版本或内容无效。");
        file.Remove("snapshot");
        return snapshot;
    }

    void SwitchTownSaveSlot(int target)
    {
        if(!UseTownPreferences()||ExplicitSavePath!=null||target==_saveSlot||target<1||target>TownSlotCount)return;
        try{
            string targetPath=TownSlotPath(target);
            if(File.Exists(targetPath)){
                var snapshot=ReadTownSlot(targetPath);
                var root=new JsonObject{{"snapshot",snapshot}};
                foreach(string key in new[]{"version","rules","terrain","zones","portals"})root[key]=Data.Root[key]?.DeepClone();
                _=new WorldData(root);
                var targetGame=new NativeGame(root);
                targetGame.Validate();
                LegacyImport.ValidateReferences(targetGame);
            }
            Data.Meta["native_real_anchor"]=DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            WriteTownProgress();
            RecordSnapshotIfDue(true);
            if(_snapshotHistoryWarning.Length>0)return;
            WriteTownSaveSlot(target);
            var result=GetTree().ReloadCurrentScene();
            if(result!=Error.Ok){
                WriteTownSaveSlot(_saveSlot);
                throw new IOException($"无法重新载入小镇（{result}）。");
            }
        }catch(Exception error){SetManagementFeedback($"存档切换失败 · {error.Message}",true);}
    }

    static void WriteTownSaveSlot(int slot)=>UpdateTownSlotConfig(ProjectSettings.GlobalizePath(TownSlotsPath),
        config=>config.SetValue("town","slot",slot));

    static void UpdateTownSlotConfig(string destination,Action<ConfigFile> update)
    {
        using var config=new ConfigFile();
        if(File.Exists(destination)&&config.Load(destination)!=Error.Ok)config.Clear();
        update(config);
        string pending=destination+$".{Guid.NewGuid():N}.tmp";
        try{
            if(config.Save(pending)!=Error.Ok)throw new IOException("无法写入存档槽位设置。");
            File.Move(pending,destination,true);
        }finally{WorldData.DeleteSaveStage(pending);}
    }
}
