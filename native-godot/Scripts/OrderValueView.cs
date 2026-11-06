using System.Collections.Generic;
using System.Text.Json.Nodes;

public partial class Main
{
    string OrderValueCaption(JsonNode order)
    {
        var value=Game.QuoteOrderValue(order);
        if(value.Missing==0)return $"追加支出 0 金币 · 现货估值 {value.OnHandValue} · 预计净收益 {value.Reward-value.OnHandValue:+0;-0;0} 金币";
        var lines=new List<string>();
        if(value.Buy is { } buy)lines.Add(Describe(buy));
        if(value.Craft is { } craft)lines.Add(Describe(craft));
        return lines.Count==0?"缺货来源暂无可核算报价 · 现货按当前售价估值":string.Join("\n",lines);

        string Describe(NativeGame.OrderSourceCost cost)
        {
            long stock=value.OnHandValue+cost.MaterialValue;
            return $"{cost.Source}方案 · 采购 {cost.Purchase} · 加工费 {cost.Fee} · 库存估值 {stock} · 预计净收益 {value.Reward-cost.Total-value.OnHandValue:+0;-0;0} 金币";
        }
    }
}
