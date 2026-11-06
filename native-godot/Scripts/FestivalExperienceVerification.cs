using Godot;
using System;
using System.Linq;
using System.Text.Json.Nodes;

public partial class Main
{
    void VerifyFestivalExperience(Action<bool,string> check)
    {
        var original=Data.Snapshot;
        var display=(_festivalShowCandidates,_festivalShowHistory,_selectedPet,_buildWorkspace,_buildDecorKind,_buildDecorTheme,
            _decorationCatalogQuery,_decorationBudgetFilter,_buildDecorationId,_decorationPlacedPage);
        Vector3 cameraCenter=_center;
        RoadAgent? hostActor=null;
        (Vector3 Position,bool Inside,bool Visible,int Index) hostState=default;
        try{
            Data.Root["snapshot"]=original.DeepClone();Data.Reindex();
            (_festivalShowCandidates,_festivalShowHistory)=(false,false);
            Game.SetZone("market",1);
            var market=Game.FestivalOccurrences(Game.Now).First(activity=>activity.Key=="market");
            Game.Meta["sim_ts"]=market.Opens-60;
            Game.Meta.Remove("native_festival_history");
            Game.Meta.Remove("native_festival_awards");
            Game.Meta["native_festival_decor"]=new JsonObject{{"kit_park_bench",2}};
            Game.Rows("events").Add(new JsonObject{{"event_id","festival_check_event"},{"kind","merchant"},
                {"text","测试旅行商人"},{"status","待处理"},{"expiry_ts",Game.Now+NativeGame.Hour},
                {"options",new JsonArray("欢迎商人","婉拒来访")}});

            ShowManagement("事件","festival_check_event");
            check(Find<Button>("TownEventPendingTab").ButtonPressed&&Find<Control>("TownPendingEvent_festival_check_event").Visible&&
                _managementFocusTarget is Label{Text:"测试旅行商人"},"事件直达没有进入待处理分区");
            Press("TownEventFestivalTab");
            check(Find<Button>("TownEventFestivalTab").ButtonPressed&&_managementEntryTarget==WeekendFestivalEntry&&
                _managementBody!.FindChild("TownPendingEventList",true,false)==null,"周末活动与待处理事件没有分区");
            check(Game.FestivalCompletions().Length==0&&Find<Label>("TownFestivalCredit_kit_park_bench").Text.Contains("2 件"),
                "旧奖励余额不正确或被误记为已完成记录");
            var foster=Data.Pets.OfType<JsonNode>().First(Game.Active);
            foster["name"]="测试展示候选";foster["fostered"]=true;
            Find<CheckButton>("TownFestivalCandidateToggle").ButtonPressed=true;
            var chooser=Find<OptionButton>("TownFestivalCandidates");
            int index=Enumerable.Range(0,chooser.ItemCount).First(i=>chooser.GetItemText(i).Contains("测试展示候选"));
            chooser.Select(index);chooser.EmitSignal(OptionButton.SignalName.ItemSelected,(long)index);
            check(Find<Label>("TownFestivalCandidateReason").Text.Contains("寄养中")&&!Game.FestivalShowPetEligible(foster),
                "展示候选没有说明真实报名限制");

            Game.Meta["sim_ts"]=market.Opens;
            Game.State["inventory"]=new JsonArray();
            Game.Jobs.Clear();Game.CancelFreight("活动验收");
            Game.AssignDaytimeDestinations();
            Vector3 site=FestivalSite("market")??throw new InvalidOperationException("活动验收缺少集市场地");
            var host=Game.People.OfType<JsonNode>().First(person=>NativeGame.S(person,"native_festival")=="market"&&
                Agents.Any(actor=>actor.Ident==NativeGame.S(person,"person_id")));
            hostActor=Agents.First(actor=>actor.Ident==NativeGame.S(host,"person_id"));
            hostState=(hostActor.Position,hostActor.InsideHome,hostActor.Visual.Visible,hostActor.Index);
            hostActor.InsideHome=false;hostActor.Visual.Show();hostActor.Index=hostActor.Path.Count;
            hostActor.Position=site+Vector3.Right*20;
            _center=site+Vector3.Right*120;
            ShowManagement("事件",WeekendFestivalEntry);
            var participate=Find<Button>("TownFestivalParticipate");
            check(participate.Disabled&&Find<Label>("TownFestivalCheckSite").Text.Contains("先前往")&&
                Find<Label>("TownFestivalCheckAdmission").Text.Contains("0 / 2")&&Find<Button>("TownFestivalSupply").Visible,
                "活动清单没有同时显示镜头距离及真实谷物缺口");
            _center=site;hostActor.Position=FestivalVisitPoint(host,site);
            Game.Add("谷物",2);
            _refreshManagementPage!.Invoke();
            check(!participate.Disabled&&Find<Label>("TownFestivalCheckHost").Text.Contains("已到场")&&
                Find<Label>("TownFestivalCheckAdmission").Text.Contains("2 / 2"),"清单没有随实际居民到场及库存更新");
            var args=new JsonObject{{"festival","market"},{"resident_id",hostActor.Ident}};
            Game.Execute("weekend_festival",args);
            bool duplicate=false;
            try{Game.Execute("weekend_festival",args);}catch(InvalidOperationException){duplicate=true;}
            ShowManagement("事件",WeekendFestivalEntry);
            Find<CheckButton>("TownFestivalHistoryToggle").ButtonPressed=true;
            var receipt=Game.FestivalCompletions().Single();
            var reloaded=new NativeGame(Data.Root.DeepClone().AsObject());
            check(duplicate&&receipt.Title==market.Title&&receipt.At==Game.Now&&receipt.Contribution.Contains("2 份谷物")&&
                reloaded.FestivalCompletions().Single()==receipt&&Find<Control>("TownFestivalHistory").GetChildCount()==1&&
                Find<Label>("TownFestivalCredit_"+market.Decoration).Text.Contains("1 件"),"活动明细或奖励余额没有随实际结算写入");
            Press("TownFestivalOpenDecoration_"+market.Decoration);
            check(_tab=="建造"&&_managementEntryTarget=="decor:"+market.Decoration&&_buildWorkspace==3&&_buildDecorKind==market.Decoration,
                "活动奖励未直达装饰目录中的对应项目");
            GD.Print("FESTIVAL_EXPERIENCE_VERIFIED event_sections participation_checks candidate_reasons decoration_credit completed_history");
        }finally{
            _management?.Hide();
            Data.Root["snapshot"]=original;Data.Reindex();
            if(hostActor!=null){
                (hostActor.Position,hostActor.InsideHome,hostActor.Index)=(hostState.Position,hostState.Inside,hostState.Index);
                hostActor.Visual.Visible=hostState.Visible;
            }
            _center=cameraCenter;
            (_festivalShowCandidates,_festivalShowHistory,_selectedPet,_buildWorkspace,_buildDecorKind,_buildDecorTheme,
                _decorationCatalogQuery,_decorationBudgetFilter,_buildDecorationId,_decorationPlacedPage)=display;
        }

        T Find<T>(string name) where T:Node=>(T)_managementBody!.FindChild(name,true,false)!;
        void Press(string name)=>Find<Button>(name).EmitSignal(BaseButton.SignalName.Pressed);
    }
}
