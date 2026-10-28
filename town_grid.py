"""统一48像素建造格、保护地形、稀疏路网与按revision失效的拓扑缓存。"""
from collections import deque
import math
import game_config as C
from town_catalog import GRID_SIZE, GRID_COLS, GRID_ROWS

DIRECTIONS = ((0,-1),(1,0),(0,1),(-1,0))
PORTALS = {"estate":(37,30),"station":(37,11),"homes":(16,13),"market":(60,13),"park":(21,30),"fields":(55,30)}
LANDMARKS = ((250,290,180,195),(3200,330,180,185),(1300,220,940,280),(3200,1245,195,190))


def world_to_cell(x,y):
    return math.floor(x/GRID_SIZE),math.floor(y/GRID_SIZE)


def cell_rect(cx,cy):
    return cx*GRID_SIZE,cy*GRID_SIZE,GRID_SIZE,GRID_SIZE


def cell_center(cx,cy):
    return (cx+.5)*GRID_SIZE,(cy+.5)*GRID_SIZE


def in_bounds(cx,cy):
    return isinstance(cx,int) and not isinstance(cx,bool) and isinstance(cy,int) and not isinstance(cy,bool) and 0 <= cx < GRID_COLS and 0 <= cy < GRID_ROWS


def iter_visible_cells(camera):
    bounds=camera.ground_bounds() if getattr(camera,'is_3d',False) else (camera.x,camera.y,camera.x+camera.width/camera.zoom,camera.y+camera.height/camera.zoom)
    left,top = world_to_cell(bounds[0],bounds[1])
    right,bottom = world_to_cell(bounds[2],bounds[3])
    for y in range(max(0,top),min(GRID_ROWS-1,bottom)+1):
        for x in range(max(0,left),min(GRID_COLS-1,right)+1):
            yield x,y


def intersects(a,b):
    x,y,w,h=a; xx,yy,ww,hh=b
    return x < xx+ww and xx < x+w and y < yy+hh and yy < y+h


def zone_for(cell):
    x,y = cell_center(*cell)
    return next((k for k,s in C.TOWN_ZONES.items() if s['rect'][0] <= x < s['rect'][0]+s['rect'][2] and s['rect'][1] <= y < s['rect'][1]+s['rect'][3]),None)


def terrain(cell):
    if not in_bounds(*cell):
        return "outside"
    rect = cell_rect(*cell)
    x,y,w,h = C.FARM_WORLD_RECT
    if intersects(rect,(x-48,y-48,w+96,h+96)):
        return "estate"
    if any(intersects(rect,r) for r in LANDMARKS):
        return "landmark"
    # 湖泊按格的最近点判定，避免格角侵入水面；保留木桥也是保护地形。
    px,py,pw,ph = C.TOWN_ZONES['park']['rect']
    cx,cy,rx,ry = px+pw/2,py+180+(ph-320)/2,(pw-200)/2,(ph-320)/2
    nearx=max(rect[0],min(cx,rect[0]+48)); neary=max(rect[1],min(cy,rect[1]+48))
    if ((nearx-cx)/rx)**2+((neary-cy)/ry)**2 <= 1:
        return "water"
    return "zone" if zone_for(cell) else "grass"


def cell_key(cell):
    return f"{cell[0]},{cell[1]}"


def line_cells(start,end):
    """Bresenham补全并把对角步展开为正交步，快速拖动仍保持四邻接。"""
    x,y=start; tx,ty=end; dx=abs(tx-x); dy=-abs(ty-y)
    sx=1 if x<tx else -1; sy=1 if y<ty else -1; error=dx+dy
    result=[(x,y)]
    while (x,y)!=(tx,ty):
        twice=2*error
        if twice>=dy:
            error+=dy; x+=sx; result.append((x,y))
        if twice<=dx and y!=ty:
            error+=dx; y+=sy; result.append((x,y))
    return result


def default_roads(now):
    west,north,east,sw,se=(22,13),(37,13),(52,13),(22,30),(52,30)
    edges=((PORTALS['station'],north),(west,north),(north,east),(west,PORTALS['homes']),
           (east,PORTALS['market']),(west,sw),(east,se),(sw,PORTALS['estate']),
           (PORTALS['estate'],se),(sw,PORTALS['park']),(se,PORTALS['fields']))
    cells=set(c for a,b in edges for c in line_cells(a,b))
    return [dict(road_id=cell_key(c),cx=c[0],cy=c[1],road_style='asphalt',built_ts=now,paid=0) for c in sorted(cells)]


class RoadGrid:
    def __init__(self,roads,revision=0):
        self.roads={ (r['cx'],r['cy']):r for r in roads }
        self.road_revision=revision
        self._components=None; self._labels={}; self._paths={}

    def mask(self,cell):
        x,y=cell
        return sum(1<<i for i,(dx,dy) in enumerate(DIRECTIONS) if (x+dx,y+dy) in self.roads)

    def neighbors(self,cell):
        x,y=cell
        return [(x+dx,y+dy) for dx,dy in DIRECTIONS if (x+dx,y+dy) in self.roads]

    def components(self):
        if self._components is None:
            remaining=set(self.roads); groups=[]
            while remaining:
                first=min(remaining); group={first}; remaining.remove(first); todo=[first]
                while todo:
                    for nxt in self.neighbors(todo.pop()):
                        if nxt in remaining:
                            remaining.remove(nxt); group.add(nxt); todo.append(nxt)
                for cell in group:
                    self._labels[cell]=len(groups)
                groups.append(frozenset(group))
            self._components=tuple(groups)
        return self._components

    def is_connected(self,a,b):
        self.components()
        return a in self._labels and b in self._labels and self._labels[a]==self._labels[b]

    def connected_zones(self):
        return {k:(k=='estate' or self.is_connected(PORTALS['estate'],v)) for k,v in PORTALS.items()}

    def shortest_path(self,start,goal):
        if not self.is_connected(start,goal):
            return []
        key=start,goal
        if key not in self._paths:
            todo=deque([start]); parents={start:None}
            while todo:
                current=todo.popleft()
                if current==goal:
                    break
                for nxt in self.neighbors(current):
                    if nxt not in parents:
                        parents[nxt]=current; todo.append(nxt)
            path=[]; cell=goal
            while cell is not None:
                path.append(cell); cell=parents[cell]
            if len(self._paths)>512:
                self._paths.clear()
            self._paths[key]=tuple(reversed(path))
        return list(self._paths[key])
