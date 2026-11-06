using Godot;
using System;
using System.Linq;
using System.Text.Json.Nodes;

public partial class Main
{
    void VerifyArchiveExperience(Action<bool,string> check)
    {
        var original=Data.Snapshot;
        var archive=(_archiveQuery,_archiveStatus,_archiveSort,_archivePage,_petTimelineQuery,_petTimelinePage,
            _archiveSpeciesQuery,_archiveSpeciesFilter,_archiveSpecies,_archiveRecordQuery,_archiveRecordKind,
            _archiveRecordStatus,_archiveRecordPage,_archiveOpenRecord,_marketSection);
        try{
            Data.Root["snapshot"]=original.DeepClone();Data.Reindex();
            (_archiveQuery,_archiveStatus,_archiveSort,_archivePage)=("",0,0,0);
            (_archiveSpeciesQuery,_archiveSpeciesFilter,_archiveSpecies)=("",0,"");
            (_archiveRecordQuery,_archiveRecordKind,_archiveRecordStatus,_archiveRecordPage,_archiveOpenRecord)=("",0,0,0,"");
            var pets=Data.Pets.OfType<JsonNode>().Take(2).ToArray();
            check(pets.Length==2,"档案验收缺少伙伴");
            var pet=pets[0];
            string id=NativeGame.S(pet,"pet_id"),species=NativeGame.S(pet,"species"),oldId=NativeGame.S(pets[1],"pet_id");
            pet["name"]="足迹验收伙伴";
            pet["history"]!.AsArray().Add(new JsonObject{{"ts",Game.Now},{"text","去柳岸公园散步"}});
            pets[1]["departure"]="出售";pets[1]["death_ts"]=0;pets[1].AsObject().Remove("departure_ts");
            Game.State["codex"]![species]=new JsonObject{{"rare",true},{"natural_death",true}};
            var order=new JsonObject{{"order_id","archive_check"},{"npc","验收邮差"},{"kind","checkin"},{"status","进行中"},
                {"created_ts",Game.Now-60},{"expiry_ts",Game.Now+NativeGame.Hour},{"reward",19},{"reputation",2}};
            Game.Rows("orders").Add(order);
            Game.Execute("order",new JsonObject{{"order_id","archive_check"}});
            var evt=new JsonObject{{"event_id","archive_event"},{"kind","merchant"},{"text","验收旅行商人来访"},
                {"status","待处理"},{"created_ts",Game.Now-60},{"expiry_ts",Game.Now+NativeGame.Hour},{"options",new JsonArray("欢迎商人","婉拒来访")}};
            Game.Rows("events").Add(evt);
            Game.Execute("event",new JsonObject{{"event_id","archive_event"},{"option",1}});
            Game.Rows("event_archive").Add(new JsonObject{{"event_id","archive_old_event"},{"text","验收早期事件"},{"status","过期"},
                {"created_ts",Game.Now-300},{"expiry_ts",Game.Now-200}});
            check(NativeGame.D(order,"resolved_ts")==Game.Now&&NativeGame.I(evt,"resolved_option",-1)==1,"实际结算未记录时间或选项");

            ShowManagement("档案","companions");
            check(Find<Control>("ArchiveRecordRows")==null&&Find<Button>("ArchiveSection_companions")!.ButtonPressed,
                "档案分区混入了别的页面");
            Type("ArchivePetQuery",$"足迹验收 {species}");
            check(Find<Button>("ArchivePet_"+id)!=null&&Find<Button>("ArchivePet_"+oldId)==null,"伙伴搜索没有匹配名字与物种");
            Press("ArchivePet_"+id);
            check(_managementEntryTarget=="companion:"+id&&Find<Button>("PetPhotoAlbum")!=null,"伙伴详情没有保留相册入口");
            check(TextIn("ArchiveTimelineRows","出生"),"足迹未显示原有出生时间");
            Type("ArchiveTimelineQuery","公园 散步");
            check(TextIn("ArchiveTimelineRows","去柳岸公园散步")&&!TextIn("ArchiveTimelineRows","出生"),"足迹未按文字缩小范围");
            GoBackInManagement();
            check(Find<LineEdit>("ArchivePetQuery")!.Text.Contains("足迹验收"),"从详情返回未恢复名册搜索");
            Type("ArchivePetQuery","");Choose("ArchivePetStatus",3);
            check(Find<Button>("ArchivePet_"+oldId)!=null&&Find<Button>("ArchivePet_"+id)==null,"离园筛选混入其他伙伴");
            Press("ArchivePet_"+oldId);Type("ArchiveTimelineQuery","离园");
            check(TextIn("ArchiveTimelineRows","时间未记录")&&TextIn("ArchiveTimelineRows","出售"),"旧档案没有保留时间缺失状态");

            Press("ArchiveSection_codex");Choose("ArchiveSpeciesFilter",4);Type("ArchiveSpeciesQuery",species);
            check(Find<Button>("ArchiveSpecies_"+species)!=null&&Find<Label>("ArchiveSpeciesFacts")!.Text.Contains("寿终记录已收录"),
                "图鉴未读取已存的寿终记录");
            Press("ArchiveSpeciesShop");
            check(_tab=="商店"&&_managementEntryTarget=="companion-shop:"+species&&_managementFocusTarget!=null,"图鉴未跳到商店对应物种");
            GoBackInManagement();Press("ArchiveSection_records");
            Type("ArchiveRecordQuery","验收邮差");
            var toggle=Find<Button>("ArchiveRecord_order_archive_check")!;toggle.ButtonPressed=true;
            var reward=Find<Label>("ArchiveRecordDetails_order_archive_check")!;
            check(reward.Visible&&reward.Text.Contains("19 金币")&&reward.Text.Contains("2 声望"),"已完成订单明细没有实际奖励");
            Type("ArchiveRecordQuery","验收");Choose("ArchiveRecordKind",2);Choose("ArchiveRecordStatus",1);
            Find<Button>("ArchiveRecord_event_archive_event")!.ButtonPressed=true;
            check(TextIn("ArchiveRecordRows","婉拒来访")&&Find<Button>("ArchiveRecord_event_archive_old_event")==null,
                "事件过滤或已选处理方式显示错误");
            Choose("ArchiveRecordStatus",2);
            check(Find<Label>("ArchiveRecordDetails_event_archive_old_event")!.Text.Contains("未记录"),"旧事件生成了无来源的选项");
            GD.Print("ARCHIVE_EXPERIENCE_VERIFIED sections roster_search timeline legacy_dates codex_drilldown record_outcomes");
        }finally{
            Data.Root["snapshot"]=original;Data.Reindex();_management?.Hide();
            (_archiveQuery,_archiveStatus,_archiveSort,_archivePage,_petTimelineQuery,_petTimelinePage,
                _archiveSpeciesQuery,_archiveSpeciesFilter,_archiveSpecies,_archiveRecordQuery,_archiveRecordKind,
                _archiveRecordStatus,_archiveRecordPage,_archiveOpenRecord,_marketSection)=archive;
        }

        T? Find<T>(string name) where T:Node=>_managementBody!.FindChild(name,true,false) as T;
        void Press(string name)=>Find<Button>(name)!.EmitSignal(BaseButton.SignalName.Pressed);
        void Type(string name,string text){var line=Find<LineEdit>(name)!;line.Text=text;line.EmitSignal(LineEdit.SignalName.TextChanged,text);}
        void Choose(string name,int index){var menu=Find<OptionButton>(name)!;menu.Select(index);menu.EmitSignal(OptionButton.SignalName.ItemSelected,(long)index);}
        bool TextIn(string container,string text)=>Find<Control>(container)!.FindChildren("*","Label",true,false).OfType<Label>()
            .Any(label=>label.Visible&&label.Text.Contains(text,StringComparison.Ordinal));
    }
}
