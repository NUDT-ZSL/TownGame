using Godot;
using System;
using System.Linq;
using System.Text.Json.Nodes;

public partial class Main
{
    void VerifyJournalExperience(Action<bool,string> check)
    {
        var original=Data.Snapshot;
        var journal=(_journalView,_journalFilter,_journalPage,_townCalendarOpen,_townCalendarDate,_townCalendarCategory);
        var camera=(_center,_distance,_pitch,_buildCamera,_portraitAnchor,_previousCameraSite,_selection);
        bool suppressWrites=_firstDayGuideSuppressWrites;
        try{
            Data.Root["snapshot"]=original.DeepClone();
            Data.Reindex();
            _firstDayGuideSuppressWrites=true;
            _journalView=JournalView.Pending;
            _journalFilter=_townCalendarCategory="";
            _journalPage=0;
            _townCalendarOpen=true;
            _townCalendarDate=double.NaN;
            Game.Rows("orders").Clear();
            Game.Rows("events").Clear();
            double today=Math.Floor(Game.Now/NativeGame.Day)*NativeGame.Day;
            AddOrder("journal-experience-urgent","短期验收",Game.Now+.5*NativeGame.Hour);
            AddOrder("journal-experience-future","明日日程验收",today+NativeGame.Day+15*NativeGame.Hour);
            var pet=Game.Pets.OfType<JsonNode>().First(row=>Game.Active(row)&&!Game.IsPetInHospital(row));
            pet["status"]="健康";pet["health"]=100;pet["hunger"]=100;pet["cleanliness"]=100;
            pet["fostered"]=false;pet["illness_type"]="";pet["injury"]=0;
            string petId=NativeGame.S(pet,"pet_id");
            Game.Meta["native_hatches"]=new JsonArray(new JsonObject{{"pet_id",petId},{"name","孵化验收"},{"when",Game.Now}});

            ShowManagement("今日待办");
            var days=Find<OptionButton>("TownCalendarDay");
            check(days.ItemCount==7&&days.Selected==0&&Find<Button>("TownCalendarPrevious").Disabled,
                "周历未以今天作为七天范围的起点");
            Press("TownCalendarNext");
            var calendar=Find<VBoxContainer>("TownCalendarEntries");
            check(days.Selected==1&&days.GetItemText(1).Contains("明天")&&HasLabel(calendar,"明日日程验收")&&HasLabel(calendar,"15:00"),
                "后一天没有显示真实日期和订单截止时间");
            Select("TownCalendarCategory",3);
            check(!HasLabel(calendar,"明日日程验收"),"周历工资筛选仍显示订单");
            Press("TownCalendarToday");
            check(days.Selected==0&&Find<Button>("TownCalendarToday").Disabled,"周历无法回到今天");

            Select("JournalScope",(int)JournalView.Urgent);
            check(Find<Control>("JournalCard_订单_journal-experience-urgent")!=null&&
                Find<Control>("JournalCard_订单_journal-experience-future")==null&&Find<Button>("JournalOpenUrgent").Visible,
                "优先视图没有正确区分临期和未来订单");
            Select("JournalScope",(int)JournalView.Pending);
            Press("JournalFilter_宠物");
            var hatchCard=Find<PanelContainer>($"JournalCard_宠物_{petId}");
            check(hatchCard?.FindChild("DismissHatchNotice",true,false) is Button {Visible:true},"孵化消息未提供已读按钮");
            ((Button)hatchCard!.FindChild("DismissHatchNotice",true,false)!).EmitSignal(BaseButton.SignalName.Pressed);
            check(!_journalEntries.Any(entry=>entry.Page=="宠物"&&entry.Id==petId)&&Game.ReviewedJournalNotices().Any(notice=>notice.Id==petId),
                "孵化已读消息未从待办移到已读历史");
            Select("JournalScope",(int)JournalView.Reviewed);
            check(Find<PanelContainer>($"JournalCard_宠物_{petId}") is { } reviewed&&HasLabel(reviewed,"孵化验收")&&
                reviewed.FindChild("DismissHatchNotice",true,false) is Button {Visible:false},"已读页未显示历史消息或仍允许重复确认");
            var root=new JsonObject();
            foreach(string key in new[]{"version","snapshot","rules","terrain","zones","portals"})root[key]=Data.Root[key]!.DeepClone();
            var reloaded=new NativeGame(root);
            check(reloaded.ReviewedJournalNotices().Any(notice=>notice.Id==petId)&&
                reloaded.RecentHatches().Any(row=>NativeGame.S(row,"pet_id")==petId&&NativeGame.B(row,"journal_read")),"已读记录无法随存档重载");

            var future=Game.FestivalOccurrences(Game.Now).First(activity=>activity.Opens>Game.Now);
            ShowManagement("事件","weekend-festivals");
            check(HasLabel(Find<PanelContainer>("TownFestivalUpcoming"),future.Title)&&
                Find<Label>("TownFestivalParticipation").Text.Length>0,"节庆页面未分开显示当前状态和未来日程");
            Press($"TownFestivalPreview_{future.Key}_0");
            check(_management?.Visible==false&&_selection.Contains(future.Title)&&_distance==58,"未来活动的地点预览没有定位场景");
            Game.Meta["sim_ts"]=future.Opens+60;
            Game.SetZone(future.Zone,0);
            ShowManagement("事件","weekend-festivals");
            check(Find<Label>("TownFestivalParticipation").Text.Contains("开放活动所在街区")&&
                !Find<Button>("TownFestivalParticipate").Visible,"活动条件不足时未解释原因或仍可参加");
            GD.Print("JOURNAL_EXPERIENCE_VERIFIED dated_calendar filters urgent_scope reviewed_reload festival_preview_blockers");
        }finally{
            _management?.Hide();
            Data.Root["snapshot"]=original;
            Data.Reindex();
            (_journalView,_journalFilter,_journalPage,_townCalendarOpen,_townCalendarDate,_townCalendarCategory)=journal;
            (_center,_distance,_pitch,_buildCamera,_portraitAnchor,_previousCameraSite,_selection)=camera;
            _firstDayGuideSuppressWrites=suppressWrites;
            UpdateJournalBadge();
        }

        void AddOrder(string id,string owner,double expires)=>Game.Rows("orders").Add(new JsonObject{
            {"order_id",id},{"npc",owner},{"kind","checkin"},{"quantity",1},{"created_ts",Game.Now},
            {"expiry_ts",expires},{"status","进行中"},{"reward",8}});
        T Find<T>(string name) where T:Node=>(T)_managementBody!.FindChild(name,true,false)!;
        void Press(string name)=>Find<Button>(name).EmitSignal(BaseButton.SignalName.Pressed);
        void Select(string name,int index)=>Find<OptionButton>(name).EmitSignal(OptionButton.SignalName.ItemSelected,index);
        static bool HasLabel(Node node,string text)=>node.FindChildren("*","Label",true,false).OfType<Label>()
            .Any(label=>label.Text.Contains(text,StringComparison.Ordinal));
    }
}
