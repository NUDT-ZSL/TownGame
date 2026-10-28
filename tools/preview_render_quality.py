"""固定镜头、时间和天气，生成渲染质量对比截图，不读取正式存档。"""
import argparse
import os
import sys
import tempfile
from pathlib import Path
os.environ['SDL_VIDEODRIVER']='dummy'
os.environ['SDL_AUDIODRIVER']='dummy'
ROOT=Path(__file__).resolve().parents[1];sys.path.insert(0,str(ROOT))
import pygame
import ui
import game_config as C
from db import Database
from game import Game
from interface import ManorApp


def main():
    parser=argparse.ArgumentParser();parser.add_argument('--tag',default='after');args=parser.parse_args()
    output=ROOT/'.test-output';output.mkdir(exist_ok=True)
    with tempfile.TemporaryDirectory() as folder:
        g=Game(Database(Path(folder)/'render.db'),real_now=1800000000.,seed=42)
        try:
            app=ManorApp(g,settings_path=Path(folder)/'ui.json');app.set_window_size((1600,1000))
            for name,hour,weather in (('day-clear',12,'晴天'),('night-wind',21,'大风'),('rain',16,'小雨')):
                g.meta['sim_ts']=g.meta['start_ts']//C.DAY*C.DAY+hour*C.HOUR
                g.meta['weather']=weather;app.toast_until=0;app.courtyard.time=1
                app.courtyard.camera.zoom=.8;app.courtyard.camera.center((1850,790))
                app.draw();path=output/f'render-{args.tag}-{name}.png'
                pygame.image.save(app.screen,str(path));print(path)
            if args.tag=='after' and (output/'render-before-night-wind.png').exists():
                sheet=pygame.Surface((1280,470));sheet.fill(ui.PANEL)
                fonts=ui.Fonts()
                for i,tag in enumerate(('before','after')):
                    source=pygame.image.load(str(output/f'render-{tag}-night-wind.png'))
                    crop=source.subsurface((220,115,1040,650))
                    sheet.blit(pygame.transform.smoothscale(crop,(616,385)),(16+i*632,55))
                    fonts.text(sheet,'调整前' if i==0 else '调整后',(20+i*632,17),23,ui.GREEN)
                fonts.text(sheet,'同一镜头 · 21:00 夜间 · 大风：降低灰雾与风线干扰，改善材质、光照与缩放。',(20,444),15,ui.MUTED)
                pygame.image.save(sheet,str(output/'render-comparison.png'))
        finally:g.db.close();pygame.quit()


if __name__=='__main__':main()
