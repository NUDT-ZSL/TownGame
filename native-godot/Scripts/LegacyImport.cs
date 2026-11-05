using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json.Nodes;

// Windows自带SQLite。只读事务包括quick_check、元数据和所有表，读取同一WAL快照。
public sealed class LegacySqlite : IDisposable
{
    IntPtr _db;
    [DllImport("winsqlite3",CallingConvention=CallingConvention.Cdecl)]static extern int sqlite3_open_v2([MarshalAs(UnmanagedType.LPUTF8Str)]string file,out IntPtr db,int flags,IntPtr vfs);
    [DllImport("winsqlite3",CallingConvention=CallingConvention.Cdecl)]static extern int sqlite3_close_v2(IntPtr db);
    [DllImport("winsqlite3",CallingConvention=CallingConvention.Cdecl)]static extern int sqlite3_busy_timeout(IntPtr db,int timeout);
    [DllImport("winsqlite3",CallingConvention=CallingConvention.Cdecl)]static extern IntPtr sqlite3_errmsg(IntPtr db);
    [DllImport("winsqlite3",CallingConvention=CallingConvention.Cdecl)]static extern int sqlite3_prepare_v2(IntPtr db,[MarshalAs(UnmanagedType.LPUTF8Str)]string sql,int length,out IntPtr stmt,IntPtr tail);
    [DllImport("winsqlite3",CallingConvention=CallingConvention.Cdecl)]static extern int sqlite3_step(IntPtr stmt);
    [DllImport("winsqlite3",CallingConvention=CallingConvention.Cdecl)]static extern int sqlite3_finalize(IntPtr stmt);
    [DllImport("winsqlite3",CallingConvention=CallingConvention.Cdecl)]static extern int sqlite3_column_count(IntPtr stmt);
    [DllImport("winsqlite3",CallingConvention=CallingConvention.Cdecl)]static extern IntPtr sqlite3_column_name(IntPtr stmt,int index);
    [DllImport("winsqlite3",CallingConvention=CallingConvention.Cdecl)]static extern int sqlite3_column_type(IntPtr stmt,int index);
    [DllImport("winsqlite3",CallingConvention=CallingConvention.Cdecl)]static extern long sqlite3_column_int64(IntPtr stmt,int index);
    [DllImport("winsqlite3",CallingConvention=CallingConvention.Cdecl)]static extern double sqlite3_column_double(IntPtr stmt,int index);
    [DllImport("winsqlite3",CallingConvention=CallingConvention.Cdecl)]static extern IntPtr sqlite3_column_text(IntPtr stmt,int index);
    public LegacySqlite(string path,bool readOnly=true)
    {
        if(readOnly&&!File.Exists(path))throw new FileNotFoundException("旧存档不存在",path);
        if(sqlite3_open_v2(Path.GetFullPath(path),out _db,readOnly?1:6,IntPtr.Zero)!=0){string error=Error();Dispose();throw new InvalidDataException(error);}
        sqlite3_busy_timeout(_db,5000);
    }
    string Error()=>Marshal.PtrToStringUTF8(sqlite3_errmsg(_db))??"SQLite读取失败";
    public List<JsonObject> Query(string sql)
    {
        if(sqlite3_prepare_v2(_db,sql,-1,out var stmt,IntPtr.Zero)!=0)throw new InvalidDataException(Error());
        try{
            var rows=new List<JsonObject>();int result;
            while((result=sqlite3_step(stmt))==100){var row=new JsonObject();for(int i=0;i<sqlite3_column_count(stmt);i++){
                string name=Marshal.PtrToStringUTF8(sqlite3_column_name(stmt,i))!;
                row[name]=sqlite3_column_type(stmt,i) switch{1=>JsonValue.Create(sqlite3_column_int64(stmt,i)),2=>JsonValue.Create(sqlite3_column_double(stmt,i)),3=>JsonValue.Create(Marshal.PtrToStringUTF8(sqlite3_column_text(stmt,i))),5=>null,_=>throw new InvalidDataException("旧存档包含不支持的二进制字段")};
            }rows.Add(row);}if(result!=101)throw new InvalidDataException(Error());return rows;
        }finally{sqlite3_finalize(stmt);}
    }
    public void Dispose(){if(_db!=IntPtr.Zero){sqlite3_close_v2(_db);_db=IntPtr.Zero;}}
}

public static class LegacyImport
{
    public static JsonObject Read(string path,JsonObject assets)
    {
        JsonObject snapshot;
        if(Path.GetExtension(path).Equals(".json",StringComparison.OrdinalIgnoreCase)){
            var json=JsonNode.Parse(File.ReadAllText(path))!.AsObject();if(json["version"]!=null&&NativeGame.I(json,"version")!=1)throw new InvalidDataException("未知JSON存档版本");snapshot=(json["snapshot"]??json).DeepClone().AsObject();
        }else{
            using var db=new LegacySqlite(path);db.Query("BEGIN");
            if(db.Query("PRAGMA quick_check").Any(r=>r.First().Value!.ToString()!="ok"))throw new InvalidDataException("SQLite完整性检查失败");
            int version=(int)NativeGame.D(db.Query("PRAGMA user_version")[0],"user_version");if(version<1||version>6)throw new InvalidDataException("只支持旧版v1～v6存档，请勿导入未知格式");
            var tables=db.Query("SELECT name FROM sqlite_master WHERE type='table'").Select(r=>NativeGame.S(r,"name")).ToHashSet();
            if(!new[]{"meta","pets","buildings","inventory"}.All(tables.Contains))throw new InvalidDataException("旧存档缺少必要数据表");
            var meta=new JsonObject();foreach(var row in db.Query("SELECT * FROM meta"))meta[NativeGame.S(row,"key")]=JsonNode.Parse(NativeGame.S(row,"value"));
            snapshot=new JsonObject{{"meta",meta}};
            var pets=new JsonArray();foreach(var pet in db.Query("SELECT * FROM pets")){
                if(pet["history"] is JsonValue)pet["history"]=JsonNode.Parse(NativeGame.S(pet,"history"));
                foreach(string key in new[]{"rare","fostered","pregnancy_rare"})if(pet[key]!=null)pet[key]=NativeGame.I(pet,key)!=0;
                pets.Add(pet);
            }snapshot["pets"]=pets;
            foreach(string table in new[]{"buildings","inventory","events","orders","waste","town_zones","people","roads","town_buildings"}){
                var rows=new JsonArray();if(tables.Contains(table))foreach(var row in db.Query("SELECT data FROM "+table))rows.Add(JsonNode.Parse(NativeGame.S(row,"data")));snapshot[table]=rows;
            }
            var achievements=new JsonObject();if(tables.Contains("achievements"))foreach(var row in db.Query("SELECT * FROM achievements"))achievements[NativeGame.S(row,"id")]=row["ts"]!.DeepClone();snapshot["achievements"]=achievements;
            var codex=new JsonObject();if(tables.Contains("codex"))foreach(var row in db.Query("SELECT * FROM codex"))codex[NativeGame.S(row,"species")]=JsonNode.Parse(NativeGame.S(row,"data"));snapshot["codex"]=codex;
            var logs=new JsonArray();if(tables.Contains("logs"))foreach(var row in db.Query("SELECT * FROM logs ORDER BY ts,rowid"))logs.Add(row);snapshot["logs"]=logs;
            if(version<5&&meta["road_revision"]==null)snapshot["roads"]=assets["snapshot"]!["roads"]!.DeepClone();
            db.Query("COMMIT");
        }
        Normalize(snapshot,assets);
        // 仅克隆规则与地图元数据，避免复制数十MB的网格字符串。
        var check=new JsonObject();foreach(string key in new[]{"version","rules","terrain","zones","portals"})check[key]=assets[key]!.DeepClone();check["snapshot"]=snapshot;
        foreach(string key in new[]{"objects","meshes"})if(assets[key]!=null)check[key]=assets[key]!.DeepClone();
        var game=new NativeGame(check);game.Validate();ValidateReferences(game);snapshot["meta"]!["native_imported_from"]=Path.GetFileName(path);
        snapshot["meta"]!["native_real_anchor"]=DateTimeOffset.UtcNow.ToUnixTimeSeconds();return snapshot;
    }
    static void Normalize(JsonObject state,JsonObject assets)
    {
        var meta=state["meta"]?.AsObject()??throw new InvalidDataException("存档缺少元数据");
        foreach(string key in new[]{"sim_ts","start_ts","coins","real_anchor"})if(meta[key]==null||NativeGame.D(meta,key)<0)throw new InvalidDataException("存档缺少有效的"+key);
        double now=NativeGame.D(meta,"sim_ts");if(now>253402300799||NativeGame.D(meta,"start_ts")>now)throw new InvalidDataException("存档时间无效");
        foreach(var pair in assets["snapshot"]!["meta"]!.AsObject())if(meta[pair.Key]==null&&!pair.Key.StartsWith("native_")&&pair.Key!="settlement"){
            if(pair.Key.EndsWith("_ts")&&pair.Value is JsonValue value&&value.TryGetValue<double>(out double ts))meta[pair.Key]=ts>0?now+Math.Max(0,ts-NativeGame.D(assets["snapshot"]!["meta"]!,"sim_ts")):0;
            else meta[pair.Key]=pair.Value?.DeepClone();
        }
        meta["native_speed"]??=NativeGame.S(meta,"mode")=="fast"?120:1;
        foreach(string table in new[]{"pets","buildings","inventory","events","orders","waste","town_zones","people","roads","town_buildings","logs","log_archive"})state[table]??=new JsonArray();
        if(state["buildings"]!.AsArray().Count!=20)throw new InvalidDataException("庄园地块数量不正确");
        foreach(var zone in assets["zones"]!.AsObject())if(!state["town_zones"]!.AsArray().Any(z=>NativeGame.S(z!,"zone_id")==zone.Key))state["town_zones"]!.AsArray().Add(new JsonObject{{"zone_id",zone.Key},{"level",zone.Key=="estate"?1:0}});
        state["codex"]??=new JsonObject();state["achievements"]??=new JsonObject();
        foreach(var pet in state["pets"]!.AsArray()){
            foreach(string key in new[]{"pet_id","name","species","sex","birth_ts","last_update_ts","lifespan_max","building_id","personality"})if(pet?[key]==null)throw new InvalidDataException("宠物缺少必要字段："+key);
            foreach(string key in new[]{"death_reason","departure","mate_id","mother_id","father_id","pregnancy_father_id","illness_type"})pet![key]??="";
            foreach(string key in new[]{"breed_ready_ts","paused_seconds","death_ts","product_ready_ts","product_expiry_ts","interact_ready_ts","pregnancy_start_ts","pregnancy_due_ts","injury","treatment_ready_ts","fight_ready_ts"})pet![key]??=0;
            foreach(string key in new[]{"rare","fostered","pregnancy_rare"})pet![key]??=false;
            pet!["history"]??=new JsonArray();pet["status"]??="正常";pet["illness_budget"]??=1;
            if(NativeGame.S(pet,"status")=="生病"&&NativeGame.S(pet,"illness_type")=="")pet["illness_type"]="普通感染";
        }
        while(state["logs"]!.AsArray().Count>300){var first=state["logs"]![0]!;state["log_archive"]!.AsArray().Add(first.DeepClone());state["logs"]!.AsArray().RemoveAt(0);}
    }
    public static void ValidateReferences(NativeGame game)
    {
        var buildings=game.Rows("buildings").Select(b=>NativeGame.I(b!,"building_id")).ToHashSet();if(buildings.Count!=20||!Enumerable.Range(0,20).All(buildings.Contains))throw new InvalidDataException("地块编号无效");
        var ids=game.Pets.Select(p=>NativeGame.S(p!,"pet_id")).ToHashSet();
        foreach(var p in game.Pets){
            if(game.Rules["SPECIES"]?[NativeGame.S(p!,"species")]==null||!buildings.Contains(NativeGame.I(p!,"building_id")))throw new InvalidDataException("宠物物种或居所无效");
            if(NativeGame.D(p!,"lifespan_max")<=0||NativeGame.D(p!,"birth_ts")>NativeGame.D(p!,"last_update_ts")||NativeGame.D(p!,"last_update_ts")>game.Now+.01)throw new InvalidDataException("宠物年龄无效");
            foreach(string key in new[]{"health","hunger","happy","cleanliness","favor"})if(p![key]==null||NativeGame.D(p,key)<0||NativeGame.D(p,key)>100)throw new InvalidDataException("宠物属性无效");
            foreach(string key in new[]{"mate_id","mother_id","father_id","pregnancy_father_id"})if(NativeGame.S(p!,key)!=""&&(!ids.Contains(NativeGame.S(p!,key))||NativeGame.S(p!,key)==NativeGame.S(p!,"pet_id")))throw new InvalidDataException("宠物亲属引用无效");
        }
        foreach(var b in game.Rows("buildings"))if(NativeGame.S(b!,"kind")!=""&&game.Rules["BUILDINGS"]?[NativeGame.S(b!,"kind")]==null)throw new InvalidDataException("未知庄园建筑");
        foreach(var b in game.Rows("town_buildings"))if(game.Rules["CATALOG"]?[NativeGame.S(b!,"catalog_key")]==null)throw new InvalidDataException("未知城镇建筑");
        foreach(var pair in game.Meta["pet_appearances"]!.AsObject())if(!ids.Contains(pair.Key)||pair.Value!.GetValue<int>()<0||pair.Value.GetValue<int>()>=384)throw new InvalidDataException("宠物外观无效");
        foreach(var row in game.Meta["finance_ledger"]!.AsArray())if(NativeGame.D(row!,"ts")>game.Now||NativeGame.I(row!,"balance")<0)throw new InvalidDataException("财务历史无效");
    }
    public static void SaveImported(JsonObject state,string destination)
    {
        string folder=Path.GetDirectoryName(Path.GetFullPath(destination))!;Directory.CreateDirectory(folder);
        string temp=destination+".import.tmp";File.WriteAllText(temp,new JsonObject{{"version",1},{"snapshot",state.DeepClone()}}.ToJsonString());
        if(File.Exists(destination))File.Copy(destination,destination+".before-import-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fffffff")+".json",false);
        File.Move(temp,destination,true);
    }
}
