"""SQLite 仓储。写入采用单事务，读取校验，异常原档隔离保留并尝试恢复备份。"""
import json
import math
import random
import sqlite3
import time
from contextlib import closing
from dataclasses import fields
from dataclasses import MISSING
from pathlib import Path
from pet import Pet
import game_config as C


class SaveError(RuntimeError):
    pass

SCHEMA_VERSION = 5


def require(condition, message="存档字段超出允许范围"):
    # 不用 assert：python -O 启动时也必须执行存档检查。
    if not condition:
        raise ValueError(message)


def validate(state):
    """拒绝 NaN、负数量、失效外键等损坏存档，避免进入渲染/模拟后崩溃。"""
    def finite_tree(value):
        if isinstance(value, float) and not math.isfinite(value):
            raise ValueError("存档含非有限数值")
        if isinstance(value, dict):
            for v in value.values():
                finite_tree(v)
        elif isinstance(value, list):
            for v in value:
                finite_tree(v)
    finite_tree(state)
    from town_build import validate_build_state
    validate_build_state(state)
    m = state["meta"]
    appearances = m.get('pet_appearances',{})
    require(isinstance(appearances,dict),'宠物外观配置错误')
    pet_species = {p['pet_id']:p['species'] for p in state['pets']}
    require(all(pet_species.get(pid) in C.SPECIES and type(value) is int and 0<=value<384
                for pid,value in appearances.items()),'宠物外观配置错误')
    originals=m.get('pet_initial_appearances',{})
    require(isinstance(originals,dict) and all(pid in pet_species and type(v) is int and 0<=v<384 for pid,v in originals.items()),'初始外观配置错误')
    favorites=m.get('style_favorites',{})
    require(isinstance(favorites,dict) and all(species in C.SPECIES and isinstance(values,list) and len(values)<=12
            and len(values)==len(set(values)) and all(type(v) is int and 0<=v<384 for v in values) for species,values in favorites.items()),'收藏外观配置错误')
    ledger=m.get('finance_ledger',[])
    require(isinstance(ledger,list) and len(ledger)<=3000,'收支记录配置错误')
    for row in ledger:
        require(isinstance(row,dict) and type(row.get('delta')) is int and type(row.get('balance')) is int and row['balance']>=0
                and isinstance(row.get('reason'),str) and isinstance(row.get('ts'),(int,float)) and 0<=row['ts']<=m['sim_ts'],'收支明细错误')
    require(m["mode"] in ("real", "fast") and m["coins"] >= 0 and m["reputation"] >= 0)
    require(0 <= m["sim_ts"] <= 253402300799 and 0 <= m["real_anchor"] <= 253402300799)
    require(0 <= m["start_ts"] <= m["sim_ts"])
    require(all(k in m for k in ("next_care_ts", "next_visitor_ts", "next_event_ts", "next_order_ts", "stats", "caretaker", "discount_until", "inflation_until", "adopt_ready_ts")))
    require(all(isinstance(m["stats"][k], int) and m["stats"][k] >= 0 for k in ("breeds", "sold", "orders")))
    require(isinstance(m["caretaker"], bool))
    if "farm_manager" in m:
        require(isinstance(m["farm_manager"],bool))
        require(m["manager_next_wage_ts"] >= 0 and m["manager_next_scan_ts"] >= 0)
        require(isinstance(m["manager_cleaned"],int) and m["manager_cleaned"] >= 0 and isinstance(m["manager_status"],str))
        require(0 <= m["manager_last_clean_ts"] <= m["sim_ts"])
        require(0 <= m["manager_u"] <= 1 and 0 <= m["manager_v"] <= 1)
    if "rng_state" in m:
        s = m["rng_state"]
        random.Random().setstate((s[0], tuple(s[1]), s[2]))
    buildings = {b["building_id"]: b for b in state["buildings"]}
    require(len(buildings) == len(state["buildings"]) == C.GRID_COLS*C.GRID_ROWS)
    require(set(buildings) == set(range(C.GRID_COLS*C.GRID_ROWS)))
    for b in buildings.values():
        require(isinstance(b["unlocked"], bool))
        require(not b["kind"] or b["kind"] in C.BUILDINGS)
        require((1 <= b["level"] <= C.MAX_LEVEL and b["unlocked"]) if b["kind"] else b["level"] == 0)
    seen, population = set(), {}
    for data in state["pets"]:
        p = Pet.from_dict(data)
        for f in fields(Pet):
            v = getattr(p, f.name)
            require(isinstance(v, (int, float)) if f.type is float else isinstance(v, f.type), f"宠物字段类型错误：{f.name}")
        require(bool(p.name.strip()) and len(p.name) <= C.RENAME_MAX and p.name.isprintable())
        require(p.pet_id not in seen)
        seen.add(p.pet_id)
        require(p.species in C.SPECIES and p.personality in C.PERSONALITIES)
        require(p.sex in C.SEXES and p.status in ("正常", "生病", "受伤", "老年", "离世"))
        require(p.illness_type == "" or p.illness_type in C.ILLNESSES)
        require(0 <= p.injury <= 100 and p.treatment_ready_ts >= 0 and p.fight_ready_ts >= 0)
        require(p.next_excretion_ts >= 0)
        require(0 <= p.birth_ts <= p.last_update_ts <= m["sim_ts"] + .01)
        require(p.lifespan_max > 0 and 0 <= p.paused_seconds <= p.last_update_ts - p.birth_ts + .01)
        require(all(0 <= getattr(p, key) <= 100 for key in ("health", "hunger", "happy", "cleanliness", "favor")))
        require(all(isinstance(h, dict) and isinstance(h.get("text"), str) and isinstance(h.get("ts"), (int, float)) for h in p.history))
        require(0 <= p.death_ts <= m["sim_ts"] and (bool(p.death_ts) == (p.status == "离世")))
        require(p.departure in ("", "出售", "订单交付"))
        if p.active:
            b = buildings[p.building_id]
            require(b["kind"] and C.BUILDINGS[b["kind"]]["capacity"] > 0)
            require(p.fostered == (b["kind"] == "寄养屋"))
            population[p.building_id] = population.get(p.building_id, 0) + 1+int(p.pregnant)
        if p.pregnant:
            require(p.active and p.sex == "雌" and 0 < p.pregnancy_start_ts <= m["sim_ts"] and p.pregnancy_due_ts >= m["sim_ts"]-.01)
        else:
            require(p.pregnancy_start_ts == 0 and p.pregnancy_father_id == "" and not p.pregnancy_rare)
    require(sum(population.values()) <= C.PET_LIMIT)
    pets = {p["pet_id"]:Pet.from_dict(p) for p in state["pets"]}
    for p in pets.values():
        if p.mate_id:
            require(p.mate_id in pets and p.mate_id != p.pet_id)
            partner = pets[p.mate_id]
            require(partner.mate_id == p.pet_id and partner.sex != p.sex and partner.species == p.species)
        for key,sex in (("mother_id","雌"),("father_id","雄"),("pregnancy_father_id","雄")):
            ident = getattr(p,key)
            if ident:
                require(ident in pets and ident != p.pet_id and pets[ident].sex == sex and pets[ident].species == p.species)
    for bid, count in population.items():
        b = buildings[bid]
        conf = C.BUILDINGS[b["kind"]]
        require(count <= conf["capacity"] + conf["growth"]*(b["level"]-1))
    for b in state["inventory"]:
        require(b["item"] in C.ITEMS and isinstance(b["quantity"], int) and b["quantity"] > 0)
        require(b["expiry_ts"] >= 0 and b["created_ts"] >= 0)
    capacity = C.BASE_STORAGE + sum(C.STORAGE_PER_LEVEL*b["level"] for b in buildings.values() if b["kind"] == "仓库")
    require(sum(b["quantity"] for b in state["inventory"]) <= capacity)
    for table, key in (("inventory", "batch_id"), ("orders", "order_id"), ("events", "event_id")):
        require(len({d[key] for d in state[table]}) == len(state[table]), f"重复的{key}")
    for order in state["orders"]:
        require(order["kind"] in ("item", "pet") and isinstance(order["quantity"], int) and order["quantity"] > 0)
        require(order["status"] in ("进行中", "完成", "过期"))
        require(order["expiry_ts"] >= order["created_ts"])
        require(order["reward"] >= 0 and order["reputation"] >= 0 and isinstance(order["npc"], str))
        if order["kind"] == "item":
            require(order["item"] in C.ITEMS)
        else:
            require(order["species"] in C.SPECIES and order["stage"] in ("幼崽", "少年", "成年", "老年"))
    require(all(k in C.ACHIEVEMENTS for k in state["achievements"]))
    require(all(k in C.SPECIES for k in state["codex"]))
    for entry in state["codex"].values():
        require(all(isinstance(entry[k], bool) for k in ("rare", "natural_death")))
    for event in state["events"]:
        require(event["kind"] in ("stray", "merchant", "tourists", "bonus", "rain", "infection", "inflation", "scare"))
        require(event["status"] in ("待处理", "完成", "过期"))
        require(all(k in event for k in ("event_id", "created_ts", "expiry_ts", "text", "options")))
        require(len(event["options"]) == 2 and all(isinstance(o, str) for o in event["options"]))
    for row in state["logs"]:
        require(isinstance(row["text"], str) and isinstance(row["ts"], (int, float)) and isinstance(row["log_id"], str))
    waste = state.get("waste",[])
    require(len({w["waste_id"] for w in waste}) == len(waste))
    for w in waste:
        require(isinstance(w["waste_id"],str) and w["pet_id"] in pets and w["building_id"] in buildings)
        require(isinstance(w["quantity"],int) and not isinstance(w["quantity"],bool) and w["quantity"] > 0)
        require(pets[w["pet_id"]].birth_ts <= w["created_ts"] <= m["sim_ts"] and 0 <= w["u"] <= 1 and 0 <= w["v"] <= 1)
    job = m.get("manager_job")
    if job is not None:
        require(m.get("farm_manager",False) and isinstance(job,dict))
        require(job["waste_id"] in {w["waste_id"] for w in waste} and job["building_id"] in buildings)
        require(0 <= job["start_ts"] <= m["sim_ts"] <= job["end_ts"] and job["start_ts"] < job["sweep_ts"] < job["end_ts"])
        require(all(0 <= job[k] <= 1 for k in ("u","v","from_u","from_v")))
        target = next(w for w in waste if w["waste_id"] == job["waste_id"])
        require(all(job[k] == target[k] for k in ("u","v","building_id")))
    if "weather" in m:
        require(m["weather"] in C.WEATHERS and 0 <= m["weather_since_ts"] <= m["sim_ts"] and m["next_weather_ts"] >= m["sim_ts"])
    if "town_open_ts" in m:
        require(0 <= m["town_open_ts"] <= m["sim_ts"])
        zones = state["town_zones"]
        require(len(zones) == len(C.TOWN_ZONES) and {z["zone_id"] for z in zones} == set(C.TOWN_ZONES))
        require(all(isinstance(z["level"],int) and 0 <= z["level"] <= C.TOWN_MAX_LEVEL for z in zones))
        require(next(z["level"] for z in zones if z["zone_id"] == "estate") == 1)
        require(all(m[k] >= 0 for k in ("next_population_ts","next_town_arrival_ts","next_rent_ts","town_visitors_total","town_income","field_ready_ts","field_expiry_ts","next_field_ts")))
        require(all(k in C.ITEMS and isinstance(n,int) and n > 0 for k,n in m["field_batch"].items()))
        people = state["people"]
        require(len(people) <= C.TOWN_MAX_PEOPLE and len({p["person_id"] for p in people}) == len(people))
        for p in people:
            require(isinstance(p["person_id"],str) and isinstance(p["name"],str) and p["role"] in ("居民","游客"))
            require(p["home_zone"] in C.TOWN_ZONES and p["destination"] in C.TOWN_ZONES)
            require(0 <= p["entered_ts"] <= m["sim_ts"] and isinstance(p["spent"],bool) and p["status"] in ("生活","游览","离镇"))
            require(isinstance(p["color_index"],int) and 0 <= p["color_index"] < 6)
            if p["role"] == "游客":
                require(p["entered_ts"] <= p["spend_ts"] <= p["leave_ts"] < p["exit_ts"] and p["exit_ts"] > m["sim_ts"])


class Database:
    JSON_TABLES = {"buildings": "building_id", "inventory": "batch_id", "events": "event_id", "orders": "order_id", "waste":"waste_id", "town_zones":"zone_id", "people":"person_id",'roads':'road_id','town_buildings':'building_id'}

    def __init__(self, path=C.DB_PATH):
        self.path = Path(path)
        self.path.parent.mkdir(parents=True, exist_ok=True)
        self.backup_path = self.path.with_suffix(".backup.db")
        self.recovery_message = ""
        self.conn = None
        try:
            self._open()
        except SaveError:
            if self.conn:
                self.conn.close()
            raise
        except sqlite3.OperationalError as exc:
            if self.conn:
                self.conn.close()
            raise SaveError(f"无法访问数据库：{exc}") from exc
        except (sqlite3.Error, ValueError, AssertionError, TypeError, KeyError, IndexError, AttributeError) as exc:
            if self.conn:
                self.conn.close()
            self._recover(exc)

    def _open(self):
        self.conn = sqlite3.connect(self.path, timeout=10)
        self.conn.row_factory = sqlite3.Row
        self.conn.execute("PRAGMA foreign_keys=ON")
        self.conn.execute("PRAGMA busy_timeout=10000")
        result = self.conn.execute("PRAGMA quick_check").fetchone()[0]
        if result != "ok":
            raise ValueError(result)
        version = self.conn.execute("PRAGMA user_version").fetchone()[0]
        if version > SCHEMA_VERSION:
            raise SaveError("存档由较新版本创建，请使用对应版本打开；原存档未修改。")
        self.conn.execute("PRAGMA journal_mode=WAL")
        self.conn.execute("PRAGMA synchronous=FULL")
        if 0 < version < SCHEMA_VERSION:
            # 必须在任何DDL之前备份；另留带版本名的不可覆盖迁移前快照。
            before_path=self.path.with_name(self.path.stem+f'.pre-v{SCHEMA_VERSION}.db')
            with closing(sqlite3.connect(before_path)) as destination:
                self.conn.backup(destination)
            if not self.backup_path.exists():
                with closing(sqlite3.connect(self.backup_path)) as destination:
                    self.conn.backup(destination)
        self.conn.execute("BEGIN IMMEDIATE")
        with self.conn:
            self.conn.execute("CREATE TABLE IF NOT EXISTS meta (key TEXT PRIMARY KEY, value TEXT NOT NULL)")
            columns = []
            for f in fields(Pet):
                sql_type = "TEXT" if f.type in (str, list) else ("INTEGER" if f.type in (int, bool) else "REAL")
                columns.append(f'"{f.name}" {sql_type}' + (" PRIMARY KEY" if f.name == "pet_id" else " NOT NULL"))
            self.conn.execute("CREATE TABLE IF NOT EXISTS pets (" + ",".join(columns) + ")")
            # v1/v2 -> v3：只添加有默认值的新列，事务内迁移，不清空原宠物表。
            existing = {r[1] for r in self.conn.execute("PRAGMA table_info(pets)")}
            for f in fields(Pet):
                if f.name not in existing:
                    if f.default is MISSING:
                        raise ValueError(f"存档缺少必要原始字段：{f.name}")
                    sql_type = "TEXT" if f.type is str else ("INTEGER" if f.type in (int,bool) else "REAL")
                    literal = "''" if f.default == "" else str(int(f.default) if isinstance(f.default,bool) else f.default)
                    self.conn.execute(f'ALTER TABLE pets ADD COLUMN "{f.name}" {sql_type} NOT NULL DEFAULT {literal}')
            if version < 2:
                self.conn.execute("UPDATE pets SET illness_type='普通感染' WHERE status='生病' AND illness_type=''")
            for table, key in self.JSON_TABLES.items():
                self.conn.execute(f"CREATE TABLE IF NOT EXISTS {table} ({key} TEXT PRIMARY KEY, data TEXT NOT NULL)")
            self.conn.execute("CREATE TABLE IF NOT EXISTS achievements (id TEXT PRIMARY KEY, ts REAL NOT NULL)")
            self.conn.execute("CREATE TABLE IF NOT EXISTS codex (species TEXT PRIMARY KEY, data TEXT NOT NULL)")
            self.conn.execute("CREATE TABLE IF NOT EXISTS logs (log_id TEXT PRIMARY KEY, ts REAL NOT NULL, text TEXT NOT NULL)")
            self.conn.execute("CREATE INDEX IF NOT EXISTS logs_ts ON logs(ts)")
            self.conn.execute("CREATE INDEX IF NOT EXISTS pet_home ON pets(building_id)")
            if version < 5:
                self._migrate_v5()
            state = self.load()
            if state:
                validate(state)  # 数据校验也在DDL事务内，失败不会提交半迁移表。
            self.conn.execute(f"PRAGMA user_version={SCHEMA_VERSION}")
        state = self.load()
        if state:
            validate(state)
            self.backup()

    def _migrate_v5(self):
        """只补默认道路；旧街区、天气、人口、玩家金币和永久日志不改写。"""
        meta={r['key']:json.loads(r['value']) for r in self.conn.execute('SELECT * FROM meta')}
        if meta:
            for key in ('roads_built','town_buildings_built','town_tax_total'):
                self.conn.execute('INSERT OR IGNORE INTO meta VALUES (?,?)',(key,'0'))
        if meta and 'road_revision' not in meta:
            from town_grid import default_roads
            for road in default_roads(meta['sim_ts']):
                self.conn.execute('INSERT INTO roads VALUES (?,?)',(road['road_id'],json.dumps(road,ensure_ascii=False)))
            self.conn.execute('INSERT INTO meta VALUES (?,?)',('road_revision','0'))

    def _recover(self, exc):
        # 不覆盖损坏原档；SQLite WAL/SHM 一并隔离，方便人工恢复。
        tag = f".damaged-{time.time_ns()}"
        for suffix in ("", "-wal", "-shm"):
            source = Path(str(self.path)+suffix)
            if source.exists():
                source.rename(Path(str(self.path)+tag+suffix))
        recovered = False
        if self.backup_path.exists():
            try:
                with closing(sqlite3.connect(self.backup_path)) as source:
                    source.row_factory = sqlite3.Row
                    require(source.execute("PRAGMA quick_check").fetchone()[0] == "ok")
                    old = self.conn
                    self.conn = source
                    validate(self.load())
                    self.conn = old
                    with closing(sqlite3.connect(self.path)) as destination:
                        source.backup(destination)
                recovered = True
            except (sqlite3.Error, ValueError, AssertionError, TypeError, KeyError, IndexError, AttributeError):
                self.conn = None
        self.recovery_message = f"原存档校验失败，已隔离保留。{'已恢复最近备份。' if recovered else '无可用备份，建立新庄园。'}（{type(exc).__name__}）"
        self._open()

    def load(self):
        meta = {r["key"]: json.loads(r["value"]) for r in self.conn.execute("SELECT * FROM meta")}
        if not meta:
            require(not self.conn.execute("SELECT 1 FROM pets LIMIT 1").fetchone(), "存档缺少全局时钟")
            return None
        pets = []
        for row in self.conn.execute("SELECT * FROM pets"):
            record = dict(row)
            record["history"] = json.loads(record["history"])
            for name in ("rare", "fostered", "pregnancy_rare"):
                if name in record:
                    record[name] = bool(record[name])
            pets.append(record)
        state = {"meta": meta, "pets": pets}
        existing_tables = {r[0] for r in self.conn.execute("SELECT name FROM sqlite_master WHERE type='table'")}
        for table in self.JSON_TABLES:
            if table in ("waste","town_zones","people",'roads','town_buildings') and table not in existing_tables:
                state[table] = []  # 兼容迁移前的旧备份，主库打开时会自动建表。
                continue
            state[table] = [json.loads(r["data"]) for r in self.conn.execute(f"SELECT data FROM {table}")]
        state["achievements"] = {r["id"]: r["ts"] for r in self.conn.execute("SELECT * FROM achievements")}
        state["codex"] = {r["species"]: json.loads(r["data"]) for r in self.conn.execute("SELECT * FROM codex")}
        state["logs"] = list(reversed(self.read_logs(300)))
        return state

    def save(self, state):
        try:
            validate(state)
            # 整个操作（金币、物品、宠物、奖励、日志）要么一起提交，要么一起回滚。
            with self.conn:
                self.conn.execute("DELETE FROM meta")
                self.conn.executemany("INSERT INTO meta VALUES (?,?)", [(k, json.dumps(v, ensure_ascii=False)) for k, v in state["meta"].items()])
                self.conn.execute("DELETE FROM pets")
                names = [f.name for f in fields(Pet)]
                sql = "INSERT INTO pets (" + ",".join(f'"{n}"' for n in names) + ") VALUES (" + ",".join("?" for _ in names) + ")"
                values = []
                for pet in state["pets"]:
                    values.append(tuple(json.dumps(pet[n], ensure_ascii=False) if n == "history" else pet[n] for n in names))
                self.conn.executemany(sql, values)
                for table, key in self.JSON_TABLES.items():
                    self.conn.execute(f"DELETE FROM {table}")
                    self.conn.executemany(f"INSERT INTO {table} VALUES (?,?)", [(str(d[key]), json.dumps(d, ensure_ascii=False)) for d in state[table]])
                self.conn.execute("DELETE FROM achievements")
                self.conn.executemany("INSERT INTO achievements VALUES (?,?)", state["achievements"].items())
                self.conn.execute("DELETE FROM codex")
                self.conn.executemany("INSERT INTO codex VALUES (?,?)", [(k, json.dumps(v, ensure_ascii=False)) for k, v in state["codex"].items()])
                self.conn.executemany("INSERT OR IGNORE INTO logs VALUES (:log_id,:ts,:text)", state["logs"])
        except (sqlite3.Error, AssertionError, ValueError, TypeError, KeyError, IndexError, AttributeError) as exc:
            raise SaveError(f"存档未能提交，操作已回滚：{exc}") from exc

    def read_logs(self, limit=100, offset=0):
        return [dict(r) for r in self.conn.execute("SELECT * FROM logs ORDER BY ts DESC, rowid DESC LIMIT ? OFFSET ?", (limit, offset))]

    def backup(self):
        temp = self.backup_path.with_suffix(".tmp")
        try:
            with closing(sqlite3.connect(temp)) as dest:
                self.conn.backup(dest)
            temp.replace(self.backup_path)
        except (OSError, sqlite3.Error) as exc:
            raise SaveError(f"备份失败，主存档仍保留：{exc}") from exc

    def close(self):
        if self.conn:
            self.backup()
            self.conn.close()
            self.conn = None
