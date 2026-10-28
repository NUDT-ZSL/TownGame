"""图鉴与成就规则，避免将判定条件散落在操作代码中。"""
import game_config as C


def update_codex(codex, pets):
    for pet in pets:
        entry = codex.setdefault(pet.species, {"rare": False, "natural_death": False})
        entry["rare"] |= pet.rare
        entry["natural_death"] |= pet.death_reason == "寿终正寝"


def achieved(game):
    stats = game.meta["stats"]
    return {
        "first_breed": stats["breeds"] >= 1,
        "rare": any(p.rare for p in game.pets.values()),
        "collector": all(species in game.codex for species in C.CLASSIC_SPECIES),
        "all_species": all(species in game.codex for species in C.SPECIES),
        "old_friend": any(p.death_reason == "寿终正寝" for p in game.pets.values()),
        "builder": sum(bool(b.kind) for b in game.buildings.values()) >= 6,
        "orders": stats["orders"] >= 3,
        "friends": any(p.favor >= 80 for p in game.pets.values()),
        "seller": stats["sold"] >= 3,
    }


def check(game):
    update_codex(game.codex, game.pets.values())
    for key, result in achieved(game).items():
        if result and key not in game.achievements:
            title, category, description, coins, rep = C.ACHIEVEMENTS[key]
            game.achievements[key] = game.now
            game.meta["coins"] += coins
            game.meta["reputation"] += rep
            text = f"成就达成：{title}，奖励{coins}金币 / {rep}声望。"
            game.log(text)
            game.notices.append(text)
