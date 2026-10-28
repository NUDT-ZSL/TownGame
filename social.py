"""伴侣、怀孕/育卵、分娩与同居冲突；由全局时钟统一调度。"""
import game_config as C
from pet import Pet, clamp


class SocialSystem:
    def __init__(self, game):
        self.game = game

    def expecting(self, pet):
        return any(p.pregnant and (p.pet_id == pet.pet_id or p.pregnancy_father_id == pet.pet_id) for p in self.game.living)

    def pair(self, first_id, second_id):
        g = self.game
        a,b = g._pet(first_id), g._pet(second_id)
        if a.pet_id == b.pet_id or a.species != b.species or a.sex == b.sex:
            raise ValueError("伴侣必须是同品种、不同性别的两只宠物")
        if any(p.stage(g.now) != "成年" for p in (a,b)):
            raise ValueError("双方成年后才能结为伴侣")
        if (a.mate_id and a.mate_id != b.pet_id) or (b.mate_id and b.mate_id != a.pet_id):
            raise ValueError("已有其他伴侣，请先在档案中解除关系")
        if a.mate_id == b.pet_id and b.mate_id == a.pet_id:
            return "它们已经是伴侣。"
        a.mate_id, b.mate_id = b.pet_id, a.pet_id
        for p,other in ((a,b),(b,a)):
            p.note(g.now, f"与{other.name}结为伴侣")
        text = f"{a.name}和{b.name}结为伴侣，安排同居后可以尝试繁育。"
        g.log(text)
        return text

    def unpair(self, pet_id):
        g = self.game
        p = g._pet(pet_id, True)
        if self.expecting(p):
            raise ValueError("孕期内不能解除伴侣关系")
        other = g.pets.get(p.mate_id)
        if not other:
            raise ValueError("目前没有伴侣")
        p.mate_id = ""
        other.mate_id = ""
        for pet,partner in ((p,other),(other,p)):
            pet.note(g.now, f"与{partner.name}解除伴侣关系")
        g.log(f"{p.name}与{other.name}解除伴侣关系。")

    def conceive(self, first_id, second_id):
        g = self.game
        a,b = g._pet(first_id), g._pet(second_id)
        if a.pet_id == b.pet_id or a.sex == b.sex or a.species != b.species:
            raise ValueError("只有同品种异性宠物才可以受孕")
        if a.building_id != b.building_id:
            raise ValueError("需要先将双方迁入同一座建筑，一起生活后才能受孕")
        if g.buildings[a.building_id].kind == "宠物医院":
            raise ValueError("住院期间不能繁育，请康复后回家")
        for p in (a,b):
            if self.expecting(p):
                raise ValueError("其中一方已有未出生的幼崽")
            if p.stage(g.now) != "成年" or p.favor < C.BREED_FAVOR or p.breed_ready_ts > g.now or p.unwell or p.health < C.BREED_MIN_HEALTH:
                raise ValueError(f"双方需成年、健康≥{C.BREED_MIN_HEALTH}、无病无伤、好感≥{C.BREED_FAVOR}且冷却结束")
        if g.population_with_reservations >= C.PET_LIMIT:
            raise ValueError("庄园宠物及待出生幼崽数量已达上限")
        home = g.buildings[a.building_id]
        if g.housing_used(home.building_id)+1 > home.capacity:
            raise ValueError("同居建筑没有幼崽空位，请先升级或腾出空位")
        self.pair(a.pet_id, b.pet_id)
        g._pay(C.BREED_COST)
        for item,n in C.BREED_MATERIALS.items():
            g.inventory.take(item,n)
        if g.rng.random() >= C.CONCEPTION_CHANCE:
            for p in (a,b):
                p.breed_ready_ts = g.now+C.CONCEPTION_RETRY
            text = f"{a.name}和{b.name}这次未能受孕，已消耗繁育费用与物资，休息后再试。"
            g.log(text)
            return text
        mother,father = (a,b) if a.sex == "雌" else (b,a)
        duration = C.SPECIES[mother.species].get("gestation", C.PREGNANCY_DURATION)
        mother.pregnancy_start_ts, mother.pregnancy_due_ts = g.now, g.now+duration
        mother.pregnancy_father_id = father.pet_id
        mother.pregnancy_rare = g.rng.random() < C.MUTATION_CHANCE
        for p in (mother,father):
            p.breed_ready_ts = mother.pregnancy_due_ts+C.BREED_COOLDOWN
            p.note(g.now, f"{mother.name}开始{mother.pregnancy_label}，父亲是{father.name}")
        text = f"{mother.name}{mother.pregnancy_label}成功，预计{duration/C.HOUR:g}游戏小时后迎来幼崽，已预留一个床位。"
        g.log(text)
        return text

    def deliver(self):
        g = self.game
        for mother in list(g.living):
            if not mother.pregnant or mother.fostered or mother.pregnancy_due_ts > g.now:
                continue
            father = g.pets[mother.pregnancy_father_id]
            # 调度器在预产时间戳切段，因此长时间离线也保留准确生日。
            child = Pet.create(mother.species, mother.pregnancy_due_ts, mother.building_id, g.rng, rare=mother.pregnancy_rare)
            child.mother_id, child.father_id = mother.pet_id, father.pet_id
            child.note(g.now, f"母亲：{mother.name}；父亲：{father.name}")
            g.pets[child.pet_id] = child
            mother.clear_pregnancy()
            for p in (mother,father):
                p.breed_ready_ts = g.now+C.BREED_COOLDOWN
                p.note(g.now, f"孩子{child.name}诞生")
            g.meta["stats"]["breeds"] += 1
            g.meta["reputation"] += C.BREED_RARE_REPUTATION if child.rare else C.BREED_REPUTATION
            text = f"{mother.name}迎来{'变异' if child.rare else '普通'}幼崽{child.name}（{child.sex}），父亲是{father.name}。"
            g.log(text)
            g.notices.append(text)

    def fights(self):
        g = self.game
        for building in g.buildings.values():
            if building.kind in ("寄养屋", "宠物医院"):
                continue
            candidates = [p for p in g.occupants(building.building_id) if not p.fostered and p.stage(g.now) != "幼崽" and p.fight_ready_ts <= g.now]
            if len(candidates) < 2:
                continue
            a,b = g.rng.sample(candidates,2)
            chance = C.FIGHT_CHANCE
            if min(a.hunger,b.hunger) < C.STARVING:
                chance += C.FIGHT_HUNGER_BONUS
            if min(a.happy,b.happy) < C.FIGHT_UNHAPPY_THRESHOLD:
                chance += C.FIGHT_UNHAPPY_BONUS
            if g.housing_used(building.building_id) >= building.capacity:
                chance += C.FIGHT_CROWD_BONUS
            chance *= (C.FIGHT_PERSONALITY[a.personality]+C.FIGHT_PERSONALITY[b.personality])/2
            if a.mate_id == b.pet_id:
                chance *= C.FIGHT_MATE_FACTOR
            if g.rng.random() < min(1,chance):
                self.fight_pair(a,b)

    def fight_pair(self, a, b):
        g = self.game
        g.log(f"{a.name}和{b.name}在同一居所发生打架冲突，两只宠物都受伤了。")
        for p,other in ((a,b),(b,a)):
            damage = g.rng.uniform(*C.FIGHT_DAMAGE)
            p.health = clamp(p.health-damage)
            p.injury = clamp(p.injury+g.rng.uniform(*C.FIGHT_INJURY))
            p.happy = clamp(p.happy-C.FIGHT_HAPPY_LOSS)
            p.fight_ready_ts = g.now+C.FIGHT_COOLDOWN
            p.note(g.now, f"与{other.name}打架，健康损失{damage:.1f}，需要住院处理伤势")
            if p.health <= 0:
                p.die(g.now,"打架伤重离世")
                g.log(f"{p.name}因打架伤重离世，已收入纪念册。")
            else:
                p.refresh_status(g.now)
