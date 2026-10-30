import math
import tempfile
import unittest
from pathlib import Path
from types import SimpleNamespace
import pygame
import game_config as C
from db import Database
from game import Game
from traffic import RoadWalker
from town_grid import PORTALS,cell_center
from town_care import TownCare,farm_path
import courtyard


class Farm:
    def __init__(self):self.building_hits={}
    def _layout_buildings(self,g):return self.building_hits


class View:
    def __init__(self):self.farm=Farm()
    def local_to_world(self,pos):return pos[0]+C.FARM_WORLD_RECT[0],pos[1]+C.FARM_WORLD_RECT[1]-88


class TownCareTests(unittest.TestCase):
    def setUp(self):
        self.temp=tempfile.TemporaryDirectory();self.g=Game(Database(Path(self.temp.name)/'game.db'),real_now=1800000000,seed=2)
        self.addCleanup(self.temp.cleanup);self.addCleanup(self.g.db.close)
        self.g.meta['caretaker']=True;self.view=View();self.care=TownCare()
        pet=self.g.living[0];pet.hunger=20
        self.pet=SimpleNamespace(pet_id=pet.pet_id,x=800.,y=600.,react=lambda *args:None,sync_from_pet=lambda *args:None)
        person=self.g.people[0];self.person=person
        self.actor=RoadWalker(person['person_id'],*cell_center(*PORTALS['estate']))
        self.actor.target='estate';self.actor.wait=1;self.actor.revision=self.g.construction.grid.road_revision

    def test_path_avoids_obstacles_and_outside_targets(self):
        self.view.farm.building_hits={1:pygame.Rect(700,530,60,90)}
        path=farm_path(self.view,self.pet);self.assertTrue(path)
        obstacle=self.view.farm.building_hits[1].inflate(36,36)
        self.assertFalse(any(obstacle.collidepoint(p) for p in path))
        self.pet.y=700;self.assertFalse(farm_path(self.view,self.pet))

    def test_hungry_pet_at_bowl_is_reachable_from_edge(self):
        self.pet.x,self.pet.y=courtyard.BOWL
        path=farm_path(self.view,self.pet)
        self.assertTrue(path)
        self.assertLessEqual(math.dist(path[-1],courtyard.BOWL),55)
        self.assertFalse(pygame.Rect(courtyard.BOWL[0]-40,courtyard.BOWL[1]-40,80,80).collidepoint(path[-1]))

    def test_real_feed_requires_reaching_pet_and_runs_once(self):
        if not self.g.inventory.count('普通饲料'):
            self.g.inventory.add('普通饲料',2,self.g.now,self.g.storage_capacity)
        before=self.g.inventory.count('普通饲料');hunger=self.g.pets[self.pet.pet_id].hunger
        self.care.start(self.view,self.actor,self.person,self.g,[self.pet])
        self.assertIsNotNone(self.care.session)
        self.assertEqual(self.g.inventory.count('普通饲料'),before)
        for _ in range(4000):
            self.care.update(self.view,self.actor,self.person,self.g,[self.pet],.25)
            if self.care.session is None:break
        self.assertIsNone(self.care.session)
        self.assertEqual(self.g.inventory.count('普通饲料'),before-1)
        self.assertGreater(self.g.pets[self.pet.pet_id].hunger,hunger)
        self.care.start(self.view,self.actor,self.person,self.g,[self.pet])
        self.assertIsNone(self.care.session)

    def test_no_care_if_disabled_or_pet_unreachable(self):
        self.g.meta['caretaker']=False
        self.care.start(self.view,self.actor,self.person,self.g,[self.pet]);self.assertIsNone(self.care.session)
        self.g.meta['caretaker']=True;self.pet.y=900;self.actor.visits+=1
        self.care.start(self.view,self.actor,self.person,self.g,[self.pet]);self.assertIsNone(self.care.session)

    def test_visitor_departure_returns_without_feeding(self):
        before=self.g.inventory.count('普通饲料')
        self.care.start(self.view,self.actor,self.person,self.g,[self.pet])
        self.assertIsNotNone(self.care.session)
        for _ in range(10):self.care.update(self.view,self.actor,self.person,self.g,[self.pet],.25)
        self.person['status']='离镇'
        for _ in range(4000):
            self.care.update(self.view,self.actor,self.person,self.g,[self.pet],.25)
            if self.care.session is None:break
        self.assertIsNone(self.care.session)
        self.assertEqual(before,self.g.inventory.count('普通饲料'))

    def test_road_change_returns_along_safe_path_without_feeding(self):
        self.view.farm.building_hits={1:pygame.Rect(700,530,60,90)}
        obstacle=self.view.farm.building_hits[1].inflate(36,36)
        before=self.g.inventory.count('普通饲料')
        self.care.start(self.view,self.actor,self.person,self.g,[self.pet])
        while self.care.session['index']<4:
            self.care.update(self.view,self.actor,self.person,self.g,[self.pet],.25)
        self.g.construction.grid.road_revision+=1
        for _ in range(4000):
            self.care.update(self.view,self.actor,self.person,self.g,[self.pet],.25)
            x=self.actor.x-C.FARM_WORLD_RECT[0];y=self.actor.y-C.FARM_WORLD_RECT[1]+88
            self.assertFalse(obstacle.collidepoint(x,y))
            if self.care.session is None:break
        self.assertIsNone(self.care.session)
        self.assertEqual(before,self.g.inventory.count('普通饲料'))

    def test_real_view_feeds_hungry_pet_at_bowl(self):
        from interface import ManorApp
        app=ManorApp(self.g,settings_path=Path(self.temp.name)/'ui.json',scene_mode='2d')
        self.addCleanup(pygame.quit)
        pet_actor=next(p for p in app.actor_order if p.pet_id==self.pet.pet_id)
        pet_actor.x,pet_actor.y=courtyard.BOWL
        self.g.people=[self.person]
        self.actor.goal=self.person['destination'];self.actor.points=[];self.actor.index=0
        app.courtyard.people={self.actor.ident:self.actor}
        before=self.g.inventory.count('普通饲料')
        for _ in range(500):
            app.courtyard.tick(.25,self.g,app.actor_order)
            if self.g.inventory.count('普通饲料')<before:break
        self.assertEqual(self.g.inventory.count('普通饲料'),before-1)
