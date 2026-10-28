"""缩放后的真实输入、详情面板边界和非打断通知回归。"""
import os
os.environ['SDL_VIDEODRIVER'] = 'dummy'
os.environ['SDL_AUDIODRIVER'] = 'dummy'
os.environ['PYGAME_HIDE_SUPPORT_PROMPT'] = '1'
import tempfile
import time
import unittest
from pathlib import Path
from unittest.mock import patch
import pygame
import ui
import event as events
from db import Database
from display import Display
from game import Game
from interface import ManorApp


class DisplayTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.folder = Path(self.temp.name)
        self.game = Game(Database(self.folder/'game.db'), seed=42)
        self.app = ManorApp(self.game, settings_path=self.folder/'settings.json',scene_mode='2d')
        self.output = Path(__file__).resolve().parents[1]/'.test-output'
        self.output.mkdir(exist_ok=True)

    def tearDown(self):
        self.game.db.close()
        ui.display = None
        pygame.quit()
        self.temp.cleanup()

    def click_native(self, logical):
        pos = self.app.display.to_native(logical)
        for kind in (pygame.MOUSEBUTTONDOWN,pygame.MOUSEBUTTONUP):
            self.app.handle_native(pygame.event.Event(kind,button=1,pos=pos))

    def test_resize_letterbox_and_real_notification_click(self):
        for size in ((800,600),(1920,1080),(1600,1000),(1000,1400)):
            self.app.close_modal()
            self.app.handle_native(pygame.event.Event(pygame.VIDEORESIZE,size=size,w=size[0],h=size[1]))
            display = self.app.display
            self.assertEqual(display.window.get_size(),size)
            for point in ((0,0),(640,430),(1250,830)):
                mapped = display.to_logical(display.to_native(point))
                self.assertLessEqual(max(abs(a-b) for a,b in zip(mapped,point)),1)
            self.click_native(self.app.notification_button.rect.center)
            self.assertEqual(self.app.page[0],'notifications')
            self.app.close_modal()
            if display.viewport.x:
                self.app.handle_native(pygame.event.Event(pygame.MOUSEBUTTONDOWN,button=1,pos=(0,size[1]//2)))
                self.assertIsNone(self.app.pan)
                self.assertIsNone(self.app.page)
        self.app.draw()

    def test_side_panel_scene_pan_and_occlusion(self):
        app = self.app
        app.open('pet',{'pet_id':self.game.living[0].pet_id})
        panel = app.modal
        self.assertTrue(panel.is_side)
        app.draw()
        pygame.image.save(app.screen,str(self.output/'responsive-pet.png'))
        before = app.courtyard.camera.x
        app.handle(pygame.event.Event(pygame.MOUSEBUTTONDOWN,button=1,pos=(500,400)))
        app.handle(pygame.event.Event(pygame.MOUSEMOTION,pos=(450,400),rel=(-50,0)))
        self.assertNotEqual(before,app.courtyard.camera.x)
        app.handle(pygame.event.Event(pygame.MOUSEBUTTONUP,button=1,pos=(800,400)))
        self.assertIsNone(app.pan)
        self.assertIs(app.modal,panel)
        zoom = app.courtyard.camera.zoom
        with patch('pygame.mouse.get_pos',return_value=app.display.to_native((900,400))):
            app.handle(pygame.event.Event(pygame.MOUSEWHEEL,y=-1))
        self.assertEqual(zoom,app.courtyard.camera.zoom)
        with patch('pygame.mouse.get_pos',return_value=app.display.to_native((400,400))):
            app.handle(pygame.event.Event(pygame.MOUSEWHEEL,y=-1))
        self.assertLess(app.courtyard.camera.zoom,zoom)
        app.draw()

    def test_modal_still_blocks_world_and_panel_history_restores(self):
        app = self.app
        app.open('pet',{'pet_id':self.game.living[0].pet_id})
        app.modal.scroll = 120
        app.open('feed',{'pet_id':self.game.living[0].pet_id})
        self.assertFalse(app.modal.is_side)
        app.handle(pygame.event.Event(pygame.MOUSEBUTTONDOWN,button=1,pos=(50,400)))
        self.assertIsNone(app.pan)
        app.back()
        self.assertTrue(app.modal.is_side)
        self.assertEqual(app.modal.scroll,120)

    def test_notices_and_pending_events_do_not_interrupt(self):
        app,g = self.app,self.game
        g.events = [events.create_event(g.now,50,g.rng)]
        app.open('pet',{'pet_id':g.living[0].pet_id})
        page = app.page
        g.notices.append('完成成就：第一位伙伴')
        app.last_tick = time.monotonic()
        app.tick()
        self.assertEqual(app.page,page)
        self.assertFalse(g.notices)
        self.assertIn('第一位伙伴',app.notifications[-1]['text'])
        self.assertFalse(app.notifications[-1]['read'])
        app.open('notifications')
        self.assertTrue(app.notifications[-1]['read'])
        app.draw()
        pygame.image.save(app.screen,str(self.output/'responsive-notifications.png'))
        action = next(b for b in app.modal.buttons if b.label=='查看并选择')
        self.click_native(action.rect.move(app.modal.offset()).center)
        self.assertEqual(app.page[0],'event')
        self.assertFalse(app.modal.is_side)

    def test_font_setting_reflows_and_persists(self):
        app = self.app
        app.open('help')
        old_height = app.modal.cursor
        app.set_font_scale(1.3)
        self.assertGreater(app.modal.cursor,old_height)
        app.open('pet',{'pet_id':self.game.living[0].pet_id})
        self.assertTrue(all(b.rect.right<=app.modal.body.w for b in app.modal.buttons))
        self.assertTrue(all(bar.rect.w<=app.modal.body.w for _,kind,bar in app.modal.blocks if kind=='bar'))
        app.draw()
        pygame.image.save(app.screen,str(self.output/'responsive-pet-large.png'))
        app.set_window_size((960,640))
        loaded = Display(self.folder/'settings.json')
        self.assertEqual(loaded.font_scale,1.3)
        self.assertEqual(loaded.windowed_size,(960,640))
        app.open('settings')
        app.draw()
        pygame.image.save(app.display.window,str(self.output/'responsive-settings.png'))

    def test_fullscreen_restores_previous_window_and_global_shortcuts(self):
        app = self.app
        app.set_window_size((960,640))
        app.handle_native(pygame.event.Event(pygame.KEYDOWN,key=pygame.K_F11))
        self.assertTrue(app.display.fullscreen)
        app.handle_native(pygame.event.Event(pygame.KEYDOWN,key=pygame.K_RETURN,mod=pygame.KMOD_ALT))
        self.assertFalse(app.display.fullscreen)
        self.assertEqual(app.display.window.get_size(),(960,640))
        app.handle_native(pygame.event.Event(pygame.KEYDOWN,key=pygame.K_F10))
        self.assertEqual(app.page[0],'settings')

    def test_ime_candidate_rectangle_uses_native_coordinates(self):
        app = self.app
        app.set_window_size((1600,1000))
        app.open('rename',{'pet_id':self.game.living[0].pet_id})
        box = app.modal.inputs[0]
        with patch('pygame.key.set_text_input_rect') as setter:
            app.handle_native(pygame.event.Event(pygame.TEXTINPUT,text='小镇'))
            setter.assert_called_with(app.display.native_rect(box.rect.move(app.modal.offset())))
        self.assertTrue(box.text.endswith('小镇'))

    def test_large_window_renders_native_ui_and_keeps_click_coordinates(self):
        app = self.app
        app.set_window_size((1920,1290))
        app.open('pet',{'pet_id':self.game.living[0].pet_id})
        app.draw()
        self.assertEqual(app.screen.get_size(),(1920,1290))
        self.assertGreater(app.courtyard.camera.width,1280)
        self.assertEqual(app.bottom.action_buttons['feed'].rect.w,74)
        pygame.image.save(app.display.window,str(self.output/'native-large-window.png'))
        close = app.modal.close.rect.center
        self.click_native(close)
        self.assertIsNone(app.modal)
        self.click_native(app.notification_button.rect.center)
        self.assertEqual(app.page[0],'notifications')
        app.draw()

    def test_invalid_settings_fall_back_without_losing_startup(self):
        path = self.folder/'bad-settings.json'
        for value in ('[]','{"window_size": null}','bad json'):
            path.write_text(value,encoding='utf-8')
            display = Display(path)
            self.assertEqual(display.font_scale,1.0)
            self.assertEqual(display.windowed_size,(1280,860))


if __name__ == '__main__':
    unittest.main()
