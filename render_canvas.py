"""保留逻辑坐标，放大窗口时按绘制顺序在实际像素上重绘界面。

记录裁剪与遮挡顺序，文字直接使用目标字号栅格化，避免先画小再拉大。
普通 Surface（包括场景离屏缓存）保持 Pygame 原有行为。
"""
import functools
import pygame


class Glyph(pygame.Surface):
    def __init__(self, image, render_native):
        super().__init__(image.get_size(), pygame.SRCALPHA)
        self.blit(image, (0, 0))
        self.render_native = render_native
        self.native_key = None
        self.native_image = None

    def native(self, scale):
        # Pygame 的 copy/subsurface 会保留 Surface 子类但不复制 Python 属性。
        # 经过像素编辑的副本应按普通位图处理，不能复用原图的重绘回调。
        if not hasattr(self,'render_native'):
            return pygame.transform.smoothscale(self,(max(1,round(self.get_width()*scale)),
                                                     max(1,round(self.get_height()*scale))))
        if self.native_key != scale:
            self.native_key = scale
            self.native_image = self.render_native(scale)
        self.native_image.set_alpha(self.get_alpha())
        return self.native_image


class RenderCanvas(pygame.Surface):
    def __init__(self, size):
        super().__init__(size)
        self.commands = []
        self.recording = False

    def begin(self, record):
        self.commands.clear()
        self.recording = record

    def record(self, kind, args, kwargs):
        if self.recording:
            self.commands.append((kind, args, kwargs, self.get_clip().copy()))

    def fill(self, color, rect=None, special_flags=0):
        self.record('fill', (color, pygame.Rect(rect) if rect is not None else None, special_flags), {})
        return super().fill(color, rect, special_flags)

    def blit(self, source, dest, area=None, special_flags=0):
        pos = dest.topleft if isinstance(dest,pygame.Rect) else tuple(dest)[:2]
        self.record('blit', (source, pos, pygame.Rect(area) if area is not None else None, special_flags), {})
        return super().blit(source, dest, area, special_flags)


def recorded_region(canvas, area, image):
    """将离屏场景的裁剪、镜头缩放保留到最终呈现，场景内文字也能原生绘制。"""
    area = pygame.Rect(area)
    def render(scale):
        size = (max(1,round(image.get_width()*scale)),max(1,round(image.get_height()*scale)))
        target = pygame.Surface(size,pygame.SRCALPHA)
        sx,sy = size[0]/area.w,size[1]/area.h
        viewport = pygame.Rect(round(-area.x*sx),round(-area.y*sy),
                               round(canvas.get_width()*sx),round(canvas.get_height()*sy))
        replay(canvas,target,viewport)
        return target
    return Glyph(image,render)


def _install_draw_recorders():
    # 只记录 RenderCanvas 目标，不改动离屏素材的绘制与缓存。
    for name in ('rect','ellipse','circle','line','lines','polygon','arc'):
        native = getattr(pygame.draw,name)
        if getattr(native,'_records_canvas',False):
            continue
        def wrap(name,native):
            @functools.wraps(native)
            def draw(surface,*args,**kwargs):
                if isinstance(surface,RenderCanvas):
                    surface.record(name,args,kwargs)
                return native(surface,*args,**kwargs)
            draw._records_canvas = True
            return draw
        setattr(pygame.draw,name,wrap(name,native))


_install_draw_recorders()


def replay(canvas, target, viewport):
    sx,sy = viewport.w/canvas.get_width(),viewport.h/canvas.get_height()
    scale = min(sx,sy)
    def point(pos):
        return (round(viewport.x+pos[0]*sx),round(viewport.y+pos[1]*sy))
    def rect(value):
        value = pygame.Rect(value)
        a,b = point(value.topleft),point(value.bottomright)
        return pygame.Rect(a,(max(0,b[0]-a[0]),max(0,b[1]-a[1])))
    def width(value):
        return max(1,round(value*scale)) if value else 0
    saved_clip = target.get_clip()
    try:
        for kind,args,kwargs,clip in canvas.commands:
            target.set_clip(rect(clip).clip(viewport))
            if kind == 'fill':
                color,area,flags = args
                target.fill(color,rect(area) if area is not None else viewport,flags)
            elif kind == 'blit':
                source,pos,area,flags = args
                if isinstance(source,Glyph) and area is None:
                    image = source.native(scale)
                else:
                    image = source.subsurface(area) if area is not None else source
                    size = (max(1,round(image.get_width()*sx)),max(1,round(image.get_height()*sy)))
                    if size != image.get_size():
                        image = pygame.transform.smoothscale(image,size)
                target.blit(image,point(pos),special_flags=flags)
            else:
                data = list(args)
                options = dict(kwargs)
                # 第一参数均为颜色，其余参数使用各图元的公开位置参数。
                if kind in ('rect','ellipse','arc'):
                    data[1] = rect(data[1])
                    index = 4 if kind == 'arc' else 2
                    if len(data)>index:
                        data[index] = width(data[index])
                    elif kind == 'arc' and 'width' not in options:
                        data.append(width(1))
                    if kind == 'rect':
                        for i in range(3,len(data)):
                            if data[i]>0:
                                data[i] = width(data[i])
                elif kind == 'circle':
                    data[1],data[2] = point(data[1]),width(data[2])
                    if len(data)>3:
                        data[3] = width(data[3])
                elif kind == 'line':
                    data[1],data[2] = point(data[1]),point(data[2])
                    if len(data)>3:
                        data[3] = width(data[3])
                    elif 'width' not in options:
                        data.append(width(1))
                elif kind in ('lines','polygon'):
                    index = 2 if kind == 'lines' else 1
                    data[index] = [point(p) for p in data[index]]
                    if len(data)>index+1:
                        data[index+1] = width(data[index+1])
                    elif kind == 'lines' and 'width' not in options:
                        data.append(width(1))
                for key,value in options.items():
                    if key == 'width' or key.startswith('border_') and value>0:
                        options[key] = width(value)
                getattr(pygame.draw,kind)(target,*data,**options)
    finally:
        target.set_clip(saved_clip)
