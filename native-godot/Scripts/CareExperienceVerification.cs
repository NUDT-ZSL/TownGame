using Godot;
using System;
using System.Linq;
using System.Text.Json.Nodes;

public partial class Main
{
    void VerifyCareExperience(Action<bool,string> check)
    {
        var snapshot=Data.Snapshot;
        var board=(_careWorkbenchClinic,_careTriageFilter,_careClinicFilter,_carePage);
        var view=(_selectedPet,_selection,_careSupplyRoute,_orderSupplyRoute);
        var previousBatch=Game.LastBatchCare.ToArray();
        bool suppressWrites=_firstDayGuideSuppressWrites;
        try{
            Data.Root["snapshot"]=snapshot.DeepClone();Data.Reindex();
            _firstDayGuideSuppressWrites=true;
            (_careWorkbenchClinic,_careTriageFilter,_careClinicFilter,_carePage)=(false,0,0,0);
            var pets=Data.Pets.OfType<JsonNode>().Where(Game.Active).ToArray();
            check(pets.Length>=2,"照护验收缺少两只在册伙伴");
            foreach(var pet in pets){
                pet["fostered"]=false;pet["health"]=90;pet["hunger"]=100;pet["cleanliness"]=100;
                pet["status"]="正常";pet["illness_type"]="";pet["injury"]=0;pet["pregnancy_due_ts"]=0;
            }
            var hungry=pets[0];var patient=pets[1];
            string first=NativeGame.S(hungry,"pet_id"),second=NativeGame.S(patient,"pet_id");
            hungry["hunger"]=10;hungry["cleanliness"]=15;
            patient["health"]=50;patient["illness_type"]="普通感染";patient["status"]="生病";patient["treatment_ready_ts"]=0;
            Game.Meta["coins"]=5000;
            Game.Meta["sim_ts"]=NativeGame.D(Game.Meta,"start_ts")+6*NativeGame.Day;
            Game.Meta["native_health_timeline"]=new JsonObject{{first,new JsonArray(Sample(1,30),Sample(5,65),Sample(6,80))}};

            ShowManagement("照护","care-worklist");
            var filter=Find<OptionButton>("CareTriageFilter");
            check(filter.GetItemText(1).EndsWith("· 1")&&filter.GetItemText(2).EndsWith("· 1")&&filter.GetItemText(3).EndsWith("· 1"),
                "优先照护未正确分类实际伤病、喂食和洗澡事项");
            Select("CareTriageFilter",2);
            Press("CareTriageOpen0");
            check(_tab=="宠物"&&_selectedPet==first&&_managementEntryTarget=="care:feed"&&_managementFocusTarget!=null,
                "优先照护未定位到所选伙伴的喂食操作");
            check(Find<Label>("PetHealthSummary").Text.Contains("最低健康 30"),"七日记录未包含早期数据");
            Select("PetHealthRange",0);
            check(Find<Label>("PetHealthSummary").Text.Contains("最低健康 65")&&
                !Find<Label>("PetHealthValues").Text.Contains("第 1 天"),"三日记录未按游戏日筛选并重算最低值");

            var hospital=Game.Rows("buildings").OfType<JsonNode>().FirstOrDefault(plot=>NativeGame.S(plot,"kind")=="宠物医院")
                ??Game.Rows("buildings").OfType<JsonNode>().First(plot=>NativeGame.B(plot,"unlocked")&&NativeGame.S(plot,"kind")=="");
            string oldKind=NativeGame.S(hospital,"kind");
            var oldLevel=hospital["level"]?.DeepClone();
            int hospitalId=NativeGame.I(hospital,"building_id"),home=NativeGame.I(patient,"building_id");
            hospital["kind"]="宠物医院";hospital["level"]=1;
            patient["building_id"]=hospitalId;
            Game.Rows("inventory").Clear();
            ShowManagement("照护","clinic-board");
            Select("CareClinicFilter",2);
            var capacity=Game.PetHospitalBeds().Single(item=>item.Plot==hospitalId);
            check(Find<Label>("CareClinicBeds").Text.Contains($"已占 {capacity.Used} / {capacity.Total}")&&
                Find<Label>("CareClinicBeds").Text.Contains(NativeGame.S(patient,"name"))&&
                Find<Button>("CareClinicOpen0").Text=="查看治疗","诊疗台未显示真实床位、在院名单或治疗状态");
            Press("CareClinicOpen0");
            check(_selectedPet==second&&_managementFocusTarget?.Name=="PetClinic","诊疗台未打开正确的住院伙伴");
            patient["building_id"]=home;
            hospital["kind"]=oldKind;hospital["level"]=oldLevel;

            const string order="verify-care-water";
            Game.Rows("orders").Add(new JsonObject{{"order_id",order},{"kind","item"},{"item","清水"},{"quantity",1},
                {"status","进行中"},{"created_ts",Game.Now},{"expiry_ts",Game.Now+NativeGame.Day},{"reward",20}});
            Game.Add("清水",1);
            Game.Execute("reserve_stock",new JsonObject{{"owner",NativeGame.OrderReservationOwner(order)},{"item","清水"},{"quantity",1}});
            OpenPetManagement(first,"care:bath");
            Press("PetCareSupply_bath");
            int estimate=Game.BuyPrice("清水")+Game.BuyPrice("沐浴露");
            check(Find<VBoxContainer>("CareSupplyChecklist").GetChildren().OfType<Label>().Any(label=>
                label.Text.Contains("清水")&&label.Text.Contains("可用 0 · 已预留 1"))&&
                Find<Label>("CareSupplyEstimate").Text.Contains($"预计 {estimate} 金币"),"补给清单没有区分预留库存或计算采购额");
            Game.Add("清水",1);Game.Add("沐浴露",1);
            _refreshManagementPage?.Invoke();
            check(Find<Button>("CareSupplyReturn").Text=="返回洗澡","补齐物资后未更新返回入口");
            Press("CareSupplyReturn");
            check(_selectedPet==first&&_managementEntryTarget=="care:bath","补齐后未返回原伙伴洗澡位置");

            patient["hunger"]=30;
            Game.Add("普通饲料",1);
            ShowManagement("照护");
            Press("PreviewBatchCare_feed");
            Find<CheckButton>("BatchCareChooseCompanions").ButtonPressed=true;
            Press("BatchCareSelectNone");
            check(Find<Button>("ConfirmBatchCare").Disabled,"空名单仍可确认照护");
            Find<CheckButton>($"BatchCarePet_{second}").ButtonPressed=true;
            string before=Data.Snapshot.ToJsonString();
            check(!Find<Button>("ConfirmBatchCare").Disabled&&Game.Stock("普通饲料")==1,"选中的伙伴没有重新获得可用的物资配额");
            Press("ConfirmBatchCare");
            check(Game.LastBatchCare.SequenceEqual(new[]{second})&&NativeGame.D(hungry,"hunger")==10&&
                NativeGame.D(patient,"hunger")>30&&Game.Stock("普通饲料")==0&&Data.Snapshot.ToJsonString()!=before,
                "批量照护没有严格执行选定名单");
            GD.Print("CARE_EXPERIENCE_VERIFIED triage_routes health_day_range clinic_roster selected_batch supply_context");
        }finally{
            _management?.Hide();
            Data.Root["snapshot"]=snapshot;Data.Reindex();
            (_careWorkbenchClinic,_careTriageFilter,_careClinicFilter,_carePage)=board;
            (_selectedPet,_selection,_careSupplyRoute,_orderSupplyRoute)=view;
            Game.LastBatchCare.Clear();Game.LastBatchCare.AddRange(previousBatch);
            _firstDayGuideSuppressWrites=suppressWrites;
        }

        static JsonObject Sample(int day,int health)=>new(){{"day",day},{"health",health},{"hunger",70},{"clean",80}};
        T Find<T>(string name) where T:Node=>(T)_managementBody!.FindChild(name,true,false)!;
        void Press(string name)=>Find<Button>(name).EmitSignal(BaseButton.SignalName.Pressed);
        void Select(string name,int selected){var menu=Find<OptionButton>(name);menu.Select(selected);menu.EmitSignal(OptionButton.SignalName.ItemSelected,(long)selected);}
    }
}
