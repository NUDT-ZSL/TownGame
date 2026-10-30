"""排泄、环境卫生、粪便交易与农场管理员。只处理游戏状态，不依赖Pygame。"""
import uuid
import game_config as C


class SanitationSystem:
    def __init__(self, game):
        self.game = game

    def defaults(self):
        g = self.game
        values = {"farm_manager":False, "manager_job":None, "manager_status":"未雇佣",
                  "manager_next_wage_ts":g.now+C.MANAGER_WAGE_INTERVAL,
                  "manager_next_scan_ts":g.now, "manager_cleaned":0,
                  "manager_fed":0, "manager_last_feed_ts":0,
                  "manager_u":C.MANAGER_HOME[0], "manager_v":C.MANAGER_HOME[1],
                  "manager_last_clean_ts":0}
        for key,value in values.items():
            g.meta.setdefault(key,value)

    def piles(self, building_id=None):
        return [w for w in self.game.waste if building_id is None or w["building_id"] == building_id]

    def quantity(self, building_id=None):
        return sum(w["quantity"] for w in self.piles(building_id))

    def exposure(self, building_id, when=None):
        when = self.game.now if when is None else when
        piles = self.piles(building_id)
        total = sum(w["quantity"] for w in piles)
        stale = sum(w["quantity"] for w in piles if when-w["created_ts"] >= C.WASTE_UNSAFE_AFTER)
        return self._environment(total,stale)

    @staticmethod
    def _environment(total, stale):
        return {"total":total, "stale":stale,
                "hazard":1+min(C.WASTE_MAX_HAZARD_BONUS,stale*C.WASTE_DISEASE_PER_UNIT),
                "dirt":min(C.WASTE_MAX_DIRT,total*C.WASTE_FRESH_DIRT+stale*C.WASTE_STALE_DIRT)}

    def exposure_map(self, when):
        # 每个结算片段汇总一次，避免每只宠物都重新扫描长期积累的全部粪便。
        counts = {bid:[0,0] for bid in self.game.buildings}
        for w in self.game.waste:
            values = counts[w["building_id"]]
            values[0] += w["quantity"]
            if when-w["created_ts"] >= C.WASTE_UNSAFE_AFTER:
                values[1] += w["quantity"]
        return {bid:self._environment(*values) for bid,values in counts.items()}

    def boundaries(self):
        g = self.game
        times = [p.next_excretion_ts for p in g.living if not p.fostered and p.next_excretion_ts > g.now]
        times += [w["created_ts"]+C.WASTE_UNSAFE_AFTER for w in g.waste if w["created_ts"]+C.WASTE_UNSAFE_AFTER > g.now]
        if g.meta["farm_manager"]:
            times.append(g.meta["manager_next_wage_ts"])
            job = g.meta["manager_job"]
            times.append(job["end_ts"] if job else g.meta["manager_next_scan_ts"])
        return times

    def emit(self):
        g = self.game
        for p in g.living:
            if p.fostered:
                continue
            while p.next_excretion_ts and p.next_excretion_ts <= g.now:
                when = p.next_excretion_ts
                ident = uuid.uuid5(uuid.NAMESPACE_URL,f"manor:waste:{p.pet_id}:{when:.6f}").hex
                # 坐标由来源和时间确定并保存，不消耗游戏RNG，也不依赖庭院宠物动画。
                u = .10 + int(ident[:8],16)/0xffffffff*.80
                v = .25 + int(ident[8:16],16)/0xffffffff*.60
                g.waste.append({"waste_id":ident,"pet_id":p.pet_id,"building_id":p.building_id,
                                "quantity":1,"created_ts":when,"u":u,"v":v})
                g.log(f"{p.name}在地块{p.building_id+1}排泄了一份粪便，需要清理。",when)
                p.next_excretion_ts += C.SPECIES[p.species].get("excretion_interval",C.EXCRETION_INTERVAL)

    def hire_toggle(self):
        g = self.game
        if g.meta["farm_manager"]:
            self._remember_position()
            g.meta["farm_manager"] = False
            g.meta["manager_job"] = None
            g.meta["manager_status"] = "已解雇"
            text = "已解雇农场管理员；尚未清理的粪便仍留在地面。"
        else:
            g._pay(C.MANAGER_HIRE_COST)
            g.meta["farm_manager"] = True
            g.meta["manager_next_wage_ts"] = g.now+C.MANAGER_WAGE_INTERVAL
            g.meta["manager_next_scan_ts"] = g.now
            g.meta["manager_status"] = "巡查"
            self.wake()
            text = f"雇佣农场管理员{C.MANAGER_NAME}，每{C.MANAGER_WAGE_INTERVAL/C.HOUR:g}游戏小时工资{C.MANAGER_WAGE}金币。"
        g.log(text)
        return text

    def selected(self, waste_id=None, building_id=None):
        if building_id is not None and building_id not in self.game.buildings:
            raise ValueError("无效地块")
        piles = [w for w in self.piles(building_id) if waste_id is None or w["waste_id"] == waste_id]
        if not piles:
            raise ValueError("没有待清理的粪便，可能已经被管理员收走")
        return piles

    def collect(self, waste_id=None, building_id=None, by_manager=False):
        g = self.game
        piles = self.selected(waste_id,building_id)
        quantity = sum(w["quantity"] for w in piles)
        # 先检查仓容再移除地面物资；失败不丢粪便、不解除尚未完成的工作。
        g.inventory.add("粪便",quantity,g.now,g.storage_capacity)
        self._remove(piles)
        if by_manager:
            g.meta["manager_cleaned"] += quantity
            g.meta["manager_last_clean_ts"] = g.now
        text = f"{'农场管理员'+C.MANAGER_NAME if by_manager else '你'}清扫收集粪便 ×{quantity}，已放入仓库，可出售。"
        g.log(text)
        return text

    def sell(self, waste_id=None, building_id=None):
        g = self.game
        g._require_building("商店")
        piles = self.selected(waste_id,building_id)
        quantity = sum(w["quantity"] for w in piles)
        value = quantity*C.ITEMS["粪便"]["sell"]
        self._remove(piles)
        g.meta["coins"] += value
        text = f"清理并就地出售粪便 ×{quantity}，获得{value}金币。"
        g.log(text)
        return text

    def _remove(self, piles):
        g = self.game
        ids = {w["waste_id"] for w in piles}
        g.waste = [w for w in g.waste if w["waste_id"] not in ids]
        job = g.meta["manager_job"]
        if job and job.get("waste_id") in ids:
            self._remember_position()
            g.meta["manager_job"] = None
            g.meta["manager_next_scan_ts"] = g.now

    def wake(self):
        g = self.game
        job = g.meta["manager_job"]
        if job and job.get("kind") == "feed" and not self._feed_target(job):
            self._remember_position()
            g.meta["manager_job"] = None
        if not g.meta["farm_manager"] or g.meta["manager_job"]:
            return
        piles = sorted(g.waste,key=lambda w:(w["created_ts"],w["waste_id"]))
        hungry = sorted((p for p in g.living if not p.fostered and p.hunger < C.CARE_FEED_THRESHOLD),
                        key=lambda p:(p.hunger,p.pet_id))
        feed = hungry[0] if hungry and g.inventory.count("普通饲料") else None
        clean = next((w for w in piles if g.inventory.used+w["quantity"] <= g.storage_capacity),None)
        # 先处理非常饥饿的动物，其余时间在卫生和喂食之间按需求工作。
        if feed and (feed.hunger < 35 or clean is None):
            # 固定食盆在庭院 (318, 668)，饥饿动物的自主行为也会前往这里。
            target = {"kind":"feed","pet_id":feed.pet_id,"building_id":feed.building_id,
                      "u":(318-210)/860,"v":(668-552)/124}
        elif clean:
            target = dict(clean,kind="clean")
        else:
            reasons = []
            if piles: reasons.append("仓库已满，等待腾空")
            if hungry: reasons.append("普通饲料不足，等待补货")
            g.meta["manager_status"] = "；".join(reasons) if reasons else "休息待命"
            g.meta["manager_next_scan_ts"] = g.now+(C.MANAGER_RETRY_INTERVAL if reasons else C.MANAGER_SCAN_INTERVAL)
            return
        g.meta["manager_status"] = "工作中"
        g.meta["manager_job"] = {"kind":target["kind"],"waste_id":target.get("waste_id",""),
                                 "pet_id":target.get("pet_id",""),"building_id":target["building_id"],
                                 "u":target["u"],"v":target["v"],
                                 "from_u":g.meta["manager_u"],"from_v":g.meta["manager_v"],
                                 "start_ts":g.now,"sweep_ts":g.now+C.MANAGER_TRAVEL_SECONDS,
                                 "end_ts":g.now+C.MANAGER_TRAVEL_SECONDS+C.MANAGER_SWEEP_SECONDS}

    def _feed_target(self, job):
        p = self.game.pets.get(job.get("pet_id"))
        return p if p and p in self.game.living and not p.fostered and p.building_id == job["building_id"] and p.hunger < C.CARE_FEED_THRESHOLD else None

    def _remember_position(self):
        g = self.game
        job = g.meta.get("manager_job")
        if not job:
            return
        ratio = max(0,min(1,(g.now-job["start_ts"])/max(.001,job["sweep_ts"]-job["start_ts"])))
        for axis in ("u","v"):
            g.meta["manager_"+axis] = job["from_"+axis]+(job[axis]-job["from_"+axis])*ratio

    def tick(self):
        g = self.game
        self.emit()
        if not g.meta["farm_manager"]:
            return
        if g.now >= g.meta["manager_next_wage_ts"]:
            if g.meta["coins"] < C.MANAGER_WAGE:
                self._remember_position()
                g.meta["farm_manager"] = False
                g.meta["manager_job"] = None
                g.meta["manager_status"] = "工资不足，已停工"
                g.log("金币不足支付农场管理员工资，已停止清扫。")
                return
            g.meta["coins"] -= C.MANAGER_WAGE
            g.meta["manager_next_wage_ts"] += C.MANAGER_WAGE_INTERVAL
            g.log(f"支付农场管理员工资{C.MANAGER_WAGE}金币。")
        job = g.meta["manager_job"]
        if job and g.now >= job["end_ts"]:
            g.meta["manager_u"],g.meta["manager_v"] = job["u"],job["v"]
            try:
                if job.get("kind") == "feed":
                    pet = self._feed_target(job)
                    if pet:
                        g._act_feed(pet.pet_id)
                        g.meta["manager_fed"] += 1
                        g.meta["manager_last_feed_ts"] = g.now
                        g.log(f"农场管理员{C.MANAGER_NAME}喂养了{pet.name}。")
                    g.meta["manager_job"] = None
                else:
                    self.collect(waste_id=job["waste_id"],by_manager=True)
            except ValueError as error:
                g.meta["manager_job"] = None
                g.meta["manager_status"] = str(error)
                g.meta["manager_next_scan_ts"] = g.now+C.MANAGER_RETRY_INTERVAL
        # 动物可能在两次扫描间离园、死亡或已被其他照护者喂饱。
        self.wake()

    def status(self):
        g = self.game
        job = g.meta["manager_job"]
        if g.meta["farm_manager"] and job:
            if job.get("kind") == "feed":
                return "前往喂食" if g.now < job["sweep_ts"] else "喂食中"
            return "前往清扫" if g.now < job["sweep_ts"] else "清扫中"
        return g.meta["manager_status"]
