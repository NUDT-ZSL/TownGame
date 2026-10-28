"""持久化天气与UTC昼夜；天气独立随机源，不改变繁育/疾病的游戏随机序列。"""
import hashlib
import random
import game_config as C


def seeded_rng(seed, when, purpose):
    key = f"{seed}:{when:.6f}:{purpose}".encode()
    return random.Random(int.from_bytes(hashlib.blake2b(key,digest_size=8).digest(),"big"))


def day_phase(now):
    hour = now % C.DAY / C.HOUR
    if 5 <= hour < 7.5:
        return "dawn"
    if 7.5 <= hour < 17:
        return "day"
    if 17 <= hour < 19.5:
        return "dusk"
    return "night"


class WeatherSystem:
    def __init__(self, game):
        self.game = game

    def defaults(self, anchor=None, reset=False):
        g = self.game
        anchor = g.now if anchor is None else anchor
        values = {"weather":"晴天","weather_since_ts":anchor,"next_weather_ts":anchor+C.WEATHER_INTERVAL,
                  "weather_seed":int(g.meta["start_ts"])}
        for k,v in values.items():
            if reset or k not in g.meta:
                g.meta[k] = v

    @property
    def current(self):
        return self.game.meta["weather"]

    def choose(self, when):
        g = self.game
        season = C.SEASONS[int((when-g.meta["start_ts"])//(C.SEASON_DAYS*C.DAY))%4]
        return seeded_rng(g.meta["weather_seed"],when,"weather").choices(list(C.WEATHERS),weights=C.WEATHER_WEIGHTS[season])[0]

    def tick(self):
        g = self.game
        if g.now >= g.meta["next_weather_ts"]:
            when = g.meta["next_weather_ts"]
            previous = self.current
            g.meta["weather"] = self.choose(when)
            g.meta["weather_since_ts"] = when
            g.meta["next_weather_ts"] += C.WEATHER_INTERVAL
            if self.current != previous:
                g.log(f"天气转为{self.current}，客流和户外环境随之变化。")

    def forecast(self):
        return self.choose(self.game.meta["next_weather_ts"])

    def outdoor_effect(self, building):
        if not building.outdoor:
            return 1.0,0.0
        spec = C.WEATHERS[self.current]
        return spec["dirt"],spec["health"]
