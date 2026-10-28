"""经营街区与持久化流动人口；人群绘制与模型消费/到离镇时刻相互独立。"""
import uuid
import game_config as C
import town_catalog as K
from weather import seeded_rng

NAMES = ("林悠","周安","陈栀","许岚","沈川","温宁","顾禾","陆星","苏棠","方晴","宋野","叶遥")


class TownSystem:
    def __init__(self, game):
        self.game = game

    def defaults(self, anchor=None, reset=False):
        g = self.game
        anchor = g.now if anchor is None else anchor
        if not g.town_zones:
            g.town_zones = [{"zone_id":k,"level":1 if k == "estate" else 0} for k in C.TOWN_ZONES]
        values = {"town_open_ts":anchor,"town_seed":int(g.meta["start_ts"]),
                  "next_population_ts":anchor+C.TOWN_POP_INTERVAL,"next_town_arrival_ts":anchor+C.TOWN_VISIT_INTERVAL,
                  "next_rent_ts":anchor+C.DAY,"town_visitors_total":0,"town_income":0,
                  "field_ready_ts":0,"field_expiry_ts":0,"next_field_ts":0,"field_batch":{}}
        for k,v in values.items():
            if reset or k not in g.meta:
                g.meta[k] = v
        if not g.people or reset:
            g.people = [self._person("居民",anchor,i) for i in range(C.TOWN_BASE_RESIDENTS)]
            g.people += [self._person("游客",anchor,i) for i in range(2)]
            g.meta["town_visitors_total"] = 2

    def level(self, zone_id):
        return next(z["level"] for z in self.game.town_zones if z["zone_id"] == zone_id)

    def effective_level(self,zone_id):
        # 新档/旧档实体装载过程中先保留旧等级，路网服务随后完成初始化。
        if not hasattr(self.game,'roads') or self.game.roads is None or 'road_revision' not in self.game.meta:
            return self.level(zone_id)
        return self.level(zone_id) if self.game.construction.connected(zone_id) else 0

    @property
    def town_level(self):
        return 1+sum(z["level"] for z in self.game.town_zones if z["zone_id"] != "estate")//3

    def _person(self, role, when, index):
        g = self.game
        rng = seeded_rng(g.meta["town_seed"],when,f"{role}:{index}")
        ident = uuid.uuid5(uuid.NAMESPACE_URL,f"town:{g.meta['town_seed']}:{when:.6f}:{role}:{index}").hex
        destinations = [k for k in ("estate","market","park") if self.effective_level(k)]
        return {"person_id":ident,"name":rng.choice(NAMES),"role":role,
                "home_zone":"homes" if self.effective_level("homes") else "estate",
                "destination":rng.choice(destinations),"entered_ts":when,
                "leave_ts":when+C.TOWN_VISIT_STAY if role == "游客" else 0,
                "exit_ts":when+C.TOWN_VISIT_STAY+C.TOWN_EXIT_TIME if role == "游客" else 0,
                "spend_ts":when+C.TOWN_SPEND_DELAY if role == "游客" else 0,
                "spent":False,"status":"游览" if role == "游客" else "生活",
                "color_index":rng.randrange(6)}

    def upgrade_cost(self, zone_id):
        level = self.level(zone_id)
        return C.TOWN_ZONES[zone_id]["cost"] if not level else int(C.TOWN_ZONES[zone_id]["cost"]*(level+1)*C.TOWN_UPGRADE_FACTOR)

    def upgrade(self, zone_id):
        g = self.game
        if zone_id not in C.TOWN_ZONES or zone_id == "estate":
            raise ValueError("中央庄园请通过原有地块升级建筑")
        level = self.level(zone_id)
        if level >= C.TOWN_MAX_LEVEL:
            raise ValueError("街区已经达到最高等级")
        g._pay(self.upgrade_cost(zone_id))
        next(z for z in g.town_zones if z["zone_id"] == zone_id)["level"] += 1
        if zone_id == "fields" and not level:
            g.meta["next_field_ts"] = g.now+C.TOWN_FIELD_INTERVAL
        self._residents()
        text = f"{C.TOWN_ZONES[zone_id]['name']}{'升级' if level else '开放'}至{level+1}级。"
        g.log(text)
        return text

    def _residents(self):
        g = self.game
        target = C.TOWN_BASE_RESIDENTS+self.effective_level("homes")*C.TOWN_RESIDENTS_PER_LEVEL+self.game.construction.bonuses()['population']
        target=min(C.TOWN_MAX_PEOPLE,target)
        residents = [p for p in g.people if p["role"] == "居民"]
        if len(residents)>target:
            leaving={p['person_id'] for p in residents[target:]}
            g.people=[p for p in g.people if p['person_id'] not in leaving]
            g.log(f'住宅容量减少，{len(leaving)}位居民迁出。')
            residents=residents[:target]
        limit = min(target,len(residents)+max(0,C.TOWN_MAX_PEOPLE-len(g.people)))
        for i in range(len(residents),limit):
            g.people.append(self._person("居民",g.now,i))
        if limit > len(residents):
            g.log(f"居民社区迎来{limit-len(residents)}位新居民。")
        for p in g.people:
            if p["role"] == "居民":
                p["home_zone"] = "homes" if self.effective_level("homes") else "estate"

    def boundaries(self):
        g = self.game
        times = [g.meta["next_population_ts"],g.meta["next_town_arrival_ts"],g.meta["next_rent_ts"]]
        for p in g.people:
            if p["role"] == "游客":
                times += [p["leave_ts"],p["exit_ts"]]
                if not p["spent"]:
                    times.append(p["spend_ts"])
        if g.meta["next_field_ts"]:
            times.append(g.meta["next_field_ts"])
        if g.meta["field_expiry_ts"]:
            times.append(g.meta["field_expiry_ts"])
        return [t for t in times if t > g.now]

    def tick(self):
        g = self.game
        for p in g.people:
            if p["role"] == "游客":
                if not p["spent"] and g.now >= p["spend_ts"]:
                    # 同一游客只消费一次；没有商业街时只参观，不凭空增加收入。
                    income = int(C.TOWN_SPEND_PER_LEVEL*self.effective_level("market")*(1+g.construction.bonuses()['commerce']))
                    p["spent"] = True
                    g.meta["coins"] += income
                    g.meta["town_income"] += income
                    if income:
                        g.log(f"游客{p['name']}在商业街消费，收入{income}金币。")
                if g.now >= p["leave_ts"]:
                    p["status"],p["destination"] = "离镇","station"
        g.people = [p for p in g.people if p["role"] == "居民" or p["exit_ts"] > g.now]
        if g.now >= g.meta["next_population_ts"]:
            self._residents()
            hour = g.now%C.DAY/C.HOUR
            for p in g.people:
                if p["role"] == "居民":
                    if hour < 7 or hour >= 20:
                        p["destination"] = p["home_zone"]
                    elif hour < 17:
                        p["destination"] = "market" if self.effective_level("market") else "estate"
                    else:
                        p["destination"] = "park" if self.effective_level("park") else p["home_zone"]
            g.meta["next_population_ts"] += C.TOWN_POP_INTERVAL
        if g.now >= g.meta["next_town_arrival_ts"]:
            traffic = C.WEATHERS[g.weather.current]["traffic"]
            bonus=g.construction.bonuses()
            count = max(1,int((C.TOWN_BASE_ARRIVALS+2*self.effective_level("station"))*(1+.12*self.effective_level("park"))*(1+min(g.meta["reputation"],100)/200)*traffic*(1+bonus['traffic']+max(-.5,bonus['attraction']*K.ATTRACTION_TRAFFIC)))) if g.construction.connected('station') else 0
            count = min(count,C.TOWN_MAX_PEOPLE-len(g.people))
            when = g.meta["next_town_arrival_ts"]
            for i in range(max(0,count)):
                g.people.append(self._person("游客",when,i))
            g.meta["town_visitors_total"] += max(0,count)
            g.meta["next_town_arrival_ts"] += C.TOWN_VISIT_INTERVAL
            if count > 0:
                g.log(f"{count}位游客抵达小镇，天气：{g.weather.current}。")
        if g.now >= g.meta["next_rent_ts"]:
            if self.effective_level("homes"):
                income = sum(p["role"] == "居民" for p in g.people)*C.TOWN_RENT_PER_PERSON
                g.meta["coins"] += income
                g.meta["town_income"] += income
                g.log(f"居民社区收取租金{income}金币。")
            g.meta["next_rent_ts"] += C.DAY
        if g.meta["field_expiry_ts"] and g.now >= g.meta["field_expiry_ts"]:
            g.meta["field_ready_ts"] = g.meta["field_expiry_ts"] = 0
            g.meta["field_batch"] = {}
            g.log("山麓农田的待收作物超时损失。")
        if g.meta["next_field_ts"] and g.now >= g.meta["next_field_ts"]:
            if not g.meta["field_ready_ts"] and g.construction.connected('fields'):
                factor = C.WEATHERS[g.weather.current]["crop"]*self.level("fields")*(1+g.construction.bonuses()['crops'])
                g.meta["field_batch"] = {"谷物":max(1,round(C.TOWN_FIELD_GRAIN*factor)),"鲜草":max(1,round(C.TOWN_FIELD_GRASS*factor))}
                g.meta["field_ready_ts"] = g.now
                g.meta["field_expiry_ts"] = g.now+C.TOWN_FIELD_TTL
                g.log("山麓农田有作物可以收获。")
            g.meta["next_field_ts"] += C.TOWN_FIELD_INTERVAL

    def harvest(self):
        g = self.game
        if not g.construction.connected('fields'):
            raise ValueError('山麓农田道路未连通，接通后才能收获')
        if not self.level("fields") or not g.meta["field_ready_ts"]:
            raise ValueError("农田尚无可收获作物")
        batch = g.meta["field_batch"]
        if g.inventory.used+sum(batch.values()) > g.storage_capacity:
            raise ValueError("仓库容量不足，作物仍留在农田")
        for item,n in batch.items():
            g.inventory.add(item,n,g.now,g.storage_capacity)
        g.meta["field_batch"] = {}
        g.meta["field_ready_ts"] = g.meta["field_expiry_ts"] = 0
        g.log("收获山麓农田作物并入库。")
        return "农田作物已收获入库。"
