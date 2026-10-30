"""交通仅为路网表现：车道、转弯、重规划、安全退场与随机数隔离。"""
import copy
import math
import tempfile
import unittest
from pathlib import Path
import game_config as C
import town_catalog as K
from db import Database
from game import Game
from traffic import TrafficSystem,RoadWalker,lane_points,Vehicle
from town_grid import PORTALS,world_to_cell,cell_center


class TrafficTests(unittest.TestCase):
    def setUp(self):
        self.tmp=tempfile.TemporaryDirectory(dir=C.ROOT/'.test-output')
        self.addCleanup(self.tmp.cleanup)
        self.g=Game(Database(Path(self.tmp.name)/'traffic.db'),real_now=1800000000.,seed=3)
        self.addCleanup(self.g.db.close)
        self.traffic=TrafficSystem(17)

    def test_new_town_generates_three_vehicle_types_without_buildings(self):
        for _ in range(300): self.traffic.update(.05,self.g)
        self.assertTrue(self.traffic.vehicles)
        self.assertEqual({v.kind for v in self.traffic.vehicles},{'car','bus','truck'})

    def test_lane_offset_keeps_right_hand_side(self):
        east=lane_points([(5,5),(6,5)])
        west=lane_points([(6,5),(5,5)])
        self.assertTrue(all(y==cell_center(5,5)[1]+K.LANE_OFFSET for x,y in east))
        self.assertTrue(all(y==cell_center(5,5)[1]-K.LANE_OFFSET for x,y in west))

    def test_turns_and_dead_end_uturn_remain_on_road(self):
        for path in ([(5,5),(6,5),(6,6)],[(5,5),(6,5),(6,4)],[(5,5),(6,5),(5,5)]):
            with self.subTest(path=path):
                points=lane_points(path)
                self.assertTrue(all(world_to_cell(x,y) in path for x,y in points))
                self.assertTrue(all(math.hypot(a[0]-b[0],a[1]-b[1])<35 for a,b in zip(points,points[1:])))

    def test_moving_vehicles_never_leave_the_grid_roads(self):
        for _ in range(1200):
            self.traffic.update(.05,self.g)
            self.assertTrue(all(v.cell in self.g.construction.grid.roads for v in self.traffic.vehicles))

    def test_revision_replans_or_safely_removes_vehicles(self):
        for _ in range(50): self.traffic.update(.05,self.g)
        victim=self.traffic.vehicles[0]; removed=victim.path[len(victim.path)//2]
        self.g.perform('remove_roads',cells=[removed],real_now=self.g.meta['real_anchor'])
        self.traffic.update(.05,self.g)
        for v in self.traffic.vehicles:
            self.assertEqual(v.revision,self.g.meta['road_revision'])
            self.assertTrue(all(c in self.g.construction.grid.roads for c in v.path))

    def test_all_roads_deleted_no_crash_no_vehicle_in_grass(self):
        self.traffic.update(.05,self.g)
        self.g.perform('remove_roads',cells=list(self.g.construction.grid.roads),real_now=self.g.meta['real_anchor'])
        self.traffic.update(.05,self.g)
        self.assertEqual(self.traffic.vehicles,[])

    def test_population_peak_and_weather_multipliers_and_cap(self):
        g=self.g; g.people=g.people*14
        g.meta['sim_ts']=g.now//C.DAY*C.DAY+8*C.HOUR
        clear=self.traffic.target_count(g)
        g.meta['weather']='暴雨'; rain=self.traffic.target_count(g)
        self.assertLess(rain,clear)
        g.meta['weather']='晴天'; g.meta['sim_ts']=g.now//C.DAY*C.DAY+23*C.HOUR
        self.assertLess(self.traffic.target_count(g),clear)
        self.assertLessEqual(clear,K.MAX_VEHICLES)

    def test_dt_is_clamped_no_teleport(self):
        self.traffic.update(.05,self.g)
        positions={v.vehicle_id:(v.x,v.y) for v in self.traffic.vehicles}
        self.traffic.update(60,self.g)
        for v in self.traffic.vehicles:
            if v.vehicle_id in positions:
                x,y=positions[v.vehicle_id]
                self.assertLessEqual(math.hypot(v.x-x,v.y-y),max(K.VEHICLE_SPEEDS.values())*K.TRAFFIC_MAX_DT+.01)

    def test_weather_and_night_reduce_actual_vehicle_speed(self):
        from unittest.mock import patch
        path=self.g.construction.grid.shortest_path(PORTALS['estate'],PORTALS['market']); points=lane_points(path)
        v=Vehicle('speed-test','car',(150,170,150),*points[0],path,path[-1],'庄园','商街',points=points,revision=0)
        self.traffic.vehicles=[v]
        self.g.meta['sim_ts']=self.g.now//C.DAY*C.DAY+11*C.HOUR
        with patch.object(self.traffic,'target_count',return_value=1):
            self.traffic.update(.05,self.g); clear=v.speed
            self.g.meta['weather']='暴雨'; self.traffic.update(.05,self.g); self.assertLess(v.speed,clear)
            self.g.meta['weather']='晴天'; self.g.meta['sim_ts']+=11*C.HOUR
            self.traffic.update(.05,self.g); self.assertLess(v.speed,clear)

    def test_vehicle_and_walker_updates_do_not_consume_game_rng(self):
        before=self.g.rng.getstate(); state=self.g.snapshot()
        p=self.g.people[0]; x,y=cell_center(*PORTALS['estate']); walker=RoadWalker(p['person_id'],x,y)
        for _ in range(200):
            self.traffic.update(.05,self.g); walker.update(.05,p,self.g.construction.grid)
        self.assertEqual(before,self.g.rng.getstate())
        self.assertEqual(state,self.g.snapshot())

    def test_walker_routes_on_sidewalk_and_recovers_from_cut(self):
        p=copy.deepcopy(self.g.people[0]); p['destination']='market'
        x,y=cell_center(*PORTALS['homes']); walker=RoadWalker(p['person_id'],x,y)
        for _ in range(100): walker.update(.25,p,self.g.construction.grid)
        self.assertIn(world_to_cell(walker.x,walker.y),self.g.construction.grid.roads)
        self.g.perform('remove_roads',cells=[(58,13)],real_now=self.g.meta['real_anchor'])
        for _ in range(100):
            walker.update(.25,p,self.g.construction.grid)
            self.assertIn(world_to_cell(walker.x,walker.y),self.g.construction.grid.roads)
        self.assertNotEqual(world_to_cell(*walker.points[-1]),PORTALS['market'])

    def test_walker_without_any_road_waits_hidden(self):
        p=self.g.people[0]; x,y=cell_center(*PORTALS['estate']); walker=RoadWalker(p['person_id'],x,y)
        self.g.perform('remove_roads',cells=list(self.g.construction.grid.roads),real_now=self.g.meta['real_anchor'])
        walker.update(.25,p,self.g.construction.grid)
        self.assertFalse(walker.visible)

    def test_walker_visits_multiple_connected_districts_and_reports_activity(self):
        p=copy.deepcopy(self.g.people[0]); p['destination']='estate'; p['status']='生活'
        walker=RoadWalker(p['person_id'],*cell_center(*PORTALS['estate']))
        visited=set(); activities=set(); grid=self.g.construction.grid
        for _ in range(18000):
            walker.update(.25,p,grid)
            if walker.activity and not walker.activity.startswith('前往'):
                activities.add(walker.activity)
                if walker.target and world_to_cell(walker.x,walker.y)==PORTALS[walker.target]:
                    visited.add(walker.target)
            self.assertIn(world_to_cell(walker.x,walker.y),grid.roads)
        self.assertGreaterEqual(len(visited),3)
        self.assertGreaterEqual(len(activities),3)

    def test_departing_visitor_stays_at_station(self):
        p=copy.deepcopy(self.g.people[0]); p.update(destination='station',status='离镇')
        walker=RoadWalker(p['person_id'],*cell_center(*PORTALS['station']))
        for _ in range(200): walker.update(.25,p,self.g.construction.grid)
        self.assertEqual(walker.target,'station')


if __name__=='__main__': unittest.main()
