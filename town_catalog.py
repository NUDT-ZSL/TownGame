"""小镇建设目录与平衡参数；领域层不依赖Pygame。"""
GRID_SIZE = 48
GRID_COLS, GRID_ROWS = 125, 80
CHUNK_SIZE = 6
ROAD_STYLES = {
    "asphalt": {"name":"沥青街道","cost":10,"color":(123,135,137)},
    "stone": {"name":"青石街","cost":14,"color":(178,177,160)},
    "garden": {"name":"花园砖路","cost":12,"color":(220,198,160)},
}
FAMILIES = {"classic":"古风","modern":"现代","country":"田园","industrial":"工业","public":"公共"}
# key/name/family/category/占地/造价/工期/人口/商业/吸引/税/地价/色板。
_ROWS = (
 ("tile_home","青瓦民居","classic","住宅",(2,2),260,4,4,0,0,2,1,(231,225,204),(103,129,133)),
 ("tea_house","木构茶楼","classic","商业",(2,2),380,6,0,.08,4,3,1,(225,206,174),(91,121,123)),
 ("inn","飞檐客栈","classic","商业",(3,3),680,10,6,.12,5,4,1,(229,216,184),(92,114,126)),
 ("glass_home","玻璃公寓","modern","住宅",(3,3),520,8,10,0,0,4,0,(218,227,218),(117,178,185)),
 ("cafe","街角咖啡","modern","商业",(2,2),420,5,0,.06,3,3,1,(237,220,196),(110,159,161)),
 ("office","云端写字楼","modern","商业",(3,3),900,14,6,.10,4,7,0,(221,225,214),(104,153,175)),
 ("cream_home","奶油小屋","country","住宅",(2,2),240,4,5,0,0,2,1,(246,225,185),(191,129,103)),
 ("flower_shop","花房商店","country","商业",(2,2),360,5,0,.07,5,3,2,(242,229,195),(155,172,139)),
 ("windmill","风车粮仓","country","产业",(3,2),480,8,2,0,1,3,1,(231,212,170),(164,124,88)),
 ("brick_workshop","红砖工坊","industrial","产业",(3,2),620,10,0,0,0,6,-2,(183,130,105),(114,128,127)),
 ("iron_store","铸铁仓库","industrial","产业",(3,2),540,8,0,0,0,5,-2,(159,153,136),(103,121,125)),
 ("steam_factory","蒸汽厂房","industrial","产业",(3,3),980,16,0,0,-2,12,-4,(178,117,94),(105,123,122)),
 ("pocket_park","口袋公园","public","公共",(3,3),400,6,0,0,6,0,3,(154,183,128),(220,203,159)),
 ("town_square","小镇广场","public","公共",(4,3),800,12,0,0,12,0,5,(211,204,175),(140,180,170)),
 ("bus_stop","公交站","public","公共",(2,1),200,3,0,0,1,0,1,(217,225,205),(91,151,144)),
)
CATALOG = {}
for key,name,family,category,footprint,cost,hours,pop,commerce,attraction,tax,desirability,wall,roof in _ROWS:
    CATALOG[key] = dict(key=key,name=name,family=family,category=category,footprint=footprint,cost=cost,
        build_hours=hours,population=pop,commerce=commerce,attraction=attraction,tax=tax,desirability=desirability,
        description=f"{FAMILIES[family]}{category}，完工且道路通达后提供经营效果。",palette={"wall":wall,"roof":roof})
ROAD_REFUND = .5
BUILDING_REFUND = {"building":.15,"ready":.30}
LAND_ROAD = 2
LAND_PUBLIC_RADIUS, LAND_INDUSTRY_RADIUS = 6,4
LAND_FACTOR_MIN, LAND_FACTOR_MAX, LAND_FACTOR_PER_POINT = .75,1.40,.025
CROP_WINDMILL_BONUS = .15
BUS_TRAFFIC_BONUS = .10
ATTRACTION_TRAFFIC = .01
MAX_VEHICLES = 40
TRAFFIC_BASE = 5
TRAFFIC_PER_PERSON = .25
TRAFFIC_SPAWN_INTERVAL = .65
TRAFFIC_PEAK_MULT = 1.25
TRAFFIC_NIGHT_MULT = .45
TRAFFIC_PEAK_HOURS = ((7,10),(17,20))
TRAFFIC_NIGHT_HOURS = (5,22)
TRAFFIC_PER_BUILDING = 1
TRAFFIC_PER_COMMERCE = 8
TRAFFIC_NIGHT_SPEED = .85
TRAFFIC_MAX_DT = .05
VEHICLE_GAP = 55
VEHICLE_TIMEOUT = 9
VEHICLE_SPEEDS = {"car":52,"bus":39,"truck":35}
VEHICLE_NAMES = {"car":"小轿车","bus":"公交车","truck":"货车"}
LANE_OFFSET = 10
SIDEWALK_OFFSET = 23
