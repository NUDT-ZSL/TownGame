using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;

public sealed partial class NativeGame
{
    void InitializePresentationData()
    {
        Meta["pet_appearances"]??=new JsonObject();Meta["pet_initial_appearances"]??=new JsonObject();
        Meta["style_favorites"]??=new JsonObject();Meta["finance_ledger"]??=new JsonArray();
        Meta["ledger_balance"]??=I(Meta,"coins");Meta["native_road_history"]??=new JsonArray();
        State["log_archive"]??=new JsonArray();State["order_archive"]??=new JsonArray();State["event_archive"]??=new JsonArray();
    }
    void RecordFinance(string reason)
    {
        int balance=I(Meta,"coins"),delta=balance-I(Meta,"ledger_balance",balance);
        if(delta!=0){var ledger=Meta["finance_ledger"]!.AsArray();ledger.Add(new JsonObject{{"ts",Now},{"delta",delta},{"balance",balance},{"reason",reason}});while(ledger.Count>3000)ledger.RemoveAt(0);}
        Meta["ledger_balance"]=balance;
    }
    void ArchiveOld(string table,string archive)
    {
        while(Rows(table).Count>30){var old=Rows(table).FirstOrDefault(r=>S(r!,"status") is not "进行中" and not "待处理");if(old==null)break;Rows(archive).Add(old.DeepClone());Rows(table).Remove(old);}
    }
    public int Appearance(JsonNode pet)
    {
        string id=S(pet,"pet_id");return Meta["pet_appearances"]?[id]?.GetValue<int>()??NativeAppearance.Default(id);
    }
    void Note(JsonNode pet,string text)
    {
        pet["history"]??=new JsonArray();pet["history"]!.AsArray().Add(new JsonObject{{"ts",Now},{"text",text}});
    }
    void NotePetChanges(JsonNode before,string message)
    {
        var previous=before["pets"]!.AsArray().ToDictionary(p=>S(p!,"pet_id"));
        foreach(var pet in Pets){if(!previous.TryGetValue(S(pet!,"pet_id"),out var old)){if(pet!["history"]!.AsArray().Count==0)Note(pet,"来到庄园，开始新的生活。");}else if(!JsonNode.DeepEquals(old,pet)&&pet!["history"]!.AsArray().Count==old!["history"]!.AsArray().Count)Note(pet!,message);}
    }
    public readonly System.Collections.Generic.List<string> LastBatchCare=new();
    string PresentationCommand(string action,JsonObject args)
    {
        if(action=="batch_care")return BatchCare(S(args,"kind"));
        if(action=="favorite_style"){
            string species=S(args,"species");int variant=I(args,"variant",-1);ValidateStyle(species,variant);
            var favorites=Meta["style_favorites"]!.AsObject();favorites[species]??=new JsonArray();var rows=favorites[species]!.AsArray();
            var found=rows.FirstOrDefault(n=>n!.GetValue<int>()==variant);
            if(found!=null){rows.Remove(found);return "已取消收藏";}
            if(rows.Count>=12)throw new InvalidOperationException("每个品种最多收藏12种外观");rows.Add(variant);return "外观已收藏";
        }
        var pet=Pet(S(args,"pet_id"),true);string id=S(pet,"pet_id");
        int style=action=="reset_appearance"?(Meta["pet_initial_appearances"]?[id]?.GetValue<int>()??NativeAppearance.Default(id)):I(args,"variant",-1);
        ValidateStyle(S(pet,"species"),style);
        Meta["pet_initial_appearances"]![id]??=Appearance(pet);Meta["pet_appearances"]![id]=style;
        Note(pet,action=="reset_appearance"?"恢复初始外观":"更换外观："+NativeAppearance.Description(S(pet,"species"),style));WorldRevision++;return "外观已更新";
    }
    void ValidateStyle(string species,int variant)
    {
        if(Rules["SPECIES"]?[species]==null||variant<0||variant>=384)throw new InvalidOperationException("请选择有效的动物外观");
    }
    string BatchCare(string kind)
    {
        if(!new[]{"feed","bath","play","collect"}.Contains(kind))throw new InvalidOperationException("不支持的批量照护");
        int completed=0;var skipped=new List<string>();LastBatchCare.Clear();
        var ids=Pets.Where(p=>Active(p!)).OrderBy(p=>kind=="feed"?D(p!,"hunger"):kind=="bath"?D(p!,"cleanliness"):0).Select(p=>S(p!,"pet_id")).ToArray();
        foreach(string id in ids){
            // 每只动物独立回滚；整批仍由外层Execute在写盘失败时一起回滚。
            var checkpoint=State.DeepClone();long rev=WorldRevision;
            try{
                var pet=Pet(id);if(kind=="feed"&&D(pet,"hunger")>=100||kind=="bath"&&D(pet,"cleanliness")>=100)throw new InvalidOperationException("无需照护");
                var a=new JsonObject{{"pet_id",id}};
                if(kind=="feed"){
                    string? food=Rules["ITEMS"]!.AsObject().Where(p=>p.Key!="腐败物"&&p.Value?["feed"]!=null&&Stock(p.Key)>0).Select(p=>p.Key).FirstOrDefault();
                    if(food==null)throw new InvalidOperationException("缺少可用食物");a["item"]=food;
                }
                Command(kind,a);completed++;LastBatchCare.Add(id);
            }catch(InvalidOperationException ex){root["snapshot"]=checkpoint;WorldRevision=rev;skipped.Add(id[..Math.Min(6,id.Length)]+"："+ex.Message);}
        }
        return $"批量完成{completed}只，跳过{skipped.Count}只。"+string.Join("；",skipped.Take(4));
    }
    public double PetAge(JsonNode pet)
    {
        double end=D(pet,"death_ts")>0?D(pet,"death_ts"):D(pet,"departure_ts")>0?D(pet,"departure_ts"):S(pet,"departure")!=""?D(pet,"last_update_ts"):Now;
        return Math.Max(0,end-D(pet,"birth_ts")-D(pet,"paused_seconds"));
    }
    public IReadOnlySet<Cell> ConnectedRoads=>Connected();
    public double LandValue(Cell cell)
    {
        if(cell.X<0||cell.Y<0||cell.X>=125||cell.Y>=80)return 0;
        string terrain=root["terrain"]![cell.Y]![cell.X]!.GetValue<string>();if(terrain!="grass"&&terrain!="zone")return 0;
        double value=15;var linked=Connected();
        if(Directions.Any(d=>linked.Contains(new(cell.X+d.X,cell.Y+d.Y))))value+=30;
        string zone=Zone(cell);if(zone!="")value+=ZoneLevel(zone)*8;
        foreach(var b in Rows("town_buildings")){
            double distance=Math.Abs(I(b!,"cx")+I(b!,"w")*.5-cell.X)+Math.Abs(I(b!,"cy")+I(b!,"h")*.5-cell.Y);
            if(distance>10)continue;string key=S(b!,"catalog_key");value+=(key.Contains("park")||key.Contains("square")?14:key.Contains("factory")?-12:4)*(1-distance/11);
        }
        return Math.Clamp(value,0,100);
    }
}
