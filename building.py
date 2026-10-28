"""地块与建筑实体。容量由配置与等级派生，不重复持久化。"""
from dataclasses import dataclass, asdict
import game_config as C


@dataclass
class Building:
    building_id: int  # 固定对应网格地块编号
    unlocked: bool = False
    kind: str = ""
    level: int = 0

    @property
    def capacity(self):
        if not self.kind:
            return 0
        spec = C.BUILDINGS[self.kind]
        return spec["capacity"] + spec["growth"]*(self.level-1)

    @property
    def outdoor(self):
        return bool(self.kind and C.BUILDINGS[self.kind]["outdoor"])

    @property
    def upgrade_cost(self):
        return int(C.BUILDINGS[self.kind]["cost"] * self.level * C.UPGRADE_PRICE_FACTOR)

    def to_dict(self):
        return asdict(self)

    @classmethod
    def from_dict(cls, data):
        return cls(**data)
