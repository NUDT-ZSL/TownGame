"""可拖拽的小镇世界。庄园保留原坐标逻辑，摄像机统一转换显示和命中坐标。"""
import math
import pygame
import art
import ui
from render_canvas import RenderCanvas, recorded_region
import game_config as C
import courtyard
from camera import Camera
from weather import day_phase
import town_art
from town_grid import world_to_cell, cell_center, cell_rect, PORTALS, LANDMARKS
import town_catalog as K
from traffic import TrafficSystem,RoadWalker
from tree_art import tree_sprite,ground_sprite
from landscape import world_trees,estate_trees,ground_details
from scene_style import weather_layer

# 保留旧的视图实体名，实际寻路全部由同一RoadGrid派生。
PersonActor=RoadWalker

VIEWPORT = courtyard.SCENE
FX,FY,FW,FH = C.FARM_WORLD_RECT


class EstateView(courtyard.CourtyardView):
    """把原庭院换成与小镇一致的俯视花园，复用所有宠物/管理员与交互。"""
    def _build(self, season, light):
        colors = courtyard.SEASONS[season]
        s = pygame.Surface(courtyard.SCENE.size)
        s.fill(colors["grass"])
        pygame.draw.rect(s,art.lighten(colors["grass"],.08),(14,12,1252,590),border_radius=35)
        path = art.mix(colors["path"],(248,242,222),.4)
        pygame.draw.rect(s,path,(605,25,70,590),border_radius=20)
        for row in range(4):
            pygame.draw.rect(s,path,(65,158+row*88,1150,24),border_radius=10)
        for bid in range(C.GRID_COLS*C.GRID_ROWS):
            col,row = bid%C.GRID_COLS,bid//C.GRID_COLS
            rect = pygame.Rect(67+col*230,58+row*88,170,80)
            fill=art.lighten(colors['grass'],.025+(bid%3)*.006)
            pygame.draw.rect(s,fill,rect,border_radius=16)
            pygame.draw.rect(s,art.mix(colors['grass'],(99,137,87),.12),rect,1,border_radius=16)
            for corner in (rect.bottomleft,rect.bottomright):
                cx,cy = corner[0]+(12 if corner == rect.bottomleft else -12),corner[1]-11
                pygame.draw.ellipse(s,(119,157,105),(cx-10,cy-4,20,10))
                for i in range(1+bid%3):
                    pygame.draw.circle(s,((238,196,165),(239,217,157),(221,187,191))[bid%3],(cx-6+i*6,cy-5-(i%2)*3),2)
        for x in range(28,1260,42):
            pygame.draw.circle(s,(221,181,126),(x,602),3)
            pygame.draw.line(s,(194,163,117),(x,586),(x,606),3)
        pygame.draw.line(s,(221,181,126),(28,596),(1255,596),3)
        for tree in estate_trees():
            image=tree_sprite(tree.species,tree.variant,season,light,tree.height)
            s.blit(image,image.get_rect(midbottom=(tree.x,tree.y)))
        for x,y,color in courtyard.FLOWERS:
            pygame.draw.circle(s,color,(x,y-88),3)
        self.fonts.text(s,"中央庄园  /  PET ESTATE",(28,10),17,(68,103,65))
        return s

    def _layout_buildings(self, game):
        self.building_hits = {bid:pygame.Rect(90+(bid%C.GRID_COLS)*230,141+(bid//C.GRID_COLS)*88,128,92)
                              for bid,b in game.buildings.items() if b.kind and b.unlocked}
        return self.building_hits


class SanitationAdapter:
    def __init__(self, owner, source):
        self.owner,self.source = owner,source

    def __getattr__(self,key):
        return getattr(self.source,key)

    @property
    def sign_rect(self):
        return self.owner.local_rect(self.source.sign_rect)

    @property
    def waste_hits(self):
        return [(self.owner.local_rect(rect),ident) for rect,ident in self.source.waste_hits]

    @property
    def manager_rect(self):
        return self.owner.local_rect(self.source.manager_rect) if self.source.manager_rect else None

    def waste_at(self,pos):
        return self.source.waste_at(self.owner.screen_to_local(pos))

    def manager_pose(self, game):
        pose = self.source.manager_pose(game)
        if not pose:
            return None
        x,y = self.owner.local_to_screen(pose[:2])
        return x,y,pose[2]


class TownView:
    def __init__(self, renderer, rng, fonts):
        self.renderer,self.rng,self.fonts = renderer,rng,fonts
        self.farm = EstateView(renderer,rng,fonts)
        self.sanitation = SanitationAdapter(self,self.farm.sanitation)
        self.camera = Camera(C.WORLD_SIZE,position=(FX,FY))
        self.viewport=VIEWPORT.copy()
        self.backgrounds = art.LRU(2)
        self.farm_surface = RenderCanvas(C.WINDOW)
        self.people = {}
        self.person_hits = []
        self.zone_hits = []
        self.time = 0
        self.light = "day"
        self.home_button = pygame.Rect(1048,102,208,38)
        self.overview_rect = pygame.Rect(1048,510,208,174)
        self.mini_inner = self.overview_rect.inflate(-16,-34).move(0,9)
        self.legend_rect = pygame.Rect(24,101,380,54)
        self.last_game = None
        self.road_layer=town_art.RoadLayer()
        self.town_building_hits=[]
        self.decoration_hits=[]
        self.static_builds=0
        self.traffic=TrafficSystem()
        self.vehicle_hits=[]
        self.vehicle_card=None
        self.scaled_sprites=art.LRU(512)
        self.static_frames=art.LRU(3)
        self.minimap_frames=art.LRU(4)

    def home(self):
        self.camera.zoom = 1
        self.camera.center((FX+FW/2,FY+FH/2))

    def layout(self,size):
        center=self.camera.screen_to_world((self.viewport.centerx,self.viewport.centery))
        self.viewport=pygame.Rect(0,88,size[0],size[1]-244)
        self.camera.width,self.camera.height=self.viewport.size
        self.camera.center(center)
        self.home_button.x=size[0]-232
        self.overview_rect.topleft=(size[0]-232,size[1]-350)
        self.mini_inner=self.overview_rect.inflate(-16,-34).move(0,9)
        self.static_frames.clear()

    def focus_zone(self, zone_id):
        x,y,w,h = C.TOWN_ZONES[zone_id]["rect"]
        self.camera.center((x+w/2,y+h/2))

    def project_rect(self, rect):
        rect = pygame.Rect(rect)
        x,y = self.camera.world_to_screen(rect.topleft)
        return pygame.Rect(round(x),round(y),max(1,round(rect.w*self.camera.zoom)),max(1,round(rect.h*self.camera.zoom)))

    def local_to_world(self,pos):
        return pos[0]+FX,pos[1]+FY-courtyard.SCENE.y

    def local_to_screen(self,pos):
        return self.camera.world_to_screen(self.local_to_world(pos))

    def screen_to_local(self,pos):
        wx,wy = self.camera.screen_to_world(pos)
        return wx-FX,wy-FY+courtyard.SCENE.y

    def local_rect(self,rect):
        return self.project_rect(pygame.Rect(rect).move(FX,FY-courtyard.SCENE.y))

    @property
    def building_hits(self):
        return {bid:self.local_rect(rect) for bid,rect in self.farm.building_hits.items()}

    def building_at(self,pos):
        if not self.viewport.collidepoint(pos):
            return None
        return self.farm.building_at(self.screen_to_local(pos))

    def prop_at(self,pos):
        return self.farm.prop_at(self.screen_to_local(pos)) if self.viewport.collidepoint(pos) else None

    def pet_at(self,pos,actors):
        return self.farm.pet_at(self.screen_to_local(pos),actors) if self.viewport.collidepoint(pos) else None

    def zone_at(self,pos):
        wp = self.camera.screen_to_world(pos)
        return next((key for key,spec in C.TOWN_ZONES.items() if pygame.Rect(spec["rect"]).collidepoint(wp)),None)

    def person_at(self,pos):
        return next((ident for rect,ident in reversed(self.person_hits) if rect.collidepoint(pos)),None)

    def town_building_at(self,pos):
        return next((ident for rect,ident in reversed(self.town_building_hits) if rect.collidepoint(pos)),None) if self.viewport.collidepoint(pos) else None

    def vehicle_at(self,pos):
        return next((ident for rect,ident in reversed(self.vehicle_hits) if rect.collidepoint(pos)),None) if self.viewport.collidepoint(pos) else None

    def decoration_at(self,pos):
        if not self.viewport.collidepoint(pos):return None
        for rect,ident,image in reversed(self.decoration_hits):
            if not rect.collidepoint(pos):continue
            x=min(image.get_width()-1,int((pos[0]-rect.x)*image.get_width()/rect.w))
            y=min(image.get_height()-1,int((pos[1]-rect.y)*image.get_height()/rect.h))
            if image.get_at((x,y)).a>30:return ident
        return None

    def fixed_click(self,pos):
        if self.home_button.collidepoint(pos):
            self.home()
            return True
        if self.overview_rect.collidepoint(pos):
            rx = max(0,min(1,(pos[0]-self.mini_inner.x)/self.mini_inner.w))
            ry = max(0,min(1,(pos[1]-self.mini_inner.y)/self.mini_inner.h))
            self.camera.center((rx*C.WORLD_SIZE[0],ry*C.WORLD_SIZE[1]))
            return True
        return False

    def hoverable(self,pos,actors):
        return self.viewport.collidepoint(pos) and bool(self.home_button.collidepoint(pos) or self.overview_rect.collidepoint(pos) or self.pet_at(pos,actors) or self.prop_at(pos) or self.building_at(pos) is not None or self.sanitation.waste_at(pos) or self.zone_at(pos) or self.person_at(pos) or self.vehicle_at(pos) or self.town_building_at(pos))

    def scaled(self,sprite):
        zoom=self.camera.zoom
        if zoom==1: return sprite
        return self.scaled_sprites.get((sprite,zoom),lambda:pygame.transform.smoothscale(sprite,(max(1,round(sprite.get_width()*zoom)),max(1,round(sprite.get_height()*zoom)))))

    def static_frame(self,g,area):
        key=(area.x,area.y,self.camera.zoom,self.viewport.size,g.season,self.light,g.weather.current,g.meta['road_revision'],tuple(z['level'] for z in g.town_zones))
        def build():
            layer=pygame.Surface(area.size)
            layer.blit(self._background(g),(-area.x,-area.y))
            self.road_layer.draw(layer,g.construction.grid,area,g.season,self.light,g.weather.current)
            return pygame.transform.smoothscale(layer,self.viewport.size)
        return self.static_frames.get(key,build)

    def tick(self,dt,game=None):
        self.time += dt
        self.farm.tick(dt)
        if game is not None:
            self.traffic.update(dt,game)
            alive = {p["person_id"] for p in game.people}
            self.people = {k:a for k,a in self.people.items() if k in alive}
            for p in game.people:
                ident = p["person_id"]
                if ident not in self.people:
                    cell=PORTALS[p['home_zone']] if p['role']=='居民' else PORTALS['station']
                    x,y=cell_center(*cell)
                    self.people[ident]=RoadWalker(ident,x,y)
                self.people[ident].update(dt,p,game.construction.grid)

    def spawn_hearts(self,*args):
        self.farm.spawn_hearts(*args)

    def spawn_bath(self,*args):
        self.farm.spawn_bath(*args)

    def spawn_food(self,*args):
        self.farm.spawn_food(*args)

    def _background(self,game):
        key = (game.season,self.light,game.weather.current,tuple((z["zone_id"],z["level"]) for z in game.town_zones))
        return self.backgrounds.get(key,lambda:self._build_world(game))

    def _build_world(self,g):
        self.static_builds+=1
        s = pygame.Surface(C.WORLD_SIZE)
        season = courtyard.SEASONS[g.season]
        grass = art.mix(season["grass"],(212,224,174),.25)
        s.fill(grass)
        self._lawn_texture(s,pygame.Rect(0,0,*C.WORLD_SIZE),grass,1100)
        # 河谷、花木和柔和的地形色带。
        for i in range(100):
            x,y = (i*337+97)%3600,(i*193+181)%2400
            pygame.draw.ellipse(s,art.lighten(grass,.035),(x,y,180,90))
        for key,spec in C.TOWN_ZONES.items():
            if key == "estate":
                continue
            r = pygame.Rect(spec["rect"])
            level = g.town.level(key)
            fill = art.mix(grass,spec['color'],.19 if level else .09)
            art.draw_shadow(s,(r.centerx,r.bottom+3),(r.w-25,20),15,9)
            pygame.draw.rect(s,fill,r,border_radius=46)
            pygame.draw.rect(s,art.mix(fill,(245,237,209),.48),r,3,border_radius=46)
            self._lawn_texture(s,r.inflate(-24,-24),fill,150)
            self._zone_art(s,key,r,level,g)
            tag = pygame.Rect(r.x+28,r.y+20,r.w-56,62)
            art.clay_panel(s,tag,(255,251,239),16,(200,189,155),shadow_alpha=18)
            self.fonts.text(s,spec["name"],(tag.x+22,tag.y+12),27,ui.GREEN)
            self.fonts.text(s,f"Lv{level}" if level else f"待开放 · {spec['cost']}金币",(tag.right-230,tag.y+22),17,ui.MUTED)
        return s

    @staticmethod
    def _lawn_texture(surface,rect,color,count):
        for i in range(count):
            x=rect.x+(i*337+97)%max(1,rect.w);y=rect.y+(i*193+181)%max(1,rect.h)
            shade=art.mix(color,(97,132,82),.075) if i%3 else art.lighten(color,.04)
            pygame.draw.line(surface,shade,(x,y),(x+3+i%4,y-2),1)
            if i%4==0:pygame.draw.line(surface,shade,(x+5,y),(x+7,y-3),1)

    def _zone_art(self,s,key,r,level,g):
        night = self.light == "night"
        if key in ("homes","market"):
            landmark=pygame.Rect(LANDMARKS[0 if key=='homes' else 1])
            art.draw_cottage(s,landmark,'商店' if key=='market' else '饲养小屋',max(1,level),
                             (187,163,143),night=night,winter=g.season=='冬')
        elif key == "station":
            art.draw_cottage(s,pygame.Rect(r.x+130,r.y+100,270,210),"商店",max(1,level),(157,172,181),night=night)
            pygame.draw.rect(s,(226,214,184),(r.x+450,r.y+145,480,185),border_radius=16)
            trackbed=pygame.Rect(r.x+35,r.y+327,r.w-70,78)
            pygame.draw.rect(s,(188,183,158),trackbed,border_radius=12)
            pygame.draw.line(s,(211,204,176),trackbed.topleft,trackbed.topright,2)
            for yy in (r.y+350,r.y+385):
                pygame.draw.line(s,(133,143,127),(r.x+40,yy),(r.right-40,yy),3)
            for x in range(r.x+50,r.right-50,32):
                pygame.draw.line(s,(158,143,113),(x,r.y+340),(x,r.y+397),5)
            for i in range(3):
                coach = pygame.Rect(r.x+460+i*145,r.y+250,135,95)
                art.clay_panel(s,coach,(101,155,143),13,(86,132,123),shadow_alpha=0,
                               top=(136,179,161),bottom=(77,125,119))
                pygame.draw.line(s,(185,206,180),(coach.x+12,coach.y+7),(coach.right-12,coach.y+7),2)
                pygame.draw.line(s,(203,181,120),(coach.x+5,coach.y+66),(coach.right-5,coach.y+66),3)
                for dx in (12,48,84):
                    pygame.draw.rect(s,(224,238,221),(coach.x+dx,coach.y+18,25,31),border_radius=5)
                    pygame.draw.line(s,(245,248,233),(coach.x+dx+5,coach.y+22),(coach.x+dx+18,coach.y+22),2)
                for dx in (25,105):
                    pygame.draw.circle(s,(81,91,83),(coach.x+dx,coach.bottom),12)
            for x in (r.x+95,r.right-75):
                y=r.y+327
                pygame.draw.line(s,(101,119,101),(x,y-46),(x,y+10),4)
                if night:art.draw_glow(s,(x,y-46),48,(255,215,143),42)
                pygame.draw.ellipse(s,(244,221,158) if night else (194,194,155),(x-10,y-50,20,8))
        elif key == "park":
            pond = pygame.Rect(r.x+100,r.y+180,r.w-200,r.h-320)
            pygame.draw.ellipse(s,(181,204,165),pond.inflate(28,28))
            pygame.draw.ellipse(s,(121,183,189),pond)
            pygame.draw.ellipse(s,(159,208,207),pond.inflate(-45,-55))
            for i in range(20):
                x = pond.x+90+(i*107)%(pond.w-180)
                y = pond.y+80+(i*71)%(pond.h-160)
                pygame.draw.line(s,(207,232,219),(x,y),(x+25,y),3)
            for i in range(8):
                x = pond.x+90+(i*137)%(pond.w-180)
                y = pond.y+150+(i*113)%(pond.h-240)
                pygame.draw.ellipse(s,(109,163,127),(x,y,38,21))
                pygame.draw.circle(s,(241,191,192),(x+18,y+7),6)
                pygame.draw.circle(s,(250,226,163),(x+18,y+7),2)
            for x,y in ((pond.x+180,pond.y+270),(pond.right-150,pond.y+390)):
                pygame.draw.ellipse(s,(104,161,165),(x-38,y+10,93,25))
                pygame.draw.polygon(s,(178,132,87),[(x-35,y),(x+40,y-8),(x+60,y+6),(x+22,y+24),(x-25,y+23)])
                pygame.draw.polygon(s,(224,185,121),[(x-23,y+4),(x+36,y-1),(x+43,y+8),(x+18,y+16),(x-16,y+16)])
                pygame.draw.line(s,(141,103,68),(x-40,y+29),(x+29,y-22),4)
            for i in range(4):
                x,y = pond.x+130+i*115,pond.y+180+(i%2)*70
                pygame.draw.ellipse(s,(252,245,213),(x,y,23,13))
                pygame.draw.circle(s,(252,245,213),(x+19,y-1),7)
                pygame.draw.polygon(s,(224,158,75),[(x+24,y-2),(x+33,y),(x+24,y+3)])
            pygame.draw.rect(s,(216,181,126),(r.x+35,r.y+340,r.w-70,65),border_radius=8)
            for x in range(r.x+45,r.right-35,30):
                pygame.draw.line(s,(177,143,100),(x,r.y+343),(x,r.y+401),1)
        else:
            art.draw_cottage(s,pygame.Rect(LANDMARKS[3]),"仓库",max(1,level),(184,153,113),night=night)
        if not level:
            shade = pygame.Surface(r.size,pygame.SRCALPHA)
            shade.fill((204,214,187,18))
            s.blit(shade,r.topleft)

    def _person_sprite(self, actor, person, wet):
        frame=int(actor.anim*7+int(actor.ident[:2],16))%4 if actor.path else -1
        facing = getattr(actor,'facing',1)
        if actor.index<len(actor.points):
            dx = actor.points[actor.index][0]-actor.x
            if abs(dx)>.1:
                facing = -1 if dx<0 else 1
        actor.facing = facing
        return town_art.person_sprite(person['color_index'],person['role']=='游客',wet,frame,person['person_id'],facing)

    def _draw_people(self,layer,g,area,only=None):
        lookup = {p["person_id"]:p for p in g.people}
        for actor in ([only] if only else sorted(self.people.values(),key=lambda a:a.y)):
            if not actor.visible: continue
            person = lookup.get(actor.ident)
            if person is None:
                continue
            x,y = round(actor.x-area.x),round(actor.y-area.y)
            if not (-80 < x < area.w+80 and -100 < y < area.h+40):
                continue
            layer.blit(self._person_sprite(actor,person,g.weather.current in ('小雨','暴雨','雨夹雪')),(x-33,y-96))
            world_rect = pygame.Rect(actor.x-16,actor.y-67,34,73)
            self.person_hits.append((self.project_rect(world_rect),actor.ident))

    def _weather_overlay(self,screen,g):
        overlay=weather_layer(self.viewport.size,self.light,g.weather.current,self.time)
        if overlay is not None:screen.blit(overlay,self.viewport.topleft)

    def _mini(self,s,g):
        r=self.overview_rect
        key=(g.meta['road_revision'],tuple(z['level'] for z in g.town_zones),tuple(b['building_id'] for b in g.town_buildings))
        def build():
            image=pygame.Surface(r.size,pygame.SRCALPHA)
            art.clay_panel(image,image.get_rect(),(255,251,239),12,ui.LINE,shadow_alpha=0)
            self.fonts.text(image,'小镇导览 · 点击定位',(12,8),13,ui.GREEN)
            inside=self.mini_inner.move(-r.x,-r.y)
            pygame.draw.rect(image,(192,211,167),inside,border_radius=6)
            for zone,spec in C.TOWN_ZONES.items():
                wx,wy,w,h=spec['rect']
                rect=pygame.Rect(inside.x+wx/3600*inside.w,inside.y+wy/2400*inside.h,max(2,w/3600*inside.w),max(2,h/2400*inside.h))
                pygame.draw.rect(image,spec['color'] if g.town.level(zone) else (205,205,179),rect,border_radius=3)
            for road in g.roads:
                wx,wy=cell_center(road['cx'],road['cy'])
                pygame.draw.rect(image,(113,126,116),(inside.x+wx/3600*inside.w,inside.y+wy/2400*inside.h,2,2))
            for b in g.town_buildings:
                pygame.draw.rect(image,(175,123,89),(inside.x+b['cx']*48/3600*inside.w,inside.y+b['cy']*48/2400*inside.h,3,3))
            return image
        s.blit(self.minimap_frames.get(key,build),r)
        inside=self.mini_inner
        frame=pygame.Rect(inside.x+self.camera.x/3600*inside.w,inside.y+self.camera.y/2400*inside.h,self.viewport.w/self.camera.zoom/3600*inside.w,self.viewport.h/self.camera.zoom/2400*inside.h)
        pygame.draw.rect(s,ui.GREEN,frame,2,border_radius=3)

    def _hover_tip(self,screen,g):
        pos=ui.pointer()
        if not self.viewport.collidepoint(pos) or any(r.collidepoint(pos) for r in (self.legend_rect,self.home_button,self.overview_rect)):
            return
        text=''; bid=self.town_building_at(pos); vid=self.vehicle_at(pos)
        if vid:
            vehicle=next((v for v in self.traffic.vehicles if v.vehicle_id==vid),None)
            if vehicle:
                text=K.VEHICLE_NAMES[vehicle.kind]+' / '+vehicle.state+' · 点击查看行程'
                rect=next(r for r,i in self.vehicle_hits if i==vid)
                pygame.draw.rect(screen,(232,194,106),rect.inflate(4,4),2,border_radius=8)
        elif bid:
            building=next(b for b in g.town_buildings if b['building_id']==bid); spec=K.CATALOG[building['catalog_key']]
            text=spec['name']+' / '+K.FAMILIES[spec['family']]+' / '+('施工中' if building['status']=='building' else '已完工')
            if not g.construction.building_connected(building): text+=' / 道路中断'
        else:
            cell=world_to_cell(*self.camera.screen_to_world(pos)); road=g.construction.grid.roads.get(cell)
            if road: text=K.ROAD_STYLES[road['road_style']]['name']+' / '+('道路通达' if g.construction.grid.is_connected(PORTALS['estate'],cell) else '道路未连通')
        if text:
            width=min(440,self.fonts.get(13).size(text)[0]+24)
            rect=pygame.Rect(min(self.viewport.w-width-8,pos[0]+15),min(self.viewport.bottom-46,pos[1]+18),width,35)
            art.clay_panel(screen,rect,ui.PANEL,8,ui.LINE,shadow_alpha=15)
            self.fonts.text(screen,text,(rect.x+12,rect.y+8),13,ui.TEXT)

    def draw(self,screen,g,actors,focus_id=None,drag_item=None,feed_count=0):
        self.light=day_phase(g.now)
        if not self.people or self.traffic.revision!=g.meta['road_revision']:
            self.tick(0,g)
        self.last_game=g
        area=pygame.Rect(int(self.camera.x),int(self.camera.y),math.ceil(self.viewport.w/self.camera.zoom),math.ceil(self.viewport.h/self.camera.zoom))
        screen.blit(self.static_frame(g,area),self.viewport.topleft)
        saved_clip=screen.get_clip(); screen.set_clip(self.viewport)
        self.town_building_hits=[]; self.person_hits=[]; self.vehicle_hits=[];self.decoration_hits=[]
        drawables=[]; lookup={p['person_id']:p for p in g.people}
        wet=g.weather.current in ('小雨','暴雨','雨夹雪')
        if pygame.Rect(C.FARM_WORLD_RECT).colliderect(area):
            self.farm_surface.begin(getattr(screen,'recording',False) or self.camera.zoom!=1)
            self.farm.draw(self.farm_surface,g,actors,focus_id,drag_item,feed_count,pointer=self.screen_to_local(ui.pointer()))
            farm_image=self.farm_surface.subsurface(courtyard.SCENE)
            image=pygame.transform.smoothscale(farm_image,(round(FW*self.camera.zoom),round(FH*self.camera.zoom))) if self.camera.zoom!=1 else farm_image
            if self.farm_surface.recording:
                image = recorded_region(self.farm_surface,courtyard.SCENE,image)
                if not getattr(screen,'recording',False):image=image.native(1)
            drawables.append((FY+FH,'farm',None,(image,self.project_rect(C.FARM_WORLD_RECT))))
        for actor in self.people.values():
            person=lookup.get(actor.ident)
            rect=pygame.Rect(actor.x-33,actor.y-96,72,106)
            if not actor.visible or not person or not rect.colliderect(area): continue
            sprite=self._person_sprite(actor,person,wet)
            drawables.append((actor.y,'person',actor,(self.scaled(sprite),self.project_rect(rect))))
        for building in g.town_buildings:
            b=building
            stage=4 if b['status']=='ready' else min(3,int(4*(g.now-b['placed_ts'])/(b['finish_ts']-b['placed_ts'])))
            sprite=town_art.building_sprite(b['catalog_key'],b['orientation'],self.light,g.season,g.weather.current,stage)
            rect=sprite.get_rect(midbottom=((b['cx']+b['w']/2)*48,(b['cy']+b['h'])*48+8))
            if rect.colliderect(area): drawables.append(((b['cy']+b['h'])*48,'building',b,(self.scaled(sprite),self.project_rect(rect))))
        from decoration_ui import sprite as decoration_sprite
        for d in g.meta.get('decorations',[]):
            image=decoration_sprite(d['kind'],d['orientation'],g.season,self.light,d['id'])
            rect=image.get_rect(midbottom=((d['cx']+.5)*48,(d['cy']+1)*48))
            if rect.colliderect(area):drawables.append(((d['cy']+1)*48,'decoration',d,(self.scaled(image),self.project_rect(rect))))
        for vehicle in self.traffic.vehicles:
            v=vehicle; sprite=town_art.vehicle_sprite(v.kind,v.color,v.angle,self.light=='night',wet)
            rect=sprite.get_rect(center=(v.x,v.y))
            if rect.colliderect(area): drawables.append((v.y,'vehicle',v,(self.scaled(sprite),self.project_rect(rect))))
        cleared=set(g.meta['town_cleared']); occupied=g.construction.occupied()
        for x,y,kind,variant in ground_details():
            cell=world_to_cell(x,y)
            if f'{cell[0]},{cell[1]}' in cleared or cell in occupied or cell in g.construction.grid.roads:continue
            image=ground_sprite(kind,variant,g.season,self.light)
            rect=image.get_rect(midbottom=(x,y))
            if rect.colliderect(area):drawables.append((y,'ground',None,(self.scaled(image),self.project_rect(rect))))
        for tree in world_trees():
            x,y=tree.x,tree.y
            cell=world_to_cell(x,y)
            if f'{cell[0]},{cell[1]}' in cleared or cell in occupied or cell in g.construction.grid.roads: continue
            if not pygame.Rect(x-tree.height//2,y-tree.height,tree.height,tree.height).colliderect(area):continue
            image=tree_sprite(tree.species,tree.variant,g.season,self.light,tree.height)
            rect=image.get_rect(midbottom=(x,y))
            if rect.colliderect(area):
                drawables.append((y,'tree',None,(self.scaled(image),self.project_rect(rect))))
        pointer=ui.pointer()
        actor_bounds=[self.local_rect(a.hit_rect()) for a in actors]
        for _,kind,obj,payload in sorted(drawables,key=lambda row:row[0]):
            sprite,rect=payload
            is_tree=kind=='tree' or kind=='decoration' and obj['kind'].startswith('tree_')
            if is_tree and (rect.collidepoint(pointer) or any(rect.colliderect(bounds) for bounds in actor_bounds)):
                sprite=sprite.copy();sprite.set_alpha(155)
            screen.blit(sprite,rect)
            if kind=='person':
                self.person_hits.append((self.project_rect(pygame.Rect(obj.x-16,obj.y-72,34,78)),obj.ident))
            elif kind=='decoration':
                self.decoration_hits.append((rect,obj['id'],sprite))
            elif kind=='building':
                self.town_building_hits.append((rect,obj['building_id']))
                if not g.construction.building_connected(obj): self.fonts.text(screen,'道路中断',(rect.x+10,rect.bottom-16),13,ui.RED)
            elif kind=='vehicle': self.vehicle_hits.append((rect,obj.vehicle_id))
        screen.set_clip(saved_clip)
        old_clip = screen.get_clip()
        screen.set_clip(self.viewport)
        self._weather_overlay(screen,g)
        screen.set_clip(old_clip)
        art.clay_panel(screen,self.home_button,(255,251,239),10,ui.LINE,shadow_alpha=20)
        self.fonts.text(screen,"返回庄园  /  Home",(self.home_button.x+19,self.home_button.y+9),15,ui.GREEN)
        art.clay_panel(screen,self.legend_rect,(255,251,239),12,ui.LINE,shadow_alpha=20)
        self.fonts.text(screen,f"{g.weather.current}  ·  {len(g.people)}人在镇上",(40,110),18,ui.GREEN)
        self.fonts.text(screen,"拖拽地图 · 滚轮缩放 · 点击街区经营",(40,137),12,ui.MUTED)
        self._mini(screen,g)
        self._hover_tip(screen,g)
        if self.vehicle_card:
            vehicle=next((v for v in self.traffic.vehicles if v.vehicle_id==self.vehicle_card),None)
            if vehicle:
                r=pygame.Rect(24,165,340,142)
                art.clay_panel(screen,r,ui.PANEL,12,ui.LINE,shadow_alpha=25)
                self.fonts.text(screen,K.VEHICLE_NAMES[vehicle.kind]+' · '+vehicle.state,(38,177),19,ui.GREEN)
                self.fonts.text(screen,'起点：'+vehicle.origin_name,(38,211),15,ui.TEXT)
                self.fonts.text(screen,'终点：'+vehicle.destination_name,(38,237),15,ui.TEXT)
                self.fonts.text(screen,f'速度 {vehicle.speed:.0f} 世界像素/秒 · 点击卡片收起',(38,273),12,ui.MUTED)
            else: self.vehicle_card=None
