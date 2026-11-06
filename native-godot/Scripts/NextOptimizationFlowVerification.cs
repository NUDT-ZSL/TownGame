using Godot;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;

public partial class Main
{
    void VerifyTownFlowOptimizations(Action<bool,string> check)
    {
        VerifyTownPhotoFlow(check);
        var snapshot = Data.Snapshot;
        var previousSelection = _selection;
        var previousTarget = _fastForward;
        var previousTimeResult = (_lastFastForward,_fastForwardBegan);
        var previousBatch = Game.LastBatchCare.ToArray();
        double previousAccumulator = _simulationAccumulator;
        bool wasPaused = Paused;
        bool wasManaging = _management?.Visible == true;
        string previousTab = _tab, previousEntry = _managementEntryTarget;
        try
        {
            Data.Root["snapshot"] = snapshot.DeepClone();
            Data.Reindex();
            VerifyFastForwardFlow(check);
            Data.Root["snapshot"] = snapshot.DeepClone();
            Data.Reindex();
            VerifyBatchCarePreviewFlow(check);
        }
        finally
        {
            _management?.Hide();
            Data.Root["snapshot"] = snapshot;
            Data.Reindex();
            Game.LastBatchCare.Clear();
            Game.LastBatchCare.AddRange(previousBatch);
            _fastForward = previousTarget;
            (_lastFastForward,_fastForwardBegan)=previousTimeResult;
            _simulationAccumulator = previousAccumulator;
            _selection = previousSelection;
            SetTownPaused(wasPaused);
            RefreshSpeedControl();
            SyncBusinessWorld();
            if (wasManaging) ShowManagement(previousTab, previousEntry);
            else
            {
                _tab = previousTab;
                _managementEntryTarget = previousEntry;
            }
        }
    }

    void VerifyTownPhotoFlow(Action<bool,string> check)
    {
        bool wasPaused = Paused, wasManaging = _management?.Visible == true;
        bool hudVisible = _townHudLayer.Visible;
        bool previousPausedFlag = _beforePhotoPaused;
        float fov = _camera.Fov, previousFovFlag = _beforePhotoFov;
        var mouseMode = Input.MouseMode;
        try
        {
            SetTownPaused(false);
            _camera.Fov = 67;
            TogglePhotoMode();
            check(InPhotoMode && Paused && !_townHudLayer.Visible && _management?.Visible != true,
                "进入拍照模式后没有暂停游戏、收起界面");
            _photoLens!.Value = 42;
            check(Mathf.IsEqualApprox(_camera.Fov, 42), "取景视野滑块没有更新镜头");
            LeavePhotoMode();
            check(!InPhotoMode && !Paused && _townHudLayer.Visible && Mathf.IsEqualApprox(_camera.Fov, 67),
                "退出拍照模式后没有恢复运行状态和镜头视野");
            SetTownPaused(true);
            TogglePhotoMode();
            LeavePhotoMode();
            check(Paused, "原先暂停的游戏在退出拍照模式后被继续运行");
        }
        finally
        {
            LeavePhotoMode();
            _camera.Fov = fov;
            _beforePhotoFov = previousFovFlag;
            _beforePhotoPaused = previousPausedFlag;
            SetTownPaused(wasPaused);
            _townHudLayer.Visible = hudVisible;
            Input.MouseMode = mouseMode;
            if (wasManaging) ShowManagement(_tab, _managementEntryTarget);
        }

        string folder = Path.Combine(MigrationOutput, "photo-path-check-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var date = new DateTimeOffset(2026, 1, 2, 3, 4, 5, 6, TimeSpan.Zero);
        string first = NewPhotoPath(folder, date);
        string second = "";
        try
        {
            check(Path.GetFileName(first) == "20260102-030405-006.png", "照片文件名没有精确到毫秒");
            File.WriteAllBytes(first, Array.Empty<byte>());
            second = NewPhotoPath(folder, date);
            check(Path.GetFileName(second) == "20260102-030405-006-2.png", "同一毫秒拍照时会覆盖原照片");
            File.WriteAllBytes(second, Array.Empty<byte>());
            check(Path.GetFileName(NewPhotoPath(folder, date)) == "20260102-030405-006-3.png",
                "照片重名时没有继续分配唯一文件名");
        }
        finally
        {
            if (File.Exists(first)) File.Delete(first);
            if (second.Length > 0 && File.Exists(second)) File.Delete(second);
            Directory.Delete(folder);
        }
    }

    void VerifyFastForwardFlow(Action<bool,string> check)
    {
        var living = Game.Pets.OfType<JsonNode>().Where(Game.Active).ToArray();
        foreach (var pet in living)
        {
            pet["status"] = "正常";
            pet["illness_type"] = "";
            pet["illness_budget"] = 10000;
            pet["injury"] = 0;
            pet["health"] = 100;
            pet["hunger"] = 100;
            pet["cleanliness"] = 100;
            pet["fostered"] = false;
        }
        Game.Commons["enabled"] = false;
        Game.CancelFreight("快进测试");
        Data.Meta["farm_manager"] = false;
        Data.Meta["caretaker"] = false;
        Data.Meta["native_next_hour"] = Game.Now + NativeGame.Hour;
        Data.Meta["native_speed"] = 120;

        var projects = Game.Rows("town_buildings").OfType<JsonNode>().ToArray();
        foreach (var underway in projects.Where(IsTownProjectUnderway)) underway["status"] = "ready";
        var project = projects.First();
        string id = NativeGame.S(project, "building_id");
        double finish = Game.Now + 45;
        project["placed_ts"] = Game.Now;
        project["finish_ts"] = finish;
        project["status"] = "building";
        BeginFastForward(FastForwardGoal.Construction);
        check(_fastForward is { Goal: FastForwardGoal.Construction } && _fastForward.Value.At == finish,
            "没有锁定下一项工程的完工时间");
        AdvanceTownClock(.5);
        check(Math.Abs(Game.Now - finish) < 1e-6 && !_fastForward.HasValue
            && NativeGame.S(Game.FindTownBuilding(id)!, "status") == "ready"
            && NativeGame.I(Data.Meta, "native_speed") == 1, "快进没有在完工时刻精确停止并恢复正常速度");

        BeginFastForward(FastForwardGoal.Morning);
        check(_fastForward is { Goal: FastForwardGoal.Morning }, "健康状态下无法快进到次日");
        var patient = Game.Pets.OfType<JsonNode>().First(Game.Active);
        patient["health"] = 40;
        double interruptedAt = Game.Now;
        AdvanceTownClock(.5);
        check(!_fastForward.HasValue && Game.Now == interruptedAt && _selection.Contains("紧急照护", StringComparison.Ordinal),
            "紧急照护未中断快进");
        patient["health"] = 100;

        int trips = NativeGame.I(Game.FreightState, "trips");
        Data.Meta["start_ts"] = Game.Now;
        double opens = Game.Now + 55;
        Data.Meta["native_town_commission"] = new JsonObject
        {
            ["season"] = Game.TownCommissionSeason, ["opens"] = opens, ["freight_baseline"] = trips,
        };
        BeginFastForward(FastForwardGoal.Commission);
        check(_fastForward is { Goal: FastForwardGoal.Commission }, "没有识别季节委托的交付时段");
        AdvanceTownClock(.5);
        var commission = Game.TownCommission();
        check(Math.Abs(Game.Now - opens) < 1e-6 && !_fastForward.HasValue && commission.Stage == 1
            && !commission.FreightArrived && NativeGame.I(Game.FreightState, "trips") == trips
            && _selection.Contains("实际货运", StringComparison.Ordinal), "快进到交付时段绕过了实际货运");
    }

    void VerifyBatchCarePreviewFlow(Action<bool,string> check)
    {
        var pets = Game.Pets.OfType<JsonNode>().Where(Game.Active).ToArray();
        foreach (var pet in pets)
        {
            pet["fostered"] = false;
            pet["hunger"] = 100;
        }
        pets[0]["hunger"] = 30;
        pets[1]["hunger"] = 5;
        string hungriest = NativeGame.S(pets[1], "pet_id");
        string next = NativeGame.S(pets[0], "pet_id");
        Game.Rows("inventory").Clear();
        Game.Add("普通饲料", 1);
        Game.Add("腐败物", 2);

        ShowManagement("照护");
        string before = Game.State.ToJsonString();
        Press("PreviewBatchCare_feed");
        var preview = Game.PreviewBatchCare("feed");
        check(Game.State.ToJsonString() == before && Game.Stock("普通饲料") == 1,
            "批量照护的预览阶段消耗了物资或改写了存档");
        check(preview.Rows[0].Id == hungriest && preview.Rows[0].Skip.Length == 0
            && preview.Rows.Single(row => row.Id == next).Skip == "缺少可用食物"
            && preview.Supplies.GetValueOrDefault("普通饲料") == 1, "批量喂食预览未按饥饿程度或实际余量排序");
        var panel = _managementBody!.FindChild("BatchCarePreview", true, false) as PanelContainer;
        var list = panel?.FindChild("BatchCarePreviewList", true, false) as RichTextLabel;
        check(panel?.Visible == true && list?.Text.Contains("缺少可用食物", StringComparison.Ordinal) == true,
            "预览面板没有呈现被跳过的伙伴和原因");

        Game.Add("普通饲料", 1);
        string changed = Game.State.ToJsonString();
        Press("ConfirmBatchCare");
        check(Game.State.ToJsonString() == changed && _selection.Contains("重新预览", StringComparison.Ordinal),
            "物资改变后过期预览仍能执行");

        Game.Take("普通饲料", 1);
        Press("PreviewBatchCare_feed");
        Press("ConfirmBatchCare");
        var fed = Game.Pets.OfType<JsonNode>().Single(pet => NativeGame.S(pet, "pet_id") == hungriest);
        var skipped = Game.Pets.OfType<JsonNode>().Single(pet => NativeGame.S(pet, "pet_id") == next);
        check(Game.LastBatchCare.SequenceEqual(new[] { hungriest }) && NativeGame.D(fed, "hunger") > 5
            && NativeGame.D(skipped, "hunger") == 30 && Game.Stock("普通饲料") == 0 && Game.Stock("腐败物") == 2,
            "确认照护后没有按照预览处理伙伴和物资");

        void Press(string name)
        {
            var button = _managementBody!.FindChild(name, true, false) as Button;
            check(button is { Visible: true, Disabled: false }, $"批量照护按钮不可用 · {name}");
            button!.EmitSignal(BaseButton.SignalName.Pressed);
        }
    }
}
