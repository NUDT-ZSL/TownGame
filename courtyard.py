"""沉浸庭院：季节/时段背景、小屋、食盆浴盆玩具、粒子与宠物深度排序。

场景只负责绘制与命中检测，所有玩法操作仍由 interface 调用 Game.perform。
背景按 (季节, 时段, 建筑签名) 缓存；环境粒子数量固定且不使用 game.rng。
"""
import math
import pygame
import art
import game_config as C
import ui
import pet_art
from sanitation_view import SanitationView


SCENE = pygame.Rect(0, 88, 1280, 616)
ACTIVITY = pygame.Rect(210, 552, 860, 124)
# 宠物可使用庄园南侧的完整空地；卫生设施仍沿用原活动范围。
PET_ACTIVITY = pygame.Rect(40, 510, 1200, 170)
BOWL = (318, 668)
BATH = (1004, 664)
TOY = (848, 674)
PROP_LABELS = {"bowl": "食盆 · 点击喂食", "bath": "浴盆 · 洗澡", "toy": "玩具 · 抚摸互动"}

SEASONS = {
    "春": {"sky_top": (176, 215, 228), "sky_low": (252, 236, 205), "hill_far": (186, 208, 186),
           "hill_near": (158, 192, 150), "grass": (166, 201, 133), "grass_dark": (134, 175, 108),
           "path": (232, 212, 176), "tree": (104, 158, 104), "accent": (246, 190, 196)},
    "夏": {"sky_top": (150, 209, 236), "sky_low": (250, 244, 208), "hill_far": (170, 205, 168),
           "hill_near": (134, 186, 124), "grass": (136, 188, 106), "grass_dark": (106, 158, 90),
           "path": (228, 206, 166), "tree": (84, 148, 90), "accent": (250, 220, 120)},
    "秋": {"sky_top": (178, 208, 222), "sky_low": (250, 226, 190), "hill_far": (206, 192, 148),
           "hill_near": (190, 172, 116), "grass": (192, 180, 116), "grass_dark": (158, 146, 94),
           "path": (224, 196, 158), "tree": (198, 132, 78), "accent": (236, 148, 88)},
    "冬": {"sky_top": (188, 210, 228), "sky_low": (238, 240, 246), "hill_far": (212, 220, 228),
           "hill_near": (198, 208, 216), "grass": (228, 234, 234), "grass_dark": (198, 208, 212),
           "path": (218, 218, 220), "tree": (150, 166, 162), "accent": (176, 200, 216)},
}
FLOWERS = ((92, 606, (246, 178, 190)), (138, 578, (250, 224, 140)), (206, 606, (214, 190, 240)),
           (252, 586, (246, 178, 190)), (352, 604, (250, 224, 140)), (436, 590, (246, 178, 190)),
           (486, 612, (214, 190, 240)), (560, 596, (250, 224, 140)), (726, 604, (246, 178, 190)),
           (790, 586, (250, 224, 140)), (852, 610, (214, 190, 240)), (944, 590, (246, 178, 190)),
           (1092, 606, (250, 224, 140)), (1148, 584, (214, 190, 240)))
GRASS_TUFTS = ((70, 640), (120, 620), (180, 650), (240, 632), (300, 660), (380, 626),
               (470, 650), (540, 632), (610, 660), (690, 640), (760, 622), (830, 652),
               (900, 628), (970, 660), (1040, 634), (1100, 652), (1170, 624), (1220, 606))


class CourtyardView:
    def __init__(self, renderer, rng, fonts):
        self.renderer = renderer
        self.rng = rng
        self.fonts = fonts
        self.time = 0.0
        self.backgrounds = art.LRU(3)
        self.building_hits = {}
        self.prop_hits = {}
        self.effects = []
        self.signature = ()
        self.light = "day"
        self.season = "春"
        self.feed_count = 0
        self.sanitation = SanitationView(fonts,ACTIVITY)

    # ---------- 时间与背景 ----------
    @staticmethod
    def light_key(now):
        hour = (now % C.DAY)/C.HOUR
        if 5.0 <= hour < 7.5:
            return "dawn"
        if 7.5 <= hour < 17.0:
            return "day"
        if 17.0 <= hour < 19.5:
            return "dusk"
        return "night"

    def _signature(self, game):
        return tuple(sorted((b.building_id, b.kind, b.level)
                            for b in game.buildings.values() if b.kind))

    def background(self, season, light):
        key = (season, light, self.signature)
        return self.backgrounds.get(key, lambda: self._build(season, light))

    def _build(self, season, light):
        colors = SEASONS[season]
        surface = pygame.Surface(SCENE.size)
        night = light == "night"
        surface.blit(art.vertical_gradient((1280, 330), colors["sky_top"], colors["sky_low"]), (0, 0))
        # 太阳/月亮与云
        if night:
            for i in range(18):
                x, y = 40+(i*67) % 1220, 18+(i*41) % 220
                pygame.draw.circle(surface, (226, 232, 246), (x, y), 1 if i % 3 else 2)
            art.draw_glow(surface, (1072, 118), 62, (246, 242, 214), 70)
            pygame.draw.circle(surface, (250, 246, 220), (1072, 118), 34)
            pygame.draw.circle(surface, (226, 222, 196), (1062, 110), 6)
            pygame.draw.circle(surface, (230, 226, 200), (1082, 128), 4)
        else:
            art.draw_glow(surface, (1072, 112), 96, (255, 240, 190), 80)
            pygame.draw.circle(surface, (255, 232, 168), (1072, 112), 44)
            pygame.draw.circle(surface, (255, 244, 206), (1062, 102), 22)
        for cx, cy, w, h in ((220, 128, 190, 54), (620, 90, 150, 40), (860, 176, 176, 46)):
            pygame.draw.ellipse(surface, (250, 250, 246) if not night else (206, 214, 232),
                                (cx-w//2, cy-h//2+8, w, h))
            pygame.draw.ellipse(surface, (255, 255, 252) if not night else (218, 226, 242),
                                (cx-w//2-14, cy-h//2-4, w*.55, h*.9))
            pygame.draw.ellipse(surface, (255, 255, 252) if not night else (218, 226, 242),
                                (cx-w//4, cy-h//2-12, w*.5, h))
        # 远山与近坡
        for cx, cy, w, h, color in ((220, 372, 760, 210, colors["hill_far"]),
                                    (820, 386, 900, 240, colors["hill_far"]),
                                    (1080, 398, 640, 210, colors["hill_near"]),
                                    (250, 420, 700, 220, colors["hill_near"])):
            pygame.draw.ellipse(surface, color, (cx-w//2, cy-h//2, w, h))
        # 草地
        surface.blit(art.vertical_gradient((1280, 616-330), colors["grass"], colors["grass_dark"]), (0, 330))
        # 小径
        path = colors["path"]
        edge = art.darken(path, .06)
        pygame.draw.polygon(surface, edge, [(560, 616), (722, 616), (704, 520), (672, 448),
                                            (630, 448), (626, 520)])
        pygame.draw.polygon(surface, path, [(566, 616), (716, 616), (698, 520), (666, 452),
                                            (636, 452), (632, 520)])
        pygame.draw.ellipse(surface, path, (568, 572, 148, 66))
        pygame.draw.ellipse(surface, path, (628, 438, 56, 34))
        for sx, sy, sw, sh in ((648, 594, 26, 8), (676, 560, 20, 7), (640, 528, 17, 6),
                               (694, 606, 22, 7), (612, 566, 16, 6), (656, 478, 18, 6)):
            pygame.draw.ellipse(surface, art.darken(path, .08), (sx, sy, sw, sh))
        # 草丛与花
        for x, y in GRASS_TUFTS:
            y -= 88
            pygame.draw.line(surface, colors["grass_dark"], (x, y), (x-5, y-9), 2)
            pygame.draw.line(surface, colors["grass_dark"], (x, y), (x+4, y-11), 2)
        if season != "冬":
            for x, y, color in FLOWERS:
                y -= 88
                pygame.draw.line(surface, colors["grass_dark"], (x, y), (x, y-9), 1)
                for dx, dy in ((0, -12), (4, -9), (-4, -9), (0, -7)):
                    pygame.draw.circle(surface, color, (x+dx, y+dy), 3)
                pygame.draw.circle(surface, (250, 236, 150), (x, y-9), 2)
        # 树
        self._tree(surface, 92, 500, 1.0, season, colors, light)
        self._tree(surface, 1202, 512, .82, season, colors, light)
        # 围栏
        self._fence(surface, season, colors)
        # 季节点缀
        if season == "冬":
            for x, y, w, h in ((120, 602, 150, 32), (420, 598, 190, 30), (760, 606, 160, 26),
                               (1040, 602, 180, 30), (300, 522, 110, 22)):
                pygame.draw.ellipse(surface, (244, 249, 252), (x, y, w, h))
            self._snowman(surface, 1150, 566)
        elif season == "秋":
            pygame.draw.ellipse(surface, (236, 156, 80), (1172, 576, 76, 34))
            pygame.draw.ellipse(surface, (226, 140, 70), (1212, 576, 40, 34))
            pygame.draw.line(surface, (120, 92, 58), (1202, 576), (1196, 556), 5)
        # 时段色调
        tint = {"dawn": (255, 194, 176, 24), "dusk": (255, 152, 96, 30),
                "night": (46, 60, 112, 88)}.get(light)
        if tint:
            surface.blit(art.shade_surface(SCENE.size, tint), (0, 0))
        return surface

    def _tree(self, surface, x, base, scale, season, colors, light):
        from tree_art import profile,tree_sprite
        species,variant,_=profile(f'courtyard:{x}:{base}')
        image=tree_sprite(species,variant,season,light,max(48,round(220*scale)))
        surface.blit(image,image.get_rect(midbottom=(round(x),round(base))))

    def _fence(self, surface, season, colors):
        wood = (196, 164, 120) if season != "冬" else (176, 172, 166)
        dark = art.darken(wood, .18)
        y = 440
        pygame.draw.line(surface, dark, (34, y+12), (1246, y+12), 7)
        pygame.draw.line(surface, wood, (34, y+8), (1246, y+8), 5)
        pygame.draw.line(surface, dark, (34, y+30), (1246, y+30), 6)
        pygame.draw.line(surface, wood, (34, y+26), (1246, y+26), 4)
        for x in range(46, 1250, 62):
            pygame.draw.rect(surface, dark, (x-4, y-4, 10, 42), border_radius=3)
            pygame.draw.rect(surface, wood, (x-4, y-4, 7, 40), border_radius=3)
            if season == "冬":
                pygame.draw.ellipse(surface, (246, 250, 252), (x-8, y-8, 16, 8))

    def _snowman(self, surface, x, y):
        art.draw_shadow(surface, (x, y+2), (64, 18), 45, 6)
        pygame.draw.circle(surface, (250, 252, 255), (x, y-20), 26)
        pygame.draw.circle(surface, (250, 252, 255), (x, y-54), 19)
        pygame.draw.circle(surface, (250, 252, 255), (x, y-80), 14)
        pygame.draw.circle(surface, (60, 54, 50), (x-5, y-84), 2)
        pygame.draw.circle(surface, (60, 54, 50), (x+5, y-84), 2)
        pygame.draw.polygon(surface, (238, 150, 62), [(x, y-80), (x+18, y-76), (x, y-73)])
        pygame.draw.rect(surface, (92, 106, 128), (x-14, y-98, 28, 7), border_radius=3)
        pygame.draw.rect(surface, (92, 106, 128), (x-9, y-108, 18, 12), border_radius=2)
        pygame.draw.line(surface, (210, 96, 96), (x-13, y-66), (x+13, y-66), 4)
        pygame.draw.circle(surface, (250, 252, 255), (x, y-20), 8)

    # ---------- 布局与命中 ----------
    def _layout_buildings(self, game):
        hits = {}
        for bid, b in sorted(game.buildings.items()):
            if not b.unlocked or not b.kind:
                continue
            col, row = bid % C.GRID_COLS, bid // C.GRID_COLS
            # 两排交错的远景村落：相邻排横向错开半格，避免同列小屋互相遮挡。
            scale = .74+row*.06
            w, h = int(152*scale), int(120*scale)
            x = 152+col*236+(118 if row % 2 else 0)+(row//2)*59
            base_y = 420+row*34
            hits[bid] = pygame.Rect(x-w//2, base_y-h, w, h)
        self.building_hits = hits
        return hits

    def _layout_props(self):
        self.prop_hits = {}
        for name, (x, y) in (("bowl", BOWL), ("bath", BATH), ("toy", TOY)):
            self.prop_hits[name] = pygame.Rect(x-58, y-46, 116, 58)
        return self.prop_hits

    def building_at(self, pos):
        for bid, rect in self.building_hits.items():
            if rect.collidepoint(pos):
                return bid
        return None

    def prop_at(self, pos):
        for name, rect in self.prop_hits.items():
            if rect.collidepoint(pos):
                return name
        return None

    @staticmethod
    def pet_at(pos, actors):
        # 与绘制顺序一致：基线更靠下的伙伴画在最前景，也应优先被点到。
        for actor in sorted(actors, key=lambda a: a.y, reverse=True):
            if actor.hit_rect().collidepoint(pos):
                return actor
        return None

    def hoverable(self, pos, actors):
        return bool(self.sanitation.hoverable(pos) or self.building_at(pos) or self.prop_at(pos) or self.pet_at(pos, actors))

    # ---------- 粒子与特效 ----------
    def tick(self, dt):
        self.time += dt
        self.sanitation.tick(dt)
        for effect in self.effects:
            effect["life"] -= dt
            effect["x"] += effect["vx"]*dt
            effect["y"] += effect["vy"]*dt
            effect["vy"] += effect.get("gravity", 0)*dt
        self.effects = [e for e in self.effects if e["life"] > 0][-40:]

    def _add(self, kind, x, y, vx=0, vy=-16, life=1.2, **extra):
        self.effects.append({"kind": kind, "x": x, "y": y, "vx": vx, "vy": vy,
                             "life": life, "total": life, **extra})
        self.effects = self.effects[-40:]

    def spawn_hearts(self, actor):
        top = actor.y-actor.drawn_height*.92
        for i in range(3):
            self._add("heart", actor.x+self.rng.uniform(-14, 18), top+self.rng.uniform(-6, 6),
                      self.rng.uniform(-9, 9), self.rng.uniform(-30, -18), 1.3,
                      color=(224, 118, 132) if i else (240, 186, 96), size=self.rng.uniform(14, 20))
        self._add("sparkle", actor.x, top, 0, -22, .8, color=(250, 214, 120), size=9)

    def spawn_bath(self, actor):
        top = actor.y-actor.drawn_height*.7
        for i in range(7):
            self._add("bubble", actor.x+self.rng.uniform(-26, 26), top+self.rng.uniform(-14, 30),
                      self.rng.uniform(-8, 8), self.rng.uniform(-34, -18), self.rng.uniform(.8, 1.3),
                      size=self.rng.uniform(5, 10))

    def spawn_food(self, actor):
        top = actor.y-actor.drawn_height*.8
        for i in range(4):
            self._add("crumb", actor.x+self.rng.uniform(-16, 16), top+self.rng.uniform(0, 22),
                      self.rng.uniform(-12, 12), self.rng.uniform(-26, -12), .8,
                      color=(214, 166, 96), size=self.rng.uniform(3, 5))
        self._add("heart", actor.x, top-8, 0, -26, 1.0, color=(240, 186, 96), size=16)

    # ---------- 绘制 ----------
    def draw(self, surface, game, actors, focus_id=None, drag_item=None, feed_count=0, pointer=None):
        self.season = game.season
        self.light = self.light_key(game.now)
        self.signature = self._signature(game)
        self.feed_count = feed_count
        night = self.light == "night"
        surface.blit(self.background(self.season, self.light), SCENE.topleft)
        self._layout_buildings(game)
        self._layout_props()
        mouse = ui.pointer() if pointer is None else pointer

        hover_bid = self.building_at(mouse)
        hover_prop = self.prop_at(mouse)
        hover_pet = self.pet_at(mouse, actors) if SCENE.collidepoint(mouse) else None

        # 小屋
        drawables = []
        for bid, b in sorted(game.buildings.items()):
            if bid not in self.building_hits:
                continue
            rect = self.building_hits[bid]
            drawables.append((rect.bottom, "cottage", bid, rect))
        for name, (x, y) in (("bowl", BOWL), ("bath", BATH), ("toy", TOY)):
            drawables.append((y, "prop", name, (x, y)))
        for actor in actors:
            actor.selected = actor.pet_id == focus_id
            actor.hovered = actor is hover_pet
            drawables.append((actor.y, "pet", actor, None))
        drawables.sort(key=lambda item: item[0])

        for _, kind, payload, rect in drawables:
            if kind == "cottage":
                self._draw_cottage(surface, game, payload, rect, hover_bid == payload, night)
            elif kind == "prop":
                self._draw_prop(surface, payload, rect, hover_prop == payload, drag_item)
        self.sanitation.draw_ground(surface,game)
        # 宠物脚下的光环在实体之前绘制会穿模，因此单独在实体之前补一层地面提示
        for actor in actors:
            if actor.selected:
                self._selection_ring(surface, actor)
            if drag_item and self.pet_at(mouse, actors) is actor:
                art.draw_glow(surface, (int(actor.x), int(actor.y-actor.drawn_height*.45)),
                              int(actor.drawn_height*.8), (255, 210, 120), 46)
        drawables = [d for d in drawables if d[1] == "pet"]
        manager = self.sanitation.manager_pose(game)
        if manager:
            drawables.append((manager[1],"manager",manager,None))
        for _,kind,actor,_ in sorted(drawables,key=lambda d:d[0]):
            if kind == "manager":
                self.sanitation.draw_manager(surface,actor,actors)
            else:
                self._draw_pet(surface, actor)

        self._draw_props_labels(surface, hover_prop)
        self._draw_nameplates(surface, actors, focus_id, hover_pet, hover_bid, game)
        self._draw_particles(surface, night)
        self._draw_effects(surface)
        self.sanitation.draw_sign(surface,game)
        if not actors:
            self._draw_empty(surface)
        return hover_bid or hover_prop or hover_pet

    def _draw_cottage(self, surface, game, bid, rect, hovered, night):
        building = game.buildings[bid]
        color = C.BUILDINGS[building.kind]["color"]
        if hovered:
            art.draw_glow(surface, (rect.centerx, rect.bottom-8), int(rect.width*.72),
                          (255, 224, 150), 44)
        art.draw_shadow(surface, (rect.centerx, rect.bottom-2),
                        (int(rect.width*.78), max(8, int(rect.height*.16))), 55, 6)
        art.draw_cottage(surface, rect, building.kind, building.level, color,
                         winter=self.season == "冬", night=night)
        if hovered:
            pygame.draw.rect(surface, (236, 198, 110), rect.inflate(8, 6), 2, border_radius=10)

    def _draw_prop(self, surface, name, pos, hovered, drag_item):
        x, y = pos
        if hovered:
            art.draw_glow(surface, (x, y-14), 68, (255, 224, 150), 40)
        art.draw_shadow(surface, (x, y-2), (96, 20), 55, 6)
        if name == "bowl":
            pygame.draw.ellipse(surface, (156, 96, 78), (x-46, y-22, 92, 36))
            pygame.draw.ellipse(surface, (238, 190, 158), (x-46, y-30, 92, 38))
            pygame.draw.ellipse(surface, (208, 146, 118), (x-39, y-26, 78, 26))
            pygame.draw.ellipse(surface, (248, 232, 210), (x-33, y-24, 66, 18))
            if self.feed_count:
                for i in range(6):
                    fx = x-22+(i % 3)*17
                    fy = y-16-(i//3)*7
                    pygame.draw.circle(surface, (196, 146, 82), (fx, fy), 5)
                    pygame.draw.circle(surface, (222, 174, 106), (fx-1, fy-1), 2)
                if (self.time % 2) < 1.4:
                    for i in range(2):
                        sx = x-12+i*24
                        pygame.draw.line(surface, (255, 255, 250, 120), (sx, y-36),
                                         (sx+3, y-44-(i*3)), 2)
            if hovered:
                pygame.draw.ellipse(surface, (240, 200, 120), (x-46, y-30, 92, 38), 2)
        elif name == "bath":
            art.clay_blob(surface, (x-52, y-36, 104, 48), (176, 212, 220), shine=44)
            pygame.draw.ellipse(surface, (118, 164, 178), (x-47, y-40, 94, 38))
            pygame.draw.ellipse(surface, (126, 190, 212), (x-44, y-40, 88, 32))
            pygame.draw.ellipse(surface, (188, 228, 238), (x-34, y-35, 40, 10))
            for i in range(3):
                bx = x-26+i*26
                by = y-46-((self.time*18+i*13) % 22)
                pygame.draw.circle(surface, (250, 252, 252), (int(bx), int(by)), 5+i)
                pygame.draw.circle(surface, (172, 208, 218), (int(bx), int(by)), 5+i, 1)
            if hovered:
                pygame.draw.ellipse(surface, (240, 200, 120), (x-52, y-44, 104, 48), 2)
        else:
            pygame.draw.circle(surface, (214, 108, 98), (x, y-22), 22)
            pygame.draw.circle(surface, (240, 146, 126), (x-4, y-26), 17)
            art.draw_star(surface, (x-3, y-25), 7, (255, 244, 208))
            pygame.draw.circle(surface, (176, 84, 80), (x+24, y-6), 6)
            pygame.draw.line(surface, (250, 232, 190), (x+19, y-9), (x+27, y-9), 3)
            if hovered:
                pygame.draw.circle(surface, (240, 200, 120), (x, y-22), 24, 2)

    def _draw_pet(self, surface, actor):
        scale = self.renderer.perspective(actor.y, ACTIVITY.top, ACTIVITY.bottom)
        height = pet_art.STAGE_HEIGHTS.get(actor.stage, 100)*scale
        art.draw_shadow(surface, (int(actor.x), int(actor.y+2)),
                        (max(22, int(height*.52)), max(7, int(height*.14))), 62, 6)
        self.renderer.draw(surface, actor, self.light, scale)
        if actor.state == "sleep":
            top = actor.y-actor.drawn_height-4
            art.draw_zzz(surface, (int(actor.x+actor.drawn_width*.3),
                                   int(top-((self.time*10) % 14))), 18, (122, 138, 172))
        if actor.sick:
            bx, by = int(actor.x+actor.drawn_width*.34), int(actor.y-actor.drawn_height*.86)
            pygame.draw.circle(surface, (248, 246, 240), (bx, by), 9)
            pygame.draw.rect(surface, (196, 84, 76), (bx-4, by-1, 8, 2))
            pygame.draw.rect(surface, (196, 84, 76), (bx-1, by-4, 2, 8))

    def _selection_ring(self, surface, actor):
        w = max(58, int(actor.drawn_width*1.15))
        rect = pygame.Rect(0, 0, w, max(14, int(w*.28)))
        rect.center = (int(actor.x), int(actor.y-2))
        pygame.draw.ellipse(surface, (250, 226, 160), rect)
        pygame.draw.ellipse(surface, (222, 176, 78), rect, 2)
        art.draw_glow(surface, (int(actor.x), int(actor.y-actor.drawn_height*.4)),
                      int(actor.drawn_height*.55), (255, 228, 160), 24)

    def _draw_nameplates(self, surface, actors, focus_id, hover_pet, hover_bid, game):
        for actor in actors:
            if actor.pet_id == focus_id or actor is hover_pet:
                label = f"{actor.name} · {actor.stage}"
                if actor.selected:
                    label += " · 已选中"
                self._plate(surface, int(actor.x), int(actor.y-actor.drawn_height-4), label,
                            (56, 98, 68) if actor.selected else (108, 96, 78))
        if hover_bid is not None:
            b = game.buildings[hover_bid]
            self._plate(surface, self.building_hits[hover_bid].centerx,
                        self.building_hits[hover_bid].top-10,
                        f"{b.kind} Lv{b.level} · 点击管理", (56, 98, 68))

    def _plate(self, surface, cx, bottom, text, color):
        width = self.fonts.get(14).size(text)[0]+22
        rect = pygame.Rect(0, 0, width, 26)
        rect.midbottom = (cx, bottom)
        art.clay_panel(surface, rect, (255, 251, 242), 9, (205, 190, 166),
                       shadow_alpha=26)
        self.fonts.text(surface, text, (rect.centerx-width//2+11, rect.y+5), 14, color)

    def _draw_props_labels(self, surface, hover_prop):
        if not hover_prop:
            return
        x, y = {"bowl": BOWL, "bath": BATH, "toy": TOY}[hover_prop]
        self._plate(surface, x, y-58, PROP_LABELS[hover_prop], (56, 98, 68))

    def _draw_particles(self, surface, night):
        t = self.time
        if night:
            for i in range(14):
                x = 80+(i*89) % 1120+math.sin(t*.6+i)*22
                y = 430+((i*67) % 210)+math.sin(t*1.1+i*1.7)*10
                alpha = .55+.45*math.sin(t*2+i)
                art.draw_glow(surface, (int(x), int(y)), 12, (255, 240, 150), int(60*alpha))
                pygame.draw.circle(surface, (255, 252, 200), (int(x), int(y)), 2)
            return
        if self.season == "春":
            for i in range(16):
                x = 60+(i*83+t*(9+i % 4)) % 1180
                y = 180+((i*57) % 380)+math.sin(t*.8+i)*14
                pygame.draw.ellipse(surface, (246, 190, 198), (int(x), int(y), 7, 4))
        elif self.season == "夏":
            for i in range(8):
                x = 100+(i*137+t*(16+i % 3)*.6) % 1080
                y = 360+((i*61) % 240)+math.sin(t*1.7+i)*26
                flap = 5 if int(t*6+i) % 2 else 2
                pygame.draw.ellipse(surface, (250, 226, 140), (int(x-flap), int(y-5), flap*2, 9))
                pygame.draw.ellipse(surface, (246, 170, 128), (int(x-flap), int(y+1), flap*2, 8))
                pygame.draw.circle(surface, (120, 96, 62), (int(x), int(y+2)), 1)
        elif self.season == "秋":
            for i in range(14):
                x = 60+(i*97+t*(12+i % 5)) % 1180
                y = 190+((i*71+t*24) % 400)
                pygame.draw.ellipse(surface, (226, 150, 84), (int(x), int(y), 8, 5))
                pygame.draw.line(surface, (176, 110, 62), (int(x), int(y+2)), (int(x+8), int(y+3)), 1)
        else:
            for i in range(20):
                x = 40+(i*61+t*(8+i % 3)) % 1200
                y = 150+((i*53+t*18) % 440)
                pygame.draw.circle(surface, (250, 252, 255), (int(x), int(y)), 2)

    def _draw_effects(self, surface):
        for e in self.effects:
            ratio = max(0.0, min(1.0, e["life"]/e["total"]))
            if e["kind"] == "heart":
                heart = art.heart_surface((int(e["size"]), int(e["size"])), e["color"]).copy()
                heart.set_alpha(int(255*ratio))
                surface.blit(heart, heart.get_rect(center=(int(e["x"]), int(e["y"]))))
            elif e["kind"] == "sparkle":
                color = art.mix(e["color"], (246, 240, 226), 1-ratio)
                art.draw_sparkle(surface, (int(e["x"]), int(e["y"])),
                                 e["size"]*ratio+.5, color)
            elif e["kind"] == "bubble":
                radius = max(1, int(e["size"]*ratio))
                pygame.draw.circle(surface, (250, 252, 252), (int(e["x"]), int(e["y"])), radius)
                pygame.draw.circle(surface, (164, 202, 214), (int(e["x"]), int(e["y"])), radius, 1)
            else:
                pygame.draw.circle(surface, e["color"], (int(e["x"]), int(e["y"])),
                                   max(1, int(e["size"]*ratio)))

    def _draw_empty(self, surface):
        rect = pygame.Rect(430, 430, 420, 132)
        art.clay_panel(surface, rect, (255, 251, 242), 18, (214, 198, 172))
        self.fonts.text(surface, "庭院里还没有小伙伴", (rect.x+32, rect.y+26), 23, (56, 98, 68))
        self.fonts.text(surface, "打开商店领养一只，或去寄养屋接回老朋友。",
                        (rect.x+32, rect.y+70), 16, (120, 105, 86))
