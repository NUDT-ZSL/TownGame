"""应用服务：统一时钟、离线结算、经济与操作事务。完全不依赖 Pygame。"""
import copy
import random
import time
import uuid
from datetime import datetime, timezone
import game_config as C
from pet import Pet, clamp
from building import Building
from inventory import Inventory
from db import Database, SaveError
import event as events
import achievement
from social import SocialSystem
from medical import MedicalSystem
from sanitation import SanitationSystem
from weather import WeatherSystem
from town import TownSystem
from town_build import TownBuildSystem
from pet_services import action_reason


class GameError(ValueError):
    """可以直接展示给玩家的规则错误。"""


def utc_text(ts):
    return datetime.fromtimestamp(ts, timezone.utc).strftime("%Y-%m-%d %H:%M UTC")


def remaining(ts, now):
    seconds = max(0, int(ts-now))
    return f"{seconds//C.DAY}天{seconds//C.HOUR%24}时{seconds//60%60}分"


class Game:
    def __init__(self, database=None, real_now=None, seed=None):
        self.db = database or Database()
        self.rng = random.Random(seed)
        self.notices = []
        self.social = SocialSystem(self)
        self.medical = MedicalSystem(self)
        self.sanitation = SanitationSystem(self)
        self.weather = WeatherSystem(self)
        self.town = TownSystem(self)
        self.construction = TownBuildSystem(self)
        current = time.time() if real_now is None else real_now
        state = self.db.load()
        if state:
            self._load(state)
            # 升级前的离线时段不追溯生成粪便；新排泄计时从本次打开时开始。
            target = self.now+max(0,current-self.meta["real_anchor"])*(C.SPEED if self.meta["mode"] == "fast" else 1)
            if "town_open_ts" not in state["meta"]:
                self.weather.defaults(target,reset=True)
                self.town.defaults(target,reset=True)
            if 'construction_open_ts' not in state['meta']:
                self.meta['construction_open_ts']=target
                self.meta['next_tax_ts']=target+C.DAY
            for pet in self.living:
                if not pet.next_excretion_ts:
                    origin = self.now if pet.fostered else target
                    pet.next_excretion_ts = origin+C.SPECIES[pet.species].get("excretion_interval",C.EXCRETION_INTERVAL)
        else:
            self._new(current)
            self.save()
        previous = self.now
        self.advance(current)
        self.offline_seconds = self.now - previous
        # 开启存档时也有一次事件机会；设冷却，避免反复重开刷奖励。
        if state and self.now-self.meta.get("last_visit_event_ts", previous) >= C.OPEN_EVENT_COOLDOWN:
            self.meta["last_visit_event_ts"] = self.now
            if not any(e["status"] == "待处理" for e in self.events) and self.rng.random() < C.EVENT_CHANCE:
                entry = events.create_event(self.now, self.meta["reputation"], self.rng)
                self.events = (self.events + [entry])[-30:]
                self.log("开园事件："+entry["text"])
            self.save()
        if self.db.recovery_message:
            self.notices.append(self.db.recovery_message)
            self.log(self.db.recovery_message)
            self.save()

    @property
    def now(self):
        return self.meta["sim_ts"]

    @property
    def season(self):
        return C.SEASONS[int((self.now-self.meta["start_ts"]) // (C.SEASON_DAYS*C.DAY)) % 4]

    @property
    def storage_capacity(self):
        return C.BASE_STORAGE + sum(C.STORAGE_PER_LEVEL*b.level for b in self.buildings.values() if b.kind == "仓库")

    @property
    def living(self):
        return [p for p in self.pets.values() if p.active]

    def occupants(self, building_id):
        return [p for p in self.living if p.building_id == building_id]

    def housing_used(self, building_id):
        return sum(1+int(p.pregnant) for p in self.occupants(building_id))

    @property
    def population_with_reservations(self):
        return sum(1+int(p.pregnant) for p in self.living)

    def _new(self, now):
        self.meta = {"sim_ts": now, "real_anchor": now, "start_ts": now, "mode": "real",
                     "coins": C.INITIAL_COINS, "reputation": 0, "caretaker": False,
                     "next_care_ts": now+C.CARE_INTERVAL, "next_visitor_ts": now+C.VISITOR_INTERVAL,
                     "next_event_ts": now+C.EVENT_INTERVAL, "next_order_ts": now+C.ORDER_INTERVAL,
                     "next_fight_ts": now+C.FIGHT_INTERVAL,
                     "discount_until": 0, "inflation_until": 0, "adopt_ready_ts": 0, "last_visit_event_ts": now,
                     "stats": {"breeds": 0, "sold": 0, "orders": 0}}
        self.buildings = {i: Building(i, i in C.INITIAL_OPEN_PLOTS) for i in range(C.GRID_COLS*C.GRID_ROWS)}
        for bid, kind in C.INITIAL_BUILDINGS.items():
            self.buildings[bid] = Building(bid, True, kind, 1)
        self.pets = {}
        self.inventory = Inventory()
        self.waste = []
        self.sanitation.defaults()
        self.events, self.orders, self.logs = [], events.create_orders(now, self.rng), []
        self.achievements, self.codex = {}, {}
        for sex, name in (("雌", "团子"), ("雄", "麻薯")):
            p = Pet.create("兔子", now, 0, self.rng, name=name, sex=sex)
            self.pets[p.pet_id] = p
        for item, quantity in C.STARTER_ITEMS.items():
            self.inventory.add(item, quantity, now, self.storage_capacity)
        achievement.update_codex(self.codex, self.pets.values())
        self.log("欢迎来到慢时光庄园！团子和麻薯正在等你。")
        self.town_zones,self.people = [],[]
        self.weather.defaults()
        self.town.defaults()
        self.roads,self.town_buildings = None,[]
        self.construction.defaults()

    def snapshot(self):
        return copy.deepcopy({"meta": self.meta, "pets": [p.to_dict() for p in self.pets.values()],
                              "buildings": [b.to_dict() for b in self.buildings.values()],
                              "inventory": self.inventory.batches, "waste":self.waste, "events": self.events,
                              "orders": self.orders, "logs": self.logs,
                              "achievements": self.achievements, "codex": self.codex,
                              "town_zones":self.town_zones,"people":self.people,
                              'roads':self.roads,'town_buildings':self.town_buildings})

    def _load(self, state):
        self.meta = copy.deepcopy(state["meta"])
        self.meta.setdefault("next_fight_ts", self.meta["sim_ts"]+C.FIGHT_INTERVAL)
        self.sanitation.defaults()
        if "rng_state" in self.meta:
            s = self.meta["rng_state"]
            self.rng.setstate((s[0], tuple(s[1]), s[2]))
        self.pets = {d["pet_id"]: Pet.from_dict(copy.deepcopy(d)) for d in state["pets"]}
        self.buildings = {d["building_id"]: Building.from_dict(d) for d in state["buildings"]}
        self.inventory = Inventory(copy.deepcopy(state["inventory"]))
        self.waste = copy.deepcopy(state.get("waste",[]))
        for key in ("events", "orders", "logs", "achievements", "codex"):
            setattr(self, key, copy.deepcopy(state[key]))
        self.town_zones = copy.deepcopy(state.get("town_zones",[]))
        self.people = copy.deepcopy(state.get("people",[]))
        self.weather.defaults()
        self.town.defaults()
        self.roads = copy.deepcopy(state.get('roads')) if 'road_revision' in state['meta'] else None
        self.town_buildings = copy.deepcopy(state.get('town_buildings',[]))
        self.construction.defaults()

    def save(self):
        s = self.rng.getstate()
        self.meta["rng_state"] = [s[0], list(s[1]), s[2]]
        self.db.save(self.snapshot())
        self.logs = self.logs[-300:]

    def log(self, text, ts=None):
        self.logs.append({"log_id": uuid.uuid4().hex, "ts": self.now if ts is None else ts, "text": text})
        self._record_finance(text,self.now if ts is None else ts)

    def _record_finance(self,reason,ts=None):
        coins=self.meta['coins'];previous=self.meta.get('ledger_balance',coins)
        if coins!=previous:
            rows=self.meta.setdefault('finance_ledger',[])
            rows.append(dict(ts=self.now if ts is None else ts,delta=coins-previous,balance=coins,reason=reason))
            self.meta['finance_ledger']=rows[-3000:]
        self.meta['ledger_balance']=coins

    def advance(self, real_now=None, persist=True):
        """每次动作前与退出时调用。模式切换前先按旧倍率结算。

        UTC 实时时钟与庄园时钟分开持久化。加速模式离线仍按倍率推进，
        若要长期离线可选真实模式或寄养。系统时钟回拨不产生负年龄或重复收益。
        时间差不封顶；按小时及调度边界分段推进，不会丢弃长期离线时间。
        """
        real = time.time() if real_now is None else real_now
        delta = max(0.0, real-self.meta["real_anchor"])
        if delta <= 0:
            return
        before = self.snapshot()
        rng_state, notices = self.rng.getstate(), list(self.notices)
        target = self.now + delta*(C.SPEED if self.meta["mode"] == "fast" else 1)
        try:
            while self.now < target:
                old = self.now
                season_end = self.meta["start_ts"] + (int((old-self.meta["start_ts"])/(C.SEASON_DAYS*C.DAY))+1)*C.SEASON_DAYS*C.DAY
                boundaries = [target, old+C.STEP, season_end, self.meta["next_visitor_ts"], self.meta["next_event_ts"], self.meta["next_order_ts"], self.meta["next_fight_ts"]]
                boundaries += [p.pregnancy_due_ts for p in self.living if p.pregnant and not p.fostered]
                boundaries += self.sanitation.boundaries()
                boundaries += [self.meta["next_weather_ts"]]+self.town.boundaries()
                boundaries += self.construction.boundaries()
                if self.meta["caretaker"]:
                    boundaries.append(self.meta["next_care_ts"])
                boundaries += [e["expiry_ts"] for e in self.events if e["status"] == "待处理"]
                end = min(x for x in boundaries if x > old+1e-6) if target-old > 1e-6 else target
                previous_season = self.season
                exposures = self.sanitation.exposure_map(old)
                for p in self.living:
                    b = self.buildings[p.building_id]
                    was_foster = p.fostered
                    exposure = exposures[p.building_id]
                    weather_dirt,weather_damage = self.weather.outdoor_effect(b)
                    for ts, msg in p.advance(end, b, previous_season, self.rng, exposure["hazard"],exposure["dirt"],weather_dirt,weather_damage):
                        self.log(msg, ts)
                    if was_foster:
                        p.next_product_ts += end-old
                    self._products(p, end)
                self.meta["sim_ts"] = end
                self.weather.tick()
                self.construction.tick()
                self.town.tick()
                self.social.deliver()
                self.sanitation.tick()
                if end >= self.meta["next_fight_ts"]:
                    self.social.fights()
                    self.meta["next_fight_ts"] += C.FIGHT_INTERVAL
                for message in self.inventory.expire(end, self.storage_capacity):
                    self.log(message)
                if self.meta["caretaker"] and end >= self.meta["next_care_ts"]:
                    self._care()
                    self.meta["next_care_ts"] += C.CARE_INTERVAL
                if end >= self.meta["next_visitor_ts"]:
                    self._visitors()
                    self.meta["next_visitor_ts"] += C.VISITOR_INTERVAL
                for order in self.orders:
                    if order["status"] == "进行中" and order["expiry_ts"] <= end:
                        order["status"] = "过期"
                        self.log(f'{order["npc"]}的订单已过期。')
                for entry in self.events:
                    if entry["status"] == "待处理" and entry["expiry_ts"] <= end:
                        self._resolve_event(entry, 1, automatic=True)
                        entry["status"] = "过期"
                if end >= self.meta["next_order_ts"]:
                    active = [o for o in self.orders if o["status"] == "进行中"]
                    if len(active) < C.ORDER_COUNT*2:
                        self.orders = (self.orders + events.create_orders(end, self.rng))[-30:]
                        self.log("公告栏刷新了新的邻里订单。")
                    self.meta["next_order_ts"] += C.ORDER_INTERVAL
                if end >= self.meta["next_event_ts"]:
                    if not any(e["status"] == "待处理" for e in self.events) and self.rng.random() < C.EVENT_CHANCE:
                        entry = events.create_event(end, self.meta["reputation"], self.rng)
                        self.events = (self.events + [entry])[-30:]
                        self.log("庄园事件：" + entry["text"])
                    self.meta["next_event_ts"] += C.EVENT_INTERVAL
            self.meta["real_anchor"] = max(real, self.meta["real_anchor"])
            achievement.check(self)
            self._record_finance('时间推进结算')
            if persist:
                self.save()
        except Exception:
            self._load(before)
            self.rng.setstate(rng_state)
            self.notices = notices
            raise

    def _products(self, p, end):
        if not p.active:
            return
        if p.product_ready_ts and end >= p.product_expiry_ts:
            self.log(f"{p.name}的副产品超时消失。", p.product_expiry_ts)
            p.product_ready_ts = p.product_expiry_ts = 0
        if p.fostered:
            return
        interval = C.SPECIES[p.species]["interval"]
        adult_ts = p.birth_ts + p.paused_seconds + p.lifespan_max*C.STAGES[1][0]
        due = max(p.next_product_ts, adult_ts)
        if end >= due:
            count = int((end-due)//interval)+1
            last_due = due+(count-1)*interval
            p.next_product_ts = due+count*interval
            if p.hunger >= C.STARVING and not p.unwell:
                # 每只宠物只存一份未收取产物；后续产出不刷新旧产物保质期。
                if not p.product_ready_ts and last_due+C.PRODUCT_TTL > end:
                    p.product_ready_ts, p.product_expiry_ts = last_due, last_due+C.PRODUCT_TTL

    def _care(self):
        pets = [p for p in self.living if not p.fostered]
        if not pets:
            return
        if self.meta["coins"] < C.CARE_WAGE:
            self.meta["caretaker"] = False
            self.log("金币不足支付工资，饲养员已停止工作。")
            return
        self.meta["coins"] -= C.CARE_WAGE
        fed = cleaned = 0
        for p in sorted(pets, key=lambda p: p.hunger):
            if p.hunger < C.CARE_FEED_THRESHOLD and self.inventory.count("普通饲料"):
                self.inventory.take("普通饲料", 1)
                p.hunger = clamp(p.hunger+C.ITEMS["普通饲料"]["feed"]["hunger"])
                fed += 1
            if p.cleanliness < C.CARE_CLEAN_THRESHOLD and self.inventory.count("清水"):
                self.inventory.take("清水", 1)
                p.cleanliness = clamp(p.cleanliness+C.CARE_CLEAN_GAIN)
                cleaned += 1
        self.log(f"饲养员值班：喂食{fed}只、清洁{cleaned}只，工资{C.CARE_WAGE}金币。")

    def _visitors(self):
        pets = [p for p in self.living if not p.fostered]
        if not pets:
            return
        shops = sum(b.level for b in self.buildings.values() if b.kind == "商店")
        parks = sum(b.level for b in self.buildings.values() if b.kind == "游乐区")
        quality = sum(p.happy for p in pets)/len(pets)/100
        income = int((C.VISITOR_BASE + len(pets)*C.VISITOR_PER_PET)*(1+self.meta["reputation"]/100)*(1+shops*C.VISITOR_SHOP_BONUS+parks*C.VISITOR_PARK_BONUS)*quality)
        income = int(income*C.WEATHERS[self.weather.current]["traffic"])
        self.meta["coins"] += income
        self.log(f"游客参观带来门票收入 {income} 金币。")

    def perform(self, action, real_now=None, **kwargs):
        self.advance(real_now)
        handler = getattr(self, "_act_"+action, None)
        if handler is None:
            raise GameError("未知操作")
        before, rng_state, notices = self.snapshot(), self.rng.getstate(), list(self.notices)
        try:
            result = handler(**kwargs)
            self.sanitation.wake()
            achievement.check(self)
            self._record_finance('操作结算：'+action)
            self.save()
            if action in ('build_roads','remove_roads'):
                self.construction.undo.append(self.construction.pending_undo)
                self.construction.undo=self.construction.undo[-20:]
            elif action=='undo_roads':
                self.construction.undo.pop()
                if self.construction.undo:
                    self.construction.undo[-1]['revision']=self.meta['road_revision']
            return result or "操作完成，已保存。"
        except Exception as exc:
            self._load(before)
            self.rng.setstate(rng_state)
            self.notices = notices
            if isinstance(exc, (ValueError, KeyError)):
                raise GameError(str(exc)) from exc
            raise

    def _pay(self, coins):
        if self.meta["coins"] < coins:
            raise GameError(f"金币不足，需要{coins}金币")
        self.meta["coins"] -= coins

    def _pet(self, pet_id, allow_foster=False):
        p = self.pets.get(pet_id)
        if not p or not p.active:
            raise GameError("这只宠物已不在庄园中")
        if p.fostered and not allow_foster:
            raise GameError("请先将宠物接出寄养屋")
        return p

    def _home(self, building_id, slots=1):
        b = self.buildings.get(building_id)
        if not b or not b.kind or self.housing_used(building_id)+slots > b.capacity:
            raise GameError("建筑没有可用宠物空位")
        return b

    def find_home(self, species, foster=False):
        kind = "寄养屋" if foster else C.SPECIES[species]["building"]
        for b in self.buildings.values():
            if b.kind == kind and self.housing_used(b.building_id) < b.capacity:
                return b.building_id
        raise GameError(f"请先建造或腾出{kind}的空位")

    def _act_mode(self):
        self.meta["mode"] = "fast" if self.meta["mode"] == "real" else "real"
        text = "切换为" + (f"加速模式 ×{C.SPEED}（离线也加速）" if self.meta["mode"] == "fast" else "真实时间模式")
        self.log(text)
        return text

    def _act_feed(self, pet_id, item="普通饲料"):
        p = self._pet(pet_id)
        if item not in C.ITEMS or "feed" not in C.ITEMS[item]:
            raise GameError("该物品不能喂食")
        self.inventory.take(item, 1)
        for attr, value in C.ITEMS[item]["feed"].items():
            setattr(p, attr, clamp(getattr(p, attr)+value))
        if item == "腐败物":
            p.contract_illness(self.now,self.rng,"普通感染")
        if p.health <= 0:
            p.die(self.now, "误食腐败物导致健康耗尽")
        self.log(f"给{p.name}喂了{item}。")

    def _act_batch_care(self,kind):
        if kind not in ('feed','bath','play','collect'):
            raise GameError('不支持的批量操作')
        done=[];skipped=[]
        for p in sorted(self.living,key=lambda p:(p.hunger if kind=='feed' else p.cleanliness if kind=='bath' else 0,p.pet_id)):
            reason=action_reason(self,p,kind,batch=True)
            if reason:
                skipped.append(f'{p.name}：{reason}')
                continue
            if kind=='feed':
                item=next(item for item,spec in C.ITEMS.items() if 'feed' in spec and item!='腐败物' and self.inventory.count(item))
                self._act_feed(p.pet_id,item)
            else:getattr(self,'_act_'+kind)(pet_id=p.pet_id)
            done.append(p.name)
        label={'feed':'喂食','bath':'洗澡','play':'互动','collect':'收取'}[kind]
        result=f'批量{label}完成 {len(done)} 只，跳过 {len(skipped)} 只。'
        if skipped:result+=' '+'；'.join(skipped[:4])
        return result

    def _act_bath(self, pet_id):
        p = self._pet(pet_id)
        self.inventory.take("清水", 1)
        self.inventory.take("沐浴露", 1)
        p.cleanliness = clamp(p.cleanliness+C.BATH_GAIN)
        self.log(f"给{p.name}洗了个香香的澡。")

    def _act_play(self, pet_id):
        p = self._pet(pet_id)
        if self.now < p.interact_ready_ts:
            raise GameError("宠物需要休息，"+remaining(p.interact_ready_ts, self.now)+"后可再次互动")
        factor = C.PLAY_PERSONALITY_MULT[p.personality]
        park = sum(b.level for b in self.buildings.values() if b.kind == "游乐区")
        park += self.town.effective_level("park")
        p.favor = clamp(p.favor+C.PLAY_FAVOR*factor)
        p.happy = clamp(p.happy+C.PLAY_HAPPY+min(park*C.PLAY_PARK_BONUS, C.PLAY_PARK_MAX))
        p.interact_ready_ts = self.now+C.INTERACT_COOLDOWN
        text = p.name + C.FEEDBACK[p.personality]
        p.note(self.now, text)
        self.log(text)
        return text

    def _act_treat(self, pet_id):
        return self.medical.treat(pet_id)

    def _act_hospitalize(self, pet_id):
        return self.medical.hospitalize(pet_id)

    def _validate_appearance(self, species, variant):
        if species not in C.SPECIES or type(variant) is not int or not 0 <= variant < 384:
            raise GameError('请选择有效的动物外观')

    def _act_favorite_style(self, species, variant):
        self._validate_appearance(species,variant)
        favorites=self.meta.setdefault('style_favorites',{}).setdefault(species,[])
        if variant in favorites:
            favorites.remove(variant)
            return '已取消收藏样式。'
        if len(favorites)>=12:
            raise GameError('每个品种最多收藏12种样式，请先取消一个收藏')
        favorites.append(variant)
        return '样式已加入收藏。'

    def _act_reset_appearance(self,pet_id):
        p=self._pet(pet_id,allow_foster=True)
        from appearance import rabbit_variant
        initial=self.meta.get('pet_initial_appearances',{}).get(pet_id,rabbit_variant(pet_id))
        self.meta.setdefault('pet_appearances',{})[pet_id]=initial
        return f'{p.name}已恢复初始样式。'

    def _act_pet_appearance(self, pet_id, variant):
        p = self._pet(pet_id,allow_foster=True)
        self._validate_appearance(p.species,variant)
        from appearance import pet_variant
        self.meta.setdefault('pet_initial_appearances',{}).setdefault(pet_id,pet_variant(pet_id,self.meta))
        self.meta.setdefault('pet_appearances',{})[pet_id] = variant
        text = f'{p.name}的外观已更新。'
        self.log(text)
        return text

    def _act_rename(self, pet_id, name):
        p = self._pet(pet_id, True)
        name = name.strip()
        if not name or len(name) > C.RENAME_MAX or not name.isprintable():
            raise GameError(f"名字应为1～{C.RENAME_MAX}个可显示字符")
        text = f"{p.name}改名为{name}。"
        p.name = name
        p.note(self.now, text)
        self.log(text)

    def _act_move(self, pet_id, building_id):
        p = self._pet(pet_id, True)
        if p.building_id == building_id:
            raise GameError("已经住在这里了")
        b = self._home(building_id, 1+int(p.pregnant))
        if b.kind == "寄养屋":
            self._pay(C.FOSTER_ENTER_COST)
        p.building_id, p.fostered = building_id, b.kind == "寄养屋"
        text = f"{p.name}{'进入寄养，年龄和属性暂停' if p.fostered else '搬入'+b.kind+'，正常成长'}。"
        p.note(self.now, text)
        self.log(text)
        return text

    def _act_breed(self, first_id, second_id):
        return self.social.conceive(first_id,second_id)

    def _act_pair(self, first_id, second_id):
        return self.social.pair(first_id,second_id)

    def _act_unpair(self, pet_id):
        return self.social.unpair(pet_id)

    def pet_price(self, p):
        stage_factor = C.SELL_STAGE_FACTOR.get(p.stage(self.now), 0)
        shop = max((b.level for b in self.buildings.values() if b.kind == "商店"), default=0)
        base = C.SPECIES[p.species].get("buy_price", C.PET_BUY_COST)
        return int(base*C.SELL_BASE_FACTOR*stage_factor*(C.RARE_PRICE_FACTOR if p.rare else 1)*(1+shop*C.SELL_SHOP_BONUS)*(max(p.health, C.SELL_HEALTH_FLOOR)/100))

    def pet_buy_price(self, species, egg=False):
        """界面展示和实际扣款共用定价；新品种可配置价格与孵化选项。"""
        if species not in C.SPECIES:
            raise GameError("未知品种")
        spec = C.SPECIES[species]
        if egg and not spec.get("egg_available", True):
            raise GameError("该品种只出售幼崽，没有宠物蛋")
        return int(spec.get("buy_price", C.PET_BUY_COST)*(C.EGG_PRICE_FACTOR if egg else 1))

    def _require_building(self, kind, level=1):
        if not any(b.kind == kind and b.level >= level for b in self.buildings.values()):
            raise GameError(f"需要{level}级{kind}")

    def _act_sell_pet(self, pet_id):
        self._require_building("商店")
        p = self._pet(pet_id)
        if self.social.expecting(p):
            raise GameError("孕期内不能出售父母，请等待幼崽出生")
        price = self.pet_price(p)
        p.departure = "出售"
        p.product_ready_ts = p.product_expiry_ts = 0
        p.note(self.now, f"以{price}金币找到新的家庭")
        self.meta["coins"] += price
        self.meta["stats"]["sold"] += 1
        text = f"{p.name}已挂牌售出，获得{price}金币。"
        self.log(text)
        return text

    def _act_buy_pet(self, species, egg=False, sex=None, appearance=None):
        if appearance is not None:
            self._validate_appearance(species,appearance)
        self._require_building("商店")
        price = self.pet_buy_price(species, egg)
        if sex is not None and sex not in C.SEXES:
            raise GameError("性别只能为雌或雄")
        if self.population_with_reservations >= C.PET_LIMIT:
            raise GameError("宠物数量已达上限")
        home = self.find_home(species)
        self._pay(price)
        rare = egg and self.rng.random() < C.MUTATION_CHANCE
        p = Pet.create(species, self.now, home, self.rng, rare=rare, sex=sex)
        if egg:
            p.note(self.now, "从商店宠物蛋中孵化")
        else:
            p.note(self.now, f"在商店花费{price}金币购买")
        self.pets[p.pet_id] = p
        if appearance is not None:
            self.meta.setdefault('pet_appearances',{})[p.pet_id] = appearance
            self.meta.setdefault('pet_initial_appearances',{})[p.pet_id] = appearance
        self.meta["reputation"] += C.EGG_RARE_REPUTATION if rare else 0
        text = f"花费{price}金币{'孵化' if egg else '购买'}新伙伴{p.name}（{p.sex}），生日已记录。"
        self.log(text)
        return text

    def _act_adopt(self):
        if self.now < self.meta["adopt_ready_ts"]:
            raise GameError("公益领养冷却中")
        # 经济失败后的可恢复路径，不直接赠币；仍需有合适建筑与空位。
        available = []
        for species, spec in C.SPECIES.items():
            if spec.get("purchase_only", False):
                continue
            try:
                available.append((species, self.find_home(species)))
            except GameError:
                pass
        if not available or self.population_with_reservations >= C.PET_LIMIT:
            raise GameError("请先腾出适配建筑的空位")
        species, home = self.rng.choice(available)
        p = Pet.create(species, self.now, home, self.rng)
        self.pets[p.pet_id] = p
        self.meta["adopt_ready_ts"] = self.now+C.ADOPTION_COOLDOWN
        self.log(f"公益领养：{p.name}来到庄园。")

    def item_buy_price(self, item):
        factor = C.EVENT_EFFECTS["merchant"]["discount"] if self.meta["discount_until"] > self.now else 1
        if self.meta["inflation_until"] > self.now:
            factor *= C.EVENT_EFFECTS["inflation"]["mult"]
        return max(1, int(C.ITEMS[item]["price"]*factor))

    def _act_buy(self, item, quantity=1):
        self._require_building("商店")
        if item not in C.ITEMS or C.ITEMS[item]["price"] <= 0 or not isinstance(quantity, int) or quantity <= 0:
            raise GameError("该物品不可采购或数量无效")
        self._pay(self.item_buy_price(item)*quantity)
        self.inventory.add(item, quantity, self.now, self.storage_capacity)
        self.log(f"采购{item} ×{quantity}。")

    def _act_sell_item(self, item, quantity=1):
        self._require_building("商店")
        self.inventory.take(item, quantity)
        price = C.ITEMS[item]["sell"]*quantity
        self.meta["coins"] += price
        self.log(f"出售{item} ×{quantity}，收入{price}金币。")

    def _act_discard(self, item, quantity=1):
        self.inventory.take(item, quantity)
        self.log(f"丢弃{item} ×{quantity}。")

    def _act_collect(self, pet_id):
        p = self._pet(pet_id)
        if not p.product_ready_ts or p.product_expiry_ts <= self.now:
            raise GameError("暂无可收取副产品")
        product = C.SPECIES[p.species]["product"]
        self.inventory.add(product, C.PRODUCT_RARE_COUNT if p.rare else C.PRODUCT_NORMAL_COUNT, self.now, self.storage_capacity)
        p.product_ready_ts = p.product_expiry_ts = 0
        self.log(f"收取{p.name}的{product}。")

    def _act_craft(self, item):
        if item not in C.RECIPES:
            raise GameError("未知配方")
        recipe = C.RECIPES[item]
        self._require_building("加工坊", recipe["level"])
        self._pay(recipe["cost"])
        for material, count in recipe["inputs"].items():
            self.inventory.take(material, count)
        self.inventory.add(item, recipe["count"], self.now, self.storage_capacity)
        self.log(f"加工{item} ×{recipe['count']}。")

    def _act_unlock(self, building_id):
        b = self.buildings[building_id]
        if b.unlocked:
            raise GameError("地块已经开放")
        x, y = building_id%C.GRID_COLS, building_id//C.GRID_COLS
        neighbors = [(x-1,y), (x+1,y), (x,y-1), (x,y+1)]
        if not any(0 <= a < C.GRID_COLS and 0 <= c < C.GRID_ROWS and self.buildings[c*C.GRID_COLS+a].unlocked for a,c in neighbors):
            raise GameError("只能解锁与现有区域相邻的地块")
        self._pay(self.unlock_price)
        b.unlocked = True
        self.log(f"解锁地块 {building_id+1}。")

    @property
    def unlock_price(self):
        return C.UNLOCK_BASE + C.UNLOCK_GROWTH*max(0, sum(b.unlocked for b in self.buildings.values())-len(C.INITIAL_OPEN_PLOTS))

    def _act_build(self, building_id, kind):
        b = self.buildings[building_id]
        if not b.unlocked or b.kind or kind not in C.BUILDINGS:
            raise GameError("需要已开放的空地与有效建筑类型")
        self._pay(C.BUILDINGS[kind]["cost"])
        b.kind, b.level = kind, 1
        self.log(f"在地块{building_id+1}建造了{kind}。")

    def _act_upgrade(self, building_id):
        b = self.buildings[building_id]
        if not b.kind or b.level >= C.MAX_LEVEL:
            raise GameError("建筑不可升级")
        self._pay(b.upgrade_cost)
        b.level += 1
        self.log(f"{b.kind}升级至{b.level}级。")

    def _act_demolish(self, building_id):
        b = self.buildings[building_id]
        if not b.kind or self.occupants(building_id):
            raise GameError("请先迁移建筑中的全部宠物")
        new_capacity = self.storage_capacity-(C.STORAGE_PER_LEVEL*b.level if b.kind == "仓库" else 0)
        if self.inventory.used > new_capacity:
            raise GameError("拆除后仓库不足，请先清理物资")
        refund = int(C.BUILDINGS[b.kind]["cost"]*C.DEMOLISH_REFUND)
        self.meta["coins"] += refund
        self.log(f"拆除{b.kind}，返还{refund}金币。")
        b.kind, b.level = "", 0

    def _act_caretaker(self):
        if self.meta["caretaker"]:
            self.meta["caretaker"] = False
            self.log("已解雇饲养员。")
            return "饲养员停止值班。"
        self._pay(C.CARE_HIRE_COST)
        self.meta["caretaker"] = True
        self.meta["next_care_ts"] = self.now+C.CARE_INTERVAL
        self.log(f"雇佣饲养员，今后每3游戏小时支付{C.CARE_WAGE}金币。")

    def _act_farm_manager(self):
        return self.sanitation.hire_toggle()

    def _act_clean_waste(self, waste_id=None, building_id=None):
        return self.sanitation.collect(waste_id,building_id)

    def _act_sell_waste(self, waste_id=None, building_id=None):
        return self.sanitation.sell(waste_id,building_id)

    def _act_sell_manure(self):
        # 在操作前的时间结算之后读取总库存，包含刚被管理员收进来的粪便。
        quantity = self.inventory.count("粪便")
        if not quantity:
            raise GameError("仓库里没有可出售的粪便")
        return self._act_sell_item("粪便",quantity)

    def _act_upgrade_zone(self, zone_id):
        return self.town.upgrade(zone_id)

    def _act_build_roads(self,cells,style='asphalt'):
        return self.construction.edit_roads(cells,style)

    def _act_remove_roads(self,cells):
        return self.construction.edit_roads(cells,remove=True)

    def _act_undo_roads(self):
        return self.construction.undo_road()

    def _act_place_town_building(self,catalog_key,cx,cy,orientation=None):
        return self.construction.place(catalog_key,cx,cy,orientation)

    def _act_remove_town_building(self,building_id):
        return self.construction.remove(building_id)

    def _act_dismiss_build_guide(self):
        self.meta['build_guide_dismissed']=True
        return '建设指引已收起'

    def _act_decoration(self,operation,**kwargs):
        from decorations import edit
        text=edit(self,operation,**kwargs)
        self.log(text)
        return text

    def _act_harvest_field(self):
        return self.town.harvest()

    def _act_order(self, order_id, pet_id=None):
        order = next((o for o in self.orders if o["order_id"] == order_id), None)
        if not order or order["status"] != "进行中" or order["expiry_ts"] <= self.now:
            raise GameError("订单已完成或过期")
        if order["kind"] == "item":
            self.inventory.take(order["item"], order["quantity"])
        else:
            p = self._pet(pet_id)
            if p.species != order["species"] or p.stage(self.now) != order["stage"] or p.unwell:
                raise GameError("需要订单指定品种、年龄阶段且未生病的宠物")
            if self.social.expecting(p):
                raise GameError("孕期内不能交付父母，请等待幼崽出生")
            p.departure = "订单交付"
            p.product_ready_ts = p.product_expiry_ts = 0
            p.note(self.now, f"受{order['npc']}委托，前往新家")
        order["status"] = "完成"
        self.meta["coins"] += order["reward"]
        self.meta["reputation"] += order["reputation"]
        self.meta["stats"]["orders"] += 1
        self.log(f"完成{order['npc']}的订单，获得{order['reward']}金币与{order['reputation']}声望。")

    def _act_event(self, event_id, option):
        entry = next((e for e in self.events if e["event_id"] == event_id), None)
        if not entry or entry["status"] != "待处理" or option not in (0, 1):
            raise GameError("事件已结束或选项无效")
        return self._resolve_event(entry, option)

    def _resolve_event(self, entry, option, automatic=False):
        kind = entry["kind"]
        effect = C.EVENT_EFFECTS[kind]
        pets = [p for p in self.living if not p.fostered]
        if kind == "stray" and option == 0:
            if self.population_with_reservations >= C.PET_LIMIT:
                raise GameError("宠物数量已达上限")
            candidates = []
            for species, spec in C.SPECIES.items():
                if spec.get("purchase_only", False):
                    continue
                try:
                    candidates.append((species, self.find_home(species)))
                except GameError:
                    pass
            if not candidates:
                raise GameError("没有适配建筑的空位")
            self.inventory.take("普通饲料", effect["feed"])
            species, home = self.rng.choice(candidates)
            p = Pet.create(species, self.now, home, self.rng, rare=True)
            self.pets[p.pet_id] = p
            self.meta["reputation"] += effect["rep"]
            self.log(f"收留稀有流浪宠物{p.name}。")
        elif kind == "merchant" and option == 0:
            self.meta["discount_until"] = self.now+C.EVENT_DURATION
        elif kind == "tourists":
            if option == 0:
                self._pay(effect["cost"])
                self.meta["coins"] += effect["reward"]
                self.meta["reputation"] += effect["rep"]
            else:
                self.meta["coins"] += effect["passive_reward"]
        elif kind == "bonus":
            if option == 0:
                product = C.SPECIES[self.rng.choice(pets).species]["product"] if pets else "兔毛"
                self.inventory.add(product, effect["count"], self.now, self.storage_capacity)
            else:
                self.meta["reputation"] += effect["gift_rep"]
        elif kind == "rain":
            if option == 0:
                self._pay(effect["cost"])
            else:
                for p in pets:
                    if self.buildings[p.building_id].outdoor:
                        p.health, p.cleanliness = clamp(p.health+effect["health"]), clamp(p.cleanliness+effect["cleanliness"])
        elif kind == "infection":
            if option == 0:
                self.inventory.take("药品", effect["medicine"])
            else:
                for p in pets:
                    if self.rng.random() < effect["chance"]:
                        p.contract_illness(self.now,self.rng)
        elif kind == "inflation":
            if option == 0:
                self._pay(effect["cost"])
            else:
                self.meta["inflation_until"] = self.now+C.EVENT_DURATION
        elif kind == "scare":
            if option == 0:
                self.inventory.take("零食", effect["snacks"])
            else:
                for p in pets:
                    p.happy = clamp(p.happy+effect["happy"])
        for p in pets:
            if p.health <= 0:
                p.die(self.now, "突发天气导致健康耗尽")
                self.log(f"{p.name}离世，已收入纪念册。")
        entry["status"] = "完成"
        text = ("事件超时自动选择：" if automatic else "事件选择：")+entry["options"][option]
        self.log(text)
        return text

    def todos(self):
        pets = [p for p in self.living if not p.fostered]
        ready_orders = 0
        for o in self.orders:
            if o["status"] != "进行中":
                continue
            if o["kind"] == "item":
                ready_orders += self.inventory.count(o["item"]) >= o["quantity"]
            else:
                ready_orders += any(p.species == o["species"] and p.stage(self.now) == o["stage"] and not p.unwell and not self.social.expecting(p) for p in pets)
        return [f"饥饿宠物：{sum(p.hunger < 30 for p in pets)} 只",
                f"需就医：{sum(p.unwell for p in pets)} 只 / 孕宠：{sum(p.pregnant for p in self.living)} 只",
                f"待收副产品：{sum(bool(p.product_ready_ts) for p in pets)} 份",
                f"{C.EXPIRING_HOURS}小时内过期：{self.inventory.expiring(self.now)} 份",
                f"待处理事件：{sum(e['status'] == '待处理' for e in self.events)} 个",
                f"可交付订单：{ready_orders} 张",
                f"地面粪便：{self.sanitation.quantity()}份 / 管理员：{self.sanitation.status()}",
                f"小镇游客：{sum(p['role'] == '游客' for p in self.people)}人 / 农田：{'待收获' if self.meta['field_ready_ts'] else '暂无成熟作物'}"]

    def close(self, real_now=None):
        self.advance(real_now)
        self.save()
        # SQLite 是日志唯一真源；文本日志可随时由数据库重新导出。
        if self.db.path.resolve() == C.DB_PATH.resolve():
            try:
                with (C.ROOT/"log.txt").open("w", encoding="utf-8") as handle:
                    offset = 0
                    while True:
                        rows = self.db.read_logs(1000, offset)
                        if not rows:
                            break
                        for row in rows:
                            handle.write(f"[{utc_text(row['ts'])}] {row['text']}\n")
                        offset += len(rows)
            except OSError:
                pass  # 可选文本导出失败不影响数据库存档。
        self.db.close()
