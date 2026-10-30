"""天气确定性、街区经济/人口/迁移，以及纯摄像机坐标与边界测试。"""
import copy
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch
import game_config as C
from game import Game,GameError
from db import Database,SaveError,validate,SCHEMA_VERSION
from camera import Camera
from weather import day_phase
from pet import Pet


class CameraTests(unittest.TestCase):
    def test_round_trip_coordinates_at_multiple_zoom_levels(self):
        c = Camera(C.WORLD_SIZE)
        for zoom in (.6,1,1.4):
            c.zoom = zoom
            for point in ((1200,900),(2100,1400),(0,0)):
                back = c.screen_to_world(c.world_to_screen(point))
                self.assertAlmostEqual(back[0],point[0])
                self.assertAlmostEqual(back[1],point[1])

    def test_drag_direction_bounds_and_center(self):
        c = Camera(C.WORLD_SIZE)
        x,y = c.x,c.y
        c.pan(100,60)
        self.assertEqual((c.x,c.y),(x-100,y-60))
        c.pan(100000,100000)
        self.assertEqual((c.x,c.y),(0,0))
        c.pan(-100000,-100000)
        self.assertEqual((c.x,c.y),(C.WORLD_SIZE[0]-1280,C.WORLD_SIZE[1]-616))
        c.center((1800,1200))
        self.assertEqual(c.world_to_screen((1800,1200)),(640,396))

    def test_zoom_keeps_pointer_anchor_and_is_limited(self):
        c = Camera(C.WORLD_SIZE)
        screen = (620,380)
        before = c.screen_to_world(screen)
        c.zoom_at(screen,1.2)
        after = c.screen_to_world(screen)
        self.assertAlmostEqual(before[0],after[0])
        self.assertAlmostEqual(before[1],after[1])
        c.zoom_at(screen,100)
        self.assertEqual(c.zoom,1.4)
        c.zoom_at(screen,.0001)
        self.assertEqual(c.zoom,.6)


class TownWeatherTests(unittest.TestCase):
    def setUp(self):
        rules = patch.multiple(C,EVENT_CHANCE=0,FIGHT_CHANCE=0,FIGHT_HUNGER_BONUS=0,FIGHT_UNHAPPY_BONUS=0,FIGHT_CROWD_BONUS=0)
        rules.start()
        self.addCleanup(rules.stop)
        out = C.ROOT/".test-output"
        out.mkdir(exist_ok=True)
        self.temp = tempfile.TemporaryDirectory(dir=out)
        self.path = Path(self.temp.name)/"town.db"
        self.t = 1800000000.0
        self.g = Game(Database(self.path),real_now=self.t,seed=8)
        self.g.meta['settlement']['enabled']=False  # 此组隔离验证原有街区收支与迁入。
        self.g.meta['settlement']['population_mode']='legacy'
        self.g.meta["coins"] = 100000
        self.g.meta["next_visitor_ts"] = self.t+1000*C.DAY
        for p in self.g.living:
            p.illness_budget = 1e9
        self.g.save()

    def tearDown(self):
        if self.g.db.conn:
            self.g.db.close()
        self.temp.cleanup()

    def do(self,action,**kwargs):
        return self.g.perform(action,real_now=self.g.meta["real_anchor"],**kwargs)

    def shift(self,delta):
        self.g.advance(self.g.meta["real_anchor"]+delta)

    def test_initial_world_has_six_districts_and_visible_population(self):
        self.assertEqual(len(self.g.town_zones),len(C.TOWN_ZONES))
        self.assertEqual(self.g.town.level("estate"),1)
        self.assertEqual(sum(z["level"] for z in self.g.town_zones),1)
        self.assertEqual(sum(p["role"] == "居民" for p in self.g.people),4)
        self.assertEqual(sum(p["role"] == "游客" for p in self.g.people),2)
        self.assertGreater(C.WORLD_SIZE[0],C.WINDOW[0]*2)

    def test_utc_day_night_boundaries(self):
        for hour,phase in ((0,"night"),(5,"dawn"),(7.5,"day"),(17,"dusk"),(19.5,"night")):
            self.assertEqual(day_phase(hour*C.HOUR),phase)

    def test_weather_changes_and_forecast_are_deterministic(self):
        rng_before = self.g.rng.getstate()
        forecast = self.g.weather.forecast()
        self.assertEqual(forecast,self.g.weather.forecast())
        self.assertEqual(self.g.rng.getstate(),rng_before)
        self.shift(C.WEATHER_INTERVAL)
        self.assertEqual(self.g.weather.current,forecast)
        self.assertEqual(self.g.meta["next_weather_ts"],self.t+2*C.WEATHER_INTERVAL)

    def test_weather_restart_does_not_reset_forecast_or_clock(self):
        self.shift(C.WEATHER_INTERVAL)
        kind,forecast = self.g.weather.current,self.g.weather.forecast()
        anchor = self.g.meta["real_anchor"]
        self.g.db.close()
        self.g = Game(Database(self.path),real_now=anchor)
        self.assertEqual((self.g.weather.current,self.g.weather.forecast()),(kind,forecast))

    def test_weather_affects_outdoors_and_not_indoor_pets(self):
        from building import Building
        a = copy.deepcopy(self.g.living[0])
        b = copy.deepcopy(a)
        a.personality = b.personality = "粘人"
        self.g.meta["weather"] = "暴雨"
        outdoor = Building(5,True,"牧场",1)
        indoor = self.g.buildings[0]
        dirt,damage = self.g.weather.outdoor_effect(outdoor)
        self.assertGreater(dirt,1)
        self.assertGreater(damage,0)
        self.assertEqual(self.g.weather.outdoor_effect(indoor),(1,0))
        a.advance(self.g.now+C.HOUR,outdoor,"春",self.g.rng,weather_dirt=dirt,weather_damage=damage)
        b.advance(self.g.now+C.HOUR,indoor,"春",self.g.rng)
        self.assertLess(a.cleanliness,b.cleanliness)
        self.assertLess(a.health,b.health)

    def test_zone_unlock_upgrade_and_maximum(self):
        cost = self.g.town.upgrade_cost("market")
        coins = self.g.meta["coins"]
        self.do("upgrade_zone",zone_id="market")
        self.assertEqual(self.g.meta["coins"],coins-cost)
        self.assertEqual(self.g.town.level("market"),1)
        self.do("upgrade_zone",zone_id="market")
        self.do("upgrade_zone",zone_id="market")
        before = self.g.snapshot()
        with self.assertRaises(GameError):
            self.do("upgrade_zone",zone_id="market")
        self.assertEqual(self.g.snapshot(),before)

    def test_invalid_zone_and_insufficient_money_roll_back(self):
        self.g.meta["coins"] = 0
        before = self.g.snapshot()
        for zone in ("station","unknown","estate"):
            with self.assertRaises(GameError):
                self.do("upgrade_zone",zone_id=zone)
            self.assertEqual(self.g.snapshot(),before)

    def test_market_spending_is_once_per_visit_including_restart(self):
        self.do("upgrade_zone",zone_id="market")
        coins = self.g.meta["coins"]
        self.shift(C.TOWN_SPEND_DELAY)
        self.assertEqual(self.g.meta["coins"],coins+2*C.TOWN_SPEND_PER_LEVEL)
        self.assertTrue(all(p["spent"] for p in self.g.people if p["role"] == "游客"))
        self.g.db.close()
        self.g = Game(Database(self.path),real_now=self.g.meta["real_anchor"])
        self.assertEqual(self.g.meta["coins"],coins+2*C.TOWN_SPEND_PER_LEVEL)

    def test_residential_population_and_daily_rent(self):
        self.do("upgrade_zone",zone_id="homes")
        self.assertEqual(sum(p["role"] == "居民" for p in self.g.people),12)
        coins = self.g.meta["coins"]
        self.shift(C.DAY)
        self.assertEqual(self.g.meta["coins"],coins+12*C.TOWN_RENT_PER_PERSON)
        self.assertTrue(all(p["home_zone"] == "homes" for p in self.g.people if p["role"] == "居民"))

    def test_visitors_arrive_leave_then_are_removed(self):
        visitors = {p["person_id"] for p in self.g.people if p["role"] == "游客"}
        self.shift(C.TOWN_VISIT_INTERVAL)
        self.assertGreater(self.g.meta["town_visitors_total"],2)
        self.shift(C.TOWN_VISIT_STAY-C.TOWN_VISIT_INTERVAL)
        self.assertTrue(all(p["status"] == "离镇" for p in self.g.people if p["person_id"] in visitors))
        self.shift(C.TOWN_EXIT_TIME)
        self.assertFalse(any(p["person_id"] in visitors for p in self.g.people))

    def test_bad_weather_reduces_arrivals_and_station_increases_them(self):
        def next_arrivals(weather,station):
            g = self.g
            before = g.meta["town_visitors_total"]
            g.meta["weather"] = weather
            g.meta["next_weather_ts"] = g.now+10*C.DAY
            next(z for z in g.town_zones if z["zone_id"] == "station")["level"] = station
            self.shift(C.TOWN_VISIT_INTERVAL)
            return g.meta["town_visitors_total"]-before
        rain = next_arrivals("暴雨",0)
        clear = next_arrivals("晴天",0)
        train = next_arrivals("晴天",2)
        self.assertLess(rain,clear)
        self.assertGreater(train,clear)

    def test_field_harvest_respects_capacity_and_cannot_repeat(self):
        self.do("upgrade_zone",zone_id="fields")
        self.shift(C.TOWN_FIELD_INTERVAL)
        batch = dict(self.g.meta["field_batch"])
        self.assertTrue(batch)
        self.g.inventory.add("谷物",self.g.storage_capacity-self.g.inventory.used,self.g.now,self.g.storage_capacity)
        before = self.g.snapshot()
        with self.assertRaises(GameError):
            self.do("harvest_field")
        self.assertEqual(self.g.snapshot(),before)
        self.do("discard",item="谷物",quantity=sum(batch.values()))
        old_grain,old_grass = self.g.inventory.count("谷物"),self.g.inventory.count("鲜草")
        self.do("harvest_field")
        self.assertEqual(self.g.inventory.count("谷物"),old_grain+batch["谷物"])
        self.assertEqual(self.g.inventory.count("鲜草"),old_grass+batch["鲜草"])
        with self.assertRaises(GameError):
            self.do("harvest_field")

    def test_population_and_weather_match_bulk_vs_incremental_offline(self):
        state = self.g.snapshot()
        other = Game(Database(Path(self.temp.name)/"other.db"),real_now=self.t)
        try:
            other._load(copy.deepcopy(state))
            self.shift(C.DAY)
            for i in range(48):
                other.advance(self.t+(i+1)*C.HOUR/2)
            self.assertEqual(self.g.people,other.people)
            self.assertEqual(self.g.weather.current,other.weather.current)
            self.assertEqual(self.g.meta["town_visitors_total"],other.meta["town_visitors_total"])
        finally:
            other.db.close()

    def test_zone_save_failure_restores_population_and_money(self):
        before = self.g.snapshot()
        with patch.object(self.g.db,"save",side_effect=SaveError("磁盘满")):
            with self.assertRaises(SaveError):
                self.do("upgrade_zone",zone_id="homes")
        self.assertEqual(self.g.snapshot(),before)

    def test_residential_expansion_during_full_tourism_waits_for_space(self):
        g = self.g
        residents = [p for p in g.people if p["role"] == "居民"]
        g.people = residents+[g.town._person("游客",g.now,i) for i in range(C.TOWN_MAX_PEOPLE-len(residents))]
        g.meta["town_visitors_total"] = C.TOWN_MAX_PEOPLE-len(residents)
        self.do("upgrade_zone",zone_id="homes")
        self.assertEqual(len(g.people),C.TOWN_MAX_PEOPLE)
        self.assertEqual(sum(p["role"] == "居民" for p in g.people),4)
        self.shift(C.TOWN_VISIT_STAY+C.TOWN_EXIT_TIME)
        self.assertEqual(sum(p["role"] == "居民" for p in g.people),12)
        self.assertLessEqual(len(g.people),C.TOWN_MAX_PEOPLE)

    def test_v3_upgrade_preserves_pets_and_starts_town_at_upgrade_time(self):
        g = self.g
        ids = set(g.pets)
        with g.db.conn:
            for table in ("people","town_zones"):
                g.db.conn.execute(f"DROP TABLE {table}")
            for key in list(g.meta):
                if key.startswith(("town_","weather","next_weather","next_population","next_town_","next_rent","field_","next_field")):
                    g.db.conn.execute("DELETE FROM meta WHERE key=?",(key,))
            g.db.conn.execute("PRAGMA user_version=3")
        g.db.close()
        self.g = Game(Database(self.path),real_now=self.t+C.DAY)
        self.assertEqual(self.g.db.conn.execute("PRAGMA user_version").fetchone()[0],SCHEMA_VERSION)
        self.assertEqual(set(self.g.pets),ids)
        self.assertEqual(self.g.meta["town_open_ts"],self.g.now)
        self.assertEqual(self.g.meta["town_income"],0)
        self.assertEqual(len(self.g.people),6)
        validate(self.g.snapshot())


if __name__ == "__main__":
    unittest.main()
