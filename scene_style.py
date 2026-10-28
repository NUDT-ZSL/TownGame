"""场景环境色与轻量天气效果，不修改玩法时钟、随机数或天气。"""
import math
import pygame
import art
from appearance import identity_seed

_AMBIENT=art.LRU(8)


def weather_layer(size,phase,weather,time):
    tint={'dawn':(255,217,175,12),'dusk':(239,170,122,23),'night':(25,43,76,58)}.get(phase)
    cloudy=weather in ('阴天','暴雨','雨夹雪')
    if not tint and weather=='晴天':return None
    def background():
        image=pygame.Surface(size,pygame.SRCALPHA)
        if tint:image.fill(tint)
        if cloudy:
            image.blit(art.shade_surface(size,(105,124,136,12 if weather=='阴天' else 24)),(0,0))
        return image
    layer=_AMBIENT.get((tuple(size),phase,weather),background).copy()
    width,height=size;area=width*height/(1280*616)
    if weather in ('小雨','暴雨','雨夹雪'):
        for i in range(round((100 if weather=='暴雨' else 55)*area)):
            seed=identity_seed(str(i),'rain-particle')
            x=(seed%width-time*118)%(width+20)-10
            y=(seed//173%height+time*(380+i%4*35))%(height+24)-12
            pygame.draw.line(layer,(199,222,232,95 if weather=='暴雨' else 65),
                             (round(x),round(y)),(round(x-4),round(y+9+i%4)),1)
    if weather in ('降雪','雨夹雪'):
        for i in range(round(65*area)):
            seed=identity_seed(str(i),'snow-particle')
            x=(seed%width+time*12+math.sin(time+i)*7)%width
            y=(seed//173%height+time*(18+i%4*5))%height
            pygame.draw.circle(layer,(246,250,248,125+i%4*15),(round(x),round(y)),1+i%2)
    if weather=='大风':
        for i in range(round(13*area)):
            seed=identity_seed(str(i),'wind-particle')
            x=(seed%width+time*(150+i%3*22))%(width+40)-20
            y=(seed//173%height+math.sin(time*1.2+i)*5)%height
            length=12+i%4*3
            pygame.draw.lines(layer,(226,234,217,38+i%3*8),False,
                              [(round(x),round(y)),(round(x+length*.5),round(y-2)),(round(x+length),round(y-1))],1)
            if i%3==0:
                pygame.draw.ellipse(layer,(161,164,108,105),(round(x+8),round(y+5),4,2))
    return layer
