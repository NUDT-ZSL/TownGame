using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json.Nodes;

public static class NativeMigrationVerification
{
    public static int Run(JsonObject assets)
    {
        int count=0;
        JsonObject Fixture(){var root=new JsonObject();foreach(string key in new[]{"version","snapshot","rules","terrain","zones","portals"})root[key]=assets[key]!.DeepClone();return root;}
        void Check(bool good,string why){if(!good)throw new Exception("MIGRATION_TEST_FAILED: "+why);}
        void Test(string name,Action<NativeGame,JsonObject> run){var root=Fixture();var game=new NativeGame(root);run(game,root);game.Validate();count++;Godot.GD.Print("MIGRATION_OK "+name);}
        Test("legacy_appearance_hash",(g,r)=>{Check(NativeAppearance.Default("test")==28&&NativeAppearance.Default("native-pet-001")==296&&NativeAppearance.Default(new string('a',150))==313,"旧外观身份散列不兼容");});
        Test("appearance_favorite_reset_persist",(g,r)=>{var pet=g.Pets[0]!;string id=NativeGame.S(pet,"pet_id");int initial=g.Appearance(pet);g.Execute("pet_appearance",new JsonObject{{"pet_id",id},{"variant",383}});g.Execute("favorite_style",new JsonObject{{"species",NativeGame.S(pet,"species")},{"variant",383}});var reload=new NativeGame(r.DeepClone().AsObject());Check(reload.Appearance(reload.Pets[0]!)==383,"重载外观丢失");g.Execute("reset_appearance",new JsonObject{{"pet_id",id}});Check(g.Appearance(pet)==initial,"不能恢复初始外观");});
        Test("appearance_invalid_atomic",(g,r)=>{string before=g.State.ToJsonString();try{g.Execute("pet_appearance",new JsonObject{{"pet_id",NativeGame.S(g.Pets[0]!,"pet_id")},{"variant",384}});throw new Exception("非法样式未拒绝");}catch(InvalidOperationException){}Check(before==g.State.ToJsonString(),"外观失败未回滚");});
        Test("batch_partial_resources_no_rotten",(g,r)=>{g.Rows("inventory").Clear();g.Add("普通饲料",1);g.Add("腐败物",5);foreach(var p in g.Pets)p!["hunger"]=10;var hungry=g.Pets[0]!;hungry["hunger"]=5;g.Execute("batch_care",new JsonObject{{"kind","feed"}});Check(g.LastBatchCare.Count==1&&g.Stock("普通饲料")==0&&g.Stock("腐败物")==5,"批量喂食越过库存或使用腐败物");Check(NativeGame.D(g.Pets[0]!,"hunger")==35,"没有优先照顾最饿的动物");});
        Test("batch_foster_cooldown_and_disk_rollback",(g,r)=>{foreach(var p in g.Pets)p!["interact_ready_ts"]=g.Now+3600;g.Pets[0]!["fostered"]=true;g.Execute("batch_care",new JsonObject{{"kind","play"}});Check(g.LastBatchCare.Count==0,"冷却或寄养中仍互动");string before=g.State.ToJsonString();try{g.Execute("batch_care",new JsonObject{{"kind","feed"}},()=>throw new IOException("disk"));}catch(IOException){}Check(g.State.ToJsonString()==before,"整批写盘失败后未恢复库存和属性");});
        Test("finance_atomic_and_history",(g,r)=>{g.Meta["ledger_balance"]=NativeGame.I(g.Meta,"coins");int rows=g.Meta["finance_ledger"]!.AsArray().Count;g.Execute("buy",new JsonObject{{"item","清水"},{"quantity",3}});var entry=g.Meta["finance_ledger"]!.AsArray().Last()!;Check(NativeGame.I(entry,"delta")==-6&&g.Meta["finance_ledger"]!.AsArray().Count==rows+1,"收支数额错误");string before=g.State.ToJsonString();try{g.Execute("buy",new JsonObject{{"item","清水"}},()=>throw new IOException("disk"));}catch(IOException){}Check(g.State.ToJsonString()==before,"写盘失败遗留财务记录");});
        Test("archives_and_frozen_age",(g,r)=>{var pet=g.Pets[0]!;pet["departure"]="出售";pet["last_update_ts"]=g.Now;double age=g.PetAge(pet);g.Commons["enabled"]=false;g.Advance(3600);Check(g.PetAge(pet)==age,"离园档案仍增长年龄");for(int i=0;i<305;i++)g.Log("archive-"+i);Check(g.Rows("log_archive").Count>=5&&g.Rows("logs").Count==300,"旧日志被丢弃");});
        Test("road_stroke_atomic_and_multilevel_reload",(g,r)=>{
            g.Meta["coins"]=50000;var roads=g.Rows("roads").Take(3).ToArray();var cells=new JsonArray(roads.Select(c=>(JsonNode)new JsonArray(NativeGame.I(c!,"cx"),NativeGame.I(c!,"cy"))).ToArray());var original=roads.Select(c=>c!.DeepClone()).ToArray();
            int coins=NativeGame.I(g.Meta,"coins");g.Execute("road_stroke",new JsonObject{{"cells",cells},{"style","stone"}});Check(NativeGame.I(g.Meta,"coins")==coins-42,"整笔费用错误");g.Execute("road_stroke",new JsonObject{{"cells",cells.DeepClone()},{"style","garden"}});
            var restored=new NativeGame(r.DeepClone().AsObject());restored.Execute("undo_road",new JsonObject());restored.Execute("undo_road",new JsonObject());Check(NativeGame.I(restored.Meta,"coins")==coins,"重载后多步撤销没有恢复费用");
            string before=g.State.ToJsonString();try{g.Execute("road_stroke",new JsonObject{{"cells",new JsonArray(cells[0]!.DeepClone(),new JsonArray(-1,-1))},{"style","asphalt"}});throw new Exception("部分笔画成功");}catch(InvalidOperationException){}Check(before==g.State.ToJsonString(),"非法笔画未整体回滚");
        });
        Test("road_line_and_automatic_quote",(g,r)=>{var line=NativeGame.RoadLine(new(1,2),new(5,6));Check(line.Count==9&&line.Zip(line.Skip(1)).All(p=>Math.Abs(p.First.X-p.Second.X)+Math.Abs(p.First.Y-p.Second.Y)==1),"笔画存在跳格");var entry=g.Portal("estate");Check(g.AutoRoadPlan(entry).Count==0,"已接通入口仍收费修路");});
        Test("land_value_road_access",(g,r)=>{var c=g.Portal("estate");double value=g.LandValue(c);Check(value>=0&&value<=100&&g.LandValue(new(-1,-1))==0,"地价越界");});
        if(OperatingSystem.IsWindows()){
            string folder=Godot.ProjectSettings.GlobalizePath("res://../.test-output/legacy-matrix-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
            for(int version=1;version<=6;version++){
                string file=Path.Combine(folder,"v"+version+".db");WriteFixture(file,assets["snapshot"]!.AsObject(),version);
                var hash=SHA256.HashData(File.ReadAllBytes(file));var imported=LegacyImport.Read(file,assets);
                Check(hash.SequenceEqual(SHA256.HashData(File.ReadAllBytes(file))),"导入修改了原数据库");
                Check(imported["pets"]!.AsArray().Count==assets["snapshot"]!["pets"]!.AsArray().Count&&NativeGame.I(imported["meta"]!,"coins")==NativeGame.I(assets["snapshot"]!["meta"]!,"coins"),"旧档核心数据丢失");
                count++;Godot.GD.Print("MIGRATION_OK sqlite_v"+version+"_readonly");
            }
            string live=Path.Combine(folder,"wal.db");WriteFixture(live,assets["snapshot"]!.AsObject(),6);
            using(var db=new LegacySqlite(live,false)){db.Query("PRAGMA journal_mode=WAL");db.Query("UPDATE meta SET value='7777' WHERE key='coins'");var imported=LegacyImport.Read(live,assets);Check(NativeGame.I(imported["meta"]!,"coins")==7777,"未读取WAL中已提交数据");}
            count++;Godot.GD.Print("MIGRATION_OK sqlite_wal_snapshot");
            string future=Path.Combine(folder,"future.db");WriteFixture(future,assets["snapshot"]!.AsObject(),6);using(var db=new LegacySqlite(future,false))db.Query("PRAGMA user_version=99");var bytes=File.ReadAllBytes(future);try{LegacyImport.Read(future,assets);throw new Exception("未来版本未拒绝");}catch(InvalidDataException){}Check(bytes.SequenceEqual(File.ReadAllBytes(future)),"拒绝导入时修改原文件");count++;Godot.GD.Print("MIGRATION_OK future_version_rejected");
            string json=Path.Combine(folder,"native.json");File.WriteAllText(json,new JsonObject{{"version",1},{"snapshot",assets["snapshot"]!.DeepClone()}}.ToJsonString());var snapshot=LegacyImport.Read(json,assets);LegacyImport.SaveImported(snapshot,json);Check(Directory.GetFiles(folder,"native.json.before-import-*.json").Length==1,"导入前没有备份");count++;Godot.GD.Print("MIGRATION_OK json_import_and_backup");
        }
        return count;
    }
    static string Sql(string value)=>"'"+value.Replace("'","''")+"'";
    static void WriteFixture(string file,JsonObject state,int version)
    {
        using var db=new LegacySqlite(file,false);db.Query("BEGIN");db.Query("PRAGMA user_version="+version);db.Query("CREATE TABLE meta(key TEXT PRIMARY KEY,value TEXT NOT NULL)");
        foreach(var pair in state["meta"]!.AsObject())if(!pair.Key.StartsWith("native_")&&(version>=5||pair.Key!="road_revision"))db.Query("INSERT INTO meta VALUES("+Sql(pair.Key)+","+Sql(pair.Value?.ToJsonString()??"null")+")");
        var source=state["pets"]!.AsArray();var keys=source[0]!.AsObject().Select(p=>p.Key).Where(k=>version>=3||!new[]{"illness_type","injury","treatment_ready_ts","fight_ready_ts","next_excretion_ts"}.Contains(k)).ToArray();
        db.Query("CREATE TABLE pets("+string.Join(',',keys.Select(k=>'"'+k+'"'))+")");
        foreach(var pet in source)db.Query("INSERT INTO pets VALUES("+string.Join(',',keys.Select(k=>pet![k] is JsonArray||pet[k] is JsonObject?Sql(pet[k]!.ToJsonString()):pet[k] is JsonValue v&&v.TryGetValue<bool>(out bool b)?b?"1":"0":pet[k] is JsonValue s&&s.TryGetValue<string>(out string? str)?Sql(str):pet[k]?.ToJsonString()??"NULL"))+")");
        foreach(string table in new[]{"buildings","inventory","events","orders","waste","town_zones","people","roads","town_buildings"}){
            if(version<4&&new[]{"town_zones","people"}.Contains(table)||version<5&&new[]{"roads","town_buildings"}.Contains(table)||version<6&&table=="waste")continue;
            db.Query("CREATE TABLE "+table+"(data TEXT)");foreach(var row in state[table]!.AsArray())db.Query("INSERT INTO "+table+" VALUES("+Sql(row!.ToJsonString())+")");
        }
        db.Query("COMMIT");
    }
}
