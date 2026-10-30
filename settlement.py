"""持久化居民自治：需求评分、资源预约、生产任务与家庭生命周期。

使用游戏时钟结算，渲染帧率不影响生产与出生；公共库存不占用玩家仓库。
"""
import heapq
import math
import uuid
import game_config as C
import town_catalog as K
from town_grid import PORTALS,DIRECTIONS,terrain,in_bounds,cell_key,world_to_cell,default_roads
from town_build import footprint,frontage

HOUR,DAY=C.HOUR,C.DAY
GROW_UP=7*DAY


def defaults(g):
    if 'settlement' not in g.meta:
        g.meta['settlement']={'enabled':True,'next_tick':g.now+HOUR,'funds':3000,
            'stock':{'food':60,'seed':12,'wood':30,'stone':30,'feed':16,'herd':2},
            'jobs':[],'serial':0,'last_day':int(g.now//DAY),'births':0,'completed':0,
            'last_birth':g.now,'history':[],'blocked':'','food_days':0,'next_herd_birth':g.now+3*DAY,'population_mode':'families'}
    for i,p in enumerate(p for p in g.people if p['role']=='居民'):
        p.setdefault('born_ts',g.now-(12+i%12)*DAY)
        p.setdefault('sex','女' if i%2 else '男')
        p.setdefault('partner_id','')
        p.setdefault('pregnant_until',0)
        p.setdefault('family_cooldown',g.now)
        p.setdefault('job_id','')
        p.setdefault('profession',('农民','工匠','牧民','建筑工')[i%4])


def adults(g):
    return [p for p in g.people if p['role']=='居民' and g.now-p.get('born_ts',g.now)>=GROW_UP]


def capacity(g):
    return C.TOWN_BASE_RESIDENTS+g.town.effective_level('homes')*C.TOWN_RESIDENTS_PER_LEVEL+g.construction.bonuses()['population']


def entrances(building):
    """建筑周边的合法服务入口，优先选模型前门所在的一侧。"""
    x,y,w,h=building['cx'],building['cy'],building['w'],building['h']
    front=((x+w//2,y+h),(x-1,y+h//2),(x+w//2,y-1),(x+w,y+h//2))[building.get('orientation',0)]
    edge={(xx,y-1) for xx in range(x,x+w)}|{(xx,y+h) for xx in range(x,x+w)}
    edge|={(x-1,yy) for yy in range(y,y+h)}|{(x+w,yy) for yy in range(y,y+h)}
    return sorted(edge,key=lambda c:(c!=front,abs(c[0]-front[0])+abs(c[1]-front[1]),c))


def road_plan(g,targets,limit=12000):
    """加权 A*：既有路成本低，空地需要铺路；目标是庄园连通路网。"""
    grid=g.construction.grid;grid.components()
    root=PORTALS['estate']
    if root not in grid.roads and grid.roads:root=min(grid.roads,key=lambda c:abs(c[0]-root[0])+abs(c[1]-root[1]))
    connected={c for c in grid.roads if grid.is_connected(root,c)}
    if not connected:
        return [PORTALS['estate']] if not g.construction.reason(PORTALS['estate']) else []
    occupied=g.construction.occupied()
    public={(r['cx'],r['cy']) for r in default_roads(g.now)}
    def allowed(c):
        if not in_bounds(*c) or c in occupied:return False
        if c in grid.roads:return True
        return not g.construction.reason(c,occupied) or (c in public and terrain(c) in ('grass','zone'))
    sources=[tuple(c) for c in targets if allowed(tuple(c))]
    if not sources:return []
    if any(c in connected for c in sources):return []
    # 目标包围盒距离是可采纳启发式，避免逐格扫描整个道路集合。
    xmin=min(c[0] for c in connected);xmax=max(c[0] for c in connected)
    ymin=min(c[1] for c in connected);ymax=max(c[1] for c in connected)
    def heuristic(c):return max(xmin-c[0],0,c[0]-xmax)+max(ymin-c[1],0,c[1]-ymax)
    todo=[];cost={};parents={}
    for c in sources:cost[c]=0;parents[c]=None;heapq.heappush(todo,(heuristic(c),0,c))
    end=None
    for _ in range(limit):
        if not todo:break
        _,distance,c=heapq.heappop(todo)
        if distance!=cost[c]:continue
        if c in connected:end=c;break
        for dx,dy in DIRECTIONS:
            nxt=c[0]+dx,c[1]+dy
            if not allowed(nxt):continue
            new=distance+(1 if nxt in grid.roads else 10)
            if new<cost.get(nxt,float('inf')):
                cost[nxt]=new;parents[nxt]=c;heapq.heappush(todo,(new+heuristic(nxt),new,nxt))
    if end is None:return []
    result=[]
    while end is not None:
        if end not in grid.roads:result.append(end)
        end=parents[end]
    return result


class Settlement:
    def __init__(self,g):self.g=g
    @property
    def state(self):return self.g.meta['settlement']

    def boundaries(self):
        s=self.state
        return ([s['next_tick']]+[j['due'] for j in s['jobs']] +
                [p['pregnant_until'] for p in self.g.people if p.get('pregnant_until',0)>self.g.now]) if s['enabled'] else []

    def record(self,text):
        s=self.state;s['history']=(s['history']+[{'ts':self.g.now,'text':text}])[-60:]
        self.g.log('居民自治：'+text)

    def toggle(self):
        s=self.state;s['enabled']=not s['enabled']
        if s['enabled']:
            # 暂停期间不追溯工作，也不重复结算日消耗。
            s['next_tick']=self.g.now+HOUR;s['last_day']=int(self.g.now//DAY)
        else:
            for job in list(s['jobs']):self.cancel(job,'自治暂停，退回预约资源')
        return '居民自治已'+('开启' if s['enabled'] else '暂停')

    def reserve(self,cost):
        return all((self.state['funds'] if k=='funds' else self.state['stock'][k])>=v for k,v in cost.items())

    def change(self,cost,sign):
        for k,v in cost.items():
            if k=='funds':self.state['funds']+=sign*v
            else:self.state['stock'][k]+=sign*v

    def enqueue(self,kind,zone,worker,cost,hours,**payload):
        s=self.state
        if worker.get('job_id') or len(s['jobs'])>=3 or any(j['kind']==kind for j in s['jobs']):return False
        if not self.reserve(cost):return False
        self.change(cost,-1);s['serial']+=1
        ident=f"job-{s['serial']}"
        worker['job_id']=ident
        # 道路距离折算为游戏时间，工作在到达后完成，离线遵循同一时间线。
        start=PORTALS.get(worker.get('destination'),PORTALS['estate'])
        path=self.g.construction.grid.shortest_path(start,PORTALS[zone])
        travel=max(.25,len(path)/24)
        s['jobs'].append(dict(id=ident,kind=kind,zone=zone,worker=worker['person_id'],
            start=self.g.now,arrive=self.g.now+travel*HOUR,due=self.g.now+(travel+hours)*HOUR,
            cost=cost,payload=payload))
        return True

    def cancel(self,job,reason):
        self.change(job['cost'],1)
        self.finish(job)
        self.state['blocked']=reason

    def finish(self,job):
        self.state['jobs'].remove(job)
        p=next((p for p in self.g.people if p['person_id']==job['worker']),None)
        if p:p['job_id']=''

    def complete(self,job):
        g=self.g;s=self.state;kind=job['kind'];zone=job['zone'];stock=s['stock'];payload=job['payload']
        worker=next((p for p in g.people if p['person_id']==job['worker']),None)
        if not worker or not g.construction.connected(zone):
            self.cancel(job,'工作地点道路中断，任务退回资源');return
        if kind=='road':
            cells=[tuple(c) for c in payload['cells']]
            public={(r['cx'],r['cy']) for r in default_roads(g.now)}
            def invalid(c):
                if c in g.construction.occupied():return True
                return bool(g.construction.reason(c)) and not (c in public and terrain(c) in ('grass','zone'))
            if any(invalid(c) for c in cells if c not in g.construction.grid.roads):
                self.cancel(job,'修路位置已被占用，重新规划');return
            old=g.construction.grid.connected_zones();existing=set(g.construction.grid.roads)
            fresh=[c for c in cells if c not in existing]
            for x,y in fresh:g.roads.append(dict(road_id=cell_key((x,y)),cx=x,cy=y,road_style='asphalt',built_ts=g.now,paid=0))
            # 被玩家提前铺好的格子退回公共费用。
            s['funds']+=(len(cells)-len(fresh))*10
            g.meta['roads_built']+=len(fresh);g.construction._changed(old,fresh)
            self.record(f'完成公共道路接入 {len(fresh)} 格')
        elif kind=='open':
            if g.town.level(payload['target_zone']):self.change(job['cost'],1)
            else:
                next(z for z in g.town_zones if z['zone_id']==payload['target_zone'])['level']=1
                if payload['target_zone']=='fields':g.meta['next_field_ts']=g.now+C.TOWN_FIELD_INTERVAL
                self.record('公共预算开放'+C.TOWN_ZONES[payload['target_zone']]['name'])
        elif kind=='build':
            plan=payload['building'];preview=g.construction.building_preview(plan['catalog_key'],plan['cx'],plan['cy'],plan['orientation'])
            if preview['reason']:
                self.cancel(job,'住宅选址失效，退回建材和预算');return
            b=preview['building'];spec=K.CATALOG[b['catalog_key']]
            b.update(building_id=uuid.uuid5(uuid.NAMESPACE_URL,f"{g.meta['town_seed']}:{job['id']}").hex,
                status='building',placed_ts=g.now,finish_ts=g.now+spec['build_hours']*HOUR,community=True)
            g.town_buildings.append(b);g.meta['town_buildings_built']+=1
            g.meta['town_cleared']=sorted(set(g.meta['town_cleared'])|{cell_key(c) for c in preview['cells']})
            g.construction.invalidate();self.record('居民使用公共预算与建材开工'+spec['name'])
        elif kind=='crop':
            if zone!='estate' and not g.town.effective_level(zone):self.cancel(job,'农田已关闭');return
            harvest=max(8,round(30*C.WEATHERS[g.weather.current]['crop']))
            stock['food']=min(1000,stock['food']+harvest);stock['seed']=min(100,stock['seed']+3);stock['feed']=min(300,stock['feed']+6)
            self.record(f'播种、照料后收获 {harvest} 份公共粮食')
        elif kind=='ranch':
            if not g.town.effective_level('ranch'):self.cancel(job,'牧场不可用');return
            stock['food']=min(1000,stock['food']+4*stock['herd'])
            if stock['herd']<12 and stock['feed']>=4 and g.now>=s['next_herd_birth']:
                stock['feed']-=4;stock['herd']+=1;s['next_herd_birth']=g.now+3*DAY
                self.record('公共牧场新增一只幼畜')
        elif kind=='gather':
            stock['wood']=min(300,stock['wood']+6);stock['stone']=min(300,stock['stone']+4)
        elif kind=='trade':
            s['funds']+=36;self.record('居民出售公共余粮，预算增加36')
        elif kind=='harvest':
            try:g.town.harvest()
            except ValueError:
                self.cancel(job,'农田尚未成熟或玩家仓库已满');return
        s['completed']+=1;self.finish(job)

    def road_target(self):
        g=self.g
        if PORTALS['estate'] not in g.construction.grid.roads:return [PORTALS['estate']],'estate'
        # 先恢复已开放区域、玩家住房，再处理新区固定建筑的服务入口。
        for zone in C.TOWN_ZONES:
            if g.town.level(zone) and not g.construction.connected(zone):
                return [PORTALS[zone]],'estate'
        for b in g.town_buildings:
            if not g.construction.building_connected(b):
                return entrances(b),'estate'
        from districts import landmarks
        from town_grid import LANDMARKS
        fixed=landmarks()+[(zone,'',LANDMARKS[index],1) for zone,index in (('homes',0),('market',1),('station',2),('fields',3))]
        for zone,_,(x,y,w,h),_ in fixed:
            if not g.town.level(zone):continue
            b={'cx':x//48,'cy':y//48,'w':math.ceil((x+w)/48)-x//48,'h':math.ceil((y+h)/48)-y//48,'orientation':0}
            targets=entrances(b)
            if not any(g.construction.grid.is_connected(PORTALS['estate'],c) for c in targets):return targets,'estate'
        return None

    def home_site(self):
        g=self.g;occupied=g.construction.occupied();zone='homes' if g.town.effective_level('homes') else 'estate'
        choices=[]
        for x,y in sorted(g.construction.grid.roads):
            if not g.construction.grid.is_connected(PORTALS['estate'],(x,y)):continue
            for cx,cy in ((x,y+1),(x,y-2),(x+1,y),(x-2,y)):
                if any((cx+dx,cy+dy) in occupied for dx in range(2) for dy in range(2)):continue
                p=g.construction.building_preview('cream_home',cx,cy)
                if not p['reason']:choices.append(p['building'])
            if choices:break
        return (choices[0],zone) if choices else None

    def plan(self):
        g=self.g;s=self.state;stock=s['stock'];residents=[p for p in g.people if p['role']=='居民']
        if not s['enabled']:
            s['blocked']='自治已暂停，开启后才会安排公共任务';return
        busy={j['kind'] for j in s['jobs']};workers=[p for p in adults(g) if not p['job_id']]
        if not workers:return
        # 同种任务独占一份资源预约，避免多人重复建房或播同一块田。
        options=[]
        if stock['food']<max(80,len(residents)*8):options.append((100,'crop','fields' if g.town.effective_level('fields') else 'estate',{'seed':1},8,{}))
        if g.meta['field_ready_ts'] and g.town.effective_level('fields'):
            if g.inventory.used+sum(g.meta['field_batch'].values())<=g.storage_capacity:
                options.append((120,'harvest','fields',{},1,{}))
            else:s['blocked']='玩家仓库已满，等待腾出空间后自动收获'
        target=self.road_target() if 'road' not in busy else None
        if target:
            cells=road_plan(g,target[0])
            if cells:options.append((85,'road','estate',{'funds':len(cells)*10},max(1,len(cells)/6),{'cells':cells}))
            else:s['blocked']='没有可施工的接入路线：检查占地和街区开放状态'
        if stock['wood']<24 or stock['stone']<20:options.append((70,'gather','industry' if g.town.effective_level('industry') else 'estate',{'food':1},4,{}))
        if capacity(g)-len(residents)<3 and not any(b['status']=='building' for b in g.town_buildings):
            site=self.home_site()
            if site:options.append((90,'build',site[1],{'funds':240,'wood':12,'stone':8},3,{'building':site[0]}))
        if stock['food']>max(45,len(residents)*6):options.append((60,'trade','market' if g.town.effective_level('market') else 'estate',{'food':12},2,{}))
        if g.town.effective_level('ranch') and stock['herd'] and stock['feed']>=2:
            options.append((65,'ranch','ranch',{'feed':max(2,math.ceil(stock['herd']/2))},6,{}))
        # 根据公共预算逐步开放生产、住房与其他区域，使用现有开放费用。
        for zone in ('fields','homes','ranch','market','park','industry','city','station'):
            if not g.town.level(zone):
                options.append((40,'open','estate',{'funds':C.TOWN_ZONES[zone]['cost']},4,{'target_zone':zone}));break
        for _,kind,zone,cost,hours,payload in sorted(options,key=lambda row:-row[0]):
            if kind in busy or not workers or len(s['jobs'])>=3:continue
            preferred={'crop':'农民','harvest':'农民','gather':'工匠','ranch':'牧民','road':'建筑工','build':'建筑工'}.get(kind,'工匠')
            worker=max(workers,key=lambda p:p['profession']==preferred)
            if self.enqueue(kind,zone,worker,cost,hours,**payload):
                workers.remove(worker);busy.add(kind);s['blocked']=''
            else:s['blocked']='公共资源不足，等待生产或贸易积累'

    def families(self):
        g=self.g;s=self.state;residents=[p for p in g.people if p['role']=='居民'];people={p['person_id']:p for p in residents}
        for p in residents:
            if p['partner_id'] not in people:p['partner_id']=''
        women=[p for p in adults(g) if p['sex']=='女' and not p['partner_id']]
        men=[p for p in adults(g) if p['sex']=='男' and not p['partner_id']]
        for woman,man in zip(women,men):
            # 直系亲属不能组成家庭。
            if set(woman.get('parents',[]))&set(man.get('parents',[])) or woman['person_id'] in man.get('parents',[]) or man['person_id'] in woman.get('parents',[]):continue
            woman['partner_id']=man['person_id'];man['partner_id']=woman['person_id']
        for mother in list(residents):
            due=mother['pregnant_until']
            if due and g.now>=due:
                if len(g.people)>=C.TOWN_MAX_PEOPLE or len(residents)>=capacity(g) or s['stock']['food']<6:
                    mother['pregnant_until']=g.now+DAY;continue
                child=g.town._person('居民',g.now,10000+s['births'])
                child.update(born_ts=g.now,sex='女' if s['births']%2 else '男',partner_id='',pregnant_until=0,
                    family_cooldown=g.now+GROW_UP,job_id='',profession='孩童',parents=[mother['person_id'],mother['partner_id']],name=mother['name'][0]+('小禾' if s['births']%2 else '小安'))
                s['stock']['food']-=6;g.people.append(child);residents.append(child);s['births']+=1
                mother['pregnant_until']=0;mother['family_cooldown']=g.now+14*DAY
                self.record(f"{mother['name']}的家庭迎来新生儿{child['name']}")
            elif mother['sex']=='女' and mother['partner_id'] and not due and mother['family_cooldown']<=g.now and g.now-mother['born_ts']<60*DAY:
                reserved=sum(bool(p.get('pregnant_until')) for p in residents)
                if len(residents)+reserved<capacity(g) and len(g.people)+reserved<C.TOWN_MAX_PEOPLE and s['stock']['food']>=len(residents)*3+6:
                    mother['pregnant_until']=g.now+3*DAY;self.record(mother['name']+'的家庭正在等待新成员')
        for p in adults(g):
            if p['profession']=='孩童':p['profession']='农民';self.record(p['name']+'已长大，可以参加小镇工作')

    def tick(self):
        g=self.g;defaults(g);s=self.state
        if not s['enabled']:return
        for job in list(s['jobs']):
            if job['due']<=g.now:self.complete(job)
        if g.now<s['next_tick']:return
        s['next_tick']=g.now+HOUR
        day=int(g.now//DAY)
        if day>s['last_day']:
            count=sum(p['role']=='居民' for p in g.people)
            need=count*(day-s['last_day']);food=s['stock']['food']
            s['stock']['food']=max(0,food-need);s['last_day']=day
            s['food_days']=s['food_days']+1 if food<need else 0
            if food<need:s['blocked']='公共食物不足，优先种植；家庭暂缓生育'
        self.families();self.plan()


def validate_state(state):
    s=state['meta'].get('settlement')
    if s is None:return
    def require(ok):
        if not ok:raise ValueError('居民自治数据无效')
    require(type(s['enabled']) is bool and type(s['funds']) is int and s['funds']>=0)
    require(all(type(s['stock'][k]) is int and 0<=s['stock'][k]<=10000 for k in ('food','seed','wood','stone','feed','herd')))
    require(0<=s['next_tick'] and len(s['jobs'])<=3 and len({j['id'] for j in s['jobs']})==len(s['jobs']))
    require(len({j['worker'] for j in s['jobs']})==len(s['jobs']))
    require(all(type(s[k]) is int and s[k]>=0 for k in ('serial','births','completed','food_days')))
    require(isinstance(s['history'],list) and len(s['history'])<=60)
    people={p['person_id']:p for p in state['people'] if p['role']=='居民'}
    for p in people.values():
        require(p['sex'] in ('男','女') and isinstance(p['born_ts'],(float,int)) and p['born_ts']<=state['meta']['sim_ts'])
        require(isinstance(p['job_id'],str) and isinstance(p['profession'],str) and p['pregnant_until']>=0)
    for j in s['jobs']:
        require(j['worker'] in people and people[j['worker']]['job_id']==j['id'])
        require(j['kind'] in ('road','open','build','crop','ranch','gather','trade','harvest'))
        require(j['zone'] in C.TOWN_ZONES and 0<=j['start']<=j['arrive']<=j['due'])
        require(all(k in ('funds','food','seed','wood','stone','feed','herd') and type(v) is int and v>=0 for k,v in j['cost'].items()))
        if j['kind']=='road':require(isinstance(j['payload']['cells'],list) and all(len(c)==2 and in_bounds(*c) for c in j['payload']['cells']))
        if j['kind']=='open':require(j['payload']['target_zone'] in C.TOWN_ZONES)
        if j['kind']=='build':
            b=j['payload']['building'];require(b['catalog_key'] in K.CATALOG and in_bounds(b['cx'],b['cy']) and b['orientation'] in range(4))
