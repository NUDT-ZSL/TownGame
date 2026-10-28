"""透视轨道相机与地面射线拾取；世界 XY 为地面，Z 为真实高度。"""
import math


def dot(a,b):return sum(x*y for x,y in zip(a,b))


class Camera3D:
    is_3d=True

    def __init__(self,world_size,viewport=(0,88,1280,616),position=(1160,760)):
        self.world_w,self.world_h=world_size
        self.vx,self.vy,self.width,self.height=viewport
        self.zoom=1.0;self.yaw=math.radians(32);self.pitch=math.radians(49)
        self.cx,self.cy=position[0]+640,position[1]+308
        self.clamp()

    @property
    def x(self):return self.cx-self.width/self.zoom/2

    @x.setter
    def x(self,value):self.cx=value+self.width/self.zoom/2

    @property
    def y(self):return self.cy-self.height/self.zoom/2

    @y.setter
    def y(self,value):self.cy=value+self.height/self.zoom/2

    def basis(self):
        s,c=math.sin(self.yaw),math.cos(self.yaw);sp,cp=math.sin(self.pitch),math.cos(self.pitch)
        right=(c,-s,0);up=(-s*sp,-c*sp,cp);forward=(-s*cp,-c*cp,-sp)
        distance=1450/self.zoom
        eye=(self.cx-forward[0]*distance,self.cy-forward[1]*distance,-forward[2]*distance)
        return eye,right,up,forward,max(200,self.height*1.4)

    def project(self,pos):
        eye,right,up,forward,focal=self.basis()
        v=(pos[0]-eye[0],pos[1]-eye[1],(pos[2] if len(pos)>2 else 0)-eye[2])
        depth=dot(v,forward)
        if depth<=5:return None
        return (self.vx+self.width/2+dot(v,right)*focal/depth,
                self.vy+self.height/2-dot(v,up)*focal/depth,depth)

    def world_to_screen(self,pos):
        p=self.project(pos)
        return p[:2] if p else (-100000,-100000)

    def screen_to_world(self,pos):
        eye,right,up,forward,focal=self.basis()
        u=(pos[0]-self.vx-self.width/2)/focal;v=(self.vy+self.height/2-pos[1])/focal
        ray=tuple(forward[i]+right[i]*u+up[i]*v for i in range(3))
        t=-eye[2]/min(-.001,ray[2])
        return eye[0]+ray[0]*t,eye[1]+ray[1]*t

    def ground_bounds(self):
        points=[self.screen_to_world(p) for p in ((self.vx,self.vy),(self.vx+self.width,self.vy),
                  (self.vx+self.width,self.vy+self.height),(self.vx,self.vy+self.height))]
        return min(p[0] for p in points),min(p[1] for p in points),max(p[0] for p in points),max(p[1] for p in points)

    def clamp(self):
        self.cx=max(0,min(self.world_w,self.cx));self.cy=max(0,min(self.world_h,self.cy))

    def center(self,pos):self.cx,self.cy=pos;self.clamp()

    def pan(self,dx,dy):
        middle=(self.vx+self.width/2,self.vy+self.height/2)
        a=self.screen_to_world(middle);b=self.screen_to_world((middle[0]+dx,middle[1]+dy))
        self.cx-=b[0]-a[0];self.cy-=b[1]-a[1];self.clamp()

    def zoom_at(self,pos,factor):
        a=self.screen_to_world(pos);self.zoom=max(.55,min(1.8,self.zoom*factor));b=self.screen_to_world(pos)
        self.cx+=a[0]-b[0];self.cy+=a[1]-b[1];self.clamp()

    def orbit(self,degrees=0,tilt=0):
        self.yaw=(self.yaw+math.radians(degrees))%(2*math.pi)
        self.pitch=max(math.radians(30),min(math.radians(68),self.pitch+math.radians(tilt)))
