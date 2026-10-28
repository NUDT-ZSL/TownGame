"""庄园全部平衡参数。时间单位为秒，数值速率以游戏小时计。"""
from pathlib import Path

ROOT = Path(__file__).resolve().parent
DB_PATH = ROOT / "manor.db"
HOUR, DAY = 3600, 86400
WINDOW = (1280, 860)
FPS = 30
SPEED = 120
SAVE_INTERVAL = 10
STEP = HOUR
INITIAL_COINS = 3600
GRID_COLS, GRID_ROWS = 5, 4
UNLOCK_BASE = 180
MAX_LEVEL = 5
BASE_STORAGE = 120
STORAGE_PER_LEVEL = 100
SEASON_DAYS = 7
SEASONS = ("春", "夏", "秋", "冬")
WORLD_SIZE = (3600,2400)
FARM_WORLD_RECT = (1160,760,1280,616)
WEATHER_INTERVAL = 4*HOUR
WEATHERS = {
    "晴天":{"traffic":1.0,"dirt":1.0,"health":0,"crop":1.0},
    "阴天":{"traffic":.95,"dirt":1.0,"health":0,"crop":1.0},
    "大风":{"traffic":.80,"dirt":1.15,"health":.025,"crop":.9},
    "小雨":{"traffic":.75,"dirt":1.20,"health":.02,"crop":1.15},
    "暴雨":{"traffic":.45,"dirt":1.45,"health":.07,"crop":.7},
    "雨夹雪":{"traffic":.50,"dirt":1.30,"health":.09,"crop":.65},
    "降雪":{"traffic":.65,"dirt":1.10,"health":.08,"crop":.6},
}
WEATHER_WEIGHTS = {"春":(38,22,12,20,6,2,0),"夏":(40,14,10,23,13,0,0),
                   "秋":(35,25,22,13,4,1,0),"冬":(24,20,12,4,2,18,20)}
# 交通沿用同一天气字典，视觉车速与玩法客流使用不同的明确倍率。
for _weather,_speed in zip(WEATHERS,(1.,1.,.85,.8,.65,.55,.6)):
    WEATHERS[_weather]['speed']=_speed
TOWN_POP_INTERVAL = HOUR
TOWN_VISIT_INTERVAL = 2*HOUR
TOWN_VISIT_STAY = 4*HOUR
TOWN_EXIT_TIME = 3*HOUR
TOWN_SPEND_DELAY = HOUR
TOWN_BASE_RESIDENTS = 4
TOWN_RESIDENTS_PER_LEVEL = 8
TOWN_BASE_ARRIVALS = 3
TOWN_MAX_PEOPLE = 80
TOWN_RENT_PER_PERSON = 2
TOWN_SPEND_PER_LEVEL = 6
TOWN_MAX_LEVEL = 3
TOWN_UPGRADE_FACTOR = .75
TOWN_FIELD_INTERVAL = 12*HOUR
TOWN_FIELD_TTL = 12*HOUR
TOWN_FIELD_GRAIN = 4
TOWN_FIELD_GRASS = 2
TOWN_ZONES = {
    "estate":{"name":"中央庄园","cost":0,"rect":FARM_WORLD_RECT,"color":(166,197,133),"feature":"宠物养成、繁育、医院与原有20块地块"},
    "market":{"name":"晨光商业街","cost":900,"rect":(2520,240,930,650),"color":(217,173,145),"feature":"游客在商店消费，升级提高每笔消费收入"},
    "homes":{"name":"居民社区","cost":1100,"rect":(130,160,930,780),"color":(179,191,215),"feature":"吸引常住居民，每游戏日按人口收取租金"},
    "park":{"name":"柳岸公园","cost":600,"rect":(130,1090,930,1070),"color":(145,190,176),"feature":"增加游客吸引力，并提升宠物互动开心收益"},
    "station":{"name":"车站广场","cost":1400,"rect":(1260,100,1040,510),"color":(208,181,137),"feature":"更多游客乘车抵达，坏天气仍保留部分客流"},
    "fields":{"name":"山麓农田","cost":750,"rect":(2520,1150,930,1010),"color":(188,198,126),"feature":"定时生产谷物和鲜草，手动收获入库，天气影响产量"},
}
CARE_INTERVAL = 3 * HOUR
CARE_HIRE_COST = 260
CARE_WAGE = 8
EXCRETION_INTERVAL = 6 * HOUR
WASTE_UNSAFE_AFTER = 12 * HOUR
WASTE_FRESH_DIRT = .03
WASTE_STALE_DIRT = .14
WASTE_MAX_DIRT = 2.0
WASTE_DISEASE_PER_UNIT = 1.2
WASTE_MAX_HAZARD_BONUS = 18
MANAGER_NAME = "小林"
MANAGER_HIRE_COST = 180
MANAGER_WAGE = 6
MANAGER_WAGE_INTERVAL = 6 * HOUR
MANAGER_TRAVEL_SECONDS = 6
MANAGER_SWEEP_SECONDS = 8
MANAGER_SCAN_INTERVAL = HOUR
MANAGER_RETRY_INTERVAL = HOUR / 2
MANAGER_HOME = (.88, .30)
MAX_DRAWN_WASTE = 48
VISITOR_INTERVAL = 6 * HOUR
VISITOR_BASE = 18
EVENT_INTERVAL = 8 * HOUR
EVENT_CHANCE = .35
EVENT_DURATION = 12 * HOUR
ORDER_INTERVAL = DAY
ORDER_DURATION = 2 * DAY
ORDER_COUNT = 3
BREED_COST = 100
BREED_FAVOR = 35
BREED_COOLDOWN = 2 * DAY
SEXES = ("雌", "雄")
CONCEPTION_CHANCE = .85
PREGNANCY_DURATION = DAY
CONCEPTION_RETRY = 6 * HOUR
FIGHT_INTERVAL = 6 * HOUR
FIGHT_CHANCE = .04
FIGHT_HUNGER_BONUS = .12
FIGHT_UNHAPPY_BONUS = .08
FIGHT_UNHAPPY_THRESHOLD = 25
FIGHT_CROWD_BONUS = .04
FIGHT_MATE_FACTOR = .25
FIGHT_PERSONALITY = {"粘人": .8, "高冷": 1.3, "胆小": .65, "吃货": 1.1, "活泼": 1.0}
FIGHT_DAMAGE = (8, 22)
FIGHT_INJURY = (10, 25)
FIGHT_HAPPY_LOSS = 12
FIGHT_COOLDOWN = 12 * HOUR
INJURY_HOURLY_DAMAGE = .02  # 每点伤势每游戏小时的额外健康损失。
TREATMENT_COOLDOWN = 6 * HOUR
MEDICAL_LEVEL_BONUS = .08
PALLIATIVE_HEAL = 5
SICK_DAMAGE = .50
ILLNESSES = {
    "普通感染": {"weight": 80, "damage": SICK_DAMAGE, "cure": 1.0, "failure_damage": 2, "fatality": 0},
    "重症感染": {"weight": 17, "damage": 1.2, "cure": .45, "failure_damage": 8, "fatality": .10},
    "绝症": {"weight": 3, "damage": 1.8, "cure": 0, "failure_damage": 0, "fatality": 0},
}
INJURY_TREATMENT = {"cure": .90, "failure_damage": 5, "fatality": .02}
MUTATION_CHANCE = .08
RARE_LIFE_FACTOR = 1.5
RARE_PRICE_FACTOR = 3
FOSTER_ENTER_COST = 40
PET_BUY_COST = 160
ADOPTION_COOLDOWN = DAY
RENAME_MAX = 16
INTERACT_COOLDOWN = HOUR
PET_LIMIT = 100
STAGES = ((.10, "幼崽"), (.25, "少年"), (.80, "成年"), (1.0, "老年"))
PERSONALITIES = ("粘人", "高冷", "胆小", "吃货", "活泼")
FEEDBACK = {
    "粘人": "扑进你的怀里，久久不肯离开。", "高冷": "悄悄靠近，假装只是路过。",
    "胆小": "小心翼翼地蹭了蹭你的手心。", "吃货": "翻着你的口袋，期待下一份零食。",
    "活泼": "围着你蹦跳，邀请你一起追逐玩具。",
}
DECAY = {"hunger": .90, "happy": .38, "cleanliness": .60}
STARVING = 20
STARVE_DAMAGE = 1.8
DIRTY = 25
OLD_DAMAGE = .07
HEALTH_RECOVERY = .06
ILLNESS_HOURLY = .0008
DIRTY_ILLNESS_MULT = 12
OLD_ILLNESS_MULT = 8
MISMATCH_MULT = 1.8
SUMMER_DIRT_MULT = 1.5
WINTER_DAMAGE = .12
PLAY_FAVOR = 8
PLAY_HAPPY = 18
BATH_GAIN = 55
MEDICINE_HEALTH = 35
PRODUCT_TTL = 18 * HOUR
INITIAL_OPEN_PLOTS = (0, 1, 2, 5, 6, 7)
INITIAL_BUILDINGS = {0: "饲养小屋", 1: "仓库", 2: "商店"}
PET_INITIAL = {"health": 100, "hunger": 85, "happy": 80, "cleanliness": 90, "favor": 20}
LEVEL_DECAY_BONUS = .06
GLUTTON_HUNGER_MULT = 1.2
MISMATCH_ILLNESS_MULT = 2
HOSPITAL_RECOVERY = .08
HOSPITAL_HEAL_BONUS = 3
TREATMENT_FEE = 12
HOSPITAL_FEE_REDUCTION = 4
PLAY_PERSONALITY_MULT = {"粘人": 1.3, "高冷": .75, "胆小": .9, "吃货": 1, "活泼": 1.0}
PLAY_PARK_BONUS = 2
PLAY_PARK_MAX = 10
BREED_MIN_HEALTH = 50
BREED_MATERIALS = {"普通饲料": 3, "清水": 2}
BREED_REPUTATION = 1
BREED_RARE_REPUTATION = 3
EGG_PRICE_FACTOR = .85
EGG_RARE_REPUTATION = 2
CARE_FEED_THRESHOLD = 70
CARE_CLEAN_THRESHOLD = 65
CARE_CLEAN_GAIN = 35
VISITOR_PER_PET = 2
VISITOR_SHOP_BONUS = .15
VISITOR_PARK_BONUS = .10
SELL_BASE_FACTOR = .8
SELL_STAGE_FACTOR = {"幼崽": 1, "少年": 1.2, "成年": 1.5, "老年": .7}
SELL_SHOP_BONUS = .08
SELL_HEALTH_FLOOR = 10
UNLOCK_GROWTH = 30
UPGRADE_PRICE_FACTOR = .8
DEMOLISH_REFUND = .3
PRODUCT_NORMAL_COUNT = 1
PRODUCT_RARE_COUNT = 2
EXPIRING_HOURS = 6
OPEN_EVENT_COOLDOWN = HOUR
REPUTATION_RARE_WEIGHT = .035
REPUTATION_RARE_CAP = 100
ORDER_ITEM_POOL = ("兔毛", "羊奶", "鸟蛋", "高级饲料", "普通饲料", "绒毛")
ORDER_ITEM_COUNT = (2, 5)
ORDER_PET_REWARD = 240
ORDER_PET_PRICE_MULT = 1.5
ORDER_ITEM_REWARD_MIN = 40
ORDER_ITEM_REWARD_MULT = 2
ORDER_PET_REPUTATION = 5
ORDER_ITEM_REPUTATION = 2
EVENT_EFFECTS = {
    "stray": {"feed": 5, "rep": 3},
    "merchant": {"discount": .8},
    "tourists": {"cost": 30, "reward": 120, "rep": 2, "passive_reward": 40},
    "bonus": {"count": 2, "gift_rep": 3},
    "rain": {"cost": 35, "health": -8, "cleanliness": -15},
    "infection": {"medicine": 2, "chance": .4},
    "inflation": {"cost": 25, "mult": 1.5},
    "scare": {"snacks": 3, "happy": -18},
}

SPECIES = {
    "兔子": {"life": 30*DAY, "building": "饲养小屋", "product": "兔毛", "interval": 12*HOUR, "color": (239, 213, 185)},
    "绵羊": {"life": 45*DAY, "building": "牧场", "product": "羊奶", "interval": 12*HOUR, "color": (229, 230, 222)},
    "鹦鹉": {"life": 36*DAY, "building": "鸟舍", "product": "鸟蛋", "interval": 18*HOUR, "color": (142, 210, 180)},
    "狐狸": {"life": 40*DAY, "building": "饲养小屋", "product": "绒毛", "interval": 18*HOUR, "color": (235, 165, 110)},
    # 新品种的首只只能在商店购买；繁育仍使用通用规则，原有免费品种不变。
    "猫咪": {"life": 40*DAY, "building": "饲养小屋", "product": "猫毛", "interval": 16*HOUR,
             "color": (215, 168, 118), "buy_price": 240, "purchase_only": True, "egg_available": False},
    "小狗": {"life": 42*DAY, "building": "饲养小屋", "product": "犬毛", "interval": 16*HOUR,
             "color": (190, 143, 102), "buy_price": 260, "purchase_only": True, "egg_available": False},
    "仓鼠": {"life": 24*DAY, "building": "饲养小屋", "product": "软绒", "interval": 10*HOUR,
             "color": (231, 197, 149), "buy_price": 180, "purchase_only": True, "egg_available": False},
    "刺猬": {"life": 32*DAY, "building": "饲养小屋", "product": "森林果实", "interval": 16*HOUR,
             "color": (155, 124, 98), "buy_price": 300, "purchase_only": True, "egg_available": False},
    "羊驼": {"life": 48*DAY, "building": "牧场", "product": "羊驼绒", "interval": 18*HOUR,
             "color": (235, 222, 196), "buy_price": 420, "purchase_only": True, "egg_available": False},
    "梅花鹿": {"life": 50*DAY, "building": "牧场", "product": "林间香草", "interval": 14*HOUR,
               "color": (193, 139, 92), "buy_price": 480, "purchase_only": True, "egg_available": False},
    "小鸭": {"life": 28*DAY, "building": "鸟舍", "product": "鸭蛋", "interval": 12*HOUR,
             "color": (247, 221, 119), "buy_price": 200, "purchase_only": True, "egg_available": True},
    "孔雀": {"life": 46*DAY, "building": "鸟舍", "product": "孔雀羽", "interval": 24*HOUR,
             "color": (83, 165, 169), "buy_price": 600, "purchase_only": True, "egg_available": True},
}
# 保持旧成就条件稳定：更新后无需重新获得原来的“四季伙伴”。
CLASSIC_SPECIES = ("兔子", "绵羊", "鹦鹉", "狐狸")

BUILDINGS = {
    "饲养小屋": {"cost": 180, "capacity": 4, "growth": 2, "outdoor": False, "color": (195, 158, 119)},
    "牧场": {"cost": 220, "capacity": 4, "growth": 2, "outdoor": True, "color": (110, 165, 112)},
    "鸟舍": {"cost": 200, "capacity": 4, "growth": 2, "outdoor": True, "color": (113, 168, 184)},
    "宠物医院": {"cost": 350, "capacity": 2, "growth": 1, "outdoor": False, "color": (197, 134, 149)},
    "仓库": {"cost": 200, "capacity": 0, "growth": 0, "outdoor": False, "color": (160, 155, 195)},
    "加工坊": {"cost": 280, "capacity": 0, "growth": 0, "outdoor": False, "color": (182, 156, 106)},
    "商店": {"cost": 250, "capacity": 0, "growth": 0, "outdoor": False, "color": (201, 157, 168)},
    "游乐区": {"cost": 200, "capacity": 3, "growth": 2, "outdoor": True, "color": (160, 185, 114)},
    "寄养屋": {"cost": 380, "capacity": 3, "growth": 2, "outdoor": False, "color": (123, 157, 198)},
}

# 鲜货过期后转为腐败物，腐败物再过一天消失；保留一次误喂风险和清理玩法。
ITEMS = {
    "普通饲料": {"price": 5, "sell": 2, "ttl": 0, "feed": {"hunger": 30}},
    "鲜草": {"price": 4, "sell": 1, "ttl": 2*DAY, "feed": {"hunger": 23, "happy": 2}},
    "高级饲料": {"price": 16, "sell": 8, "ttl": 0, "feed": {"hunger": 55, "health": 4, "happy": 5}},
    "零食": {"price": 10, "sell": 4, "ttl": 0, "feed": {"hunger": 10, "happy": 16, "favor": 3, "health": -2}},
    "特制零食": {"price": 25, "sell": 12, "ttl": 3*DAY, "feed": {"hunger": 15, "happy": 25, "favor": 7}},
    "清水": {"price": 2, "sell": 1, "ttl": 0},
    "沐浴露": {"price": 7, "sell": 3, "ttl": 0},
    "药品": {"price": 18, "sell": 7, "ttl": 0},
    "谷物": {"price": 3, "sell": 1, "ttl": 0},
    "兔毛": {"price": 0, "sell": 12, "ttl": 0},
    "绒毛": {"price": 0, "sell": 16, "ttl": 0},
    "羊奶": {"price": 0, "sell": 14, "ttl": DAY},
    "鸟蛋": {"price": 0, "sell": 15, "ttl": 2*DAY},
    "猫毛": {"price": 0, "sell": 18, "ttl": 0},
    "犬毛": {"price": 0, "sell": 20, "ttl": 0},
    "软绒": {"price": 0, "sell": 10, "ttl": 0},
    "森林果实": {"price": 0, "sell": 18, "ttl": 2*DAY},
    "羊驼绒": {"price": 0, "sell": 28, "ttl": 0},
    "林间香草": {"price": 0, "sell": 18, "ttl": 3*DAY},
    "鸭蛋": {"price": 0, "sell": 12, "ttl": 2*DAY},
    "孔雀羽": {"price": 0, "sell": 32, "ttl": 0},
    "粪便": {"price": 0, "sell": 2, "ttl": 0},
    "腐败物": {"price": 0, "sell": 0, "ttl": DAY, "feed": {"hunger": 5, "health": -18}},
}
RECIPES = {
    "高级饲料": {"inputs": {"谷物": 3, "清水": 1}, "count": 2, "cost": 3, "level": 1},
    "特制零食": {"inputs": {"羊奶": 1, "鸟蛋": 1, "谷物": 2}, "count": 2, "cost": 5, "level": 2},
}
STARTER_ITEMS = {"普通饲料": 25, "清水": 18, "沐浴露": 12, "药品": 6, "谷物": 15, "零食": 5}
ORDER_ITEM_POOL += tuple(spec["product"] for spec in SPECIES.values() if spec.get("purchase_only", False))
ACHIEVEMENTS = {
    "first_breed": ("新生命", "养成", "第一次成功繁育", 120, 3),
    "rare": ("闪光伙伴", "收集", "拥有一只变异宠物", 200, 5),
    "collector": ("四季伙伴", "收集", "结识兔子、绵羊、鹦鹉和狐狸", 200, 5),
    "all_species": ("庄园百兽册", "收集", f"结识全部{len(SPECIES)}个品种", 500, 10),
    "old_friend": ("相伴一生", "养成", "陪伴一只宠物寿终正寝", 200, 5),
    "builder": ("庄园建筑师", "经营", "拥有六座建筑", 180, 4),
    "orders": ("可靠邻居", "经营", "累计完成三个订单", 160, 4),
    "friends": ("心有灵犀", "养成", "任意宠物好感达到80", 120, 3),
    "seller": ("小小经营家", "经营", "累计出售三只宠物", 150, 4),
}
