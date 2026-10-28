"""v5格网/原子建设/占地/施工/连通经济与真实SQLite迁移的回归测试。"""
import copy
import json
import sqlite3
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch
import game_config as C
import town_catalog as K
from camera import Camera
from db import Database, SaveError, validate
from game import Game, GameError
from town_grid import *


class GridTests(unittest.TestCase):
    def test_data_driven_catalog_and_road_configuration(self):
        self.assertEqual(len(K.CATALOG),15)
        self.assertEqual({s['family'] for s in K.CATALOG.values()},set(K.FAMILIES))
        self.assertEqual({s['cost'] for s in K.ROAD_STYLES.values()},{10,12,14})
        self.assertTrue(all(s['cost']>0 and s['build_hours']>0 and len(s['footprint'])==2 for s in K.CATALOG.values()))

    def test_roundtrip_and_visible_bounds(self):
        for c in ((0,0),(74,49),(32,21)):
            self.assertEqual(world_to_cell(*cell_center(*c)),c)
            self.assertEqual(cell_rect(*c)[:2],(c[0]*48,c[1]*48))
        camera=Camera(C.WORLD_SIZE)
        for zoom in (.6,1.4):
            camera.zoom=zoom; camera.center((3600,2400))
            self.assertTrue(all(in_bounds(*c) for c in iter_visible_cells(camera)))

    def test_default_roads_connect_all_six_zones_and_avoid_reserves(self):
        roads=default_roads(10); grid=RoadGrid(roads)
        self.assertTrue(all(grid.connected_zones().values()))
        self.assertTrue(all(terrain(c) in ('grass','zone') for c in grid.roads))
        self.assertEqual(len(grid.components()),1)

    def test_all_16_masks_and_mixed_styles(self):
        center=(5,5)
        for mask in range(16):
            cells=[center]+[(5+dx,5+dy) for i,(dx,dy) in enumerate(DIRECTIONS) if mask & (1<<i)]
            grid=RoadGrid([dict(cx=x,cy=y,road_style='stone' if x%2 else 'asphalt') for x,y in cells])
            self.assertEqual(grid.mask(center),mask)

    def test_drag_fast_diagonal_stays_orthogonal(self):
        for a,b in (((1,2),(20,40)),((60,25),(5,3)),((3,3),(3,3))):
            cells=line_cells(a,b)
            self.assertEqual((cells[0],cells[-1]),(a,b))
            self.assertTrue(all(abs(a[0]-b[0])+abs(a[1]-b[1])==1 for a,b in zip(cells,cells[1:])))

    def test_disconnected_path_is_empty(self):
        grid=RoadGrid(default_roads(0))
        self.assertFalse(grid.shortest_path((0,0),PORTALS['estate']))
        path=grid.shortest_path(PORTALS['station'],PORTALS['market'])
        self.assertTrue(all(c in grid.roads for c in path))
        self.assertEqual(path,grid.shortest_path(path[0],path[-1]))


class BuildTests(unittest.TestCase):
    def setUp(self):
        self.temp=tempfile.TemporaryDirectory(dir=C.ROOT/'.test-output'); self.addCleanup(self.temp.cleanup)
        self.path=Path(self.temp.name)/'build.db'; self.t=1800000000.
        self.g=Game(Database(self.path),real_now=self.t,seed=7)
        self.addCleanup(lambda:self.g.db.close())
        self.g.meta['coins']=100000

    def do(self,action,**kwargs):
        return self.g.perform(action,real_now=self.g.meta['real_anchor'],**kwargs)

    def shift(self,delta):
        self.g.advance(self.g.meta['real_anchor']+delta)

    def road(self):
        self.do('build_roads',cells=line_cells((2,20),(22,20)))

    def house(self,key='cream_home'):
        self.road(); self.do('place_town_building',catalog_key=key,cx=2,cy=21)
        return self.g.town_buildings[-1]

    def test_mixed_road_preview_matches_fee_skips_invalid(self):
        cells=[(2,20),(3,20),(2,20),(30,20),(-1,0)]
        p=self.g.construction.road_preview(cells,'stone')
        self.assertEqual(p['cost'],28); self.assertEqual(len(p['valid']),2)
        coins=self.g.meta['coins']; self.do('build_roads',cells=cells,style='stone')
        self.assertEqual(self.g.meta['coins'],coins-p['cost'])
        self.assertEqual(self.g.meta['road_revision'],1)

    def test_no_duplicate_fee_and_repaint_preserves_original_refund(self):
        self.do('build_roads',cells=[(2,20)])
        before=self.g.snapshot()
        with self.assertRaises(GameError):
            self.do('build_roads',cells=[(2,20)])
        self.assertEqual(before,self.g.snapshot())
        self.do('build_roads',cells=[(2,20)],style='stone')
        coins=self.g.meta['coins']; self.do('remove_roads',cells=[(2,20)])
        self.assertEqual(self.g.meta['coins'],coins+5)
        with self.assertRaises(GameError):
            self.do('remove_roads',cells=[(2,20)])

    def test_insufficient_funds_rejects_whole_stroke(self):
        self.g.meta['coins']=19; before=self.g.snapshot()
        with self.assertRaises(GameError):
            self.do('build_roads',cells=[(2,20),(3,20)])
        self.assertEqual(before,self.g.snapshot())

    def test_save_failure_rolls_back_roads_money_and_undo(self):
        before=self.g.snapshot()
        with patch.object(self.g.db,'save',side_effect=SaveError('磁盘已满')):
            with self.assertRaises(SaveError):
                self.do('build_roads',cells=[(2,20)])
        self.assertEqual(before,self.g.snapshot()); self.assertEqual(self.g.construction.undo,[])

    def test_undo_restores_preexisting_styles_and_fee(self):
        before=copy.deepcopy(self.g.roads); coins=self.g.meta['coins']
        self.do('build_roads',cells=[(2,20),(3,20)])
        self.do('undo_roads')
        self.assertEqual(self.g.roads,before); self.assertEqual(self.g.meta['coins'],coins)
        self.assertEqual(self.g.meta['road_revision'],2)
        with self.assertRaises(GameError): self.do('undo_roads')

    def test_topology_cut_and_restore(self):
        self.do('upgrade_zone',zone_id='market'); cut=(58,13)
        self.do('remove_roads',cells=[cut])
        self.assertFalse(self.g.construction.connected('market'))
        self.do('build_roads',cells=[cut])
        self.assertTrue(self.g.construction.connected('market'))
        self.assertTrue(any('道路中断' in r['text'] for r in self.g.logs))

    def test_placement_requires_street_not_diagonal(self):
        self.do('build_roads',cells=[(1,20)])
        p=self.g.construction.building_preview('cream_home',2,21)
        self.assertIn('道路相邻',p['reason'])
        self.do('build_roads',cells=[(2,20)])
        self.assertFalse(self.g.construction.building_preview('cream_home',2,21)['reason'])

    def test_auto_frontage_rotation_and_building_save_failure(self):
        self.road(); self.do('upgrade_zone',zone_id='park')
        preview=self.g.construction.building_preview('windmill',2,21)
        self.assertEqual(preview['building']['orientation'],0)
        rotated=self.g.construction.building_preview('windmill',2,21,1)
        self.assertEqual((rotated['building']['w'],rotated['building']['h']),(2,3))
        before=self.g.snapshot()
        with patch.object(self.g.db,'save',side_effect=SaveError('存盘失败')):
            with self.assertRaises(SaveError): self.do('place_town_building',catalog_key='windmill',cx=2,cy=21,orientation=1)
        self.assertEqual(before,self.g.snapshot())

    def test_corrupt_road_load_isolates_and_recovers_real_backup(self):
        self.road(); self.g.db.backup(); ids=set(self.g.pets)
        record=dict(self.g.roads[0]); record['road_style']='unknown'
        with self.g.db.conn:
            self.g.db.conn.execute('UPDATE roads SET data=? WHERE road_id=?',(json.dumps(record),record['road_id']))
        # 模拟异常退出，不能调用会主动覆盖备份的正常close()。
        self.g.db.conn.close(); self.g.db.conn=None
        self.g=Game(Database(self.path),real_now=self.t)
        self.assertEqual(set(self.g.pets),ids)
        self.assertIn('已恢复',self.g.db.recovery_message)
        self.assertTrue(list(self.path.parent.glob('build.db.damaged-*')))
        validate(self.g.snapshot())

    def test_placement_rejects_all_reserves_and_closed_zones(self):
        for cell in ((-1,2),(74,49),(30,20),(10,30),(67,27),(5,5)):
            with self.subTest(cell=cell):
                before=self.g.snapshot()
                with self.assertRaises(GameError): self.do('place_town_building',catalog_key='cream_home',cx=cell[0],cy=cell[1])
                self.assertEqual(before,self.g.snapshot())

    def test_overlap_road_and_building_and_unknown_catalog(self):
        self.house()
        for key,cx,cy in (('cream_home',2,21),('cream_home',2,20),('missing',2,22)):
            with self.assertRaises(GameError): self.do('place_town_building',catalog_key=key,cx=cx,cy=cy)

    def test_construction_exact_finish_and_restart_no_duplicate(self):
        b=self.house(); finish=b['finish_ts']; self.shift(4*C.HOUR-1)
        self.assertEqual(b['status'],'building'); self.shift(1)
        self.assertEqual(b['status'],'ready')
        count=sum('奶油小屋施工完成' in l['text'] for l in self.g.logs)
        self.g.db.close(); self.g=Game(Database(self.path),real_now=self.g.meta['real_anchor'])
        self.assertEqual(self.g.town_buildings[0]['finish_ts'],finish)
        self.assertEqual(sum('奶油小屋施工完成' in l['text'] for l in self.g.logs),count)

    def test_construction_and_ready_demolition_fixed_refunds(self):
        b=self.house(); coins=self.g.meta['coins']
        self.do('remove_town_building',building_id=b['building_id'])
        self.assertEqual(self.g.meta['coins'],coins+36)
        self.do('place_town_building',catalog_key='cream_home',cx=2,cy=21)
        self.shift(4*C.HOUR); coins=self.g.meta['coins']; b=self.g.town_buildings[-1]
        self.do('remove_town_building',building_id=b['building_id'])
        self.assertEqual(self.g.meta['coins'],coins+72)

    def test_disconnect_building_frontage_and_restore(self):
        b=self.house(); self.shift(4*C.HOUR)
        self.assertTrue(self.g.construction.building_connected(b))
        self.assertEqual(self.g.construction.bonuses()['population'],5)
        self.do('remove_roads',cells=[(2,20),(3,20)])
        self.assertFalse(self.g.construction.building_connected(b))
        self.assertEqual(self.g.construction.bonuses()['population'],0)
        self.do('build_roads',cells=[(2,20),(3,20)])
        self.assertEqual(self.g.construction.bonuses()['population'],5)

    def test_tax_boundary_single_payment_and_disconnection_pauses(self):
        b=self.house('tile_home'); self.shift(4*C.HOUR)
        self.g.meta['next_visitor_ts']=self.t+100*C.DAY
        self.shift(20*C.HOUR)
        self.assertGreater(self.g.meta['town_tax_total'],0)
        total=self.g.meta['town_tax_total']; self.shift(1)
        self.assertEqual(self.g.meta['town_tax_total'],total)
        self.do('remove_roads',cells=[(2,20),(3,20)])
        self.shift(C.DAY)
        self.assertEqual(self.g.meta['town_tax_total'],total)

    def test_disconnected_market_consumption_and_rent_pause_without_backpay(self):
        self.do('upgrade_zone',zone_id='market'); self.do('upgrade_zone',zone_id='homes')
        self.g.meta['next_visitor_ts']=self.t+100*C.DAY
        self.do('remove_roads',cells=[(58,13),(18,13)])
        coins=self.g.meta['coins']; self.shift(C.DAY)
        self.assertEqual(self.g.meta['coins'],coins)
        self.do('build_roads',cells=[(58,13),(18,13)])
        coins=self.g.meta['coins']; self.shift(C.HOUR)
        self.assertLessEqual(self.g.meta['coins']-coins,100)

    def test_disconnected_fields_produce_nothing_and_cannot_harvest(self):
        self.do('upgrade_zone',zone_id='fields')
        self.do('remove_roads',cells=[(54,30)])
        self.shift(12*C.HOUR)
        self.assertFalse(self.g.meta['field_ready_ts'])
        self.do('build_roads',cells=[(54,30)])
        self.shift(12*C.HOUR)
        self.assertTrue(self.g.meta['field_ready_ts'])
        self.do('remove_roads',cells=[(54,30)])
        with self.assertRaises(GameError): self.do('harvest_field')

    def test_housing_capacity_population_appears_after_construction_and_leaves_on_cut(self):
        self.house(); self.assertEqual(len([p for p in self.g.people if p['role']=='居民']),4)
        self.shift(4*C.HOUR)
        self.assertEqual(len([p for p in self.g.people if p['role']=='居民']),9)
        self.do('remove_roads',cells=[(2,20),(3,20)]); self.shift(C.HOUR)
        self.assertEqual(len([p for p in self.g.people if p['role']=='居民']),4)

    def test_offline_construction_taxes_and_population_match_incremental(self):
        self.house(); state=self.g.snapshot()
        other=Game(Database(Path(self.temp.name)/'other.db'),real_now=self.t)
        try:
            other._load(copy.deepcopy(state)); self.shift(2*C.DAY)
            for i in range(96): other.advance(self.t+(i+1)*C.HOUR/2)
            for key in ('coins','town_tax_total','next_tax_ts','town_visitors_total'):
                self.assertEqual(self.g.meta[key],other.meta[key])
            self.assertEqual(self.g.town_buildings,other.town_buildings)
            self.assertEqual(self.g.people,other.people)
        finally: other.db.close()

    def test_land_value_public_industry_and_clamp(self):
        g=self.g; self.road(); self.do('upgrade_zone',zone_id='park'); base=g.construction.land_value((5,21))
        self.do('place_town_building',catalog_key='pocket_park',cx=8,cy=21); self.shift(6*C.HOUR)
        self.assertGreater(g.construction.land_value((5,21)),base)
        self.do('place_town_building',catalog_key='steam_factory',cx=12,cy=21); self.shift(16*C.HOUR)
        self.assertLess(g.construction.land_value((15,21)),g.construction.land_value((5,21)))
        self.assertGreaterEqual(g.construction.land_factor((15,21)),.75)
        self.assertLessEqual(g.construction.land_factor((5,21)),1.4)

    def test_corrupted_roads_and_buildings_rejected(self):
        self.house()
        for mutate in (lambda s:s['roads'][0].update(cx=-1),lambda s:s['roads'][0].update(road_style='bad'),
                       lambda s:s['town_buildings'].append(copy.deepcopy(s['town_buildings'][0])),
                       lambda s:s['town_buildings'][0].update(catalog_key='unknown')):
            state=self.g.snapshot(); mutate(state)
            with self.assertRaises((ValueError,KeyError)): validate(state)

    def test_v4_migration_preserves_business_state_and_backup(self):
        self.do('upgrade_zone',zone_id='homes'); self.do('upgrade_zone',zone_id='market')
        state=self.g.snapshot(); self.g.db.close()
        conn=sqlite3.connect(self.path)
        with conn:
            conn.execute('DROP TABLE roads'); conn.execute('DROP TABLE town_buildings')
            for key in ('road_revision','construction_open_ts','next_tax_ts','roads_built','town_buildings_built','town_tax_total','build_guide_dismissed','town_cleared'):
                conn.execute('DELETE FROM meta WHERE key=?',(key,))
            conn.execute('PRAGMA user_version=4')
        conn.close()
        self.g=Game(Database(self.path),real_now=self.t)
        for key in ('pets','town_zones','people'):
            self.assertEqual(self.g.snapshot()[key],state[key])
        for key in ('coins','weather','field_batch','next_field_ts'):
            self.assertEqual(self.g.meta[key],state['meta'][key])
        self.assertTrue(all(self.g.construction.grid.connected_zones().values()))
        self.assertFalse(self.g.town_buildings)
        self.assertTrue(self.path.with_name('build.pre-v5.db').exists())

    def test_migration_failure_rolls_back_before_isolation(self):
        self.g.db.close(); conn=sqlite3.connect(self.path)
        with conn:
            conn.execute('DROP TABLE roads'); conn.execute('DROP TABLE town_buildings')
            conn.execute("DELETE FROM meta WHERE key='road_revision'")
            conn.execute('PRAGMA user_version=4')
        conn.close()
        def failure(db):
            db.conn.execute("INSERT INTO roads VALUES ('temp','{}')")
            raise ValueError('模拟迁移中断')
        with patch.object(Database,'_migrate_v5',failure):
            recovered=Database(self.path)
            self.assertIn('已恢复',recovered.recovery_message)
            recovered.close()
        evidence=list(self.path.parent.glob('build.db.damaged-*'))
        self.assertTrue(evidence)
        original=next(p for p in evidence if not p.name.endswith(('-wal','-shm')))
        conn=sqlite3.connect(original)
        self.assertEqual(conn.execute('PRAGMA user_version').fetchone()[0],4)
        self.assertFalse(conn.execute("SELECT 1 FROM sqlite_master WHERE name='roads'").fetchone())
        conn.close()


if __name__=='__main__': unittest.main()
