"""顶部 HUD、底部动作栏、更多菜单与庄园地图。

只负责布局、绘制和命中检测；所有玩法操作通过回调交给 interface/Game。
"""
from datetime import datetime, timezone
import pygame
import art
import game_config as C
import ui
from appearance import pet_variant
from pet_services import action_reason
from game import remaining


BARS = (("健康", "health"), ("饱食", "hunger"), ("开心", "happy"),
        ("清洁", "cleanliness"), ("好感", "favor"))
FEED_TRAY = ("普通饲料", "鲜草", "高级饲料", "零食")
MENU_PAGES = (("宠物名册", "pets"), ("加工坊", "craft"), ("邻里订单", "orders"),
              ("庄园事件", "events"), ("伙伴图鉴", "codex"), ("成长里程碑", "achievements"),
              ("庄园商店", "shop"), ("物资仓库", "inventory"), ("回忆花园", "memorial"),
              ("农场卫生 / 管理员", "sanitation"), ("小镇经营总览", "town"), ("小镇全景地图", "map"),
              ("道路与建筑建设 B", "build"),
              ("永久日志", "logs"), ("慢时光指南", "help"), ("今日庄园", "today"),
              ("通知中心 N", "notifications"), ("显示与字号 F10", "settings"),
              ('批量照护','care'),('经营与关注','dashboard'),('自由装饰 D','decorations'))
STATUS_COLORS = {"正常": ui.GREEN, "生病": ui.RED, "受伤": ui.RED, "老年": ui.GOLD, "寄养中": (96, 132, 172)}


def _day_of(game):
    return int((game.now-game.meta["start_ts"])//C.DAY)+1


def _clock(now):
    return datetime.fromtimestamp(now, timezone.utc).strftime("%H:%M")


class TopHUD:
    H = 88

    def __init__(self, fonts, on_mode, on_menu, on_today):
        self.fonts = fonts
        self.on_today = on_today
        self.mode_button = ui.Button((940, 22, 198, 44), "", on_mode, tone=ui.GREEN)
        self.menu_button = ui.Button((1150, 22, 106, 44), "更多 ···", on_menu)
        self.today_rect = pygame.Rect(656, 16, 278, 56)
        self.coins_rect = pygame.Rect(352, 18, 140, 52)
        self.rep_rect = pygame.Rect(504, 18, 140, 52)
        self.bg = art.vertical_gradient((C.WINDOW[0], self.H), (255, 252, 244), (241, 232, 214))

    def draw(self, surface, game):
        f = self.fonts
        surface.blit(self.bg, (0, 0))
        pygame.draw.line(surface, ui.LINE, (0, self.H-1), (C.WINDOW[0], self.H-1))
        pygame.draw.line(surface, (255, 255, 252), (0, 1), (C.WINDOW[0], 1))
        f.text(surface, "慢时光", (28, 12), 30, ui.GOLD)
        f.text(surface, "经营小镇", (136, 22), 17, ui.MUTED)
        f.text(surface, "宠物 · 街区 · 四季生活", (28, 52), 13, ui.MUTED)

        art.clay_panel(surface, self.coins_rect, (255, 251, 242), 14, ui.LINE, shadow_alpha=14)
        art.draw_coin(surface, (374, 44), 12)
        f.text(surface, f"{game.meta['coins']:,}", (394, 19), 20, ui.TEXT)
        f.text(surface, "金币", (394, 44), 13, ui.MUTED)

        art.clay_panel(surface, self.rep_rect, (255, 251, 242), 14, ui.LINE, shadow_alpha=14)
        art.draw_star(surface, (524, 44), 10, (228, 176, 76))
        f.text(surface, f"{game.meta['reputation']:,}", (544, 19), 20, ui.TEXT)
        f.text(surface, f"声望 · 小镇Lv{game.town.town_level}", (544, 44), 11, ui.MUTED)

        hover = self.today_rect.collidepoint(ui.pointer())
        art.clay_panel(surface, self.today_rect, (252, 247, 235) if hover else (255, 251, 242),
                       14, ui.GREEN if hover else ui.LINE, shadow_alpha=16)
        self._sun(surface, (676, 44), game.now)
        f.text(surface, f"{game.season}季 · 第{_day_of(game)}天 · {game.weather.current}", (698, 24), 16, ui.GREEN)
        f.text(surface, f"{_clock(game.now)} UTC · {len(game.people)}人在镇上", (698, 48), 12, ui.MUTED)

        self.mode_button.label = (f"加速时间 ×{C.SPEED} · 切换" if game.meta["mode"] == "fast"
                                  else "真实时间 ×1 · 切换")
        self.mode_button.draw(surface, f)
        self.menu_button.draw(surface, f)

    @staticmethod
    def _sun(surface, center, now):
        hour = (now % C.DAY)/C.HOUR
        night = hour < 5 or hour >= 19.5
        x, y = center
        if night:
            art.draw_glow(surface, center, 24, (250, 246, 214), 55)
            pygame.draw.circle(surface, (250, 246, 214), (x, y), 11)
            pygame.draw.circle(surface, (236, 230, 202), (x-4, y-3), 3)
            pygame.draw.circle(surface, (240, 234, 208), (x+4, y+3), 2)
        else:
            art.draw_glow(surface, center, 26, (255, 226, 150), 65)
            for dx, dy in ((0, -15), (0, 15), (-15, 0), (15, 0),
                           (-11, -11), (11, -11), (-11, 11), (11, 11)):
                pygame.draw.line(surface, (242, 200, 118), (x+dx*.72, y+dy*.72),
                                 (x+dx, y+dy), 2)
            pygame.draw.circle(surface, (250, 206, 110), (x, y), 11)
            pygame.draw.circle(surface, (255, 232, 170), (x-3, y-3), 6)

    def handle(self, pos):
        if self.mode_button.click(pos) or self.menu_button.click(pos):
            return True
        if self.today_rect.collidepoint(pos):
            self.on_today()
            return True
        return False


class BottomBar:
    """焦点宠物卡 + 动作组 + 食物盘 + 导航。"""

    def __init__(self, fonts, renderer, actions):
        self.fonts, self.renderer, self.actions = fonts, renderer, actions
        self.focus_rect = pygame.Rect(16, 712, 464, 140)
        self.action_rect = pygame.Rect(490, 712, 336, 140)
        self.tray_rect = pygame.Rect(834, 712, 136, 140)
        self.nav_rect = pygame.Rect(978, 712, 286, 140)
        self.action_buttons = {}
        for i, (key, label) in enumerate((("feed", "喂食"), ("bath", "洗澡"),
                                          ("play", "抚摸"), ("treat", "看病"))):
            self.action_buttons[key] = ui.Button((498+i*83, 718, 74, 60), label,
                                                 actions[key], tone=ui.GREEN)
        self.dossier_button = ui.Button((120, 798, 104, 32), "查看档案", actions["dossier"])
        self.food_slots = ((pygame.Rect(844, 736, 56, 46), FEED_TRAY[0]),
                           (pygame.Rect(906, 736, 56, 46), FEED_TRAY[1]),
                           (pygame.Rect(844, 786, 56, 46), FEED_TRAY[2]),
                           (pygame.Rect(906, 786, 56, 46), FEED_TRAY[3]))
        self.nav_buttons = {
            "map": ui.Button((986, 718, 84, 56), "小镇地图", actions["map"], tone=ui.GREEN),
            "shop": ui.Button((1078, 718, 84, 56), "商店", actions["shop"]),
            "inventory": ui.Button((1170, 718, 84, 56), "仓库", actions["inventory"]),
            "build": ui.Button((986, 780, 84, 56), "建设 B", actions.get("build")),
            "more": ui.Button((1078, 780, 176, 56), "更多", actions["more"]),
        }
        self.bg = art.vertical_gradient((C.WINDOW[0], 156), (255, 251, 242), (241, 231, 213))

    def food_at(self, pos):
        for rect, item in self.food_slots:
            if rect.collidepoint(pos):
                return item
        return None

    def draw(self, surface, game, focus):
        f = self.fonts
        surface.blit(self.bg, (0, 704))
        pygame.draw.line(surface, ui.LINE, (0, 704), (C.WINDOW[0], 704))
        pygame.draw.line(surface, (255, 255, 252), (0, 706), (C.WINDOW[0], 706))
        for rect in (self.focus_rect, self.action_rect, self.tray_rect, self.nav_rect):
            art.clay_panel(surface, rect, (255, 251, 242), 14, ui.LINE, shadow_alpha=14)
        self._draw_focus(surface, game, focus)
        self._draw_actions(surface, game, focus)
        self._draw_tray(surface, game)
        for button in self.nav_buttons.values():
            button.draw(surface, f)

    # ---------- 左：焦点宠物 ----------
    def _draw_focus(self, surface, game, focus):
        f = self.fonts
        if focus is None:
            box = pygame.Rect(34, 730, 76, 76)
            pygame.draw.ellipse(surface, (244, 235, 218), box)
            pygame.draw.ellipse(surface, (216, 202, 180), box, 2)
            f.text(surface, "？", (62, 746), 34, (190, 176, 152))
            f.text(surface, "还没有选中的伙伴", (128, 730), 19, ui.GREEN)
            f.text(surface, "点击庭院里的宠物开始互动，", (128, 758), 14, ui.MUTED)
            f.text(surface, "或打开名册挑选一位。", (128, 778), 14, ui.MUTED)
            self.dossier_button.label = "伙伴名册"
            self.dossier_button.enabled = True
            self.dossier_button.draw(surface, f)
            return

        stage = focus.stage(game.now)
        sick = focus.unwell
        box = pygame.Rect(28, 724, 84, 84)
        art.clay_panel(surface, box, (246, 238, 222), 14, (224, 211, 190), shadow_alpha=10)
        sprite = self.renderer.portrait(focus.species, stage, focus.rare, sick, height=74,appearance=pet_variant(focus.pet_id,game.meta))
        surface.blit(sprite, sprite.get_rect(midbottom=(box.centerx, box.bottom-4)))

        name = focus.name
        size = 20
        while f.get(size).size(name)[0] > 128 and size > 13:
            size -= 1
        f.text(surface, name, (120, 718), size, ui.GREEN)
        if focus.rare:
            art.draw_star(surface, (120+f.get(size).size(name)[0]+12, 729), 8, (234, 182, 82))
        f.text(surface, f"{stage} · {focus.sex} · {focus.personality}", (120, 746), 14, ui.MUTED)
        status = "寄养中" if focus.fostered else focus.status
        color = STATUS_COLORS.get(status, ui.GREEN)
        chip_w = f.get(14).size(status)[0]+18
        chip = pygame.Rect(120, 768, chip_w, 22)
        pygame.draw.rect(surface, art.lighten(color, .82), chip, border_radius=11)
        pygame.draw.rect(surface, art.lighten(color, .35), chip, 1, border_radius=11)
        f.text(surface, status, (chip.x+9, chip.y+3), 14, art.darken(color, .12))
        self.dossier_button.label = "查看档案"
        self.dossier_button.enabled = True
        self.dossier_button.draw(surface, f)

        for i, (label, attr) in enumerate(BARS):
            ui.ProgressBar((272, 716+i*23, 192, 21), label, getattr(focus, attr),
                           compact=True).draw(surface, f)

    # ---------- 中：动作组 ----------
    def _states(self, game, focus):
        inv = game.inventory
        if focus is None:
            reason = "先在庭院里点击一只伙伴"
            return {key: (False, reason) for key in self.action_buttons}
        fostered = focus.fostered
        feed_stock = any("feed" in spec and inv.count(item)
                         for item, spec in C.ITEMS.items())
        states = {
            "feed": (not fostered and feed_stock,
                     "寄养中无法喂食" if fostered else "仓库里没有食物"),
            "bath": (not fostered and inv.count("清水") > 0 and inv.count("沐浴露") > 0,
                     "寄养中无法洗澡" if fostered else "缺少清水或沐浴露"),
            "play": (not fostered and game.now >= focus.interact_ready_ts,
                     "寄养中无法互动" if fostered else "抚摸冷却中"),
            "treat": (not fostered and focus.unwell,
                      "寄养中无法看病" if fostered else "它现在很健康"),
        }
        return {key:(not action_reason(game,focus,key),action_reason(game,focus,key)) for key in states}

    def _hint(self, game, focus, states):
        if focus is None:
            return "先在庭院里点击一只伙伴，或打开名册选择。", False
        if focus.fostered:
            return "寄养中的伙伴暂停成长与互动，先接回再照顾。", True
        if focus.unwell:
            return "它生病或受伤了：点击看病查看诊断，并送入医院治疗。", True
        if focus.interact_ready_ts > game.now:
            return f"抚摸冷却中，{remaining(focus.interact_ready_ts, game.now)}后可再次互动。", False
        if not states["feed"][0]:
            return "仓库里没有食物了，去商店采购一些吧。", True
        return "小提示：把喂食盘里的食物拖到伙伴身上也能喂食。", False

    def _draw_actions(self, surface, game, focus):
        f = self.fonts
        states = self._states(game, focus)
        for key, button in self.action_buttons.items():
            button.enabled = states[key][0]
            button.draw(surface, f)
            if not button.enabled:
                for i,line in enumerate(f.wrap(states[key][1],76,10)[:2]):
                    f.text(surface,line,(button.rect.x,782+i*12),10,ui.RED)
        hint, warning = self._hint(game, focus, states)
        color = ui.GOLD if warning else ui.MUTED
        for i, line in enumerate(f.wrap(hint, 316, 12)[:2]):
            f.text(surface, line, (498, 817+i*15), 12, color)

    # ---------- 右：食物盘 ----------
    def _draw_tray(self, surface, game):
        f = self.fonts
        f.text(surface, "喂食盘", (846, 714), 13, ui.MUTED)
        mouse = ui.pointer()
        for rect, item in self.food_slots:
            count = game.inventory.count(item)
            hover = rect.collidepoint(mouse) and count > 0
            pygame.draw.rect(surface, (250, 244, 230) if count else (238, 229, 213),
                             rect, border_radius=10)
            pygame.draw.rect(surface, ui.GREEN if hover else ui.LINE, rect,
                             2 if hover else 1, border_radius=10)
            art.draw_item_icon(surface, item, (rect.x+14, rect.y+3, 28, 28),
                               alpha=255 if count else 84)
            text = f"×{count}"
            rendered = f.render(text, 11, ui.TEXT if count else ui.MUTED)
            surface.blit(rendered, (rect.right-rendered.get_width()-5, rect.bottom-15))
        f.text(surface, "拖到伙伴身上", (845, 833), 11, ui.MUTED)

    def handle(self, pos):
        for button in self.action_buttons.values():
            if button.click(pos):
                return True
        if self.dossier_button.click(pos):
            return True
        for button in self.nav_buttons.values():
            if button.click(pos):
                return True
        return False


class MoreMenu:
    """右上下拉的二级菜单，覆盖在庭院之上。"""

    def __init__(self, fonts, on_open, on_caretaker):
        self.fonts = fonts
        self.on_open = on_open
        rows = (len(MENU_PAGES)+1)//2
        footer_y = 124+rows*52+8
        self.rect = pygame.Rect(798,72,458,footer_y+42+12-72)
        self.items = []
        for i, (label, page) in enumerate(MENU_PAGES):
            col, row = i % 2, i // 2
            self.items.append((pygame.Rect(818+col*224, 124+row*52, 202, 44), label, page))
        self.caretaker_button = ui.Button((818,footer_y,418,42), "", on_caretaker, tone=ui.GOLD)

    def draw(self, surface, game):
        f = self.fonts
        mouse = ui.pointer()
        art.draw_shadow(surface, (self.rect.centerx, self.rect.bottom-6),
                        (self.rect.width-60, 36), 80, 12)
        art.clay_panel(surface, self.rect, (255, 251, 242), 18, ui.LINE, shadow_alpha=0)
        f.text(surface, "更多功能", (self.rect.x+22, self.rect.y+16), 19, ui.GREEN)
        f.text(surface, "所有庄园系统都在这里", (self.rect.x+124, self.rect.y+21), 12, ui.MUTED)
        for rect, label, _ in self.items:
            hover = rect.collidepoint(mouse)
            pygame.draw.rect(surface, ui.GREEN if hover else ui.PANEL_LIGHT, rect, border_radius=9)
            if not hover:
                pygame.draw.line(surface, (255, 255, 252), (rect.x+8, rect.y+2),
                                 (rect.right-8, rect.y+2), 1)
            rendered = f.render(label, 15, ui.PANEL if hover else ui.TEXT)
            surface.blit(rendered, rendered.get_rect(center=rect.center))
        self.caretaker_button.label = ("解雇饲养员 · 当前在职" if game.meta["caretaker"]
                                       else f"雇佣饲养员 · {C.CARE_HIRE_COST} 金币")
        self.caretaker_button.draw(surface, f)

    def handle(self, pos):
        for rect, _, page in self.items:
            if rect.collidepoint(pos):
                self.on_open(page)
                return True
        if self.caretaker_button.click(pos):
            return True
        return self.rect.collidepoint(pos)


class ManorMapOverlay(ui.Modal):
    """20 块土地的鸟瞰地图；几何独立于通用弹窗，但沿用 Modal 的关闭/返回语义。"""

    TILE = (202, 134)
    ORIGIN = (111, 156)

    def __init__(self, fonts, game, on_close, on_back, on_pick):
        super().__init__("庄园地图", fonts, on_close, on_back)
        self.game = game
        self.on_pick = on_pick
        dx,dy=(ui.canvas_size[0]-1280)//2,(ui.canvas_size[1]-860)//2
        self.rect = pygame.Rect(80+dx, 44+dy, 1120, 772)
        self.body = pygame.Rect(104+dx, 132+dy, 1072, 660)
        self.close.rect = pygame.Rect(self.rect.right-62, self.rect.y+18, 44, 36)
        if self.back:
            self.back.rect = pygame.Rect(self.rect.right-154, self.rect.y+18, 82, 36)
        self.hotspots = []
        for bid in sorted(game.buildings):
            col, row = bid % C.GRID_COLS, bid // C.GRID_COLS
            x = self.ORIGIN[0]+dx+col*(self.TILE[0]+12)
            y = self.ORIGIN[1]+dy+row*(self.TILE[1]+12)
            self.hotspots.append((pygame.Rect(x, y, *self.TILE), bid))

    def draw(self, surface):
        f = self.fonts
        surface.blit(art.shade_surface(surface.get_size(), (28, 36, 28, 150)), (0, 0))
        art.draw_shadow(surface, (self.rect.centerx, self.rect.bottom+4),
                        (self.rect.width-40, 46), 90, 12)
        art.clay_panel(surface, self.rect, ui.PANEL, 18, ui.LINE, shadow_alpha=0)
        pygame.draw.line(surface, ui.CREAM, (self.rect.x+16, self.rect.y+2),
                         (self.rect.right-16, self.rect.y+2), 2)
        f.text(surface, "庄园地图", (self.rect.x+26, self.rect.y+17), 25, ui.GREEN)
        open_plots = sum(b.unlocked for b in self.game.buildings.values())
        built = sum(bool(b.kind) for b in self.game.buildings.values())
        f.text(surface, f"共 {C.GRID_COLS*C.GRID_ROWS} 块土地 · 已开放 {open_plots} 块 · 已建造 {built} 座",
               (self.rect.x+190, self.rect.y+27), 14, ui.MUTED)
        self.close.draw(surface, f)
        if self.back:
            self.back.draw(surface, f)
        for rect, bid in self.hotspots:
            self._tile(surface, bid, rect)
        f.text(surface, "点击地块查看建造 / 解锁 / 升级；锁定的土地需要与已开放区域相邻。",
               (self.rect.x+26, self.rect.bottom-42), 14, ui.MUTED)

    def _tile(self, surface, bid, rect):
        f, g = self.fonts, self.game
        b = g.buildings[bid]
        hover = rect.collidepoint(ui.pointer())
        if not b.unlocked:
            fill = (236, 229, 215)
        elif not b.kind:
            fill = (249, 242, 227)
        else:
            fill = (255, 251, 242)
        art.clay_panel(surface, rect, fill, 14, ui.GREEN if hover else ui.LINE, shadow_alpha=10)
        f.text(surface, f"{bid+1:02}", (rect.x+12, rect.y+9), 12, ui.MUTED)
        cx = rect.centerx
        if not b.unlocked:
            lx, ly = cx, rect.y+54
            pygame.draw.rect(surface, (150, 140, 122), (lx-14, ly-2, 28, 22), border_radius=5)
            pygame.draw.arc(surface, (150, 140, 122), (lx-9, ly-16, 18, 26), 0, 3.1416, 3)
            pygame.draw.circle(surface, fill, (lx, ly+8), 3)
            self._center(surface, "待开拓", cx, rect.y+84, 16, ui.MUTED)
            self._center(surface, f"解锁 {g.unlock_price} 金币", cx, rect.y+106, 13, ui.GOLD)
        elif not b.kind:
            pygame.draw.circle(surface, (226, 214, 192), (cx, rect.y+46), 24)
            pygame.draw.circle(surface, fill, (cx, rect.y+46), 22)
            f.text(surface, "+", (cx-12, rect.y+24), 37, ui.GREEN)
            self._center(surface, "空地 · 点击建造", cx, rect.y+88, 15, ui.MUTED)
            self._center(surface, "可选 9 种建筑", cx, rect.y+109, 12, (150, 136, 116))
        else:
            cottage = pygame.Rect(rect.x+58, rect.y+8, 86, 72)
            art.draw_cottage(surface, cottage, b.kind, b.level,
                             C.BUILDINGS[b.kind]["color"], winter=g.season == "冬", night=False)
            f.text(surface, b.kind, (rect.x+12, rect.y+86), 16, ui.GREEN)
            pets = g.occupants(bid)
            detail = f"Lv{b.level}"
            if b.capacity:
                detail += f" · {g.housing_used(bid)}/{b.capacity}"
            else:
                detail += " · 经营设施"
            f.text(surface, detail, (rect.x+12, rect.y+109), 12, ui.MUTED)
            dirty = g.sanitation.quantity(bid)
            if dirty:
                f.text(surface,f"污 {dirty}",(rect.x+12,rect.y+8),12,ui.RED)
            seen = []
            for p in pets:
                if p.species not in seen:
                    seen.append(p.species)
            for i, species in enumerate(seen[:4]):
                center = (rect.right-24-i*20, rect.y+113)
                pygame.draw.circle(surface, C.SPECIES[species]["color"], center, 7)
                pygame.draw.circle(surface, (255, 255, 252), (center[0]-2, center[1]-2), 2)
                if any(p.species == species and p.rare for p in pets):
                    pygame.draw.circle(surface, ui.GOLD, center, 8, 1)
            if any(p.product_ready_ts for p in pets):
                art.draw_coin(surface, (rect.right-18, rect.y+20), 8)

    def _center(self, surface, text, cx, y, size, color):
        rendered = self.fonts.render(text, size, color)
        surface.blit(rendered, rendered.get_rect(midtop=(cx, y)))

    def handle(self, event):
        if event.type == pygame.KEYDOWN and event.key == pygame.K_ESCAPE:
            self.close.callback()
            return
        if event.type == pygame.MOUSEBUTTONDOWN and event.button == 1:
            if self.close.click(event.pos) or (self.back and self.back.click(event.pos)):
                return
            for rect, bid in self.hotspots:
                if rect.collidepoint(event.pos):
                    self.on_pick(bid)
                    return
