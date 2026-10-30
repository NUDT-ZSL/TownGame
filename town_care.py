"""居民从庄园入口出发照顾宠物的临时视觉行为。"""
from collections import deque
import math
import pygame
import courtyard
from db import SaveError
from game import GameError


def farm_path(view, pet):
    """在南侧空地寻路；将房屋和固定设施当作障碍，不可达时返回空列表。"""
    step = 20
    start = (32, 33)  # 庄园围栏中央开口内侧 (640, 660)
    rects = [r.inflate(36, 36) for r in view.farm.building_hits.values()]
    rects += [pygame.Rect(x-40,y-40,80,80) for x,y in (courtyard.BOWL,courtyard.BATH,courtyard.TOY)]
    target = (pet.x, pet.y)
    if not (60 <= pet.x <= 1220 and 530 <= pet.y <= 690):
        return []
    def clear(cell):
        x,y=cell[0]*step,cell[1]*step
        return 60 <= x <= 1220 and 530 <= y <= 660 and not any(r.collidepoint(x,y) for r in rects)
    queue=deque([start]); parents={start:None}; end=None
    while queue:
        cell=queue.popleft(); x,y=cell[0]*step,cell[1]*step
        if math.hypot(x-target[0], y-target[1]) <= 55:
            end=cell; break
        for nxt in ((cell[0]+1,cell[1]),(cell[0]-1,cell[1]),(cell[0],cell[1]+1),(cell[0],cell[1]-1)):
            if nxt not in parents and clear(nxt):
                parents[nxt]=cell;queue.append(nxt)
    if end is None:
        return []
    result=[]
    while end is not None:
        result.append((end[0]*step,end[1]*step));end=parents[end]
    return list(reversed(result))


class TownCare:
    def __init__(self):
        self.session=None
        self.visits={}
        self.cooldown=0.0

    def tick(self,dt):
        self.cooldown=max(0,self.cooldown-dt)

    def start(self,view,actor,person,game,pets):
        if self.session or self.cooldown or not actor.wait or actor.target != 'estate':return
        visit=(actor.target,actor.visits)
        if self.visits.get(actor.ident)==visit:return
        self.visits[actor.ident]=visit
        if person.get('status')=='离镇' or not game.meta.get('caretaker') or not game.inventory.count('普通饲料'):return
        view.farm._layout_buildings(game)
        candidates=[a for a in pets if a.pet_id in game.pets and game.pets[a.pet_id].active
                    and not game.pets[a.pet_id].fostered and game.pets[a.pet_id].hunger<45]
        for pet in sorted(candidates,key=lambda p:game.pets[p.pet_id].hunger):
            route=farm_path(view,pet)
            if route:
                origin=(actor.x,actor.y)
                points=[view.local_to_world((640,700))]+[view.local_to_world(p) for p in route]
                self.session={'person':actor.ident,'pet':pet.pet_id,'origin':origin,'points':points,'index':0,
                              'phase':'out','revision':game.construction.grid.road_revision}
                actor.activity='前往照顾宠物'
                return

    def update(self,view,actor,person,game,pets,dt):
        s=self.session
        if not s or s['person']!=actor.ident:return False
        if s['phase'] != 'back' and (person.get('status')=='离镇' or s['revision']!=game.construction.grid.road_revision):
            s['phase']='back';s['points']=list(reversed(s['points'][:s['index']]))+[s['origin']];s['index']=0
        if s['phase']=='feed':
            s['time']-=dt
            if s['time']>0:return True
            s['phase']='back';s['points']=list(reversed(s['points']))+[s['origin']];s['index']=0
            actor.activity='返回道路'
        if s['index'] < len(s['points']):
            target=s['points'][s['index']];dx=target[0]-actor.x;dy=target[1]-actor.y;distance=math.hypot(dx,dy)
            step=min(distance,42*dt)
            if distance:actor.x+=dx/distance*step;actor.y+=dy/distance*step
            actor.anim+=dt;actor.points=[target];actor.index=0;actor.path=[target]
            if step>=distance-.01:s['index']+=1
            return True
        if s['phase']=='out':
            pet_actor=next((p for p in pets if p.pet_id==s['pet']),None);pet=game.pets.get(s['pet'])
            if pet_actor and pet and pet.active and not pet.fostered and pet.hunger<45 and game.meta.get('caretaker') and game.inventory.count('普通饲料'):
                pos=view.local_to_world((pet_actor.x,pet_actor.y))
                if math.hypot(actor.x-pos[0],actor.y-pos[1])<=60:
                    try:game.perform('feed',real_now=game.meta['real_anchor'],pet_id=pet.pet_id,item='普通饲料')
                    except (GameError,SaveError):pass
                    else:
                        pet_actor.react('eat',2.5);pet_actor.sync_from_pet(game.pets[pet.pet_id],game.now)
                        actor.activity='正在喂宠物';actor.path=[];s['phase']='feed';s['time']=2.5;self.cooldown=30
                        return True
            s['phase']='back';s['points']=list(reversed(s['points']))+[s['origin']];s['index']=0
            actor.activity='返回道路'
            return True
        actor.x,actor.y=s['origin'];actor.path=[];actor.revision=-1;self.session=None
        return True
