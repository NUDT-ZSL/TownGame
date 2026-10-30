"""信号灯的二维和三维外观，共享交通调度相位。"""
from functools import lru_cache
import pygame
from mesh3d import Mesh
from signals import phase
from town_grid import cell_center


@lru_cache(maxsize=4)
def signal_mesh(state):
    mesh=Mesh()
    for side in (-1,1):
        x,y=side*22,side*22
        mesh.box((x,y,23),(3,3,46),(87,99,96))
        for axis in (0,1):
            xx=x+axis*7
            mesh.box((xx,y,44),(6,5,17),(53,64,62))
            green=state==('ns' if axis==0 else 'ew')
            for z,color in ((49,(82,95,83) if green else (244,80,66,1)),(39,(75,223,121,1) if green else (70,89,78))):
                mesh.ellipsoid((xx,y+3,z),(2.2,1,2.2),color,6,4)
        mesh.box((x,y+4,28),(6,3,7),(70,226,126,1) if state=='walk' else (239,87,70,1))
    for side in (-1,1):
        for stripe in range(-16,20,7):mesh.plane((stripe,side*19-2,4,4),2.7,(229,227,207))
    return mesh


def draw_signals(view,screen):
    state=phase(view.traffic.signals.time)
    for cell in view.traffic.signals.junctions:
        x,y=cell_center(*cell)
        px,py=view.camera.world_to_screen((x+22,y+22))
        if not view.viewport.collidepoint(px,py):continue
        pygame.draw.line(screen,(75,89,79),(px,py),(px,py-30),3)
        for i,axis in enumerate(('ns','ew','walk')):
            color=(76,217,119) if state==axis else (231,81,67)
            pygame.draw.circle(screen,(53,64,60),(round(px+i*7),round(py-32)),5)
            pygame.draw.circle(screen,color,(round(px+i*7),round(py-32)),3)
