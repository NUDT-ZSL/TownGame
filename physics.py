"""平面运动碰撞。使用实体占地，与镜头缩放无关。"""
import math


def vehicle_contains(vehicle, point, margin=0):
    angle=math.radians(-vehicle.angle+180)
    dx,dy=point[0]-vehicle.x,point[1]-vehicle.y
    # 模型的局部 +Y 对应车头。
    x=dx*math.cos(angle)+dy*math.sin(angle)
    y=-dx*math.sin(angle)+dy*math.cos(angle)
    length={'car':36,'truck':45,'bus':62}.get(vehicle.kind,36)*1.5
    return abs(x)<9+margin and abs(y)<length/2+margin


def pet_radius(actor):
    return {'奶牛':20,'绵羊':17,'梅花鹿':18,'小马':20}.get(actor.species,14)


def separate_pets(actors,bounds):
    """稳定顺序解开重叠；包括静止、睡眠和玩家交互中的伙伴。"""
    ordered=sorted(actors,key=lambda a:a.pet_id)
    for _ in range(12):
        changed=False
        for i,a in enumerate(ordered):
            for b in ordered[i+1:]:
                dx,dy=b.x-a.x,b.y-a.y;distance=math.hypot(dx,dy)
                minimum=pet_radius(a)+pet_radius(b)
                if distance>=minimum-.01:continue
                ux,uy=(dx/distance,dy/distance) if distance>.001 else (1.,0.)
                push=(minimum-distance)/2+.02
                a.x-=ux*push;a.y-=uy*push;b.x+=ux*push;b.y+=uy*push
                changed=True
        for a in ordered:
            a.x=max(bounds.left+26,min(bounds.right-26,a.x))
            a.y=max(bounds.top+30,min(bounds.bottom-4,a.y))
        if not changed:break
