using Godot;
using System;
using System.Linq;

public partial class Main
{
    readonly record struct WagePayment(double Due,string Roles,int Cost,long Remaining);

    static WagePayment[] WagePayments(NativeGame.ExpenseForecast forecast)
    {
        long remaining=forecast.PrivateBalance;
        return forecast.Wages.SelectMany(wage=>Enumerable.Range(0,wage.Shifts).Select(shift=>
                (Due:wage.FirstDue+shift*wage.Interval,wage.Role,wage.PerShift)))
            .GroupBy(shift=>shift.Due).OrderBy(group=>group.Key).Select(group=>{
                int cost=group.Sum(shift=>shift.PerShift);
                remaining-=cost;
                return new WagePayment(group.Key,string.Join("、",group.Select(shift=>shift.Role)),cost,remaining);
            }).ToArray();
    }

    Control ScheduledExpenseControls()
    {
        var card=MarketCard(_managementBody!,color:"f3f1e4");
        var panel=card.GetParent<Control>();
        if(_managementEntryTarget=="scheduled-expenses")_managementFocusTarget=panel;
        MarketLabel(card,"未来 24 游戏小时 · 已知支出",18,TownTheme.Accent("财务"));
        MarketLabel(card,"按当前计划和人员估算。营收及自治新开工会随游戏进度变化。",color:TownTheme.Muted,wrap:true);
        var personal=MarketLabel(card,"",16,TownTheme.Accent("财务"),wrap:true);
        personal.Name="ScheduledPrivateExpense";
        var wages=MarketLabel(card,"",color:TownTheme.Muted,wrap:true);
        var wageRisk=MarketLabel(card,"",14,TownTheme.Muted,wrap:true);
        wageRisk.Name="ScheduledWageRisk";
        var showTimeline=new CheckButton{Name="ScheduledWageToggle",Text="查看工资扣款顺序",SizeFlagsHorizontal=Control.SizeFlags.ShrinkBegin};
        TownTheme.AccentButton(showTimeline,"财务",5);
        card.AddChild(showTimeline);
        var timeline=new VBoxContainer{Name="ScheduledWageTimeline",Visible=false};card.AddChild(timeline);
        showTimeline.Toggled+=visible=>timeline.Visible=visible;
        var currentPayments=Array.Empty<WagePayment>();
        var publicFunds=MarketLabel(card,"",16,TownTheme.Accent("自治"),wrap:true);
        publicFunds.Name="ScheduledPublicExpense";
        var publicCommitments=MarketLabel(card,"",color:TownTheme.Muted,wrap:true);
        var publicFood=MarketLabel(card,"",color:TownTheme.Muted,wrap:true);
        void Refresh()
        {
            var forecast=Game.UpcomingExpenses();
            string remainder=forecast.PrivateRemaining>=0?$"预计余额 {forecast.PrivateRemaining}":$"资金缺口 {-forecast.PrivateRemaining}";
            personal.Text=$"庄园金币 · 现有 {forecast.PrivateBalance} · 工资待付 {forecast.ScheduledPrivate} · {remainder}";
            wages.Text=forecast.Wages.Length==0?"未来一天暂无已排班的工资。":string.Join("\n",forecast.Wages.Select(wage=>
                $"{wage.Role} · {TownProjectClock(wage.FirstDue)} 起 · {wage.Shifts} 班 × {wage.PerShift} 金币"));
            var payments=WagePayments(forecast);
            var firstGap=Array.FindIndex(payments,payment=>payment.Remaining<0);
            wageRisk.Text=firstGap>=0?$"首个资金缺口 · {TownProjectClock(payments[firstGap].Due)} · 缺 {-payments[firstGap].Remaining:N0} 金币":
                payments.Length>0?$"预计可支付全部工资 · 下次扣款 {TownProjectClock(payments[0].Due)}":"";
            wageRisk.Visible=payments.Length>0;
            wageRisk.AddThemeColorOverride("font_color",firstGap>=0?new Color("873f35"):TownTheme.Leaf);
            showTimeline.Visible=payments.Length>0;
            timeline.Visible=payments.Length>0&&showTimeline.ButtonPressed;
            if(!currentPayments.SequenceEqual(payments)){
                currentPayments=payments;
                foreach(var child in timeline.GetChildren()){timeline.RemoveChild(child);child.QueueFree();}
                for(int i=0;i<payments.Length;i++){
                    var payment=payments[i];
                    string after=payment.Remaining<0?$"缺 {-payment.Remaining:N0} 金币":$"余 {payment.Remaining:N0} 金币";
                    var row=MarketCard(timeline,color:i==firstGap?"f2e0da":"faf7ec");
                    MarketLabel(row,$"{TownProjectClock(payment.Due)} · {payment.Roles} · −{payment.Cost:N0} 金币 · {after}",14,
                        payment.Remaining<0?new Color("873f35"):TownTheme.Ink,wrap:true);
                }
            }
            publicFunds.Text=$"公共金币 · 工程已拨款 {forecast.PublicCommitted} · 已排程待扣 0 · 预计余额 {forecast.PublicBalance}";
            publicCommitments.Text=forecast.Commitments.Length==0?"暂无公共工程占用资金或物资。":string.Join("\n",forecast.Commitments.Select(job=>{
                string kind=job.Kind switch{
                    "build"=>"建造","road"=>"铺路","open"=>"开放街区","crop"=>"耕种","ranch"=>"牧养",
                    "gather"=>"采集","trade"=>"贸易","harvest"=>"收割",_=>job.Kind
                };
                string budget=string.Join("、",new[]{job.Gold>0?$"{job.Gold} 金币":"",job.Materials}.Where(value=>value.Length>0));
                return $"{kind} · 预计 {TownProjectClock(job.Due)}{(budget.Length>0?$" · 已拨 {budget}":"")}";
            }));
            publicFood.Text=forecast.PublicFoodUsed>0
                ?$"公共粮食 · {TownProjectClock(forecast.PublicFoodDue)} 预计消耗 {forecast.PublicFoodUsed} 份 · 预计余 {forecast.PublicFoodBalance-forecast.PublicFoodUsed}"
                :$"公共粮食 · 现有 {forecast.PublicFoodBalance} 份";
        }
        Refresh();
        _refreshManagementPage+=Refresh;
        card.TreeExiting+=()=>_refreshManagementPage-=Refresh;
        return panel;
    }
}
