"""按批次保存保质期，先过期先消耗；所有数量为正整数。"""
import uuid
import game_config as C


class Inventory:
    def __init__(self, batches=None):
        self.batches = batches or []

    @property
    def used(self):
        return sum(b["quantity"] for b in self.batches)

    def count(self, item):
        return sum(b["quantity"] for b in self.batches if b["item"] == item)

    def add(self, item, quantity, now, capacity, expiry_ts=None):
        if item not in C.ITEMS or not isinstance(quantity, int) or quantity <= 0:
            raise ValueError("无效物品或数量")
        if self.used + quantity > capacity:
            raise ValueError("仓库容量不足，请出售或丢弃物资，或升级仓库")
        ttl = C.ITEMS[item]["ttl"]
        expiry = expiry_ts if expiry_ts is not None else (now + ttl if ttl else 0)
        for batch in self.batches:
            if batch["item"] == item and batch["expiry_ts"] == expiry:
                batch["quantity"] += quantity
                return
        self.batches.append({"batch_id": uuid.uuid4().hex, "item": item,
                             "quantity": quantity, "expiry_ts": expiry, "created_ts": now})

    def take(self, item, quantity):
        if not isinstance(quantity, int) or quantity <= 0:
            raise ValueError("数量必须为正整数")
        if self.count(item) < quantity:
            raise ValueError(f"{item}不足，需要{quantity}份")
        remaining = quantity
        for batch in sorted(self.batches, key=lambda b: b["expiry_ts"] or float("inf")):
            if batch["item"] == item:
                removed = min(batch["quantity"], remaining)
                batch["quantity"] -= removed
                remaining -= removed
                if not remaining:
                    break
        self.batches = [b for b in self.batches if b["quantity"] > 0]

    def expire(self, now, capacity):
        messages, rotten = [], []
        keep = []
        for batch in self.batches:
            if batch["expiry_ts"] and batch["expiry_ts"] <= now:
                messages.append(f'{batch["item"]} ×{batch["quantity"]} 已过期' + ("并腐烂消失。" if batch["item"] == "腐败物" else "，变为腐败物。"))
                # 不能在重新上线时给几个月前的腐败物重新计算保质期。
                expiry = batch["expiry_ts"] + C.ITEMS["腐败物"]["ttl"]
                if batch["item"] != "腐败物" and expiry > now:
                    rotten.append((batch["quantity"], expiry))
            else:
                keep.append(batch)
        self.batches = keep
        for quantity, expiry in rotten:
            self.add("腐败物", quantity, now, capacity, expiry)
        return messages

    def expiring(self, now):
        return sum(b["quantity"] for b in self.batches if b["expiry_ts"] and 0 < b["expiry_ts"]-now <= C.EXPIRING_HOURS*C.HOUR)
