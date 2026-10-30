"""自治领域回归：真实资源、公共预算、出生、路径、离线及事务回滚。"""
import copy
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch
import game_config as C
from game import Game
from db import Database,SaveError,validate
from settlement import road_plan,entrances,GROW_UP
from town_grid import PORTALS,RoadGrid,terrain,cell_key


class SettlementTests(unittest.TestCase):
    def setUp(self):
        self.temp=tempfile.TemporaryDirectory();self.path=Path(self.temp.name)/'game.db'
        self.g=Game(Database(self.path),real_now=1800000000,seed=7)
        self.addCleanup(self.temp.cleanup);self.addCleanup(self.g.db.close)

    def shift(self,seconds):self.g.advance(self.g.meta['real_anchor']+seconds)
    @property
    def s(self):return self.g.meta['settlement']

    def test_pause_refunds_reserved_resources_and_reopen_does_not_grant_again(self):
        stock=copy.deepcopy(self.s['stock']);funds=self.s['funds']
        worker=self.g.people[0]
        self.assertTrue(self.g.settlement.enqueue('crop','estate',worker,{'seed':1},8))
        self.assertEqual(self.s['stock']['seed'],stock['seed']-1)
        self.g.perform('autonomy',real_now=self.g.meta['real_anchor'])
        self.assertEqual(self.s['stock'],stock);self.assertEqual(self.s['funds'],funds)
        self.g.db.close();self.g=Game(Database(self.path),real_now=self.g.meta['real_anchor'])
        self.addCleanup(self.g.db.close)
        self.assertFalse(self.s['enabled']);self.assertEqual(self.s['stock'],stock)

    def test_crop_requires_seed_and_elapsed_work_then_produces_real_food(self):
        p=self.g.people[0];stock=self.s['stock'];before=stock['food'];seed=stock['seed']
        self.assertTrue(self.g.settlement.enqueue('crop','estate',p,{'seed':1},8))
        due=self.s['jobs'][0]['due']
        with patch.object(self.g.settlement,'plan'):
            self.shift(C.HOUR)
            self.assertEqual(stock['food'],before)
            self.shift(due-self.g.now)
        self.assertGreater(stock['food'],before);self.assertEqual(stock['seed'],seed+2)
        self.assertEqual(self.s['completed'],1)

    def test_astar_repairs_a_gap_and_does_not_cross_reserved_land(self):
        cut=(53,13);self.g.roads=[r for r in self.g.roads if (r['cx'],r['cy'])!=cut]
        self.g.meta['road_revision']+=1;self.g.construction.invalidate()
        self.assertFalse(self.g.construction.connected('market'))
        cells=road_plan(self.g,[PORTALS['market']]);self.assertIn(cut,cells)
        self.assertTrue(all(terrain(c) in ('grass','zone') for c in cells))
        self.assertTrue(self.g.settlement.enqueue('road','estate',self.g.people[0],{'funds':10*len(cells)},1,cells=cells))
        self.g.meta['sim_ts']=self.s['jobs'][0]['due'];self.g.settlement.complete(self.s['jobs'][0])
        self.assertTrue(self.g.construction.connected('market'))

    def test_occupied_road_route_cancels_and_refunds(self):
        cells=[(38,40)];before=self.s['funds']
        self.g.settlement.enqueue('road','estate',self.g.people[0],{'funds':10},1,cells=cells)
        with patch.object(self.g.construction,'reason',return_value='已占用'):
            self.g.settlement.complete(self.s['jobs'][0])
        self.assertEqual(self.s['funds'],before);self.assertFalse(self.s['jobs'])

    def test_jobs_reserve_worker_and_no_duplicate_construction(self):
        self.g.settlement.plan();self.g.settlement.plan()
        jobs=self.s['jobs'];self.assertLessEqual(len(jobs),3)
        self.assertEqual(len({j['worker'] for j in jobs}),len(jobs))
        self.assertLessEqual(sum(j['kind']=='build' for j in jobs),1)

    def test_birth_requires_capacity_and_food_then_child_grows(self):
        for z in self.g.town_zones:
            if z['zone_id']=='homes':z['level']=1
        before=len(self.g.people);self.g.settlement.families()
        mother=next(p for p in self.g.people if p.get('pregnant_until'))
        self.g.meta['sim_ts']=mother['pregnant_until'];self.g.settlement.families()
        self.assertGreater(len(self.g.people),before)
        child=next(p for p in self.g.people if p.get('parents'))
        self.assertEqual(child['profession'],'孩童');self.assertFalse(child['job_id'])
        self.g.meta['sim_ts']=child['born_ts']+GROW_UP;self.g.settlement.families()
        self.assertNotEqual(child['profession'],'孩童')

    def test_no_birth_when_housing_or_food_missing(self):
        self.g.settlement.families()
        self.assertFalse(any(p.get('pregnant_until') for p in self.g.people))
        next(z for z in self.g.town_zones if z['zone_id']=='homes')['level']=1
        self.s['stock']['food']=0;self.g.settlement.families()
        self.assertFalse(any(p.get('pregnant_until') for p in self.g.people))

    def test_pause_does_not_switch_to_automatic_population_fill(self):
        next(z for z in self.g.town_zones if z['zone_id']=='homes')['level']=1
        count=len(self.g.people);self.g.settlement.toggle()
        self.g.town._residents()
        self.assertEqual(len(self.g.people),count)

    def test_real_community_building_uses_materials_without_player_spending(self):
        coins=self.g.meta['coins'];wood=self.s['stock']['wood']
        site,zone=self.g.settlement.home_site()
        self.g.settlement.enqueue('build',zone,self.g.people[0],{'funds':240,'wood':12,'stone':8},3,building=site)
        job=self.s['jobs'][0];self.g.meta['sim_ts']=job['due'];self.g.settlement.complete(job)
        self.assertTrue(self.g.town_buildings[-1]['community']);self.assertEqual(self.g.meta['coins'],coins)
        self.assertEqual(self.s['stock']['wood'],wood-12)
        validate(self.g.snapshot())

    def test_save_failure_rolls_back_every_reserved_resource(self):
        before=self.g.snapshot()
        with patch.object(self.g.db,'save',side_effect=SaveError('磁盘失败')):
            with self.assertRaises(SaveError):self.g.perform('plan_roads',real_now=self.g.meta['real_anchor'])
        self.assertEqual(self.g.snapshot(),before)

    def test_offline_and_hourly_progress_have_same_communal_state(self):
        state=self.g.snapshot()
        other=Game(Database(Path(self.temp.name)/'other.db'),real_now=1800000000,seed=7)
        self.addCleanup(other.db.close);other._load(state)
        for _ in range(48):self.shift(C.HOUR)
        other.advance(other.meta['real_anchor']+2*C.DAY)
        self.assertEqual(self.s,other.meta['settlement'])
        self.assertEqual(self.g.town_buildings,other.town_buildings)

    def test_upgrade_does_not_replay_autonomy_before_install(self):
        state=self.g.snapshot();del state['meta']['settlement']
        self.g.db.save(state);anchor=self.g.meta['real_anchor'];self.g.db.close()
        self.g=Game(Database(self.path),real_now=anchor+2*C.DAY,seed=7)
        self.addCleanup(self.g.db.close)
        self.assertEqual(self.s['completed'],0);self.assertEqual(self.s['births'],0)
        self.assertEqual(self.s['funds'],3000)
        self.assertGreater(self.s['next_tick'],self.g.now)

    def test_empty_road_network_can_bootstrap_public_root(self):
        self.g.roads=[];self.g.meta['road_revision']+=1;self.g.construction.invalidate()
        self.assertEqual(road_plan(self.g,[PORTALS['estate']]),[PORTALS['estate']])
