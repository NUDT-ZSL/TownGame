"""画面可读性、天气覆盖范围、材质明暗与缩放质量回归。"""
import os
os.environ['SDL_VIDEODRIVER']='dummy'
os.environ['SDL_AUDIODRIVER']='dummy'
import random
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch
import pygame
import art
import ui
import courtyard
from scene_style import weather_layer
from tree_art import tree_sprite
from game import Game
from db import Database
from interface import ManorApp


def light(color):return sum(color[:3])/3


class RenderQualityTests(unittest.TestCase):
    def setUp(self):
        pygame.init();pygame.display.set_mode((1280,860))

    def tearDown(self):
        ui.display=None;pygame.quit()

    def test_night_keeps_ground_readable_instead_of_washing_it_gray(self):
        original=(185,195,166)
        target=pygame.Surface((800,600));target.fill(original)
        target.blit(weather_layer(target.get_size(),'night','阴天',1),(0,0))
        ratio=light(target.get_at((400,300)))/light(original)
        self.assertGreater(ratio,.70)
        self.assertLess(ratio,.93)
        self.assertIsNone(weather_layer((800,600),'day','晴天',1))

    def test_wind_is_sparse_and_subtle(self):
        layer=weather_layer((1280,860),'day','大风',1)
        colored=sum(layer.get_at((x,y)).a>0 for y in range(860) for x in range(1280))
        self.assertLess(colored,1280*860*.005)
        self.assertLessEqual(max(layer.get_at((x,y)).a for y in range(0,860,3) for x in range(0,1280,3)),110)

    def test_weather_covers_wide_and_tall_viewports(self):
        for kind in ('小雨','降雪','大风'):
            layer=weather_layer((1920,1080),'day',kind,1)
            mask=pygame.mask.from_surface(layer,10)
            bounds=mask.get_bounding_rects()
            right=max(r.right for r in bounds);bottom=max(r.bottom for r in bounds)
            self.assertGreater(right,1920*.85)
            self.assertGreater(bottom,1080*.85)

    def test_weather_animation_is_repeatable_and_rng_independent(self):
        state=random.getstate()
        a=weather_layer((800,600),'night','大风',1)
        b=weather_layer((800,600),'night','大风',1)
        c=weather_layer((800,600),'night','大风',2)
        self.assertEqual(pygame.image.tostring(a,'RGBA'),pygame.image.tostring(b,'RGBA'))
        self.assertNotEqual(pygame.image.tostring(a,'RGBA'),pygame.image.tostring(c,'RGBA'))
        self.assertEqual(random.getstate(),state)

    def test_leaf_lobes_have_soft_directional_shading(self):
        image=art.foliage_lobe((80,80),(110,160,100))
        self.assertEqual(image.get_at((0,0)).a,0)
        self.assertGreater(image.get_at((24,24)).g,image.get_at((58,58)).g+10)
        self.assertIs(image,art.foliage_lobe((80,80),(110,160,100)))

    def test_tree_asset_night_tint_does_not_double_blacken_foliage(self):
        day=tree_sprite('oak',7,'夏','day')
        night=tree_sprite('oak',7,'夏','night')
        points=[(x,y) for y in range(day.get_height()) for x in range(day.get_width()) if day.get_at((x,y)).a>220]
        day_mean=sum(light(day.get_at(p)) for p in points)/len(points)
        night_mean=sum(light(night.get_at(p)) for p in points)/len(points)
        self.assertGreater(night_mean/day_mean,.8)
        self.assertLess(night_mean/day_mean,1)

    def test_zoomed_estate_uses_native_text_replay_not_nearest_resize(self):
        with tempfile.TemporaryDirectory() as folder:
            g=Game(Database(Path(folder)/'render.db'),real_now=1800000000.,seed=42)
            try:
                app=ManorApp(g,settings_path=Path(folder)/'ui.json')
                app.courtyard.camera.zoom=.8
                before=g.snapshot();rng=g.rng.getstate()
                with patch('pygame.transform.scale',wraps=pygame.transform.scale) as scale:
                    app.draw()
                    bad=[c for c in scale.call_args_list if c.args[0].get_size()==courtyard.SCENE.size
                         and tuple(c.args[1])==(1024,493)]
                    self.assertFalse(bad)
                self.assertTrue(app.courtyard.farm_surface.recording)
                self.assertEqual(g.snapshot(),before);self.assertEqual(g.rng.getstate(),rng)
            finally:g.db.close()
