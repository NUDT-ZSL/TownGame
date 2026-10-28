"""程序化伪 3D 宠物：十二物种、成长阶段、稀有/生病外观、姿势动画与帧缓存。

所有立绘由椭圆、胶囊和渐变现场绘制，2× 超采样后缩小得到柔边。宠物动画由
`PetActor` 驱动，只使用调用方传入的随机数（UI 的独立 RNG），不接触存档随机源。
"""
import math
from dataclasses import dataclass
import pygame
from render_canvas import Glyph
import art
import game_config as C
from appearance import rabbit_variant


STAGE_HEIGHTS = {"幼崽": 96, "少年": 110, "成年": 128, "老年": 116}
POSE_FRAMES = {"idle": 4, "idle_blink": 4, "walk": 6, "sleep": 2, "eat": 4, "pet": 4, "bath": 6}
POSE_SPEED = {"idle": 1.7, "idle_blink": 1.7, "walk": 5.4, "sleep": .9, "eat": 4.2, "pet": 2.6, "bath": 4.6}
REACTIONS = ("eat", "pet", "bath")
LIGHT_TINTS = {"dawn": (255, 238, 226), "day": (255, 255, 255),
               "dusk": (255, 235, 221), "night": (225, 232, 248)}
SPECIES_KEYS = {"兔子": "rabbit", "绵羊": "sheep", "鹦鹉": "parrot", "狐狸": "fox",
                "猫咪": "cat", "小狗": "dog", "仓鼠": "hamster", "刺猬": "hedgehog",
                "羊驼": "alpaca", "梅花鹿": "deer", "小鸭": "duck", "孔雀": "peacock"}

PALETTES = {
    "兔子": {"body": (247, 224, 196), "belly": (255, 246, 232), "ear": (246, 182, 182),
             "face": (247, 224, 196), "paw": (238, 206, 176), "nose": (228, 136, 138),
             "eye": (56, 44, 38), "accent": (232, 158, 140)},
    "绵羊": {"body": (241, 238, 228), "belly": (252, 250, 242), "ear": (210, 182, 154),
             "face": (229, 204, 174), "paw": (132, 106, 84), "nose": (104, 80, 66),
             "eye": (54, 46, 40), "accent": (198, 168, 132)},
    "鹦鹉": {"body": (124, 196, 160), "belly": (178, 226, 196), "ear": (92, 166, 134),
             "face": (132, 202, 168), "paw": (238, 160, 90), "beak": (247, 178, 80),
             "tail": (86, 152, 124), "eye": (52, 48, 40), "accent": (246, 120, 96)},
    "狐狸": {"body": (238, 150, 86), "belly": (253, 243, 230), "ear": (232, 134, 88),
             "face": (242, 160, 96), "paw": (108, 74, 54), "nose": (70, 48, 38),
             "eye": (86, 56, 38), "accent": (255, 206, 158)},
}
RARE_PALETTES = {
    "兔子": {"body": (255, 234, 238), "belly": (255, 251, 250), "ear": (246, 196, 210),
             "face": (255, 234, 238), "paw": (250, 220, 228), "nose": (214, 156, 88),
             "accent": (240, 188, 120)},
    "绵羊": {"body": (247, 222, 148), "belly": (255, 240, 190), "face": (240, 216, 180),
             "ear": (222, 190, 132), "nose": (120, 92, 62), "accent": (204, 160, 80)},
    "鹦鹉": {"body": (150, 196, 238), "belly": (208, 232, 250), "ear": (112, 160, 214),
             "face": (160, 204, 242), "beak": (246, 190, 90), "tail": (96, 138, 200),
             "accent": (246, 140, 120)},
    "狐狸": {"body": (182, 202, 230), "belly": (246, 250, 255), "ear": (152, 172, 208),
             "face": (190, 208, 234), "paw": (96, 114, 150), "nose": (64, 76, 104),
             "accent": (226, 236, 252)},
}

# 新品种共用材质键，轮廓由独立绘图分支决定；色彩由品种配置集中控制。
for _species in ("猫咪", "小狗", "仓鼠", "刺猬", "羊驼", "梅花鹿", "小鸭", "孔雀"):
    _body = C.SPECIES[_species]["color"]
    PALETTES[_species] = {
        "body": _body, "face": _body, "belly": art.mix(_body, (255, 248, 228), .70),
        "ear": art.mix(_body, (228, 163, 160), .35), "paw": art.darken(_body, .18),
        "nose": (106, 76, 69), "eye": (58, 48, 45), "accent": (232, 169, 113),
        "beak": (237, 161, 64), "tail": art.darken(_body, .20),
    }
    _tint = (193, 212, 245) if _species in ("小狗", "梅花鹿", "孔雀") else (244, 209, 233)
    RARE_PALETTES[_species] = {
        key: art.mix(value, _tint, .58) for key, value in PALETTES[_species].items()
        if key not in ("eye", "nose", "beak")
    }


RABBIT_COATS = ((247,224,196),(244,241,232),(146,111,91),(157,168,179),
                (210,164,108),(100,106,116),(201,193,169),(181,121,91))


def palette_for(species, rare=False, stage="成年", sick=False, appearance=0):
    pal = dict(PALETTES[species])
    if species!='兔子' and appearance%8:
        coat=RABBIT_COATS[appearance%8]
        for key in ('body','face','paw','tail','ear'):
            if key in pal:
                pal[key]=art.mix(pal[key],coat,.78)
        pal['belly']=art.mix(coat,(255,249,235),.76)
    if species=='兔子':
        coat = RABBIT_COATS[appearance%8]
        pal.update(body=coat,face=coat,paw=art.darken(coat,.08),
                   belly=art.mix(coat,(255,249,235),.72))
    if rare:
        pal.update(RARE_PALETTES[species])
        if species!='兔子' and appearance%8:
            for key in ('body','face','paw'):
                pal[key]=art.mix(pal[key],RABBIT_COATS[appearance%8],.35)
        if species=='兔子':
            for key in ('body','face','paw'):
                pal[key] = art.mix(pal[key],RABBIT_COATS[appearance%8],.25)
    if stage == "老年":
        for key in ("body", "face", "belly", "ear", "paw", "wing"):
            if key in pal:
                pal[key] = art.mix(pal[key], (212, 202, 190), .26)
    elif stage == "幼崽":
        for key in ("body", "face", "belly"):
            if key in pal:
                pal[key] = art.lighten(pal[key], .04)
    if sick:
        for key in ("body", "face", "belly", "ear", "wing"):
            if key in pal:
                pal[key] = art.mix(pal[key], (208, 206, 192), .18)
    if species=='兔子':
        pal['pattern'],pal['ears'],pal['tuft'] = appearance//8%4,appearance//32%3,appearance//96%4
        pal['mark'] = art.mix(pal['body'],(86,68,60),.48)
        pal['white'] = art.mix(pal['body'],(255,250,239),.86)
    return pal


@dataclass
class PetActor:
    """庭院中的一只宠物。坐标是脚底中心，y 同时用于深度排序。"""
    pet_id: str
    name: str
    species: str
    stage: str = "幼崽"
    rare: bool = False
    sick: bool = False
    x: float = 0.0
    y: float = 0.0
    facing: int = 1
    state: str = "idle"
    state_time: float = 2.0
    state_duration: float = 2.0
    target: tuple = (0, 0)
    anim: float = 0.0
    blink: float = 0.0
    blink_timer: float = 3.0
    selected: bool = False
    hovered: bool = False
    drawn_height: int = 100
    drawn_width: int = 85
    appearance: int = -1
    personality: str = '粘人'
    hunger: float = 100
    happy: float = 80
    behavior_note: str = '自在休息'

    def sync_from_pet(self, pet, now):
        self.name = pet.name
        self.stage = pet.stage(now)
        self.rare = pet.rare
        self.sick = pet.unwell
        self.personality,self.hunger,self.happy=pet.personality,pet.hunger,pet.happy

    @property
    def pose(self):
        pose = self.state if self.state in POSE_FRAMES else "idle"
        if pose == "idle" and self.blink > 0:
            return "idle_blink"
        return pose

    def react(self, state, duration=None):
        self.state = state
        self.state_duration = duration if duration is not None else 1.5
        self.state_time = self.state_duration
        self.target = (self.x, self.y)

    def update(self, dt, bounds, rng, night=False, context=None):
        dt = max(0.0, min(dt, .25))
        self.anim += dt
        if self.state != "sleep":
            self.blink = max(0.0, self.blink-dt)
            self.blink_timer -= dt
            if self.blink_timer <= 0:
                self.blink = .16
                self.blink_timer = rng.uniform(2.4, 5.4)
        self.state_time -= dt
        if self.state in REACTIONS:
            if self.state_time <= 0:
                self.state, self.state_time = "idle", rng.uniform(1.6, 3.6)
            return
        if self.state == "walk":
            tx, ty = self.target
            dx, dy = tx-self.x, ty-self.y
            distance = math.hypot(dx, dy)
            if distance < 3 or self.state_time <= 0:
                self.state, self.state_time = "idle", rng.uniform(1.8, 4.2)
                return
            speed = 30.0*(.7 if self.sick else 1.0)*(1.15 if self.personality in ('粘人','活泼') else .85 if self.personality=='高冷' else 1)
            step = min(distance, speed*dt)
            self.x += dx/distance*step
            self.y += dy/distance*step
            if abs(dx) > .5:
                self.facing = 1 if dx > 0 else -1
        elif self.state == "sleep":
            if self.state_time <= 0:
                self.state, self.state_time = "idle", rng.uniform(1.4, 3.2)
        elif self.state_time <= 0:
            self._decide(bounds, rng, night, context)
        self.x = max(bounds.left+26, min(bounds.right-26, self.x))
        self.y = max(bounds.top+30, min(bounds.bottom-4, self.y))

    def _decide(self, bounds, rng, night, context=None):
        roll = rng.random()
        if context and not night and not self.sick:
            target=None
            neighbors=context.get('companions',[])
            if self.personality=='胆小' and any(math.hypot(self.x-x,self.y-y)<130 for x,y in neighbors):
                corners=[(bounds.left+40,bounds.top+40),(bounds.right-40,bounds.top+40),
                         (bounds.left+40,bounds.bottom-12),(bounds.right-40,bounds.bottom-12)]
                target=max(corners,key=lambda p:min(math.hypot(p[0]-x,p[1]-y) for x,y in neighbors))
                self.behavior_note='避开热闹，寻找安静角落'
            elif self.hunger<45 or self.personality=='吃货' and roll<.7:
                target=context.get('food');self.behavior_note='靠近食盆，等候喂食'
            elif self.personality in ('粘人','活泼') and self.happy>=60 and roll<(.85 if self.personality=='活泼' else .65):
                toy=context.get('toy')
                if toy:
                    target=(toy[0]+rng.uniform(-48,48),toy[1]+rng.uniform(-24,24))
                    self.behavior_note='围着玩具蹦跳玩耍'
            elif self.personality=='高冷' and roll<.5:
                self.state,self.state_time='idle',rng.uniform(3,6)
                self.behavior_note='独自观察小镇'
                return
            if target:
                self.target=(max(bounds.left+30,min(bounds.right-30,target[0])),max(bounds.top+30,min(bounds.bottom-4,target[1])))
                self.state,self.state_time='walk',6
                return
        self.behavior_note='夜间休息' if night else '自在散步'
        sleep_chance = .16+(.20 if night else 0)+(.20 if self.sick else 0)
        if roll < sleep_chance:
            self.state, self.state_duration = "sleep", rng.uniform(4.0, 8.5)
            self.state_time = self.state_duration
        elif roll < sleep_chance+.52:
            self.target = (rng.uniform(bounds.left+30, bounds.right-30),
                           rng.uniform(bounds.top+24, bounds.bottom-4))
            self.state, self.state_duration = "walk", 6.0
            self.state_time = self.state_duration
        else:
            self.state, self.state_time = "idle", rng.uniform(2.0, 5.0)

    def hit_rect(self):
        width = max(46, int(self.drawn_width*.92))
        height = max(40, int(self.drawn_height*.88))
        return pygame.Rect(int(self.x-width/2), int(self.y-height),
                           width, height)


class PetArtRenderer:
    """绘制并缓存宠物帧；同一帧键只绘制一次。"""
    S = 2
    W, H = 128, 150

    def __init__(self):
        self.frames = art.LRU(256)
        self.sprites = art.LRU(224)
        self.portraits = art.LRU(24)

    # ---------- 基础工具 ----------
    def _p(self, x, y):
        return (int(x*self.S), int(y*self.S))

    def _r(self, x, y, w, h):
        return pygame.Rect(int(x*self.S), int(y*self.S),
                           max(1, int(w*self.S)), max(1, int(h*self.S)))

    def _ell(self, surface, color, x, y, w, h):
        pygame.draw.ellipse(surface, color, self._r(x, y, w, h))

    def _circ(self, surface, color, x, y, radius):
        pygame.draw.circle(surface, color, self._p(x, y), max(1, int(radius*self.S)))

    def _rect(self, surface, color, x, y, w, h, radius=0):
        pygame.draw.rect(surface, color, self._r(x, y, w, h),
                         border_radius=max(0, int(radius*self.S)))

    def _caps(self, surface, color, a, b, width, outline=None):
        if outline:
            self._caps(surface, outline, a, b, width+2.2)
        w = max(1, int(width*self.S))
        pygame.draw.line(surface, color, self._p(*a), self._p(*b), w)
        self._circ(surface, color, a[0], a[1], width/2)
        self._circ(surface, color, b[0], b[1], width/2)

    def _eyes(self, surface, points, pal, pose, blink=0, droop=False, scale=1.0):
        closed = pose in ("sleep", "bath", "pet", "idle_blink") or blink
        for x, y in points:
            rx, ry = 3.6*scale, 4.9*scale
            if closed:
                line = art.darken(pal["eye"], .05)
                pts = [(x-rx, y-.6), (x, y+1.9*scale), (x+rx, y-.6)]
                pygame.draw.lines(surface, line, False,
                                  [self._p(px, py) for px, py in pts],
                                  max(1, int(1.6*self.S)))
            else:
                self._ell(surface, pal["eye"], x-rx, y-ry, rx*2, ry*2)
                self._circ(surface, (255, 255, 255), x-rx*.38, y-ry*.44, 1.25*scale)
                self._circ(surface, art.lighten(pal["eye"], .45), x+rx*.32, y+ry*.38, .72*scale)
                if droop:
                    pygame.draw.line(surface, art.darken(pal["eye"], .05),
                                     self._p(x-rx-1, y-ry*.2), self._p(x+rx+1, y-ry*.55),
                                     max(1, int(1.2*self.S)))

    def _mouth(self, surface, x, y, color, open_=False):
        if open_:
            self._ell(surface, (124, 66, 62), x-3.4, y-.6, 6.8, 7.2)
            self._ell(surface, (222, 132, 128), x-2.2, y+2.4, 4.4, 2.6)
        else:
            pts = [(x-4.2, y), (x-1.5, y+2.3), (x, y+.5), (x+1.5, y+2.3), (x+4.2, y)]
            pygame.draw.lines(surface, color, False, [self._p(px, py) for px, py in pts],
                              max(1, int(1.35*self.S)))

    def _cheeks(self, surface, pal, points, w=11, h=7):
        color = art.mix(pal["face"], (236, 138, 138), .34)
        for x, y in points:
            self._ell(surface, color, x-w/2, y-h/2, w, h)

    def _decorate(self, surface, rare, sick, head):
        if rare:
            for x, y, size in ((20, 48, 4.6), (109, 34, 3.8), (112, 92, 3.4)):
                art.draw_star(surface, self._p(x, y), size*self.S, (250, 208, 96))
        if sick:
            art.draw_drop(surface, self._p(head[0]+32, head[1]-16), 6.6*self.S, (144, 198, 228))

    # ---------- 物种立绘 ----------
    def _rabbit_marks(self,s,pal,head,hr):
        x,y = head
        if pal['pattern']==1:
            self._ell(s,pal['mark'],x-hr+2,y-hr*.4,hr*.70,hr*.94)
        elif pal['pattern']==2:
            self._ell(s,pal['white'],x-6,y-hr+2,12,hr*1.3)
        elif pal['pattern']==3:
            for dx,dy,r in ((-16,-14,8),(15,-16,6),(21,6,5)):
                self._circ(s,pal['mark'],x+dx,y+dy,r)
        if pal['tuft']:
            color = pal['white'] if pal['pattern']!=2 else pal['mark']
            for i in range(pal['tuft']):
                self._ell(s,color,x-8+i*4,y-hr-3-i%2*2,7,11)

    def _draw_rabbit(self, s, pal, stage, pose, frame, rare, sick):
        baby, old = stage == "幼崽", stage == "老年"
        if rare:
            art.draw_glow(s, self._p(64, 86), 52*self.S, (255, 224, 154), 32)
        if pose == "sleep":
            breath = -frame
            self._ell(s, pal["paw"], 30, 126+breath, 60, 16)
            self._blob(s, self._r(26, 96+breath, 74, 44), pal["body"])
            self._ell(s, pal["belly"], 44, 110+breath, 36, 24)
            if pal['pattern'] in (1,3):
                self._ell(s,pal['mark'],32,99+breath,21,16)
            self._caps(s, pal["body"], (72, 96+breath), (46, 82+breath), 15)
            self._caps(s, pal["ear"], (68, 96+breath), (50, 86+breath), 6)
            self._circ(s, pal["belly"], 24, 114+breath, 10)
            head = (94, 106+breath)
            self._blob(s, self._r(head[0]-26, head[1]-26, 52, 52), pal["body"])
            self._rabbit_marks(s,pal,head,26)
            self._eyes(s, [(head[0]-10, head[1]), (head[0]+10, head[1])], pal, pose, 0, scale=.9)
            self._ell(s, pal["nose"], head[0]-3.2, head[1]+8, 6.4, 4.6)
            self._cheeks(s, pal, [(head[0]-19, head[1]+7), (head[0]+19, head[1]+7)], 9, 6)
            self._decorate(s, rare, sick, head)
            return
        hop = abs(math.sin(frame/6*math.pi*2)) if pose == "walk" else 0
        bob = -hop*3.0-math.sin(frame/4*math.pi*2)*1.0
        if pose == "pet":
            bob = -1.5
        if pose == "eat":
            bob = [0, 2.5, 3.5, 2.5][frame % 4]
        head = (71, 58+bob+(2 if baby else 0))
        hr = 28+(2 if baby else 0)
        # 尾巴与后脚
        self._circ(s, pal["belly"], 28, 106+bob*.6, 10)
        self._ell(s, art.darken(pal["paw"], .06), 46+hop*1.5, 130, 23, 13)
        self._ell(s, pal["paw"], 70-hop*1.5, 131, 23, 13)
        for tx in (76, 82):
            pygame.draw.line(s, art.darken(pal["paw"], .18), self._p(tx, 136),
                             self._p(tx, 141), max(1, int(1.1*self.S)))
        # 身体
        self._blob(s, self._r(34, 70+bob, 62, 70), pal["body"])
        self._ell(s, pal["belly"], 48, 96+bob, 36, 38)
        if pal['pattern'] in (1,3):
            self._ell(s,pal['mark'],36,84+bob,19,24)
            if pal['pattern']==3:
                self._ell(s,pal['mark'],79,93+bob,13,17)
        self._ell(s, pal["paw"], 43, 104+bob, 15, 23)
        self._ell(s, pal["paw"], 73, 106+bob, 15, 21)
        # 耳朵
        ear_outline = art.darken(pal["body"], .28)
        if old or pal['ears']==1:
            self._caps(s, pal["body"], (61, 42+bob), (42, 30+bob), 15, ear_outline)
            self._caps(s, pal["body"], (82, 42+bob), (101, 30+bob), 15, ear_outline)
            self._caps(s, pal["ear"], (61, 41+bob), (46, 32+bob), 6)
            self._caps(s, pal["ear"], (82, 41+bob), (97, 32+bob), 6)
            if not old:
                self._caps(s,pal['body'],(42,30+bob),(38,57+bob),13,ear_outline)
                self._caps(s,pal['body'],(101,30+bob),(105,57+bob),13,ear_outline)
                self._caps(s,pal['ear'],(40,35+bob),(39,51+bob),5)
                self._caps(s,pal['ear'],(103,35+bob),(104,51+bob),5)
        else:
            sway = math.sin(frame/4*math.pi*2)*(1.4 if pose == "idle" else 0)
            self._caps(s, pal["body"], (61, 41+bob), (54-sway, 7+bob), 15, ear_outline)
            tip = (108+sway,53+bob) if pal['ears']==2 else (90+sway,7+bob)
            self._caps(s, pal["body"], (82, 41+bob), tip, 15, ear_outline)
            self._caps(s, pal["ear"], (61, 37+bob), (55-sway, 12+bob), 6.5)
            inner = (104+sway,50+bob) if pal['ears']==2 else (89+sway,12+bob)
            self._caps(s, pal["ear"], (82, 37+bob), inner, 6.5)
        # 头与五官
        self._blob(s, self._r(head[0]-hr, head[1]-hr, hr*2, hr*2), pal["body"])
        self._rabbit_marks(s,pal,head,hr)
        eye_dx = 9.5 if baby else 11
        self._eyes(s, [(head[0]-eye_dx, head[1]-1), (head[0]+eye_dx, head[1]-1)],
                   pal, pose, droop=sick, scale=1.05 if baby else 1.0)
        self._ell(s, pal["nose"], head[0]-3.5, head[1]+5, 7, 5)
        self._mouth(s, head[0], head[1]+10, art.darken(pal["eye"], .1),
                    open_=(pose == "eat" and frame % 2 == 1))
        if not old:
            self._rect(s, (255, 252, 244), head[0]-3.4, head[1]+15, 6.8, 5.0, 1.4)
        self._cheeks(s, pal, [(head[0]-23, head[1]+6), (head[0]+13, head[1]+6)],
                     11, 7)
        self._decorate(s, rare, sick, head)

    def _draw_sheep(self, s, pal, stage, pose, frame, rare, sick):
        old = stage == "老年"
        if rare:
            art.draw_glow(s, self._p(64, 90), 54*self.S, (255, 224, 154), 32)
        if pose == "sleep":
            breath = -frame
            self._wool_cluster(s, pal, 62, 116+breath, 30, 18)
            head = (96, 112+breath)
            self._blob(s, self._r(head[0]-18, head[1]-17, 36, 34), pal["face"])
            self._caps(s, pal["ear"], (head[0]-4, head[1]-2), (head[0]-20, head[1]+6), 9)
            self._eyes(s, [(head[0]-6, head[1]), (head[0]+8, head[1])], pal, pose, 0, scale=.85)
            self._ell(s, pal["nose"], head[0]+0.5, head[1]+8, 5.5, 4)
            self._decorate(s, rare, sick, head)
            return
        hop = abs(math.sin(frame/6*math.pi*2))*.8 if pose == "walk" else 0
        bob = -hop-math.sin(frame/4*math.pi*2)*.8
        # 四条短腿
        for i, (lx, ly) in enumerate(((50, 142), (68, 143), (84, 143), (100, 142))):
            step = math.sin(frame/6*math.pi*2+i*math.pi)*(2.2 if pose == "walk" else 0)
            self._caps(s, pal["paw"], (lx, 120+bob), (lx+step, ly), 8.0)
            self._circ(s, art.darken(pal["paw"], .22), lx+step, ly, 4.2)
        # 羊毛底座 + 云朵团
        self._ell(s, art.darken(pal["body"], .05), 27, 74+bob, 74, 52)
        self._wool_cluster(s, pal, 64, 98+bob, 35, 25)
        # 脸
        head = (79, 74+bob)
        self._blob(s, self._r(head[0]-19, head[1]-19, 38, 38), pal["face"])
        self._caps(s, pal["ear"], (head[0]-7, head[1]-1), (head[0]-23, head[1]+9), 9.5)
        self._caps(s, pal["ear"], (head[0]+15, head[1]-1), (head[0]+31, head[1]+9), 9.5)
        # 头顶卷毛
        for dx, dy, r in ((-7, -17, 9), (3, -21, 10), (13, -16, 8)):
            self._blob(s, self._r(head[0]+dx-r, head[1]+dy-r, r*2, r*2), pal["body"])
        self._eyes(s, [(head[0]-7.5, head[1]+1), (head[0]+8.5, head[1]+1)],
                   pal, pose, 0, droop=sick)
        self._ell(s, pal["nose"], head[0]+0.5, head[1]+9, 5.5, 4)
        self._mouth(s, head[0], head[1]+13, art.darken(pal["eye"], .15), open_=(pose == "eat" and frame % 2 == 1))
        self._cheeks(s, pal, [(head[0]-14, head[1]+8), (head[0]+15, head[1]+8)], 9, 6)
        if old:
            for dx, dy in ((-10, 6), (6, 7)):
                pygame.draw.line(s, art.darken(pal["face"], .25), self._p(head[0]+dx-3, head[1]+dy),
                                 self._p(head[0]+dx+3, head[1]+dy), max(1, int(self.S)))
        self._decorate(s, rare, sick, head)

    def _wool_cluster(self, s, pal, cx, cy, rx, ry):
        dark = art.darken(pal["body"], .12)
        blobs = ((-.62, -.18, .42), (-.10, -.48, .48), (.44, -.28, .44),
                 (.72, .18, .32), (.18, .30, .44), (-.34, .34, .38), (-.78, .26, .28),
                 (.48, .58, .28))
        for dx, dy, r in blobs:
            self._blob(s, self._r(cx+dx*rx-r*rx, cy+dy*ry+2-r*rx,
                                     r*rx*2, r*rx*2), dark)
        for dx, dy, r in blobs:
            self._blob(s, self._r(cx+dx*rx-r*rx, cy+dy*ry-r*rx,
                                     r*rx*2, r*rx*2), pal["body"])

    def _draw_parrot(self, s, pal, stage, pose, frame, rare, sick):
        old = stage == "老年"
        if rare:
            art.draw_glow(s, self._p(64, 84), 50*self.S, (255, 224, 154), 30)
        bob = -math.sin(frame/4*math.pi*2)*1.2
        if pose == "walk":
            bob -= abs(math.sin(frame/6*math.pi*2))*2
        if pose == "sleep":
            breath = -frame
            self._poly(s, pal["tail"], [(40, 116+breath), (26, 144), (44, 140), (54, 114+breath)])
            self._ell(s, art.lighten(pal["body"], .05), 40, 104+breath, 58, 38)
            head = (92, 108+breath)
            self._blob(s, self._r(head[0]-22, head[1]-21, 44, 42), pal["body"])
            self._caps(s, pal["accent"], (head[0]-8, head[1]-14), (head[0]-18, head[1]-26), 5)
            self._ell(s, pal["beak"], head[0]+14, head[1]-3, 17, 13)
            self._eyes(s, [(head[0]+2, head[1]-2)], pal, pose, 0, scale=.9)
            self._decorate(s, rare, sick, head)
            return
        # 尾羽、腿
        self._poly(s, pal["tail"], [(60, 104+bob), (32, 148), (47, 145), (68, 108+bob)])
        self._poly(s, art.lighten(pal["tail"], .14),
                   [(61, 107+bob), (40, 141), (48, 139), (66, 109+bob)])
        for i, lx in enumerate((58, 74)):
            step = math.sin(frame/6*math.pi*2+i*math.pi)*(2.5 if pose == "walk" else 0)
            self._caps(s, pal["paw"], (lx, 118+bob), (lx+step, 140), 5.6)
            pygame.draw.line(s, art.darken(pal["paw"], .25), self._p(lx+step-4, 140), self._p(lx+step+4, 140),
                             max(1, int(1.2*self.S)))
        # 身体、腹部、翅膀
        self._blob(s, self._r(44, 74+bob, 52, 62), pal["body"])
        self._ell(s, pal["belly"], 56, 96+bob, 30, 36)
        self._blob(s, self._r(48, 82+bob, 30, 46), pal["ear"])
        for i in range(3):
            yy = 102+i*8+bob
            pygame.draw.line(s, art.lighten(pal["ear"], .22), self._p(54+i*2, yy),
                             self._p(70+i, yy+6), max(1, int(1.4*self.S)))
        # 头与冠羽
        head = (74, 52+bob)
        self._blob(s, self._r(head[0]-26, head[1]-26, 52, 52), pal["face"])
        for i, (ax, ay, bx, by) in enumerate(((65, 34+bob, 56, 12+bob),
                                             (71, 30+bob, 68, 8+bob),
                                             (78, 32+bob, 86, 12+bob))):
            self._caps(s, pal["accent"] if i != 1 else art.lighten(pal["accent"], .12),
                       (ax, ay), (bx, by), 6, art.darken(pal["accent"], .28))
        # 喙
        self._poly(s, pal["beak"], [(92, 48+bob), (110, 53+bob), (100, 64+bob), (93, 62+bob)])
        self._ell(s, art.darken(pal["beak"], .12), 94, 60+bob, 12, 7)
        self._circ(s, art.lighten(pal["beak"], .35), 98, 52+bob, 2.4)
        # 眼、腮红
        self._circ(s, pal["belly"], head[0]+2, head[1]-2, 7)
        self._eyes(s, [(head[0]+2, head[1]-2)], pal, pose, 0, droop=sick, scale=1.0)
        self._circ(s, pal["accent"], head[0]+6, head[1]+10, 4.2)
        if old:
            self._caps(s, art.mix(pal["face"], (220, 220, 210), .4),
                       (head[0]-22, head[1]+8), (head[0]-30, head[1]+16), 4)
        self._decorate(s, rare, sick, head)

    def _draw_fox(self, s, pal, stage, pose, frame, rare, sick):
        old = stage == "老年"
        baby = stage == "幼崽"
        if rare:
            art.draw_glow(s, self._p(60, 88), 54*self.S, (255, 224, 154), 32)
        if pose == "sleep":
            breath = -frame
            self._ell(s, pal["body"], 16, 106+breath, 54, 30)
            self._circ(s, pal["belly"], 22, 104+breath, 13)
            self._ell(s, pal["paw"], 36, 128+breath, 50, 14)
            head = (94, 112+breath)
            self._blob(s, self._r(head[0]-24, head[1]-22, 48, 44), pal["body"])
            self._poly(s, pal["ear"], [(head[0]-14, head[1]-16), (head[0]-20, head[1]-32), (head[0]-4, head[1]-22)])
            self._poly(s, pal["ear"], [(head[0]+6, head[1]-18), (head[0]+16, head[1]-32), (head[0]+14, head[1]-14)])
            self._ell(s, pal["belly"], head[0]-4, head[1]+4, 28, 18)
            self._eyes(s, [(head[0]-6, head[1]-2), (head[0]+8, head[1]-2)], pal, pose, 0, scale=.9)
            self._circ(s, pal["nose"], head[0]+14, head[1]+7, 4)
            self._decorate(s, rare, sick, head)
            return
        bob = -math.sin(frame/4*math.pi*2)*1.1
        if pose == "walk":
            bob -= abs(math.sin(frame/6*math.pi*2))*2.2
        if pose == "eat":
            bob += [0, 2, 3, 2][frame % 4]
        head = (71, 56+bob+(2 if baby else 0))
        # 尾巴（蓬松，白色尾尖，从身体左下侧卷出）
        self._caps(s, pal["body"], (52, 122+bob*.3), (18, 105+bob*.3), 32)
        self._caps(s, art.lighten(pal["body"], .10), (46, 120+bob*.3), (26, 108+bob*.3), 13)
        self._circ(s, pal["belly"], 16, 102+bob*.3, 11.5)
        # 腿
        for i, lx in enumerate((54, 76)):
            step = math.sin(frame/6*math.pi*2+i*math.pi)*(2.5 if pose == "walk" else 0)
            self._caps(s, pal["paw"], (lx, 118+bob), (lx+step, 142), 7.2)
        # 身体与胸毛
        self._blob(s, self._r(34, 74+bob, 62, 66), pal["body"])
        self._ell(s, pal["belly"], 50, 92+bob, 30, 42)
        # 耳朵：三角底座藏在头顶，只露出挺立的耳尖
        for sign, tip in ((-1, (41, 13)), (1, (101, 13))):
            base_a = (head[0]+sign*4, head[1]-19)
            base_b = (head[0]+sign*25, head[1]-4)
            self._poly(s, pal["ear"], [tip, base_a, base_b])
            dark_tip = (tip[0]+sign*1.5, tip[1]+2)
            self._poly(s, art.darken(pal["ear"], .45),
                       [dark_tip, (tip[0]+(base_a[0]-tip[0])*.40, tip[1]+(base_a[1]-tip[1])*.40),
                        (tip[0]+(base_b[0]-tip[0])*.32, tip[1]+(base_b[1]-tip[1])*.32)])
            inner = art.mix(pal["body"], (255, 214, 190), .5)
            self._poly(s, inner,
                       [(tip[0]+(base_a[0]-tip[0])*.22, tip[1]+(base_a[1]-tip[1])*.22),
                        (tip[0]+(base_a[0]-tip[0])*.55, tip[1]+(base_a[1]-tip[1])*.55),
                        (tip[0]+(base_b[0]-tip[0])*.42, tip[1]+(base_b[1]-tip[1])*.42)])
        # 头、吻部
        hr = 27+(2 if baby else 0)
        self._blob(s, self._r(head[0]-hr, head[1]-hr, hr*2, hr*2), pal["body"])
        self._ell(s, pal["belly"], head[0]-16, head[1]+6, 34, 20)
        self._circ(s, pal["nose"], head[0]+4, head[1]+12, 4.4)
        self._eyes(s, [(head[0]-10, head[1]-1), (head[0]+12, head[1]-1)],
                   pal, pose, 0, droop=sick, scale=1.0)
        self._mouth(s, head[0]+4, head[1]+17, art.darken(pal["eye"], .2),
                    open_=(pose == "eat" and frame % 2 == 1))
        self._cheeks(s, pal, [(head[0]-18, head[1]+7), (head[0]+20, head[1]+7)], 10, 6)
        if old:
            self._caps(s, art.mix(pal["belly"], (222, 214, 202), .5),
                       (head[0]-6, head[1]+13), (head[0]+14, head[1]+15), 4)
        self._decorate(s, rare, sick, head)

    # ---------- 新品种：共用动画骨架，保留每种宠物的标志性轮廓 ----------
    def _draw_cat(self, *args):
        self._draw_companion(*args, kind="cat")

    def _draw_dog(self, *args):
        self._draw_companion(*args, kind="dog")

    def _draw_hamster(self, *args):
        self._draw_companion(*args, kind="hamster")

    def _draw_hedgehog(self, *args):
        self._draw_companion(*args, kind="hedgehog")

    def _draw_companion(self, s, pal, stage, pose, frame, rare, sick, kind):
        sleeping = pose == "sleep"
        step = math.sin(frame/6*math.pi*2) if pose == "walk" else 0
        bob = -abs(step)*2 if pose == "walk" else math.sin(frame/4*math.pi*2)
        if pose == "eat":
            bob += frame % 2 * 3
        hx, hy = (87, 112-frame) if sleeping else (68, 67+bob)
        hr = 28 + (3 if stage == "幼崽" else 0)
        if rare:
            art.draw_glow(s, self._p(64, 98), 50*self.S, (255, 224, 154), 28)
        if kind == "cat":
            self._caps(s, pal["body"], (40, 120), (19, 98+bob), 12)
            self._caps(s, pal["body"], (19, 98+bob), (23, 75+bob), 10)
        elif kind == "dog":
            self._caps(s, pal["body"], (36, 116), (16, 102+step*4), 15)
        elif kind == "hedgehog":
            # 短刺只画在背部外轮廓，不会覆盖眼睛或腹部。
            for i in range(12):
                angle = math.radians(90+i*20)
                cx, cy = 61, 101
                p1 = (cx+35*math.cos(angle-.14), cy+36*math.sin(angle-.14))
                tip = (cx+46*math.cos(angle), cy+45*math.sin(angle))
                p2 = (cx+35*math.cos(angle+.14), cy+36*math.sin(angle+.14))
                self._poly(s, art.darken(pal["body"], .28), [p1, tip, p2])
        self._blob(s, self._r(31, 101 if sleeping else 85+bob, 66, 36 if sleeping else 56), pal["body"])
        if not sleeping:
            self._ell(s, pal["belly"], 45, 104+bob, 40, 31)
        for i, px in enumerate((49, 81)):
            self._ell(s, pal["paw"], px-9+step*(1 if i else -1)*3, 133, 21, 11)
        for sign in (-1, 1):
            ex = hx+sign*22
            if kind == "cat":
                self._poly(s, pal["body"], [(ex-sign*12, hy-17), (ex+sign*6, hy-39), (ex+sign*13, hy-9)])
                self._poly(s, pal["ear"], [(ex-sign*3, hy-20), (ex+sign*5, hy-32), (ex+sign*8, hy-15)])
            elif kind == "dog":
                self._blob(s, self._r(ex-10, hy-19, 19, 40), art.darken(pal["body"], .18))
            else:
                self._circ(s, pal["body"], ex, hy-23, 11 if kind == "hamster" else 8)
                self._circ(s, pal["ear"], ex, hy-23, 6 if kind == "hamster" else 4)
        self._blob(s, self._r(hx-hr, hy-hr, hr*2, hr*2), pal["face"])
        self._ell(s, pal["belly"], hx-18, hy+5, 36, 21)
        if kind == "hamster":
            self._circ(s, pal["belly"], hx-22, hy+10, 12)
            self._circ(s, pal["belly"], hx+22, hy+10, 12)
            self._rect(s, (255, 252, 242), hx-4, hy+19, 8, 7, 2)
        self._eyes(s, [(hx-11, hy-1), (hx+11, hy-1)], pal, pose, droop=sick)
        self._ell(s, pal["nose"], hx-4, hy+9, 8, 6)
        self._mouth(s, hx, hy+16, pal["nose"], open_=pose == "eat" and frame % 2 == 1)
        if kind == "cat":
            for sign in (-1, 1):
                for dy in (-3, 3):
                    self._caps(s, pal["nose"], (hx+sign*15, hy+11), (hx+sign*31, hy+11+dy), 1)
            for dx in (-7, 0, 7):
                self._caps(s, art.darken(pal["body"], .23), (hx+dx, hy-25), (hx+dx, hy-18), 3)
        self._cheeks(s, pal, [(hx-21, hy+8), (hx+21, hy+8)], 9, 6)
        self._decorate(s, rare, sick, (hx, hy))

    def _draw_alpaca(self, *args):
        self._draw_grazer(*args, deer=False)

    def _draw_deer(self, *args):
        self._draw_grazer(*args, deer=True)

    def _draw_grazer(self, s, pal, stage, pose, frame, rare, sick, deer):
        sleeping = pose == "sleep"
        bob = -abs(math.sin(frame/6*math.pi*2))*2 if pose == "walk" else -math.sin(frame/4*math.pi*2)
        hx, hy = (86, 112-frame) if sleeping else (79, 53+bob+(3 if pose == "eat" else 0))
        if rare:
            art.draw_glow(s, self._p(64, 95), 53*self.S, (255, 224, 154), 28)
        self._caps(s, pal["body"], (32, 108), (19, 97+bob), 11)
        for i, px in enumerate((39, 55, 79, 94)):
            step = math.sin(frame/6*math.pi*2+i*math.pi)*3 if pose == "walk" else 0
            self._caps(s, pal["paw"], (px, 115), (px+step, 137 if sleeping else 143), 8)
        if deer:
            self._blob(s, self._r(23, 93+bob, 78, 39), pal["body"])
            for px, py in ((36,104),(49,98),(61,107),(42,116),(73,100)):
                self._ell(s, pal["belly"], px, py+bob, 6, 4)
        else:
            self._wool_cluster(s, pal, 60, 110+bob, 33, 21)
        if not sleeping:
            self._caps(s, pal["body"], (80, 106+bob), (hx, hy+7), 20 if deer else 26)
        for sign in (-1, 1):
            self._caps(s, pal["body"], (hx+sign*12, hy-10), (hx+sign*24, hy-28), 10)
            self._caps(s, pal["ear"], (hx+sign*16, hy-15), (hx+sign*23, hy-25), 4)
            if deer and stage != "幼崽" and not sleeping:
                base = (hx+sign*10, hy-17)
                self._caps(s, pal["paw"], base, (hx+sign*16, hy-42), 4)
                self._caps(s, pal["paw"], (hx+sign*14,hy-32), (hx+sign*26,hy-36), 3)
        self._blob(s, self._r(hx-22, hy-20, 44, 43), pal["face"])
        if not deer:
            for dx in (-12, 0, 12):
                self._circ(s, pal["body"], hx+dx, hy-19, 9)
        self._ell(s, pal["belly"], hx-14, hy+4, 30, 17)
        self._eyes(s, [(hx-9,hy-1),(hx+9,hy-1)], pal, pose, droop=sick, scale=.9)
        self._ell(s, pal["nose"], hx-3, hy+9, 6, 4)
        self._mouth(s,hx,hy+14,pal["nose"],open_=pose == "eat" and frame % 2 == 1)
        self._decorate(s, rare, sick, (hx,hy))

    def _draw_duck(self, *args):
        self._draw_waterbird(*args, peacock=False)

    def _draw_peacock(self, *args):
        self._draw_waterbird(*args, peacock=True)

    def _draw_waterbird(self, s, pal, stage, pose, frame, rare, sick, peacock):
        sleeping = pose == "sleep"
        step = math.sin(frame/6*math.pi*2)*3 if pose == "walk" else 0
        bob = -abs(step) if pose == "walk" else -math.sin(frame/4*math.pi*2)
        if peacock:
            for i in range(9):
                angle = math.radians(18+i*18)
                x,y = 62+49*math.cos(angle), 105-71*math.sin(angle)
                self._caps(s,pal["tail"],(62,113),(x,y),14)
                self._circ(s,pal["accent"],x,y,6)
                self._circ(s,pal["belly"],x,y,3.4)
                self._circ(s,art.darken(pal["body"],.3),x,y,1.6)
        else:
            self._poly(s,pal["body"],[(33,101+bob),(14,86+bob),(24,119+bob)])
        for i,px in enumerate((52,77)):
            foot = px+step*(1 if i else -1)
            self._caps(s,pal["beak"],(px,125),(foot,138),5)
            self._ell(s,pal["beak"],foot-9,135,20,8)
        self._blob(s,self._r(28,98 if sleeping else 87+bob,70,39 if sleeping else 48),pal["body"])
        self._ell(s,pal["belly"],52,104+bob,36,26)
        self._blob(s,self._r(33,100+bob,32,22),art.darken(pal["body"],.10))
        hx,hy = (88,113-frame) if sleeping else (82,66+bob+(2 if pose == "eat" else 0))
        self._caps(s,pal["body"],(76,106+bob),(hx,hy+7),23)
        self._blob(s,self._r(hx-23,hy-23,46,46),pal["face"])
        if peacock:
            for dx in (-8,0,8):
                self._caps(s,pal["tail"],(hx,hy-20),(hx+dx,hy-36),2)
                self._circ(s,pal["accent"],hx+dx,hy-36,3)
            self._poly(s,pal["beak"],[(hx+17,hy+2),(hx+31,hy+9),(hx+17,hy+14)])
        else:
            self._blob(s,self._r(hx+13,hy+3,28,14),pal["beak"])
        self._eyes(s,[(hx+4,hy-2)],pal,pose,droop=sick,scale=1.1)
        self._cheeks(s,pal,[(hx+8,hy+11)],8,6)
        self._decorate(s,rare,sick,(hx,hy))

    def _blob(self, surface, rect, fill):
        art.clay_blob(surface, rect, fill, rim=True, shine=44)

    def _poly(self, surface, color, points):
        pygame.draw.polygon(surface, color, [self._p(x, y) for x, y in points])

    def _bubbles(self, surface, frame):
        for i, (x, y, r) in enumerate(((32, 98, 6.5), (94, 90, 8.5), (44, 72, 5.5),
                                       (102, 112, 7.5), (28, 122, 4.8))):
            yy = y-(frame*3+i*5) % 24
            self._circ(surface, (255, 255, 255), x, yy, r)
            pygame.draw.circle(surface, (158, 203, 214), self._p(x, yy),
                               max(1, int(r*self.S)), max(1, int(self.S*.9)))
            self._circ(surface, (255, 255, 255), x-r*.32, yy-r*.32, r*.32)

    # ---------- 帧与精灵 ----------
    def _base(self, species, stage, rare, sick, pose, frame, light, appearance=0):
        surface = pygame.Surface((self.W*self.S, self.H*self.S), pygame.SRCALPHA)
        pal = palette_for(species, rare, stage, sick, appearance)
        drawer = getattr(self, "_draw_"+SPECIES_KEYS.get(species, "rabbit"))
        drawer(surface, pal, stage, pose, frame, rare, sick)
        if species!='兔子':
            pattern,body,accent=appearance//8%4,appearance//32%3,appearance//96%4
            color=art.darken(pal['body'],.28) if pattern!=2 else art.lighten(pal['body'],.38)
            if pattern:
                for x,y,r in (((46,113,9),(83,121,5)) if pattern==1 else
                              ((53,113,5),(64,119,5),(75,125,5)) if pattern==2 else
                              ((44,111,4),(52,120,4),(79,116,4),(86,126,3))):
                    self._ell(surface,color,x-r,y-r,r*2,r*1.5)
            if accent:
                x,y=(57,123) if pose=='sleep' else (69,96)
                c=pal.get('accent',(217,158,102))
                if accent==1:
                    self._poly(surface,c,[(x-10,y-5),(x,y),(x-10,y+5)])
                    self._poly(surface,c,[(x+10,y-5),(x,y),(x+10,y+5)])
                elif accent==2:
                    art.draw_star(surface,self._p(x,y),6*self.S,(233,181,75))
                else:
                    for dx,dy in ((-4,0),(4,0),(0,-4),(0,4)):
                        self._circ(surface,c,x+dx,y+dy,3)
                    self._circ(surface,(246,214,127),x,y,2)
            if body:
                width=round(surface.get_width()*(.92 if body==1 else 1.08))
                image=pygame.transform.smoothscale(surface,(width,surface.get_height()))
                surface.fill((0,0,0,0));surface.blit(image,((surface.get_width()-width)//2,0))
        if pose == "bath":
            self._bubbles(surface, frame)
        tint = LIGHT_TINTS.get(light, (255, 255, 255))
        if tint != (255, 255, 255):
            overlay = pygame.Surface(surface.get_size(), pygame.SRCALPHA)
            overlay.fill((*tint, 255))
            surface.blit(overlay, (0, 0), special_flags=pygame.BLEND_RGBA_MULT)
        return surface

    def frame_surface(self, species, stage, rare, sick, pose, frame, light="day", appearance=0):
        key = (species, stage, bool(rare), bool(sick), pose, frame, light, appearance)
        return self.frames.get(key, lambda: self._base(species, stage, rare, sick, pose, frame, light, appearance))

    def sprite(self, species, stage, rare, sick, pose, frame, light, height, facing=1, appearance=0):
        base_key = (species, stage, bool(rare), bool(sick), pose, frame, light, appearance)
        height = max(48, int(height)//4*4)
        width = max(36, int(height*self.W/self.H))
        key = (base_key, height, 1 if facing >= 0 else -1)
        def build():
            base = self.frame_surface(species, stage, rare, sick, pose, frame, light, appearance)
            scaled = pygame.transform.smoothscale(base, (width, height))
            def native(scale):
                image = pygame.transform.smoothscale(base,(max(1,round(width*scale)),max(1,round(height*scale))))
                return pygame.transform.flip(image,True,False) if facing<0 else image
            image = pygame.transform.flip(scaled, True, False) if facing < 0 else scaled
            return Glyph(image,native)
        return self.sprites.get(key, build)

    def perspective(self, y, top, bottom):
        t = art.clamp((y-top)/max(1, bottom-top))
        return .80+.32*t

    def draw(self, surface, actor, light="day", perspective=1.0):
        pose = actor.pose
        frames = POSE_FRAMES[pose]
        frame = int(actor.anim*POSE_SPEED.get(pose, 1.8)) % frames
        base_height = STAGE_HEIGHTS.get(actor.stage, 100)
        height = max(48, int(base_height*perspective/4*4))
        sprite = self.sprite(actor.species, actor.stage, actor.rare, actor.sick,
                             pose, frame, light, height, actor.facing,
                             actor.appearance if actor.appearance>=0 else rabbit_variant(actor.pet_id))
        rect = sprite.get_rect(midbottom=(int(actor.x), int(actor.y)))
        surface.blit(sprite, rect)
        actor.drawn_height, actor.drawn_width = height, rect.width
        return rect

    def portrait(self, species, stage, rare=False, sick=False, height=232,
                 light="day", pose="idle", frame=1, appearance=0):
        key = (species, stage, bool(rare), bool(sick), height, light, pose, frame, appearance)
        return self.portraits.get(key, lambda: self.sprite(
            species, stage, rare, sick, pose, frame, light, height, 1, appearance))
