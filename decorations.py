"""自由装饰：可移动、旋转、拆除，撤销只逆转本次金币变化。"""
import copy
import uuid
from town_grid import in_bounds,terrain

CATALOG={'flowers':('花坛',20),'lamp':('路灯',35),'bench':('长椅',25),'fence':('围栏',15),'sign':('路牌',18)}
CATALOG.update({'tree_oak':('橡树',45),'tree_birch':('白桦',40),'tree_pine':('松树',45),
                'tree_willow':('垂柳',55),'tree_cherry':('花树',60),'tree_maple':('枫树',50)})


def placement_reason(g,cx,cy,ignore=None):
    if type(cx) is not int or type(cy) is not int or not in_bounds(cx,cy):return '超出小镇边界'
    from town_build import footprint
    occupied={c for b in g.town_buildings for c in footprint(b)}
    reason=g.construction.reason((cx,cy),occupied)
    if reason:return reason
    if (cx,cy) in g.construction.grid.roads:return '不能占用道路'
    if any(d['id']!=ignore and (d['cx'],d['cy'])==(cx,cy) for d in g.meta.get('decorations',[])):
        return '该位置已有装饰'
    return ''


def edit(g,operation,kind=None,cx=None,cy=None,orientation=0,decoration_id=None):
    rows=g.meta.setdefault('decorations',[])
    history=g.meta.setdefault('decoration_undo',[])
    if operation=='undo':
        if not history:raise ValueError('没有可撤销的装饰操作')
        item=history[-1]
        if g.meta['coins']-item['delta']<0:raise ValueError('金币不足，无法撤销拆除退款')
        from town_build import footprint
        occupied=set(g.construction.grid.roads)|{c for b in g.town_buildings for c in footprint(b)}
        validate({'decorations':item['before']},occupied)
        g.meta['coins']-=item['delta'];g.meta['decorations']=copy.deepcopy(item['before']);history.pop()
        g.construction.invalidate()
        return '已撤销最近的装饰操作。'
    if operation not in ('place','move','remove'):raise ValueError('未知装饰操作')
    if type(orientation) is not int or orientation not in range(4):raise ValueError('朝向无效')
    selected=next((d for d in rows if d['id']==decoration_id),None)
    before=copy.deepcopy(rows);coins=g.meta['coins']
    if operation=='place':
        if kind not in CATALOG:raise ValueError('未知装饰')
        reason=placement_reason(g,cx,cy)
        if reason:raise ValueError(reason)
        g._pay(CATALOG[kind][1]);rows.append(dict(id=uuid.uuid4().hex,kind=kind,cx=cx,cy=cy,orientation=orientation))
    else:
        if not selected:raise ValueError('该装饰不存在')
        if operation=='remove':
            rows.remove(selected);g.meta['coins']+=CATALOG[selected['kind']][1]//2
        else:
            reason=placement_reason(g,cx,cy,selected['id'])
            if reason:raise ValueError(reason)
            selected.update(cx=cx,cy=cy,orientation=orientation)
    history.append(dict(before=before,delta=g.meta['coins']-coins))
    g.meta['decoration_undo']=history[-20:]
    g.construction.invalidate()
    return {'place':'装饰已摆放','move':'装饰已移动 / 旋转','remove':'装饰已拆除，退还一半金币'}[operation]


def validate(meta,occupied):
    seen=set()
    for d in meta.get('decorations',[]):
        cell=(d['cx'],d['cy'])
        if not isinstance(d['id'],str) or d['id'] in seen or d['kind'] not in CATALOG:
            raise ValueError('装饰类型或身份错误')
        if type(d['cx']) is not int or type(d['cy']) is not int or not in_bounds(*cell) or terrain(cell) not in ('grass','zone') or cell in occupied:
            raise ValueError('装饰位置重叠或越界')
        if type(d['orientation']) is not int or d['orientation'] not in range(4):raise ValueError('装饰朝向无效')
        occupied.add(cell);seen.add(d['id'])
