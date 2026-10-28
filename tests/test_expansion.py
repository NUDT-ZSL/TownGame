"""外观预设、性格行为、批量操作、装饰、账本与宽屏交互的回归测试。"""
import os
os.environ['SDL_VIDEODRIVER']='dummy'
os.environ['SDL_AUDIODRIVER']='dummy'
import copy
import random
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch
import pygame
import game_config as C
import ui
import courtyard
from appearance import pet_variant,style_groups
from db import Database,SaveError,validate
from game import Game,GameError
from interface import ManorApp
from pet_art import PetActor
from pet_services import attention_items,action_reason
from decorations import placement_reason,CATALOG
from town_grid import cell_center


class ExpansionTests(unittest.TestCase):
    def setUp(self):
        self.temp=tempfile.TemporaryDirectory()
        self.path=Path(self.temp.name)/'game.db'
        self.g=Game(Database(self.path),real_now=1800000000.,seed=42)
        self.clock=patch('game.time.time',return_value=self.g.meta['real_anchor']);self.clock.start()
        self.app=ManorApp(self.g,settings_path=Path(self.temp.name)/'settings.json')
        self.output=C.ROOT/'.test-output';self.output.mkdir(exist_ok=True)

    def tearDown(self):
        self.clock.stop();self.g.db.close();ui.pointer_transform=None;ui.display=None;pygame.quit();self.temp.cleanup()

    def do(self,action,**args):return self.g.perform(action,real_now=self.g.meta['real_anchor'],**args)

    def click(self,pos):
        native=self.app.display.to_native(pos)
        for kind in (pygame.MOUSEBUTTONDOWN,pygame.MOUSEBUTTONUP):
            self.app.handle_native(pygame.event.Event(kind,button=1,pos=native))

    def label(self,label):
        b=next(b for b in self.app.modal.buttons if b.label==label)
        self.app.modal.scroll=min(self.app.modal.max_scroll,max(0,b.rect.y-120))
        self.click(b.rect.move(self.app.modal.offset()).center)

    def free_cells(self):
        return [(x,y) for y in range(18,24) for x in range(1,20) if not placement_reason(self.g,x,y)]

    def test_every_species_has_selectable_visual_styles(self):
        p=self.g.living[0]
        for species in C.SPECIES:
            p.species=species
            self.do('pet_appearance',pet_id=p.pet_id,variant=367)
            self.assertEqual(pet_variant(p.pet_id,self.g.meta),367)
            images=[self.app.renderer.portrait(species,'成年',height=128,appearance=v) for v in (0,1,19,183)]
            self.assertEqual(len({pygame.image.tostring(s,'RGBA') for s in images}),4)
            self.app.open('appearance',{'pet_id':p.pet_id});self.app.draw()
        self.assertEqual(len(style_groups('猫咪')),4)

    def test_favorites_toggle_and_reset_restore_original_purchase_style(self):
        p=self.g.living[0];initial=pet_variant(p.pet_id,self.g.meta)
        self.do('pet_appearance',pet_id=p.pet_id,variant=7)
        self.do('favorite_style',species=p.species,variant=7)
        self.assertEqual(self.g.meta['style_favorites'][p.species],[7])
        self.do('reset_appearance',pet_id=p.pet_id)
        self.assertEqual(pet_variant(p.pet_id,self.g.meta),initial)
        self.g.db.close();self.g=Game(Database(self.path),real_now=1800000000.)
        self.assertEqual(self.g.meta['style_favorites'][p.species],[7])
        self.do('favorite_style',species=p.species,variant=7)
        self.assertEqual(self.g.meta['style_favorites'][p.species],[])

    def test_inline_purchase_cards_and_editor_favorites(self):
        app=self.app
        app.open('buy_pet',{'species':'猫咪'})
        self.assertEqual(sum(isinstance(b,ui.ChoiceCard) for b in app.modal.buttons),8)
        self.label('银灰')
        self.assertEqual(app.page[1]['appearance'],3)
        app.open('appearance',{'species':'猫咪','variant':3})
        self.label('收藏此样式')
        self.assertEqual(self.g.meta['style_favorites']['猫咪'],[3])
        app.draw();pygame.image.save(app.screen,str(self.output/'all-species-editor.png'))

    def test_personality_changes_target_without_game_rng(self):
        rng_state=self.g.rng.getstate();bounds=courtyard.ACTIVITY
        context={'food':courtyard.BOWL,'toy':courtyard.TOY,'companions':[(500,600)]}
        for personality,expected in (('吃货','食盆'),('胆小','安静'),('粘人','玩具'),('活泼','玩具')):
            a=PetActor('id','伙伴','兔子',x=500,y=600,personality=personality,hunger=80)
            a._decide(bounds,random.Random(42),False,context)
            self.assertIn(expected,a.behavior_note)
            self.assertEqual(a.state,'walk')
            if personality=='吃货':self.assertEqual(a.target,courtyard.BOWL)
        self.assertEqual(self.g.rng.getstate(),rng_state)

    def test_batch_skips_missing_material_without_partial_consumption(self):
        for p in self.g.living:p.cleanliness=10
        self.g.inventory.batches=[b for b in self.g.inventory.batches if b['item'] not in ('清水','沐浴露')]
        for item in ('清水','沐浴露'):self.g.inventory.add(item,1,self.g.now,self.g.storage_capacity)
        result=self.do('batch_care',kind='bath')
        self.assertIn('完成 1 只，跳过 1 只',result)
        self.assertEqual(self.g.inventory.count('清水'),0)
        self.assertEqual(self.g.inventory.count('沐浴露'),0)
        self.assertEqual(sum(p.cleanliness>10 for p in self.g.living),1)
        self.assertIn('缺少清水',action_reason(self.g,self.g.living[0],'bath'))

    def test_batch_save_failure_restores_every_pet_and_inventory(self):
        before=self.g.snapshot()
        with patch.object(self.g.db,'save',side_effect=SaveError('磁盘错误')):
            with self.assertRaises(SaveError):self.do('batch_care',kind='feed')
        self.assertEqual(self.g.snapshot(),before)

    def test_batch_does_not_waste_food_on_full_pets(self):
        pets=self.g.living;pets[0].hunger=100;pets[1].hunger=20
        before=sum(self.g.inventory.count(item) for item,spec in C.ITEMS.items() if 'feed' in spec and item!='腐败物')
        result=self.do('batch_care',kind='feed')
        after=sum(self.g.inventory.count(item) for item,spec in C.ITEMS.items() if 'feed' in spec and item!='腐败物')
        self.assertEqual(before-after,1)
        self.assertIn('完成 1 只，跳过 1 只',result)

    def test_decoration_move_rotate_remove_undo_and_road_conflict(self):
        a,b=self.free_cells()[:2];coins=self.g.meta['coins']
        self.do('decoration',operation='place',kind='flowers',cx=a[0],cy=a[1])
        d=self.g.meta['decorations'][0]
        with self.assertRaises(GameError):self.do('build_roads',cells=[a])
        self.do('decoration',operation='move',decoration_id=d['id'],cx=b[0],cy=b[1],orientation=3)
        d=self.g.meta['decorations'][0]
        self.assertEqual((d['cx'],d['cy'],d['orientation']),(*b,3))
        self.do('decoration',operation='remove',decoration_id=d['id'])
        for _ in range(3):self.do('decoration',operation='undo')
        self.assertEqual(self.g.meta['decorations'],[])
        self.assertEqual(self.g.meta['coins'],coins)
        validate(self.g.snapshot())

    def test_decoration_undo_keeps_unrelated_income_and_survives_reload(self):
        a=self.free_cells()[0]
        self.do('decoration',operation='place',kind='bench',cx=a[0],cy=a[1])
        self.g.meta['coins']+=123
        coins=self.g.meta['coins']
        self.g.save();self.g.db.close();self.g=Game(Database(self.path),real_now=1800000000.)
        self.do('decoration',operation='undo')
        self.assertEqual(self.g.meta['coins'],coins+CATALOG['bench'][1])

    def test_invalid_decoration_and_save_failure_are_atomic(self):
        before=self.g.snapshot()
        with self.assertRaises(GameError):self.do('decoration',operation='place',kind='lamp',cx=-1,cy=2)
        self.assertEqual(before,self.g.snapshot())
        a=self.free_cells()[0]
        with patch.object(self.g.db,'save',side_effect=SaveError('磁盘错误')):
            with self.assertRaises(SaveError):self.do('decoration',operation='place',kind='lamp',cx=a[0],cy=a[1])
        self.assertEqual(before,self.g.snapshot())

    def test_finance_records_expense_and_rollback(self):
        self.g.buildings[2].kind='商店';self.g.buildings[2].level=1;self.g.buildings[2].unlocked=True
        price=self.g.item_buy_price('清水')
        self.do('buy',item='清水',quantity=1)
        self.assertEqual(self.g.meta['finance_ledger'][-1]['delta'],-price)
        before=self.g.snapshot()
        with patch.object(self.g.db,'save',side_effect=SaveError('磁盘错误')):
            with self.assertRaises(SaveError):self.do('buy',item='清水',quantity=1)
        self.assertEqual(before,self.g.snapshot())

    def test_attention_opens_and_centers_target(self):
        p=self.g.living[0];p.hunger=20
        items=attention_items(self.g)
        self.assertTrue(any(args.get('pet_id')==p.pet_id for _,_,args in items))
        self.app.open('dashboard');self.app.draw()
        self.label(p.name+'肚子饿了')
        self.assertEqual(self.app.focus_pet_id,p.pet_id)
        self.assertEqual(self.app.page,('pet',{'pet_id':p.pet_id}))

    def test_wide_window_shows_more_world_and_bottom_actions_stay_same_size(self):
        app=self.app;before=app.courtyard.camera.width/app.courtyard.camera.zoom
        app.set_window_size((1920,1080))
        self.assertGreater(app.courtyard.camera.width/app.courtyard.camera.zoom,before)
        p=self.g.living[0];app.focus_pet_id=p.pet_id
        self.assertEqual(app.bottom.action_buttons['feed'].rect.size,(74,60))
        x=(app.screen.get_width()-1280)//2;y=app.screen.get_height()-860
        self.click(app.bottom.action_buttons['feed'].rect.move(x,y).center)
        self.assertEqual(app.page[0],'feed')
        app.close_modal();app.draw();pygame.image.save(app.screen,str(self.output/'wide-world.png'))

    def test_decoration_real_click_place_select_rotate_move_and_undo(self):
        app=self.app;a,b=self.free_cells()[:2]
        app.set_window_size((1920,1080));app.decorator.enter()
        app.courtyard.camera.center(cell_center(*a))
        self.click(app.courtyard.camera.world_to_screen(cell_center(*a)))
        self.assertEqual(len(self.g.meta['decorations']),1)
        self.click(app.courtyard.camera.world_to_screen(cell_center(*a)))
        self.assertIsNotNone(app.decorator.moving)
        app.handle(pygame.event.Event(pygame.KEYDOWN,key=pygame.K_r))
        self.click(app.courtyard.camera.world_to_screen(cell_center(*b)))
        self.assertEqual(self.g.meta['decorations'][0]['orientation'],1)
        app.handle(pygame.event.Event(pygame.KEYDOWN,key=pygame.K_z,mod=pygame.KMOD_CTRL))
        self.assertEqual((self.g.meta['decorations'][0]['cx'],self.g.meta['decorations'][0]['cy']),a)
        app.draw();pygame.image.save(app.screen,str(self.output/'free-decorations.png'))

    def test_new_pages_render_at_small_and_wide_sizes(self):
        for size in ((960,640),(1920,1080)):
            self.app.set_window_size(size)
            for page in ('care','dashboard','settings','manor_map'):
                self.app.open(page);self.app.draw()
            self.app.open('dashboard');self.app.draw()
        pygame.image.save(self.app.screen,str(self.output/'management-dashboard.png'))

    def test_wide_build_toolbar_and_actual_road_stroke(self):
        app=self.app;a=self.free_cells()[0]
        app.set_window_size((1920,1080));app.builder.enter()
        app.courtyard.camera.center(cell_center(*a));app.draw()
        button=app.builder.tool_buttons[0]
        self.click(button.rect.move(320,220).center)
        self.assertEqual(app.builder.tool,0)
        pos=app.courtyard.camera.world_to_screen(cell_center(*a))
        app.handle(pygame.event.Event(pygame.MOUSEMOTION,pos=pos,rel=(0,0)))
        self.click(pos)
        self.assertIn(a,self.g.construction.grid.roads)
        app.draw();pygame.image.save(app.screen,str(self.output/'wide-construction.png'))

    def test_undo_does_not_restore_decoration_over_new_road(self):
        a=self.free_cells()[0]
        self.do('decoration',operation='place',kind='bench',cx=a[0],cy=a[1])
        ident=self.g.meta['decorations'][0]['id']
        self.do('decoration',operation='remove',decoration_id=ident)
        self.do('build_roads',cells=[a])
        before=self.g.snapshot()
        with self.assertRaises(GameError):self.do('decoration',operation='undo')
        self.assertEqual(before,self.g.snapshot())

    def test_period_income_expense_ledger_reconciles_to_cash(self):
        coins=self.g.meta['coins']
        self.g.advance(self.g.meta['real_anchor']+C.VISITOR_INTERVAL)
        rows=self.g.meta.get('finance_ledger',[])
        self.assertEqual(sum(row['delta'] for row in rows),self.g.meta['coins']-coins)
        self.assertTrue(rows)
