"""可拖拽的小镇世界。庄园保留原坐标逻辑，摄像机统一转换显示和命中坐标。"""
import math
import pygame
import art
import ui
import game_config as C
import courtyard
from camera import Camera
from weather import day_phase
import town_art
from town_grid import world_to_cell, cell_center, cell_rect, PORTALS, LANDMARKS
import town_catalog as K
from traffic import TrafficSystem,RoadWalker

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
            pygame.draw.rect(s,art.lighten(colors["grass"],.05),rect,border_radius=12)
            pygame.draw.rect(s,art.darken(colors["grass"],.10),rect,1,border_radius=12)
            for corner in (rect.bottomleft,rect.bottomright):
                cx,cy = corner[0]+(12 if corner == rect.bottomleft else -12),corner[1]-11
                pygame.draw.ellipse(s,(119,157,105),(cx-10,cy-4,20,10))
                for i in range(3):
                    pygame.draw.circle(s,(238,196,165),(cx-6+i*6,cy-5-(i%2)*3),2)
        for x in range(28,1260,42):
            pygame.draw.circle(s,(221,181,126),(x,602),3)
            pygame.draw.line(s,(194,163,117),(x,586),(x,606),3)
        pygame.draw.line(s,(221,181,126),(28,596),(1255,596),3)
        for x in (33,1245):
            for y in (75,190,305,420):
                self._tree(s,x,y,.36,season,colors,light)
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
        self.backgrounds = art.LRU(2)
        self.farm_surface = pygame.Surface(C.WINDOW)
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
        self.static_builds=0
        self.traffic=TrafficSystem()
        self.vehicle_hits=[]
        self.vehicle_card=None
        self.prop_sprites=art.LRU(8)
        self.scaled_sprites=art.LRU(512)
        self.static_frames=art.LRU(3)
        self.minimap_frames=art.LRU(4)

    def home(self):
        self.camera.zoom = 1
        self.camera.x,self.camera.y = FX,FY
        self.camera.clamp()

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
        if not VIEWPORT.collidepoint(pos):
            return None
        return self.farm.building_at(self.screen_to_local(pos))

    def prop_at(self,pos):
        return self.farm.prop_at(self.screen_to_local(pos)) if VIEWPORT.collidepoint(pos) else None

    def pet_at(self,pos,actors):
        return self.farm.pet_at(self.screen_to_local(pos),actors) if VIEWPORT.collidepoint(pos) else None

    def zone_at(self,pos):
        wp = self.camera.screen_to_world(pos)
        return next((key for key,spec in C.TOWN_ZONES.items() if pygame.Rect(spec["rect"]).collidepoint(wp)),None)

    def person_at(self,pos):
        return next((ident for rect,ident in reversed(self.person_hits) if rect.collidepoint(pos)),None)

    def town_building_at(self,pos):
        return next((ident for rect,ident in reversed(self.town_building_hits) if rect.collidepoint(pos)),None) if VIEWPORT.collidepoint(pos) else None

    def vehicle_at(self,pos):
        return next((ident for rect,ident in reversed(self.vehicle_hits) if rect.collidepoint(pos)),None) if VIEWPORT.collidepoint(pos) else None

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
        return VIEWPORT.collidepoint(pos) and bool(self.home_button.collidepoint(pos) or self.overview_rect.collidepoint(pos) or self.pet_at(pos,actors) or self.prop_at(pos) or self.building_at(pos) is not None or self.sanitation.waste_at(pos) or self.zone_at(pos) or self.person_at(pos) or self.vehicle_at(pos) or self.town_building_at(pos))

    def scaled(self,sprite):
        zoom=self.camera.zoom
        if zoom==1: return sprite
        return self.scaled_sprites.get((sprite,zoom),lambda:pygame.transform.smoothscale(sprite,(max(1,round(sprite.get_width()*zoom)),max(1,round(sprite.get_height()*zoom)))))

    def static_frame(self,g,area):
        key=(area.x,area.y,self.camera.zoom,g.season,self.light,g.weather.current,g.meta['road_revision'],tuple(z['level'] for z in g.town_zones))
        def build():
            layer=pygame.Surface(area.size)
            layer.blit(self._background(g),(-area.x,-area.y))
            self.road_layer.draw(layer,g.construction.grid,area,g.season,self.light,g.weather.current)
            return pygame.transform.smoothscale(layer,VIEWPORT.size)
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

    def _tree(self,s,x,y,r=32):
        art.draw_shadow(s,(x,y+10),(r*1.7,r*.45),40,5)
        pygame.draw.line(s,(130,104,74),(x,y+9),(x,y-r),8)
        for dx,dy,rr in ((-15,-28,r*.75),(14,-29,r*.80),(0,-48,r*.85)):
            pygame.draw.circle(s,(106,151,112),(int(x+dx),int(y+dy)),int(rr))
            pygame.draw.circle(s,(142,180,128),(int(x+dx-5),int(y+dy-6)),int(rr*.72))

    def _background(self,game):
        key = (game.season,self.light,game.weather.current,tuple((z["zone_id"],z["level"]) for z in game.town_zones))
        return self.backgrounds.get(key,lambda:self._build_world(game))

    def _build_world(self,g):
        self.static_builds+=1
        s = pygame.Surface(C.WORLD_SIZE)
        season = courtyard.SEASONS[g.season]
        grass = art.mix(season["grass"],(212,224,174),.25)
        s.fill(grass)
        # 河谷、花木和柔和的地形色带。
        for i in range(100):
            x,y = (i*337+97)%3600,(i*193+181)%2400
            pygame.draw.ellipse(s,art.lighten(grass,.035),(x,y,180,90))
        for key,spec in C.TOWN_ZONES.items():
            if key == "estate":
                continue
            r = pygame.Rect(spec["rect"])
            level = g.town.level(key)
            fill = art.mix(spec["color"],(247,239,216),.56 if level else .78)
            art.draw_shadow(s,(r.centerx,r.bottom+8),(r.w-10,45),40,12)
            pygame.draw.rect(s,fill,r,border_radius=46)
            pygame.draw.rect(s,(249,244,220),r,5,border_radius=46)
            self._zone_art(s,key,r,level,g)
            tag = pygame.Rect(r.x+28,r.y+20,r.w-56,62)
            art.clay_panel(s,tag,(255,251,239),16,(200,189,155),shadow_alpha=18)
            self.fonts.text(s,spec["name"],(tag.x+22,tag.y+12),27,ui.GREEN)
            self.fonts.text(s,f"Lv{level}" if level else f"待开放 · {spec['cost']}金币",(tag.right-230,tag.y+22),17,ui.MUTED)
        return s

    def _zone_art(self,s,key,r,level,g):
        night = self.light == "night"
        if key in ("homes","market"):
            landmark=pygame.Rect(LANDMARKS[0 if key=='homes' else 1])
            art.draw_cottage(s,landmark,'商店' if key=='market' else '饲养小屋',max(1,level),
                             (187,163,143),night=night,winter=g.season=='冬')
        elif key == "station":
            art.draw_cottage(s,pygame.Rect(r.x+130,r.y+100,270,210),"商店",max(1,level),(157,172,181),night=night)
            pygame.draw.rect(s,(226,214,184),(r.x+450,r.y+145,480,185),border_radius=16)
            for yy in (r.y+350,r.y+385):
                pygame.draw.line(s,(112,115,104),(r.x+40,yy),(r.right-40,yy),6)
            for x in range(r.x+50,r.right-50,32):
                pygame.draw.line(s,(143,126,97),(x,r.y+340),(x,r.y+397),8)
            for i in range(3):
                coach = pygame.Rect(r.x+460+i*145,r.y+250,135,95)
                pygame.draw.rect(s,(107,158,145),coach,border_radius=13)
                for dx in (12,48,84):
                    pygame.draw.rect(s,(224,238,221),(coach.x+dx,coach.y+18,25,31),border_radius=5)
                for dx in (25,105):
                    pygame.draw.circle(s,(81,91,83),(coach.x+dx,coach.bottom),12)
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
            shade.fill((224,222,199,95))
            s.blit(shade,r.topleft)

    def _draw_people(self,layer,g,area,only=None):
        lookup = {p["person_id"]:p for p in g.people}
        colors = ((115,155,169),(185,133,136),(145,167,122),(188,155,105),(156,143,183),(119,169,158))
        for actor in ([only] if only else sorted(self.people.values(),key=lambda a:a.y)):
            if not actor.visible: continue
            person = lookup.get(actor.ident)
            if person is None:
                continue
            x,y = round(actor.x-area.x),round(actor.y-area.y)
            if not (-80 < x < area.w+80 and -100 < y < area.h+40):
                continue
            walk = math.sin(actor.anim*6)*3 if actor.path else 0
            art.draw_shadow(layer,(x,y),(25,8),38,3)
            for sign in (-1,1):
                pygame.draw.line(layer,(88,99,99),(x+sign*5,y-14),(int(x+sign*(5+walk)),y-1),5)
            pygame.draw.rect(layer,colors[person["color_index"]],(x-10,y-35,21,24),border_radius=7)
            pygame.draw.circle(layer,(237,201,166),(x,y-42),10)
            pygame.draw.arc(layer,(107,84,65),(x-10,y-54,21,21),0,math.pi,5)
            pygame.draw.circle(layer,(65,60,52),(x+4,y-42),1)
            if person["role"] == "游客":
                pygame.draw.rect(layer,(209,179,118),(x-16,y-29,9,14),border_radius=3)
            if g.weather.current in ("小雨","暴雨","雨夹雪"):
                pygame.draw.line(layer,(119,114,100),(x+10,y-28),(x+10,y-61),2)
                pygame.draw.arc(layer,colors[person["color_index"]],(x-9,y-79,40,33),0,math.pi,14)
            world_rect = pygame.Rect(actor.x-16,actor.y-67,34,73)
            self.person_hits.append((self.project_rect(world_rect),actor.ident))

    def _weather_overlay(self,screen,g):
        weather = g.weather.current
        tint = {"dawn":(255,192,151,20),"dusk":(222,145,113,40),"night":(26,39,77,115)}.get(self.light)
        if not tint and weather=='晴天': return
        overlay = pygame.Surface(VIEWPORT.size,pygame.SRCALPHA)
        if tint:
            overlay.fill(tint)
        if weather in ("阴天","暴雨","雨夹雪"):
            overlay.blit(art.shade_surface(VIEWPORT.size,(66,87,100,34 if weather == "阴天" else 60)),(0,0))
        if weather in ("小雨","暴雨","雨夹雪"):
            for i in range(130 if weather == "暴雨" else 70):
                x = int((i*151-self.time*150)%1320)-20
                y = int((i*79+self.time*460)%660)-25
                pygame.draw.line(overlay,(210,232,241,150),(x,y),(x-8,y+18),1 if weather != "暴雨" else 2)
        if weather in ("降雪","雨夹雪"):
            for i in range(85):
                x = int((i*163+self.time*20+math.sin(self.time+i)*13)%1280)
                y = int((i*83+self.time*(24+i%5*5))%616)
                pygame.draw.circle(overlay,(252,254,255,220),(x,y),2+i%3)
        if weather == "大风":
            for i in range(35):
                x = int((i*191+self.time*260)%1360)-40
                y = int((i*79+math.sin(self.time*2+i)*9)%616)
                pygame.draw.line(overlay,(246,244,218,150),(x,y),(x+40,y-5),2)
                pygame.draw.ellipse(overlay,(183,164,91,200),(x+20,y-10,9,4))
        screen.blit(overlay,VIEWPORT.topleft)

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
        frame=pygame.Rect(inside.x+self.camera.x/3600*inside.w,inside.y+self.camera.y/2400*inside.h,1280/self.camera.zoom/3600*inside.w,616/self.camera.zoom/2400*inside.h)
        pygame.draw.rect(s,ui.GREEN,frame,2,border_radius=3)

    def _hover_tip(self,screen,g):
        pos=pygame.mouse.get_pos()
        if not VIEWPORT.collidepoint(pos) or any(r.collidepoint(pos) for r in (self.legend_rect,self.home_button,self.overview_rect)):
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
            rect=pygame.Rect(min(1280-width-8,pos[0]+15),min(658,pos[1]+18),width,35)
            art.clay_panel(screen,rect,ui.PANEL,8,ui.LINE,shadow_alpha=15)
            self.fonts.text(screen,text,(rect.x+12,rect.y+8),13,ui.TEXT)

    def draw(self,screen,g,actors,focus_id=None,drag_item=None,feed_count=0):
        self.light=day_phase(g.now)
        if not self.people or self.traffic.revision!=g.meta['road_revision']:
            self.tick(0,g)
        self.last_game=g
        area=pygame.Rect(int(self.camera.x),int(self.camera.y),math.ceil(VIEWPORT.w/self.camera.zoom),math.ceil(VIEWPORT.h/self.camera.zoom))
        screen.blit(self.static_frame(g,area),VIEWPORT.topleft)
        saved_clip=screen.get_clip(); screen.set_clip(VIEWPORT)
        self.town_building_hits=[]; self.person_hits=[]; self.vehicle_hits=[]
        drawables=[]; lookup={p['person_id']:p for p in g.people}
        wet=g.weather.current in ('小雨','暴雨','雨夹雪')
        if pygame.Rect(C.FARM_WORLD_RECT).colliderect(area):
            self.farm.draw(self.farm_surface,g,actors,focus_id,drag_item,feed_count,pointer=self.screen_to_local(pygame.mouse.get_pos()))
            farm_image=self.farm_surface.subsurface(courtyard.SCENE)
            # 宠物/建筑本身已有抗锯齿；整块庄园使用快速缩放，避免每帧重采样百万像素。
            image=pygame.transform.scale(farm_image,(round(FW*self.camera.zoom),round(FH*self.camera.zoom))) if self.camera.zoom!=1 else farm_image
            drawables.append((FY+FH,'farm',None,(image,self.project_rect(C.FARM_WORLD_RECT))))
        for actor in self.people.values():
            person=lookup.get(actor.ident)
            rect=pygame.Rect(actor.x-33,actor.y-96,72,106)
            if not actor.visible or not person or not rect.colliderect(area): continue
            frame=int(actor.anim*7+int(actor.ident[:2],16))%4 if actor.path else -1
            sprite=town_art.person_sprite(person['color_index'],person['role']=='游客',wet,frame)
            drawables.append((actor.y,'person',actor,(self.scaled(sprite),self.project_rect(rect))))
        for building in g.town_buildings:
            b=building
            stage=4 if b['status']=='ready' else min(3,int(4*(g.now-b['placed_ts'])/(b['finish_ts']-b['placed_ts'])))
            sprite=town_art.building_sprite(b['catalog_key'],b['orientation'],self.light,g.season,g.weather.current,stage)
            rect=sprite.get_rect(midbottom=((b['cx']+b['w']/2)*48,(b['cy']+b['h'])*48+8))
            if rect.colliderect(area): drawables.append(((b['cy']+b['h'])*48,'building',b,(self.scaled(sprite),self.project_rect(rect))))
        for vehicle in self.traffic.vehicles:
            v=vehicle; sprite=town_art.vehicle_sprite(v.kind,v.color,v.angle,self.light=='night',wet)
            rect=sprite.get_rect(center=(v.x,v.y))
            if rect.colliderect(area): drawables.append((v.y,'vehicle',v,(self.scaled(sprite),self.project_rect(rect))))
        cleared=set(g.meta['town_cleared']); occupied=g.construction.occupied()
        trees=[(x,2280) for x in range(150,3500,230)]+[(55,y) for y in range(130,2200,240)]+[(3540,y+35) for y in range(130,2200,240)]
        for x,y in trees:
            cell=world_to_cell(x,y)
            if f'{cell[0]},{cell[1]}' in cleared or cell in occupied or cell in g.construction.grid.roads: continue
            rect=pygame.Rect(x-55,y-110,110,125)
            if rect.colliderect(area):
                def tree_sprite():
                    image=pygame.Surface((110,125),pygame.SRCALPHA); self._tree(image,55,110,30); return image
                sprite=self.prop_sprites.get(('tree',g.season,self.light),tree_sprite)
                drawables.append((y,'tree',None,(self.scaled(sprite),self.project_rect(rect))))
        for _,kind,obj,payload in sorted(drawables,key=lambda row:row[0]):
            sprite,rect=payload; screen.blit(sprite,rect)
            if kind=='person':
                self.person_hits.append((self.project_rect(pygame.Rect(obj.x-16,obj.y-72,34,78)),obj.ident))
            elif kind=='building':
                self.town_building_hits.append((rect,obj['building_id']))
                if not g.construction.building_connected(obj): self.fonts.text(screen,'道路中断',(rect.x+10,rect.bottom-16),13,ui.RED)
            elif kind=='vehicle': self.vehicle_hits.append((rect,obj.vehicle_id))
        screen.set_clip(saved_clip)
        old_clip = screen.get_clip()
        screen.set_clip(VIEWPORT)
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
