"""道路与玩家建筑领域服务：共享预览校验、原子动作、施工和离线税收。"""
import copy
import math
import uuid
import game_config as C
import town_catalog as K
from town_grid import RoadGrid, PORTALS, terrain, zone_for, cell_key, default_roads, DIRECTIONS, in_bounds


def footprint(building):
    return [(x,y) for y in range(building['cy'],building['cy']+building['h']) for x in range(building['cx'],building['cx']+building['w'])]


def frontage(cells,grid):
    owned=set(cells)
    return {(x+dx,y+dy) for x,y in owned for dx,dy in DIRECTIONS if (x+dx,y+dy) not in owned and (x+dx,y+dy) in grid.roads}


def validate_build_state(state):
    """既用于保存前校验也用于启动迁移；拒绝不合法坐标/占用/目录。"""
    roads=state.get('roads',[]); buildings=state.get('town_buildings',[]); now=state['meta']['sim_ts']
    if 'road_revision' not in state['meta']:
        if roads or buildings:
            raise ValueError('路网缺少版本')
        return
    if type(state['meta']['road_revision']) is not int or state['meta']['road_revision']<0:
        raise ValueError('路网版本无效')
    occupied=set()
    for r in roads:
        cell=(r['cx'],r['cy'])
        if not in_bounds(*cell) or terrain(cell) not in ('grass','zone') or cell in occupied or r['road_id']!=cell_key(cell):
            raise ValueError('道路坐标、重复格或保护地形无效')
        if r['road_style'] not in K.ROAD_STYLES or not 0<=r['built_ts']<=now or type(r['paid']) is not int or not 0<=r['paid']<=max(s['cost'] for s in K.ROAD_STYLES.values()):
            raise ValueError('道路风格、时间或造价无效')
        occupied.add(cell)
    seen=set()
    for b in buildings:
        spec=K.CATALOG[b['catalog_key']]
        if type(b['cx']) is not int or type(b['cy']) is not int:
            raise ValueError('建筑格坐标类型无效')
        size=spec['footprint'] if b['orientation']%2==0 else tuple(reversed(spec['footprint']))
        if b['family']!=spec['family'] or (b['w'],b['h'])!=size or type(b['orientation']) is not int or b['orientation'] not in range(4):
            raise ValueError('建筑风格、朝向或占地无效')
        if not isinstance(b['building_id'],str) or b['building_id'] in seen or b['status'] not in ('building','ready'):
            raise ValueError('建筑ID或状态无效')
        if not (0<=b['placed_ts']<=now and abs(b['finish_ts']-b['placed_ts']-spec['build_hours']*C.HOUR)<.01):
            raise ValueError('施工时间无效')
        if b['status']=='ready' and b['finish_ts']>now+.01:
            raise ValueError('尚未到完工时刻')
        seen.add(b['building_id'])
        for c in footprint(b):
            if not in_bounds(*c) or terrain(c) not in ('grass','zone') or c in occupied:
                raise ValueError('建筑重叠、越界或侵入保护地形')
            occupied.add(c)
    m=state['meta']
    if 'next_tax_ts' in m and (m['next_tax_ts']<now or any(type(m[k]) is not int or m[k]<0 for k in ('roads_built','town_buildings_built','town_tax_total'))):
        raise ValueError('建设调度或统计无效')


class TownBuildSystem:
    def __init__(self,game):
        self.game=game; self._grid=None; self._revision=None; self.undo=[]
        self._economy_cache=None

    def invalidate(self):
        self._grid=None; self._economy_cache=None

    def defaults(self,anchor=None):
        g=self.game; anchor=g.now if anchor is None else anchor
        for k,v in dict(road_revision=0,construction_open_ts=anchor,next_tax_ts=anchor+C.DAY,
                       roads_built=0,town_buildings_built=0,town_tax_total=0,build_guide_dismissed=False,town_cleared=[]).items():
            g.meta.setdefault(k,v)
        if g.roads is None:
            g.roads=default_roads(g.now)
        self.invalidate()

    @property
    def grid(self):
        g=self.game
        if self._grid is None or self._revision!=g.meta['road_revision']:
            self._revision=g.meta['road_revision']; self._grid=RoadGrid(g.roads,self._revision)
        return self._grid

    def connected(self,zone):
        return self.grid.connected_zones().get(zone,True)

    def occupied(self):
        return {c for b in self.game.town_buildings for c in footprint(b)}

    def reason(self,cell,occupied=None):
        if len(cell)!=2 or not in_bounds(*cell):
            return '超出小镇边界'
        kind=terrain(cell)
        if kind not in ('grass','zone'):
            return {'water':'此处是湖面，不能施工','estate':'庄园及安全边距是保留地','landmark':'此处是固定地标，不能占用'}.get(kind,'超出小镇边界')
        zone=zone_for(cell)
        if zone and not self.game.town.level(zone):
            return '需要先开放'+C.TOWN_ZONES[zone]['name']
        if cell in (self.occupied() if occupied is None else occupied):
            return '该格已被建筑占用'
        return ''

    def road_preview(self,cells,style='asphalt',remove=False):
        if style not in K.ROAD_STYLES:
            raise ValueError('未知道路风格')
        valid=[]; invalid=[]; cost=0; occupied=self.occupied(); seen=set()
        for raw in cells:
            cell=tuple(raw)
            if cell in seen:
                continue
            seen.add(cell); existing=self.grid.roads.get(cell)
            if remove:
                reason='' if existing else '此处没有道路'
            elif existing and existing['road_style']==style:
                reason='相同风格，无变化'
            else:
                reason=self.reason(cell,occupied)
            if reason:
                invalid.append((cell,reason))
            else:
                valid.append(cell)
                cost+= -math.floor(existing['paid']*K.ROAD_REFUND) if remove else K.ROAD_STYLES[style]['cost']
        return dict(valid=valid,invalid=invalid,cost=cost,total=len(seen))

    def _changed(self,old_connections,cleared=()):
        g=self.game; g.meta['road_revision']+=1; self.invalidate()
        g.meta['town_cleared']=sorted(set(g.meta['town_cleared'])|{cell_key(c) for c in cleared})
        connections=self.grid.connected_zones()
        for zone in C.TOWN_ZONES:
            if old_connections[zone]!=connections[zone]:
                g.log(C.TOWN_ZONES[zone]['name']+('恢复道路通达。' if connections[zone] else '街区道路中断，收益暂停。'))

    def edit_roads(self,cells,style='asphalt',remove=False):
        g=self.game; preview=self.road_preview(cells,style,remove)
        if not preview['valid']:
            raise ValueError(preview['invalid'][0][1] if preview['invalid'] else '无变化')
        cost=preview['cost']
        if cost>g.meta['coins']:
            raise ValueError(f'资金不足，还差{cost-g.meta["coins"]}金币')
        old=self.grid.connected_zones(); roads=dict(self.grid.roads)
        previous={cell:copy.deepcopy(roads.get(cell)) for cell in preview['valid']}
        for cell in preview['valid']:
            if remove:
                del roads[cell]
            else:
                roads[cell]=dict(road_id=cell_key(cell),cx=cell[0],cy=cell[1],road_style=style,built_ts=g.now,
                                 paid=roads[cell]['paid'] if cell in roads else K.ROAD_STYLES[style]['cost'])
        g.meta['coins']-=cost; g.roads=list(roads.values())
        if not remove:
            g.meta['roads_built']+=len(preview['valid'])
        self._changed(old,preview['valid'])
        # 保存成功后Game才把该记录加入会话撤销栈，异常不会留下可领取退款的记录。
        self.pending_undo=dict(previous=previous,cost=cost,revision=g.meta['road_revision'])
        text=f"{'拆除' if remove else '铺设/改铺'}{len(preview['valid'])}格道路，{'返还' if remove else '花费'}{abs(cost)}金币"
        disconnected=[C.TOWN_ZONES[z]['name'] for z in old if old[z] and not self.connected(z)]
        if disconnected:
            text+='；道路中断：'+'、'.join(disconnected)
        g.log(text+'。'); return text

    def undo_road(self):
        g=self.game
        if not self.undo or self.undo[-1]['revision']!=g.meta['road_revision']:
            raise ValueError('没有可撤销的道路笔画，或路网已发生其他变更')
        entry=self.undo[-1]; old=self.grid.connected_zones(); roads=dict(self.grid.roads)
        if entry['cost']<0:
            g._pay(-entry['cost'])
        else:
            g.meta['coins']+=entry['cost']
        for cell,record in entry['previous'].items():
            if record is None:
                roads.pop(cell,None)
            else:
                if cell in self.occupied():
                    raise ValueError('道路位置已被新建筑占用，不能撤销')
                roads[cell]=copy.deepcopy(record)
        g.roads=list(roads.values()); self._changed(old)
        g.log('已撤销最近一笔道路操作。'); return '已撤销道路笔画并恢复费用'

    def building_preview(self,key,cx,cy,orientation=None):
        if orientation is None:
            candidates=[self.building_preview(key,cx,cy,i) for i in range(4)]
            def score(p):
                b=p['building']; x,y,w,h=b['cx'],b['cy'],b['w'],b['h']; direction=b['orientation']
                edge=([(xx,y-1) for xx in range(x,x+w)] if direction==0 else [(x+w,yy) for yy in range(y,y+h)] if direction==1 else [(xx,y+h) for xx in range(x,x+w)] if direction==2 else [(x-1,yy) for yy in range(y,y+h)])
                return (not bool(p['reason']),sum(c in self.grid.roads for c in edge))
            return max(candidates,key=score)
        if key not in K.CATALOG or type(orientation) is not int or orientation not in range(4):
            raise ValueError('未知建筑或朝向')
        if type(cx) is not int or type(cy) is not int:
            raise ValueError('建筑格坐标必须为整数')
        spec=K.CATALOG[key]; w,h=spec['footprint'] if orientation%2==0 else reversed(spec['footprint'])
        b=dict(catalog_key=key,family=spec['family'],cx=cx,cy=cy,w=w,h=h,orientation=orientation)
        cells=footprint(b); occupied=self.occupied(); reason=''
        for cell in cells:
            reason=self.reason(cell,occupied)
            if not reason and cell in self.grid.roads:
                reason='建筑不能压在道路上'
            if reason:
                break
        adjacent=frontage(cells,self.grid)
        if not reason and not adjacent:
            reason='建筑必须与道路相邻，斜角不算临街'
        return dict(building=b,cells=cells,frontage=adjacent,reason=reason,cost=spec['cost'])

    def place(self,key,cx,cy,orientation=None):
        g=self.game; preview=self.building_preview(key,cx,cy,orientation)
        if preview['reason']:
            raise ValueError(preview['reason'])
        g._pay(preview['cost']); b=preview['building']; spec=K.CATALOG[key]
        b.update(building_id=uuid.uuid4().hex,status='building',placed_ts=g.now,finish_ts=g.now+spec['build_hours']*C.HOUR)
        g.town_buildings.append(b); g.meta['town_buildings_built']+=1
        g.meta['town_cleared']=sorted(set(g.meta['town_cleared'])|{cell_key(c) for c in preview['cells']})
        self._economy_cache=None
        text=f"{spec['name']}开始施工，花费{spec['cost']}金币，工期{spec['build_hours']}游戏小时。"
        g.log(text); return text

    def remove(self,building_id):
        g=self.game; b=next((b for b in g.town_buildings if b['building_id']==building_id),None)
        if b is None:
            raise ValueError('建筑不存在')
        spec=K.CATALOG[b['catalog_key']]; refund=math.floor(spec['cost']*K.BUILDING_REFUND[b['status']])
        g.town_buildings.remove(b); g.meta['coins']+=refund; self._economy_cache=None
        text=f"拆除{spec['name']}，返还{refund}金币。"; g.log(text); return text

    def building_connected(self,b):
        zone=zone_for((b['cx'],b['cy']))
        return (not zone or self.connected(zone)) and any(self.grid.is_connected(PORTALS['estate'],c) for c in frontage(footprint(b),self.grid))

    def active(self):
        return [b for b in self.game.town_buildings if b['status']=='ready' and self.building_connected(b)]

    def land_value(self,cell):
        if terrain(cell) not in ('grass','zone'):
            return 0
        x,y=cell; score=K.LAND_ROAD if any((x+dx,y+dy) in self.grid.roads for dx,dy in ((0,0),)+DIRECTIONS) else 0
        zone=zone_for(cell)
        score+=(self.game.town.level(zone) if zone else 0)+(self.game.town.level('park') if self.connected('park') else 0)
        for b in self.game.town_buildings:
            if b['status']!='ready' or not self.building_connected(b):
                continue
            spec=K.CATALOG[b['catalog_key']]; dist=max(abs(x-(b['cx']+b['w']/2)),abs(y-(b['cy']+b['h']/2)))
            if spec['category']=='公共' and dist<=K.LAND_PUBLIC_RADIUS:
                score+=spec['desirability']
            elif spec['family']=='industrial' and dist<=K.LAND_INDUSTRY_RADIUS:
                score+=spec['desirability']
        return int(score)

    def land_factor(self,cell):
        return max(K.LAND_FACTOR_MIN,min(K.LAND_FACTOR_MAX,1+self.land_value(cell)*K.LAND_FACTOR_PER_POINT))

    def bonuses(self):
        signature=(self.game.meta['road_revision'],tuple((b['building_id'],b['status']) for b in self.game.town_buildings),tuple(z['level'] for z in self.game.town_zones))
        if self._economy_cache and self._economy_cache[0]==signature:
            return self._economy_cache[1]
        result=dict(population=0,commerce=0.,attraction=0.,tax=0.,crops=0.,traffic=0.)
        for b in self.active():
            spec=K.CATALOG[b['catalog_key']]; factor=self.land_factor((b['cx'],b['cy']))
            result['population']+=spec['population']; result['commerce']+=spec['commerce']
            result['attraction']+=spec['attraction']*(factor if spec['category']=='商业' else 1)
            result['tax']+=spec['tax']*(factor if spec['category']=='住宅' else 1)
            result['crops']+=K.CROP_WINDMILL_BONUS if spec['key']=='windmill' else 0
            result['traffic']+=K.BUS_TRAFFIC_BONUS if spec['key']=='bus_stop' else 0
        result['tax']=max(0,math.floor(result['tax']))
        self._economy_cache=signature,result; return result

    def boundaries(self):
        return [self.game.meta['next_tax_ts']]+[b['finish_ts'] for b in self.game.town_buildings if b['status']=='building']

    def tick(self):
        g=self.game
        for b in g.town_buildings:
            if b['status']=='building' and g.now>=b['finish_ts']:
                b['status']='ready'; self._economy_cache=None
                g.log(K.CATALOG[b['catalog_key']]['name']+'施工完成。',b['finish_ts'])
        if g.now>=g.meta['next_tax_ts']:
            income=self.bonuses()['tax']; g.meta['coins']+=income; g.meta['town_income']+=income
            g.meta['town_tax_total']+=income; g.meta['next_tax_ts']+=C.DAY
            if income:
                g.log(f'小镇建筑每日税收{income}金币。')
