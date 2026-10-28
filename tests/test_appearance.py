"""个体外观的稳定性、动画覆盖、缓存隔离与旧档兼容。"""
import os
os.environ['SDL_VIDEODRIVER'] = 'dummy'
os.environ['SDL_AUDIODRIVER'] = 'dummy'
import tempfile
import unittest
from unittest.mock import patch
from pathlib import Path
import pygame
from appearance import rabbit_variant,pet_variant
from db import Database,SaveError
from game import Game,GameError
from interface import ManorApp
from pet_art import PetArtRenderer, PetActor, POSE_FRAMES
import town_art


def pixels(surface):
    return pygame.image.tostring(surface,'RGBA')


class AppearanceTests(unittest.TestCase):
    def setUp(self):
        pygame.init()
        pygame.display.set_mode((1280,860))
        self.renderer = PetArtRenderer()

    def tearDown(self):
        pygame.quit()

    def test_same_shirt_color_still_has_distinct_people(self):
        images = [town_art.person_sprite(1,False,False,-1,f'person-{i}') for i in range(24)]
        self.assertGreaterEqual(len({pixels(s) for s in images}),22)
        self.assertGreater(len({pygame.mask.from_surface(s).count() for s in images}),12)
        self.assertIs(images[0],town_art.person_sprite(1,False,False,-1,'person-0'))
        for frame in (-1,0,1,2,3):
            for wet in (False,True):
                s = town_art.person_sprite(1,True,wet,frame,'person-0',-1)
                self.assertEqual(s.get_size(),(72,106))
                self.assertGreater(pygame.mask.from_surface(s).count(),700)

    def test_rabbit_appearance_varies_and_caches_by_identity(self):
        variants = [rabbit_variant(f'rabbit-{i}') for i in range(24)]
        images = [self.renderer.portrait('兔子','成年',height=140,appearance=v) for v in variants]
        self.assertGreaterEqual(len({pixels(s) for s in images}),20)
        self.assertIs(images[0],self.renderer.portrait('兔子','成年',height=140,appearance=variants[0]))
        for stage in ('幼崽','成年','老年'):
            for rare,sick in ((False,False),(True,False),(False,True)):
                for pose,count in POSE_FRAMES.items():
                    for variant in (0,15,43,95,183,383):
                        sprite = self.renderer.sprite('兔子',stage,rare,sick,pose,count-1,'night',128,appearance=variant)
                        self.assertGreater(pygame.mask.from_surface(sprite).count(),100)

    def test_actor_and_portrait_use_the_same_rabbit_variant(self):
        actor = PetActor('rabbit-identity','团子','兔子',stage='成年',x=80,y=140)
        target = pygame.Surface((200,200),pygame.SRCALPHA)
        self.renderer.draw(target,actor)
        expected = self.renderer.sprite('兔子','成年',False,False,'idle',0,'day',128,
                                        appearance=rabbit_variant(actor.pet_id))
        expected_target = pygame.Surface((200,200),pygame.SRCALPHA)
        expected_target.blit(expected,expected.get_rect(midbottom=(80,140)))
        self.assertEqual(pixels(target),pixels(expected_target))
        portrait = self.renderer.portrait('兔子','成年',height=128,frame=0,appearance=rabbit_variant(actor.pet_id))
        self.assertEqual(pixels(expected),pixels(portrait))

    def test_old_save_reload_and_render_do_not_change_game_rng(self):
        with tempfile.TemporaryDirectory() as folder:
            path = Path(folder)/'appearance.db'
            g = Game(Database(path),real_now=1800000000.,seed=7)
            try:
                rng = g.rng.getstate()
                pet = g.living[0]
                before = rabbit_variant(pet.pet_id)
                person = g.people[0]
                person_before = pixels(town_art.person_sprite(person['color_index'],False,False,0,person['person_id']))
                self.renderer.portrait('兔子','幼崽',appearance=before)
                self.assertEqual(rng,g.rng.getstate())
                g.save()
                g.db.close()
                g = Game(Database(path),real_now=1800000000.,seed=999)
                self.assertEqual(before,rabbit_variant(g.pets[pet.pet_id].pet_id))
                reloaded = next(p for p in g.people if p['person_id']==person['person_id'])
                self.assertEqual(person_before,pixels(town_art.person_sprite(reloaded['color_index'],False,False,0,reloaded['person_id'])))
            finally:
                g.db.close()


class AppearanceSelectionTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.path = Path(self.temp.name)/'game.db'
        self.game = Game(Database(self.path),real_now=1800000000.,seed=42)
        self.clock = patch('game.time.time',return_value=self.game.meta['real_anchor'])
        self.clock.start()
        self.app = ManorApp(self.game,settings_path=Path(self.temp.name)/'settings.json',scene_mode='2d')
        self.pet_id = self.game.living[0].pet_id

    def tearDown(self):
        self.clock.stop()
        self.game.db.close()
        pygame.quit()
        self.temp.cleanup()

    def click_label(self, label):
        button = next(b for b in self.app.modal.buttons if b.label==label or b.label.startswith(label+' · 已选'))
        self.app.modal.scroll = min(self.app.modal.max_scroll,max(0,button.rect.y-200))
        self.app.handle(pygame.event.Event(pygame.MOUSEBUTTONDOWN,button=1,pos=button.rect.move(self.app.modal.offset()).center))

    def test_preview_cancel_save_and_reload(self):
        app,g = self.app,self.game
        app.open('pet',{'pet_id':self.pet_id})
        self.click_label('选择外观')
        before = g.snapshot()
        self.click_label('炭灰')
        self.click_label('花斑')
        self.click_label('单侧垂耳')
        self.assertEqual(before,g.snapshot())
        selected = app.page[1]['variant']
        app.draw()
        output=Path(__file__).resolve().parents[1]/'.test-output'/'appearance-picker.png'
        pygame.image.save(app.screen,str(output))
        self.click_label('取消')
        self.assertEqual(before,g.snapshot())
        app.open('appearance',{'pet_id':self.pet_id,'variant':selected})
        self.click_label('保存外观')
        self.assertEqual(app.page,('pet',{'pet_id':self.pet_id}))
        self.assertEqual(pet_variant(self.pet_id,g.meta),selected)
        self.assertEqual(app.actors[self.pet_id].appearance,selected)
        g.db.close()
        self.game = Game(Database(self.path),real_now=1800000000.)
        self.assertEqual(pet_variant(self.pet_id,self.game.meta),selected)

    def test_purchase_uses_selected_preview(self):
        app,g = self.app,self.game
        g.buildings[2].unlocked=True
        g.buildings[2].kind='商店'
        g.buildings[2].level=1
        app.open('buy_pet',{'species':'兔子'})
        self.click_label('选择兔子外观')
        self.click_label('肉桂')
        self.click_label('垂耳')
        selected = app.page[1]['variant']
        self.click_label('使用此样式')
        self.assertEqual(app.page[1]['appearance'],selected)
        ids = set(g.pets)
        self.click_label('购买雌性（母）')
        added = set(g.pets)-ids
        self.assertEqual(len(added),1)
        self.assertEqual(pet_variant(added.pop(),g.meta),selected)

    def test_invalid_variants_rejected_and_save_failure_rolls_back(self):
        g = self.game
        for value in (-1,384,True,'1'):
            with self.assertRaises(GameError):
                g.perform('pet_appearance',pet_id=self.pet_id,variant=value)
        before = g.snapshot()
        with patch.object(g.db,'save',side_effect=SaveError('磁盘错误')):
            with self.assertRaises(SaveError):
                g.perform('pet_appearance',pet_id=self.pet_id,variant=20)
        self.assertEqual(before,g.snapshot())
