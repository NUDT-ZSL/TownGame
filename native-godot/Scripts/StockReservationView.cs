using Godot;
using System;
using System.Text.Json.Nodes;

public partial class Main
{
    void ChangeStockReservation(string owner, string item, int quantity) =>
        Act("reserve_stock", new JsonObject { ["owner"] = owner, ["item"] = item, ["quantity"] = quantity });

    Action<NativeGame.StockAvailability, int, bool> AddStockReservationControls(Container parent,
        Func<string> owner, Func<string> item, string nodePrefix, string accent)
    {
        var row = new HFlowContainer { Name = $"{nodePrefix}Controls" };
        row.AddThemeConstantOverride("h_separation", 6);
        parent.AddChild(row);
        var quantity = MarketQuantity(row, 1);
        quantity.Name = $"{nodePrefix}Quantity";
        quantity.TooltipText = "选择要新增预留的份数";
        var reserve = MarketButton(row, "预留物资", () => ChangeStockReservation(owner(), item(), (int)quantity.Value),
            expand: false, accent: accent);
        reserve.Name = $"{nodePrefix}Hold";
        reserve.TooltipText = "将所选数量留给这项委托";
        var release = MarketButton(row, "释放预留", () => ChangeStockReservation(owner(), item(), 0),
            expand: false, accent: accent);
        release.Name = $"{nodePrefix}Release";
        var notice = MarketLabel(parent, "", color: new Color("8a4938"), wrap: true);
        notice.Name = $"{nodePrefix}Expiry";
        notice.VerticalAlignment = VerticalAlignment.Center;

        return (stock, required, active) =>
        {
            row.Visible = active;
            notice.Visible = active && stock.ExpiringForOwner > 0;
            if (!active) return;
            int maximum = Math.Min(stock.Available, required - stock.ReservedForOwner);
            quantity.MaxValue = Math.Max(1, maximum);
            quantity.Editable = maximum > 0;
            reserve.Disabled = maximum <= 0;
            reserve.TooltipText = maximum > 0 ? $"可新增预留 {maximum} 份" :
                stock.ReservedForOwner >= required ? "已预留本委托所需数量" : "暂无可用库存";
            release.Visible = stock.ReservedForOwner > 0;
            notice.Text = notice.Visible ? $"预留临期 {stock.ExpiringForOwner} 份 · 最早{DeadlineText(stock.NextExpiry - Game.Now)}" : "";
        };
    }

    static string ReservationStockText(NativeGame.StockAvailability stock) =>
        $"可用 {stock.Available} · 本单预留 {stock.ReservedForOwner}" +
        (stock.Reserved > stock.ReservedForOwner ? $" · 其他预留 {stock.Reserved - stock.ReservedForOwner}" : "");
}
