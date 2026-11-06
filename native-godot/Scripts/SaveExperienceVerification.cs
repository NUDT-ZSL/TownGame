using Godot;
using System;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;

public partial class Main
{
    void VerifySaveExperience(Action<bool,string> check)
    {
        var directory=Directory.CreateTempSubdirectory("town-save-experience-");
        int previousFilter=_snapshotHistoryFilter,previousEstimate=_offlineEstimateIndex;
        var snapshot=Data.Snapshot.ToJsonString();
        try{
            string configPath=Path.Combine(directory.FullName,"slots.cfg");
            UpdateTownSlotConfig(configPath,config=>{
                config.SetValue("labels","slot_1","  林间\n庄园 ");
                config.SetValue("labels","slot_2","海边小镇");
            });
            UpdateTownSlotConfig(configPath,config=>config.SetValue("town","slot",3));
            using(var config=new ConfigFile()){
                check(config.Load(configPath)==Error.Ok&&config.GetValue("town","slot",1).AsInt32()==3&&
                    ReadTownSlotLabel(config,1)=="林间庄园"&&ReadTownSlotLabel(config,2)=="海边小镇"&&ReadTownSlotLabel(config,3)=="",
                    "切换存档槽位未保留各槽的本机名称");
            }
            check(NormalizeTownSlotLabel(new string('林',30)).Length==TownSlotLabelLimit,"槽位名称没有限制显示长度");

            string save=Path.Combine(directory.FullName,"history.json");
            var history=new NativeSnapshotHistory(save);
            var fixture=new NativeGame(NativeContent.Load());
            var candidate=fixture.State.DeepClone().AsObject();
            candidate["meta"]!["coins"]=DescribeSaveProgress(Data.Snapshot).Gold+123;
            history.RestoreValidated(candidate);
            var now=DateTimeOffset.UtcNow;
            string manual=history.Record(now,NativeSnapshotHistory.SnapshotKind.Manual);
            string corrupt=history.Record(now.AddSeconds(1),NativeSnapshotHistory.SnapshotKind.Automatic);
            File.WriteAllText(Path.Combine(history.DirectoryPath,corrupt),"{invalid");
            var points=history.List();
            bool enteredRestore=false;
            ShowManagement("存档");
            var host=new VBoxContainer{Name="SaveExperienceFixture"};
            _managementBody!.AddChild(host);
            _snapshotHistoryFilter=0;
            AddRecoveryHistory(host,points,point=>ShowRecoveryComparison(history,point,_=>enteredRestore=true));
            var filter=(OptionButton)host.FindChild("RecoveryHistoryFilter",true,false)!;
            filter.Select(4);filter.EmitSignal(OptionButton.SignalName.ItemSelected,4L);
            var invalid=host.FindChildren("RestoreRecoverySnapshot","Button",true,false).OfType<Button>().Single();
            check(invalid.Disabled&&((Label)host.FindChild("RecoveryHistoryCount",true,false)!).Text.Contains("1 / 2"),
                "恢复点文件异常筛选没有显示数量或阻止操作");
            filter.Select(1);filter.EmitSignal(OptionButton.SignalName.ItemSelected,1L);
            var valid=host.FindChildren("RestoreRecoverySnapshot","Button",true,false).OfType<Button>().Single();
            check(!valid.Disabled,"手动恢复点无法进入比较");
            valid.EmitSignal(BaseButton.SignalName.Pressed);
            var preview=_management!.GetChildren().OfType<ConfirmationDialog>().LastOrDefault(dialog=>dialog.Title=="比较历史恢复点"&&dialog.Visible);
            check(preview!=null,$"无法打开恢复点对比 · {_managementFeedbackLabel?.Text}");
            check(preview!.DialogText.Contains("当前 ·")&&preview.DialogText.Contains("恢复后 ·")&&
                preview.DialogText.Contains("金币 +123")&&preview.DialogText.Contains("仓库")&&!enteredRestore&&Data.Snapshot.ToJsonString()==snapshot,
                "恢复对比未显示实际差异或在确认前改动存档");
            File.AppendAllText(Path.Combine(history.DirectoryPath,manual)," ");
            preview.EmitSignal(AcceptDialog.SignalName.Confirmed);
            preview.Hide();
            check(!enteredRestore,"恢复点在比较后变化时仍然执行恢复");

            string photos=Path.Combine(directory.FullName,"photos");
            string camera=Path.Combine(directory.FullName,"camera.cfg");
            var active=DescribeBundleDestination(1,"林间庄园",new(save,photos,camera),Data.Snapshot);
            var emptySlot=new NativeTownBundle.Slot(Path.Combine(directory.FullName,"new.json"),photos,camera);
            var empty=DescribeBundleDestination(2,"接收小镇",emptySlot);
            File.WriteAllText(camera,"");
            var existing=DescribeBundleDestination(2,"海边小镇",emptySlot);
            check(active.Active&&active.Occupied&&!empty.Occupied&&existing.Occupied&&existing.Files.Contains("有机位文件"),
                "迁移目标未区分当前小镇、空槽和有附件的槽位");
            int selectedSlot=0;
            var chooser=new VBoxContainer();host.AddChild(chooser);
            AddBundleDestinationChooser(chooser,chooser,new[]{active,existing},slot=>selectedSlot=slot);
            var select=(OptionButton)chooser.FindChild("BundleDestinationSelect",true,false)!;
            var next=(Button)chooser.FindChild("BundleDestinationContinue",true,false)!;
            check(select.Selected==0&&next.Disabled,"所有槽位已有文件时默认选中了覆盖目标");
            select.Select(2);select.EmitSignal(OptionButton.SignalName.ItemSelected,2L);
            next.EmitSignal(BaseButton.SignalName.Pressed);
            check(!next.Disabled&&selectedSlot==2&&((Label)chooser.FindChild("BundleDestinationStatus",true,false)!).Text.Contains("海边小镇"),
                "迁移目标信息未随所选槽位更新");
            var autoTarget=new VBoxContainer();host.AddChild(autoTarget);
            AddBundleDestinationChooser(autoTarget,autoTarget,new[]{active,empty},_=>{});
            check(((OptionButton)autoTarget.FindChild("BundleDestinationSelect",true,false)!).Selected==2,"有空槽时未推荐空槽");

            var duration=(OptionButton)_managementBody.FindChild("TownOfflineEstimateDuration",true,false)!;
            duration.Select(3);duration.EmitSignal(OptionButton.SignalName.ItemSelected,3L);
            var estimate=(Label)_managementBody.FindChild("TownOfflineEstimateResult",true,false)!;
            check(estimate.Text==DescribeOfflineEstimate(OfflineEstimateTimes[3],Game.OfflineLimit)&&
                DescribeOfflineEstimate(8*NativeGame.Hour,0).Contains("游戏推进 0 秒")&&
                DescribeOfflineEstimate(8*NativeGame.Hour,4).Contains("跳过 4 小时")&&Data.Snapshot.ToJsonString()==snapshot,
                "离线估算未反映暂停、时间上限或改动了当前进度");
            GD.Print("SAVE_EXPERIENCE_VERIFIED local_slot_labels recovery_filter readonly_comparison stale_guard destination_choice offline_estimate");
        }finally{
            _snapshotHistoryFilter=previousFilter;
            _offlineEstimateIndex=previousEstimate;
            _management?.Hide();
            directory.Delete(recursive:true);
        }
    }
}
