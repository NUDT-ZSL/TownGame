"""事件文案与生成。事件效果在 Game 中按单个原子操作结算。"""
import uuid
import game_config as C

E = C.EVENT_EFFECTS
EVENTS = {
    "stray": ("门口出现一只稀有流浪宠物，正期待新的家。", [f"收留（消耗{E['stray']['feed']}份普通饲料）", "让它继续旅行"]),
    "merchant": (f"旅行商人带来特价物资，接受后{C.EVENT_DURATION/C.HOUR:g}小时采购价格为{E['merchant']['discount']:.0%}。", ["欢迎商人", "婉拒来访"]),
    "tourists": ("附近举办庆典，游客们准备来庄园参观。", [f"花{E['tourists']['cost']}金币布置：获得{E['tourists']['reward']}金币及声望", f"自然接待：获得{E['tourists']['passive_reward']}金币"]),
    "bonus": ("宠物们状态不错，发现了一份额外副产品。", ["收进仓库", f"赠送邻居：声望+{E['bonus']['gift_rep']}"]),
    "rain": ("暴雨降温，户外宠物需要保暖。", [f"花{E['rain']['cost']}金币搭雨棚", f"接受淋雨：户外宠物健康{E['rain']['health']}、清洁{E['rain']['cleanliness']}"]),
    "infection": ("附近出现传染病，需要为庄园做一次防护。", [f"消耗{E['infection']['medicine']}份药品预防", "暂不防护：部分宠物可能生病"]),
    "inflation": ("饲料供应紧张，商人希望调整售价。", [f"花{E['inflation']['cost']}金币签保价协议", f"接受{C.EVENT_DURATION/C.HOUR:g}小时采购价格变为{E['inflation']['mult']:.0%}"]),
    "scare": ("远处烟花让宠物们受到了惊吓。", [f"消耗{E['scare']['snacks']}份零食安抚", f"等待恢复：全部非寄养宠物开心{E['scare']['happy']}"]),
}


def create_event(now, reputation, rng):
    kinds = list(EVENTS)
    weights = [1 + min(reputation, C.REPUTATION_RARE_CAP)*C.REPUTATION_RARE_WEIGHT if k in ("stray", "bonus") else 1 for k in kinds]
    kind = rng.choices(kinds, weights=weights)[0]
    text, options = EVENTS[kind]
    return {"event_id": uuid.uuid4().hex, "kind": kind, "text": text, "options": options,
            "created_ts": now, "expiry_ts": now + C.EVENT_DURATION, "status": "待处理"}


def create_orders(now, rng):
    orders = []
    for i in range(C.ORDER_COUNT):
        is_pet = i == C.ORDER_COUNT-1
        item = rng.choice(C.ORDER_ITEM_POOL)
        quantity = rng.randint(*C.ORDER_ITEM_COUNT) if not is_pet else 1
        species = rng.choice(list(C.SPECIES))
        pet_reward = max(C.ORDER_PET_REWARD, int(C.SPECIES[species].get("buy_price", C.PET_BUY_COST)*C.ORDER_PET_PRICE_MULT))
        orders.append({"order_id": uuid.uuid4().hex, "npc": rng.choice(("林阿姨", "邮差小周", "邻居阿禾", "研究员宁宁")),
                       "kind": "pet" if is_pet else "item", "item": item, "quantity": quantity,
                       "species": species, "stage": rng.choice(("幼崽", "少年", "成年")),
                       "reward": pet_reward if is_pet else max(C.ORDER_ITEM_REWARD_MIN, C.ITEMS[item]["sell"]*quantity*C.ORDER_ITEM_REWARD_MULT),
                       "reputation": C.ORDER_PET_REPUTATION if is_pet else C.ORDER_ITEM_REPUTATION, "created_ts": now,
                       "expiry_ts": now+C.ORDER_DURATION, "status": "进行中"})
    return orders
