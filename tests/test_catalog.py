"""新品种的购买门槛、养成链路及旧存档兼容测试（仅标准库）。"""
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch

import achievement
import event
import game_config as C
from db import Database, validate
from game import Game, GameError


NEW_SPECIES = ("猫咪", "小狗", "仓鼠", "刺猬", "羊驼", "梅花鹿", "小鸭", "孔雀")


class CatalogTests(unittest.TestCase):
    def setUp(self):
        rules = patch.multiple(C, CONCEPTION_CHANCE=1, FIGHT_CHANCE=0, FIGHT_HUNGER_BONUS=0, FIGHT_UNHAPPY_BONUS=0, FIGHT_CROWD_BONUS=0)
        rules.start()
        self.addCleanup(rules.stop)
        output = C.ROOT/".test-output"
        output.mkdir(exist_ok=True)
        self.folder = tempfile.TemporaryDirectory(dir=output)
        self.path = Path(self.folder.name)/"catalog.db"
        self.game = Game(Database(self.path), real_now=1800000000.0, seed=99)
        self.game.meta["coins"] = 100000
        self.do("upgrade", building_id=0)  # 六个小屋空位，足够初始兔子+四种新品种。
        self.do("build", building_id=5, kind="牧场")
        self.do("build", building_id=6, kind="鸟舍")

    def tearDown(self):
        if self.game.db.conn:
            self.game.db.close()
        self.folder.cleanup()

    def do(self, action, **kwargs):
        return self.game.perform(action, real_now=self.game.meta["real_anchor"], **kwargs)

    def buy(self, species, egg=False):
        before = set(self.game.pets)
        self.do("buy_pet", species=species, egg=egg)
        return self.game.pets[(set(self.game.pets)-before).pop()]

    def test_complete_catalog_configuration(self):
        self.assertEqual(len(C.SPECIES), 12)
        self.assertEqual({s for s,p in C.SPECIES.items() if p.get("purchase_only")}, set(NEW_SPECIES))
        for species in NEW_SPECIES:
            with self.subTest(species=species):
                spec = C.SPECIES[species]
                self.assertGreater(spec["buy_price"], 0)
                self.assertIn(spec["building"], C.BUILDINGS)
                self.assertIn(spec["product"], C.ITEMS)
                self.assertIn(spec["product"], C.ORDER_ITEM_POOL)
                self.assertGreater(spec["life"], spec["interval"])

    def test_every_new_pet_charges_its_displayed_price(self):
        g = self.game
        for species in NEW_SPECIES:
            with self.subTest(species=species):
                coins = g.meta["coins"]
                p = self.buy(species)
                self.assertEqual(g.meta["coins"], coins-g.pet_buy_price(species))
                self.assertEqual(p.age_seconds(g.now), 0)
                self.assertEqual(p.birth_ts, g.now)
                self.assertEqual(p.lifespan_max, C.SPECIES[species]["life"])
                self.assertEqual(g.buildings[p.building_id].kind, C.SPECIES[species]["building"])
                self.assertIn(species, g.codex)
                self.assertTrue(any("购买" in h["text"] for h in p.history))
        validate(g.snapshot())

    def test_no_money_does_not_create_a_pet_or_consume_resources(self):
        g = self.game
        g.meta["coins"] = 0
        g.save()
        before = g.snapshot()
        with self.assertRaisesRegex(GameError, "金币不足"):
            self.do("buy_pet", species="孔雀")
        self.assertEqual(g.snapshot(), before)
        self.assertEqual(g.db.load(), before)

    def test_missing_building_and_full_home_do_not_charge_money(self):
        g = self.game
        self.do("demolish", building_id=5)
        before = g.snapshot()
        with self.assertRaisesRegex(GameError, "牧场"):
            self.do("buy_pet", species="羊驼")
        self.assertEqual(g.snapshot(), before)
        for _ in range(4):
            self.buy("兔子")
        before = g.snapshot()
        with self.assertRaisesRegex(GameError, "空位"):
            self.do("buy_pet", species="猫咪")
        self.assertEqual(g.snapshot(), before)

    def test_only_bird_new_species_allow_eggs(self):
        g = self.game
        for species in NEW_SPECIES:
            with self.subTest(species=species):
                if C.SPECIES[species]["egg_available"]:
                    before = g.meta["coins"]
                    with patch.object(C, "MUTATION_CHANCE", 0):
                        p = self.buy(species, egg=True)
                    self.assertEqual(g.meta["coins"], before-int(C.SPECIES[species]["buy_price"]*C.EGG_PRICE_FACTOR))
                    self.assertTrue(any("孵化" in h["text"] for h in p.history))
                else:
                    before = g.snapshot()
                    with self.assertRaisesRegex(GameError, "没有宠物蛋"):
                        self.do("buy_pet", species=species, egg=True)
                    self.assertEqual(g.snapshot(), before)

    def test_free_sources_cannot_bypass_purchase(self):
        g = self.game
        candidate_pools = []
        def prefer_paid_if_present(values):
            if values and isinstance(values[0], tuple):
                candidate_pools.append([s for s,_ in values])
                return next((x for x in values if x[0] in NEW_SPECIES), values[0])
            return values[0]
        with patch.object(g.rng, "choice", side_effect=prefer_paid_if_present):
            before = set(g.pets)
            self.do("adopt")
            text, options = event.EVENTS["stray"]
            entry = {"event_id":"free-stray", "kind":"stray", "text":text, "options":options,
                     "created_ts":g.now, "expiry_ts":g.now+C.EVENT_DURATION, "status":"待处理"}
            g.events.append(entry)
            self.do("event", event_id=entry["event_id"], option=0)
        self.assertEqual(len(candidate_pools), 2)
        for pool in candidate_pools:
            self.assertFalse(set(pool) & set(NEW_SPECIES))
        self.assertTrue(all(g.pets[pid].species not in NEW_SPECIES for pid in set(g.pets)-before))

    def test_new_species_produce_collect_and_sell(self):
        g = self.game
        pets = [self.buy(s) for s in NEW_SPECIES]
        for p in pets:
            p.birth_ts = g.now-p.lifespan_max*.3
            p.last_stage = "成年"
            p.illness_budget = 1e9
            p.next_product_ts = g.now+60
        g.advance(g.meta["real_anchor"]+61)
        for p in pets:
            with self.subTest(species=p.species):
                item = C.SPECIES[p.species]["product"]
                self.do("collect", pet_id=p.pet_id)
                self.assertEqual(g.inventory.count(item), 1)
                coins = g.meta["coins"]
                self.do("sell_item", item=item)
                self.assertEqual(g.meta["coins"], coins+C.ITEMS[item]["sell"])

    def test_purchased_parents_can_breed_newborn(self):
        g = self.game
        for species in NEW_SPECIES:
            with self.subTest(species=species):
                a,b = self.buy(species), self.buy(species)
                for i,p in enumerate((a,b)):
                    p.sex = ("雌","雄")[i]
                    p.birth_ts = g.now-p.lifespan_max*.3
                    p.favor = 50
                    p.last_stage = "成年"
                    p.illness_budget = 1e9
                before = set(g.pets)
                with patch.object(C, "MUTATION_CHANCE", 0):
                    self.do("breed", first_id=a.pet_id, second_id=b.pet_id)
                with patch.object(C,"EVENT_CHANCE",0):
                    g.advance(g.meta["real_anchor"]+C.PREGNANCY_DURATION)
                child = g.pets[(set(g.pets)-before).pop()]
                self.assertEqual(child.species, species)
                self.assertEqual(child.age_seconds(g.now), 0)
                for p in (a,b,child):
                    self.do("sell_pet", pet_id=p.pet_id)

    def test_new_pet_foster_and_restart_preserve_archive(self):
        g = self.game
        self.do("build", building_id=7, kind="寄养屋")
        p = self.buy("猫咪")
        self.do("move", pet_id=p.pet_id, building_id=7)
        original_birth = p.birth_ts
        g.db.close()
        self.game = Game(Database(self.path), real_now=g.meta["real_anchor"]+C.DAY, seed=42)
        p = self.game.pets[p.pet_id]
        self.assertTrue(p.fostered)
        self.assertEqual(p.birth_ts, original_birth)
        self.assertEqual(p.age_seconds(self.game.now), 0)
        self.assertIn("猫咪", self.game.codex)

    def test_classic_and_expanded_collection_awards_are_independent(self):
        g = self.game
        g.codex = {s:{"rare":False,"natural_death":False} for s in C.CLASSIC_SPECIES}
        achievement.check(g)
        self.assertIn("collector", g.achievements)
        self.assertNotIn("all_species", g.achievements)
        for s in NEW_SPECIES:
            g.codex[s] = {"rare":False,"natural_death":False}
        achievement.check(g)
        self.assertIn("all_species", g.achievements)
        coins = g.meta["coins"]
        achievement.check(g)
        self.assertEqual(g.meta["coins"], coins)

    def test_orders_account_for_species_cost_and_products(self):
        g = self.game
        seen = set()
        for _ in range(80):
            for order in event.create_orders(g.now, g.rng):
                if order["kind"] == "pet":
                    species = order["species"]
                    seen.add(species)
                    self.assertGreaterEqual(order["reward"], g.pet_buy_price(species)*C.ORDER_PET_PRICE_MULT)
        self.assertTrue(set(NEW_SPECIES) <= seen)


if __name__ == "__main__":
    unittest.main()
