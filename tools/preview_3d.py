"""使用独立临时存档检查三维画面，生成原生分辨率截图。"""
import os
import sys
import time
import tempfile
import argparse
import json
from pathlib import Path
os.environ['SDL_VIDEODRIVER']='dummy'
os.environ['SDL_AUDIODRIVER']='dummy'
os.environ['PYGAME_HIDE_SUPPORT_PROMPT']='1'
ROOT=Path(__file__).resolve().parents[1];sys.path.insert(0,str(ROOT))
import pygame
import game_config as C
from game import Game
from db import Database
from interface import ManorApp
from pet import Pet


def main():
    parser=argparse.ArgumentParser();parser.add_argument('--stress',action='store_true');args=parser.parse_args()
    out=ROOT/'.test-output';out.mkdir(exist_ok=True)
    with tempfile.TemporaryDirectory() as folder:
        g=Game(Database(Path(folder)/'preview.db'),real_now=1800000000.,seed=42);app=None
        try:
            g.meta['sim_ts']=g.meta['start_ts']//C.DAY*C.DAY+12*C.HOUR;g.meta['weather']='晴天'
            for species in ('猫咪','小狗','绵羊','梅花鹿','小鸭'):
                p=Pet.create(species,g.now,0,g.rng);g.pets[p.pet_id]=p
            app=ManorApp(g,settings_path=Path(folder)/'ui.json',scene_mode='3d')
            app.toast_until=0;app.draw();pygame.image.save(app.screen,str(out/'3d-default.png'))
            app.set_window_size((1600,1000));app.toast_until=0
            for i,actor in enumerate(app.actor_order):actor.x=280+i*115;actor.y=609+(i%2)*35
            view=app.courtyard
            print(view.engine.backend,view.engine.failure or '')
            for name,center,yaw,zoom,hour in (('estate',(1800,1070),26,1.35,12),('rotated',(1800,1070),-30,1.35,12),
                       ('town',(1800,850),20,.7,12),('station',(1780,380),25,1.4,12),('night',(1800,1070),26,1.35,21)):
                view.camera.center(center);view.camera.yaw=yaw*3.14159265/180;view.camera.zoom=zoom
                g.meta['sim_ts']=g.meta['start_ts']//C.DAY*C.DAY+hour*C.HOUR
                app.draw();path=out/f'3d-{name}.png';pygame.image.save(app.screen,str(path));print(path)
            view.camera.center((1800,1070));view.camera.zoom=1.;g.meta['sim_ts']=g.meta['start_ts']//C.DAY*C.DAY+12*C.HOUR
            start=time.perf_counter()
            for i in range(12):view.tick(1/30,g);app.draw()
            print(f'12帧平均 {(time.perf_counter()-start)/12*1000:.1f}ms')
            if args.stress:
                from traffic import Vehicle,lane_points
                from town_grid import line_cells,cell_center
                g.people += [g.town._person('游客',g.now,100+i) for i in range(80-len(g.people))]
                view.tick(0,g)
                path=line_cells((22,13),(52,13))+line_cells((52,13),(52,30))[1:]+line_cells((52,30),(22,30))[1:]
                points=lane_points(path);vehicles=[]
                for i in range(40):
                    index=1+i*4;x,y=points[index];kind=('car','bus','truck')[i%3]
                    vehicles.append(Vehicle(f'stress-{i}',kind,(155,176,163),x,y,path,path[-1],'入口','终点',points=points,index=index+1,revision=g.meta['road_revision']))
                view.traffic.vehicles=vehicles
                for i,p in enumerate(g.people):
                    a=view.people[p['person_id']];a.x,a.y=cell_center(*path[i%len(path)]);a.path=path;a.points=points;a.index=1
                # 固定位置，只推进显示动画；不调用经营结算或写入正式存档。
                samples=[]
                for i in range(100):
                    for actor in view.people.values():actor.anim=i/30
                    begin=time.perf_counter();app.draw();elapsed=(time.perf_counter()-begin)*1000
                    if i>=40:samples.append(elapsed)
                stats=dict(backend=view.engine.backend,size=app.screen.get_size(),people=80,vehicles=40,pets=len(app.actor_order),
                           frames=len(samples),mean_ms=sum(samples)/len(samples),p95_ms=sorted(samples)[int(len(samples)*.95)],max_ms=max(samples))
                (out/'3d-performance.json').write_text(json.dumps(stats,ensure_ascii=False,indent=2),encoding='utf-8')
                pygame.image.save(app.screen,str(out/'3d-stress.png'));print(json.dumps(stats,ensure_ascii=False))
        finally:
            if app:app.courtyard.close()
            g.db.close();pygame.quit()


if __name__=='__main__':main()
