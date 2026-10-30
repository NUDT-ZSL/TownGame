import copy
import json
import math
import sqlite3
import tempfile
import unittest
from pathlib import Path
from contextlib import closing
from types import SimpleNamespace
import game_config as C
from db import Database,validate
from game import Game
from signals import Signals,phase
from traffic import RoadWalker,TrafficSystem,Vehicle
from town_grid import RoadGrid,default_roads,cell_center,PORTALS
from physics import vehicle_contains


class SignalWorldTests(unittest.TestCase):
    def test_lights_have_exclusive_vehicle_and_pedestrian_phases(self):
        signals=Signals();signals.junctions=((22,13),)
        x,y=cell_center(22,13)
        for t in range(32):
            signals.time=t
            permitted=[signals.allows(1,0),signals.allows(0,1),signals.allows(1,0,True)]
            self.assertLessEqual(sum(permitted),1)
        signals.time=0
        self.assertTrue(signals.stop(x-60,y,x-50,y,half_length=27))
        self.assertFalse(signals.stop(x-20,y,x-10,y,half_length=27),'路口内继续清空')
        self.assertTrue(signals.stop(x-30,y,x-20,y,pedestrian=True))
        signals.time=23
        self.assertFalse(signals.stop(x-30,y,x-20,y,pedestrian=True))

    def test_walker_can_get_around_a_waiting_car(self):
        grid=RoadGrid(default_roads(0));x,y=cell_center(*PORTALS['estate'])
        actor=RoadWalker('a100',x-35,y,points=[(x+100,y)],index=0,goal='estate',revision=0)
        car=SimpleNamespace(x=x,y=y,angle=90,kind='car')
        for _ in range(160):
            actor.update(.05,{'destination':'estate','role':'居民'},grid,vehicles=[car])
            self.assertFalse(vehicle_contains(car,(actor.x,actor.y),4))
        self.assertGreater(actor.x,x+30,'应走出相互等待的位置')

    def test_vehicle_remains_at_red_and_moves_at_green(self):
        with tempfile.TemporaryDirectory() as folder:
            g=Game(Database(Path(folder)/'game.db'),real_now=1800000000,seed=3)
            try:
                traffic=TrafficSystem();traffic.target_count=lambda game:1
                x,y=cell_center(22,13)
                car=Vehicle('test','car',(120,130,140),x-100,y+7,[],(24,13),'A','B',points=[(x+100,y+7)],index=0,revision=g.meta['road_revision'],angle=90)
                traffic.vehicles=[car]
                for _ in range(100):traffic.update(.05,g)
                self.assertEqual(car.state,'红灯等待');self.assertIn(car,traffic.vehicles)
                self.assertLess(car.x,x-52)
                before=car.x;traffic.signals.time=12
                for _ in range(20):traffic.update(.05,g)
                self.assertGreater(car.x,before+10)
            finally:g.db.close()

    def test_expansion_migration_preserves_old_city_and_adds_connected_zones(self):
        with tempfile.TemporaryDirectory() as folder:
            path=Path(folder)/'old.db';g=Game(Database(path),real_now=1800000000,seed=4)
            before=g.snapshot();g.db.close()
            with closing(sqlite3.connect(path)) as conn, conn:
                conn.execute("DELETE FROM town_zones WHERE zone_id IN ('ranch','industry','city')")
                for row in conn.execute('SELECT road_id,data FROM roads').fetchall():
                    road=json.loads(row[1])
                    if road['cx']>60 or (road['cx']==37 and road['cy']>30):conn.execute('DELETE FROM roads WHERE road_id=?',(row[0],))
                conn.execute('PRAGMA user_version=5')
            db=Database(path)
            try:
                after=db.load();validate(after)
                self.assertEqual(after['pets'],before['pets']);self.assertEqual(after['meta']['coins'],before['meta']['coins'])
                self.assertEqual(after['town_buildings'],before['town_buildings'])
                self.assertEqual({z['zone_id'] for z in after['town_zones']},set(C.TOWN_ZONES))
                grid=RoadGrid(after['roads'])
                self.assertTrue(all(grid.is_connected(PORTALS['estate'],PORTALS[z]) for z in ('ranch','industry','city')))
                self.assertEqual(C.WORLD_SIZE,(6000,3840))
            finally:db.close()
