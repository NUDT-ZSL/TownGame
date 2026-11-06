using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;

public sealed partial class NativeGame
{
    internal sealed record WageForecast(string Role,double FirstDue,int Shifts,int PerShift,double Interval)
    {
        public int Total=>Shifts*PerShift;
    }
    internal sealed record PublicCommitment(string Kind,double Due,int Gold,string Materials);
    internal sealed record ExpenseForecast(int PrivateBalance,WageForecast[] Wages,int PublicBalance,
        PublicCommitment[] Commitments,double PublicFoodDue,int PublicFoodUsed,int PublicFoodBalance)
    {
        public int ScheduledPrivate=>Wages.Sum(wage=>wage.Total);
        public int PrivateRemaining=>PrivateBalance-ScheduledPrivate;
        public int PublicCommitted=>Commitments.Sum(job=>job.Gold);
    }

    double NextTownTick(double due)
    {
        double nextHour=Math.Max(Now,D(Meta,"native_next_hour",Now+Hour));
        return nextHour+Math.Max(0,Math.Ceiling((due-nextHour-1e-6)/Hour))*Hour;
    }

    WageForecast[] UpcomingWages(double horizon)
    {
        var wages=new List<WageForecast>();
        void AddWage(string role,double due,double interval,int fee)
        {
            if(due>horizon||fee<=0)return;
            interval=Math.Max(1,interval);
            int shifts=(int)Math.Floor((horizon-due)/interval)+1;
            wages.Add(new(role,Math.Max(Now,due),shifts,fee,interval));
        }
        if(B(Meta,"farm_manager"))
            AddWage("农场管理员",D(Meta,"manager_next_wage_ts",Now),
                Rule("MANAGER_WAGE_INTERVAL",6*Hour),I(Rules,"MANAGER_WAGE"));
        if(B(Meta,"caretaker"))
            AddWage("饲养员",NextTownTick(D(Meta,"next_care_ts",Now)),
                Math.Max(Hour,Math.Ceiling(Rule("CARE_INTERVAL",3*Hour)/Hour)*Hour),I(Rules,"CARE_WAGE",8));
        return wages.OrderBy(wage=>wage.FirstDue).ToArray();
    }

    internal ExpenseForecast UpcomingExpenses()
    {
        double horizon=Now+Day;
        var commitments=Jobs.OfType<JsonNode>().OrderBy(job=>D(job,"due")).Select(job=>{
            var cost=job["cost"]!.AsObject();
            string materials=string.Join("、",cost.Where(pair=>pair.Key!="funds").Select(pair=>
                $"{PublicExpenseMaterial(pair.Key)} ×{pair.Value!.GetValue<int>()}"));
            return new PublicCommitment(S(job,"kind"),D(job,"due"),I(cost,"funds"),materials);
        }).ToArray();
        int food=I(PublicStock,"food");
        double mealDue=B(Commons,"enabled")?NextTownTick((I(Commons,"last_day")+1)*Day):0;
        int foodUsed=mealDue>0&&mealDue<=horizon
            ?Math.Min(food,People.Count(person=>S(person!,"role")=="居民")*
                Math.Max(1,(int)(mealDue/Day)-I(Commons,"last_day")))
            :0;
        return new(I(Meta,"coins"),UpcomingWages(horizon),I(Commons,"funds"),
            commitments,foodUsed>0?mealDue:0,foodUsed,food);
    }

    static string PublicExpenseMaterial(string key)=>key switch{
        "food"=>"粮食","seed"=>"种子","wood"=>"木材","stone"=>"石材","feed"=>"饲料",_=>key
    };
}
