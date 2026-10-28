"""窗口呈现与坐标转换。玩法始终使用逻辑画布，窗口可独立缩放。"""
import json
from pathlib import Path
import pygame
import game_config as C
from render_canvas import replay


def prepare_dpi():
    """在创建窗口前声明 DPI 感知，避免 Windows 再次对整个窗口做模糊缩放。"""
    import sys
    if sys.platform == 'win32':
        import ctypes
        try:
            ctypes.windll.user32.SetProcessDpiAwarenessContext(ctypes.c_void_p(-4))
        except (AttributeError,OSError):
            ctypes.windll.user32.SetProcessDPIAware()


class Display:
    def __init__(self, settings_path=None):
        self.path = Path(settings_path) if settings_path else C.ROOT/'ui-settings.json'
        self.windowed_size = C.WINDOW
        self.fullscreen = False
        self.font_scale = 1.0
        self.scene_mode = '3d'
        try:
            data = json.loads(self.path.read_text(encoding='utf-8'))
            if not isinstance(data, dict):
                data = {}
            size = data.get('window_size', C.WINDOW)
            if len(size) == 2 and all(isinstance(v, int) and low <= v <= 7680 for v,low in zip(size,(640,430))):
                self.windowed_size = tuple(size)
            self.fullscreen = data.get('fullscreen') is True
            if data.get('scene_mode') in ('2d','3d'):
                self.scene_mode = data['scene_mode']
            if data.get('font_scale') in (1.0, 1.15, 1.3):
                self.font_scale = data['font_scale']
        except (OSError, ValueError, TypeError):
            pass
        self.set_mode()

    def set_mode(self):
        self.window = pygame.display.set_mode((0, 0) if self.fullscreen else self.windowed_size,
                                              pygame.FULLSCREEN if self.fullscreen else pygame.RESIZABLE)
        # 部分 SDL 驱动退出全屏时先恢复桌面尺寸，再次指定窗口尺寸才生效。
        if not self.fullscreen and self.window.get_size() != self.windowed_size:
            self.window = pygame.display.set_mode(self.windowed_size, pygame.RESIZABLE)
        self.update_viewport()

    def update_viewport(self):
        width, height = self.window.get_size()
        factor = min(1,width/C.WINDOW[0], height/C.WINDOW[1])
        self.logical_size=(max(1280,round(width/factor)),max(860,round(height/factor)))
        self.viewport = pygame.Rect(0,0,width,height)

    def resize(self, size):
        if not self.fullscreen:
            self.windowed_size = (max(640, size[0]), max(430, size[1]))
            self.set_mode()

    def to_logical(self, pos):
        return (round((pos[0]-self.viewport.x)*self.logical_size[0]/self.viewport.w),
                round((pos[1]-self.viewport.y)*self.logical_size[1]/self.viewport.h))

    def to_native(self, pos):
        return (round(self.viewport.x+pos[0]*self.viewport.w/self.logical_size[0]),
                round(self.viewport.y+pos[1]*self.viewport.h/self.logical_size[1]))

    def native_rect(self, rect):
        a, b = self.to_native(rect.topleft), self.to_native(rect.bottomright)
        return pygame.Rect(a, (b[0]-a[0], b[1]-a[1]))

    def present(self, canvas):
        self.window.fill((29, 36, 30))
        if getattr(canvas,'recording',False):
            replay(canvas,self.window,self.viewport)
        elif self.viewport.size == canvas.get_size():
            self.window.blit(canvas, self.viewport)
        else:
            self.window.blit(pygame.transform.smoothscale(canvas, self.viewport.size), self.viewport)
        pygame.display.flip()

    def save(self):
        data = {'window_size': self.windowed_size, 'fullscreen': self.fullscreen,
                'font_scale': self.font_scale, 'scene_mode': self.scene_mode}
        temp = self.path.with_suffix('.tmp')
        try:
            temp.write_text(json.dumps(data, indent=2), encoding='utf-8')
            temp.replace(self.path)
            return True
        except OSError:
            return False
