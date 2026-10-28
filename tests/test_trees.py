"""树种、季节、固定外观、景观分布和种植编辑回归。"""
import os
os.environ['SDL_VIDEODRIVER']='dummy'
os.environ['SDL_AUDIODRIVER']='dummy'
import random
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch
import pygame
import ui
from appearance import identity_seed
from db import Database,SaveError,validate
from game import Game,GameError
from interface import ManorApp
from tree_art import SPECIES,tree_sprite,profile
from landscape import world_trees,estate_trees,ground_details
from decorations import CATALOG,placement_reason
from town_grid import terrain,world_to_cell,cell_center


def pixels(s):return pygame.image.tostring(s,'RGBA')


class TreeArtTests(unittest.TestCase):
    def setUp(self):
        pygame.init();pygame.display.set_mode((1280,860))

    def tearDown(self):pygame.quit()

    def test_species_have_distinct_silhouettes_in_each_season(self):
        for season in ('春','夏','秋','冬'):
            images=[tree_sprite(kind,7,season,'day') for kind in SPECIES]
            self.assertEqual(len({pixels(s) for s in images}),6)
            masks=[pygame.mask.from_surface(s) for s in images]
            self.assertGreaterEqual(len({mask.count() for mask in masks}),5)
            self.assertTrue(all(mask.count()>300 for mask in masks))
        self.assertIs(tree_sprite('oak',7,'春','day'),tree_sprite('oak',7,'春','day'))

    def test_individuals_are_stable_and_variants_do_not_share_frames(self):
        for species in SPECIES:
            images=[tree_sprite(species,v,'夏','day') for v in range(24)]
            self.assertGreaterEqual(len({pixels(s) for s in images}),22)
        self.assertEqual(profile('one-tree'),profile('one-tree'))
        self.assertGreater(len({profile(f'tree-{i}') for i in range(24)}),20)

    def test_lighting_and_season_have_visible_changes(self):
        for kind in SPECIES:
            day=tree_sprite(kind,4,'夏','day')
            self.assertNotEqual(pixels(day),pixels(tree_sprite(kind,4,'夏','night')))
            self.assertNotEqual(pixels(day),pixels(tree_sprite(kind,4,'冬','day')))
        oak=tree_sprite('oak',4,'冬','day');summer=tree_sprite('oak',4,'夏','day')
        self.assertLess(pygame.mask.from_surface(oak).count(),pygame.mask.from_surface(summer).count()*.6)

    def test_layout_is_repeatable_safe_and_not_a_regular_grid(self):
        before=random.getstate()
        first=world_trees();world_trees.cache_clear();second=world_trees()
        self.assertEqual(first,second)
        self.assertEqual(before,random.getstate())
        self.assertGreater(len(first),30)
        self.assertEqual({t.species for t in first},set(SPECIES))
        self.assertGreater(len({t.height for t in first}),3)
        self.assertTrue(all(terrain(world_to_cell(t.x,t.y)) in ('grass','zone') for t in first))
        self.assertTrue(ground_details())
        estate=estate_trees()
        self.assertEqual(len(estate),8)
        self.assertEqual(len({t.species for t in estate}),6)
        self.assertGreater(len({t.variant for t in estate}),3)


class TreePlantingTests(unittest.TestCase):
    def setUp(self):
        self.temp=tempfile.TemporaryDirectory();self.path=Path(self.temp.name)/'tree.db'
        self.g=Game(Database(self.path),real_now=1800000000.,seed=42)
        self.clock=patch('game.time.time',return_value=self.g.meta['real_anchor']);self.clock.start()
        self.app=ManorApp(self.g,settings_path=Path(self.temp.name)/'ui.json',scene_mode='2d')

    def tearDown(self):
        self.clock.stop();self.g.db.close();ui.display=None;pygame.quit();self.temp.cleanup()

    def do(self,**args):return self.g.perform('decoration',real_now=self.g.meta['real_anchor'],**args)

    def cells(self):
        return [(x,y) for y in range(18,24) for x in range(1,20) if not placement_reason(self.g,x,y)]

    def click(self,pos):
        pos=self.app.display.to_native(pos)
        for kind in (pygame.MOUSEBUTTONDOWN,pygame.MOUSEBUTTONUP):
            self.app.handle_native(pygame.event.Event(kind,pos=pos,button=1))

    def test_all_tree_kinds_save_reload_and_preserve_game_randomness(self):
        before=self.g.rng.getstate()
        for (kind,_),cell in zip(((k,v) for k,v in CATALOG.items() if k.startswith('tree_')),self.cells()):
            self.do(operation='place',kind=kind,cx=cell[0],cy=cell[1])
        self.assertEqual(len(self.g.meta['decorations']),6)
        self.assertEqual(before,self.g.rng.getstate())
        rows=self.g.meta['decorations'];looks=[profile(d['id'],d['kind'][5:]) for d in rows]
        self.g.db.close();self.g=Game(Database(self.path),real_now=1800000000.)
        self.assertEqual(rows,self.g.meta['decorations'])
        self.assertEqual(looks,[profile(d['id'],d['kind'][5:]) for d in self.g.meta['decorations']])
        validate(self.g.snapshot())

    def test_tree_tab_cards_are_clickable_and_select_tree_kind(self):
        self.app.decorator.enter()
        button=next(b for b in self.app.decorator.buttons() if b.label=='树木种类')
        self.click(button.rect.center)
        cards=[b for b in self.app.decorator.buttons() if isinstance(b,ui.ChoiceCard)]
        self.assertEqual(len(cards),6)
        self.assertTrue(all(self.app.decorator.panel().contains(b.rect) for b in cards))
        self.click(cards[-1].rect.center)
        self.assertEqual(self.app.decorator.kind,'tree_maple')

    def test_canopy_click_selects_tree_for_moving_and_undo_keeps_identity(self):
        a,b=self.cells()[:2]
        self.do(operation='place',kind='tree_willow',cx=a[0],cy=a[1])
        tree=self.g.meta['decorations'][0];initial=profile(tree['id'],'willow')
        app=self.app;app.decorator.enter();app.courtyard.camera.center(cell_center(*a));app.draw()
        rect,ident,image=next(item for item in app.courtyard.decoration_hits if item[1]==tree['id'])
        # 点击树冠而不是主干所在格，必须选中原树，不能意外新种一棵。
        pos=next((rect.x+x,rect.y+y) for y in range(image.get_height()//4,image.get_height()//2)
                 for x in range(image.get_width()) if image.get_at((x,y)).a>180)
        self.click(pos)
        self.assertEqual(app.decorator.moving,tree['id'])
        app.handle(pygame.event.Event(pygame.KEYDOWN,key=pygame.K_r))
        self.click(app.courtyard.camera.world_to_screen(cell_center(*b)))
        self.assertEqual(len(self.g.meta['decorations']),1)
        self.assertEqual(profile(tree['id'],'willow'),initial)
        self.do(operation='undo')
        self.assertEqual((self.g.meta['decorations'][0]['cx'],self.g.meta['decorations'][0]['cy']),a)

    def test_model_and_rng_are_unchanged_by_seasonal_tree_render(self):
        before=self.g.snapshot();rng=self.g.rng.getstate()
        self.app.draw()
        for kind in SPECIES:
            for season in ('春','夏','秋','冬'):tree_sprite(kind,3,season,'night')
        self.assertEqual(before,self.g.snapshot());self.assertEqual(rng,self.g.rng.getstate())

    def test_tree_plant_failure_is_atomic_and_protected_terrain_is_rejected(self):
        before=self.g.snapshot();cell=self.cells()[0]
        with patch.object(self.g.db,'save',side_effect=SaveError('磁盘错误')):
            with self.assertRaises(SaveError):self.do(operation='place',kind='tree_pine',cx=cell[0],cy=cell[1])
        self.assertEqual(before,self.g.snapshot())
        with self.assertRaises(GameError):self.do(operation='place',kind='tree_cherry',cx=30,cy=20)
        self.assertEqual(before,self.g.snapshot())
