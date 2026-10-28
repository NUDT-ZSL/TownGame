"""独立宠物实体：不访问数据库或 UI；所有时间都是 UTC 时间轴上的秒。"""
from dataclasses import dataclass, field, asdict
import uuid
import game_config as C


def clamp(value):
    return max(0.0, min(100.0, value))


@dataclass
class Pet:
    pet_id: str
    name: str
    species: str
    sex: str
    birth_ts: float
    last_update_ts: float
    lifespan_max: float
    building_id: int
    personality: str
    health: float = C.PET_INITIAL["health"]
    hunger: float = C.PET_INITIAL["hunger"]
    happy: float = C.PET_INITIAL["happy"]
    cleanliness: float = C.PET_INITIAL["cleanliness"]
    favor: float = C.PET_INITIAL["favor"]
    status: str = "正常"
    rare: bool = False
    breed_ready_ts: float = 0
    paused_seconds: float = 0
    fostered: bool = False
    death_ts: float = 0
    death_reason: str = ""
    departure: str = ""  # 出售/订单交付也保存档案，纪念册仅显示离世。
    product_ready_ts: float = 0
    product_expiry_ts: float = 0
    next_product_ts: float = 0
    interact_ready_ts: float = 0
    illness_budget: float = 1
    last_stage: str = "幼崽"
    history: list = field(default_factory=list)
    mate_id: str = ""
    mother_id: str = ""
    father_id: str = ""
    pregnancy_start_ts: float = 0
    pregnancy_due_ts: float = 0
    pregnancy_father_id: str = ""
    pregnancy_rare: bool = False
    illness_type: str = ""
    injury: float = 0
    treatment_ready_ts: float = 0
    fight_ready_ts: float = 0
    next_excretion_ts: float = 0

    @classmethod
    def create(cls, species, now, building_id, rng, name=None, rare=False, sex=None):
        if sex is not None and sex not in C.SEXES:
            raise ValueError("性别只能为雌或雄")
        ident = uuid.uuid4().hex
        return cls(ident, name or f'{"星光" if rare else "小"}{species}{ident[:3]}', species,
                   sex if sex is not None else rng.choice(C.SEXES), now, now,
                   C.SPECIES[species]["life"] * (C.RARE_LIFE_FACTOR if rare else 1),
                   building_id, rng.choice(C.PERSONALITIES), rare=rare,
                   next_product_ts=now + C.SPECIES[species]["interval"],
                   next_excretion_ts=now + C.SPECIES[species].get("excretion_interval", C.EXCRETION_INTERVAL),
                   illness_budget=rng.expovariate(1), history=[{"ts": now, "text": "来到庄园，开始新的生活。"}])

    @property
    def active(self):
        return not self.death_ts and not self.departure

    @property
    def pregnant(self):
        return bool(self.pregnancy_due_ts)

    @property
    def disease(self):
        # 兼容旧档及外部事件只设置status的行为。
        return self.illness_type or ("普通感染" if self.status == "生病" else "")

    @property
    def unwell(self):
        return bool(self.disease or self.injury > 0)

    @property
    def pregnancy_label(self):
        return C.SPECIES[self.species].get("pregnancy_label", "育卵" if C.SPECIES[self.species]["building"] == "鸟舍" else "怀孕")

    def refresh_status(self, now):
        if self.death_ts:
            self.status = "离世"
        elif self.disease:
            self.status = "生病"
        elif self.injury > 0:
            self.status = "受伤"
        else:
            self.status = "老年" if self.stage(now) == "老年" else "正常"

    def contract_illness(self, when, rng, kind=None):
        if self.disease:
            return
        self.illness_type = kind or rng.choices(list(C.ILLNESSES), weights=[s["weight"] for s in C.ILLNESSES.values()])[0]
        self.status = "生病"
        self.note(when, f"患上{self.illness_type}，需要送医")

    def clear_pregnancy(self):
        self.pregnancy_start_ts = self.pregnancy_due_ts = 0
        self.pregnancy_father_id = ""
        self.pregnancy_rare = False

    def age_seconds(self, now):
        # 寄养期间每次全局刷新把暂停增量记入 paused_seconds；查询不写入状态。
        pending_pause = max(0, now - self.last_update_ts) if self.fostered and self.active else 0
        end = self.death_ts or (self.last_update_ts if self.departure else now)
        return max(0.0, end - self.birth_ts - self.paused_seconds - pending_pause)

    def age_text(self, now):
        minutes = int(self.age_seconds(now) // 60)
        return f"{minutes // 1440}天{minutes // 60 % 24}小时{minutes % 60}分钟"

    def stage(self, now):
        if self.death_ts:
            return "离世"
        ratio = self.age_seconds(now) / self.lifespan_max
        for threshold, label in C.STAGES:
            if ratio < threshold - 1e-12:
                return label
        return "老年"

    def note(self, now, message):
        self.history.append({"ts": now, "text": message})

    def die(self, when, reason):
        if self.pregnant:
            self.note(when, "母体离世，孕期终止")
            self.clear_pregnancy()
        self.death_ts, self.death_reason, self.status = when, reason, "离世"
        self.last_update_ts = when
        self.product_ready_ts = self.product_expiry_ts = 0
        self.note(when, reason)

    def advance(self, now, building, season, rng, environmental_hazard=1.0, environmental_dirt=0.0, weather_dirt=1.0, weather_damage=0.0):
        """精确积分分段线性衰减，在饥饿、肮脏、成长和死亡边界切段。

        不以帧数增加年龄。长时间离线仍计算全部时差；死亡时间落在真正的
        寿命/健康耗尽边界，绝不把重新打开程序的时间当作死亡时间。
        """
        messages = []
        if not self.active or now <= self.last_update_ts:
            return messages
        if self.fostered:
            pause = now - self.last_update_ts
            self.paused_seconds += pause
            if self.pregnant:
                self.pregnancy_due_ts += pause
            if self.next_excretion_ts:
                self.next_excretion_ts += pause
            self.last_update_ts = now
            return messages
        cursor = self.last_update_ts
        fit = building.kind in (C.SPECIES[self.species]["building"], "宠物医院", "游乐区")
        mult = 1 if fit else C.MISMATCH_MULT
        level = building.level
        rates = {key: value * mult / (1 + C.LEVEL_DECAY_BONUS*(level-1)) for key, value in C.DECAY.items()}
        if self.personality == "吃货":
            rates["hunger"] *= C.GLUTTON_HUNGER_MULT
        if season == "夏":
            rates["cleanliness"] *= C.SUMMER_DIRT_MULT
        rates["cleanliness"] += environmental_dirt
        rates["cleanliness"] *= weather_dirt
        while cursor < now and self.active:
            age = self.age_seconds(cursor)
            if age >= self.lifespan_max - 1e-6:
                self.die(cursor, "寿终正寝")
                break
            stage = self.stage(cursor)
            if stage != self.last_stage:
                self.last_stage = stage
                self.note(cursor, f"进入{stage}阶段")
                messages.append((cursor, f"{self.name}进入{stage}阶段。"))
            hours = min((now-cursor)/C.HOUR, (self.lifespan_max-age)/C.HOUR)
            for threshold, _ in C.STAGES:
                boundary = self.lifespan_max * threshold - age
                if boundary > 1e-5:
                    hours = min(hours, boundary/C.HOUR)
            for attr, threshold in (("hunger", C.STARVING), ("cleanliness", C.DIRTY), ("hunger", 0), ("cleanliness", 0), ("happy", 0)):
                gap = getattr(self, attr) - threshold
                if gap > 1e-6:
                    hours = min(hours, gap/rates[attr])
            sick = bool(self.disease)
            health_rate = -C.ILLNESSES[self.disease]["damage"] if sick else 0
            health_rate -= self.injury*C.INJURY_HOURLY_DAMAGE
            health_rate -= weather_damage
            if self.hunger <= C.STARVING + 1e-6:
                health_rate -= C.STARVE_DAMAGE
            if stage == "老年":
                health_rate -= C.OLD_DAMAGE
            if season == "冬" and building.outdoor:
                health_rate -= C.WINTER_DAMAGE
            if not self.unwell and self.hunger > C.STARVING and stage != "老年":
                health_rate += C.HEALTH_RECOVERY
            if building.kind == "宠物医院":
                health_rate += C.HOSPITAL_RECOVERY*level
            hazard = C.ILLNESS_HOURLY * environmental_hazard
            if self.cleanliness <= C.DIRTY + 1e-6:
                hazard *= C.DIRTY_ILLNESS_MULT
            if stage == "老年":
                hazard *= C.OLD_ILLNESS_MULT
            if not fit:
                hazard *= C.MISMATCH_ILLNESS_MULT
            if not sick:
                hours = min(hours, max(1e-9, self.illness_budget/hazard))
            if health_rate < 0:
                hours = min(hours, max(0, self.health/-health_rate))
            if hours < 1e-12:
                self.die(cursor, "健康耗尽")
                break
            for attr, rate in rates.items():
                setattr(self, attr, clamp(getattr(self, attr)-rate*hours))
            self.health = clamp(self.health + health_rate*hours)
            cursor = min(now, cursor + hours*C.HOUR)
            if not sick:
                self.illness_budget -= hazard*hours
                if self.illness_budget <= 1e-8:
                    self.contract_illness(cursor, rng)
                    messages.append((cursor, f"{self.name}患上{self.disease}，需要送医。"))
            if self.health <= 1e-7:
                self.die(cursor, f"{self.disease}恶化" if self.disease else ("伤势恶化" if self.injury else "健康耗尽"))
            elif self.age_seconds(cursor) >= self.lifespan_max - 1e-5:
                self.die(cursor, "寿终正寝")
        if self.active:
            self.last_update_ts = now
            self.refresh_status(now)
            current_stage = self.stage(now)
            if self.last_stage != current_stage:
                self.last_stage = current_stage
                self.note(now, f"进入{current_stage}阶段")
                messages.append((now, f"{self.name}进入{current_stage}阶段。"))
        else:
            messages.append((self.death_ts, f"{self.name}离世（{self.death_reason}），已收入纪念册。"))
        return messages

    def to_dict(self):
        return asdict(self)

    @classmethod
    def from_dict(cls, data):
        return cls(**data)
