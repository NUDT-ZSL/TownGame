using Godot;
using System;
using System.Linq;

public partial class Main
{
    NativeGame.OfflineSettlement? _offlineSettlement;
    static readonly double[] OfflineEstimateTimes={.5*NativeGame.Hour,2*NativeGame.Hour,8*NativeGame.Hour,24*NativeGame.Hour};
    int _offlineEstimateIndex=2;

    void ApplyOfflineProgress(long now)
    {
        _offlineSettlement = Game.SettleOffline(now);
        if (_offlineSettlement is not { } report)
            return;

        if (report.Advanced > 0)
            SyncBusinessWorld();
        if (report.Elapsed < 60)
            return;

        _selection = report.Paused
            ? "欢迎回来 · 离线期间已暂停"
            : $"欢迎回来 · 离线结算了 {OfflineDuration(report.Advanced)}";
        bool preview = OS.GetCmdlineUserArgs().Any(arg => arg.EndsWith("-preview", StringComparison.Ordinal));
        if (!preview && DisplayServer.GetName() != "headless")
            Callable.From(ShowOfflineSettlement).CallDeferred();
    }

    void ShowOfflineSettlement()
    {
        if (_offlineSettlement is not { } report)
            return;

        var dialog = new AcceptDialog
        {
            Title = "离线结算", Borderless = true, Transient = true, Exclusive = true,
            Theme = _panel.Theme, OkButtonText = "进入小镇", DialogAutowrap = true,
            DialogText = $"欢迎回到慢时光\n\n{OfflineSummary(report)}\n\n可在「经营管理 → 存档」查看结算和调整离线设置。",
        };
        GetWindow().AddChild(dialog);
        TownTheme.AccentButton(dialog.GetOkButton(), "存档", 6);
        dialog.Confirmed += dialog.QueueFree;
        dialog.Canceled += dialog.QueueFree;
        dialog.PopupCenteredClamped(new Vector2I(500, 240), .88f);
    }

    void OfflineProgressControls()
    {
        Section("离线时间", "适用于下次返回小镇");
        Text("正常速度下，离开现实 1 秒推进游戏 1 秒。超过所选上限的时间会跳过。");
        var limits = NativeGame.OfflineLimits;
        var mode = Options(limits.Select(hours => hours == 0 ? "暂停离线推进" : $"正常速度 · 最多 {hours} 小时"));
        mode.Name="TownOfflineMode";
        mode.CustomMinimumSize = new(280, 36);
        mode.Select(Array.IndexOf(limits, Game.OfflineLimit));
        var card=MarketCard(_managementBody!,color:"e9efe5");
        MarketLabel(card,"离开前估算",16,TownTheme.Leaf);
        var row=new HFlowContainer();row.AddThemeConstantOverride("h_separation",8);card.AddChild(row);
        MarketLabel(row,"预计离开",14,TownTheme.Muted).VerticalAlignment=VerticalAlignment.Center;
        var estimate=MarketOptions(row,OfflineEstimateTimes.Select(OfflineDuration).ToArray(),_offlineEstimateIndex);
        estimate.Name="TownOfflineEstimateDuration";
        estimate.AccessibilityName="预计离开的现实时间";
        var result=MarketLabel(card,"",14,TownTheme.Ink,wrap:true);
        result.Name="TownOfflineEstimateResult";
        MarketLabel(card,"估算只计算时间。仓库和伙伴的变化会在返回小镇时结算。",14,TownTheme.Muted,wrap:true);
        estimate.ItemSelected+=index=>{_offlineEstimateIndex=(int)index;RefreshEstimate();};
        mode.ItemSelected += index=>{SaveOfflineLimit(limits[index],mode);RefreshEstimate();};
        RefreshEstimate();

        if (_offlineSettlement is { } report)
        {
            Section("本次离线结算");
            Text(OfflineSummary(report));
        }

        void RefreshEstimate()=>result.Text=DescribeOfflineEstimate(OfflineEstimateTimes[_offlineEstimateIndex],Game.OfflineLimit);
    }

    static string DescribeOfflineEstimate(double elapsed,int limit)
    {
        double advanced=Math.Min(elapsed,limit*NativeGame.Hour);
        string summary=$"现实离开 {OfflineDuration(elapsed)} · 游戏推进 {OfflineDuration(advanced)}";
        return advanced<elapsed?summary+$" · 跳过 {OfflineDuration(elapsed-advanced)}":summary;
    }

    void SaveOfflineLimit(int hours, OptionButton mode)
    {
        var meta = Data.Meta;
        var priorLimit = meta[NativeGame.OfflineLimitKey]?.DeepClone();
        var priorAnchor = meta["native_real_anchor"]?.DeepClone();
        meta[NativeGame.OfflineLimitKey] = hours;
        meta["native_real_anchor"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        try
        {
            WriteTownProgress();
            SetManagementFeedback("离线时间设置已保存");
        }
        catch (Exception error)
        {
            if (priorLimit == null)
                meta.Remove(NativeGame.OfflineLimitKey);
            else
                meta[NativeGame.OfflineLimitKey] = priorLimit;
            meta["native_real_anchor"] = priorAnchor;
            mode.Select(Array.IndexOf(NativeGame.OfflineLimits, Game.OfflineLimit));
            SetManagementFeedback("离线时间设置保存失败：" + error.Message, true);
        }
    }

    static string OfflineSummary(NativeGame.OfflineSettlement report)
    {
        string time = $"离开 {OfflineDuration(report.Elapsed)}";
        if (report.Paused)
            return $"{time}，离线期间已暂停。\n当前有 {report.CareNeeded} 位伙伴需要优先照护。";

        time += $"，实际推进 {OfflineDuration(report.Advanced)}";
        if (report.Elapsed > report.Advanced)
            time += $"，超出上限的 {OfflineDuration(report.Elapsed - report.Advanced)} 已跳过";

        return $"{time}。\n金币 {report.CoinChange:+0;-0;0} · 仓库物资 {report.StockChange:+0;-0;0} 份"
            + $"\n新增伙伴 {report.Arrivals} 位 · 离园 {report.Departures} 位 · 需优先照护 {report.CareNeeded} 位";
    }

    static string OfflineDuration(double seconds)
    {
        var duration = TimeSpan.FromSeconds(seconds);
        string days = duration.Days > 0 ? $"{duration.Days} 天 " : "";
        string hours = duration.Hours > 0 ? $"{duration.Hours} 小时 " : "";
        string minutes = duration.Minutes > 0 ? $"{duration.Minutes} 分钟" : "";
        return duration.TotalMinutes < 1
            ? $"{duration.Seconds} 秒"
            : (days + hours + minutes).TrimEnd();
    }
}
