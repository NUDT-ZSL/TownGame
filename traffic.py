"""运行时交通/人行道。共享持久化RoadGrid，完全不依赖Pygame或game.rng。"""
import math
import random
from dataclasses import dataclass,field
import game_config as C
import town_catalog as K
from town_grid import cell_center,world_to_cell,PORTALS,GRID_SIZE
from town_build import frontage,footprint
from weather import day_phase


def lane_points(path,offset=K.LANE_OFFSET):
    """右侧车道，转向在格中心用二次Bezier连接，跨格边界端点完全一致。"""
    points=[]
    def right(d): return -d[1]*offset,d[0]*offset
    for i,cell in enumerate(path):
        x,y=cell_center(*cell)
        incoming=(cell[0]-path[i-1][0],cell[1]-path[i-1][1]) if i else None
        outgoing=(path[i+1][0]-cell[0],path[i+1][1]-cell[1]) if i<len(path)-1 else None
        direction=incoming or outgoing or (0,1)
        a=right(incoming or direction); b=right(outgoing or direction)
        entry=(x-direction[0]*24+a[0],y-direction[1]*24+a[1]) if incoming else (x+a[0],y+a[1])
        points.append(entry)
        if incoming and outgoing and incoming!=outgoing:
            start=(x-incoming[0]*13+a[0],y-incoming[1]*13+a[1]); end=(x+outgoing[0]*13+b[0],y+outgoing[1]*13+b[1])
            control=(x+a[0]+b[0],y+a[1]+b[1]); points.append(start)
            for t in (.2,.4,.6,.8,1.):
                points.append(((1-t)**2*start[0]+2*(1-t)*t*control[0]+t*t*end[0],(1-t)**2*start[1]+2*(1-t)*t*control[1]+t*t*end[1]))
        else:
            points.append((x+a[0],y+a[1]))
        if outgoing:
            points.append((x+outgoing[0]*24+b[0],y+outgoing[1]*24+b[1]))
    result=[]
    for point in points:
        if not result or math.hypot(point[0]-result[-1][0],point[1]-result[-1][1])>.01:
            result.append(point)
    return result


@dataclass
class Vehicle:
    vehicle_id: str
    kind: str
    color: tuple
    x: float
    y: float
    path: list
    destination: tuple
    origin_name: str
    destination_name: str
    points: list=field(default_factory=list)
    index: int=1
    speed: float=0
    angle: float=0
    state: str='行驶'
    blocked: float=0
    revision: int=-1

    @property
    def cell(self): return world_to_cell(self.x,self.y)


class TrafficSystem:
    def __init__(self,seed=20261027):
        self.rng=random.Random(seed); self.vehicles=[]; self.serial=0; self.clock=0; self.revision=-1

    def target_count(self,g):
        hour=g.now%C.DAY/C.HOUR; bonus=g.construction.bonuses()
        phase=K.TRAFFIC_PEAK_MULT if any(a<=hour<b for a,b in K.TRAFFIC_PEAK_HOURS) else K.TRAFFIC_NIGHT_MULT if hour<K.TRAFFIC_NIGHT_HOURS[0] or hour>=K.TRAFFIC_NIGHT_HOURS[1] else 1
        return min(K.MAX_VEHICLES,max(0,int((K.TRAFFIC_BASE+len(g.people)*K.TRAFFIC_PER_PERSON+bonus['commerce']*K.TRAFFIC_PER_COMMERCE+len(g.construction.active())*K.TRAFFIC_PER_BUILDING)*phase*C.WEATHERS[g.weather.current]['traffic'])))

    def destinations(self,g):
        grid=g.construction.grid
        points=[(c,C.TOWN_ZONES[k]['name'],k) for k,c in PORTALS.items() if c in grid.roads]
        for b in g.construction.active():
            candidates=sorted(frontage(footprint(b),grid))
            if candidates: points.append((candidates[0],K.CATALOG[b['catalog_key']]['name'],K.CATALOG[b['catalog_key']]['category']))
        return points

    def spawn(self,g):
        if len(self.vehicles)>=K.MAX_VEHICLES: return
        options=self.destinations(g); grid=g.construction.grid
        if len(options)<2: return
        kind=('car','bus','truck')[self.serial%3]
        self.serial+=1  # 失败的公交行程不能阻止后续汽车/货车继续生成。
        preferred=[p for p in options if p[2]=='station'] if kind=='bus' else [p for p in options if p[2] in ('fields','产业')] if kind=='truck' else options
        start=self.rng.choice(preferred or options)
        goals=[p for p in options if p[0]!=start[0] and grid.is_connected(start[0],p[0])]
        if not goals: return
        goal=self.rng.choice(goals); path=grid.shortest_path(start[0],goal[0]); points=lane_points(path)
        if len(points)<2: return
        if any(math.hypot(v.x-points[0][0],v.y-points[0][1])<K.VEHICLE_GAP for v in self.vehicles): return
        color=self.rng.choice(((167,185,162),(193,149,136),(137,170,185),(217,194,145),(166,157,189)))
        self.vehicles.append(Vehicle(f'car-{self.serial}',kind,color,*points[0],path,goal[0],start[1],goal[1],points=points,revision=grid.road_revision))

    def _replan(self,v,grid):
        if v.cell not in grid.roads: return False
        path=grid.shortest_path(v.cell,v.destination)
        if not path: return False
        v.path=path; v.points=[(v.x,v.y)]+lane_points(path); v.index=1; v.revision=grid.road_revision
        return True

    def update(self,dt,g):
        dt=max(0,min(K.TRAFFIC_MAX_DT,dt)); grid=g.construction.grid
        count=self.target_count(g)
        self.clock+=dt
        if not self.vehicles and count:
            for _ in range(min(6,count)): self.spawn(g)
        if self.clock>=K.TRAFFIC_SPAWN_INTERVAL and len(self.vehicles)<count:
            self.spawn(g); self.clock=0
        if len(self.vehicles)>count:
            self.vehicles=self.vehicles[:count]
        reservations={}; survivors=[]; buckets={}
        for car in self.vehicles:
            buckets.setdefault((int(car.x//96),int(car.y//96)),[]).append(car)
        # 单格冲突预约采用稳定顺序；等待过久安全移出，防止闭环路口死锁。
        for v in self.vehicles:
            if v.revision!=grid.road_revision and not self._replan(v,grid): continue
            if v.cell not in grid.roads or v.index>=len(v.points): continue
            tx,ty=v.points[v.index]; dx,dy=tx-v.x,ty-v.y; distance=math.hypot(dx,dy)
            if distance<.05:
                v.index+=1; survivors.append(v); continue
            ux,uy=dx/distance,dy/distance
            speed=K.VEHICLE_SPEEDS[v.kind]*C.WEATHERS[g.weather.current]['speed']*(K.TRAFFIC_NIGHT_SPEED if day_phase(g.now)=='night' else 1)
            blocked=False
            bx,by=int(v.x//96),int(v.y//96)
            nearby=(car for xx in range(bx-1,bx+2) for yy in range(by-1,by+2) for car in buckets.get((xx,yy),()))
            for other in nearby:
                if other is v: continue
                ox,oy=other.x-v.x,other.y-v.y
                forward=ox*ux+oy*uy; lateral=abs(ox*uy-oy*ux)
                if 0<forward<K.VEHICLE_GAP and lateral<17:
                    speed*=max(0,(forward-28)/(K.VEHICLE_GAP-28))
                    if forward<31: blocked=True
            next_cell=world_to_cell(v.x+ux*20,v.y+uy*20)
            junction=next_cell if next_cell in grid.roads and len(grid.neighbors(next_cell))>=3 else None
            if junction and junction in reservations and reservations[junction]!=v.vehicle_id: blocked=True
            if junction and not blocked: reservations[junction]=v.vehicle_id
            v.speed=0 if blocked else speed; v.state='让行/拥堵' if blocked or speed<8 else '行驶'
            v.blocked=v.blocked+dt if v.speed<5 else 0
            if v.blocked>K.VEHICLE_TIMEOUT: continue
            step=min(distance,v.speed*dt); nx,ny=v.x+ux*step,v.y+uy*step
            if world_to_cell(nx,ny) not in grid.roads: continue
            v.x,v.y=nx,ny; v.angle=math.degrees(math.atan2(-ux,-uy))
            if step>=distance-.01: v.index+=1
            survivors.append(v)
        self.vehicles=survivors; self.revision=grid.road_revision


@dataclass
class RoadWalker:
    ident: str
    x: float
    y: float
    path: list=field(default_factory=list)
    points: list=field(default_factory=list)
    index: int=1
    goal: str=''
    revision: int=-1
    anim: float=0
    visible: bool=True
    wait: float=0

    def update(self,dt,person,grid):
        dt=max(0,min(.25,dt)); self.anim+=dt
        current=world_to_cell(self.x,self.y)
        if current not in grid.roads:
            if not grid.roads: self.visible=False; self.path=[]; return
            # 原脚下道路被拆时安全重建在最近剩余路格，不显示跨草地穿越。
            current=min(grid.roads,key=lambda c:abs(c[0]-current[0])+abs(c[1]-current[1]))
            self.x,self.y=cell_center(*current); self.revision=-1
        self.visible=True
        destination=person['destination']; goal=PORTALS[destination]
        changed=self.revision!=grid.road_revision or destination!=self.goal
        if changed or self.index>=len(self.points):
            if not changed:
                self.wait+=dt
                if self.wait<2: self.path=[]; return
            self.wait=0; self.goal=destination; self.revision=grid.road_revision
            if not grid.is_connected(current,goal):
                reachable=[c for c in PORTALS.values() if grid.is_connected(current,c)]
                goal=min(reachable,key=lambda c:abs(c[0]-current[0])+abs(c[1]-current[1])) if reachable else current
            elif not changed and current==goal:
                choices=[c for c in grid.roads if abs(c[0]-goal[0])+abs(c[1]-goal[1])<=4 and grid.is_connected(current,c)]
                goal=sorted(choices)[(int(self.ident[:4],16)+int(self.anim//3))%len(choices)]
            self.path=grid.shortest_path(current,goal)
            self.points=[(self.x,self.y)]+lane_points(self.path,K.SIDEWALK_OFFSET); self.index=1
        if self.index>=len(self.points): return
        tx,ty=self.points[self.index]; dx,dy=tx-self.x,ty-self.y; distance=math.hypot(dx,dy)
        step=min(distance,(40 if person['role']=='游客' else 30)*dt)
        if distance>.01:
            nx,ny=self.x+dx/distance*step,self.y+dy/distance*step
            if world_to_cell(nx,ny) not in grid.roads:
                self.revision=-1; return
            self.x,self.y=nx,ny
        if step>=distance-.01: self.index+=1
