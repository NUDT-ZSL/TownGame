"""v5真实键鼠输入与截图/性能验证，由ui_smoke在原有全部断言之后调用。"""
import copy
import json
import os
import platform
import statistics
import sqlite3
import tempfile
from pathlib import Path
import cProfile
import io
import pstats
import time
from unittest.mock import patch
import pygame
import game_config as C
import town_catalog as K
import town_art
from town_grid import cell_center,line_cells,world_to_cell
from db import validate,Database
from traffic import Vehicle,lane_points


def exercise(app,g,output):
    app.close_modal(); app.builder.exit(); app.pan=None; app.drag=None
    # UI测试固定真实时钟，避免机器负载改变快进模式的模拟时差。
    with patch('game.time.time',return_value=g.meta['real_anchor']):
        g.meta['coins']=100000
        for zone in C.TOWN_ZONES:
            if zone!='estate' and not g.town.level(zone): app.act('upgrade_zone',zone_id=zone)
        def event(kind,**args): app.handle(pygame.event.Event(kind,**args))
        def key(k,mod=0): event(pygame.KEYDOWN,key=k,mod=mod)
        def move(cell):
            pos=app.courtyard.camera.world_to_screen(cell_center(*cell))
            event(pygame.MOUSEMOTION,pos=pos,rel=(0,0),buttons=(0,0,0)); return pos
        def click(pos):
            event(pygame.MOUSEBUTTONDOWN,button=1,pos=pos); event(pygame.MOUSEBUTTONUP,button=1,pos=pos)
        camera=app.courtyard.camera; camera.zoom=1; camera.center((600,950))
        key(pygame.K_b); assert app.builder.active
        app.builder.style='stone'; before=g.snapshot(); start=move((2,20))
        event(pygame.MOUSEBUTTONDOWN,button=1,pos=start)
        move((8,20)); move((18,20))
        app.toast_until=0; app.draw(); pygame.image.save(app.screen,str(output/'build-mode.png'))
        assert g.snapshot()==before,'道路预览修改了存档'
        coins=g.meta['coins']; event(pygame.MOUSEBUTTONUP,button=1,pos=app.builder.pointer)
        assert g.meta['coins']==coins-17*14,'快速拖拽漏格或预览费用不一致'
        assert all((x,20) in g.construction.grid.roads for x in range(2,19))
        key(pygame.K_z,pygame.KMOD_CTRL)
        assert g.meta['coins']==coins and (2,20) not in g.construction.grid.roads,'撤销未恢复费用或路网'
        start=move((2,20)); event(pygame.MOUSEBUTTONDOWN,button=1,pos=start); move((18,20))
        event(pygame.MOUSEBUTTONUP,button=1,pos=app.builder.pointer)
        app.act('build_roads',cells=line_cells((19,20),(22,20)),style='garden')
        before=g.snapshot(); event(pygame.MOUSEBUTTONDOWN,button=1,pos=move((3,19))); move((6,19)); key(pygame.K_ESCAPE)
        event(pygame.MOUSEBUTTONUP,button=1,pos=app.builder.pointer)
        assert before==g.snapshot(),'Esc取消道路后仍提交了笔画'
        # 三种道路与拆除均走真实输入；固定退款向下取整。
        app.builder.style='asphalt'; start=move((3,18)); event(pygame.MOUSEBUTTONDOWN,button=1,pos=start)
        event(pygame.MOUSEBUTTONUP,button=1,pos=start); coins=g.meta['coins']; key(pygame.K_DELETE)
        click(start); assert g.meta['coins']==coins+5 and (3,18) not in g.construction.grid.roads
        before_camera=camera.x,camera.y
        event(pygame.MOUSEBUTTONDOWN,button=3,pos=(600,430))
        event(pygame.MOUSEMOTION,pos=(520,450),rel=(-80,20),buttons=(0,0,1))
        event(pygame.MOUSEBUTTONUP,button=3,pos=(520,450))
        assert (camera.x,camera.y)!=before_camera,'建设模式右键平移失效'
        with patch('pygame.mouse.get_pos',return_value=(600,400)):
            event(pygame.MOUSEWHEEL,x=0,y=1)
        assert camera.zoom>1
        camera.zoom=1; camera.center((600,950))
        key(pygame.K_2); app.draw()
        click((1035,213)) # 现代风格页签。
        assert app.builder.family=='modern' and app.builder.selected=='glass_home'
        move((30,20)); p=app.builder._preview()[0]
        assert p['reason'] and '保留地' in p['reason'],'非法建筑幽灵未给出原因'
        before=g.snapshot(); click(app.builder.pointer); assert g.snapshot()==before
        move((10,21)); key(pygame.K_r); app.draw()
        rng=g.rng.getstate(); coins=g.meta['coins']; click(app.builder.pointer)
        assert len(g.town_buildings)==1 and g.town_buildings[0]['status']=='building'
        assert g.meta['coins']==coins-K.CATALOG['glass_home']['cost']
        assert g.rng.getstate()==rng,'建造建筑消耗了宠物随机数'
        b=g.town_buildings[0]; key(pygame.K_b); assert not app.builder.active
        app.open('town_building',{'building_id':b['building_id']}); app.draw()
        pygame.image.save(app.screen,str(output/'building-detail.png')); app.close_modal()
        # 一条连通干道旁放置四种风格及公共建筑，均通过实际建设动作。
        app.act('build_roads',cells=line_cells((60,13),(60,22))+line_cells((53,22),(73,22)),style='stone')
        samples=(('tea_house',53,20),('glass_home',56,19),('cream_home',61,20),('steam_factory',64,19),('pocket_park',70,19))
        for name,cx,cy in samples:
            assert app.act('place_town_building',catalog_key=name,cx=cx,cy=cy)
        rate=C.SPEED if g.meta['mode']=='fast' else 1
        g.advance(g.meta['real_anchor']+16*C.HOUR/rate)
        # 冻结时钟重新锚定，防止后续操作被较早的patch时钟回拨。
        app.refresh()
        assert all(b['status']=='ready' for b in g.town_buildings),'离线施工未完成'
        camera.zoom=.85; camera.center((3000,950))
        for _ in range(200): app.courtyard.tick(.05,g)
        app.toast_until=0; app.draw(); pygame.image.save(app.screen,str(output/'mixed-town.png'))
        # 全15种目录美术，以及四风格不能只换色。
        sheet=pygame.Surface((1250,660)); sheet.fill((246,239,222)); signatures=set()
        for i,(name,spec) in enumerate(K.CATALOG.items()):
            for direction in (0,1):
                for light,season,weather,stage in (('day','春','晴天',4),('night','冬','降雪',4),('day','秋','暴雨',1)):
                    sprite=town_art.building_sprite(name,direction,light,season,weather,stage)
                    assert pygame.mask.from_surface(sprite).count()>100
            sprite=town_art.building_sprite(name,0,'day','春','晴天')
            signatures.add(pygame.image.tostring(sprite,'RGBA'))
            preview=pygame.transform.smoothscale(sprite,(145,165)); x,y=(i%5)*250,(i//5)*220
            sheet.blit(preview,(x+50,y+8)); app.fonts.text(sheet,spec['name'],(x+67,y+180),16)
        assert len(signatures)==15
        pygame.image.save(sheet,str(output/'building-catalog.png'))
        original_now,original_weather=g.now,g.meta['weather']
        g.meta['sim_ts']=(g.now//C.DAY+1)*C.DAY+22*C.HOUR; g.meta['weather']='小雨'
        for _ in range(220): app.courtyard.tick(.05,g)
        app.draw(); pygame.image.save(app.screen,str(output/'traffic-night.png'))
        g.meta['sim_ts'],g.meta['weather']=original_now,original_weather
        # 车辆轻量信息卡与建筑详情点击。
        app.draw()
        visible=[(r,ident) for r,ident in app.courtyard.vehicle_hits if pygame.Rect(0,175,950,480).collidepoint(r.center)]
        if not visible:
            vehicle=app.courtyard.traffic.vehicles[0]; camera.center((vehicle.x,vehicle.y)); app.draw(); visible=app.courtyard.vehicle_hits
        assert visible,'没有可点击的车辆'
        rect,_=visible[0]; ident=app.courtyard.vehicle_at(rect.center); click(rect.center)
        assert app.courtyard.vehicle_card==ident and app.modal is None,'车辆信息没有使用轻量卡片'
        app.draw(); pygame.image.save(app.screen,str(output/'traffic-card.png')); app.courtyard.vehicle_card=None
        camera.zoom=.85; camera.center((3000,950)); app.draw()
        rect,ident=app.courtyard.town_building_hits[0]; click(rect.center)
        assert app.page==('town_building',{'building_id':ident}); app.close_modal()
        # 拆除建筑必须确认；入口道路中断不能继续收益。
        house=g.town_buildings[0]
        app.act('remove_roads',cells=[(10,20),(11,20),(12,20)])
        assert not g.construction.building_connected(house)
        app.open('town_building',{'building_id':house['building_id']}); app.draw()
        pygame.image.save(app.screen,str(output/'building-disconnected.png')); app.close_modal()
        key(pygame.K_b); key(pygame.K_6); camera.zoom=1; camera.center((600,950)); app.draw()
        click(camera.world_to_screen(cell_center(house['cx'],house['cy'])))
        assert app.page and app.page[0]=='confirm','拆除没有确认'
        app.modal.buttons[0].callback(); assert house not in g.town_buildings
        app.close_modal(); app.builder.exit()
        # 地价覆层、道路局部缓存不触发全世界重建。
        app.builder.enter(); app.builder.land_overlay=True; camera.zoom=.85; camera.center((3000,950))
        app.draw(); pygame.image.save(app.screen,str(output/'land-value.png'))
        static_builds=app.courtyard.static_builds; chunks=app.courtyard.road_layer.build_count
        app.act('build_roads',cells=[(72,23)],style='garden'); app.draw()
        assert app.courtyard.static_builds==static_builds,'修路导致整张世界重建'
        assert app.courtyard.road_layer.build_count-chunks<=9,'修路失效了过多道路chunk'
        app.builder.guide_button.callback()
        assert g.meta['build_guide_dismissed'] and g.db.load()['meta']['build_guide_dismissed'],'引导关闭状态未持久化'
        app.builder.exit(); validate(g.snapshot())
        # 最大工作集性能采样：80人、40车、5宠物、数百道路与高层；测完整update+draw。
        before=copy.deepcopy(g.snapshot())
        try:
            extra=[]
            for row in range(34,47,2): extra+=line_cells((25,row),(49,row))
            extra+=line_cells((25,30),(25,46))
            app.act('build_roads',cells=extra)
            for name,cx in (('office',26),('glass_home',31),('office',35),('glass_home',40)):
                assert app.act('place_town_building',catalog_key=name,cx=cx,cy=31)
            g.advance(g.meta['real_anchor']+16*C.HOUR/rate)
            g.people+=[g.town._person('游客',g.now,100+i) for i in range(K.MAX_VEHICLES*2-len(g.people))]
            g.meta['sim_ts']=(g.now//C.DAY+1)*C.DAY+8*C.HOUR
            g.meta['weather']='晴天'
            app._sync_actors(); camera.zoom=.6; camera.center((1900,1050))
            traffic=app.courtyard.traffic
            path=line_cells((22,13),(52,13))+line_cells((52,13),(52,30))[1:]+line_cells((52,30),(22,30))[1:]+line_cells((22,30),(22,13))[1:]
            assert all(c in g.construction.grid.roads for c in path)
            points=lane_points(path)
            app.courtyard.tick(0,g)
            for i,person in enumerate(g.people):
                person['destination']='estate'
                walker=app.courtyard.people[person['person_id']]
                cell=path[i%len(path)]; walker.x,walker.y=cell_center(*cell); walker.revision=-1
            # 固定路网中的长途队列，间隔约60像素，保证测试时确有40辆车。
            candidates=[]; distance=0; next_mark=65
            for i,(a,b) in enumerate(zip(points,points[1:])):
                length=((a[0]-b[0])**2+(a[1]-b[1])**2)**.5
                while length and distance+length>=next_mark:
                    t=(next_mark-distance)/length
                    candidates.append((i,a[0]+(b[0]-a[0])*t,a[1]+(b[1]-a[1])*t)); next_mark+=65
                distance+=length
            assert len(candidates)>=40,f"长途压力场景路径不足：path={len(path)} points={len(points)} candidates={len(candidates)} start={(25,46) in g.construction.grid.roads} goal={(73,22) in g.construction.grid.roads} {app.toast}"
            traffic.vehicles=[]
            for n,(index,x,y) in enumerate(candidates[:40]):
                kind=('car','bus','truck')[n%3]
                traffic.vehicles.append(Vehicle(f'bench-{n}',kind,(157,181,164),x,y,path,path[-1],'长途入口','商业街',points=points,index=index+1,revision=g.meta['road_revision']))
            # benchmark强制40车；生产上限仍由target_count按经济需求计算。
            with patch.object(traffic,'target_count',return_value=40):
                rng=g.rng.getstate()
                for _ in range(40): app.courtyard.tick(1/30,g); app.draw()
                samples_ms=[]
                for _ in range(180):
                    start=time.perf_counter(); app.courtyard.tick(1/30,g); app.draw(); samples_ms.append((time.perf_counter()-start)*1000)
                assert rng==g.rng.getstate(),'交通/绘制污染了游戏随机数'
            stats=dict(python=platform.python_version(),platform=platform.platform(),cpu=os.environ.get('PROCESSOR_IDENTIFIER',''),driver=pygame.display.get_driver(),
                       frames=len(samples_ms),warmup=40,mean_ms=statistics.mean(samples_ms),p95_ms=sorted(samples_ms)[int(.95*len(samples_ms))],max_ms=max(samples_ms),
                       people=len(g.people),vehicles=len(traffic.vehicles),pets=len(app.actor_order),roads=len(g.roads),buildings=len(g.town_buildings))
            stats['visible_people']=len(app.courtyard.person_hits); stats['visible_vehicles']=len(app.courtyard.vehicle_hits)
            (output/'performance.json').write_text(json.dumps(stats,ensure_ascii=False,indent=2),encoding='utf-8')
            print('v5 performance:',json.dumps(stats,ensure_ascii=False))
            if stats['mean_ms']>=16:
                profiler=cProfile.Profile(); profiler.enable()
                for _ in range(40): app.courtyard.tick(1/30,g); app.draw()
                profiler.disable(); report=io.StringIO()
                pstats.Stats(profiler,stream=report).sort_stats('cumtime').print_stats(24)
                (output/'frame-profile.txt').write_text(report.getvalue(),encoding='utf-8')
            assert stats['people']==80 and stats['vehicles']==40 and stats['pets']==5
            assert stats['visible_people']==80 and stats['visible_vehicles']==40
            assert stats['mean_ms']<16,'平均帧时间未达到16ms目标'
            assert stats['max_ms']<50,'出现明显超过50ms的渲染帧'
        finally:
            g._load(before); app.courtyard.people={}; app.courtyard.traffic.vehicles=[]; app._sync_actors()
        app.courtyard.home(); app.draw(); validate(g.snapshot())
        print('v5 UI passed: road preview/commit/refund/undo/cancel, pan/zoom, catalog/style/rotation, ghost legality, construction/details/demolition, 15 building art variants, traffic cards, connectivity, land overlay and local chunk invalidation.')
        migration_preview(output)


def migration_preview(output):
    """真正v4 SQLite结构经迁移后截图；使用专门测试档，保留全部旧字段。"""
    from game import Game
    from interface import ManorApp
    with tempfile.TemporaryDirectory(dir=output) as temp:
        path=Path(temp)/'legacy.db'; t=1800000000.
        old=Game(Database(path),real_now=t,seed=11)
        old.meta['coins']=10000
        for zone in ('market','homes','park','station','fields'):
            old.perform('upgrade_zone',zone_id=zone,real_now=t)
        old.advance(t+12*C.HOUR); before=old.snapshot(); old.db.close()
        conn=sqlite3.connect(path)
        with conn:
            conn.execute('DROP TABLE roads'); conn.execute('DROP TABLE town_buildings')
            for field in ('road_revision','construction_open_ts','next_tax_ts','roads_built','town_buildings_built','town_tax_total','build_guide_dismissed','town_cleared'):
                conn.execute('DELETE FROM meta WHERE key=?',(field,))
            conn.execute('PRAGMA user_version=4')
        conn.close()
        migrated=Game(Database(path),real_now=before['meta']['real_anchor'])
        try:
            for field in ('pets','town_zones','people'):
                assert migrated.snapshot()[field]==before[field]
            for field in ('coins','weather','field_batch','field_ready_ts','next_field_ts'):
                assert migrated.meta[field]==before['meta'][field]
            assert len(migrated.roads)==114 and not migrated.town_buildings
            assert all(migrated.construction.grid.connected_zones().values())
            preview=ManorApp(migrated); preview.open('map'); preview.draw()
            pygame.image.save(preview.screen,str(output/'migrated-town.png'))
            print('v4 migration screenshot passed: pets/coins/districts/people/weather/farm preserved; 114 free connected roads.')
        finally: migrated.close(real_now=before['meta']['real_anchor'])
