"""共享的路口信号：南北、东西、行人独立相位，含清空路口时间。"""
import math
from town_grid import cell_center


def phase(seconds):
    t=seconds%32
    return 'ns' if t<9 else 'ew' if 11<=t<20 else 'walk' if 22<=t<29 else 'clear'


class Signals:
    def __init__(self):self.time=0.;self.revision=None;self.junctions=()

    def update(self,dt,grid):
        self.time+=max(0,min(.25,dt))
        if self.revision!=grid.road_revision:
            self.revision=grid.road_revision
            self.junctions=tuple(sorted(c for c in grid.roads if len(grid.neighbors(c))>=3))

    def allows(self,dx,dy,pedestrian=False):
        current=phase(self.time)
        return current=='walk' if pedestrian else current==('ew' if abs(dx)>abs(dy) else 'ns')

    def stop(self,x,y,nx,ny,pedestrian=False,half_length=0):
        dx,dy=nx-x,ny-y;distance=math.hypot(dx,dy)
        if distance<1e-8 or self.allows(dx,dy,pedestrian):return False
        ux,uy=dx/distance,dy/distance
        line=26+half_length
        for cell in self.junctions:
            cx,cy=cell_center(*cell)
            ahead=(cx-x)*ux+(cy-y)*uy;lateral=abs((cx-x)*uy-(cy-y)*ux)
            # 已进入路口的实体继续离开，禁止在路口中央等下一次绿灯。
            if lateral<25 and ahead>=line-.01 and ahead-distance<line:return True
        return False
