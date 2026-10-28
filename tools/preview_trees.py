"""生成六种树木的四季对照图，以及新版庄园与公园截图。"""
import os
import sys
import tempfile
from pathlib import Path
os.environ['SDL_VIDEODRIVER']='dummy'
os.environ['SDL_AUDIODRIVER']='dummy'
ROOT=Path(__file__).resolve().parents[1];sys.path.insert(0,str(ROOT))
import pygame
import ui
from tree_art import SPECIES,tree_sprite
from db import Database
from game import Game
from interface import ManorApp


def main():
    pygame.init();pygame.display.set_mode((1280,860));fonts=ui.Fonts()
    output=ROOT/'.test-output';output.mkdir(exist_ok=True)
    sheet=pygame.Surface((1280,950));sheet.fill(ui.PANEL)
    fonts.text(sheet,'小镇树木 · 六种轮廓与四季变化',(30,20),30,ui.GREEN)
    for col,(kind,label) in enumerate(SPECIES.items()):
        x=145+col*200
        text=fonts.render(label,20,ui.GREEN);sheet.blit(text,text.get_rect(midtop=(x,76)))
        for row,season in enumerate(('春','夏','秋','冬')):
            y=115+row*198
            if not col:fonts.text(sheet,season,(25,y+70),24,ui.GOLD)
            pygame.draw.ellipse(sheet,(237,235,216),(x-71,y+147,142,25))
            sprite=tree_sprite(kind,7,season,'day',180)
            sheet.blit(sprite,sprite.get_rect(midbottom=(x,y+174)))
    fonts.text(sheet,'同树种仍有固定的枝干、冠幅、叶色和高度差异；可在 D → 树木种类 中自行选择种植。',(30,918),17,ui.MUTED)
    pygame.image.save(sheet,str(output/'tree-catalog.png'))
    with tempfile.TemporaryDirectory() as folder:
        game=Game(Database(Path(folder)/'preview.db'),real_now=1800000000.,seed=42)
        try:
            app=ManorApp(game,settings_path=Path(folder)/'settings.json',scene_mode='2d');app.set_window_size((1600,1000))
            app.toast_until=0;app.draw();pygame.image.save(app.screen,str(output/'varied-estate.png'))
            app.courtyard.focus_zone('park');app.courtyard.camera.zoom=.85;app.draw()
            pygame.image.save(app.screen,str(output/'varied-park.png'))
            app.decorator.enter();app.decorator.set_category('trees');app.toast_until=0;app.draw()
            pygame.image.save(app.screen,str(output/'tree-planting.png'))
        finally:game.db.close()
    pygame.quit();print(output/'tree-catalog.png')


if __name__=='__main__':main()
