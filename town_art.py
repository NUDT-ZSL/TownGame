"""道路chunk、2倍超采样黏土建筑与车辆精灵；不接触模型随机数/数据库。"""
import math
import pygame
import art
import town_catalog as K
from town_grid import GRID_SIZE as S, cell_center, DIRECTIONS,PORTALS

_BUILDINGS=art.LRU(192)
_CARS=art.LRU(256)
_TILES=art.LRU(384)
_PEOPLE=art.LRU(192)


class _Canvas2x:
    """保留逻辑像素坐标，在真正的2倍表面光栅化所有建筑图元。"""
    def __init__(self,size):
        self.size=size; self.surface=pygame.Surface((size[0]*2,size[1]*2),pygame.SRCALPHA)
    def get_size(self): return self.size
    def blit(self,image,position):
        source=image.surface if isinstance(image,_Canvas2x) else pygame.transform.scale(image,(image.get_width()*2,image.get_height()*2))
        self.surface.blit(source,(round(position[0]*2),round(position[1]*2)))


class _Draw:
    def __getattr__(self,name):
        native=getattr(pygame.draw,name)
        def draw(surface,color,*args,**kwargs):
            if not isinstance(surface,_Canvas2x): return native(surface,color,*args,**kwargs)
            point=lambda p:(round(p[0]*2),round(p[1]*2))
            rect=lambda r:pygame.Rect(*(round(v*2) for v in r))
            data=list(args)
            if name in ('rect','ellipse'):
                data[0]=rect(data[0])
                if len(data)>1: data[1]*=2
            elif name=='circle':
                data[0]=point(data[0]); data[1]=round(data[1]*2)
                if len(data)>2: data[2]*=2
            elif name=='line':
                data[0]=point(data[0]); data[1]=point(data[1])
                data.append(2) if len(data)==2 else data.__setitem__(2,data[2]*2)
            elif name=='lines':
                data[1]=[point(p) for p in data[1]]
                data.append(2) if len(data)==2 else data.__setitem__(2,data[2]*2)
            elif name=='polygon':
                data[0]=[point(p) for p in data[0]]
                if len(data)>1: data[1]*=2
            elif name=='arc':
                data[0]=rect(data[0])
                data.append(2) if len(data)==3 else data.__setitem__(3,data[3]*2)
            kwargs={key:(value*2 if key=='width' or key.startswith('border_') and value>0 else value) for key,value in kwargs.items()}
            return native(surface.surface,color,*data,**kwargs)
        return draw


_draw=_Draw()


def person_sprite(color_index,visitor,umbrella,frame):
    def build():
        s=pygame.Surface((72,106),pygame.SRCALPHA); x,y=33,96
        colors=((115,155,169),(185,133,136),(145,167,122),(188,155,105),(156,143,183),(119,169,158))
        walk=math.sin(frame*math.pi/2)*3 if frame>=0 else 0
        art.draw_shadow(s,(x,y),(25,8),38,3)
        for sign in (-1,1): _draw.line(s,(88,99,99),(x+sign*5,y-14),(int(x+sign*(5+walk)),y-1),5)
        _draw.rect(s,colors[color_index],(x-10,y-35,21,24),border_radius=7)
        _draw.circle(s,(237,201,166),(x,y-42),10)
        _draw.arc(s,(107,84,65),(x-10,y-54,21,21),0,math.pi,5)
        _draw.circle(s,(65,60,52),(x+4,y-42),1)
        if visitor: _draw.rect(s,(209,179,118),(x-16,y-29,9,14),border_radius=3)
        if umbrella:
            _draw.line(s,(119,114,100),(x+10,y-28),(x+10,y-61),2)
            _draw.arc(s,colors[color_index],(x-9,y-79,40,33),0,math.pi,14)
        return s
    return _PEOPLE.get((color_index,visitor,umbrella,frame),build)


def road_tile(style,mask,season,light,weather):
    key=(style,mask,season,light,weather)
    def build():
        s=pygame.Surface((S,S),pygame.SRCALPHA); base=K.ROAD_STYLES[style]['color']
        if weather in ('小雨','暴雨','雨夹雪'): base=art.darken(base,.12)
        if light=='night': base=art.darken(base,.08)
        curb=(203,208,187) if style!='garden' else (133,164,121)
        def shape(color,width):
            half=width//2
            _draw.rect(s,color,(24-half,24-half,width,width),border_radius=9)
            for i,(dx,dy) in enumerate(DIRECTIONS):
                if mask & (1<<i):
                    rect=(24-half,0,width,25) if dy==-1 else (24-half,23,width,25) if dy==1 else (0,24-half,25,width) if dx==-1 else (23,24-half,25,width)
                    _draw.rect(s,color,rect)
        shape(curb,47); shape(base,39)
        if style=='asphalt':
            for i,(dx,dy) in enumerate(DIRECTIONS):
                if mask & (1<<i):
                    _draw.line(s,(227,227,202),(24+dx*9,24+dy*9),(24+dx*20,24+dy*20),2)
        elif style=='stone':
            for y in (11,24,37):
                _draw.line(s,art.darken(base,.17),(5,y),(43,y),1)
                _draw.line(s,art.darken(base,.17),(12 if y%2 else 31,y-10),(12 if y%2 else 31,y),1)
        else:
            for y in (13,26,39): _draw.line(s,(190,164,129),(5,y),(43,y),1)
            _draw.ellipse(s,(235,202,174),(30,9,5,3))
        if weather in ('降雪','雨夹雪') or season=='冬':
            _draw.line(s,(237,244,234),(6,6),(18,6),3)
        if weather in ('小雨','暴雨','雨夹雪'):
            _draw.line(s,(184,198,198),(8,36),(18,36),1)
        if mask.bit_count()!=2 and mask:
            for x in (11,19,27,35): _draw.line(s,(237,231,207),(x,18),(x,27),3)
        return s
    return _TILES.get(key,build)


class RoadLayer:
    """每块6×6格，签名只含该块及邻格；道路编辑不失效静态世界。"""
    def __init__(self):
        self.cache=art.LRU(160); self.build_count=0

    def draw(self,layer,grid,area,season,light,weather):
        side=K.CHUNK_SIZE*S
        for cy in range(max(0,area.top//side),min(8,area.bottom//side)+1):
            for cx in range(max(0,area.left//side),min(12,area.right//side)+1):
                rows=[]
                for y in range(cy*6,cy*6+6):
                    for x in range(cx*6,cx*6+6):
                        if (x,y) in grid.roads:
                            r=grid.roads[x,y]; rows.append((x%6,y%6,r['road_style'],grid.mask((x,y)),
                                tuple(grid.roads.get((x+dx,y+dy),{}).get('road_style') for dx,dy in DIRECTIONS)))
                if not rows: continue
                key=(cx,cy,tuple(rows),season,light,weather)
                def build():
                    self.build_count+=1; s=pygame.Surface((side,side),pygame.SRCALPHA)
                    for x,y,style,mask,neighbors in rows:
                        s.blit(road_tile(style,mask,season,light,weather),(x*S,y*S))
                        if (cx*6+x,cy*6+y) in PORTALS.values():
                            for offset in (11,19,27,35):
                                _draw.line(s,(244,234,211),(x*S+offset,y*S+16),(x*S+offset,y*S+31),3)
                        for i,n in enumerate(neighbors):
                            if n and n!=style:
                                dx,dy=DIRECTIONS[i]; px,py=x*S+24+dx*21,y*S+24+dy*21
                                for offset in (-10,0,10):
                                    _draw.line(s,(243,235,211),(px+(offset if dy else -2),py+(offset if dx else -2)),(px+(offset if dy else 2),py+(offset if dx else 2)),3)
                    return s
                layer.blit(self.cache.get(key,build),(cx*side-area.x,cy*side-area.y))


def _windows(s,r,cols,rows,night,glass=False):
    color=(255,222,143) if night else (133,184,186) if glass else (171,201,190)
    for y in range(rows):
        for x in range(cols):
            box=pygame.Rect(r.x+9+x*(r.w-12)//cols,r.y+10+y*(r.h-14)//rows,max(6,(r.w-22)//cols-5),max(7,(r.h-22)//rows-6))
            _draw.rect(s,(94,117,114),box.inflate(4,4),border_radius=3)
            _draw.rect(s,color,box,border_radius=2)
            _draw.line(s,art.lighten(color,.2),box.topleft,box.topright,2)


def _classic(s,r,spec,night):
    wall,roof=spec['palette']['wall'],spec['palette']['roof']
    _draw.rect(s,wall,r,border_radius=5)
    top=r.top-14
    _draw.polygon(s,art.darken(roof,.16),[(r.x-16,top+22),(r.centerx,top-24),(r.right+16,top+22),(r.right+9,top+30),(r.x-9,top+30)])
    _draw.polygon(s,roof,[(r.x-16,top+15),(r.centerx,top-26),(r.right+16,top+15),(r.right+9,top+21),(r.x-9,top+21)])
    for i in range(9):
        x=r.x+i*r.w//8
        _draw.line(s,art.lighten(roof,.18),(r.centerx+(x-r.centerx)*.42,top-10),(x,top+17),2)
    _draw.line(s,(104,80,65),(r.x-16,top+15),(r.x-24,top+5),4)
    _draw.line(s,(104,80,65),(r.right+16,top+15),(r.right+24,top+5),4)
    _windows(s,r,3,2 if r.h>100 else 1,night)
    for x in (r.x+5,r.right-8):
        _draw.rect(s,(123,86,63),(x,r.y+18,5,r.h-18))
        _draw.line(s,(101,91,68),(x,r.y+18),(x,r.y+32),2)
        _draw.ellipse(s,(223,142,96),(x-6,r.y+28,17,24))


def _modern(s,r,spec,night):
    wall,roof=spec['palette']['wall'],spec['palette']['roof']
    _draw.rect(s,art.darken(wall,.18),r.move(9,-7),border_radius=7)
    _draw.rect(s,wall,r,border_radius=6)
    glass=r.inflate(-17,-14); _windows(s,glass,3,max(1,r.h//32),night,True)
    _draw.rect(s,roof,(r.x-5,r.y-8,r.w+10,14),border_radius=4)
    for y in range(r.y+35,r.bottom-10,32): _draw.line(s,(220,225,214),(r.x,y),(r.right,y),3)
    _draw.rect(s,(123,162,158),(r.right-22,r.y-18,17,15),border_radius=3)
    if spec['key']=='cafe':
        _draw.rect(s,(158,186,171),(r.x-6,r.y+35,r.w+12,15),border_radius=4)
        _draw.circle(s,(234,193,127),(r.right-20,r.bottom-19),10)


def _country(s,r,spec,night):
    wall,roof=spec['palette']['wall'],spec['palette']['roof']
    _draw.rect(s,wall,r,border_radius=8)
    _draw.polygon(s,art.darken(roof,.14),[(r.x-10,r.y+5),(r.centerx,r.y-43),(r.right+12,r.y+5),(r.right+6,r.y+15),(r.x-9,r.y+15)])
    _draw.polygon(s,roof,[(r.x-9,r.y+2),(r.centerx,r.y-41),(r.right+11,r.y+2),(r.right+5,r.y+8),(r.x-7,r.y+8)])
    for i in range(6): _draw.line(s,art.lighten(roof,.16),(r.x+i*r.w//5,r.y),(r.centerx+(r.x+i*r.w//5-r.centerx)*.25,r.y-30),2)
    _windows(s,r,2,1,night)
    for x in (r.x+12,r.right-32):
        _draw.rect(s,(143,112,76),(x,r.bottom-26,23,9),border_radius=3)
        for dx in (2,10,18):
            _draw.circle(s,(139,173,113),(x+dx,r.bottom-27),5)
            _draw.circle(s,(226,157,150),(x+dx,r.bottom-30),3)
    if spec['key']=='windmill':
        center=(r.centerx,r.y-12)
        for dx,dy in ((-30,-30),(30,-30),(-30,30),(30,30)):
            _draw.line(s,(225,220,184),center,(center[0]+dx,center[1]+dy),9)
        _draw.circle(s,(127,105,75),center,7)


def _industrial(s,r,spec,night):
    wall,roof=spec['palette']['wall'],spec['palette']['roof']
    _draw.rect(s,wall,r,border_radius=4)
    for y in range(r.y+6,r.bottom,11):
        _draw.line(s,art.lighten(wall,.13),(r.x,y),(r.right,y),1)
        for x in range(r.x+(5 if y%2 else 15),r.right,25): _draw.line(s,art.darken(wall,.12),(x,y),(x,y+10),1)
    points=[(r.x-4,r.y+6)]
    for i in range(3): points.extend([(r.x+i*r.w//3,r.y-21),(r.x+(i+1)*r.w//3,r.y+1)])
    points.extend([(r.right+4,r.y+12),(r.x-4,r.y+12)])
    _draw.polygon(s,roof,points)
    _draw.rect(s,(123,114,104),(r.right-23,r.y-59,16,49),border_radius=3)
    _draw.rect(s,(99,103,97),(r.right-26,r.y-62,22,7),border_radius=3)
    _windows(s,r,3,1,night)
    _draw.line(s,(105,117,115),(r.x-4,r.y+22),(r.x-4,r.bottom-2),6)
    for i in range(5): _draw.line(s,(232,191,85),(r.x+6+i*9,r.bottom-9),(r.x+12+i*9,r.bottom-17),4)


def _public(s,r,spec,night):
    if spec['key']=='bus_stop':
        for x in (r.x+8,r.right-8): _draw.line(s,(98,127,116),(x,r.y+5),(x,r.bottom),4)
        _draw.rect(s,(151,190,182),r.inflate(-8,-10),border_radius=5)
        _draw.rect(s,(92,151,143),(r.x-6,r.y-7,r.w+12,15),border_radius=5)
        _draw.rect(s,(199,163,111),(r.x+9,r.bottom-14,r.w-18,8),border_radius=3)
        _draw.circle(s,(242,214,129),(r.right+9,r.y+9),9)
        return
    _draw.rect(s,(202,211,172),r,border_radius=12)
    _draw.ellipse(s,(224,220,193),r.inflate(-15,-8))
    _draw.ellipse(s,(132,186,188),(r.centerx-23,r.centery-16,46,24))
    _draw.line(s,(205,235,226),(r.centerx,r.centery),(r.centerx,r.centery-28),3)
    for x,y in ((r.x+16,r.y+12),(r.right-15,r.bottom-13)):
        _draw.line(s,(129,105,75),(x,y+9),(x,y-14),4)
        _draw.circle(s,(131,175,118),(x,y-18),17)
        _draw.circle(s,(166,199,139),(x-5,y-23),11)
    _draw.rect(s,(174,135,94),(r.x+15,r.bottom-15,31,7),border_radius=3)


PAINTERS={'classic':_classic,'modern':_modern,'country':_country,'industrial':_industrial,'public':_public}


def building_sprite(key,orientation,light,season,weather,stage=4):
    cache_key=(key,orientation,light,season,weather,stage)
    def build():
        spec=K.CATALOG[key]; fw,fh=spec['footprint'] if orientation%2==0 else reversed(spec['footprint'])
        width=fw*S+62; height=285 if spec['key'] in ('glass_home','office') else 230
        canvas=_Canvas2x((width,height))
        # 绘制函数使用世界像素，再2倍采样的坐标表面以统一轮廓抗锯齿。
        body_h=188 if spec['key'] in ('glass_home','office') else 111 if fw>=3 else 83
        if spec['family']=='public': body_h=65
        base=height-17; rect=pygame.Rect(33,base-body_h,fw*S-4,body_h-7)
        art.draw_shadow(canvas,(width//2,base-3),(fw*S+18,29),55,7)
        _draw.ellipse(canvas,(219,211,180),(17,base-18,width-34,30))
        PAINTERS[spec['family']](canvas,rect,spec,light=='night')
        if spec['family']!='public':
            _draw.rect(canvas,(111,100,78),(rect.centerx-9,rect.bottom-27,19,28),border_radius=5)
            _draw.circle(canvas,(234,203,137),(rect.centerx+5,rect.bottom-12),2)
        # 朝向门牌：同时反映道路入口与旋转后的占地方向。
        if orientation%2: canvas.surface=pygame.transform.flip(canvas.surface,True,False)
        direction=((0,-1),(1,0),(0,1),(-1,0))[orientation]
        _draw.line(canvas,(176,151,103),(width//2,base-5),(width//2+direction[0]*12,base-5+direction[1]*7),3)
        if season=='冬' or weather in ('降雪','雨夹雪'):
            _draw.line(canvas,(245,249,241),(rect.x+4,rect.top-9),(rect.right-4,rect.top-9),5)
        if stage<4:
            body=canvas.surface.copy(); body.set_alpha(110+stage*22)
            canvas.surface.fill((0,0,0,0)); canvas.surface.blit(body,(0,0))
            scaffold=_Canvas2x(canvas.get_size())
            for x in range(rect.x-10,rect.right+12,25):
                _draw.line(scaffold,(161,132,90),(x,rect.top-34),(x,base),3)
            for y in range(rect.top-20,base,28):
                _draw.line(scaffold,(169,138,91),(rect.x-12,y),(rect.right+13,y),3)
            canvas.blit(scaffold,(0,0))
            _draw.rect(canvas,(225,191,107),(rect.x-10,base-22,rect.w+20,20),border_radius=3)
            for x in range(rect.x-7,rect.right,16): _draw.line(canvas,(113,106,78),(x,base-4),(x+10,base-20),4)
        # 2x超采样：先在大表面细化边缘，再平滑缩回；缓存后每帧仅blit。
        return pygame.transform.smoothscale(canvas.surface,(width,height))
    return _BUILDINGS.get(cache_key,build)


def vehicle_sprite(kind,color,angle,night,wet=False):
    quantized=round(angle/10)*10%360
    key=(kind,tuple(color),quantized,night,wet)
    def build():
        w,h=(24,42) if kind=='car' else (30,70) if kind=='bus' else (29,56)
        s=pygame.Surface((w+28,h+32),pygame.SRCALPHA); x,y=14,13
        art.draw_shadow(s,(x+w/2+2,y+h/2+4),(w+7,h-3),45,5)
        for yy in (y+9,y+h-13):
            for xx in (x-3,x+w-2): _draw.rect(s,(66,76,74),(xx,yy,6,10),border_radius=2)
        _draw.rect(s,art.darken(color,.2),(x,y+3,w,h),border_radius=8)
        _draw.rect(s,color,(x,y,w,h-4),border_radius=7)
        _draw.rect(s,art.lighten(color,.15),(x+3,y+3,w-6,h-10),border_radius=6)
        if kind=='truck':
            _draw.rect(s,(208,204,179),(x+1,y+19,w-2,h-23),border_radius=3)
            for xx in range(x+5,x+w-2,7): _draw.line(s,(173,175,160),(xx,y+22),(xx,y+h-7),1)
        else:
            _draw.rect(s,(105,155,168),(x+4,y+9,w-8,11 if kind=='car' else 34),border_radius=4)
            _draw.line(s,(199,224,219),(x+6,y+10),(x+w-7,y+10),2)
        for xx in (x+3,x+w-7):
            _draw.rect(s,(255,238,174),(xx,y,5,4),border_radius=2)
            _draw.rect(s,(214,111,90),(xx,y+h-6,5,4),border_radius=2)
        if night:
            _draw.polygon(s,(255,239,177,48),[(x+2,y+2),(x-9,0),(x+w+9,0),(x+w-2,y+2)])
        return pygame.transform.rotate(s,quantized)
    return _CARS.get(key,build)
