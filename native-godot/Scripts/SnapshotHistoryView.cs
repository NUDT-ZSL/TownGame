using Godot;
using System;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;

public partial class Main
{
    int _snapshotHistoryDay=-1;
    DateTimeOffset _snapshotHistoryTime=DateTimeOffset.MinValue;
    string _snapshotHistoryWarning="";
    int _snapshotHistoryFilter;
    static readonly string[] SnapshotFilterNames={"全部来源","手动保存","自动备份","恢复前备份","文件异常"};

    void RecordSnapshotIfDue(bool manual)
    {
        if(!UseTownPreferences())return;
        var now=DateTimeOffset.UtcNow;
        int day=(int)((Game.Now-NativeGame.D(Data.Meta,"start_ts"))/NativeGame.Day);
        if(!manual&&_snapshotHistoryDay==day&&now-_snapshotHistoryTime<TimeSpan.FromMinutes(30))return;
        _snapshotHistoryDay=day;
        _snapshotHistoryTime=now;
        try{
            var history=new NativeSnapshotHistory(SavePath);
            history.Record(now,manual?NativeSnapshotHistory.SnapshotKind.Manual:NativeSnapshotHistory.SnapshotKind.Automatic);
            _snapshotHistoryWarning="";
        }catch(Exception error)when(error is IOException or UnauthorizedAccessException){
            _snapshotHistoryWarning=$"恢复点保存失败 · {error.Message}";
            if(manual)SetManagementFeedback(_snapshotHistoryWarning,true);
        }
    }

    void SnapshotHistoryControls()
    {
        Section("历史恢复点",$"保留最近 {NativeSnapshotHistory.Limit} 个");
        Text("手动保存会建立恢复点。自动存档会在首次保存、游戏换日或间隔 30 分钟时建立恢复点。");
        if(_snapshotHistoryWarning.Length>0)Text(_snapshotHistoryWarning);
        var create=ActionButton("保存当前进度为恢复点",()=>{
            if(!UseTownPreferences())return;
            try{
                WriteTownProgress();
                RecordSnapshotIfDue(true);
                ShowManagement("存档");
                SetManagementFeedback(_snapshotHistoryWarning.Length==0?"当前进度已保存为恢复点。":_snapshotHistoryWarning,
                    _snapshotHistoryWarning.Length>0);
            }catch(Exception error)when(error is IOException or UnauthorizedAccessException){
                SetManagementFeedback($"保存失败 · {error.Message}",true);
            }
        });
        create.Name="CreateRecoverySnapshot";
        create.Disabled=!UseTownPreferences();
        if(!UseTownPreferences())return;
        NativeSnapshotHistory.Point[] points;
        try{points=new NativeSnapshotHistory(SavePath).List();}
        catch(Exception error)when(error is IOException or UnauthorizedAccessException){
            Text($"无法读取恢复点 · {error.Message}");
            return;
        }
        if(points.Length==0){Text("暂无恢复点。可手动保存当前进度。");return;}
        var history=new NativeSnapshotHistory(SavePath);
        AddRecoveryHistory(_managementBody!,points,point=>ShowRecoveryComparison(history,point,RestoreSnapshotPoint));
    }

    static bool MatchesSnapshotFilter(NativeSnapshotHistory.Point point,int filter)=>filter switch{
        1=>point.Kind==NativeSnapshotHistory.SnapshotKind.Manual,
        2=>point.Kind==NativeSnapshotHistory.SnapshotKind.Automatic,
        3=>point.Kind==NativeSnapshotHistory.SnapshotKind.BeforeRestore,
        4=>!point.Valid,
        _=>true
    };

    void AddRecoveryHistory(Control parent,NativeSnapshotHistory.Point[] points,Action<NativeSnapshotHistory.Point> inspect)
    {
        var tools=new HFlowContainer();tools.AddThemeConstantOverride("h_separation",8);parent.AddChild(tools);
        var filter=MarketOptions(tools,SnapshotFilterNames.Select((name,index)=>$"{name} · {points.Count(point=>MatchesSnapshotFilter(point,index))}").ToArray(),
            _snapshotHistoryFilter);
        filter.Name="RecoveryHistoryFilter";
        filter.AccessibilityName="按恢复点来源筛选";
        var count=MarketLabel(tools,"",14,TownTheme.Muted);
        count.Name="RecoveryHistoryCount";
        count.VerticalAlignment=VerticalAlignment.Center;
        var list=new VBoxContainer{Name="RecoveryHistoryRows"};parent.AddChild(list);
        filter.ItemSelected+=index=>{_snapshotHistoryFilter=(int)index;Refresh();};
        Refresh();

        void Refresh()
        {
            foreach(var child in list.GetChildren()){list.RemoveChild(child);child.QueueFree();}
            var matching=points.Where(point=>MatchesSnapshotFilter(point,_snapshotHistoryFilter)).ToArray();
            count.Text=$"显示 {matching.Length} / {points.Length} 个";
            if(matching.Length==0)MarketLabel(list,"这一来源没有恢复点，可切回全部来源。",color:TownTheme.Muted,wrap:true);
            foreach(var point in matching){
                var card=MarketCard(list,color:"f2ebdf");
                card.Name="RecoveryHistoryRow";
                string kind=point.Kind switch{
                    NativeSnapshotHistory.SnapshotKind.Manual=>"手动保存",
                    NativeSnapshotHistory.SnapshotKind.BeforeRestore=>"恢复前备份",
                    _=>"自动备份"
                };
                MarketLabel(card,$"{point.Created.LocalDateTime:yyyy-MM-dd HH:mm:ss} · {kind}",16,TownTheme.Ink,wrap:true);
                string stats=$"第 {point.GameDay} 天 · 金币 {point.Coins:N0} · 在册伙伴 {point.Pets} 位";
                MarketLabel(card,point.Valid?stats:"文件异常，无法预览或恢复。",14,point.Valid?TownTheme.Muted:new Color("843c3f"),wrap:true);
                var button=MarketButton(card,"比较并恢复",()=>inspect(point),expand:false,accent:"存档");
                button.Name="RestoreRecoverySnapshot";
                button.Disabled=!point.Valid;
                button.SizeFlagsHorizontal=Control.SizeFlags.ShrinkBegin;
            }
        }
    }

    readonly record struct SaveComparison(double Elapsed,string GameDate,int Gold,int Pets,long Stock)
    {
        public string Progress=>$"{GameDate} · 金币 {Gold:N0} · 在册伙伴 {Pets} 位 · 仓库 {Stock:N0} 份";
    }

    static SaveComparison DescribeSaveProgress(JsonObject snapshot)
    {
        var meta=snapshot["meta"]!.AsObject();
        double sim=NativeGame.D(meta,"sim_ts"),start=NativeGame.D(meta,"start_ts");
        if(!double.IsFinite(sim)||!double.IsFinite(start)||sim<0||start<0||sim>253402300799||start>253402300799)
            throw new InvalidDataException("存档的游戏时间无法显示。");
        double elapsed=Math.Max(0,sim-start);
        int minutes=(int)(sim%NativeGame.Day/60);
        var pets=snapshot["pets"]!.AsArray().OfType<JsonObject>().Count(pet=>NativeGame.D(pet,"death_ts")==0&&NativeGame.S(pet,"departure")=="");
        long stock=snapshot["inventory"] is JsonArray inventory?inventory.OfType<JsonObject>().Sum(batch=>(long)NativeGame.I(batch,"quantity")):0;
        return new(elapsed,$"第 {1+(int)(elapsed/NativeGame.Day)} 天 {minutes/60:00}:{minutes%60:00}",NativeGame.I(meta,"coins"),pets,stock);
    }

    static string DescribeSaveDifference(SaveComparison before,SaveComparison after)
    {
        double difference=after.Elapsed-before.Elapsed;
        string time=difference<0?$"回到 {OfflineDuration(-difference)}前":difference>0?$"前进 {OfflineDuration(difference)}":"游戏时间相同";
        return $"{time} · 金币 {(long)after.Gold-before.Gold:+#,0;-#,0;0} · 伙伴 {(long)after.Pets-before.Pets:+0;-0;0} 位 · 仓库 {after.Stock-before.Stock:+#,0;-#,0;0} 份";
    }

    void ShowRecoveryComparison(NativeSnapshotHistory history,NativeSnapshotHistory.Point point,Action<string> restore)
    {
        try{
            var file=new FileInfo(Path.Combine(history.DirectoryPath,point.Id));
            long size=file.Length;
            var changed=file.LastWriteTimeUtc;
            var incoming=DescribeSaveProgress(history.ReadRecovery(point.Id,Data.Root,DateTimeOffset.UtcNow));
            var current=DescribeSaveProgress(Data.Snapshot);
            ConfirmManagementAction("比较历史恢复点",
                $"恢复点 · {point.Created.LocalDateTime:yyyy-MM-dd HH:mm:ss}\n\n当前 · {current.Progress}\n恢复后 · {incoming.Progress}\n{DescribeSaveDifference(current,incoming)}\n\n恢复前会备份当前进度。",
                "确认恢复",()=>{
                    try{
                        file.Refresh();
                        if(!file.Exists||file.Length!=size||file.LastWriteTimeUtc!=changed)throw new IOException("恢复点已变化，请重新比较后再恢复。");
                        restore(point.Id);
                    }catch(Exception error)when(IsSaveLoadError(error)){SetManagementFeedback(error.Message,true);}
                });
        }catch(Exception error)when(IsSaveLoadError(error)){
            SetManagementFeedback($"恢复点无法预览 · {error.Message}",true);
        }
    }

    void RestoreSnapshotPoint(string id)
    {
        if(!UseTownPreferences())return;
        try{
            var history=new NativeSnapshotHistory(SavePath);
            var now=DateTimeOffset.UtcNow;
            var snapshot=history.ReadRecovery(id,Data.Root,now);
            WriteTownProgress();
            history.Record(now,NativeSnapshotHistory.SnapshotKind.BeforeRestore,keep:id);
            history.RestoreValidated(snapshot);
            _enableTownAutoSave=false;
            _townSlotReloadRequired=true;
            var result=GetTree().ReloadCurrentScene();
            if(result!=Error.Ok){
                SetTownPaused(true);
                SetManagementFeedback("进度已恢复。重新启动游戏即可加载。",true);
            }
        }catch(Exception error){SetManagementFeedback($"恢复失败 · {error.Message}",true);}
    }
}
