"""运行 python -m unittest discover -s tests -v。仅依赖 Python 标准库。"""
import copy
import json
import sqlite3
import tempfile
import unittest
from contextlib import closing
from pathlib import Path
from unittest.mock import patch

import game_config as C
from db import Database, SaveError, validate
from game import Game, GameError
from pet import Pet


class ManorTests(unittest.TestCase):
    def setUp(self):
        rules = patch.multiple(C, CONCEPTION_CHANCE=1, FIGHT_CHANCE=0, FIGHT_HUNGER_BONUS=0, FIGHT_UNHAPPY_BONUS=0, FIGHT_CROWD_BONUS=0)
        rules.start()
        self.addCleanup(rules.stop)
        output = C.ROOT/".test-output"
        output.mkdir(exist_ok=True)
        self.temp = tempfile.TemporaryDirectory(dir=output)
        self.path = Path(self.temp.name)/"manor.db"
        self.t = 1800000000.0
        self.game = Game(Database(self.path), real_now=self.t, seed=42)

    def tearDown(self):
        if self.game.db.conn:
            self.game.db.close()
        self.temp.cleanup()

    def do(self, action, **kwargs):
        return self.game.perform(action, real_now=self.game.meta["real_anchor"], **kwargs)

    def shift(self, seconds):
        self.game.advance(self.game.meta["real_anchor"]+seconds)

    def safe_pet(self, pet=None):
        p = pet or self.game.living[0]
        p.illness_budget = 1e9
        return p

    def adult(self, pet):
        # 测试造境：改变出生时刻，正常生产代码仍只通过时间推进成长。
        pet.birth_ts = self.game.now-pet.lifespan_max*.3
        pet.last_stage = "成年"
        pet.favor = 90
        pet.illness_budget = 1e9

    def test_initial_persistence_and_explicit_pet_columns(self):
        g = self.game
        self.assertEqual(len(g.living), 2)
        self.assertEqual({p.sex for p in g.living}, {"雌", "雄"})
        self.assertEqual(g.inventory.count("普通饲料"), 25)
        columns = {r[1] for r in g.db.conn.execute("PRAGMA table_info(pets)")}
        self.assertTrue({"birth_ts", "last_update_ts", "lifespan_max", "favor", "building_id"} <= columns)
        self.assertEqual(g.db.conn.execute("PRAGMA quick_check").fetchone()[0], "ok")
        g.db.close()
        self.game = Game(Database(self.path), real_now=self.t, seed=2)
        self.assertEqual([p.pet_id for p in self.game.living], [p.pet_id for p in g.living])

    def test_exact_age_and_decay(self):
        p = self.safe_pet()
        self.shift(2*C.DAY+3*C.HOUR+17*60)
        self.assertEqual(p.age_text(self.game.now), "2天3小时17分钟")
        hunger_mult = C.GLUTTON_HUNGER_MULT if p.personality == "吃货" else 1
        self.assertAlmostEqual(p.hunger, max(0, C.PET_INITIAL["hunger"]-C.DECAY["hunger"]*hunger_mult*(51+17/60)), places=5)
        self.assertEqual(p.stage(self.game.now), "幼崽")

    def test_mode_switch_settles_old_multiplier_and_clock_reversal(self):
        self.game.perform("mode", real_now=self.t+10)
        self.assertEqual(self.game.now, self.t+10)
        self.shift(5)
        self.assertEqual(self.game.now, self.t+10+5*C.SPEED)
        self.do("mode")
        before = self.game.now
        self.game.advance(self.t-100)
        self.assertEqual(self.game.now, before)
        self.shift(2)
        self.assertEqual(self.game.now, before+2)

    def test_offline_fast_multiplier_survives_restart(self):
        self.do("mode")
        self.game.db.close()
        self.game = Game(Database(self.path), real_now=self.t+10, seed=42)
        self.assertEqual(self.game.now, self.t+10*C.SPEED)

    def test_foster_freezes_age_and_attributes_and_keeps_birthday(self):
        p = self.safe_pet()
        self.do("build", building_id=5, kind="寄养屋")
        birth = p.birth_ts
        self.shift(C.HOUR)
        age = p.age_seconds(self.game.now)
        self.do("move", pet_id=p.pet_id, building_id=5)
        stats = (p.health, p.hunger, p.happy, p.cleanliness)
        self.shift(100*C.DAY)
        self.assertEqual(p.birth_ts, birth)
        self.assertAlmostEqual(p.age_seconds(self.game.now), age)
        self.assertEqual((p.health, p.hunger, p.happy, p.cleanliness), stats)
        self.assertTrue(p.active)
        self.do("move", pet_id=p.pet_id, building_id=0)
        self.shift(C.HOUR)
        self.assertAlmostEqual(p.age_seconds(self.game.now), age+C.HOUR)
        self.assertLess(p.hunger, stats[1])

    def test_foster_pending_query_and_restart(self):
        p = self.safe_pet()
        self.do("build", building_id=5, kind="寄养屋")
        self.do("move", pet_id=p.pet_id, building_id=5)
        self.assertEqual(p.age_seconds(self.game.now+10*C.DAY), 0)
        self.game.db.close()
        self.game = Game(Database(self.path), real_now=self.t+4*C.DAY, seed=1)
        p = self.game.pets[p.pet_id]
        self.assertEqual(p.age_seconds(self.game.now), 0)
        self.assertEqual(p.paused_seconds, 4*C.DAY)

    def test_exact_starvation_death_time(self):
        p = self.safe_pet()
        p.hunger, p.health = 0, 9
        self.shift(C.DAY)
        self.assertAlmostEqual(p.death_ts, self.t+9/C.STARVE_DAMAGE*C.HOUR, places=4)
        self.assertEqual(p.status, "离世")
        age = p.age_seconds(self.game.now)
        self.shift(C.DAY)
        self.assertEqual(p.age_seconds(self.game.now), age)

    def test_natural_death_boundary_with_foster_offset(self):
        p = self.safe_pet()
        p.birth_ts = self.t-p.lifespan_max+120
        p.last_stage = "老年"
        self.shift(C.HOUR)
        self.assertEqual(p.death_reason, "寿终正寝")
        self.assertAlmostEqual(p.death_ts, self.t+120, places=5)
        self.assertAlmostEqual(p.age_seconds(self.game.now), p.lifespan_max)
        self.assertTrue(self.game.codex[p.species]["natural_death"])
        self.assertIn("old_friend", self.game.achievements)

    def test_one_chunk_matches_many_chunks(self):
        g = self.game
        p = self.safe_pet()
        p.hunger = 25
        original = p.to_dict()
        other = Pet.from_dict(copy.deepcopy(original))
        b = g.buildings[0]
        p.advance(self.t+12*C.HOUR, b, "春", g.rng)
        for step in range(1, 49):
            other.advance(self.t+step*C.HOUR/4, b, "春", g.rng)
        for field in ("health", "hunger", "cleanliness", "happy"):
            self.assertAlmostEqual(getattr(p, field), getattr(other, field), places=5)

    def test_illness_threshold_and_treatment_rules(self):
        p = self.safe_pet()
        original = self.game.inventory.count("药品")
        with self.assertRaises(GameError):
            self.do("treat", pet_id=p.pet_id)
        self.assertEqual(self.game.inventory.count("药品"), original)
        p = self.game.pets[p.pet_id]  # 失败动作回滚会重建实体。
        p.cleanliness, p.illness_budget = 0, .001
        self.shift(C.HOUR)
        self.assertEqual(p.status, "生病")
        p.illness_type = "普通感染"
        self.do("build",building_id=5,kind="宠物医院")
        self.do("hospitalize",pet_id=p.pet_id)
        self.do("treat", pet_id=p.pet_id)
        self.assertNotEqual(p.status, "生病")
        self.assertEqual(self.game.inventory.count("药品"), original-1)

    def test_food_effects_and_bath(self):
        p = self.game.living[0]
        p.hunger, p.cleanliness = 20, 10
        self.do("feed", pet_id=p.pet_id, item="零食")
        self.assertEqual(p.health, 98)
        self.assertEqual(p.hunger, 30)
        self.do("bath", pet_id=p.pet_id)
        self.assertEqual(p.cleanliness, 65)
        self.game.inventory.add("腐败物", 1, self.game.now, self.game.storage_capacity)
        self.do("feed", pet_id=p.pet_id, item="腐败物")
        self.assertEqual(p.status, "生病")

    def test_breeding_unique_newborn_and_cooldown(self):
        g = self.game
        a,b = g.living
        for p in (a,b):
            self.adult(p)
        with patch.object(C, "MUTATION_CHANCE", 1):
            self.do("breed", first_id=a.pet_id, second_id=b.pet_id)
        self.shift(C.PREGNANCY_DURATION)
        child = next(p for p in g.living if p.pet_id not in (a.pet_id,b.pet_id))
        self.assertEqual(child.birth_ts, g.now)
        self.assertEqual(child.age_seconds(g.now), 0)
        self.assertTrue(child.rare)
        self.assertEqual(child.lifespan_max, C.SPECIES[a.species]["life"]*C.RARE_LIFE_FACTOR)
        self.assertEqual(a.breed_ready_ts, g.now+C.BREED_COOLDOWN)
        self.assertTrue({"first_breed", "rare"} <= set(g.achievements))
        with self.assertRaises(GameError):
            self.do("breed", first_id=a.pet_id, second_id=b.pet_id)
        self.assertEqual(len(g.living), 3)

    def test_breeding_refuses_full_home_and_wrong_sex(self):
        a,b = self.game.living
        self.adult(a)
        self.adult(b)
        b.sex = a.sex
        with self.assertRaises(GameError):
            self.do("breed", first_id=a.pet_id, second_id=b.pet_id)
        g = self.game
        b = g.pets[b.pet_id]
        b.sex = "雄" if a.sex == "雌" else "雌"
        for _ in range(2):
            self.do("buy_pet", species="兔子")
        with self.assertRaises(GameError):
            self.do("breed", first_id=a.pet_id, second_id=b.pet_id)

    def test_rollback_restores_money_and_partly_consumed_materials(self):
        g = self.game
        p = g.living[0]
        g.inventory.take("沐浴露", g.inventory.count("沐浴露"))
        g.save()
        before = g.snapshot()
        with self.assertRaises(GameError):
            self.do("bath", pet_id=p.pet_id)
        self.assertEqual(g.snapshot(), before)
        self.assertEqual(g.db.load(), before)

    def test_disk_save_failure_rolls_back_action(self):
        before = self.game.snapshot()
        with patch.object(self.game.db, "save", side_effect=SaveError("磁盘已满")):
            with self.assertRaises(SaveError):
                self.do("buy", item="普通饲料", quantity=1)
        self.assertEqual(self.game.snapshot(), before)

    def test_inventory_expiry_uses_original_deadline(self):
        g = self.game
        g.inventory.add("羊奶", 2, self.t, g.storage_capacity)
        self.shift(C.DAY)
        self.assertEqual(g.inventory.count("羊奶"), 0)
        self.assertEqual(g.inventory.count("腐败物"), 2)
        self.shift(C.DAY)
        self.assertEqual(g.inventory.count("腐败物"), 0)
        g.inventory.add("羊奶", 1, g.now, g.storage_capacity)
        g.inventory.expire(g.now+100*C.DAY, g.storage_capacity)
        self.assertEqual(g.inventory.count("腐败物"), 0)

    def test_inventory_first_expiry_first_out_and_capacity(self):
        g = self.game
        g.inventory.add("羊奶", 2, g.now, g.storage_capacity, expiry_ts=g.now+C.HOUR)
        g.inventory.add("羊奶", 2, g.now, g.storage_capacity, expiry_ts=g.now+2*C.HOUR)
        g.inventory.take("羊奶", 3)
        batch = next(b for b in g.inventory.batches if b["item"] == "羊奶")
        self.assertEqual(batch["expiry_ts"], g.now+2*C.HOUR)
        with self.assertRaises(ValueError):
            g.inventory.add("谷物", g.storage_capacity, g.now, g.storage_capacity)
        with self.assertRaises(GameError):
            self.do("buy", item="谷物", quantity=-1)

    def test_products_collection_and_ttl(self):
        p = self.safe_pet()
        self.adult(p)
        p.next_product_ts = self.t+C.HOUR
        self.shift(C.HOUR)
        self.assertEqual(p.product_ready_ts, self.game.now)
        self.do("collect", pet_id=p.pet_id)
        self.assertEqual(self.game.inventory.count("兔毛"), 1)
        self.shift(C.HOUR)
        self.assertFalse(p.product_ready_ts)
        self.shift(11*C.HOUR)
        self.assertTrue(p.product_ready_ts)
        # 单次产物在到期前不被下一份产物延长。
        expiry = p.product_expiry_ts
        self.shift(12*C.HOUR)
        self.assertEqual(p.product_expiry_ts, expiry)

    def test_processing_and_warehouse_upgrade(self):
        g = self.game
        self.do("build", building_id=5, kind="加工坊")
        self.do("craft", item="高级饲料")
        self.assertEqual(g.inventory.count("高级饲料"), 2)
        with self.assertRaises(GameError):
            self.do("craft", item="特制零食")
        self.do("upgrade", building_id=5)
        for item in ("羊奶", "鸟蛋"):
            g.inventory.add(item, 1, g.now, g.storage_capacity)
        self.do("craft", item="特制零食")
        self.assertEqual(g.inventory.count("特制零食"), 2)
        capacity = g.storage_capacity
        self.do("upgrade", building_id=1)
        self.assertEqual(g.storage_capacity, capacity+C.STORAGE_PER_LEVEL)

    def test_unlock_adjacency_and_demolition_guards(self):
        with self.assertRaises(GameError):
            self.do("unlock", building_id=19)
        self.do("unlock", building_id=3)
        self.assertTrue(self.game.buildings[3].unlocked)
        with self.assertRaises(GameError):
            self.do("demolish", building_id=0)
        self.game.inventory.add("谷物", 50, self.game.now, self.game.storage_capacity)
        with self.assertRaises(GameError):
            self.do("demolish", building_id=1)

    def test_caretaker_only_feeds_cleans_and_costs_wages(self):
        g = self.game
        p = self.safe_pet()
        p.hunger, p.cleanliness, p.status = 30, 20, "生病"
        favor, medicine = p.favor, g.inventory.count("药品")
        self.do("caretaker")
        coins = g.meta["coins"]
        self.shift(C.CARE_INTERVAL)
        self.assertGreater(p.hunger, 50)
        self.assertGreater(p.cleanliness, 40)
        self.assertEqual(p.favor, favor)
        self.assertEqual(p.status, "生病")
        self.assertEqual(g.inventory.count("药品"), medicine)
        self.assertEqual(g.meta["coins"], coins-C.CARE_WAGE)

    def test_caretaker_stops_when_cannot_pay(self):
        self.do("caretaker")
        self.game.meta["coins"] = 0
        self.shift(C.CARE_INTERVAL)
        self.assertFalse(self.game.meta["caretaker"])

    def test_orders_rewards_expiry_and_pet_delivery(self):
        g = self.game
        order = g.orders[0]
        order.update(item="普通饲料", quantity=2)
        coins = g.meta["coins"]
        self.do("order", order_id=order["order_id"])
        self.assertEqual(order["status"], "完成")
        self.assertEqual(g.meta["coins"], coins+order["reward"])
        pet_order = next(o for o in g.orders if o["kind"] == "pet")
        pet_order.update(species="兔子", stage="幼崽")
        p = g.living[0]
        self.do("order", order_id=pet_order["order_id"], pet_id=p.pet_id)
        self.assertEqual(p.departure, "订单交付")
        self.assertFalse(p.active)
        unfinished = next(o for o in g.orders if o["status"] == "进行中")
        unfinished["expiry_ts"] = g.now+C.HOUR
        self.shift(C.HOUR)
        self.assertEqual(unfinished["status"], "过期")

    def test_event_options_and_expiry_are_single_use(self):
        from event import EVENTS
        g = self.game
        entry = {"event_id": "test", "kind": "tourists", "text": EVENTS["tourists"][0],
                 "options": EVENTS["tourists"][1], "created_ts": g.now,
                 "expiry_ts": g.now+C.HOUR, "status": "待处理"}
        g.events.append(entry)
        coins = g.meta["coins"]
        self.shift(C.HOUR)
        self.assertEqual(entry["status"], "过期")
        self.assertEqual(g.meta["coins"], coins+40)
        with self.assertRaises(GameError):
            self.do("event", event_id="test", option=0)
        self.assertEqual(g.meta["coins"], coins+40)

    def test_all_event_effects_can_execute(self):
        from event import EVENTS
        g = self.game
        self.do("upgrade", building_id=0)
        for kind, (text, options) in EVENTS.items():
            for option in (0, 1):
                if kind == "stray" and option == 0 and len(g.living) >= 6:
                    continue
                entry = {"event_id": f"{kind}{option}", "kind": kind, "text": text, "options": options,
                         "created_ts": g.now, "expiry_ts": g.now+C.HOUR, "status": "待处理"}
                g.events.append(entry)
                self.do("event", event_id=entry["event_id"], option=option)
                self.assertEqual(entry["status"], "完成")

    def test_sale_and_adoption_keeps_archives(self):
        g = self.game
        p = g.living[0]
        price = g.pet_price(p)
        coins = g.meta["coins"]
        self.do("sell_pet", pet_id=p.pet_id)
        self.assertEqual(g.meta["coins"], coins+price)
        self.assertEqual(p.departure, "出售")
        self.do("adopt")
        with self.assertRaises(GameError):
            self.do("adopt")
        self.assertIn(p.pet_id, g.pets)
        self.assertIn("兔子", g.codex)

    def test_achievement_reward_not_repeated_after_reload(self):
        g = self.game
        g.living[0].favor = 85
        self.do("rename", pet_id=g.living[0].pet_id, name="朋友")
        self.assertIn("friends", g.achievements)
        coins = g.meta["coins"]
        g.db.close()
        self.game = Game(Database(self.path), real_now=self.t, seed=42)
        self.do("rename", pet_id=self.game.living[0].pet_id, name="朋友二号")
        self.assertEqual(self.game.meta["coins"], coins)

    def test_seasons_and_building_mismatch(self):
        p = self.safe_pet()
        other = Pet.from_dict(copy.deepcopy(p.to_dict()))
        from building import Building
        p.advance(self.t+C.HOUR, self.game.buildings[0], "春", self.game.rng)
        other.advance(self.t+C.HOUR, Building(9, True, "牧场", 1), "夏", self.game.rng)
        self.assertLess(other.cleanliness, p.cleanliness)
        self.assertLess(other.hunger, p.hunger)
        self.game.meta["sim_ts"] = self.t+3*C.SEASON_DAYS*C.DAY
        self.assertEqual(self.game.season, "冬")

    def test_corrupt_database_preserves_original_and_recovers_backup(self):
        self.game.db.close()
        self.path.write_bytes(b"this is not sqlite")
        self.game = Game(Database(self.path), real_now=self.t)
        self.assertEqual(len(self.game.living), 2)
        self.assertIn("恢复最近备份", self.game.db.recovery_message)
        self.assertTrue(list(self.path.parent.glob("manor.db.damaged-*")))

    def test_invalid_state_recovers_and_nan_rejected(self):
        g = self.game
        bad = g.snapshot()
        bad["pets"][0]["health"] = float("nan")
        with self.assertRaises(ValueError):
            validate(bad)
        g.db.close()
        with closing(sqlite3.connect(self.path)) as conn:
            with conn:
                conn.execute("UPDATE pets SET health=-9")
        self.game = Game(Database(self.path), real_now=self.t)
        self.assertIn("恢复最近备份", self.game.db.recovery_message)
        self.assertTrue(all(p.health >= 0 for p in self.game.living))

    def test_logs_are_permanent_even_when_memory_window_trims(self):
        g = self.game
        for i in range(500):
            g.log(f"测试日志{i}")
        g.save()
        self.assertEqual(len(g.logs), 300)
        self.assertGreaterEqual(len(g.db.read_logs(1000)), 501)

    def test_long_offline_not_capped(self):
        p = self.safe_pet()
        self.shift(365*C.DAY)
        self.assertEqual(self.game.now, self.t+365*C.DAY)
        self.assertTrue(p.death_ts)
        self.assertLess(p.death_ts, self.game.now)
        validate(self.game.snapshot())

    def test_real_sqlite_failure_is_atomic(self):
        g = self.game
        before = g.snapshot()
        with g.db.conn:
            g.db.conn.execute("CREATE TRIGGER reject_inventory BEFORE INSERT ON inventory BEGIN SELECT RAISE(ABORT, 'test failure'); END")
        with self.assertRaises(SaveError):
            self.do("buy", item="普通饲料", quantity=1)
        self.assertEqual(g.snapshot(), before)
        self.assertEqual(g.db.load(), before)
        with g.db.conn:
            g.db.conn.execute("DROP TRIGGER reject_inventory")

    def test_random_stream_survives_restart(self):
        g = self.game
        g.save()
        expected = [g.rng.random() for _ in range(8)]
        g.db.close()
        self.game = Game(Database(self.path), real_now=self.t, seed=999)
        self.assertEqual([self.game.rng.random() for _ in range(8)], expected)

    def test_broken_save_without_backup_starts_new_and_keeps_evidence(self):
        path = self.path.parent/"broken.db"
        path.write_bytes(b"not a database")
        other = Game(Database(path), real_now=self.t, seed=7)
        self.assertEqual(len(other.living), 2)
        self.assertIn("建立新庄园", other.db.recovery_message)
        self.assertTrue(list(path.parent.glob("broken.db.damaged-*")))
        other.db.close()

    def test_growth_thresholds_and_rare_longevity(self):
        p = self.game.living[0]
        for fraction, stage in ((0,"幼崽"),(.10,"少年"),(.25,"成年"),(.80,"老年")):
            self.assertEqual(p.stage(p.birth_ts+p.lifespan_max*fraction), stage)
        rare = Pet.create("兔子", self.t, 0, self.game.rng, rare=True)
        self.assertGreater(rare.lifespan_max, p.lifespan_max)
        self.assertGreater(self.game.pet_price(rare), self.game.pet_price(p))

    def test_gameplay_chain_without_skipping_pet_age(self):
        # 真正用8天时差养成，而非改生日：喂养->成年->产物->繁育->寄养->加工->出售。
        g = self.game
        for p in g.living:
            self.safe_pet(p)
        self.do("caretaker")
        with patch.object(C, "EVENT_CHANCE", 0):
            self.do("farm_manager")
            for _ in range(3):
                for p in g.living:
                    self.do("play", pet_id=p.pet_id)
                self.shift(C.HOUR)
            self.shift(8*C.DAY-3*C.HOUR)
        a,b = g.living
        self.assertEqual(a.stage(g.now), "成年")
        self.assertEqual(b.stage(g.now), "成年")
        self.do("collect", pet_id=a.pet_id)
        self.assertEqual(g.inventory.count("兔毛"), 1)
        self.do("sell_item", item="兔毛")
        old_ids = set(g.pets)
        self.do("breed", first_id=a.pet_id, second_id=b.pet_id)
        with patch.object(C,"EVENT_CHANCE",0):
            self.shift(C.PREGNANCY_DURATION)
        child = g.pets[(set(g.pets)-old_ids).pop()]
        self.assertEqual(child.birth_ts, g.now)
        self.do("build", building_id=5, kind="寄养屋")
        self.do("move", pet_id=child.pet_id, building_id=5)
        with patch.object(C, "EVENT_CHANCE", 0):
            self.shift(C.DAY)
        self.assertEqual(child.age_seconds(g.now), 0)
        self.do("move", pet_id=child.pet_id, building_id=0)
        self.do("build", building_id=6, kind="加工坊")
        self.do("craft", item="高级饲料")
        self.do("feed", pet_id=child.pet_id, item="高级饲料")
        self.do("sell_pet", pet_id=child.pet_id)
        self.assertEqual(child.departure, "出售")
        self.assertEqual(g.meta["stats"]["breeds"], 1)
        self.assertEqual(g.meta["stats"]["sold"], 1)
        self.assertGreater(g.meta["coins"], 0)
        validate(g.snapshot())


if __name__ == "__main__":
    unittest.main()
