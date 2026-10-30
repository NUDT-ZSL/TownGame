"""可复用的实体网格：盒体、屋顶、球体、圆锥与地面。无图片冒充模型。"""
import math
from functools import lru_cache
from array import array


def unit(v):
    length=math.sqrt(sum(x*x for x in v)) or 1
    return tuple(x/length for x in v)


class Mesh:
    def __init__(self):self.data=array('f');self.bounds=None

    def vertex(self,p,n,color):
        self.data.extend((*p,*n,*(c/255 for c in color[:3]),color[3] if len(color)>3 else 0))
        if self.bounds is None:self.bounds=[list(p),list(p)]
        else:
            for i in range(3):self.bounds[0][i]=min(self.bounds[0][i],p[i]);self.bounds[1][i]=max(self.bounds[1][i],p[i])

    def face(self,points,color,normal=None):
        if normal is None:
            a=tuple(points[1][i]-points[0][i] for i in range(3));b=tuple(points[2][i]-points[0][i] for i in range(3))
            normal=unit((a[1]*b[2]-a[2]*b[1],a[2]*b[0]-a[0]*b[2],a[0]*b[1]-a[1]*b[0]))
        for i in range(1,len(points)-1):
            for p in (points[0],points[i],points[i+1]):self.vertex(p,normal,color)

    def box(self,center,size,color):
        x,y,z=center;a,b,c=(s/2 for s in size)
        v=[(x+sx*a,y+sy*b,z+sz*c) for sx,sy,sz in ((-1,-1,-1),(1,-1,-1),(1,1,-1),(-1,1,-1),
                                                  (-1,-1,1),(1,-1,1),(1,1,1),(-1,1,1))]
        for indices in ((0,3,2,1),(4,5,6,7),(0,1,5,4),(1,2,6,5),(2,3,7,6),(3,0,4,7)):
            self.face([v[i] for i in indices],color)

    def plane(self,rect,z,color):
        x,y,w,h=rect;self.face(((x,y,z),(x+w,y,z),(x+w,y+h,z),(x,y+h,z)),color,(0,0,1))

    def ellipsoid(self,center,radii,color,segments=10,rings=6,tilt=0):
        cx,cy,cz=center;rx,ry,rz=radii;ct,st=math.cos(tilt),math.sin(tilt)
        def point(j,i):
            theta=j*math.pi/rings;phi=i*math.tau/segments
            a,b,c=math.sin(theta)*math.cos(phi),math.sin(theta)*math.sin(phi),math.cos(theta)
            p=(rx*a,ry*b,rz*c);n=unit((a/rx,b/ry,c/rz))
            return ((cx+p[0],cy+p[1]*ct-p[2]*st,cz+p[1]*st+p[2]*ct),
                    (n[0],n[1]*ct-n[2]*st,n[1]*st+n[2]*ct))
        for j in range(rings):
            for i in range(segments):
                corners=[point(j,i),point(j,i+1),point(j+1,i+1),point(j+1,i)]
                for tri in ((0,1,2),(0,2,3)):
                    # 反向绕序，让法线朝向球体外侧。
                    for k in reversed(tri):self.vertex(corners[k][0],corners[k][1],color)

    def cone(self,center,radius,height,color,segments=10):
        x,y,z=center
        for i in range(segments):
            a,b=i*math.tau/segments,(i+1)*math.tau/segments
            p=(x+math.cos(a)*radius,y+math.sin(a)*radius,z)
            q=(x+math.cos(b)*radius,y+math.sin(b)*radius,z)
            self.face((p,q,(x,y,z+height)),color)
        self.face([(x+math.cos(-i*math.tau/segments)*radius,y+math.sin(-i*math.tau/segments)*radius,z) for i in range(segments)],color,(0,0,-1))

    def roof(self,width,depth,z,height,color):
        a,b=width/2,depth/2
        self.face(((-a,-b,z),(a,-b,z),(0,-b,z+height)),color)
        self.face(((a,b,z),(-a,b,z),(0,b,z+height)),color)
        self.face(((-a,b,z),(-a,-b,z),(0,-b,z+height),(0,b,z+height)),color)
        self.face(((a,-b,z),(a,b,z),(0,b,z+height),(0,-b,z+height)),color)

    def shadow(self,rx,ry,color):
        self.face([(math.cos(i*math.tau/20)*rx,math.sin(i*math.tau/20)*ry,.7) for i in range(20)],color,(0,0,1))


@lru_cache(maxsize=32)
def ground_tile(color,size=240):
    m=Mesh();m.plane((-size/2,-size/2,size,size),0,color);return m


@lru_cache(maxsize=32)
def road_mesh(style,mask):
    colors={'asphalt':(130,142,142),'stone':(190,188,167),'garden':(216,188,144)}
    m=Mesh();m.box((0,0,1),(48,48,2),(195,198,178));m.plane((-23,-23,46,46),2.2,colors[style])
    for bit,(dx,dy) in enumerate(((0,-1),(1,0),(0,1),(-1,0))):
        if mask&(1<<bit):
            m.plane((-23 if dy else (-24 if dx<0 else 0),-23 if dx else (-24 if dy<0 else 0),
                     46 if dy else 24,24 if dy else 46),2.25,colors[style])
            if style=='asphalt':m.plane((dx*15-1,dy*15-1,2 if dx else 3,3 if dx else 2),2.5,(238,227,177))
    return m
