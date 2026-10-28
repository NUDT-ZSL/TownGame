"""真实相机、深度拾取与三维模式下的经营输入回归。"""
import os
os.environ['SDL_VIDEODRIVER']='dummy'
os.environ['SDL_AUDIODRIVER']='dummy'
os.environ['PYGAME_HIDE_SUPPORT_PROMPT']='1'
import math
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch
import pygame
import ui
import game_config as C
from camera3d import Camera3D
from mesh3d import Mesh
from render3d import Renderer3D
from world3d import tree_mesh,pet_mesh
from game import Game
from db import Database
from interface import ManorApp
from town_grid import iter_visible_cells


class Camera3DTests(unittest.TestCase):
    def test_inverse_projection_survives_rotation_pitch_zoom_and_resize(self):
        camera=Camera3D(C.WORLD_SIZE)
        for width,height in ((1280,616),(1920,836),(900,1100)):
            camera.width,camera.height=width,height
            for yaw in (0,32,90,180,300):
                for pitch in (30,49,68):
                    camera.yaw=math.radians(yaw);camera.pitch=math.radians(pitch)
                    for zoom in (.55,1,1.8):
                        camera.zoom=zoom
                        for point in ((1400,950),(1800,1070),(2200,1200)):
                            screen=camera.world_to_screen(point);back=camera.screen_to_world(screen)
                            self.assertAlmostEqual(point[0],back[0],places=6);self.assertAlmostEqual(point[1],back[1],places=6)
        camera.orbit(0,-1000);self.assertAlmostEqual(camera.pitch,math.radians(30))
        camera.orbit(0,1000);self.assertAlmostEqual(camera.pitch,math.radians(68))

    def test_zoom_and_pan_follow_mouse_ground_anchor(self):
        c=Camera3D(C.WORLD_SIZE);pos=(800,500);before=c.screen_to_world(pos)
        c.zoom_at(pos,1.2);after=c.screen_to_world(pos)
        for a,b in zip(before,after):self.assertAlmostEqual(a,b)
        before=c.world_to_screen((1800,1050));c.pan(30,-20);after=c.world_to_screen((1800,1050))
        self.assertGreater(after[0],before[0]);self.assertLess(after[1],before[1])
        self.assertTrue(all(0<=x<75 and 0<=y<50 for x,y in iter_visible_cells(c)))

    def test_distinct_tree_and_pet_meshes_preserve_identity_and_style(self):
        shapes=[tree_mesh(s,7,'春').data.tobytes() for s in ('oak','birch','pine','willow','cherry','maple')]
        self.assertEqual(len(set(shapes)),6)
        self.assertIs(tree_mesh('oak',7,'春'),tree_mesh('oak',7,'春'))
        self.assertNotEqual(tree_mesh('maple',7,'春').data,tree_mesh('maple',7,'秋').data)
        shapes=[pet_mesh(s,0,False,'成年',False,'idle',0).data.tobytes() for s in C.SPECIES]
        self.assertEqual(len(set(shapes)),len(C.SPECIES))
        self.assertNotEqual(pet_mesh('兔子',0,False,'成年',False,'idle',0).data,
                            pet_mesh('兔子',40,False,'成年',False,'idle',0).data)


class PickingTests(unittest.TestCase):
    def setUp(self):pygame.init();pygame.display.set_mode((1280,860))
    def tearDown(self):pygame.quit()

    def check_occlusion(self,renderer):
        c=Camera3D(C.WORLD_SIZE);c.width,c.height=640,440;c.vx,c.vy=0,0;c.center((1800,1200))
        rear=Mesh();rear.box((0,0,40),(160,160,80),(240,150,100))
        front=Mesh();front.box((0,0,40),(160,160,80),(100,180,140))
        ray=c.basis()[3];near=(1800-ray[0]*100,1200-ray[1]*100,-ray[2]*100)
        objects=[(rear,(1800,1200,0),0,1,('rear',1)),(front,near,0,1,('front',2))]
        renderer.draw(c,objects)
        point=c.world_to_screen((near[0],near[1],near[2]+40))
        self.assertEqual(renderer.pick(point,c),('front',2))
        renderer.draw(c,list(reversed(objects)))
        self.assertEqual(renderer.pick(point,c),('front',2))
        objects[1]=(front,near,0,1,None);renderer.draw(c,objects)
        self.assertIsNone(renderer.pick(point,c),'不可交互的前景也必须遮住后方点击')

    def test_software_3d_keeps_occlusion_and_picking(self):
        renderer=Renderer3D(software=True)
        try:self.check_occlusion(renderer)
        finally:renderer.close()

    def test_gpu_depth_picking_is_independent_of_draw_order(self):
        renderer=Renderer3D()
        try:
            if not renderer.ctx:self.skipTest('本机没有 OpenGL 3.3：'+str(renderer.failure))
            self.check_occlusion(renderer)
        finally:renderer.close()


class App3DTests(unittest.TestCase):
    def setUp(self):
        self.temp=tempfile.TemporaryDirectory();self.folder=Path(self.temp.name)
        self.g=Game(Database(self.folder/'game.db'),real_now=1800000000.,seed=42)
        self.g.meta['sim_ts']=self.g.meta['start_ts']//C.DAY*C.DAY+12*C.HOUR
        self.app=ManorApp(self.g,settings_path=self.folder/'ui.json')
        if not self.app.courtyard.engine.ctx:
            self.app.courtyard.close();self.g.db.close();pygame.quit();self.temp.cleanup()
            self.skipTest('完整场景 GPU 验证需要 OpenGL 3.3，软件渲染另有覆盖')

    def tearDown(self):
        if hasattr(self.app.courtyard,'close'):self.app.courtyard.close()
        self.g.db.close();ui.display=None;pygame.quit();self.temp.cleanup()

    def visible_pixel(self,key):
        view=self.app.courtyard;r=view.hit_bounds[key].clip(view.viewport)
        for y in range(r.top+1,r.bottom,2):
            for x in range(r.left+1,r.right,2):
                if view.engine.pick((x,y),view.camera)==key:return x,y
        self.fail(f'实体没有可见像素：{key}')

    def test_pet_selection_food_drop_and_building_click_after_rotation_resize(self):
        app=self.app;g=self.g
        app.set_window_size((1600,1000));view=app.courtyard
        actor=app.actor_order[0];actor.x,actor.y=550,615;g.pets[actor.pet_id].hunger=20
        for yaw in (15,-40,100):
            view.camera.yaw=math.radians(yaw);view.camera.zoom=1.2;app.draw()
            point=self.visible_pixel(('pet',actor.pet_id));self.assertIs(view.pet_at(point,app.actor_order),actor)
        app.focus_pet_id=None;app._handle_click(point);self.assertEqual(app.focus_pet_id,actor.pet_id)
        app.close_modal();view.camera.yaw=math.radians(26);app.draw()
        point=self.visible_pixel(('pet',actor.pet_id));item='普通饲料';before=g.inventory.count(item)
        app.drag={'item':item,'pos':point}
        app.handle(pygame.event.Event(pygame.MOUSEBUTTONUP,button=1,pos=point))
        self.assertEqual(g.inventory.count(item),before-1);self.assertGreater(g.pets[actor.pet_id].hunger,20)
        app.close_modal();app.draw();point=self.visible_pixel(('building',0))
        app._handle_click(point);self.assertEqual(app.page,('building',{'building_id':0}))

    def test_render_modes_and_camera_keys_do_not_mutate_saved_game(self):
        app=self.app;g=self.g;app.draw();before=g.snapshot();rng=g.rng.getstate()
        people=app.courtyard.people;traffic=app.courtyard.traffic
        yaw=app.courtyard.camera.yaw;app.handle(pygame.event.Event(pygame.KEYDOWN,key=pygame.K_e))
        self.assertNotEqual(yaw,app.courtyard.camera.yaw)
        app.handle(pygame.event.Event(pygame.KEYDOWN,key=pygame.K_F9));self.assertFalse(getattr(app.courtyard,'is_3d',False));app.draw()
        app.handle(pygame.event.Event(pygame.KEYDOWN,key=pygame.K_F9));self.assertTrue(app.courtyard.is_3d);app.draw()
        self.assertIs(app.courtyard.people,people);self.assertIs(app.courtyard.traffic,traffic)
        app.open('settings');app.draw();self.assertIn('GPU',app.courtyard.engine.backend)
        self.assertEqual(g.snapshot(),before);self.assertEqual(g.rng.getstate(),rng)

    def test_construction_decoration_preview_and_four_seasons_keep_game_unchanged(self):
        app=self.app;g=self.g;before=g.snapshot();rng=g.rng.getstate()
        app.builder.enter();app.builder.pointer=(720,420);app.builder.land_overlay=True
        app.draw();app.builder.select_tool(1);app.draw();app.builder.exit()
        app.decorator.enter()
        with patch('pygame.mouse.get_pos',return_value=app.display.to_native((680,440))):
            app.draw();app.decorator.kind='tree_willow';app.draw()
        app.decorator.exit()
        # 季节由游戏时间驱动；绘制本身不能推进生产、天气或工资。
        for day in (0,7,14,21):
            g.meta['sim_ts']=g.meta['start_ts']+day*C.DAY+12*C.HOUR;app.draw()
        g.meta['sim_ts']=before['meta']['sim_ts']
        self.assertEqual(g.snapshot(),before);self.assertEqual(g.rng.getstate(),rng)

    def test_projected_mouse_builds_road_and_decoration_with_undo(self):
        from town_grid import cell_center
        from decorations import placement_reason
        app=self.app;g=self.g;view=app.courtyard
        cell=next((x,y) for y in range(15,36) for x in range(15,60)
                  if not placement_reason(g,x,y) and g.construction.road_preview([(x,y)],'garden')['valid'])
        view.camera.center(cell_center(*cell));app.builder.enter();app.builder.style='garden';app.draw()
        pos=view.viewport.center;coins=g.meta['coins']
        for kind in (pygame.MOUSEBUTTONDOWN,pygame.MOUSEBUTTONUP):app.handle(pygame.event.Event(kind,button=1,pos=pos))
        self.assertIn(cell,g.construction.grid.roads);self.assertLess(g.meta['coins'],coins)
        app.handle(pygame.event.Event(pygame.KEYDOWN,key=pygame.K_z,mod=pygame.KMOD_CTRL));self.assertNotIn(cell,g.construction.grid.roads)
        app.builder.exit();app.decorator.enter();app.decorator.kind='bench';app.draw()
        app.handle(pygame.event.Event(pygame.MOUSEBUTTONDOWN,button=1,pos=pos))
        row=g.meta['decorations'][-1];self.assertEqual((row['cx'],row['cy']),cell);app.draw()
        point=self.visible_pixel(('decoration',row['id']));self.assertEqual(view.decoration_at(point),row['id'])
        app.handle(pygame.event.Event(pygame.MOUSEBUTTONDOWN,button=1,pos=point));self.assertEqual(app.decorator.moving,row['id'])
        app.handle(pygame.event.Event(pygame.KEYDOWN,key=pygame.K_DELETE));self.assertFalse(g.meta['decorations'])
        app.handle(pygame.event.Event(pygame.KEYDOWN,key=pygame.K_z,mod=pygame.KMOD_CTRL));self.assertEqual(g.meta['decorations'][-1]['id'],row['id'])

    def test_existing_panels_and_sanitation_render_over_3d(self):
        app=self.app;g=self.g;app.draw();before=g.snapshot();rng=g.rng.getstate()
        pages=[(key,{}) for key in ('pets','shop','inventory','craft','orders','events','codex','achievements',
               'memorial','logs','help','today','sanitation','town','map','settings','notifications','care','dashboard')]
        pages += [('zone',{'zone_id':key}) for key in C.TOWN_ZONES]
        pid=app.actor_order[0].pet_id
        pages += [(key,{'pet_id':pid}) for key in ('pet','feed','move','breed','rename')]
        pages += [('building',{'building_id':i}) for i in range(20)]
        for key,args in pages:
            app.close_modal();app.open(key,args);app.draw();app.modal.scroll=app.modal.max_scroll;app.draw()
        self.assertEqual(g.snapshot(),before);self.assertEqual(g.rng.getstate(),rng)
        app.close_modal();app.draw();point=self.visible_pixel(('sanitation',True))
        app._handle_click(point);self.assertEqual(app.page[0],'sanitation')


if __name__=='__main__':unittest.main()
