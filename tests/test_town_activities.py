"""可见人物活动仅在开放区域到达后发生，且不改变经营数据。"""
import os
os.environ['SDL_VIDEODRIVER']='dummy'
os.environ['SDL_AUDIODRIVER']='dummy'
import tempfile
import unittest
from pathlib import Path
import pygame
from db import Database
from game import Game
from interface import ManorApp
from traffic import RoadWalker
from town_grid import PORTALS,cell_center
from world3d import person_mesh


class TownActivityTests(unittest.TestCase):
    def setUp(self):
        self.temp=tempfile.TemporaryDirectory();self.folder=Path(self.temp.name)
        self.game=Game(Database(self.folder/'game.db'),real_now=1800000000,seed=7)
        self.app=ManorApp(self.game,settings_path=self.folder/'ui.json',scene_mode='2d')
        self.person=self.game.people[0];self.person['status']='生活';self.game.people=[self.person]
        self.addCleanup(self.temp.cleanup);self.addCleanup(pygame.quit);self.addCleanup(self.game.db.close)

    def at(self,zone,opened):
        next(z for z in self.game.town_zones if z['zone_id']==zone)['level']=1 if opened else 0
        self.person['destination']=zone
        actor=RoadWalker(self.person['person_id'],*cell_center(*PORTALS[zone]))
        actor.goal=zone;actor.target=zone;actor.revision=self.game.construction.grid.road_revision
        self.app.courtyard.people={actor.ident:actor}
        return actor

    def tick(self):self.app.courtyard.tick(.25,self.game,[])

    def test_only_open_and_reached_locations_start_actions_without_game_effects(self):
        for zone,action in (('park','read'),('fields','farm')):
            actor=self.at(zone,False);self.tick();self.assertEqual(actor.action,'')
            actor=self.at(zone,True);before=self.game.snapshot();rng=self.game.rng.getstate()
            self.tick();self.assertEqual(actor.action,action)
            self.assertEqual(self.game.snapshot(),before);self.assertEqual(self.game.rng.getstate(),rng)
            for _ in range(10):self.tick()
            self.assertEqual(actor.action,action)
            for _ in range(12):self.tick()
            self.assertEqual(actor.action,'')

    def test_departure_and_road_change_interrupt_actions(self):
        actor=self.at('park',True);self.tick();self.assertEqual(actor.action,'read')
        self.person['status']='离镇';self.person['destination']='station';self.tick()
        self.assertEqual(actor.action,'');self.assertEqual(actor.target,'station')
        self.person['status']='生活';actor=self.at('fields',True);self.tick();self.assertEqual(actor.action,'farm')
        self.game.construction.grid.road_revision+=1;self.tick();self.assertEqual(actor.action,'')

    def test_actions_have_distinct_2d_and_3d_appearances(self):
        actor=self.at('park',True);self.tick()
        image=self.app.courtyard._person_sprite(actor,self.person,False)
        actor.action='';plain=self.app.courtyard._person_sprite(actor,self.person,False)
        self.assertNotEqual(pygame.image.tostring(image,'RGBA'),pygame.image.tostring(plain,'RGBA'))
        self.assertNotEqual(person_mesh(actor.ident,0,False,'read').data,person_mesh(actor.ident,0,False,'').data)
        self.assertNotEqual(person_mesh(actor.ident,0,False,'farm').data,person_mesh(actor.ident,0,False,'').data)
