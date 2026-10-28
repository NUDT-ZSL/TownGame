"""验证放大后文字来自目标字号，并保持原有裁剪和遮挡顺序。"""
import os
os.environ['SDL_VIDEODRIVER'] = 'dummy'
os.environ['SDL_AUDIODRIVER'] = 'dummy'
import unittest
import pygame
import ui
from render_canvas import RenderCanvas, replay, recorded_region


class NativeRenderTests(unittest.TestCase):
    def setUp(self):
        pygame.init()
        pygame.display.set_mode((400,200))
        self.fonts = ui.Fonts()

    def tearDown(self):
        pygame.quit()

    def test_enlarged_text_matches_native_font_pixels(self):
        canvas = RenderCanvas((400,100))
        canvas.begin(True)
        canvas.fill('black')
        self.fonts.text(canvas,'清晰的小镇 Town 123',(10,10),18,(255,255,255))
        native = pygame.Surface((800,200))
        replay(canvas,native,pygame.Rect(0,0,800,200))
        expected = pygame.Surface((800,200))
        expected.fill('black')
        expected.blit(self.fonts.get(36).render('清晰的小镇 Town 123',True,'white'),(20,20))
        pixels = pygame.image.tostring(native,'RGB')
        self.assertEqual(pixels,pygame.image.tostring(expected,'RGB'))
        self.assertNotEqual(pixels,pygame.image.tostring(pygame.transform.smoothscale(canvas,(800,200)),'RGB'))

    def test_occlusion_clip_and_scaled_lines(self):
        canvas = RenderCanvas((400,100))
        canvas.begin(True)
        canvas.fill('black')
        self.fonts.text(canvas,'被面板遮住的文字',(10,10),18,(255,255,255))
        pygame.draw.rect(canvas,'red',(0,0,200,70))
        canvas.set_clip((200,0,100,50))
        self.fonts.text(canvas,'超长内容不会穿出裁剪区',(180,10),18,(255,255,255))
        pygame.draw.line(canvas,'green',(200,45),(350,45),width=2)
        canvas.set_clip(None)
        target = pygame.Surface((800,200))
        replay(canvas,target,pygame.Rect(0,0,800,200))
        self.assertEqual(target.get_at((30,30)),pygame.Color('red'))
        self.assertEqual(target.get_at((650,30)),pygame.Color('black'))
        self.assertEqual(target.get_at((500,90)),pygame.Color('green'))
        self.assertEqual(target.get_at((650,90)),pygame.Color('black'))
        self.assertEqual(target.get_clip(),target.get_rect())

    def test_offscreen_region_preserves_native_text_and_camera_crop(self):
        scene = RenderCanvas((400,100))
        scene.begin(True)
        scene.fill('black')
        self.fonts.text(scene,'场景标签',(110,20),18,(255,255,255))
        area = pygame.Rect(100,10,200,80)
        image = recorded_region(scene,area,scene.subsurface(area))
        canvas = RenderCanvas((200,80))
        canvas.begin(True)
        canvas.blit(image,(0,0))
        target = pygame.Surface((400,160))
        replay(canvas,target,pygame.Rect(0,0,400,160))
        expected = pygame.Surface((400,160))
        expected.fill('black')
        expected.blit(self.fonts.get(36).render('场景标签',True,'white'),(20,20))
        self.assertEqual(pygame.image.tostring(target,'RGB'),pygame.image.tostring(expected,'RGB'))

    def test_copied_glyph_and_surface_opacity_remain_valid(self):
        glyph = self.fonts.render('副本',18,(255,255,255)).copy()
        glyph.set_alpha(100)
        canvas = RenderCanvas((200,80))
        canvas.begin(True)
        canvas.fill('black')
        canvas.blit(glyph,(10,10))
        target = pygame.Surface((400,160))
        replay(canvas,target,pygame.Rect(0,0,400,160))
        self.assertTrue(any(target.get_at((x,y)).r for x in range(20,100) for y in range(20,75)))
