"""性别购买、同居妊娠、离线出生、伴侣档案、冲突与医疗及v1迁移。"""
import tempfile
import unittest
from dataclasses import fields
from pathlib import Path
from unittest.mock import patch
import game_config as C
import achievement
from db import Database, SaveError, validate, SCHEMA_VERSION
from game import Game, GameError
from pet import Pet


class SocialMedicalTests(unittest.TestCase):
    def setUp(self):
        output = C.ROOT/".test-output"
        output.mkdir(exist_ok=True)
        self.temp = tempfile.TemporaryDirectory(dir=output)
        self.path = Path(self.temp.name)/"social.db"
        self.game = Game(Database(self.path),real_now=1800000000.0,seed=23)
        self.game.meta["coins"] = 200000
        self.do("upgrade",building_id=0)
        self.do("build",building_id=5,kind="宠物医院")
        self.do("build",building_id=6,kind="饲养小屋")
        for p in self.game.living:
            p.birth_ts = self.game.now-p.lifespan_max*.3
            p.last_stage, p.favor, p.illness_budget = "成年",80,1e9
        achievement.check(self.game)
        self.game.save()

    def tearDown(self):
        if self.game.db.conn:
            self.game.db.close()
        self.temp.cleanup()

    def do(self, action, **kwargs):
        return self.game.perform(action,real_now=self.game.meta["real_anchor"],**kwargs)

    def shift(self, delta):
        with patch.multiple(C,EVENT_CHANCE=0,FIGHT_CHANCE=0,FIGHT_HUNGER_BONUS=0,FIGHT_UNHAPPY_BONUS=0,FIGHT_CROWD_BONUS=0):
            self.game.advance(self.game.meta["real_anchor"]+delta)

    def conceive(self):
        a,b = self.game.living[:2]
        with patch.object(C,"CONCEPTION_CHANCE",1):
            self.do("breed",first_id=a.pet_id,second_id=b.pet_id)
        return (a,b) if a.sex == "雌" else (b,a)

    def test_explicit_sex_for_all_current_species_and_eggs(self):
        g = self.game
        self.do("build",building_id=7,kind="牧场")
        self.do("unlock",building_id=3)
        self.do("build",building_id=3,kind="鸟舍")
        for species,spec in C.SPECIES.items():
            for sex in C.SEXES:
                old = set(g.pets)
                self.do("buy_pet",species=species,sex=sex)
                p = g.pets[(set(g.pets)-old).pop()]
                self.assertEqual(p.sex,sex)
                self.do("sell_pet",pet_id=p.pet_id)
            if spec.get("egg_available",True):
                old = set(g.pets)
                self.do("buy_pet",species=species,sex="雌",egg=True)
                p = g.pets[(set(g.pets)-old).pop()]
                self.assertEqual(p.sex,"雌")
                self.do("sell_pet",pet_id=p.pet_id)

    def test_invalid_sex_is_rejected_without_charge(self):
        before = self.game.snapshot()
        with self.assertRaises(GameError):
            self.do("buy_pet",species="猫咪",sex="未知")
        self.assertEqual(self.game.snapshot(),before)
        with self.assertRaises(ValueError):
            Pet.create("兔子",self.game.now,0,self.game.rng,sex="")

    def test_same_sex_and_different_species_cannot_conceive(self):
        g = self.game
        a,b = g.living
        b.sex = a.sex
        before = g.snapshot()
        with self.assertRaises(GameError):
            self.do("breed",first_id=a.pet_id,second_id=b.pet_id)
        self.assertEqual(g.snapshot(),before)
        b = g.pets[b.pet_id]
        b.sex, b.species = ("雄" if a.sex == "雌" else "雌"),"狐狸"
        with self.assertRaises(GameError):
            self.do("pair",first_id=a.pet_id,second_id=b.pet_id)

    def test_cohabitation_required_even_for_mates(self):
        a,b = self.game.living
        self.do("pair",first_id=a.pet_id,second_id=b.pet_id)
        self.do("move",pet_id=b.pet_id,building_id=6)
        before = self.game.snapshot()
        with self.assertRaisesRegex(GameError,"同一座建筑"):
            self.do("breed",first_id=a.pet_id,second_id=b.pet_id)
        self.assertEqual(self.game.snapshot(),before)

    def test_partner_links_are_reciprocal_persistent_and_can_break_up(self):
        g = self.game
        a,b = g.living
        self.do("pair",first_id=a.pet_id,second_id=b.pet_id)
        self.assertEqual(a.mate_id,b.pet_id)
        self.assertEqual(b.mate_id,a.pet_id)
        g.db.close()
        self.game = Game(Database(self.path),real_now=g.meta["real_anchor"])
        self.assertEqual(self.game.pets[a.pet_id].mate_id,b.pet_id)
        self.do("unpair",pet_id=a.pet_id)
        self.assertFalse(self.game.pets[a.pet_id].mate_id)
        self.assertFalse(self.game.pets[b.pet_id].mate_id)

    def test_monogamy_prevents_replacing_a_partner(self):
        g = self.game
        a,b = g.living
        self.do("pair",first_id=a.pet_id,second_id=b.pet_id)
        old = set(g.pets)
        self.do("buy_pet",species="兔子",sex=b.sex)
        p = g.pets[(set(g.pets)-old).pop()]
        p.birth_ts = g.now-p.lifespan_max*.3
        with self.assertRaisesRegex(GameError,"已有其他伴侣"):
            self.do("pair",first_id=a.pet_id,second_id=p.pet_id)

    def test_pregnancy_and_exact_offline_birth_including_parent_ids(self):
        g = self.game
        mother,father = self.conceive()
        self.assertEqual(len(g.living),2)
        self.assertTrue(mother.pregnant)
        due = mother.pregnancy_due_ts
        g.db.close()
        with patch.object(C,"EVENT_CHANCE",0), patch.object(C,"FIGHT_CHANCE",0):
            self.game = Game(Database(self.path),real_now=g.meta["real_anchor"]+C.PREGNANCY_DURATION+60)
        child = next(p for p in self.game.living if p.mother_id)
        self.assertEqual(child.birth_ts,due)
        self.assertEqual(child.age_seconds(self.game.now),60)
        self.assertEqual((child.mother_id,child.father_id),(mother.pet_id,father.pet_id))
        self.assertIn(child.sex,C.SEXES)
        self.assertFalse(self.game.pets[mother.pet_id].pregnant)
        self.assertEqual(self.game.meta["stats"]["breeds"],1)

    def test_unsuccessful_conception_consumes_cost_and_sets_retry(self):
        g = self.game
        a,b = g.living
        coins = g.meta["coins"]
        with patch.object(C,"CONCEPTION_CHANCE",0):
            message = self.do("breed",first_id=a.pet_id,second_id=b.pet_id)
        self.assertIn("未能受孕",message)
        self.assertEqual(g.meta["coins"],coins-C.BREED_COST)
        self.assertFalse(any(p.pregnant for p in g.living))
        self.assertEqual(a.breed_ready_ts,g.now+C.CONCEPTION_RETRY)

    def test_pregnancy_reserves_bed_and_cannot_repeat_sell_or_unpair(self):
        g = self.game
        # 6床已有2只，先添3只，剩1床将被幼崽预留。
        for _ in range(3):
            self.do("buy_pet",species="兔子")
        mother,father = self.conceive()
        self.assertEqual(g.housing_used(0),6)
        self.assertEqual(g.find_home("兔子"),6)
        for action,args in (("unpair",{"pet_id":mother.pet_id}),("sell_pet",{"pet_id":father.pet_id}),("breed",{"first_id":mother.pet_id,"second_id":father.pet_id})):
            with self.assertRaises(GameError):
                self.do(action,**args)

    def test_pregnancy_pauses_in_foster_and_resumes(self):
        g = self.game
        self.do("build",building_id=7,kind="寄养屋")
        mother,father = self.conceive()
        self.shift(C.HOUR)
        left = mother.pregnancy_due_ts-g.now
        self.do("move",pet_id=mother.pet_id,building_id=7)
        self.shift(2*C.DAY)
        self.assertAlmostEqual(mother.pregnancy_due_ts-g.now,left)
        self.assertEqual(len(g.living),2)
        self.do("move",pet_id=mother.pet_id,building_id=0)
        self.shift(left)
        self.assertEqual(len(g.living),3)

    def test_maternal_death_cancels_pregnancy_and_father_record_remains(self):
        mother,father = self.conceive()
        mother.health,mother.hunger = 1,0
        self.shift(C.HOUR)
        self.assertFalse(mother.active)
        self.assertFalse(mother.pregnant)
        self.assertEqual(father.mate_id,mother.pet_id)
        self.shift(C.PREGNANCY_DURATION)
        self.assertFalse(any(p.mother_id for p in self.game.pets.values()))

    def test_father_death_does_not_erase_unborn_child_parentage(self):
        mother,father = self.conceive()
        father.die(self.game.now,"测试离世")
        self.shift(C.PREGNANCY_DURATION)
        child = next(p for p in self.game.living if p.mother_id)
        self.assertEqual(child.father_id,father.pet_id)
        self.assertEqual(mother.mate_id,father.pet_id)

    def test_scheduled_fight_injures_both_and_is_persisted(self):
        g = self.game
        a,b = g.living
        before = [p.health for p in (a,b)]
        g.meta["next_fight_ts"] = g.now+1
        with patch.object(C,"FIGHT_CHANCE",100):
            g.advance(g.meta["real_anchor"]+1)
        for p,health in zip((a,b),before):
            self.assertEqual(p.status,"受伤")
            self.assertGreater(p.injury,0)
            self.assertLess(p.health,health)
            self.assertTrue(any("打架" in h["text"] for h in p.history))
        self.assertTrue(all(p["injury"] > 0 for p in g.db.load()["pets"]))

    def test_separate_homes_hospital_and_foster_prevent_fights(self):
        g = self.game
        a,b = g.living
        self.do("move",pet_id=b.pet_id,building_id=6)
        with patch.object(C,"FIGHT_CHANCE",100):
            g.social.fights()
        self.assertEqual(a.injury+b.injury,0)
        for p in (a,b):
            self.do("move",pet_id=p.pet_id,building_id=5)
        with patch.object(C,"FIGHT_CHANCE",100):
            g.social.fights()
        self.assertEqual(a.injury+b.injury,0)

    def test_fight_can_kill_and_populate_memorial(self):
        g = self.game
        a,b = g.living
        a.health = 1
        g.social.fight_pair(a,b)
        g.save()
        self.assertEqual(a.status,"离世")
        self.assertIn("打架",a.death_reason)
        self.assertTrue(a.death_ts)
        self.assertTrue(b.active)

    def test_untreated_wounds_decay_health(self):
        p = self.game.living[0]
        p.injury = 40
        p.refresh_status(self.game.now)
        health = p.health
        self.shift(C.HOUR)
        self.assertAlmostEqual(p.health,health-40*C.INJURY_HOURLY_DAMAGE)

    def test_treatment_requires_admission_and_costs_nothing_on_failure_to_admit(self):
        g = self.game
        p = g.living[0]
        p.injury = 20
        p.refresh_status(g.now)
        before = g.snapshot()
        with self.assertRaisesRegex(GameError,"住院"):
            self.do("treat",pet_id=p.pet_id)
        self.assertEqual(g.snapshot(),before)

    def test_ordinary_infection_and_wounds_can_be_cured_in_hospital(self):
        g = self.game
        p = g.living[0]
        p.contract_illness(g.now,g.rng,"普通感染")
        p.injury = 25
        self.do("hospitalize",pet_id=p.pet_id)
        self.do("treat",pet_id=p.pet_id)
        self.assertFalse(p.unwell)
        self.assertEqual(p.injury,0)
        self.assertEqual(g.buildings[p.building_id].kind,"宠物医院")

    def test_failed_treatment_keeps_cost_and_cooldown(self):
        g = self.game
        p = g.living[0]
        p.contract_illness(g.now,g.rng,"重症感染")
        self.do("hospitalize",pet_id=p.pet_id)
        coins,stock = g.meta["coins"],g.inventory.count("药品")
        with patch.object(g.rng,"random",side_effect=[.99,.99]):
            message = self.do("treat",pet_id=p.pet_id)
        self.assertIn("治疗失败",message)
        self.assertLess(g.meta["coins"],coins)
        self.assertEqual(g.inventory.count("药品"),stock-1)
        self.assertTrue(p.unwell)
        with self.assertRaisesRegex(GameError,"恢复期"):
            self.do("treat",pet_id=p.pet_id)

    def test_severe_disease_can_die_during_treatment_and_save_outcome(self):
        g = self.game
        p = g.living[0]
        p.contract_illness(g.now,g.rng,"重症感染")
        self.do("hospitalize",pet_id=p.pet_id)
        with patch.object(g.rng,"random",side_effect=[.99,0]):
            self.do("treat",pet_id=p.pet_id)
        self.assertEqual(p.status,"离世")
        self.assertIn("救治无效",p.death_reason)
        archived = next(d for d in g.db.load()["pets"] if d["pet_id"] == p.pet_id)
        self.assertEqual(archived["death_ts"],g.now)

    def test_terminal_disease_cannot_be_cured_even_by_level_five_hospital(self):
        g = self.game
        p = g.living[0]
        p.contract_illness(g.now,g.rng,"绝症")
        p.health = 25
        self.do("hospitalize",pet_id=p.pet_id)
        for _ in range(4):
            self.do("upgrade",building_id=5)
        self.assertEqual(g.medical.plan(p)["chance"],0)
        self.do("treat",pet_id=p.pet_id)
        self.assertEqual(p.disease,"绝症")
        self.shift(2*C.DAY)
        self.assertFalse(p.active)
        self.assertIn("绝症",p.death_reason)

    def test_hospital_upgrade_improves_chance_but_never_resurrects(self):
        g = self.game
        p = g.living[0]
        p.contract_illness(g.now,g.rng,"重症感染")
        self.do("hospitalize",pet_id=p.pet_id)
        chance = g.medical.plan(p)["chance"]
        self.do("upgrade",building_id=5)
        self.assertGreater(g.medical.plan(p)["chance"],chance)
        p.die(g.now,"测试")
        with self.assertRaises(GameError):
            self.do("treat",pet_id=p.pet_id)

    def test_v1_database_migrates_without_losing_identity_or_sex(self):
        g = self.game
        names = {p.pet_id:(p.name,p.sex) for p in g.living}
        added = ["mate_id","mother_id","father_id","pregnancy_start_ts","pregnancy_due_ts","pregnancy_father_id","pregnancy_rare","illness_type","injury","treatment_ready_ts","fight_ready_ts","next_excretion_ts"]
        with g.db.conn:
            for name in added:
                g.db.conn.execute(f'ALTER TABLE pets DROP COLUMN "{name}"')
            g.db.conn.execute("DROP TABLE waste")
            g.db.conn.execute("DELETE FROM meta WHERE key='farm_manager' OR key LIKE 'manager_%'")
            g.db.conn.execute("PRAGMA user_version=1")
        g.db.close()
        self.game = Game(Database(self.path),real_now=g.meta["real_anchor"])
        self.assertEqual(self.game.db.conn.execute("PRAGMA user_version").fetchone()[0],SCHEMA_VERSION)
        self.assertEqual({p.pet_id:(p.name,p.sex) for p in self.game.living},names)
        self.assertTrue(all(not p.mate_id and not p.pregnant for p in self.game.living))
        validate(self.game.snapshot())

    def test_birth_commit_failure_rolls_back_then_delivers_once(self):
        g = self.game
        mother,father = self.conceive()
        before = g.snapshot()
        with patch.object(g.db,"save",side_effect=SaveError("磁盘已满")):
            with self.assertRaises(SaveError):
                self.shift(C.PREGNANCY_DURATION)
        self.assertEqual(g.snapshot(),before)
        self.assertTrue(g.pets[mother.pet_id].pregnant)
        self.shift(C.PREGNANCY_DURATION)
        self.assertEqual(sum(bool(p.mother_id) for p in g.pets.values()),1)
        self.shift(60)
        self.assertEqual(sum(bool(p.mother_id) for p in g.pets.values()),1)

    def test_pregnant_move_reserves_two_beds_and_blocks_overbooking(self):
        g = self.game
        mother,_ = self.conceive()
        for _ in range(3):
            p = Pet.create("兔子",g.now,6,g.rng)
            g.pets[p.pet_id] = p
        before = g.snapshot()
        with self.assertRaisesRegex(GameError,"空位"):
            self.do("move",pet_id=mother.pet_id,building_id=6)
        self.assertEqual(g.snapshot(),before)
        self.do("upgrade",building_id=6)
        self.do("move",pet_id=mother.pet_id,building_id=6)
        self.assertEqual(g.housing_used(6),5)

    def test_combined_illness_does_not_make_injury_easier_to_cure(self):
        g = self.game
        p = g.living[0]
        p.injury = 50
        p.refresh_status(g.now)
        self.do("hospitalize",pet_id=p.pet_id)
        chance = g.medical.plan(p)["chance"]
        p.contract_illness(g.now,g.rng,"普通感染")
        self.assertLessEqual(g.medical.plan(p)["chance"],chance)


if __name__ == "__main__":
    unittest.main()
