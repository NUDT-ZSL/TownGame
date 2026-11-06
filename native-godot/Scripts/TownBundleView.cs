using Godot;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;

public partial class Main
{
    static NativeTownBundle.Slot BundleSlot(int slot)=>new(TownSlotPath(slot),TownPhotoSlotPath(slot),ProjectSettings.GlobalizePath(TownCameraSlotPath(slot)));
    readonly record struct BundleDestination(int Slot,string Caption,bool Active,bool Occupied,string Progress,string Files)
    {
        public string State=>Active?"当前小镇":Occupied?"已有内容":"空槽";
        public string Effect=>Active?"导入前保存并备份当前进度，导入后重新载入小镇。":
            Occupied?"导入前备份目标槽已有文件，导入后可从存档页载入。":"导入后可从存档页载入这个小镇。";
    }

    static BundleDestination DescribeBundleDestination(int slot,string caption,NativeTownBundle.Slot files,JsonObject? current=null)
    {
        bool active=current!=null;
        try{
            bool hasSave=File.Exists(files.Save),hasCamera=File.Exists(files.Camera);
            var history=new NativeSnapshotHistory(files.Save);
            int photos=NativeTownBundle.CountPhotos(files.Photos),points=history.CountFiles();
            bool occupied=active||hasSave||Directory.Exists(files.Save)||hasCamera||Directory.Exists(files.Camera)||
                photos>0||Directory.Exists(history.DirectoryPath)||Directory.Exists(Path.Combine(files.Photos,"pets"));
            string progress;
            if(current!=null)progress=DescribeSaveProgress(current).Progress;
            else if(hasSave){
                try{progress=DescribeSaveProgress(ReadTownSlot(files.Save)).Progress;}
                catch(Exception error)when(IsSaveLoadError(error)){progress="主档无法读取，导入前仍会保留备份。";}
            }else progress=occupied?"没有可读取的主档，槽位中已有文件。":"槽位可用于接收新小镇。";
            string contents=$"目标照片 {photos} 张 · 恢复点文件 {points} 个 · {(hasCamera?"有机位文件":"无机位文件")}";
            return new(slot,caption,active,occupied,progress,contents);
        }catch(Exception error)when(IsSaveLoadError(error)){
            return new(slot,caption,active,true,"目标文件暂时无法完整读取。","确认前会按已有文件处理。");
        }
    }

    BundleDestination DescribeBundleDestination(int slot)=>DescribeBundleDestination(slot,TownSlotName(slot),BundleSlot(slot),slot==_saveSlot?Data.Snapshot:null);

    void TownBundleControls()
    {
        if(ExplicitSavePath!=null)return;
        Section("整槽迁移","存档 · 照片 · 机位 · 恢复点");
        var card=MarketCard(_managementBody!,color:"eef1e7");
        MarketLabel(card,"把当前小镇打成一个迁移包。导入时先预览内容，再选目标槽位。",wrap:true);
        var include=new CheckButton{Text="包括小镇照片",ButtonPressed=true};card.AddChild(include);
        var points=new List<(string Id,CheckBox Toggle)>();
        if(UseTownPreferences()){
            try{
                foreach(var point in new NativeSnapshotHistory(SavePath).List().Where(point=>point.Valid)){
                    var toggle=new CheckBox{Text=$"恢复点 · {point.Created.LocalDateTime:MM-dd HH:mm} · 第 {point.GameDay} 天"};
                    card.AddChild(toggle);points.Add((point.Id,toggle));
                }
            }catch(IOException){MarketLabel(card,"恢复点列表暂时无法读取。",color:TownTheme.Muted);}
            catch(UnauthorizedAccessException){MarketLabel(card,"恢复点列表暂时无法读取。",color:TownTheme.Muted);}
        }
        var row=new HFlowContainer();row.AddThemeConstantOverride("h_separation",8);card.AddChild(row);
        var export=MarketButton(row,"导出小镇迁移包",()=>ChooseTownBundle(false,path=>{
            try{
                EnsureSaveCopyPath(path);
                NativeTownBundle.Export(path,BundleSlot(_saveSlot),Data.Snapshot,include.ButtonPressed,
                    points.Where(entry=>entry.Toggle.ButtonPressed).Select(entry=>entry.Id));
                SetManagementFeedback("小镇迁移包已导出。");
            }catch(Exception error){SetManagementFeedback("导出失败 · "+error.Message,true);}
        }),expand:false,accent:"存档");
        var import=MarketButton(row,"预览并导入迁移包",()=>ChooseTownBundle(true,ShowTownBundlePreview),expand:false,accent:"地图");
        export.Disabled=import.Disabled=!UseTownPreferences();
    }

    void ChooseTownBundle(bool importing,Action<string> selected)
    {
        var dialog=new FileDialog{Title=importing?"选择小镇迁移包":"导出小镇迁移包",Theme=_panel.Theme,Transient=true,Exclusive=true,
            Access=FileDialog.AccessEnum.Filesystem,FileMode=importing?FileDialog.FileModeEnum.OpenFile:FileDialog.FileModeEnum.SaveFile,
            Filters=new[]{"*.zip ; 小镇迁移包"}};
        if(!importing)dialog.CurrentFile=$"慢时光-存档{_saveSlot}.zip";
        _management!.AddChild(dialog);
        dialog.Canceled+=dialog.QueueFree;
        dialog.FileSelected+=path=>{dialog.Hide();dialog.QueueFree();selected(path);};
        dialog.PopupCenteredClamped(new(770,510),.93f);
    }

    void ShowTownBundlePreview(string path)
    {
        try{
            var file=new FileInfo(path);
            long length=file.Length;
            var writeTime=file.LastWriteTimeUtc;
            var preview=NativeTownBundle.Inspect(path);
            var window=new Window{Name="TownBundlePreview",Title="小镇迁移包预览",Theme=_panel.Theme,Visible=false,Transient=true,Exclusive=true};
            _management!.AddChild(window);
            var card=new PanelContainer();card.AddThemeStyleboxOverride("panel",TownTheme.Box("fff9ed",12,16));window.AddChild(card);
            card.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
            var column=new VBoxContainer();column.AddThemeConstantOverride("separation",10);card.AddChild(column);
            MarketLabel(column,"小镇迁移包预览",22,TownTheme.Leaf);
            var scroll=new ScrollContainer{SizeFlagsVertical=Control.SizeFlags.ExpandFill,HorizontalScrollMode=ScrollContainer.ScrollMode.Disabled};
            column.AddChild(scroll);
            var content=new VBoxContainer{SizeFlagsHorizontal=Control.SizeFlags.ExpandFill};
            content.AddThemeConstantOverride("separation",8);scroll.AddChild(content);
            var incoming=DescribeSaveProgress(preview.Snapshot);
            var source=MarketCard(content,color:"e9efe5");
            MarketLabel(source,"迁移包中的进度",16,TownTheme.Leaf);
            MarketLabel(source,incoming.Progress,wrap:true);
            MarketLabel(source,$"导出于 {preview.Created.LocalDateTime:yyyy-MM-dd HH:mm} · 照片 {preview.Photos} 张 · 恢复点 {preview.RecoveryPoints} 个 · {(preview.Camera?"包含机位":"无机位记录")}",wrap:true);
            var destinations=Enumerable.Range(1,TownSlotCount).Select(DescribeBundleDestination).ToArray();
            var row=new HFlowContainer();row.AddThemeConstantOverride("h_separation",8);column.AddChild(row);
            var cancel=MarketButton(row,"取消",Close,expand:false,accent:"存档");
            AddBundleDestinationChooser(content,row,destinations,slot=>{
                var selected=DescribeBundleDestination(slot);
                Close();
                ConfirmManagementAction($"导入到{selected.Caption}",$"目标 · {selected.State}\n{selected.Progress}\n\n导入后 · {incoming.Progress}\n\n{selected.Effect}",
                    "确认导入",()=>Install(slot));
            });
            window.CloseRequested+=Close;
            window.PopupCenteredClamped(new(610,435),.94f);
            cancel.GrabFocus();

            void Close(){window.Hide();window.QueueFree();}
            void Install(int slot)
            {
                try{
                    file.Refresh();
                    if(!file.Exists||file.Length!=length||file.LastWriteTimeUtc!=writeTime)throw new InvalidDataException("文件已变化，请重新打开预览。");
                    if(slot==_saveSlot&&!SaveSnapshot(true))return;
                    string? backup=NativeTownBundle.Import(path,BundleSlot(slot),ValidateTownBundle);
                    if(slot==_saveSlot){
                        _enableTownAutoSave=false;
                        _townSlotReloadRequired=true;
                        if(GetTree().ReloadCurrentScene()==Error.Ok)return;
                        SetTownPaused(true);
                        SetManagementFeedback("已导入当前槽位。请重新启动以载入迁移进度。",true);
                        return;
                    }
                    SetManagementFeedback($"已导入{TownSlotName(slot)}"+(backup==null?"":$" · 原文件备份 {backup}"));
                }catch(Exception error){SetManagementFeedback("导入失败 · "+error.Message,true);}
            }
        }catch(Exception error){SetManagementFeedback("迁移包无法预览 · "+error.Message,true);}
    }

    void AddBundleDestinationChooser(Control parent,Control actions,BundleDestination[] destinations,Action<int> continueWith)
    {
        var destination=MarketCard(parent,color:"f1e9da");
        MarketLabel(destination,"目标槽位",16,TownTheme.Leaf);
        int empty=Array.FindIndex(destinations,slot=>!slot.Occupied);
        var select=MarketOptions(destination,new[]{"请选择目标槽位"}.Concat(destinations.Select(slot=>$"{slot.Caption} · {slot.State}")).ToArray(),empty+1);
        select.Name="BundleDestinationSelect";
        select.AccessibilityName="迁移包目标槽位";
        var state=MarketLabel(destination,"",wrap:true);
        state.Name="BundleDestinationStatus";
        var assets=MarketLabel(destination,"",14,TownTheme.Muted,wrap:true);
        var effect=MarketLabel(destination,"",14,TownTheme.Muted,wrap:true);
        var proceed=MarketButton(actions,"检查并继续",()=>{
            if(select.Selected>0)continueWith(destinations[select.Selected-1].Slot);
        },expand:false,accent:"地图");
        proceed.Name="BundleDestinationContinue";
        select.ItemSelected+=_=>Refresh();
        Refresh();

        void Refresh()
        {
            proceed.Disabled=select.Selected<=0;
            assets.Visible=effect.Visible=select.Selected>0;
            if(select.Selected<=0){state.Text="所有槽位已有内容，请选定目标后查看当前进度与备份说明。";return;}
            var slot=destinations[select.Selected-1];
            state.Text=$"{slot.Caption} · {slot.State}\n{slot.Progress}";
            assets.Text=slot.Files;
            effect.Text=slot.Effect;
        }
    }

    void ValidateTownBundle(string folder)
    {
        string history=Path.Combine(folder,"history");
        if(Directory.Exists(history))foreach(string path in Directory.EnumerateFiles(history,"*.json"))Check(path,false);
        string camera=Path.Combine(folder,"camera.cfg");
        if(File.Exists(camera)){
            using var config=new ConfigFile();
            if(config.Load(camera)!=Error.Ok)throw new InvalidDataException("机位配置无法读取。");
        }
        Check(Path.Combine(folder,"town.json"),true);

        void Check(string path,bool primary)
        {
            var root=new JsonObject{{"snapshot",ReadTownSlot(path)}};
            foreach(string key in new[]{"version","rules","terrain","zones","portals"})root[key]=Data.Root[key]?.DeepClone();
            var data=new WorldData(root);
            var game=new NativeGame(root);
            game.Validate();LegacyImport.ValidateReferences(game);
            if(primary){game.Meta["native_real_anchor"]=DateTimeOffset.UtcNow.ToUnixTimeSeconds();data.Save(path);}
        }
    }
}
