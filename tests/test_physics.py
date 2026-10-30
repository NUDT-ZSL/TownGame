import os
os.environ['SDL_VIDEODRIVER']='dummy'
os.environ['SDL_AUDIODRIVER']='dummy'
import math
import random
import tempfile
import unittest
from pathlib import Path
from types import SimpleNamespace
from unittest.mock import patch
import pygame
import courtyard
from physics import separate_pets,pet_radius,vehicle_contains
from pet_art import PetActor
from traffic import RoadWalker,Vehicle
from town_grid import RoadGrid,default_roads,cell_center,PORTALS
from world3d import person_mesh,vehicle_mesh,house_mesh
from db import Database
from game import Game
from interface import ManorApp
from camera3d import Camera3D
from render3d import Renderer3D
from world3d import tree_mesh


class PhysicsTests(unittest.TestCase):
    def test_tree_animation_changes_pixels_with_fixed_camera(self):
        camera=Camera3D((3600,2400));camera.width=640;camera.height=440;camera.zoom=1.8
        renderer=Renderer3D(software=True)
        objects=[(tree_mesh('willow',3,'春'),(1800,1068,0),0,1,None)]
        try:
            first=pygame.image.tobytes(renderer.draw(camera,objects,elapsed=0),'RGB')
            second=pygame.image.tobytes(renderer.draw(camera,objects,elapsed=1),'RGB')
            self.assertNotEqual(first,second)
        finally:renderer.close()

    def test_stationary_animals_are_separated_inside_boundary(self):
        actors=[PetActor(str(i),'猫','猫咪',x=70,y=550) for i in range(5)]
        for _ in range(6):separate_pets(actors,courtyard.PET_ACTIVITY)
        for i,a in enumerate(actors):
            self.assertTrue(courtyard.PET_ACTIVITY.collidepoint(a.x,a.y))
            for b in actors[i+1:]:self.assertGreaterEqual(math.hypot(a.x-b.x,a.y-b.y),pet_radius(a)+pet_radius(b)-.1)

    def test_walker_waits_before_entering_vehicle(self):
        grid=RoadGrid(default_roads(0));x,y=cell_center(*PORTALS['estate'])
        walker=RoadWalker('ab00',x-35,y,points=[(x+100,y)],index=0,goal='estate',revision=0)
        car=SimpleNamespace(x=x,y=y,angle=90,kind='car')
        p={'destination':'estate','role':'居民'}
        for _ in range(30):walker.update(.05,p,grid,vehicles=[car])
        self.assertFalse(vehicle_contains(car,(walker.x,walker.y),4))
        self.assertIn('绕车',walker.activity)

    def test_model_dimensions_have_consistent_order(self):
        human=person_mesh('sample');car=vehicle_mesh('car',(150,160,170));house=house_mesh(120,100,'宠物屋')
        height=human.bounds[1][2]-human.bounds[0][2]
        self.assertGreater(car.bounds[1][1]-car.bounds[0][1],height*2)
        self.assertGreater(house.bounds[1][2],height*4)

    def test_wasd_holds_moves_and_release_focus_stop(self):
        with tempfile.TemporaryDirectory() as folder:
            game=Game(Database(Path(folder)/'test.db'),real_now=1800000000,seed=1)
            app=ManorApp(game,settings_path=Path(folder)/'ui.json',scene_mode='2d')
            try:
                app.last_tick=app.last_save=1e30
                start=(app.courtyard.camera.x,app.courtyard.camera.y)
                app.handle(pygame.event.Event(pygame.KEYDOWN,key=pygame.K_d))
                app.last_frame=100
                with patch('interface.time.monotonic',return_value=100.1):app.tick()
                self.assertGreater(app.courtyard.camera.x,start[0]);self.assertFalse(app.decorator.active)
                app.handle(pygame.event.Event(pygame.KEYUP,key=pygame.K_d))
                self.assertFalse(app.camera_keys)
                app.handle(pygame.event.Event(pygame.KEYDOWN,key=pygame.K_w))
                app.handle(pygame.event.Event(pygame.WINDOWFOCUSLOST))
                self.assertFalse(app.camera_keys)
            finally:game.db.close();pygame.quit()
