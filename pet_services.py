"""供单只操作、批量照护和关注列表共用的规则提示。"""
import game_config as C


def action_reason(g,p,action,batch=False):
    if not p or not p.active: return '请选择在园伙伴'
    if p.fostered: return '寄养中，请先接回'
    if batch and action=='feed' and p.hunger>=95:return '已经吃饱，无需批量喂食'
    if batch and action=='bath' and p.cleanliness>=95:return '已经干净，无需批量洗澡'
    if action=='feed':
        return '' if any('feed' in spec and item!='腐败物' and g.inventory.count(item) for item,spec in C.ITEMS.items()) else '缺少可用食物'
    if action=='bath':
        missing=[name for name in ('清水','沐浴露') if not g.inventory.count(name)]
        return '缺少'+'、'.join(missing) if missing else ''
    if action=='play': return '互动冷却中' if p.interact_ready_ts>g.now else ''
    if action=='collect':
        if not p.product_ready_ts or p.product_expiry_ts<=g.now: return '暂无成熟产物'
        n=C.PRODUCT_RARE_COUNT if p.rare else C.PRODUCT_NORMAL_COUNT
        return '仓库容量不足' if g.inventory.used+n>g.storage_capacity else ''
    if action=='treat': return '' if p.unwell else '目前健康，无需治疗'
    return '未知操作'


def attention_items(g):
    result=[]
    for p in g.living:
        if p.unwell: result.append((f'{p.name}需要治疗','pet',{'pet_id':p.pet_id}))
        elif not p.fostered and p.hunger<45: result.append((f'{p.name}肚子饿了','pet',{'pet_id':p.pet_id}))
        if not p.fostered and p.product_ready_ts and p.product_expiry_ts>g.now:
            result.append((f'{p.name}有产物待收取','pet',{'pet_id':p.pet_id}))
    if g.inventory.used>=g.storage_capacity*.9:
        result.append((f'仓库即将满：{g.inventory.used}/{g.storage_capacity}','inventory',{}))
    for b in g.town_buildings:
        if not g.construction.building_connected(b):
            result.append(('建筑道路中断，收益暂停','town_building',{'building_id':b['building_id']}))
    if g.meta.get('field_ready_ts'):
        result.append(('农田作物等待收获','zone',{'zone_id':'fields'}))
    return result
