"""排泄、卫生疾病风险、可销售粪便、管理员时序/工资/仓容、存档与回滚。"""
import copy
import tempfile
import unittest
import uuid
from pathlib import Path
from unittest.mock import patch
import game_config as C
from game import Game,GameError
from db import Database,SaveError,validate,SCHEMA_VERSION
from pet import Pet


class SanitationTests(unittest.TestCase):
    def setUp(self):
        rules = patch.multiple(C,EVENT_CHANCE=0,FIGHT_CHANCE=0,FIGHT_HUNGER_BONUS=0,FIGHT_UNHAPPY_BONUS=0,FIGHT_CROWD_BONUS=0)
        rules.start()
        self.addCleanup(rules.stop)
        output = C.ROOT/".test-output"
        output.mkdir(exist_ok=True)
        self.temp = tempfile.TemporaryDirectory(dir=output)
        self.path = Path(self.temp.name)/"sanitation.db"
        self.t = 1800000000.0
        self.game = Game(Database(self.path),real_now=self.t,seed=12)
        self.game.meta["next_visitor_ts"] = self.t+1000*C.DAY
        for p in self.game.living:
            p.illness_budget = 1e9
        self.game.save()

    def tearDown(self):
        if self.game.db.conn:
            self.game.db.close()
        self.temp.cleanup()

    def do(self,action,**kwargs):
        return self.game.perform(action,real_now=self.game.meta["real_anchor"],**kwargs)

    def shift(self,delta):
        self.game.advance(self.game.meta["real_anchor"]+delta)

    def pile(self,age=0,quantity=1,building_id=0):
        g = self.game
        p = g.living[0]
        when = g.now-age
        p.birth_ts = min(p.birth_ts,when)
        value = {"waste_id":uuid.uuid4().hex,"pet_id":p.pet_id,"building_id":building_id,
                 "created_ts":when,"quantity":quantity,"u":.45,"v":.65}
        g.waste.append(value)
        g.save()
        return value

    def fill_storage(self):
        g = self.game
        g.inventory.add("谷物",g.storage_capacity-g.inventory.used,g.now,g.storage_capacity)
        g.save()

    def test_excretion_occurs_at_exact_interval_and_is_persistent(self):
        self.shift(C.EXCRETION_INTERVAL-1)
        self.assertEqual(self.game.sanitation.quantity(),0)
        self.shift(1)
        self.assertEqual(self.game.sanitation.quantity(),2)
        self.assertTrue(all(w["created_ts"] == self.t+C.EXCRETION_INTERVAL for w in self.game.waste))
        self.assertEqual(self.game.db.load()["waste"],self.game.waste)
        self.shift(C.EXCRETION_INTERVAL)
        self.assertEqual(self.game.sanitation.quantity(),4)

    def test_every_species_can_excrete(self):
        g = self.game
        for species in C.SPECIES:
            p = Pet.create(species,g.now,0,g.rng)
            p.illness_budget,p.next_excretion_ts = 1e9,g.now+1
            g.pets[p.pet_id] = p
            self.shift(1)
            self.assertTrue(any(w["pet_id"] == p.pet_id for w in g.waste))
            self.do("sell_pet",pet_id=p.pet_id)
        self.assertEqual(g.sanitation.quantity(),len(C.SPECIES))

    def test_foster_pauses_excretion_and_release_resumes(self):
        g = self.game
        p = g.living[0]
        self.do("build",building_id=5,kind="寄养屋")
        self.do("move",pet_id=p.pet_id,building_id=5)
        self.shift(C.DAY)
        self.assertFalse(any(w["pet_id"] == p.pet_id for w in g.waste))
        self.assertEqual(p.next_excretion_ts-g.now,C.EXCRETION_INTERVAL)
        self.do("move",pet_id=p.pet_id,building_id=0)
        self.shift(C.EXCRETION_INTERVAL)
        self.assertTrue(any(w["pet_id"] == p.pet_id for w in g.waste))

    def test_dead_and_sold_pets_stop_excreting(self):
        a,b = self.game.living
        a.die(self.game.now,"测试")
        self.do("sell_pet",pet_id=b.pet_id)
        self.shift(3*C.EXCRETION_INTERVAL)
        self.assertEqual(self.game.waste,[])

    def test_stale_threshold_exact_and_effect_is_local(self):
        g = self.game
        self.pile(quantity=3)
        self.assertEqual(g.sanitation.exposure(0)["hazard"],1)
        self.shift(C.WASTE_UNSAFE_AFTER-1)
        self.assertEqual(g.sanitation.exposure(0)["stale"],0)
        self.shift(1)
        self.assertEqual(g.sanitation.exposure(0)["stale"],3)
        self.assertGreater(g.sanitation.exposure(0)["hazard"],1)
        self.assertEqual(g.sanitation.exposure(5)["hazard"],1)

    def test_uncleaned_manure_can_cause_disease_in_otherwise_clean_pet(self):
        g = self.game
        self.do("build",building_id=5,kind="饲养小屋")
        a,b = g.living
        self.do("move",pet_id=b.pet_id,building_id=5)
        self.pile(age=C.WASTE_UNSAFE_AFTER,quantity=4)
        for p in (a,b):
            p.cleanliness,p.illness_budget = 100,.002
        self.shift(C.HOUR)
        self.assertTrue(a.disease)
        self.assertFalse(b.disease)
        self.assertLess(a.cleanliness,b.cleanliness)

    def test_collect_cleans_environment_but_does_not_cure_disease(self):
        g = self.game
        self.pile(age=C.WASTE_UNSAFE_AFTER,quantity=3)
        p = g.living[0]
        p.contract_illness(g.now,g.rng,"普通感染")
        self.do("clean_waste")
        self.assertEqual(g.sanitation.exposure(0)["hazard"],1)
        self.assertEqual(g.inventory.count("粪便"),3)
        self.assertEqual(p.disease,"普通感染")
        self.assertEqual(C.ITEMS["粪便"]["ttl"],0)
        with self.assertRaises(GameError):
            self.do("feed",pet_id=p.pet_id,item="粪便")

    def test_ground_sale_is_possible_even_with_full_warehouse(self):
        g = self.game
        self.pile(quantity=3)
        self.fill_storage()
        coins,stock = g.meta["coins"],g.inventory.used
        self.do("sell_waste")
        self.assertEqual(g.waste,[])
        self.assertEqual(g.inventory.used,stock)
        self.assertEqual(g.meta["coins"],coins+3*C.ITEMS["粪便"]["sell"])

    def test_collected_manure_can_be_sold_from_inventory(self):
        g = self.game
        self.pile(quantity=2)
        self.do("clean_waste")
        coins = g.meta["coins"]
        self.do("sell_manure")
        self.assertEqual(g.inventory.count("粪便"),0)
        self.assertEqual(g.meta["coins"],coins+2*C.ITEMS["粪便"]["sell"])

    def test_clean_all_fails_atomically_when_storage_is_full(self):
        self.pile(quantity=2)
        self.fill_storage()
        before = self.game.snapshot()
        with self.assertRaisesRegex(GameError,"容量不足"):
            self.do("clean_waste")
        self.assertEqual(self.game.snapshot(),before)

    def test_ground_sale_requires_shop_and_cannot_repeat(self):
        g = self.game
        w = self.pile()
        self.do("demolish",building_id=2)
        before = g.snapshot()
        with self.assertRaises(GameError):
            self.do("sell_waste",waste_id=w["waste_id"])
        self.assertEqual(g.snapshot(),before)
        self.do("build",building_id=2,kind="商店")
        self.do("sell_waste",waste_id=w["waste_id"])
        coins = g.meta["coins"]
        with self.assertRaises(GameError):
            self.do("sell_waste",waste_id=w["waste_id"])
        self.assertEqual(g.meta["coins"],coins)

    def test_hiring_deducts_fee_and_work_finishes_only_at_deadline(self):
        g = self.game
        self.pile()
        coins = g.meta["coins"]
        self.do("farm_manager")
        self.assertEqual(g.meta["coins"],coins-C.MANAGER_HIRE_COST)
        self.assertEqual(g.sanitation.status(),"前往清扫")
        self.shift(C.MANAGER_TRAVEL_SECONDS)
        self.assertEqual(g.sanitation.status(),"清扫中")
        self.assertEqual(g.sanitation.quantity(),1)
        self.shift(C.MANAGER_SWEEP_SECONDS-1)
        self.assertEqual(g.inventory.count("粪便"),0)
        self.shift(1)
        self.assertEqual(g.inventory.count("粪便"),1)
        self.assertEqual(g.meta["manager_cleaned"],1)
        self.assertEqual(g.waste,[])

    def test_no_money_cannot_hire(self):
        self.game.meta["coins"] = 0
        before = self.game.snapshot()
        with self.assertRaises(GameError):
            self.do("farm_manager")
        self.assertEqual(self.game.snapshot(),before)

    def test_offline_manager_collects_and_charges_wages(self):
        g = self.game
        self.do("farm_manager")
        coins = g.meta["coins"]
        self.shift(2*C.MANAGER_WAGE_INTERVAL+60)
        self.assertEqual(g.meta["coins"],coins-2*C.MANAGER_WAGE)
        self.assertEqual(g.meta["manager_cleaned"],4)
        self.assertEqual(g.inventory.count("粪便"),4)
        self.assertEqual(g.waste,[])

    def test_unpaid_wages_stop_manager_and_keep_droppings(self):
        g = self.game
        self.do("farm_manager")
        g.meta["coins"] = 0
        self.shift(C.MANAGER_WAGE_INTERVAL)
        self.assertFalse(g.meta["farm_manager"])
        self.assertIn("工资不足",g.sanitation.status())
        self.assertIsNone(g.meta["manager_job"])
        self.assertGreater(g.sanitation.quantity(),0)

    def test_full_storage_waits_and_selling_stock_restarts_work(self):
        g = self.game
        self.pile()
        self.fill_storage()
        self.do("farm_manager")
        self.assertIn("仓库已满",g.sanitation.status())
        self.assertIsNone(g.meta["manager_job"])
        self.do("sell_item",item="谷物")
        self.assertIsNotNone(g.meta["manager_job"])
        self.shift(C.MANAGER_TRAVEL_SECONDS+C.MANAGER_SWEEP_SECONDS)
        self.assertEqual(g.sanitation.quantity(),0)
        self.assertEqual(g.inventory.count("粪便"),1)

    def test_storage_fills_during_sweep_does_not_delete_pile(self):
        g = self.game
        self.pile()
        self.do("farm_manager")
        self.fill_storage()
        self.shift(C.MANAGER_TRAVEL_SECONDS+C.MANAGER_SWEEP_SECONDS)
        self.assertEqual(g.sanitation.quantity(),1)
        self.assertEqual(g.inventory.count("粪便"),0)
        self.assertIn("仓库已满",g.sanitation.status())

    def test_firing_mid_job_keeps_target_on_ground(self):
        g = self.game
        self.pile()
        self.do("farm_manager")
        self.shift(3)
        self.do("farm_manager")
        self.shift(C.MANAGER_TRAVEL_SECONDS+C.MANAGER_SWEEP_SECONDS)
        self.assertFalse(g.meta["farm_manager"])
        self.assertEqual(g.sanitation.quantity(),1)
        self.assertEqual(g.meta["manager_cleaned"],0)

    def test_manual_collection_cancels_target_job_without_double_credit(self):
        g = self.game
        w = self.pile()
        self.do("farm_manager")
        self.do("clean_waste",waste_id=w["waste_id"])
        self.assertIsNone(g.meta["manager_job"])
        self.shift(30)
        self.assertEqual(g.inventory.count("粪便"),1)
        self.assertEqual(g.meta["manager_cleaned"],0)

    def test_sweep_commit_failure_rolls_back_and_retry_collects_once(self):
        g = self.game
        self.pile()
        self.do("farm_manager")
        before = g.snapshot()
        duration = C.MANAGER_TRAVEL_SECONDS+C.MANAGER_SWEEP_SECONDS
        with patch.object(g.db,"save",side_effect=SaveError("磁盘满")):
            with self.assertRaises(SaveError):
                self.shift(duration)
        self.assertEqual(g.snapshot(),before)
        self.shift(duration)
        self.assertEqual(g.inventory.count("粪便"),1)
        self.assertEqual(g.meta["manager_cleaned"],1)

    def test_restart_resumes_job_without_second_hiring_fee(self):
        g = self.game
        self.pile()
        self.do("farm_manager")
        coins = g.meta["coins"]
        g.db.close()
        self.game = Game(Database(self.path),real_now=self.t+C.MANAGER_TRAVEL_SECONDS)
        self.assertEqual(self.game.sanitation.status(),"清扫中")
        self.shift(C.MANAGER_SWEEP_SECONDS)
        self.assertEqual(self.game.meta["coins"],coins)
        self.assertEqual(self.game.inventory.count("粪便"),1)

    def test_v2_migration_does_not_backfill_old_offline_waste(self):
        g = self.game
        with g.db.conn:
            g.db.conn.execute("DROP TABLE waste")
            g.db.conn.execute("ALTER TABLE pets DROP COLUMN next_excretion_ts")
            g.db.conn.execute("DELETE FROM meta WHERE key='farm_manager' OR key LIKE 'manager_%'")
            g.db.conn.execute("PRAGMA user_version=2")
        g.db.close()
        self.game = Game(Database(self.path),real_now=self.t+C.DAY)
        self.assertEqual(self.game.db.conn.execute("PRAGMA user_version").fetchone()[0],SCHEMA_VERSION)
        self.assertEqual(self.game.waste,[])
        self.assertTrue(all(p.next_excretion_ts == self.game.now+C.EXCRETION_INTERVAL for p in self.game.living))
        self.assertFalse(self.game.meta["farm_manager"])
        validate(self.game.snapshot())

    def test_offline_and_incremental_excretion_use_identical_timestamps_and_positions(self):
        g = self.game
        state = g.snapshot()
        other = Game(Database(Path(self.temp.name)/"other.db"),real_now=self.t,seed=12)
        try:
            other._load(copy.deepcopy(state))
            self.shift(2*C.EXCRETION_INTERVAL)
            for i in range(12):
                other.advance(self.t+(i+1)*C.HOUR)
            self.assertEqual(g.waste,other.waste)
            self.assertEqual(g.inventory.batches,other.inventory.batches)
        finally:
            other.db.close()

    def test_manager_feeds_hungry_pet_only_after_work_and_consumes_stock(self):
        g = self.game
        pet = g.living[0]
        pet.hunger = 60
        stock = g.inventory.count("普通饲料")
        self.do("farm_manager")
        self.assertEqual(g.meta["manager_job"]["pet_id"],pet.pet_id)
        self.assertEqual(g.sanitation.status(),"前往喂食")
        self.shift(C.MANAGER_TRAVEL_SECONDS)
        self.assertEqual(g.sanitation.status(),"喂食中")
        self.assertEqual(g.inventory.count("普通饲料"),stock)
        self.shift(C.MANAGER_SWEEP_SECONDS)
        self.assertEqual(g.inventory.count("普通饲料"),stock-1)
        self.assertGreater(pet.hunger,89)
        self.assertEqual(g.meta["manager_fed"],1)
        self.assertIsNone(g.meta["manager_job"])

    def test_manager_missing_feed_still_cleans_and_never_invents_food(self):
        g = self.game
        pet = g.living[0]
        pet.hunger = 20
        g.inventory.take("普通饲料",g.inventory.count("普通饲料"))
        self.pile()
        self.do("farm_manager")
        self.assertEqual(g.meta["manager_job"]["kind"],"clean")
        self.shift(C.MANAGER_TRAVEL_SECONDS+C.MANAGER_SWEEP_SECONDS)
        self.assertEqual(g.meta["manager_cleaned"],1)
        self.assertEqual(g.meta["manager_fed"],0)
        self.assertLess(pet.hunger,20)
        self.assertIn("饲料不足",g.sanitation.status())

    def test_manager_manual_feed_cancels_pending_feed_without_double_spend(self):
        g = self.game
        pet = g.living[0]
        pet.hunger = 60
        self.do("farm_manager")
        self.shift(3)
        self.do("feed",pet_id=pet.pet_id)
        self.assertIsNone(g.meta["manager_job"])
        stock = g.inventory.count("普通饲料")
        self.shift(C.MANAGER_TRAVEL_SECONDS+C.MANAGER_SWEEP_SECONDS)
        self.assertEqual(g.inventory.count("普通饲料"),stock)
        self.assertEqual(g.meta["manager_fed"],0)

    def test_manager_feed_reload_and_target_sale_are_safe(self):
        g = self.game
        pet = g.living[0]
        pet.hunger = 60
        ident = pet.pet_id
        self.do("farm_manager")
        g.db.close()
        self.game = Game(Database(self.path),real_now=self.t+3)
        self.assertEqual(self.game.meta["manager_job"]["pet_id"],ident)
        self.do("sell_pet",pet_id=ident)
        self.assertIsNone(self.game.meta["manager_job"])
        validate(self.game.snapshot())

    def test_manager_idle_is_stationary_and_travel_faces_destination(self):
        import math
        import pygame
        from sanitation_view import SanitationView
        g = self.game
        self.do("farm_manager")
        view = SanitationView(None,pygame.Rect(210,552,860,124))
        before = view.manager_pose(g)
        view.tick(30)
        self.assertEqual(before[:2],view.manager_pose(g)[:2])
        self.pile()
        g.sanitation.wake()
        with patch("sanitation_view.time.time",return_value=g.meta["real_anchor"]):
            view.manager_pose(g)
        job = g.meta["manager_job"]
        expected = math.atan2(-(job["u"]-job["from_u"])*860,(job["v"]-job["from_v"])*124)
        self.assertAlmostEqual(view.manager_angle,expected)


if __name__ == "__main__":
    unittest.main()
