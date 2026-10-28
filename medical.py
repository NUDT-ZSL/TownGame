"""住院、诊断与治疗。失败/病危是实际游戏结果，必须正常提交存档。"""
import game_config as C
from pet import clamp


class MedicalSystem:
    def __init__(self, game):
        self.game = game

    def plan(self, pet):
        g = self.game
        b = g.buildings[pet.building_id]
        level = b.level if b.kind == "宠物医院" else 1
        spec = C.ILLNESSES[pet.disease] if pet.disease else C.INJURY_TREATMENT
        terminal = bool(pet.disease and spec["cure"] == 0)
        chance = 0 if terminal else min(1, spec["cure"]+(level-1)*C.MEDICAL_LEVEL_BONUS)
        fatality,damage = spec["fatality"],spec["failure_damage"]
        if pet.injury:
            # 合并伤病按较低的治愈率处理，避免靠误食患上轻病反而提高外伤治愈率。
            chance = min(chance,min(1,C.INJURY_TREATMENT["cure"]+(level-1)*C.MEDICAL_LEVEL_BONUS))
            fatality = max(fatality,C.INJURY_TREATMENT["fatality"])
            damage = max(damage,C.INJURY_TREATMENT["failure_damage"])
        return {"chance":chance, "terminal":terminal, "fatality":fatality,
                "failure_damage":damage, "fee":max(0,C.TREATMENT_FEE-level*C.HOSPITAL_FEE_REDUCTION), "level":level}

    def hospitalize(self, pet_id):
        g = self.game
        pet = g._pet(pet_id)
        if not pet.unwell:
            raise ValueError("没有疾病或伤势，不需要住院")
        if g.buildings[pet.building_id].kind == "宠物医院":
            return "已经在宠物医院住院。"
        for b in g.buildings.values():
            if b.kind == "宠物医院" and g.housing_used(b.building_id)+1+int(pet.pregnant) <= b.capacity:
                return g._act_move(pet_id,b.building_id)
        raise ValueError("需要有空床位的宠物医院；孕宠需要额外预留一个幼崽床位")

    def treat(self, pet_id):
        g = self.game
        p = g._pet(pet_id)
        if not p.unwell:
            raise ValueError("药品仅用于患病或受伤的宠物")
        if g.buildings[p.building_id].kind != "宠物医院":
            raise ValueError("请先将宠物送入宠物医院住院，再进行治疗")
        if p.treatment_ready_ts > g.now:
            raise ValueError("治疗恢复期未结束，不能连续治疗")
        plan = self.plan(p)
        g._pay(plan["fee"])
        g.inventory.take("药品",1)
        p.treatment_ready_ts = g.now+C.TREATMENT_COOLDOWN
        condition = p.disease or "打架伤势"
        if plan["terminal"]:
            p.health = clamp(p.health+C.PALLIATIVE_HEAL)
            text = f"{p.name}的{condition}不可治愈，医院进行了舒缓照护，病情仍会继续恶化。"
        elif g.rng.random() < plan["chance"]:
            p.illness_type = ""
            p.status = "正常"  # 清除兼容旧档的生病标记，再重新判定老年/伤势。
            p.injury = 0
            p.health = clamp(p.health+C.MEDICINE_HEALTH+plan["level"]*C.HOSPITAL_HEAL_BONUS)
            p.illness_budget = g.rng.expovariate(1)
            p.refresh_status(g.now)
            text = f"{p.name}的{condition}治疗成功，已经康复，可以迁回原居所。"
        else:
            p.health = clamp(p.health-plan["failure_damage"])
            if p.health <= 0 or g.rng.random() < plan["fatality"]:
                p.die(g.now,f"{condition}救治无效")
                text = f"{p.name}因{condition}救治无效离世，已收入纪念册。"
            else:
                text = f"{p.name}的{condition}本次治疗失败，仍需住院观察，恢复期结束后可再次尝试。"
        p.note(g.now,text)
        g.log(text)
        return text
