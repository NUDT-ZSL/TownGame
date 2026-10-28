"""生成行人与兔子的程序化美术对照图，无需正式存档。"""
import os
import sys
from pathlib import Path
os.environ['SDL_VIDEODRIVER']='dummy'
os.environ['SDL_AUDIODRIVER']='dummy'
ROOT=Path(__file__).resolve().parents[1]
sys.path.insert(0,str(ROOT))
import pygame
import ui
import town_art
from pet_art import PetArtRenderer
from appearance import rabbit_variant,rabbit_description


def main():
    pygame.init()
    pygame.display.set_mode((1280,860))
    fonts=ui.Fonts(); renderer=PetArtRenderer()
    sheet=pygame.Surface((1280,850)); sheet.fill(ui.PANEL)
    fonts.text(sheet,'小镇居民 · 每个人都有自己的样子',(32,22),28,ui.GREEN)
    fonts.text(sheet,'同一种衣色，也有不同发型、衣着、身材与配饰。下排为同一批行人的雨天形象。',(32,66),17,ui.MUTED)
    for row,wet in enumerate((False,True)):
        for i in range(12):
            sprite=town_art.person_sprite(1,False,wet,0,f'person-{i}',-1 if i%3==0 else 1)
            image=sprite.native(1.6)
            sheet.blit(image,image.get_rect(midbottom=(64+i*104,260+row*150)))
    pygame.draw.line(sheet,ui.LINE,(32,425),(1248,425))
    fonts.text(sheet,'庄园兔子 · 毛色、花纹和耳型组合',(32,446),28,ui.GREEN)
    for i in range(8):
        ident=next(f'preview-rabbit-{n}' for n in range(10000)
                   if (lambda v:v%8==i and v//8%4==i%4 and v//32%3==i%3)(rabbit_variant(f'preview-rabbit-{n}')))
        sprite=renderer.portrait('兔子','成年',height=190,appearance=rabbit_variant(ident))
        x=80+i*160
        sheet.blit(sprite,sprite.get_rect(midbottom=(x,730)))
        coat,pattern,ears=rabbit_description(ident).split(' · ')
        for y,label,size,color in ((742,coat,20,ui.GREEN),(774,pattern+' · '+ears,15,ui.MUTED)):
            image=fonts.render(label,size,color)
            sheet.blit(image,image.get_rect(midtop=(x,y)))
    fonts.text(sheet,'外观绑定永久身份，场景、头像与档案保持一致；不会影响稀有属性、玩法或存档随机数。',(32,817),16,ui.MUTED)
    output=ROOT/'.test-output'/'appearance-catalog.png'; output.parent.mkdir(exist_ok=True)
    pygame.image.save(sheet,str(output)); print(output)
    pygame.quit()


if __name__=='__main__':
    main()
