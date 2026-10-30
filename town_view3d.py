"""三维世界适配器，沿用经营面板、寻路与存档服务。"""
import math
import pygame
import art
import ui
import courtyard
import game_config as C
from camera3d import Camera3D
from render3d import Renderer3D
from town_view import TownView,SanitationAdapter
from world3d import World3D
from weather import day_phase


class PickRegion(pygame.Rect):
    """可见轮廓命中，避免用包围盒点中被遮挡的管理员或标牌。"""
    def __init__(self,owner,kind):
        super().__init__(owner.hit_bounds.get((kind,True),pygame.Rect(0,0,0,0)))
        self.owner,self.kind=owner,kind

    def collidepoint(self,*pos):
        point=pos[0] if len(pos)==1 else pos
        return self.owner.picked(point)==(self.kind,True)


class Sanitation3D(SanitationAdapter):
    @property
    def sign_rect(self):return PickRegion(self.owner,'sanitation')

    @property
    def manager_rect(self):return PickRegion(self.owner,'manager')

    @property
    def waste_hits(self):return [(r,key[1]) for key,r in self.owner.hit_bounds.items() if key[0]=='waste']

    def waste_at(self,pos):return self.owner.pick_kind(pos,'waste')


class TownView3D(TownView):
    is_3d=True

    def __init__(self,renderer,rng,fonts,software=False):
        super().__init__(renderer,rng,fonts)
        self.camera=Camera3D(C.WORLD_SIZE)
        self.camera.zoom=1.2
        self.engine=Renderer3D(software);self.world=World3D();self.hit_bounds={};self.label_hits=[]
        self.sanitation=Sanitation3D(self,self.farm.sanitation)

    def close(self):self.engine.close()

    def home(self):
        self.camera.zoom=1.2
        x,y,w,h=C.FARM_WORLD_RECT;self.camera.center((x+w/2,y+h/2))

    def ground_polygon(self,rect,z=.7):
        r=pygame.Rect(rect)
        return [self.camera.world_to_screen((x,y,z)) for x,y in (r.topleft,r.topright,r.bottomright,r.bottomleft)]

    def project_rect(self,rect):
        points=self.ground_polygon(rect);xs=[p[0] for p in points];ys=[p[1] for p in points]
        return pygame.Rect(math.floor(min(xs)),math.floor(min(ys)),max(1,math.ceil(max(xs)-min(xs))),max(1,math.ceil(max(ys)-min(ys))))

    def picked(self,pos):
        for rect,key in self.label_hits:
            if rect.collidepoint(pos):return key
        return self.engine.pick(pos,self.camera)

    def pick_kind(self,pos,kind):
        picked=self.picked(pos)
        return picked[1] if picked and picked[0]==kind else None

    @property
    def building_hits(self):return {k[1]:r for k,r in self.hit_bounds.items() if k[0]=='building'}

    def building_at(self,pos):return self.pick_kind(pos,'building')
    def prop_at(self,pos):return self.pick_kind(pos,'prop')
    def person_at(self,pos):return self.pick_kind(pos,'person')
    def town_building_at(self,pos):return self.pick_kind(pos,'town_building')
    def vehicle_at(self,pos):return self.pick_kind(pos,'vehicle')
    def decoration_at(self,pos):return self.pick_kind(pos,'decoration')

    def pet_at(self,pos,actors):
        ident=self.pick_kind(pos,'pet')
        return next((actor for actor in actors if actor.pet_id==ident),None)

    def zone_at(self,pos):return self.pick_kind(pos,'zone') or super().zone_at(pos)

    def object_bounds(self,mesh,position,angle,scale):
        if mesh.bounds is None:return pygame.Rect(0,0,0,0)
        low,high=mesh.bounds;c,s=math.cos(angle),math.sin(angle);points=[]
        for x in (low[0],high[0]):
            for y in (low[1],high[1]):
                for z in (low[2],high[2]):
                    p=self.camera.project((position[0]+(x*c-y*s)*scale,position[1]+(x*s+y*c)*scale,position[2]+z*scale))
                    if p:points.append(p)
        if not points:return pygame.Rect(0,0,0,0)
        x,y=min(p[0] for p in points),min(p[1] for p in points)
        return pygame.Rect(x,y,max(p[0] for p in points)-x+1,max(p[1] for p in points)-y+1)

    def wire_preview(self,surface,rect,height,color,offset=(0,0)):
        r=pygame.Rect(rect);base=self.ground_polygon(r,2);top=self.ground_polygon(r,height)
        base=[(x-offset[0],y-offset[1]) for x,y in base];top=[(x-offset[0],y-offset[1]) for x,y in top]
        pygame.draw.polygon(surface,(*color[:3],60),top)
        pygame.draw.lines(surface,color,True,base,2);pygame.draw.lines(surface,color,True,top,2)
        for a,b in zip(base,top):pygame.draw.line(surface,color,a,b,2)

    def _mini(self,s,g):
        # 旧导览图仍是平面总览，视野框改为真实相机与地面的四个交点。
        camera=self.camera
        super()._mini(s,g)
        inner=self.mini_inner
        corners=[camera.screen_to_world(p) for p in (self.viewport.topleft,self.viewport.topright,self.viewport.bottomright,self.viewport.bottomleft)]
        points=[(inner.x+max(0,min(C.WORLD_SIZE[0],p[0]))/C.WORLD_SIZE[0]*inner.w,inner.y+max(0,min(C.WORLD_SIZE[1],p[1]))/C.WORLD_SIZE[1]*inner.h) for p in corners]
        pygame.draw.lines(s,(218,165,77),True,points,2)

    def _hover_tip(self,screen,g):
        pos=ui.pointer()
        if not self.viewport.collidepoint(pos) or any(r.collidepoint(pos) for r in (self.legend_rect,self.home_button,self.overview_rect)):return
        key=self.picked(pos);text=''
        if key:
            kind,ident=key
            if kind=='building':
                b=g.buildings[ident];text=f'{b.kind} · Lv{b.level} · 点击管理' if b.kind else f'{ident+1}号地块 · '+('点击选择建筑' if b.unlocked else '点击解锁')
            elif kind=='sanitation':text=f'农场卫生 · 地面粪便{g.sanitation.quantity()}份 · 点击管理'
            elif kind=='manager':text='农场管理员'+C.MANAGER_NAME+' · '+g.sanitation.status()
            elif kind=='waste':text='地面粪便 · 点击清理或出售'
            elif kind=='prop':text=courtyard.PROP_LABELS[ident]
            elif kind=='person':
                person=next((p for p in g.people if p['person_id']==ident),None)
                actor=self.people.get(ident)
                if person:text=person.get('name','小镇'+person['role'])+' · '+(actor.activity if actor else '点击查看')
            elif kind=='decoration':
                from decorations import CATALOG
                row=next((d for d in g.meta.get('decorations',[]) if d['id']==ident),None)
                if row:text=CATALOG[row['kind']][0]+' · Ctrl+D 装饰模式可移动'
            elif kind=='pet':
                actor=next((a for a in self._actors if a.pet_id==ident),None)
                if actor:text=actor.name+' · '+{'eat':'正在吃东西','pet':'开心互动','bath':'正在洗澡','sleep':'正在睡觉'}.get(actor.state,actor.behavior_note)
        if not text:return super()._hover_tip(screen,g)
        width=min(450,self.fonts.get(13).size(text)[0]+24)
        rect=pygame.Rect(pos[0]+16,pos[1]+18,width,33);rect.clamp_ip(self.viewport.inflate(-12,-12))
        art.clay_panel(screen,rect,ui.PANEL,8,ui.LINE,shadow_alpha=12)
        self.fonts.text(screen,text,(rect.x+12,rect.y+7),13,ui.TEXT)

    def draw(self,screen,g,actors,focus_id=None,drag_item=None,feed_count=0):
        self.light=day_phase(g.now);self.last_game=g;self._actors=actors
        if not self.people or self.traffic.revision!=g.meta['road_revision']:self.tick(0,g)
        objects=self.world.objects(g,self,actors)
        screen.blit(self.engine.draw(self.camera,objects,self.light=='night',self.time),self.viewport.topleft)
        self.label_hits=[];self.hit_bounds={}
        for mesh,pos,angle,scale,key in objects:
            if key is not None:self.hit_bounds[key]=self.object_bounds(mesh,pos,angle,scale)
        self.town_building_hits=[(r,k[1]) for k,r in self.hit_bounds.items() if k[0]=='town_building']
        self.vehicle_hits=[(r,k[1]) for k,r in self.hit_bounds.items() if k[0]=='vehicle']
        self.person_hits=[(r,k[1]) for k,r in self.hit_bounds.items() if k[0]=='person']
        pointer=ui.pointer();hover=self.pick_kind(pointer,'pet');clip=screen.get_clip();screen.set_clip(self.viewport)
        for actor in actors:
            actor.selected=actor.pet_id==focus_id;actor.hovered=actor.pet_id==hover
            rect=self.hit_bounds.get(('pet',actor.pet_id),pygame.Rect(0,0,0,0))
            actor.drawn_width,actor.drawn_height=rect.size
            if not rect.colliderect(self.viewport):continue
            if actor.selected or actor.hovered:
                wx,wy=self.local_to_world((actor.x,actor.y))
                ring=[self.camera.world_to_screen((wx+math.cos(i*math.tau/32)*30,wy+math.sin(i*math.tau/32)*30,1)) for i in range(32)]
                pygame.draw.lines(screen,(246,214,134),True,ring,2)
                text=f'{actor.name} · {actor.stage}'
                label=pygame.Rect(0,0,self.fonts.get(14).size(text)[0]+20,27);label.midbottom=(rect.centerx,rect.top-5)
                label.clamp_ip(self.viewport.inflate(-12,-12))
                art.clay_panel(screen,label,ui.PANEL,8,ui.GREEN,shadow_alpha=14)
                self.fonts.text(screen,text,(label.x+10,label.y+4),14,ui.GREEN)
                self.label_hits.append((label,('pet',actor.pet_id)))
        # 天气保留屏幕空间粒子；时段明暗由三维光照负责，避免叠两次灰色遮罩。
        if g.weather.current not in ('晴天','阴天'):
            from scene_style import weather_layer
            overlay=weather_layer(self.viewport.size,'day',g.weather.current,self.time)
            if overlay is not None:screen.blit(overlay,self.viewport.topleft)
        screen.set_clip(clip)
        art.clay_panel(screen,self.home_button,ui.PANEL,10,ui.LINE,shadow_alpha=20)
        self.fonts.text(screen,'返回庄园  /  Home',(self.home_button.x+19,self.home_button.y+9),15,ui.GREEN)
        self.legend_rect.w=414
        art.clay_panel(screen,self.legend_rect,ui.PANEL,12,ui.LINE,shadow_alpha=20)
        self.fonts.text(screen,f'立体小镇 · {g.weather.current} · {len(g.people)}人在镇上',(40,110),17,ui.GREEN)
        self.fonts.text(screen,'WASD/左键移图 · 滚轮缩放 · 右键旋转 · F9二维',(40,137),12,ui.MUTED)
        self._mini(screen,g);self._hover_tip(screen,g)
        if self.vehicle_card:
            import town_catalog as K
            v=next((v for v in self.traffic.vehicles if v.vehicle_id==self.vehicle_card),None)
            if v:
                r=pygame.Rect(24,165,340,132);art.clay_panel(screen,r,ui.PANEL,12,ui.LINE)
                for i,line in enumerate((K.VEHICLE_NAMES[v.kind]+' · '+v.state,'起点：'+v.origin_name,'终点：'+v.destination_name,'点击卡片收起')):
                    self.fonts.text(screen,line,(r.x+14,r.y+12+i*28),16 if i==0 else 14,ui.GREEN if i==0 else ui.MUTED)
            else:self.vehicle_card=None
