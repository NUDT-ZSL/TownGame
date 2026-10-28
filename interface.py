"""界面控制层：把玩家操作转换为 Game 命令，不直接修改实体。"""
import random
import time
import pygame
import art
import game_config as C
import ui
import hud
import courtyard
import pet_art
import town_view
from build_ui import BuildController
import town_catalog as K
from display import Display, prepare_dpi
from render_canvas import RenderCanvas
from appearance import pet_variant, rabbit_variant, style_groups, style_description
from pet_services import action_reason,attention_items
from decoration_ui import DecorationController
from game import GameError, utc_text, remaining
from db import SaveError


class ManorApp:
    def __init__(self, game, settings_path=None):
        prepare_dpi()
        pygame.init()
        pygame.display.set_caption("慢时光 · 宠物经营小镇")
        self.display = Display(settings_path)
        ui.display = self.display
        self.screen = RenderCanvas(C.WINDOW)
        self.clock = pygame.time.Clock()
        self.fonts = ui.Fonts()
        self.fonts.content_scale = self.display.font_scale
        self.game = game
        self.running = True
        self.modal = None
        self.page = None
        self.history = []
        self.toast = ""
        self.toast_until = 0
        self.last_tick = self.last_save = self.last_frame = time.monotonic()
        self.notifications = []
        self.notification_button = ui.Button((246, 22, 96, 44), '通知 N', lambda:self.open('notifications'), variant='secondary')
        # UI 专用随机数与游戏 rng 完全隔离，动画不会影响存档确定性。
        self.ui_rng = random.Random(20261027)
        self.renderer = pet_art.PetArtRenderer()
        self.courtyard = town_view.TownView(self.renderer, self.ui_rng, self.fonts)
        self.actors, self.actor_order = {}, []
        self.pet_hits, self.tile_hits = [], []
        self.menu_open = False
        self.drag = None
        self.pan = None
        self._last_pet_click = None
        self.cursor_hand = False
        self.focus_pet_id = next((p.pet_id for p in sorted(game.living, key=lambda p: p.pet_id)
                                  if not p.fostered), None)
        self.hud = hud.TopHUD(self.fonts, lambda: self.act("mode"), self.toggle_menu,
                              lambda: self.open("today"))
        self.mode_button = self.hud.mode_button
        self.more_menu = hud.MoreMenu(self.fonts, self._open_menu_page, self._toggle_caretaker)
        self.bottom = hud.BottomBar(self.fonts, self.renderer, {
            "feed": self._focus_feed,
            "bath": lambda: self._focus_care("bath"),
            "play": lambda: self._focus_care("play"),
            "treat": lambda: self._focus_care("treat"),
            "dossier": self._focus_dossier,
            "map": lambda: self.open("map"),
            "shop": lambda: self.open("shop"),
            "inventory": lambda: self.open("inventory"),
            "more": self.toggle_menu,
            'build':lambda:self.builder.enter(),
        })
        self.builder=BuildController(self)
        self.decorator=DecorationController(self)
        self.chrome=RenderCanvas(C.WINDOW)
        self._layout_size=None
        self._configure_layout()
        self._sync_actors()
        if game.offline_seconds >= 60:
            self.notify("离线结算完成：庄园流逝了"+remaining(game.offline_seconds, 0))
        if game.db.recovery_message:
            self.notify('存档恢复通知：'+game.db.recovery_message)

    def notify(self, text):
        self.toast, self.toast_until = str(text), time.monotonic()+6
        if not self.notifications or self.notifications[-1]['text'] != str(text):
            self.notifications.append({'text':str(text), 'ts':self.game.now, 'read':False})
            self.notifications = self.notifications[-100:]

    def _save_display(self):
        if not self.display.save():
            self.notify('显示设置暂时无法保存，请检查目录权限。')

    def toggle_fullscreen(self):
        self.display.fullscreen = not self.display.fullscreen
        self.display.set_mode()
        self._configure_layout()
        self._save_display()
        if self.page and self.page[0] == 'settings':
            self.refresh()

    def set_font_scale(self, scale):
        self.display.font_scale = self.fonts.content_scale = scale
        self._save_display()
        self.refresh()

    def set_window_size(self, size):
        self.display.fullscreen = False
        self.display.resize(size)
        self._configure_layout()
        self._save_display()
        self.refresh()

    def _configure_layout(self):
        size=self.display.logical_size
        if size==self._layout_size:return
        self._layout_size=size;ui.canvas_size=size
        self.screen=RenderCanvas(size)
        self.courtyard.layout(size)
        self.pan=None;self.drag=None
        if self.page:self.refresh()

    def bottom_pos(self,pos):
        x=(self.screen.get_width()-1280)//2;y=self.screen.get_height()-860
        return (pos[0]-x,pos[1]-y) if pygame.Rect(x,704+y,1280,156).collidepoint(pos) else (-10000,-10000)

    def act(self, action, **kwargs):
        try:
            self.notify(self.game.perform(action, **kwargs))
            self.refresh()
            return True
        except (GameError, SaveError) as exc:
            self.notify(str(exc))
            self.refresh()
            return False

    # ---------- 庭院伙伴 ----------
    def _sync_actors(self):
        """最多展示 5 只未寄养伙伴；焦点优先，其余保持稳定顺序。"""
        living = sorted((p for p in self.game.living if not p.fostered), key=lambda p: p.pet_id)
        living.sort(key=lambda p: p.pet_id != self.focus_pet_id)
        visible = living[:5]
        for pid in list(self.actors):
            p = self.game.pets.get(pid)
            if not p or not p.active or p.fostered:
                del self.actors[pid]
        order = []
        for p in visible:
            actor = self.actors.get(p.pet_id)
            if actor is None:
                actor = pet_art.PetActor(p.pet_id, p.name, p.species)
                actor.x = self.ui_rng.uniform(courtyard.ACTIVITY.left+50, courtyard.ACTIVITY.right-50)
                actor.y = self.ui_rng.uniform(courtyard.ACTIVITY.top+30, courtyard.ACTIVITY.bottom-10)
                self.actors[p.pet_id] = actor
            actor.sync_from_pet(p, self.game.now)
            actor.appearance = pet_variant(p.pet_id,self.game.meta)
            order.append(actor)
        self.actor_order = order
        self.pet_hits = [(self.courtyard.local_rect(a.hit_rect()), a.pet_id) for a in order]

    def _focus_pet(self):
        p = self.game.pets.get(self.focus_pet_id) if self.focus_pet_id else None
        return p if p and p.active else None

    def _focus_or_first(self):
        p = self._focus_pet()
        if p:
            return p
        for q in sorted(self.game.living, key=lambda q: q.pet_id):
            self.focus_pet_id = q.pet_id
            self._sync_actors()
            return q
        return None

    def toggle_menu(self):
        self.drag = None
        self.pan = None
        self.menu_open = not self.menu_open

    def _open_menu_page(self, page):
        self.menu_open = False
        self.open(page)

    def _toggle_caretaker(self):
        self.menu_open = False
        self.act("caretaker")

    def _focus_feed(self):
        p = self._focus_or_first()
        if not p:
            self.notify("庭院里还没有伙伴，去商店领养一只吧")
            return
        if p.fostered:
            self.notify("寄养中的伙伴需要先接回，才能喂食")
            return
        self.open("feed", {"pet_id": p.pet_id})

    def _focus_care(self, action):
        p = self._focus_or_first()
        if not p:
            self.notify("先在庭院里点击一只伙伴")
            return
        if p.fostered:
            self.notify("寄养中的伙伴需要先接回，才能互动")
            return
        actor = self.actors.get(p.pet_id)
        if action == "play":
            if self.game.now < p.interact_ready_ts:
                self.notify(f"{p.name}正在休息，{remaining(p.interact_ready_ts, self.game.now)}后可再互动")
                return
            if actor:
                actor.react("pet", 1.8)
                self.courtyard.spawn_hearts(actor)
        elif action == "bath":
            if not (self.game.inventory.count("清水") and self.game.inventory.count("沐浴露")):
                self.notify("洗澡需要清水和沐浴露，去商店采购吧")
                return
            if actor:
                actor.react("bath", 2.0)
                self.courtyard.spawn_bath(actor)
        elif action == "treat":
            if not p.unwell:
                self.notify("它现在很健康，不需要药品")
                return
            self.open("medical", {"pet_id":p.pet_id})
            return
        self.act(action, pet_id=p.pet_id)

    def _focus_dossier(self):
        p = self._focus_pet()
        if p:
            self.open("pet", {"pet_id": p.pet_id})
        else:
            self.open("pets")

    def _click_pet(self, actor):
        now = time.monotonic()
        pid = actor.pet_id
        quick = (self._last_pet_click and self._last_pet_click[0] == pid
                 and now-self._last_pet_click[1] < .38)
        self.focus_pet_id = pid
        self._sync_actors()
        if quick:
            self._last_pet_click = None
            self.open("pet", {"pet_id": pid})
            return
        self._last_pet_click = (pid, now)
        p = self.game.pets.get(pid)
        if not p or not p.active:
            return
        if p.fostered:
            self.notify("先把它从寄养屋接回来吧")
            return
        if self.game.now >= p.interact_ready_ts:
            actor.react("pet", 1.8)
            self.courtyard.spawn_hearts(actor)
            self.act("play", pet_id=pid)
        else:
            actor.react("pet", 1.0)
            self.notify(f"{p.name}正在休息，{remaining(p.interact_ready_ts, self.game.now)}后可再互动")

    def _click_prop(self, name):
        p = self._focus_or_first()
        if not p:
            self.notify("庭院里还没有伙伴，去商店领养一只吧")
            return
        self.focus_pet_id = p.pet_id
        self._sync_actors()
        if name == "bowl":
            self.open("feed", {"pet_id": p.pet_id})
        elif name == "bath":
            self._focus_care("bath")
        else:
            self._focus_care("play")

    def _handle_click(self, pos):
        if self.hud.handle(pos):
            return True
        item = self.bottom.food_at(self.bottom_pos(pos))
        if item is not None:
            if self.game.inventory.count(item) <= 0:
                self.notify(f"喂食盘里没有{item}了")
            elif not self.game.living:
                self.notify("庭院里还没有伙伴，去商店领养一只吧")
            else:
                self.drag = {"item": item, "pos": pos}
            return True
        if self.bottom.handle(self.bottom_pos(pos)):
            return True
        if not self.courtyard.viewport.collidepoint(pos):return False
        if self.courtyard.fixed_click(pos):
            return True
        if self.courtyard.legend_rect.collidepoint(pos):
            self.open("town")
            return True
        if self.courtyard.vehicle_card and pygame.Rect(24,165,340,142).collidepoint(pos):
            self.courtyard.vehicle_card=None
            return True
        vehicle_id=self.courtyard.vehicle_at(pos)
        if vehicle_id:
            self.courtyard.vehicle_card=vehicle_id
            return True
        hygiene = self.courtyard.sanitation
        if hygiene.sign_rect.collidepoint(pos):
            self.open("sanitation")
            return True
        actor = self.courtyard.pet_at(pos, self.actor_order)
        if actor:
            self._click_pet(actor)
            return True
        waste_id = hygiene.waste_at(pos)
        if waste_id:
            self.open("sanitation",{"waste_id":waste_id})
            return True
        if hygiene.manager_rect and hygiene.manager_rect.collidepoint(pos):
            self.open("sanitation")
            return True
        name = self.courtyard.prop_at(pos)
        if name:
            self._click_prop(name)
            return True
        bid = self.courtyard.building_at(pos)
        if bid is not None:
            self.open("building", {"building_id": bid})
            return True
        town_bid=self.courtyard.town_building_at(pos)
        if town_bid:
            self.open('town_building',{'building_id':town_bid})
            return True
        person_id = self.courtyard.person_at(pos)
        if person_id:
            self.open("person",{"person_id":person_id})
            return True
        town_bid=self.courtyard.town_building_at(pos)
        if town_bid:
            self.open('town_building',{'building_id':town_bid})
            return True
        zone_id = self.courtyard.zone_at(pos)
        if zone_id:
            self.open("zone",{"zone_id":zone_id})
            return True
        return False

    def _cancel_drag(self):
        self.drag = None

    def _drop_drag(self, pos):
        item = self.drag["item"]
        self.drag = None
        actor = self.courtyard.pet_at(pos, self.actor_order)
        if not actor:
            return
        p = self.game.pets.get(actor.pet_id)
        if not p or not p.active:
            return
        self.focus_pet_id = actor.pet_id
        if p.fostered:
            self.notify("寄养中的伙伴暂时不能进食")
            return
        if self.game.inventory.count(item) <= 0:
            self.notify(f"仓库里没有{item}了")
            return
        if item == "腐败物":
            self._confirm(f"{item}会让{p.name}生病并扣减健康，仍要喂食吗？",
                          "feed", pet_id=actor.pet_id, item=item)
            return
        actor.react("eat", 1.4)
        self.courtyard.spawn_food(actor)
        self.act("feed", pet_id=actor.pet_id, item=item)

    def _interactive_at(self, pos):
        if self.drag:
            return True
        if self.modal and (not self.modal.is_side or self.modal.rect.collidepoint(pos)):
            if isinstance(self.modal, hud.ManorMapOverlay):
                return any(rect.collidepoint(pos) for rect, _ in self.modal.hotspots)
            return any(b.enabled and b.rect.move(self.modal.offset()).collidepoint(pos) for b in self.modal.buttons) or self.modal.close.rect.collidepoint(pos) or bool(self.modal.back and self.modal.back.rect.collidepoint(pos))
        if self.notification_button.rect.collidepoint(pos):
            return True
        if self.menu_open:
            return self.more_menu.rect.collidepoint(pos)
        if (self.hud.mode_button.rect.collidepoint(pos) or self.hud.menu_button.rect.collidepoint(pos)
                or self.hud.today_rect.collidepoint(pos)):
            return True
        buttons = list(self.bottom.action_buttons.values())
        buttons += [self.bottom.dossier_button]+list(self.bottom.nav_buttons.values())
        if any(b.enabled and b.rect.collidepoint(self.bottom_pos(pos)) for b in buttons):
            return True
        for rect, item in self.bottom.food_slots:
            if rect.collidepoint(self.bottom_pos(pos)) and self.game.inventory.count(item) > 0:
                return True
        return bool(self.courtyard.hoverable(pos, self.actor_order))

    def _update_cursor(self):
        hand = self._interactive_at(ui.pointer())
        if hand == self.cursor_hand:
            return
        self.cursor_hand = hand
        try:
            pygame.mouse.set_cursor(pygame.SYSTEM_CURSOR_HAND if hand else pygame.SYSTEM_CURSOR_ARROW)
        except (pygame.error, AttributeError):
            pass

    def open(self, key, args=None, push=True):
        if key=='decorations':
            self.decorator.enter()
            return
        if key=='build':
            self.builder.enter()
            return
        self.pan = None
        self.drag = None
        self.menu_open = False
        if push and self.page and not (self.modal.is_side and key in ('pet','building','town_building','notifications')):
            self.history.append((self.page, self.modal.scroll if self.modal else 0))
        self.page = (key, args or {})
        self._build_modal()

    def close_modal(self):
        self.modal = self.page = None
        self.drag = None
        self.history.clear()
        pygame.key.stop_text_input()

    def back(self):
        if self.history:
            self.page, scroll = self.history.pop()
            self._build_modal()
            self.modal.scroll = min(scroll, self.modal.max_scroll)
        else:
            self.close_modal()

    def refresh(self):
        if self.page and self.page[0] != "rename":
            scroll = self.modal.scroll
            self._build_modal()
            self.modal.scroll = min(scroll, self.modal.max_scroll)
        self._sync_actors()

    def _build_modal(self):
        key, args = self.page
        if key == "manor_map":
            self.modal = hud.ManorMapOverlay(
                self.fonts, self.game, self.close_modal, self.back if self.history else None,
                lambda bid: self.open("building", {"building_id": bid}))
            return
        titles = {"pets": "伙伴名册", "pet": "宠物档案", "building": "地块与建筑", "shop": "庄园商店",
                  "inventory": "物资仓库", "craft": "加工坊", "orders": "邻里订单", "events": "庄园事件",
                  "event": "来访与意外", "codex": "伙伴图鉴", "achievements": "成长里程碑",
                  "memorial": "回忆花园", "logs": "永久庄园日志", "help": "慢时光指南", "rename": "给伙伴取名",
                  "move": "安排新住处", "breed": "选择繁育伙伴", "feed": "选择食物", "confirm": "确认操作",
                  "message": args.get("title", "提示"), "order_pet": "选择交付宠物",
                  "today": "今日小镇", "map": "小镇全景地图", "town":"小镇经营总览", "zone":"街区经营", "person":"行人档案",
                  "buy_pet": "选择伙伴性别", "medical": "宠物医院 / 诊断与治疗",
                  "sanitation":"农场卫生 / 管理员", 'settings':'显示与字号', 'notifications':'通知中心', 'appearance':'选择动物外观',
                  'care':'批量照护','dashboard':'经营与关注'}
        titles['town_building']='小镇建筑详情'
        panel = ui.SidePanel if key in ('pet','building','town_building','notifications') else ui.Modal
        self.modal = panel(titles[key], self.fonts, self.close_modal, self.back if self.history else None)
        getattr(self, "_page_"+key)(**args)
        self.modal.layout()

    def _page_settings(self):
        m = self.modal
        m.heading('窗口显示')
        m.text('大窗口会展示更多场景，按钮保持合适大小；小窗口自动缩小界面。支持系统最大化。', ui.MUTED)
        m.row([('1280 × 860',lambda:self.set_window_size((1280,860))),
               ('1600 × 1000',lambda:self.set_window_size((1600,1000))),
               ('1920 × 1080',lambda:self.set_window_size((1920,1080)))])
        m.row([('退出全屏 F11' if self.display.fullscreen else '进入全屏 F11',self.toggle_fullscreen)])
        m.heading('正文与面板按钮字号')
        m.text('放大正文后，详情会重新换行，较长内容可滚动查看。',ui.MUTED)
        m.row([(label+(' · 已选' if self.display.font_scale==scale else ''),lambda value=scale:self.set_font_scale(value))
               for label,scale in (('标准 100%',1.0),('较大 115%',1.15),('大号 130%',1.3))])
        m.heading('快捷键')
        m.text('F10 显示设置 · F11 / Alt+Enter 全屏 · N 通知中心 · Esc 关闭当前面板',ui.MUTED)

    def _page_notifications(self):
        m = self.modal
        pending = [e for e in self.game.events if e['status']=='待处理']
        m.heading(f'待处理事件 · {len(pending)}')
        if not pending:
            m.text('暂无待处理事件。',ui.MUTED)
        for entry in pending:
            m.text(entry['text'],size=16)
            m.row([('查看并选择',lambda eid=entry['event_id']:self.open('event',{'event_id':eid}))])
        m.heading('消息记录')
        if not self.notifications:
            m.text('施工完成、成就与操作结果会显示在这里。',ui.MUTED)
        for entry in reversed(self.notifications):
            entry['read'] = True
            m.text(utc_text(entry['ts'])[11:16]+'  '+entry['text'],size=16)
            m.gap(8)

    def _page_care(self):
        m,g=self.modal,self.game
        m.text('优先照顾饥饿或不洁的伙伴；已经吃饱或干净、资源不足、寄养中或条件不满足的伙伴会跳过。',ui.MUTED)
        for label,kind in (('批量收取成熟产物','collect'),('批量喂食（不使用腐败物）','feed'),('批量洗澡','bath'),('批量互动','play')):
            available=sum(not action_reason(g,p,kind,batch=True) for p in g.living)
            m.heading(label)
            m.row([(f'开始 · 当前条件可操作 {available} 只',lambda k=kind:self.act('batch_care',kind=k),available>0,ui.GREEN,'primary')])
            for p in g.living:
                reason=action_reason(g,p,kind,batch=True)
                if reason:m.text(p.name+' · '+reason,ui.MUTED,15)

    def _focus_attention(self,page,args):
        g=self.game
        if page=='pet':
            p=g.pets[args['pet_id']];self.focus_pet_id=p.pet_id;self._sync_actors()
            actor=self.actors.get(p.pet_id)
            if actor:self.courtyard.camera.center(self.courtyard.local_to_world((actor.x,actor.y)))
        elif page=='town_building':
            b=next(b for b in g.town_buildings if b['building_id']==args['building_id'])
            self.courtyard.camera.center(((b['cx']+b['w']/2)*48,(b['cy']+b['h']/2)*48))
        elif page=='zone':self.courtyard.focus_zone(args['zone_id'])
        self.open(page,args)

    def _page_dashboard(self):
        m,g=self.modal,self.game
        rows=g.meta.get('finance_ledger',[])
        today=[row for row in rows if row['ts']>=g.now-C.DAY]
        income=sum(max(0,r['delta']) for r in today);expense=-sum(min(0,r['delta']) for r in today)
        m.stats([('近24小时收入',str(income),'金币',ui.GREEN),('近24小时支出',str(expense),'金币',ui.GOLD),
                 ('净变化',f'{income-expense:+d}','金币',ui.GREEN if income>=expense else ui.RED)])
        m.heading('需要关注 · 点击定位')
        attention=attention_items(g)
        if not attention:m.notice('目前经营状态良好，暂无待处理事项。')
        for label,page,args in attention:
            m.row([(label,lambda p=page,a=args:self._focus_attention(p,a))])
        m.row([('批量收取与照护',lambda:self.open('care')),('自由装饰 D',self.decorator.enter)])
        m.heading('收支明细 · 最近100笔')
        if not rows:m.text('从当前版本开始记录收支，旧版历史不会补记。',ui.MUTED)
        for row in reversed(rows[-100:]):
            m.text(f"{utc_text(row['ts'])[5:16]}  {row['delta']:+d}金币 · 余额{row['balance']} · {row['reason']}",ui.GREEN if row['delta']>0 else ui.GOLD,16)

    def _confirm(self, text, action, **kwargs):
        self.open("confirm", {"text": text, "action": action, "params": kwargs})

    def _page_confirm(self, text, action, params):
        self.modal.text(text, ui.GOLD, 21)
        def confirm():
            if not self.act(action,**params):
                return
            self.back()
            self.refresh()
        self.modal.row([("确定并保存", confirm), ("取消", self.back)])

    def _page_message(self, title, text):
        self.modal.text(text, ui.GOLD)

    def _pet_summary(self, p):
        return f"{'[变异] ' if p.rare else ''}{p.name} · {p.species} · {p.sex} · {p.stage(self.game.now)} · {'寄养中' if p.fostered else p.status}"

    def _page_pets(self):
        m, g = self.modal, self.game
        m.row([('批量收取与照护',lambda:self.open('care')),('经营与关注',lambda:self.open('dashboard'))])
        m.text(f"当前 {len(g.living)} / {C.PET_LIMIT} 只伙伴。点击档案可喂食、互动、繁育与寄养。", ui.MUTED)
        for p in sorted(g.living, key=lambda p: (not p.unwell, p.hunger)):
            m.heading(self._pet_summary(p))
            m.text(f"{p.age_text(g.now)} | 饱食 {p.hunger:.0f} | 健康 {p.health:.0f} | 好感 {p.favor:.0f}")
            m.row([("查看档案", lambda pid=p.pet_id: self.open("pet", {"pet_id": pid})),
                   ("收取副产品", lambda pid=p.pet_id: self.act("collect", pet_id=pid), bool(p.product_ready_ts) and not p.fostered)])
        if not g.living:
            m.text("庄园暂时没有伙伴，可在商店公益领养。")

    def _page_pet(self, pet_id):
        m, g = self.modal, self.game
        p = g.pets.get(pet_id)
        if not p:
            m.text("档案不存在")
            return
        m.custom(112 if m.is_side else 150, lambda surface, rect, pet=p: self._pet_banner(surface, rect, pet))
        for label, attr in (("健康", "health"), ("饱食", "hunger"), ("开心", "happy"), ("清洁", "cleanliness"), ("好感", "favor")):
            m.bar(label, getattr(p, attr))
        if p.active:
            enabled = not p.fostered
            m.row([("喂食", lambda: self.open("feed", {"pet_id": pet_id}), not action_reason(g,p,'feed')),
                   ("洗澡 / 水+沐浴露", lambda: self.act("bath", pet_id=pet_id), not action_reason(g,p,'bath')),
                   ("抚摸互动", lambda: self.act("play", pet_id=pet_id), not action_reason(g,p,'play')),
                   ("送医 / 诊断与治疗", lambda: self.open("medical",{"pet_id":pet_id}), not action_reason(g,p,'treat'))])
            m.row([('选择外观',lambda:self.open('appearance',{'pet_id':pet_id})),
                   ('收取产物',lambda:self.act('collect',pet_id=pet_id),not action_reason(g,p,'collect'))])
        m.heading('状态与照护')
        m.text('个体外观：'+style_description(p.species,pet_variant(p.pet_id,g.meta)),ui.GREEN,16)
        actor=self.actors.get(pet_id)
        if actor:m.text('正在：'+actor.behavior_note,ui.MUTED,16)
        for label,action in (('喂食','feed'),('洗澡','bath'),('互动','play'),('收取','collect')):
            reason=action_reason(g,p,action)
            if reason:m.text(label+' · '+reason,ui.MUTED,14)
        m.text(f"状态：{p.status} | 预期寿命：{p.lifespan_max/C.DAY:g}天"
               f" | 繁育冷却：{remaining(p.breed_ready_ts, g.now)}"
               f" | 互动冷却：{remaining(p.interact_ready_ts, g.now)}", ui.MUTED)
        m.text(f"身份编号：{p.pet_id}", ui.MUTED, 15)
        if p.active:
            env = g.sanitation.exposure(p.building_id)
            m.text(f"居所粪便：{env['total']}份，其中积留{C.WASTE_UNSAFE_AFTER/C.HOUR:g}小时以上{env['stale']}份 / 环境患病风险×{env['hazard']:.1f}",ui.RED if env["stale"] else ui.MUTED,16)
        partner = g.pets.get(p.mate_id)
        if partner:
            where = "已离世" if partner.death_ts else (partner.departure or f"地块{partner.building_id+1}")
            m.text(f"伴侣：{partner.name} · {partner.species} · {partner.sex} · {where}",ui.GREEN)
            m.row([("查看伴侣档案",lambda: self.open("pet",{"pet_id":partner.pet_id})),
                   ("解除伴侣关系",lambda: self._confirm("解除伴侣关系会保留双方生平记录，孕期内不能解除。","unpair",pet_id=p.pet_id),p.active and not g.social.expecting(p))])
        else:
            m.text("伴侣：暂无；成年后可在繁育面板选择同品种异性伴侣。",ui.MUTED)
        if p.pregnant:
            father = g.pets[p.pregnancy_father_id]
            m.text(f"{p.pregnancy_label}中 / 父亲：{father.name} / 距预产期：{remaining(p.pregnancy_due_ts,g.now)}"+("（寄养中暂停）" if p.fostered else ""),ui.GOLD)
        if p.mother_id:
            m.text(f"母亲：{g.pets[p.mother_id].name} / 父亲：{g.pets[p.father_id].name}",ui.MUTED)
        if p.unwell:
            m.text(f"诊断：{p.disease or '无疾病'} / 伤势：{p.injury:.1f} / 持续影响健康，需要送医。",ui.RED)
        if p.active:
            b = g.buildings[p.building_id]
            if p.fostered:
                m.text("寄养已暂停年龄和属性衰减；接回后继续正常成长。", ui.GOLD)
            elif b.kind not in (C.SPECIES[p.species]["building"], "宠物医院", "游乐区"):
                m.text("居所不适配，属性衰减和生病风险提高。", ui.RED)
            enabled = not p.fostered
            m.row([("改名", lambda: self.open("rename", {"pet_id": pet_id})),
                   ("迁移 / 寄养 / 接回", lambda: self.open("move", {"pet_id": pet_id})),
                   (f"繁育 / {C.BREED_COST}金币+物资", lambda: self.open("breed", {"pet_id": pet_id}), enabled),
                   (f"挂牌出售 / {g.pet_price(p)}金币", lambda: self._confirm(f"将{p.name}交给新家庭，售价{g.pet_price(p)}金币。出售后无法接回，档案保留。", "sell_pet", pet_id=pet_id), enabled)])
            if p.product_ready_ts:
                product = C.SPECIES[p.species]["product"]
                m.text(f"可收取：{product}，{remaining(p.product_expiry_ts, g.now)}后消失", ui.GOLD)
                m.row([("收取副产品", lambda: self.act("collect", pet_id=pet_id), enabled)])
            else:
                m.text("成年、未生病且饱食充足时定期产出副产品。", ui.MUTED)
        elif p.death_ts:
            m.text(f"离世：{utc_text(p.death_ts)} | {p.death_reason} | 实际存活 {p.age_text(g.now)}", ui.GOLD)
        else:
            m.text("离园原因："+p.departure, ui.MUTED)
        m.heading("一生的足迹")
        for note in reversed(p.history):
            m.text(f"{utc_text(note['ts'])}  {note['text']}", ui.MUTED, 16)

    def _pet_banner(self, surface, rect, p):
        g, f = self.game, self.fonts
        stage = p.stage(g.now)
        art.clay_panel(surface, rect, (244, 235, 217), 16, (222, 208, 184), shadow_alpha=10)
        if rect.width < 600:
            sprite = self.renderer.portrait(p.species, stage, p.rare, p.unwell, height=102,appearance=pet_variant(p.pet_id,g.meta))
            surface.blit(sprite, sprite.get_rect(midbottom=(rect.right-58,rect.bottom-4)))
            size = 24
            while f.get(size).size(p.name)[0] > rect.width-150 and size>14:
                size -= 1
            f.text(surface, p.name+(' ★' if p.rare else ''), (rect.x+16,rect.y+10),size,ui.GREEN)
            f.text(surface,f'{p.species} · {p.sex} · {stage} · {p.personality}',(rect.x+16,rect.y+46),14,ui.TEXT)
            f.text(surface,f'{p.age_text(g.now)} · 地块{p.building_id+1}',(rect.x+16,rect.y+73),14,ui.MUTED)
            return
        art.draw_glow(surface, (rect.right-96, rect.centery+14), 78, (255, 238, 202), 55)
        sprite = self.renderer.portrait(p.species, stage, p.rare, p.unwell, height=136,appearance=pet_variant(p.pet_id,g.meta))
        surface.blit(sprite, sprite.get_rect(midbottom=(rect.right-96, rect.bottom-6)))
        size = 30
        while f.get(size).size(p.name)[0] > rect.width-250 and size > 18:
            size -= 1
        f.text(surface, p.name, (rect.x+24, rect.y+20), size, ui.GREEN)
        if p.rare:
            art.draw_star(surface, (rect.x+24+f.get(size).size(p.name)[0]+14, rect.y+34),
                          10, (234, 182, 82))
        f.text(surface, f"{p.species} · {p.sex} · {p.personality}", (rect.x+24, rect.y+68), 16, ui.TEXT)
        f.text(surface, f"生日 {utc_text(p.birth_ts)}", (rect.x+24, rect.y+94), 13, ui.MUTED)
        f.text(surface, f"年龄 {p.age_text(g.now)} · 居所 地块{p.building_id+1}",
               (rect.x+24, rect.y+114), 13, ui.MUTED)

    def _page_feed(self, pet_id):
        m, g = self.modal, self.game
        m.text("每次消耗一份。零食有健康负面效果，腐败物会致病。", ui.GOLD)
        for item, spec in C.ITEMS.items():
            if "feed" not in spec:
                continue
            labels = {"hunger": "饱食", "happy": "开心", "health": "健康", "favor": "好感"}
            effect = " / ".join(f"{labels[k]} {v:+g}" for k, v in spec["feed"].items())
            m.heading(f"{item}  库存 {g.inventory.count(item)}")
            m.text(effect, ui.MUTED)
            callback = (lambda it=item: self._confirm("腐败物会使宠物生病并扣18健康，仍要喂食吗？", "feed", pet_id=pet_id, item=it)) if item == "腐败物" else (lambda it=item: self.act("feed", pet_id=pet_id, item=it))
            m.row([("喂一份", callback, g.inventory.count(item) > 0)])

    def _page_rename(self, pet_id):
        m = self.modal
        m.text(f"输入1～{C.RENAME_MAX}个字符，支持中文输入法。")
        box = m.input(self.game.pets[pet_id].name)
        pygame.key.start_text_input()
        def submit():
            try:
                self.notify(self.game.perform("rename", pet_id=pet_id, name=box.text))
            except (GameError, SaveError) as exc:
                self.notify(str(exc))
                return
            pygame.key.stop_text_input()
            self.back()
        m.row([("保存名字", submit)])

    def _page_move(self, pet_id):
        m, g = self.modal, self.game
        p = g.pets[pet_id]
        m.text(f"寄养入住所需{C.FOSTER_ENTER_COST}金币，接回免费。寄养期间暂停成长与属性衰减。", ui.GOLD)
        if p.pregnant:
            m.text("孕期迁移需要2个床位：母体及未出生幼崽；寄养同时暂停孕期。",ui.GOLD)
        m.text("适配建筑："+C.SPECIES[p.species]["building"]+"；医院与游乐区通用。其他居所会增加衰减。", ui.MUTED)
        for b in g.buildings.values():
            if b.capacity and b.building_id != p.building_id:
                count = g.housing_used(b.building_id)
                m.row([(f"地块{b.building_id+1} · {b.kind} Lv{b.level} · {count}/{b.capacity}（含孕期预留）· 入住", lambda bid=b.building_id: self.act("move", pet_id=pet_id, building_id=bid), count+1+int(p.pregnant) <= b.capacity)])

    def _page_breed(self, pet_id):
        m, g = self.modal, self.game
        a = g.pets[pet_id]
        m.text(f"双方需同品种异性、成年、同住一座建筑、好感≥{C.BREED_FAVOR}、健康≥{C.BREED_MIN_HEALTH}、无病无伤且冷却结束。")
        materials = "、".join(f"{k}×{v}" for k,v in C.BREED_MATERIALS.items())
        m.text(f"每次尝试消耗{C.BREED_COST}金币、{materials}；受孕率{C.CONCEPTION_CHANCE:.0%}，失败也消耗物资。默认孕期{C.PREGNANCY_DURATION/C.HOUR:g}游戏小时，需预留幼崽床位。",ui.GOLD)
        for p in g.living:
            if p.pet_id != pet_id and p.species == a.species and p.sex != a.sex:
                m.heading(self._pet_summary(p))
                m.text(f"健康{p.health:.0f} | 好感{p.favor:.0f} | 冷却{remaining(p.breed_ready_ts,g.now)}", ui.MUTED)
                m.text(f"居所：地块{p.building_id+1} / {'同居中' if p.building_id == a.building_id else '请先迁入同一居所'}",ui.MUTED)
                m.row([("结为伴侣",lambda pid=p.pet_id:self.act("pair",first_id=pet_id,second_id=pid)),
                       ("尝试受孕（需先同居）", lambda pid=p.pet_id: self.act("breed", first_id=pet_id, second_id=pid))])

    def _page_medical(self, pet_id):
        m,g = self.modal,self.game
        p = g.pets[pet_id]
        m.heading(f"{p.name} · {p.sex} · 健康{p.health:.1f}")
        m.text(f"疾病：{p.disease or '无'} / 伤势：{p.injury:.1f}",ui.RED if p.unwell else ui.GREEN)
        if not p.active:
            m.text(f"已离世或离园：{p.death_reason or p.departure}",ui.RED)
            return
        if not p.unwell:
            m.text("目前没有疾病或伤势，无需用药；康复后可迁回日常居所。",ui.GREEN)
            m.row([("迁回居所",lambda:self.open("move",{"pet_id":pet_id}))])
            return
        plan = g.medical.plan(p)
        in_hospital = g.buildings[p.building_id].kind == "宠物医院"
        facility = f"已住院 / 医院等级{plan['level']}" if in_hospital else "尚未住院，按1级医院预估；住院后按实际等级计算"
        m.text(f"{facility} / 费用{plan['fee']}金币 + 药品1份")
        if plan["terminal"]:
            m.text(f"不可治愈疾病：只能舒缓照护，健康+{C.PALLIATIVE_HEAL}，病情仍会持续恶化并可能死亡。",ui.RED)
        else:
            m.text(f"本次治愈率{plan['chance']:.0%}；失败会损失健康，失败后有{plan['fatality']:.0%}的病危死亡概率。升级就诊医院可提高治愈率。",ui.GOLD)
        m.text(f"治疗间隔{C.TREATMENT_COOLDOWN/C.HOUR:g}游戏小时 / 剩余恢复期{remaining(p.treatment_ready_ts,g.now)} / 药品库存{g.inventory.count('药品')}",ui.MUTED)
        m.row([("送入宠物医院",lambda:self.act("hospitalize",pet_id=pet_id),p.unwell and not in_hospital and not p.fostered),
               ("舒缓照护" if plan["terminal"] else "接受治疗",lambda:self.act("treat",pet_id=pet_id),p.unwell and in_hospital and not p.fostered and p.treatment_ready_ts <= g.now)])
        m.row([("康复后迁回居所",lambda:self.open("move",{"pet_id":pet_id}))])

    def _page_building(self, building_id):
        m, g = self.modal, self.game
        b = g.buildings[building_id]
        m.heading(f"地块 {building_id+1} / 第{building_id//C.GRID_COLS+1}行 第{building_id%C.GRID_COLS+1}列")
        if not b.unlocked:
            m.text(f"这块土地尚未开放。解锁花费 {g.unlock_price} 金币，需要与已开放区域相邻。")
            m.row([("解锁地块", lambda: self.act("unlock", building_id=building_id))])
        elif not b.kind:
            m.text("选择要建造的建筑。建造后可以升级，拆除返还部分金币。", ui.MUTED)
            for kind, spec in C.BUILDINGS.items():
                capacity = f"宠物容量{spec['capacity']}" if spec["capacity"] else (f"物资容量+{C.STORAGE_PER_LEVEL}" if kind == "仓库" else "经营设施")
                m.row([(f"建造{kind} / {spec['cost']}金币 / {capacity}", lambda k=kind: self.act("build", building_id=building_id, kind=k))])
        else:
            m.text(f"{b.kind} Lv{b.level}  |  床位 {g.housing_used(building_id)}/{b.capacity}（含孕期预留）")
            m.row([(f"本地块卫生 / 粪便{g.sanitation.quantity(building_id)}份",lambda:self.open("sanitation",{"building_id":building_id}))])
            bonuses = {"饲养小屋": "适配兔子、狐狸、猫咪、小狗、仓鼠和刺猬；升级增加容量并降低衰减。", "牧场": "适配绵羊、羊驼和梅花鹿；冬季户外降温会影响健康。", "鸟舍": "适配鹦鹉、小鸭和孔雀；夏季清洁下降较快。",
                       "宠物医院": "住院后才可治疗疾病和伤势；升级提高治愈率并减免诊费，绝症仅可舒缓照护。医院内不会打架。", "仓库": "每级增加100份仓库容量。", "加工坊": "一级加工高级饲料，二级解锁特制零食。",
                       "商店": "采购物资、购买宠物蛋/幼崽、挂牌出售；等级提高售价与游客收入。", "游乐区": "通用居所，提高互动开心收益及游客收益。", "寄养屋": "所有品种通用；入住后暂停年龄与属性衰减。"}
            m.text(bonuses[b.kind], ui.MUTED)
            m.row([(f"升级 / {b.upgrade_cost}金币", lambda: self.act("upgrade", building_id=building_id), b.level < C.MAX_LEVEL),
                   ("拆除建筑", lambda: self._confirm("拆除前需清空宠物，且剩余仓库能容纳全部物资。返还基础造价的30%。", "demolish", building_id=building_id))])
            if b.kind in ("商店", "仓库", "加工坊"):
                page = {"商店": "shop", "仓库": "inventory", "加工坊": "craft"}[b.kind]
                m.row([("打开"+b.kind, lambda: self.open(page))])
            for p in g.occupants(building_id):
                m.heading(self._pet_summary(p))
                m.row([("查看完整档案", lambda pid=p.pet_id: self.open("pet", {"pet_id": pid}))])

    def _page_shop(self):
        m, g = self.modal, self.game
        m.text(f"持有 {g.meta['coins']} 金币 | 仓库 {g.inventory.used}/{g.storage_capacity}", ui.GOLD)
        m.text("新伙伴需花金币购买；先建造或升级适配居所，腾出宠物空位。", ui.MUTED)
        m.text(f"部分品种提供宠物蛋，购买后即时孵化，{C.MUTATION_CHANCE:.0%}概率变异。", ui.MUTED)
        if g.meta["discount_until"] > g.now:
            m.text("商人八折中，剩余"+remaining(g.meta["discount_until"], g.now), ui.GREEN)
        if g.meta["inflation_until"] > g.now:
            m.text("物资涨价中，剩余"+remaining(g.meta["inflation_until"], g.now), ui.RED)
        for exclusive, title in ((True, "新品种 / 购买后开始饲养"), (False, "经典伙伴")):
            m.heading(title)
            for species, spec in C.SPECIES.items():
                if bool(spec.get("purchase_only", False)) != exclusive:
                    continue
                m.custom(100, lambda surface, rect, s=species: self._draw_shop_pet(surface, rect, s))
                try:
                    home = g.find_home(species)
                    home_ok = True
                    home_text = f"地块{home+1} · {spec['building']}有空位"
                except GameError:
                    home_ok = False
                    home_text = f"请先建造或腾出{spec['building']}空位"
                m.text(home_text, ui.MUTED if home_ok else ui.RED, 16)
                price = g.pet_buy_price(species)
                can_house = home_ok and g.population_with_reservations < C.PET_LIMIT
                buttons = [(f"购买{species}幼崽 / {price}金币（选性别）", lambda s=species: self.open("buy_pet",{"species":s}), can_house and g.meta["coins"] >= price)]
                if spec.get("egg_available", True):
                    egg_price = g.pet_buy_price(species, egg=True)
                    buttons.append((f"购买宠物蛋 / {egg_price}金币（选性别）", lambda s=species: self.open("buy_pet",{"species":s,"egg":True}), can_house and g.meta["coins"] >= egg_price))
                m.row(buttons)
        m.heading("公益领养 / 经典品种")
        m.text("每游戏日可免费领养一只经典伙伴，新品种不参与公益领养和流浪赠送。", ui.MUTED)
        m.row([("公益领养 / 每游戏日一次 / 免费", lambda: self.act("adopt"))])
        m.heading("物资采购")
        for item, spec in C.ITEMS.items():
            if spec["price"] <= 0:
                continue
            price = g.item_buy_price(item)
            m.text(f"{item} · 库存{g.inventory.count(item)} · {price}金币/份 · "+(f"保鲜{spec['ttl']/C.HOUR:g}小时" if spec["ttl"] else "长期保存"))
            m.row([("购买1份", lambda it=item: self.act("buy", item=it, quantity=1)),
                   ("购买5份", lambda it=item: self.act("buy", item=it, quantity=5))])
        m.heading("挂牌出售宠物 / 即时成交")
        for p in g.living:
            m.row([(f"{p.name} · {p.stage(g.now)} · {g.pet_price(p)}金币 · 查看档案出售", lambda pid=p.pet_id: self.open("pet", {"pet_id": pid}))])

    def _draw_shop_pet(self, surface, rect, species, appearance=0):
        spec, f = C.SPECIES[species], self.fonts
        portrait = self.renderer.portrait(species, "幼崽", height=92,appearance=appearance)
        surface.blit(portrait, portrait.get_rect(midbottom=(rect.x+48, rect.bottom)))
        x = rect.x+112
        f.text(surface, f"{species} · {self.game.pet_buy_price(species)}金币", (x, rect.y+3), 22, ui.GREEN)
        f.text(surface, f"居所：{spec['building']}   标准寿命：{spec['life']/C.DAY:g}天", (x, rect.y+38), 17, ui.TEXT)
        f.text(surface, f"成年产物：{spec['product']} / 每{spec['interval']/C.HOUR:g}小时   在养：{sum(p.species == species for p in self.game.living)}只", (x, rect.y+68), 16, ui.MUTED)

    def _page_appearance(self, pet_id=None, variant=None, egg=False, species='兔子'):
        m,g = self.modal,self.game
        p = g.pets.get(pet_id) if pet_id else None
        if pet_id and (not p or not p.active):
            m.text('这只伙伴暂时不能修改外观。',ui.MUTED)
            return
        species=p.species if p else species
        value = variant if variant is not None else (pet_variant(pet_id,g.meta) if p else 0)
        coat,pattern,ears,tuft = value%8,value//8%4,value//32%3,value//96%4
        def choose(selected):
            scroll = self.modal.scroll
            self.page = ('appearance',{'pet_id':pet_id,'variant':selected,'egg':egg,'species':species})
            self._build_modal()
            self.modal.scroll = min(scroll,self.modal.max_scroll)
        def preview(surface,rect):
            pygame.draw.rect(surface,ui.PANEL_LIGHT,rect,border_radius=12)
            sprite = self.renderer.portrait(species,p.stage(g.now) if p else '幼崽',
                                            p.rare if p else False,p.unwell if p else False,
                                            height=144,appearance=value)
            surface.blit(sprite,sprite.get_rect(midbottom=(rect.x+90,rect.bottom-6)))
            self.fonts.text(surface,p.name if p else '新伙伴预览',(rect.x+200,rect.y+30),25,ui.GREEN)
            self.fonts.text(surface,style_description(species,value),(rect.x+200,rect.y+80),18,ui.TEXT)
        m.custom(154,preview)
        for label,names,step in style_groups(species):
            current=value//step%len(names)
            m.text(label,ui.GREEN,16)
            if step==1:
                m.choices(names,current,lambda i:self.renderer.portrait(species,'幼崽',height=68,appearance=value+(i-current)*step),
                          lambda i,c=current,s=step:choose(value+(i-c)*s))
            else:
                m.row([(name+(' · 已选' if i==current else ''),lambda v=value+(i-current)*step:choose(v),True,ui.GREEN,
                        'primary' if i==current else 'secondary') for i,name in enumerate(names)])
        favorites=g.meta.get('style_favorites',{}).get(species,[])
        m.row([('取消收藏' if value in favorites else '收藏此样式',lambda:self.act('favorite_style',species=species,variant=value)),
               ('恢复原样',lambda:(self.act('reset_appearance',pet_id=pet_id) and self.back()) if p else choose(0))])
        if favorites:
            m.text('收藏预设',ui.GREEN,16)
            m.choices([f'收藏 {i+1}' for i in range(len(favorites))],favorites.index(value) if value in favorites else -1,
                      lambda i:self.renderer.portrait(species,'幼崽',height=68,appearance=favorites[i]),lambda i:choose(favorites[i]))
        m.text('修改免费；只改变外观，稀有属性、健康和成长保持原有规则。保存后场景与头像同步更新。',ui.MUTED,16)
        def apply():
            if p:
                if self.act('pet_appearance',pet_id=pet_id,variant=value):
                    self.back()
            else:
                self.back()
                self.open('buy_pet',{'species':species,'egg':egg,'appearance':value},push=False)
        m.row([('保存外观' if p else '使用此样式',apply,True,ui.GREEN,'primary'),('取消',self.back)])

    def _page_buy_pet(self, species, egg=False, appearance=None):
        m,g = self.modal,self.game
        if appearance is None:
            appearance = 0
        price = g.pet_buy_price(species,egg)
        m.custom(100,lambda surface,rect:self._draw_shop_pet(surface,rect,species,appearance or 0))
        m.text('外观：'+style_description(species,appearance),ui.GREEN)
        def choose(value):
            self.page=('buy_pet',{'species':species,'egg':egg,'appearance':value})
            self._build_modal()
        m.choices(style_groups(species)[0][1],appearance%8,
                  lambda i:self.renderer.portrait(species,'幼崽',height=68,appearance=appearance-appearance%8+i),
                  lambda i:choose(appearance-appearance%8+i))
        m.row([(f'选择{species}外观',lambda:self.open('appearance',{'variant':appearance,'egg':egg,'species':species}))])
        m.text(f"选择{'孵化' if egg else '购买'}性别，每只{price}金币。购买后从零岁成长，性别永久记录在档案。",ui.GOLD)
        m.row([("购买雌性（母）",lambda:self._buy_selected(species,egg,"雌",appearance)),
               ("购买雄性（公）",lambda:self._buy_selected(species,egg,"雄",appearance))])

    def _buy_selected(self, species, egg, sex, appearance=None):
        try:
            message = self.game.perform("buy_pet",species=species,egg=egg,sex=sex,appearance=appearance)
        except (GameError,SaveError) as exc:
            self.notify(str(exc))
            return
        self.back()
        self.refresh()
        self.notify(message)

    def _page_inventory(self):
        m, g = self.modal, self.game
        m.text(f"总容量 {g.inventory.used}/{g.storage_capacity}。同类可堆叠，内部按到期批次优先消耗。", ui.GOLD)
        m.text("鲜货过期变为腐败物，再过一天消失。出售需商店，丢弃免费。", ui.MUTED)
        if g.inventory.count("粪便"):
            m.text(f"清扫所得粪便可出售，单价{C.ITEMS['粪便']['sell']}金币；入库后不会继续污染地面。",ui.GREEN)
        for item in C.ITEMS:
            count = g.inventory.count(item)
            if not count:
                continue
            batches = [b for b in g.inventory.batches if b["item"] == item]
            expiries = [b["expiry_ts"] for b in batches if b["expiry_ts"]]
            m.heading(f"{item} ×{count} / 售价{C.ITEMS[item]['sell']}金币")
            m.text("最早过期："+remaining(min(expiries), g.now) if expiries else "可长期保存", ui.RED if item == "腐败物" else ui.MUTED)
            m.row([("出售1份", lambda it=item: self.act("sell_item", item=it)),
                   ("出售全部", lambda it=item,n=count: self.act("sell_item", item=it, quantity=n)),
                   ("丢弃1份", lambda it=item: self.act("discard", item=it)),
                   ("丢弃全部", lambda it=item,n=count: self._confirm(f"丢弃{it} ×{n}，不获得收入。", "discard", item=it, quantity=n))])

    def _page_sanitation(self, waste_id=None, building_id=None):
        m,g = self.modal,self.game
        service = g.sanitation
        piles = [w for w in service.piles(building_id) if waste_id is None or w["waste_id"] == waste_id]
        quantity = sum(w["quantity"] for w in piles)
        all_quantity = service.quantity()
        stale = sum(w["quantity"] for w in g.waste if g.now-w["created_ts"] >= C.WASTE_UNSAFE_AFTER)
        m.stats([
            ('地面粪便', f'{all_quantity} 份', '待清扫或就地出售', ui.GREEN),
            ('陈旧积污', f'{stale} 份', f'积留 {C.WASTE_UNSAFE_AFTER/C.HOUR:g} 小时后影响健康', ui.RED if stale else ui.GREEN),
            ('仓库容量', f'{g.inventory.used} / {g.storage_capacity}', '收集粪便会占用仓容', ui.GOLD),
            ('已收集粪便', f"{g.inventory.count('粪便')} 份", f"出售单价 {C.ITEMS['粪便']['sell']} 金币 / 份", ui.GOLD),
        ])
        m.text(f"排泄周期 {C.EXCRETION_INTERVAL/C.HOUR:g} 游戏小时 · 陈旧积污会提高居所患病风险，并加快清洁下降。",ui.MUTED,16)
        m.heading(f"农场管理员 · {C.MANAGER_NAME}    {service.status()}")
        m.text(f"招聘 {C.MANAGER_HIRE_COST} 金币    ·    工资 {C.MANAGER_WAGE} 金币 / {C.MANAGER_WAGE_INTERVAL/C.HOUR:g} 游戏小时    ·    累计清扫 {g.meta['manager_cleaned']} 份",ui.MUTED,16)
        m.text("自动清扫并收集入库；仓库满时暂停收集，工资不足时停工。",ui.MUTED,16)
        if g.meta["farm_manager"]:
            m.text("下次发薪  "+remaining(g.meta["manager_next_wage_ts"],g.now),ui.GREEN,16)
        job = g.meta["manager_job"]
        if job:
            m.text(f"正在处理地块{job['building_id']+1}的粪便 / 剩余{max(0,int(job['end_ts']-g.now))}游戏秒",ui.GREEN)
        m.gap(6)
        m.row([("解雇农场管理员" if g.meta["farm_manager"] else f"雇佣农场管理员 · {C.MANAGER_HIRE_COST} 金币",lambda:self.act("farm_manager"), True, ui.RED if g.meta['farm_manager'] else ui.GREEN, 'secondary' if g.meta['farm_manager'] else 'primary')])
        m.heading("手动清理与销售")
        if waste_id:
            m.text("选中的粪便已经被清理。" if not piles else f"选中地块{piles[0]['building_id']+1}的粪便 {quantity}份。",ui.MUTED)
        elif building_id is not None:
            m.text(f"当前地块{building_id+1}：{quantity}份粪便。",ui.MUTED)
        else:
            m.text(f"当前范围：全场    ·    待处理 {quantity} 份",ui.MUTED,16)
        params = {"waste_id":waste_id,"building_id":building_id}
        m.row([(f"清扫收集 {quantity} 份（占仓容）",lambda:self.act("clean_waste",**params),quantity > 0,ui.GREEN,'primary'),
               (f"就地出售{quantity}份 / {quantity*C.ITEMS['粪便']['sell']}金币",lambda:self.act("sell_waste",**params),quantity > 0)])
        m.row([("出售仓库全部粪便",lambda:self.act("sell_manure"),g.inventory.count("粪便") > 0),
               ("查看全场卫生",lambda:self.open("sanitation"))])
        if not piles:
            m.notice("地面很干净，暂无需要清理的粪便。管理员可继续巡查。")
        for bid in sorted({w["building_id"] for w in piles}):
            info = service.exposure(bid)
            m.heading(f"地块{bid+1} / 粪便{info['total']}份 / 陈旧{info['stale']}份")
            m.text(f"环境患病倍率×{info['hazard']:.1f}，每游戏小时额外清洁衰减{info['dirt']:.2f}",ui.RED if info["stale"] else ui.MUTED)
        m.text("服务说明 · 管理员仅负责地面清扫；喂食和日常清洁由独立雇佣的饲养员负责，治疗需前往医院。",ui.MUTED,14)

    def _page_craft(self):
        m = self.modal
        m.text("加工坊即时加工；配方与解锁等级在 game_config.py 中配置。", ui.MUTED)
        for item, recipe in C.RECIPES.items():
            m.heading(f"{item} ×{recipe['count']}")
            m.text("原料："+"、".join(f"{k} ×{v}" for k,v in recipe["inputs"].items()))
            m.text(f"加工费{recipe['cost']}金币 | 需要加工坊 Lv{recipe['level']}", ui.MUTED)
            m.row([("开始加工", lambda it=item: self.act("craft", item=it))])

    def _page_orders(self):
        m, g = self.modal, self.game
        m.text("每游戏日刷新订单；按时交付奖励金币与声望。宠物交付会离开庄园。", ui.MUTED)
        for o in sorted(g.orders, key=lambda o: (o["status"] != "进行中", -o["created_ts"])):
            need = f"{o['species']} · {o['stage']} · 健康伙伴一只" if o["kind"] == "pet" else f"{o['item']} ×{o['quantity']}"
            m.heading(f"{o['npc']} / {o['status']}")
            m.text(f"需求：{need} | 奖励{o['reward']}金币、{o['reputation']}声望")
            if o["status"] == "进行中":
                m.text("剩余："+remaining(o["expiry_ts"], g.now), ui.GOLD)
                callback = (lambda oid=o["order_id"]: self.open("order_pet", {"order_id": oid})) if o["kind"] == "pet" else (lambda oid=o["order_id"]: self.act("order", order_id=oid))
                m.row([("选择宠物交付" if o["kind"] == "pet" else "交付物资", callback)])

    def _page_order_pet(self, order_id):
        m, g = self.modal, self.game
        o = next(o for o in g.orders if o["order_id"] == order_id)
        m.text(f"需求：{o['species']} · {o['stage']} · 未生病。交付后宠物前往新家，档案会保留。", ui.GOLD)
        candidates = [p for p in g.living if not p.fostered and p.species == o["species"] and p.stage(g.now) == o["stage"] and not p.unwell and not g.social.expecting(p)]
        for p in candidates:
            m.row([(self._pet_summary(p)+" / 交付", lambda pid=p.pet_id: self._confirm(f"确定将{g.pets[pid].name}交给{o['npc']}吗？", "order", order_id=order_id, pet_id=pid))])
        if not candidates:
            m.text("没有满足条件的宠物。")

    def _page_events(self):
        m, g = self.modal, self.game
        m.text("待处理事件在12游戏小时后自动采用第二项选项。请及时作出选择。", ui.GOLD)
        for e in reversed(g.events):
            m.heading(e["status"]+" / "+utc_text(e["created_ts"]))
            m.text(e["text"])
            if e["status"] == "待处理":
                m.row([("查看选项", lambda eid=e["event_id"]: self.open("event", {"event_id": eid}))])
        if not g.events:
            m.text("庄园暂时风平浪静。")

    def _page_event(self, event_id):
        m, g = self.modal, self.game
        e = next((e for e in g.events if e["event_id"] == event_id), None)
        if not e:
            m.text("该事件已归档。")
            return
        m.heading(e["text"])
        m.text(f"状态：{e['status']} | 剩余{remaining(e['expiry_ts'], g.now)}", ui.GOLD)
        for index, option in enumerate(e["options"]):
            m.row([(option, lambda i=index: self.act("event", event_id=event_id, option=i), e["status"] == "待处理")])

    def _page_codex(self):
        m, g = self.modal, self.game
        m.text(f"已结识 {len(g.codex)}/{len(C.SPECIES)} 个品种，离园和离世后依然保留收集记录。", ui.GOLD)
        for species, spec in C.SPECIES.items():
            if species not in g.codex:
                m.heading("？？？ / 尚未结识")
                m.text("在商店购买新品种，开始新的相伴时光。" if spec.get("purchase_only", False) else "在商店、繁育或事件中发现新的伙伴。", ui.MUTED)
                continue
            entry = g.codex[species]
            m.heading(species)
            m.text(f"标准寿命{spec['life']/C.DAY:g}天 | 居所{spec['building']} | 副产品{spec['product']}")
            m.text(f"变异：{'已发现' if entry['rare'] else '未发现'} | 寿终正寝：{'已记录' if entry['natural_death'] else '未记录'}", ui.MUTED)

    def _page_achievements(self):
        m, g = self.modal, self.game
        m.text(f"完成 {len(g.achievements)}/{len(C.ACHIEVEMENTS)} 项，达成自动发放奖励。", ui.GOLD)
        for key, (name, category, desc, coins, rep) in C.ACHIEVEMENTS.items():
            unlocked = key in g.achievements
            m.heading(f"{'已达成' if unlocked else '进行中'} / {category} / {name}")
            m.text(f"{desc} | 奖励{coins}金币、{rep}声望", ui.TEXT if unlocked else ui.MUTED)
            if unlocked:
                m.text(utc_text(g.achievements[key]), ui.MUTED, 16)

    def _page_memorial(self):
        m, g = self.modal, self.game
        m.text("每一段相伴的时光，都值得被记住。离世档案永不删除。", ui.GOLD)
        dead = [p for p in g.pets.values() if p.death_ts]
        for p in sorted(dead, key=lambda p: -p.death_ts):
            m.heading(p.name+" · "+p.species+" · "+p.personality)
            m.text(f"相伴{p.age_text(g.now)} | {p.death_reason} | {utc_text(p.death_ts)}", ui.MUTED)
            m.row([("翻阅完整生平", lambda pid=p.pet_id: self.open("pet", {"pet_id": pid}))])
        if not dead:
            m.text("花园还没有纪念档案，愿伙伴们平安健康。")
        departed = [p for p in g.pets.values() if p.departure]
        if departed:
            m.heading("离园留档 / 找到新家庭的伙伴")
            for p in departed:
                m.row([(p.name+" · "+p.departure+" · 查看档案", lambda pid=p.pet_id: self.open("pet", {"pet_id": pid}))])

    def _page_logs(self, page=0):
        m, g = self.modal, self.game
        m.text(f"第{page+1}页，每页50条。所有关键事件永久保存在 SQLite 中。", ui.MUTED)
        rows = g.db.read_logs(50, page*50)
        m.row([("上一页", lambda: self.open("logs", {"page": page-1}, False), page > 0),
               ("下一页", lambda: self.open("logs", {"page": page+1}, False), len(rows) == 50)])
        for row in rows:
            m.text(utc_text(row["ts"]), ui.GREEN, 15)
            m.text(row["text"], ui.MUTED, 17)
            m.gap(8)

    def _page_help(self):
        m = self.modal
        for title, text in [
            ("欢迎来到慢时光", "庭院里的伙伴会自由散步：单击抚摸并选中，连点两下或右键查看档案。底栏可喂食、洗澡、互动；把喂食盘里的食物拖到伙伴身上也能喂食。点击小屋管理建筑，地图查看全部地块；顶部菜单访问全部系统。滚轮翻阅弹窗，Esc关闭或取消拖拽，所有操作自动保存。"),
            ("小镇大地图", "主场景按住鼠标左键或中键拖动，滚轮以鼠标位置缩放。右下小地图点击定位，Home键或右上返回庄园按钮回到宠物区。底栏小镇地图查看六大街区，中央庄园仍保留原有20块可建造地块。拖拽不会触发宠物互动。"),
            ('自由道路与房屋', '点击底栏建设或按B，1～6切换道路、住宅、商业、产业、公共和拆除。道路左键拖动、松手整笔扣费保存；快速移动会补齐格子。右键/中键移图，滚轮缩放，R旋转建筑，Ctrl+Z撤销最近道路。Esc取消预览，再次按Esc退出。右侧切风格与建筑，格线和地价可切换。'),
            ('施工与道路中断', '临街才能放置建筑，水域、庄园安全边距和固定地标不可施工；未开放街区先支付原有开放费。建筑按游戏时间施工，离线也会完工。完工且通路才增加人口、税收和吸引力。拆除道路可能暂停街区消费、租金或农田收益，恢复后继续，不补发。默认道路免费赠送，拆除不退款；自建道路退原始费用50%，改铺费不参与退款。'),
            ('车辆与建设拆除', '汽车、公交和货车沿玩家路网右侧车道行驶，风雨雪减速，夜间亮灯。点击车辆查看轻量行程卡。路被拆后车辆改道或安全退场，行人改道或等待。拆楼需确认，施工中返还15%，完工返还30%；税收独立于实时车流结算。'),
            ("街区与流动人口", "点击街区开放或升级，最高3级。商业街增加游客消费，居民社区增加常住居民和每日租金，车站与公园吸引客流，农田每12游戏小时产出作物、12小时内手动收获。居民沿街生活，游客抵达、消费后离镇；点击行人查看档案。"),
            ("昼夜与天气", "UTC模拟时间决定晨曦、白昼、黄昏和夜晚，每4游戏小时按季节切换天气。大风、小雨、暴雨、雨夹雪、降雪均有动态效果；夜晚窗户亮灯。恶劣天气影响客流、农田产量和户外宠物的健康/清洁，室内与寄养可保护伙伴。左上天气卡可查看预报。"),
            ("第一段旅程", "商店可选择购买雌性或雄性。宠物成年、好感达到35后，可在繁育面板结为伴侣。双方需同品种异性且住在同一建筑，才能尝试受孕；孕期默认24游戏小时，分娩后幼崽从零岁成长。"),
            ("伴侣与孕期", "档案中可查看伴侣、幼崽父母和预产时间。孕期会预留幼崽床位，不能出售或交付父母；寄养暂停孕期。母体离世会终止孕期，伴侣和父母档案长期保留。"),
            ("冲突与医院", "同居宠物可能打架，饥饿、低开心和拥挤会增加风险。外伤会持续损害健康。分开居住可避免互相打架，医院与寄养屋内不会打架。点击看病先送医，再治疗。"),
            ("真实与加速时间", f"真实模式离线也按1:1流逝；加速模式按{C.SPEED}:1流逝，离线也加速。先按旧倍率结算，再切换新倍率。右上角可切换。时间全部按UTC显示。"),
            ("忙碌时的安排", "建造寄养屋，把伙伴迁入后年龄与属性完全暂停，原始生日保留。接回继续成长。饲养员每3游戏小时收费8金币，消耗普通饲料和清水，只做基础喂食与清洁。"),
            ("农场卫生与粪便", f"宠物每{C.EXCRETION_INTERVAL/C.HOUR:g}游戏小时排泄，积留{C.WASTE_UNSAFE_AFTER/C.HOUR:g}小时会提高所在居所患病风险。点击地面粪便或左侧卫生告示牌，可收集入库、就地出售，或雇佣农场管理员清扫。"),
            ("看得见的管理员", f"农场管理员{C.MANAGER_NAME}招聘费{C.MANAGER_HIRE_COST}金币，每{C.MANAGER_WAGE_INTERVAL/C.HOUR:g}游戏小时工资{C.MANAGER_WAGE}金币。雇佣后会在庭院走动并挥动扫帚，收集粪便到仓库；满仓会等待，工资不足会停工。"),
            ("经营闭环", "成年健康宠物定期产出副产品，需手动收取，18小时不收消失。羊奶、鸟蛋会过期。原料可加工成高级饲料和特制零食；商店交易、游客、邻里订单和出售幼崽提供收入。"),
            ("健康与季节", "饱食低于20持续掉健康；低清洁增加患病风险。疾病分普通感染、重症和绝症；重症救治可能失败甚至死亡，绝症只能舒缓照护。升级就诊医院可提高治愈率。冬季户外掉健康，夏季更易脏。"),
            ("长期记忆", "图鉴保留品种、变异与寿终记录。离世宠物保留在回忆花园。成就奖励自动发放。数据库开启事务、WAL和完整同步，每次操作立即提交，启动与退出保留备份。"),
            ("经济困难", "每游戏日可免费公益领养一只宠物，仍需适配居所有空位。关闭游戏会结算至当前时刻；如需长期离线，先寄养并切回真实模式。"),
        ]:
            m.heading(title)
            m.text(text, ui.MUTED)

    def _page_town(self):
        m,g = self.modal,self.game
        residents = sum(p["role"] == "居民" for p in g.people)
        guests = sum(p["role"] == "游客" for p in g.people)
        m.heading(f"慢时光小镇 Lv{g.town.town_level} / 常住{residents}人 · 流动{guests}人")
        m.text(f"累计接待{g.meta['town_visitors_total']}位游客 / 小镇经营累计收入{g.meta['town_income']}金币",ui.GOLD)
        m.text(f"当前{g.weather.current}，下一时段预计{g.weather.forecast()}，距变化{remaining(g.meta['next_weather_ts'],g.now)}",ui.GREEN)
        spec = C.WEATHERS[g.weather.current]
        m.text(f"天气客流系数{spec['traffic']:.0%}；大风雨雪影响户外清洁与健康，室内和寄养宠物受到保护。",ui.MUTED)
        m.row([("查看小镇全景",lambda:self.open("map")),("管理原庄园20块地",lambda:self.open("manor_map"))])
        bonus=g.construction.bonuses()
        m.text(f"玩家建筑{len(g.town_buildings)}栋 / 每日建筑税收{bonus['tax']}金币 / 住宅额外容量{bonus['population']}人",ui.GOLD)
        m.row([('进入道路与建筑建设 B',self.builder.enter)])
        for zone_id,config in C.TOWN_ZONES.items():
            level = g.town.level(zone_id)
            m.heading(f"{config['name']} / {'Lv'+str(level) if level else '尚未开放'}")
            m.text(config["feature"],ui.MUTED)
            m.text('道路通达' if g.construction.connected(zone_id) else '道路未连通，收益暂停',ui.GREEN if g.construction.connected(zone_id) else ui.RED)
            m.row([("定位并查看",lambda z=zone_id:self.open("zone",{"zone_id":z}))])

    def _page_zone(self, zone_id):
        m,g = self.modal,self.game
        config = C.TOWN_ZONES[zone_id]
        level = g.town.level(zone_id)
        m.heading(f"{config['name']} / {'Lv'+str(level) if level else '待开放'}")
        m.text(config["feature"],ui.MUTED)
        m.text('道路通达' if g.construction.connected(zone_id) else '道路未连通，收益暂停；接通后恢复，不补发。',ui.GREEN if g.construction.connected(zone_id) else ui.RED)
        def locate():
            self.close_modal()
            self.courtyard.focus_zone(zone_id)
        m.row([("定位到此区域",locate),("返回经营总览",lambda:self.open("town"))])
        def build_here():
            self.builder.enter(); self.courtyard.focus_zone(zone_id)
        m.row([('在此建设道路与建筑',build_here)])
        if zone_id == "estate":
            m.row([("庄园地块：建造、解锁与升级",lambda:self.open("manor_map"))])
        else:
            cost = g.town.upgrade_cost(zone_id)
            m.row([(f"{'升级' if level else '开放'}街区 / {cost}金币",lambda:self.act("upgrade_zone",zone_id=zone_id),level < C.TOWN_MAX_LEVEL)])
            m.text(f"最高{C.TOWN_MAX_LEVEL}级，当前金币{g.meta['coins']}。",ui.MUTED)
        if zone_id == "market":
            m.text(f"游客每次到访只消费一次，每人收入{C.TOWN_SPEND_PER_LEVEL*level}金币。",ui.GREEN)
            m.row([("进入庄园商店",lambda:self.open("shop"))])
        elif zone_id == "homes":
            m.text(f"当前居民容量{C.TOWN_BASE_RESIDENTS+level*C.TOWN_RESIDENTS_PER_LEVEL}人，每人每天租金{C.TOWN_RENT_PER_PERSON}金币。",ui.GREEN)
        elif zone_id == "fields":
            m.text(f"每{C.TOWN_FIELD_INTERVAL/C.HOUR:g}游戏小时产出一批谷物/鲜草，天气影响数量；{C.TOWN_FIELD_TTL/C.HOUR:g}小时内手动收获。",ui.MUTED)
            if g.meta["field_ready_ts"]:
                m.text("待收获："+"、".join(f"{k}×{v}" for k,v in g.meta["field_batch"].items()),ui.GOLD)
                m.text("剩余："+remaining(g.meta["field_expiry_ts"],g.now),ui.MUTED)
            m.row([("收获作物入库",lambda:self.act("harvest_field"),bool(g.meta["field_ready_ts"]))])

    def _page_person(self, person_id):
        m,g = self.modal,self.game
        p = next((p for p in g.people if p["person_id"] == person_id),None)
        if p is None:
            m.text("这位行人已经离开小镇。",ui.MUTED)
            return
        m.heading(f"{p['name']} / {p['role']}")
        m.text(f"状态：{p['status']} / 目的地：{C.TOWN_ZONES[p['destination']]['name']}")
        m.text("抵达："+utc_text(p["entered_ts"]),ui.MUTED)
        if p["role"] == "游客":
            m.text(f"{'已完成本次消费' if p['spent'] else '尚在游览'} / 距离镇{remaining(p['leave_ts'],g.now)}",ui.GOLD)
        else:
            m.text("居住地："+C.TOWN_ZONES[p["home_zone"]]["name"]+"；白天活动、夜晚归家。",ui.GREEN)

    def _page_map(self):
        m,g = self.modal,self.game
        m.text("3600×2400 小镇大地图 · 主界面拖拽平移、滚轮缩放；右下小地图点击定位。",ui.MUTED)
        def preview(s,r):
            area = pygame.Rect(r.x+10,r.y+5,555,370)
            pygame.draw.rect(s,(191,211,168),area,border_radius=18)
            for key,config in C.TOWN_ZONES.items():
                x,y,w,h = config["rect"]
                zr = pygame.Rect(area.x+x/3600*area.w,area.y+y/2400*area.h,w/3600*area.w,h/2400*area.h)
                pygame.draw.rect(s,config["color"],zr,border_radius=8)
                self.fonts.text(s,config["name"],(zr.x+6,zr.centery-8),13,ui.TEXT)
            for road in g.roads:
                pygame.draw.rect(s,(119,129,119),(area.x+road['cx']*48/3600*area.w,area.y+road['cy']*48/2400*area.h,7,7))
            for b in g.town_buildings:
                pygame.draw.rect(s,(180,125,88),(area.x+b['cx']*48/3600*area.w,area.y+b['cy']*48/2400*area.h,max(3,b['w']*48/3600*area.w),max(3,b['h']*48/2400*area.h)))
            self.fonts.text(s,"常住人口",(r.x+610,r.y+40),20,ui.GREEN)
            self.fonts.text(s,str(sum(p["role"] == "居民" for p in g.people))+" 人",(r.x+610,r.y+79),30,ui.TEXT)
            self.fonts.text(s,"流动游客",(r.x+610,r.y+140),20,ui.GREEN)
            self.fonts.text(s,str(sum(p["role"] == "游客" for p in g.people))+" 人",(r.x+610,r.y+180),30,ui.TEXT)
            self.fonts.text(s,g.weather.current,(r.x+610,r.y+250),24,ui.GOLD)
        m.custom(385,preview)
        ids = list(C.TOWN_ZONES)
        for i in range(0,len(ids),3):
            m.row([(C.TOWN_ZONES[z]["name"],lambda key=z:self.open("zone",{"zone_id":key})) for z in ids[i:i+3]])
        m.row([("原庄园20块地",lambda:self.open("manor_map")),("小镇经营总览",lambda:self.open("town"))])

    def _page_town_building(self,building_id):
        m,g=self.modal,self.game
        b=next((b for b in g.town_buildings if b['building_id']==building_id),None)
        if b is None:
            m.text('建筑已拆除。',ui.MUTED); return
        spec=K.CATALOG[b['catalog_key']]; connected=g.construction.building_connected(b)
        state='施工中' if b['status']=='building' else '已完工'
        m.heading(spec['name']+' / '+K.FAMILIES[spec['family']]+' / '+spec['category'])
        def portrait(s,r):
            sprite=town_view.town_art.building_sprite(b['catalog_key'],b['orientation'],self.courtyard.light,g.season,g.weather.current,3 if b['status']=='building' else 4)
            factor = min(1, (r.w-16)/sprite.get_width(), (r.h-16)/sprite.get_height())
            if factor < 1:
                sprite = pygame.transform.smoothscale(sprite,(max(1,round(sprite.get_width()*factor)),max(1,round(sprite.get_height()*factor))))
            s.blit(sprite,sprite.get_rect(midbottom=(r.centerx,r.bottom-8)))
        m.custom(190 if m.is_side else 290,portrait)
        m.text(f"{b['w']}×{b['h']}格 / 状态：{state} / {'道路通达' if connected else '道路中断，收益暂停'}",ui.GREEN if connected else ui.RED)
        if b['status']=='building':
            m.text('距完工：'+remaining(b['finish_ts'],g.now)+'（游戏时间）',ui.GOLD)
            m.bar('施工',100*max(0,min(1,(g.now-b['placed_ts'])/(b['finish_ts']-b['placed_ts']))))
        m.text(f"完工通路后：人口+{spec['population']} / 消费+{spec['commerce']:.0%} / 吸引力{spec['attraction']:+g} / 每日税{spec['tax']}金币")
        m.text(f"地价分{g.construction.land_value((b['cx'],b['cy']))} / 地价倍率{g.construction.land_factor((b['cx'],b['cy'])):.0%}",ui.MUTED)
        refund=int(spec['cost']*K.BUILDING_REFUND[b['status']])
        m.row([(f'拆除 / 返还{refund}金币',lambda:self._confirm(f"确定拆除{spec['name']}？返还{refund}金币。",'remove_town_building',building_id=building_id)),('继续建设',self.builder.enter)])

    def _page_today(self):
        m, g = self.modal, self.game
        day = int((g.now-g.meta["start_ts"])//C.DAY)+1
        m.text(f"{g.season}季 · 第{day}天 · {utc_text(g.now)}", ui.GOLD, 20)
        m.text(f"金币 {g.meta['coins']} · 声望 {g.meta['reputation']} · 伙伴 {len(g.living)}/{C.PET_LIMIT}",
               ui.MUTED)
        m.heading("今日清单")
        for line in g.todos():
            m.text("· "+line, ui.TEXT)
        m.gap(4)
        m.text(f"仓库 {g.inventory.used}/{g.storage_capacity} · 寄养中 {sum(p.fostered for p in g.living)} 只",
               ui.MUTED)
        m.heading("饲养员")
        m.text("每3游戏小时值班，消耗普通饲料与清水，工资8金币。", ui.MUTED)
        m.row([("解雇饲养员 / 当前在职" if g.meta["caretaker"]
                else f"雇佣饲养员 / {C.CARE_HIRE_COST}金币", self._toggle_caretaker)])
        m.heading("农场卫生与管理员")
        m.text(f"地面粪便{g.sanitation.quantity()}份 / {g.sanitation.status()}",ui.MUTED)
        m.row([("清扫、出售或雇佣管理员",lambda:self.open("sanitation"))])
        m.heading("最近手记")
        for row in reversed(g.logs[-6:]):
            m.text(f"{utc_text(row['ts'])[11:16]}  {row['text']}", ui.MUTED, 16)
        m.heading("操作提示")
        for line in ("点击庭院里的宠物可以抚摸；连点两下或右键查看档案。",
                     "把喂食盘里的食物拖到宠物身上即可喂食。",
                     "点击小屋进入建筑管理，底部地图查看全部地块。",
                     "F1 打开帮助，Esc 关闭弹窗或取消拖拽。"):
            m.text(line, ui.MUTED)

    def _draw_toast(self, surface):
        lines = self.fonts.wrap(self.toast, 1060, 17)[:3]
        height = len(lines)*24+16
        rect = pygame.Rect(64, self.screen.get_height()-164-height, 1152, height)
        art.clay_panel(surface, rect, (255, 251, 242), 12, ui.GREEN, shadow_alpha=55)
        pygame.draw.rect(surface, ui.GOLD, (rect.x+12, rect.y+9, 4, rect.height-18), border_radius=2)
        for i, line in enumerate(lines):
            self.fonts.text(surface, line, (rect.x+26, rect.y+8+i*24), 17, ui.TEXT)

    def draw(self):
        self._configure_layout()
        s, g = self.screen, self.game
        s.begin(self.display.viewport.w>s.get_width())
        s.fill(ui.BG)
        self.courtyard.draw(s, g, self.actor_order, self.focus_pet_id,
                            drag_item=self.drag["item"] if self.drag else None,
                            feed_count=self._feed_count())
        self.chrome.begin(False)
        self.hud.draw(self.chrome,g)
        pygame.draw.rect(s,ui.PANEL,(0,0,s.get_width(),88))
        s.blit(self.chrome,(0,0),(0,0,1280,88))
        pending = sum(e['status']=='待处理' for e in g.events)
        unread = sum(not entry['read'] for entry in self.notifications)
        self.notification_button.label = f'通知 {pending+unread}' if pending+unread else '通知 N'
        self.notification_button.draw(s,self.fonts)
        if self.builder.active:
            self.builder.draw(s)
        else:
            ui.pointer_transform=self.bottom_pos
            try:self.bottom.draw(self.chrome, g, self._focus_pet())
            finally:ui.pointer_transform=None
            pygame.draw.rect(s,ui.PANEL,(0,s.get_height()-156,s.get_width(),156))
            s.blit(self.chrome,((s.get_width()-1280)//2,s.get_height()-156),(0,704,1280,156))
        if self.decorator.active:self.decorator.draw(s)
        # 弹窗打开时不显示提示条，避免遮挡档案与地图内容。
        if self.toast and (not self.modal or self.modal.is_side) and time.monotonic() < self.toast_until:
            if self.modal and self.modal.is_side:
                rect = pygame.Rect(24, self.courtyard.viewport.bottom-78, 680, 62)
                pygame.draw.rect(s, ui.PANEL, rect, border_radius=10)
                for index,line in enumerate(self.fonts.wrap(self.toast,640,16)[:2]):
                    self.fonts.text(s,line,(rect.x+16,rect.y+8+index*24),16,ui.GREEN)
            else:
                self._draw_toast(s)
        if self.modal:
            self.modal.draw(s)
        if self.menu_open:
            self.more_menu.draw(s, g)
        if self.drag:
            pos = ui.pointer()
            art.draw_glow(s, pos, 26, (255, 220, 150), 50)
            art.draw_item_icon(s, self.drag["item"], (pos[0]-18, pos[1]-18, 36, 36))
        self.pet_hits = [(self.courtyard.local_rect(actor.hit_rect()), actor.pet_id) for actor in self.actor_order]
        self._update_cursor()
        self.display.present(s)

    def handle_native(self, event):
        """操作系统事件统一转换一次；handle 接收逻辑坐标，便于回放与测试。"""
        if event.type == pygame.VIDEORESIZE:
            self.display.resize(event.size)
            self._configure_layout()
            return
        if event.type in (getattr(pygame,'WINDOWSIZECHANGED',-1),getattr(pygame,'WINDOWRESIZED',-2)):
            self.display.update_viewport()
            self._configure_layout()
            if not self.display.fullscreen:
                self.display.windowed_size = self.display.window.get_size()
            return
        if hasattr(event, 'pos'):
            if not self.display.viewport.collidepoint(event.pos):
                if event.type == pygame.MOUSEBUTTONUP:
                    self.pan = None
                    self.builder.stroke = self.builder.pan = None
                    self._cancel_drag()
                return
            values = dict(event.dict)
            values['pos'] = self.display.to_logical(event.pos)
            if hasattr(event,'rel'):
                values['rel'] = (event.rel[0]*self.display.logical_size[0]/self.display.viewport.w,
                                 event.rel[1]*self.display.logical_size[1]/self.display.viewport.h)
            event = pygame.event.Event(event.type, values)
        self.handle(event)

    def handle(self, event):
        if event.type == pygame.QUIT:
            self.running = False
            return
        if event.type == pygame.KEYDOWN:
            if event.key == pygame.K_F11 or (event.key == pygame.K_RETURN and getattr(event,'mod',0)&pygame.KMOD_ALT):
                self.toggle_fullscreen()
                return
            if event.key == pygame.K_F10:
                self.open('settings')
                return
            if event.key == pygame.K_n and not (self.modal and self.modal.inputs):
                self.open('notifications')
                return
        if event.type == getattr(pygame, "WINDOWFOCUSLOST", -1):
            self._cancel_drag()
            self.pan = None
            self.builder.stroke=self.builder.pan=None
            self.decorator.pan=None
            return
        if self.menu_open:
            if event.type == pygame.MOUSEBUTTONDOWN:
                if event.button != 1 or not self.more_menu.handle(event.pos):
                    self.menu_open = False
                return
            if event.type == pygame.KEYDOWN and event.key == pygame.K_ESCAPE:
                self.menu_open = False
                return
        if self.modal:
            pos = getattr(event,'pos',ui.pointer())
            if not self.modal.is_side or self.modal.rect.collidepoint(pos) or (event.type == pygame.KEYDOWN and event.key in (pygame.K_ESCAPE,pygame.K_PAGEUP,pygame.K_PAGEDOWN)):
                if self.modal.is_side and event.type == pygame.MOUSEBUTTONUP:
                    self.pan = None
                    self.builder.pan = self.builder.stroke = None
                    self._cancel_drag()
                self.modal.handle(event)
                return
        if event.type == pygame.MOUSEBUTTONDOWN and event.button == 1 and self.notification_button.click(event.pos):
            return
        if event.type==pygame.KEYDOWN and event.key==pygame.K_b:
            self.builder.toggle()
            return
        if event.type==pygame.KEYDOWN and event.key==pygame.K_d:
            self.decorator.exit() if self.decorator.active else self.decorator.enter()
            return
        if self.decorator.active:
            self.decorator.handle(event)
            return
        if self.builder.active:
            self.builder.handle(event)
            return
        if event.type == pygame.KEYDOWN:
            if event.key == pygame.K_ESCAPE:
                self.pan = None
                if self.drag:
                    self._cancel_drag()
                else:
                    self.menu_open = False
                return
            if event.key == pygame.K_F1:
                self.open("help")
                return
            if event.key == pygame.K_HOME:
                self.courtyard.home()
                return
        if event.type == pygame.MOUSEWHEEL and self.courtyard.viewport.collidepoint(ui.pointer()) and not self.drag:
            self.courtyard.camera.zoom_at(ui.pointer(),1.12**event.y)
            return
        if event.type == pygame.MOUSEMOTION and self.pan:
            start,last = self.pan["start"],self.pan["last"]
            if self.pan["moving"]:
                self.courtyard.camera.pan(event.pos[0]-last[0],event.pos[1]-last[1])
            elif (event.pos[0]-start[0])**2+(event.pos[1]-start[1])**2 >= 36:
                self.pan["moving"] = True
                self.courtyard.camera.pan(event.pos[0]-start[0],event.pos[1]-start[1])
            self.pan["last"] = event.pos
            return
        if event.type == pygame.MOUSEBUTTONDOWN and event.button == 2 and self.courtyard.viewport.collidepoint(event.pos):
            self.pan = {"start":event.pos,"last":event.pos,"moving":True,"button":2}
            return
        if event.type == pygame.MOUSEBUTTONDOWN and event.button == 1:
            if not self.drag:
                view = self.courtyard
                overlay = view.home_button.collidepoint(event.pos) or view.overview_rect.collidepoint(event.pos) or view.legend_rect.collidepoint(event.pos)
                if self.courtyard.viewport.collidepoint(event.pos) and not overlay:
                    self.pan = {"start":event.pos,"last":event.pos,"moving":False,"button":1}
                else:
                    self._handle_click(event.pos)
        elif event.type == pygame.MOUSEBUTTONDOWN and event.button == 3:
            if self.drag:
                self._cancel_drag()
                return
            actor = self.courtyard.pet_at(event.pos, self.actor_order)
            if actor:
                self.focus_pet_id = actor.pet_id
                self._sync_actors()
                self.open("pet", {"pet_id": actor.pet_id})
        elif event.type == pygame.MOUSEBUTTONUP and event.button == 1 and self.drag:
            self._drop_drag(event.pos)
        elif event.type == pygame.MOUSEBUTTONUP and self.pan and event.button == self.pan["button"]:
            pan = self.pan
            self.pan = None
            if not pan["moving"] and event.button == 1:
                self._handle_click(event.pos)

    def _feed_count(self):
        return sum(self.game.inventory.count(item) for item in hud.FEED_TRAY)

    def tick(self):
        now = time.monotonic()
        dt = min(.25, max(0.0, now-self.last_frame)) if self.last_frame else 1/C.FPS
        self.last_frame = now
        self.courtyard.tick(dt,self.game)
        night = courtyard.CourtyardView.light_key(self.game.now) == "night"
        for actor in self.actor_order:
            companions=[(other.x,other.y) for other in self.actor_order if other is not actor]
            companions += [self.courtyard.screen_to_local(self.courtyard.camera.world_to_screen((p.x,p.y)))
                           for p in self.courtyard.people.values() if p.visible]
            actor.update(dt, courtyard.ACTIVITY, self.ui_rng, night,
                         {'food':courtyard.BOWL,'toy':courtyard.TOY,'companions':companions})
        if now-self.last_tick >= 1:
            persist = now-self.last_save >= C.SAVE_INTERVAL
            try:
                building_before={b['building_id'] for b in self.game.town_buildings if b['status']=='building'}
                tax_before=self.game.meta['town_tax_total']
                self.game.advance(persist=persist)
                finished=[K.CATALOG[b['catalog_key']]['name'] for b in self.game.town_buildings if b['building_id'] in building_before and b['status']=='ready']
                if finished: self.notify('施工完成：'+'、'.join(finished))
                elif self.game.meta['town_tax_total']>tax_before: self.notify(f"建筑每日税收：{self.game.meta['town_tax_total']-tax_before}金币")
                if persist:
                    self.last_save = now
                self.refresh()
            except SaveError as exc:
                self.notify(str(exc))
            self.last_tick = now
        for text in self.game.notices[:]:
            self.notify(text)
        self.game.notices.clear()

    def run(self, frame_limit=None):
        frames = 0
        try:
            while self.running:
                for event in pygame.event.get():
                    self.handle_native(event)
                self.tick()
                self.draw()
                self.clock.tick(C.FPS)
                frames += 1
                if frame_limit and frames >= frame_limit:
                    break
        finally:
            self._save_display()
            self.game.close()
            pygame.quit()
