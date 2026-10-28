"""六种程序化树木，按身份固定形态；本地随机源不影响游戏状态。"""
import math
import random
import pygame
import art
from appearance import identity_seed
from render_canvas import Glyph

SPECIES={'oak':'圆冠橡树','birch':'细叶白桦','pine':'常青松树','willow':'湖岸垂柳','cherry':'春日花树','maple':'掌叶枫树'}
_BASES=art.LRU(128)
_SPRITES=art.LRU(384)
_GROUND=art.LRU(64)
SIZE=(144,184)


def profile(identity,species=None):
    seed=identity_seed(str(identity),'tree')
    return species or tuple(SPECIES)[seed%len(SPECIES)],seed//7%24,116+(seed//173%5)*9


def _build(species,variant,season,light):
    s=pygame.Surface((SIZE[0]*2,SIZE[1]*2),pygame.SRCALPHA)
    rng=random.Random(identity_seed(f'{species}:{variant}','tree-shape'))
    def point(p):return tuple(round(v*2) for v in p)
    def line(color,a,b,width=1):pygame.draw.line(s,color,point(a),point(b),max(1,round(width*2)))
    def poly(color,points):pygame.draw.polygon(s,color,[point(p) for p in points])
    def ell(color,rect):pygame.draw.ellipse(s,color,tuple(round(v*2) for v in rect))
    def circle(color,p,r):pygame.draw.circle(s,color,point(p),max(1,round(r*2)))
    def lobe(cx,cy,rx,ry,color):
        image=art.foliage_lobe((round(rx*4),round(ry*4)),color)
        s.blit(image,point((cx-rx,cy-ry)))
    def volume_poly(color,points):art.shaded_polygon(s,[point(p) for p in points],color)
    def branch(a,b,width=3):
        line(art.darken(trunk,.16),a,b,width+1)
        line(trunk,(a[0]-1,a[1]),(b[0]-1,b[1]),width)
    trunk={'birch':(174,165,140),'cherry':(147,103,92),'maple':(125,94,78)}.get(species,(135,103,74))
    greens={'oak':(111,154,98),'birch':(143,177,115),'pine':(75,130,108),
            'willow':(130,164,105),'cherry':(124,166,109),'maple':(113,153,108)}
    leaf=greens[species]
    if season=='春':leaf=art.lighten(leaf,.08)
    if season=='秋' and species!='pine':
        leaf={'oak':(181,153,89),'birch':(213,183,99),'willow':(185,174,106),
              'cherry':(192,134,107),'maple':(193,110,82)}[species]
    tint=(variant%5-2)*4
    leaf=tuple(max(0,min(255,c+tint)) for c in leaf)
    dark,shine=art.darken(leaf,.19),art.lighten(leaf,.15)
    lean=(variant//5%5-2)*2.5
    crown_width=.88+(variant%4)*.07
    x,foot=72,169
    art.draw_shadow(s,point((x,foot+2)),(round(91*crown_width*2),26),36,8)
    # 分枝位置属于树木身份，不随季节和重绘变化。
    top=(x+lean,58)
    branch((x,foot),(x+lean,104),8 if species!='birch' else 6)
    branch((x+lean,123),top,5 if species!='birch' else 4)
    arms=[((x+lean,120),(x-29+lean,77)),((x+lean,110),(x+32+lean,64)),
          ((x+lean,95),(x-18+lean,46)),((x+lean,91),(x+13+lean,32))]
    spread={'oak':1.08,'birch':.7,'pine':.78,'willow':1.18,'cherry':.9,'maple':1.22}[species]
    arms=[(a,(x+(b[0]-x)*spread,b[1]+(4 if species=='cherry' else -4 if species=='birch' else 0))) for a,b in arms]
    if season=='冬' and species!='pine':
        for a,b in arms:
            branch(a,b,3)
            branch(b,(b[0]-11,b[1]-13),1.5)
            branch(b,(b[0]+10,b[1]-10),1.5)
    if species=='birch':
        for y in (112,129,145,158):line((113,112,96),(x-3+(y%3),y),(x+3,y-2),2)
    if season=='冬' and species!='pine':
        for a,b in arms:
            line((238,244,235),(b[0]-9,b[1]-4),(b[0]+6,b[1]-7),3)
        ell((236,242,231),(x-16,foot-3,33,6))
        if species=='willow':
            for dx in (-31,-19,18,31):
                line((153,142,105),(x+dx,78),(x+dx*1.15,128),1)
    elif species=='pine':
        # 三层不规则伞状针叶，轮廓与阔叶树明显不同。
        for cy,half,high in ((110,48,55),(79,36,49),(48,25,39)):
            cx=x+lean*(1-(cy/170))
            points=[(cx,cy-high),(cx-half*.48,cy-20),(cx-half*.34,cy-21),
                    (cx-half,cy+7),(cx-half*.7,cy+5),(cx-half*.81,cy+13),
                    (cx+half*.78,cy+13),(cx+half*.68,cy+6),(cx+half,cy+8),
                    (cx+half*.35,cy-20),(cx+half*.48,cy-18)]
            poly(dark,points)
            volume_poly(leaf,[(px-1,py-3) for px,py in points])
            if season=='冬':
                poly((232,241,231),[(cx,cy-high),(cx-half*.45,cy-18),(cx-3,cy-21),(cx+half*.42,cy-17)])
                poly((232,241,231),[(cx-half*.87,cy+4),(cx-half*.38,cy-3),(cx+half*.45,cy-2),
                                    (cx+half*.87,cy+5),(cx+half*.4,cy+9),(cx-half*.45,cy+8)])
    elif species=='willow':
        lobe(x-26,76,31,27,leaf);lobe(x+23,77,34,28,leaf);lobe(x,63,37,25,leaf)
        for i in range(12):
            dx=-48+i*8.5;length=43+rng.randrange(10,38)
            start=(x+dx,70-abs(dx)*.15);end=(x+dx*1.08+rng.randrange(-4,5),start[1]+length)
            line(dark,start,end,5)
            for t in (.08,.24,.4,.56,.72,.86):
                px=start[0]+(end[0]-start[0])*t;py=start[1]+(end[1]-start[1])*t
                ell(art.mix(leaf,shine,(i%4)*.15),(px-2.5,py-5,5,12))
    elif species=='birch':
        for i,(cx,cy,w,h) in enumerate(((x-9,36,34,43),(x+12,57,39,48),(x-20,70,37,49),
                                       (x+8,94,42,44),(x-15,108,33,35))):
            cx+=rng.randrange(-5,6);cy+=rng.randrange(-4,5)
            lobe(cx,cy,w/2,h/2,leaf)
    elif species=='maple':
        # 掌叶树冠使用有尖角的叶团，不复用橡树的圆形树冠。
        for dx,dy,r in ((-31,82,28),(0,53,35),(31,78,28),(-18,110,25),(22,107,25)):
            cx=x+dx*crown_width+lean*.4;cy=dy+rng.randrange(-3,4)
            points=[]
            for i in range(18):
                angle=i*math.pi/9;radius=r*(1 if i%2==0 else .73)
                points.append((cx+math.cos(angle)*radius,cy+math.sin(angle)*radius))
            poly(dark,[(px,py+3) for px,py in points]);volume_poly(leaf,points)
    else:
        clumps=[(-29,77,29,29),(0,46,34,31),(30,70,31,30),(-29,103,26,25),
                (5,91,34,29),(31,107,24,24),(-4,117,27,21)]
        if species=='cherry' and season=='春':
            leaf=(224+tint//2,166+tint//2,177+tint//2);dark=art.darken(leaf,.16);shine=art.lighten(leaf,.25)
        for dx,cy,rx,ry in clumps:
            cx=x+dx*crown_width+lean*.5+rng.randrange(-3,4);cy+=rng.randrange(-4,5)
            lobe(cx,cy,rx,ry,leaf)
            if species=='cherry' and season=='春':
                for _ in range(3):
                    px,py=cx+rng.randrange(-17,18),cy+rng.randrange(-12,13)
                    for ox,oy in ((-1.5,0),(1.5,0),(0,-1.5),(0,1.5)):circle((245,211,211),(px+ox,py+oy),1.6)
                    circle((246,204,131),(px,py),1)
            elif species=='cherry' and season=='夏':
                circle((191,89,80),(cx+6,cy+14),2.6)
    if season=='秋' and species!='pine':
        for i in range(7):
            dx=rng.randrange(-35,36);dy=rng.randrange(-4,9)
            ell(leaf,(x+dx,foot+dy,5+i%3,3))
    tint={'dawn':(255,245,231),'dusk':(249,230,211),'night':(226,234,247)}.get(light)
    if tint:
        overlay=pygame.Surface(s.get_size(),pygame.SRCALPHA);overlay.fill((*tint,255))
        s.blit(overlay,(0,0),special_flags=pygame.BLEND_RGBA_MULT)
    return s


def tree_sprite(species='oak',variant=0,season='春',light='day',height=144,orientation=0):
    if species not in SPECIES:raise ValueError('未知树种')
    variant=int(variant)%24;height=max(48,int(height));orientation=int(orientation)%4
    key=(species,variant,season,light,height,orientation)
    def build():
        base=_BASES.get((species,variant,season,light),lambda:_build(species,variant,season,light))
        width=round(height*SIZE[0]/SIZE[1])
        def native(scale):
            image=pygame.transform.smoothscale(base,(max(1,round(width*scale)),max(1,round(height*scale))))
            if orientation%2:
                image=pygame.transform.flip(image,True,False)
            if orientation>=2:
                image=pygame.transform.smoothscale(image,(max(1,round(width*scale*.9)),max(1,round(height*scale))))
            return image
        return Glyph(native(1),native)
    return _SPRITES.get(key,build)


def ground_sprite(kind,variant,season,light):
    key=(kind,variant%8,season,light)
    def build():
        s=pygame.Surface((62,38),pygame.SRCALPHA)
        rng=random.Random(identity_seed(f'{kind}:{variant%8}','ground'))
        leaf=(184,150,94) if season=='秋' else (129,155,112)
        if season=='冬':leaf=(225,235,219)
        if kind=='rock':
            for x,y,w,h in ((19,18,27,15),(9,23,18,10)):
                pygame.draw.ellipse(s,(132,143,130),(x,y,w,h))
                pygame.draw.ellipse(s,(186,190,171),(x,y,w,h-3))
        else:
            for i in range(7):
                x,y=rng.randrange(9,46),rng.randrange(8,24)
                pygame.draw.ellipse(s,art.lighten(leaf,i*.02),(x,y,12,9))
                if kind=='flowers' and season!='冬':pygame.draw.circle(s,(239,197,160),(x+5,y+2),2)
        if light=='night':
            overlay=pygame.Surface(s.get_size(),pygame.SRCALPHA);overlay.fill((224,234,245,255))
            s.blit(overlay,(0,0),special_flags=pygame.BLEND_RGBA_MULT)
        return s
    return _GROUND.get(key,build)
