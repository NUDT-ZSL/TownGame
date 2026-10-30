"""从现有存档生成小镇实体模型，显示层不改写任何经营数据。"""
import math
import random
from functools import lru_cache
import art
import courtyard
import game_config as C
import town_catalog as K
from appearance import identity_seed,pet_variant
from landscape import world_trees,estate_trees,ground_details
from mesh3d import Mesh,road_mesh
from town_grid import cell_center,world_to_cell,LANDMARKS
from pet_art import palette_for


def scaled_mesh(mesh,sx,sy,sz):
    result=Mesh()
    for i in range(0,len(mesh.data),10):
        p=mesh.data[i:i+3];n=mesh.data[i+3:i+6]
        normal=(n[0]/sx,n[1]/sy,n[2]/sz);length=math.sqrt(sum(v*v for v in normal)) or 1
        result.vertex((p[0]*sx,p[1]*sy,p[2]*sz),tuple(v/length for v in normal),
                      tuple(v*255 for v in mesh.data[i+6:i+9])+(mesh.data[i+9],))
    return result


@lru_cache(maxsize=64)
def tree_mesh(species,variant,season):
    m=Mesh();rng=random.Random(identity_seed(f'{species}:{variant}','tree-model'))
    leaf={'oak':(113,157,92),'birch':(150,181,111),'pine':(68,129,105),
          'willow':(133,166,98),'cherry':(131,171,112),'maple':(110,153,104)}[species]
    if season=='春' and species=='cherry':leaf=(239,182,188)
    if season=='秋' and species!='pine':leaf={'oak':(192,156,83),'birch':(224,186,89),'willow':(180,176,103),'cherry':(197,134,102),'maple':(206,110,71)}[species]
    if season=='冬':leaf=art.mix(leaf,(224,237,225),.7)
    trunk=(219,211,187) if species=='birch' else (143,108,79)
    m.cone((0,0,0),6,105,trunk,8)
    lean=(variant%5-2)*3
    if species=='pine':
        for z,r,h in ((40,40,64),(70,31,57),(99,21,48)):m.cone((lean,0,z),r,h,leaf,10)
    else:
        width={'oak':1.12,'birch':.55,'willow':1.25,'cherry':.95,'maple':1.08}[species]
        for i in range(5):
            angle=i*math.tau/5;radius=18 if i else 0
            x=lean+math.cos(angle)*radius*width;y=math.sin(angle)*radius*width
            z=88+rng.randrange(28);rx=(25+rng.randrange(10))*width
            m.cone((x*.5,y*.5,44),3,z-44,trunk,5)
            if season=='冬':
                m.ellipsoid((x,y,z),(rx*.8,rx*.7,9),(234,240,229),8,4)
            else:
                m.ellipsoid((x,y,z),(rx,rx*.8,32 if species!='willow' else 26),art.lighten(leaf,(i%3-1)*.035),10,6)
        if species=='willow' and season!='冬':
            for i in range(10):
                a=i*math.tau/10;m.ellipsoid((lean+math.cos(a)*38,math.sin(a)*30,65),(8,8,28),leaf,6,5)
    if species=='birch':
        for z in range(14,75,13):m.box((0,5,z),(8,1.5,2),(107,111,93))
    # 树冠顶点带风动标记，树干根部保持固定。
    for i in range(9,len(m.data),10):m.data[i]=-1
    return scaled_mesh(m,.85,.85,1.0)


@lru_cache(maxsize=12)
def tower_mesh(width,depth,floors):
    m=Mesh();w=width*.88;d=depth*.82;height=floors*34
    m.box((0,0,4),(width,depth,8),(190,197,192))
    m.box((0,0,8+height/2),(w,d,height),(206,219,219))
    for floor in range(floors):
        z=24+floor*34
        m.box((0,0,z-14),(w+3,d+3,3),(145,163,171))
        for col in range(5):
            x=-w*.4+col*w*.2
            for side in (-1,1):m.box((x,side*(d/2+1),z),(w*.13,2,22),(101,156,180))
        for col in range(3):
            y=-d*.32+col*d*.32
            for side in (-1,1):m.box((side*(w/2+1),y,z),(2,d*.21,22),(111,169,186))
    m.box((0,0,height+11),(w+8,d+8,6),(101,128,145))
    m.box((w*.2,0,height+19),(35,32,12),(157,174,178))
    m.box((0,d/2+2,23),(23,3,30),(83,129,146))
    return m


@lru_cache(maxsize=96)
def house_mesh(width,depth,kind,level=1,stage=4):
    m=Mesh();spec=K.CATALOG.get(kind)
    base=C.BUILDINGS.get(kind,{}).get('color',(195,158,119))
    wall=spec['palette']['wall'] if spec else art.mix(base,(249,236,202),.72)
    roof=spec['palette']['roof'] if spec else art.mix(base,(127,129,116),.20)
    if stage==4 and kind in ('牧场','鸟舍','游乐区'):
        m.box((0,0,2),(width,depth,4),(179,194,138))
        for x in (-width*.44,width*.44):
            for y in (-depth*.4,depth*.4):m.box((x,y,19),(4,4,38),(194,160,111))
        for y in (-depth*.4,depth*.4):
            for z in (15,29):m.box((0,y,z),(width*.9,3,4),(213,181,131))
        if kind=='鸟舍':
            m.box((0,-depth*.12,43),(width*.55,depth*.5,45),(218,206,165));m.roof(width*.65,depth*.7,66,25,roof)
            m.box((0,depth*.15,46),(22,3,21),(88,118,111))
        elif kind=='游乐区':
            m.box((0,0,25),(4,depth*.65,45),base);m.box((0,0,49),(width*.7,5,5),base)
            m.ellipsoid((width*.25,depth*.12,15),(16,16,16),(222,158,125),10,6)
        else:
            m.box((-width*.2,-depth*.12,19),(34,29,30),(226,208,160))
            m.cone((-width*.2,-depth*.12,34),28,22,(206,181,119),8)
        return m
    if stage==4 and kind in ('pocket_park','town_square','bus_stop'):
        m.box((0,0,2),(width*.94,depth*.94,4),(209,202,170))
        if kind=='bus_stop':
            for x in (-width*.38,width*.38):m.box((x,-depth*.22,30),(4,4,60),(95,128,116))
            m.box((0,-depth*.1,62),(width*.95,depth*.85,5),roof)
            m.box((0,0,16),(width*.7,12,5),(181,150,110))
            m.box((width*.44,depth*.3,28),(4,4,52),(95,128,116))
            m.box((width*.44,depth*.3,53),(17,4,20),(225,210,162))
        elif kind=='town_square':
            m.ellipsoid((0,0,8),(width*.25,depth*.28,8),(175,190,170),20,5)
            m.ellipsoid((0,0,15),(width*.21,depth*.24,2),(131,195,196),20,4)
            m.cone((0,0,15),7,40,(217,221,191),10)
            m.ellipsoid((0,0,54),(15,15,4),(217,221,191),12,5)
        else:
            m.plane((-width*.4,-depth*.4,width*.8,depth*.8),4.5,(152,187,116))
            for side in (-1,1):
                m.cone((side*width*.25,-depth*.18,5),4,50,(146,115,81),8)
                m.ellipsoid((side*width*.25,-depth*.18,63),(27,23,29),(115,160,98),10,6)
                m.box((side*width*.25,depth*.25,15),(35,12,5),(181,150,110))
        return m
    modern=spec and spec['family']=='modern';height=(100+level*10)*(1.5 if modern else 1)
    if stage<4:height*=.25+.17*stage
    w,d=width*.88,depth*.83
    m.box((0,0,4),(width,depth,8),(200,190,162))
    m.box((0,0,9+height/2),(w,d,height),wall)
    m.box((0,0,10),(w+3,d+3,7),art.darken(wall,.15))
    if stage==4:
        if modern:m.box((0,0,height+13),(w+12,d+12,9),roof)
        else:m.roof(w+20,d+20,height+9,37+level*3,roof)
        # 屋檐、门框与玻璃窗均为有厚度的实体，转动视角可看见侧面。
        m.box((0,d/2+1,29),(24,4,44),(165,133,99))
        m.box((0,d/2+3.2,29),(17,1,36),(92,116,113))
        m.ellipsoid((6,d/2+4,28),(1.5,1.5,1.5),(239,209,131),6,4)
        m.box((0,d/2+12,4),(34,21,6),(218,207,178))
        glass=(195,221,210,1)
        for x in (-w*.30,w*.30):
            m.box((x,d/2+1,height*.59),(23,4,29),(245,237,210))
            m.box((x,d/2+3.3,height*.59),(18,1,23),glass)
            m.box((x,d/2+4,height*.59),(2,2,24),(250,239,210))
            m.box((x,d/2+4,height*.59),(19,2,2),(250,239,210))
            m.box((x,d/2+5,height*.40),(28,11,5),(162,136,100))
            for j in (-7,0,7):m.ellipsoid((x+j,d/2+5,height*.40+5),(4,4,5),(124,167,94),6,4)
        for side in (-1,1):
            for y in (-d*.26,d*.26):
                m.box((side*(w/2+1),y,height*.6),(3,22,27),(245,237,210))
                m.box((side*(w/2+3),y,height*.6),(1,17,21),glass)
        if kind in ('商店','flower_shop','cafe','tea_house'):
            m.box((0,d/2+12,height*.80),(w*.72,28,5),roof)
        if kind=='宠物医院':
            m.box((0,d/2+3,height*.86),(22,3,6),(196,111,114));m.box((0,d/2+4,height*.86),(6,3,22),(196,111,114))
        if kind in ('brick_workshop','steam_factory','仓库'):
            m.box((w*.26,-d*.16,height+26),(16,16,55),art.darken(wall,.15))
    else:
        for x in (-w/2-8,w/2+8):
            for y in (-d/2-8,d/2+8):m.box((x,y,65),(4,4,130),(180,147,98))
        for z in (40,85):m.box((0,d/2+8,z),(w+20,4,4),(180,147,98))
    if kind=='windmill':
        for a in range(4):
            theta=a*math.pi/2+.4;x=math.cos(theta)*24;z=height+math.sin(theta)*24
            m.box((x,d/2+15,z),(8,5,36),(240,227,184))
    return m


@lru_cache(maxsize=192)
def pet_mesh(species,variant,rare,stage,sick,pose,frame):
    m=Mesh();p=palette_for(species,rare,stage,sick,variant);body=p['body'];face=p['face'];eye=p['eye']
    bird=species in ('鹦鹉','小鸭','孔雀');long=species in ('羊驼','梅花鹿')
    z=28 if not long else 44
    # 模型正面朝 +Y；沿真正的行进方向旋转，不使用左右翻图。
    m.ellipsoid((0,0,z),(20 if not bird else 17,27,23),body,12,8)
    if long:m.ellipsoid((0,15,65),(10,10,28),body,10,6)
    hz=48 if not long else 90;hy=19 if not long else 23
    if pose=='sleep':hz-=15;hy+=5
    elif pose=='eat':hz-=8+math.sin(frame*math.pi/2)*3;hy+=3
    m.ellipsoid((0,hy,hz),(18,17,18),face,12,8)
    m.ellipsoid((0,hy+12,hz-6),(11,7,8),p['belly'],10,6)
    for side in (-1,1):
        m.ellipsoid((side*8,hy+14,hz+3),(2.2,2,.8 if pose in ('sleep','idle_blink') else 2.7),eye,8,5)
        if pose not in ('sleep','idle_blink'):m.ellipsoid((side*8-.5,hy+15.5,hz+4),( .7,.7,.9),(255,250,230),6,4)
    m.ellipsoid((0,hy+19,hz-3),(3.2,2.7,2.5),p.get('nose',(91,67,62)),8,5)
    stride=math.sin(frame*math.pi/2)*5 if pose=='walk' else 0
    for x in (-12,12):
        for y in ((0,15) if bird else (-17,17)):
            m.ellipsoid((x,y+stride*(1 if x*y>0 else -1),10 if not long else 18),(7,10,10 if not long else 22),p['paw'],8,5)
    if species=='兔子':
        ears=variant//32%3
        for side in (-1,1):
            tilt=.9 if ears==1 or ears==2 and side==1 else -.12
            m.ellipsoid((side*9,hy-2,hz+24),(6,5,22),body,10,6,tilt)
            m.ellipsoid((side*9,hy+2,hz+24),(3,2,17),p['ear'],8,5,tilt)
        m.ellipsoid((0,-27,27),(9,8,9),p['belly'],8,5)
    elif species in ('猫咪','狐狸','梅花鹿'):
        for side in (-1,1):m.cone((side*12,hy-1,hz+9),8,18,body,4)
        if species=='梅花鹿':
            for side in (-1,1):
                m.cone((side*12,hy-4,hz+17),3,25,(151,119,84),5)
                m.cone((side*18,hy-4,hz+20),2,16,(151,119,84),5)
        else:m.ellipsoid((8,-30,38),(8,21,8),body,10,6,.5)
    elif species=='小狗':
        for side in (-1,1):m.ellipsoid((side*17,hy-1,hz-3),(7,8,17),p['ear'],10,6,.2)
        m.ellipsoid((0,-27,36),(6,17,7),body,8,5,.65)
    elif species=='绵羊':
        for i in range(8):
            a=i*math.tau/8;m.ellipsoid((math.cos(a)*17,math.sin(a)*22,36),(11,11,12),p['belly'],8,5)
        for side in (-1,1):m.ellipsoid((side*19,hy,hz),(9,5,5),p['ear'],8,5)
    elif species=='刺猬':
        for i in range(18):
            a=i*2.4;r=5+i%4*4;m.cone((math.cos(a)*r,math.sin(a)*r-7,43+i%3*2),3,12,p['paw'],4)
    elif bird:
        m.cone((0,hy+20,hz-7),6,11,p.get('beak',(238,163,71)),6)
        for side in (-1,1):m.ellipsoid((side*17,-2,30),(5,20,14),p.get('tail',p['ear']),10,6)
        if species=='孔雀':
            for i in range(7):
                a=(i-3)*.25;x=math.sin(a)*48;zz=50+math.cos(a)*25
                m.ellipsoid((x,-29,zz),(10,4,25),(82,155,139),8,5)
                m.ellipsoid((x,-24,zz+10),(5,2,6),(189,189,98),8,5)
    else:
        for side in (-1,1):m.ellipsoid((side*14,hy-2,hz+12),(7,5,9),p['ear'],8,5)
    pattern=variant//8%4
    if species=='兔子' and pattern in (1,2):
        if pattern==1:
            for side in (-1,1):m.ellipsoid((side*8,hy+13.5,hz+3),(6,2,7),art.darken(p['paw'],.22),10,6)
        else:m.ellipsoid((0,hy+14,hz+11),(4,3,8),p['belly'],10,6)
    elif pattern:
        for i in range(pattern*2):m.ellipsoid(((-1)**i*14,-15+i*6,44),(5,5,3),p['belly'] if pattern!=1 else p['paw'],8,4)
    detail=variant//96%4
    if species=='兔子':
        for i in range(detail):m.cone(((i-(detail-1)/2)*5,hy,hz+15),4,8,p['belly'],6)
    elif detail==1:
        for side in (-1,1):m.ellipsoid((side*5,hy+13,hz-18),(5,3,4),p['accent'],8,5)
    elif detail==2:m.box((0,hy+13,hz-18),(9,4,9),p['accent'])
    elif detail==3:
        for i in range(5):
            a=i*math.tau/5;m.ellipsoid((math.cos(a)*4,hy+13,hz-18+math.sin(a)*4),(3,2,3),p['accent'],6,4)
        m.ellipsoid((0,hy+15,hz-18),(2,2,2),(248,217,126),6,4)
    if pose=='bath':
        for i in range(5):
            a=i*math.tau/5;r=3+i%3;zz=18+(i*11+frame*7)%48
            m.ellipsoid((math.cos(a)*24,math.sin(a)*23,zz),(r,r,r),(186,225,228),8,5)
    elif pose=='eat':
        for side in (-1,1):m.ellipsoid((side*6,hy+19,18+frame%2*3),(2,2,2),(209,170,105),6,4)
    if species!='兔子':
        width=(1,.86,1.14)[variant//32%3]
        for i in range(0,len(m.data),10):
            m.data[i]*=width
        m.bounds[0][0]*=width;m.bounds[1][0]*=width
    return m


@lru_cache(maxsize=512)
def person_mesh(identity,frame=0,manager=False,action=''):
    seed=identity_seed(identity,'person-model');m=Mesh()
    clothes=((180,128,139),(108,154,165),(198,164,107),(136,151,115),(155,140,174),(202,142,108))[seed%6]
    skin=((241,205,167),(219,178,137),(174,132,104))[seed//7%3];hair=((103,81,62),(72,66,58),(148,118,78))[seed//13%3]
    height=1+(seed//19%5-2)*.03
    for sign in (-1,1):
        step=math.sin(frame*math.pi/2)*4*sign
        m.box((sign*6,step,14),(8,9,25),(84,100,98));m.ellipsoid((sign*6,step+3,4),(5,8,4),(96,84,71),8,4)
        m.ellipsoid((sign*15,-step,39),(5,5,14),skin,8,5)
    m.ellipsoid((0,0,39),(14,10,18),clothes,10,6)
    m.ellipsoid((0,0,68*height),(12,11,14),skin,10,7)
    m.ellipsoid((0,-2,75*height),(13,11,9),hair,10,6)
    for sign in (-1,1):m.ellipsoid((sign*4.5,10,68*height),(1.3,1.3,1.7),(67,60,49),6,4)
    if seed//31%3==0 or manager:
        m.ellipsoid((0,0,80*height),(20,17,3),(220,193,133),10,4)
        m.ellipsoid((0,0,85*height),(13,12,6),(220,193,133),10,4)
    if seed//37%3==1:m.ellipsoid((0,-8,60),(10,8,15),art.darken(clothes,.2),8,5)
    if manager:
        m.box((21,8,28),(3,3,55),(147,113,76));m.box((21,8,3),(17,12,5),(212,182,119))
    if action=='read':
        m.box((0,14,40),(24,4,16),(116,151,132))
        m.box((0,16.5,40),(20,1,13),(246,235,207))
        m.box((0,17.5,40),(1,1,13),(167,154,130))
    elif action=='farm':
        # 挥动手持园艺工具；仅展示动作，不改变产量。
        tilt=math.sin(frame*math.pi/2)*5
        m.box((18,12+tilt,25),(3,3,40),(155,117,78))
        m.box((18,13+tilt,4),(12,6,4),(113,130,118))
    return scaled_mesh(m,.25,.25,.25)


@lru_cache(maxsize=40)
def vehicle_mesh(kind,color):
    m=Mesh();length=62 if kind in ('bus','train') else 45 if kind=='truck' else 36
    m.box((0,0,13),(24,length,16),color)
    if kind=='car':m.box((0,-3,25),(21,20,12),art.lighten(color,.12))
    else:m.box((0,-5,28),(23,length-7,24),color)
    m.box((0,length/2-3,24),(18,2,10),(179,214,215))
    for side in (-1,1):
        for y in (-length*.3,length*.3):
            m.ellipsoid((side*12,y,8),(4,6,6),(67,76,72),8,5)
            m.ellipsoid((side*15,y,8),(1,3,3),(176,183,169),8,5)
        for y in (-10,5):m.box((side*12.2,y,26),(1,10,10),(179,214,215))
        m.box((side*8,length/2+.5,13),(5,1,4),(252,236,178,1))
    return scaled_mesh(m,.56,1.5,.65)


@lru_cache(maxsize=48)
def decoration_mesh(kind,variant=0,season='春'):
    if kind.startswith('tree_'):return tree_mesh(kind[5:],variant%24,season)
    m=Mesh();wood=(174,135,94);metal=(95,119,109)
    if kind=='bench':
        m.box((0,0,17),(38,16,5),wood);m.box((0,-7,29),(38,4,20),wood)
        for x in (-14,14):m.box((x,0,7),(4,14,14),metal)
    elif kind=='fence':
        for x in (-21,21):m.box((x,0,21),(5,5,42),wood)
        for z in (14,31):m.box((0,0,z),(45,4,5),(208,178,127))
    elif kind=='lamp':
        m.box((0,0,4),(14,14,8),metal);m.box((0,0,43),(4,4,79),metal)
        m.box((0,0,86),(14,14,17),(248,224,156,1));m.cone((0,0,94),13,10,metal,4)
    elif kind in ('sign','sanitation'):
        m.box((0,0,18),(4,4,36),wood);m.box((0,0,36),(37,5,22),(223,198,145))
        m.box((0,3,36),(25,1,3),(100,135,101))
    elif kind in ('flowers','meadow'):
        if kind=='flowers':m.box((0,0,4),(40,33,8),(196,159,117))
        for i in range(7):
            x=i%3*10-10;y=i//3*10-10;m.cone((x,y,7),2,10,(117,155,85),5)
            m.ellipsoid((x,y,18),(5,5,4),((242,182,178),(247,216,136),(219,189,224))[i%3],8,4)
    elif kind=='rock':
        m.ellipsoid((-5,0,5),(12,9,7),(151,163,150),7,4)
        m.ellipsoid((9,2,4),(8,7,5),(168,176,159),7,4)
    elif kind=='shrub':
        for x,y,z in ((-10,0,9),(6,-4,12),(0,8,8)):m.ellipsoid((x,y,z),(12,10,12),(130,168,101),8,5)
    elif kind=='bowl':
        m.ellipsoid((0,0,7),(25,20,9),(179,134,90),12,5);m.ellipsoid((0,0,13),(20,15,3),(203,177,115),12,4)
    elif kind=='bath':
        m.ellipsoid((0,0,13),(36,25,16),(228,230,206),12,6);m.ellipsoid((0,0,24),(29,19,3),(134,199,202),12,4)
    elif kind=='toy':
        m.ellipsoid((0,0,14),(14,14,14),(222,151,121),12,8);m.box((0,0,14),(3,29,3),(243,216,154))
    elif kind=='waste':
        for z,r in ((4,12),(10,8),(16,4)):m.ellipsoid((0,0,z),(r,r*.8,5),(158,115,70),8,5)
    return m


class World3D:
    def __init__(self):self.signature=None;self.static=[];self.builds=0

    def build_static(self,g,view):
        signature=(g.season,g.meta['road_revision'],tuple((b.building_id,b.kind,b.level,b.unlocked) for b in g.buildings.values()),
                   tuple((b['building_id'],b['status'],b['orientation']) for b in g.town_buildings),
                   tuple((d['id'],d['kind'],d['cx'],d['cy'],d['orientation']) for d in g.meta.get('decorations',[])),
                   tuple(g.meta['town_cleared']),tuple(z['level'] for z in g.town_zones))
        if self.signature==signature:return self.static
        self.signature=signature;self.builds+=1;objects=[]
        def add(mesh,x,y,z=0,key=None,angle=0,scale=1):objects.append((mesh,(x,y,z),angle,scale,key))
        grass=courtyard.SEASONS[g.season]['grass'];terrain=Mesh()
        terrain.box((C.WORLD_SIZE[0]/2,C.WORLD_SIZE[1]/2,-20),(*C.WORLD_SIZE,40),art.darken(grass,.10))
        for x in range(0,C.WORLD_SIZE[0],240):
            for y in range(0,C.WORLD_SIZE[1],240):terrain.plane((x,y,240,240),.1,grass)
        # 大片草地的轻微色差与花草有实际位置，不随帧随机跳动。
        for i in range(480):
            x=(i*337+97)%C.WORLD_SIZE[0];y=(i*193+181)%C.WORLD_SIZE[1]
            terrain.plane((x,y,5+i%6,2),.2,art.lighten(grass,.035 if i%3 else -.035))
        fx,fy,fw,fh=C.FARM_WORLD_RECT
        terrain.plane((fx+605,fy+24,70,fh-24),.5,(228,215,179))
        for x in range(28,1260,42):
            if 590<x<690:continue
            terrain.box((fx+x,fy+603,12),(4,4,24),(207,176,123))
            terrain.box((fx+x+17,fy+603,16),(38,3,3),(207,176,123))
        for row in range(4):terrain.plane((fx+65,fy+158+row*88,1150,24),.5,(228,215,179))
        for bid,b in sorted(g.buildings.items()):
            x=fx+67+bid%C.GRID_COLS*230;y=fy+58+bid//C.GRID_COLS*88
            terrain.plane((x,y,170,80),.4,art.lighten(grass,.035) if b.unlocked else art.darken(grass,.045))
            if not b.kind:
                # 空地也能点击开建筑详情，地面 ID 和实体 ID 使用相同机制。
                plot=Mesh();plot.plane((-85,-40,170,80),.45,art.lighten(grass,.035) if b.unlocked else art.darken(grass,.045))
                add(plot,x+85,y+40,key=('building',bid))
        for key,spec in C.TOWN_ZONES.items():
            if key=='estate':continue
            x,y,w,h=spec['rect'];terrain.plane((x+12,y+12,w-24,h-24),.3,art.mix(grass,spec['color'],.12))
        # 车站轨道、三节立体车厢。
        sx,sy,sw,sh=C.TOWN_ZONES['station']['rect']
        terrain.box((sx+sw/2,sy+365,2),(sw-70,76,4),(192,185,156))
        for x in range(sx+45,sx+sw-40,26):terrain.box((x,sy+365,5),(5,65,5),(165,141,105))
        for y in (sy+346,sy+384):terrain.box((sx+sw/2,y,8),(sw-70,3,4),(113,129,121))
        for i in range(3):add(vehicle_mesh('train',(111,167,154)),sx+510+i*95,sy+365,9,angle=-math.pi/2,scale=1.4)
        px,py,pw,ph=C.TOWN_ZONES['park']['rect'];water=Mesh()
        water.ellipsoid((0,0,0),((pw-200)/2,(ph-320)/2,2),(123,185,187),48,6)
        for i in range(9,len(water.data),10):water.data[i]=-2
        add(water,px+pw/2,py+180+(ph-320)/2,1)
        terrain.box((px+pw/2,py+370,8),(pw-70,65,12),(211,176,124))
        for x in range(px+40,px+pw-35,27):terrain.box((x,py+370,15),(2,65,1),(173,142,100))
        # 农田有垄沟和实际高度的作物。
        ax,ay,aw,ah=C.TOWN_ZONES['fields']['rect']
        for row in range(5):
            terrain.box((ax+310,ay+180+row*115,2),(390,70,4),(168,143,97))
            for col in range(10):
                x=ax+140+col*35;y=ay+180+row*115
                terrain.cone((x,y,4),8,18,(158,182,102),5)
        add(terrain,0,0)
        for road in g.roads:add(road_mesh(road['road_style'],g.construction.grid.mask((road['cx'],road['cy']))),*cell_center(road['cx'],road['cy']))
        for bid,rect in view.farm._layout_buildings(g).items():
            b=g.buildings[bid];x,y=view.local_to_world(rect.midbottom)
            add(house_mesh(128,70,b.kind,b.level),x,y-35,key=('building',bid))
        # 已有街区的地标复用原保护占地，避免影响铺路规则。
        for index,kind in ((0,'饲养小屋'),(1,'商店'),(3,'仓库')):
            x,y,w,h=LANDMARKS[index];zone=('homes','market','fields')[0 if index==0 else 1 if index==1 else 2]
            add(house_mesh(w,h*.68,kind,max(1,g.town.level(zone))),x+w/2,y+h*.58,key=('zone',zone))
        add(house_mesh(270,150,'商店',max(1,g.town.level('station'))),sx+265,sy+200,key=('zone','station'))
        from districts import landmarks
        for zone,kind,(x,y,w,h),level in landmarks():
            mesh=house_mesh(w,h,kind,level)
            if zone=='city':mesh=tower_mesh(w,h,4+level)
            add(mesh,x+w/2,y+h/2,key=('zone',zone))
        # 牧场围栏留出南北通道，草场与城市铺装有不同质感。
        rx,ry,rw,rh=C.TOWN_ZONES['ranch']['rect']
        for x in range(rx+40,rx+rw-40,80):
            if abs(x-1800)>65:
                terrain.box((x,ry+rh-80,18),(5,5,36),(198,171,120))
                terrain.box((x+36,ry+rh-80,25),(76,3,4),(214,185,137))
        for b in g.town_buildings:
            if b['status']!='ready':continue
            width,depth=K.CATALOG[b['catalog_key']]['footprint']
            add(house_mesh(width*48,depth*48,b['catalog_key']),*cell_center(b['cx']+(b['w']-1)/2,b['cy']+(b['h']-1)/2),
                key=('town_building',b['building_id']),angle=b['orientation']*math.pi/2)
        occupied=g.construction.occupied();cleared=set(g.meta['town_cleared'])
        for tree in world_trees():
            cell=world_to_cell(tree.x,tree.y)
            if cell in occupied or cell in g.construction.grid.roads or f'{cell[0]},{cell[1]}' in cleared:continue
            add(tree_mesh(tree.species,tree.variant,g.season),tree.x,tree.y,scale=tree.height/145)
        for x,y,kind,variant in ground_details():
            cell=world_to_cell(x,y)
            if cell in occupied or cell in g.construction.grid.roads or f'{cell[0]},{cell[1]}' in cleared:continue
            add(decoration_mesh('meadow' if kind=='flowers' else kind),x,y,scale=.7)
        for tree in estate_trees():add(tree_mesh(tree.species,tree.variant,g.season),fx+tree.x,fy+tree.y,scale=tree.height/145)
        for d in g.meta.get('decorations',[]):
            add(decoration_mesh(d['kind'],identity_seed(d['id'],'tree')%24,g.season),*cell_center(d['cx'],d['cy']),
                key=('decoration',d['id']),angle=d['orientation']*math.pi/2,scale=.8 if d['kind'].startswith('tree_') else 1)
        for kind,pos in (('bowl',courtyard.BOWL),('bath',courtyard.BATH),('toy',courtyard.TOY)):
            add(decoration_mesh(kind),*view.local_to_world(pos),key=('prop',kind))
        add(decoration_mesh('sanitation'),*view.local_to_world((110,652)),key=('sanitation',True))
        self.static=objects;return objects

    def objects(self,g,view,actors):
        objects=list(self.build_static(g,view))
        def add(mesh,x,y,key,angle=0,scale=1,z=0):objects.append((mesh,(x,y,z),angle,scale,key))
        from signal_art import signal_mesh
        from signals import phase
        lamps=signal_mesh(phase(view.traffic.signals.time))
        for cell in view.traffic.signals.junctions:add(lamps,*cell_center(*cell),None)
        for actor in actors:
            variant=pet_variant(actor.pet_id,g.meta);frame=int(actor.anim*6)%4
            mesh=pet_mesh(actor.species,variant,actor.rare,actor.stage,actor.sick,actor.pose,frame if actor.state in ('walk','eat','pet','bath') else 0)
            angle=math.atan2(-(actor.target[0]-actor.x),actor.target[1]-actor.y) if actor.state=='walk' else -math.pi/2*actor.facing
            scale=.5*{'幼崽':.72,'少年':.86,'成年':1,'老年':.94}[actor.stage]
            add(mesh,*view.local_to_world((actor.x,actor.y)),('pet',actor.pet_id),angle,scale,z=abs(math.sin(actor.anim*5))*1.2 if actor.state in ('walk','pet') else 0)
        people={p['person_id']:p for p in g.people}
        if g.town.effective_level('ranch'):
            herd=g.meta.get('settlement',{}).get('stock',{}).get('herd',0)
            for i in range(min(12,herd)):
                x=1510+(i%4)*140;y=2830+(i//4)*100
                add(pet_mesh('绵羊',i%8,False,'成年',False,'idle',0),x,y,None,scale=.55)
        for actor in view.people.values():
            if not actor.visible:continue
            angle=0
            if actor.index<len(actor.points):
                dx,dy=actor.points[actor.index][0]-actor.x,actor.points[actor.index][1]-actor.y;angle=math.atan2(-dx,dy)
            action=getattr(actor,'action','')
            frame=int(actor.anim*(3 if action=='farm' else 7))%4 if actor.path or action=='farm' else 0
            person=people.get(actor.ident,{})
            child=person.get('role')=='居民' and g.now-person.get('born_ts',-1e20)<7*C.DAY
            add(person_mesh(actor.ident,frame,False,action),actor.x,actor.y,('person',actor.ident),angle,scale=.6 if child else 1)
        for v in view.traffic.vehicles:add(vehicle_mesh(v.kind,tuple(v.color)),v.x,v.y,('vehicle',v.vehicle_id),math.radians(-v.angle+180))
        for pile in sorted(g.waste,key=lambda w:w['created_ts'])[:C.MAX_DRAWN_WASTE]:
            x,y=view.farm.sanitation.point(pile['u'],pile['v']);add(decoration_mesh('waste'),*view.local_to_world((x,y)),('waste',pile['waste_id']))
        pose=view.farm.sanitation.manager_pose(g)
        if pose:
            state=pose[2];feeding=state in ('前往喂食','喂食中')
            frame=int(view.time*(4 if state in ('清扫中','喂食中') else 7))%4 if state in ('前往清扫','前往喂食','清扫中','喂食中') else 0
            add(person_mesh('farm-manager',frame,not feeding),*view.local_to_world(pose[:2]),('manager',True),view.farm.sanitation.manager_angle)
        for b in g.town_buildings:
            if b['status']=='ready':continue
            stage=min(3,int(4*(g.now-b['placed_ts'])/max(1,b['finish_ts']-b['placed_ts'])))
            width,depth=K.CATALOG[b['catalog_key']]['footprint']
            add(house_mesh(width*48,depth*48,b['catalog_key'],1,stage),*cell_center(b['cx']+(b['w']-1)/2,b['cy']+(b['h']-1)/2),('town_building',b['building_id']),b['orientation']*math.pi/2)
        return objects
