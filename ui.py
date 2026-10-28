"""Pygame 通用组件。组件不访问 Game/SQLite，便于替换画面和复用。"""
from pathlib import Path
import pygame
import game_config as C
import art
from render_canvas import Glyph


# 暖阳奶油 + 森林绿：浅色底、可可棕文字，强调色保证浅底可读。
BG = (246, 240, 226)
PANEL = (255, 251, 242)
PANEL_LIGHT = (239, 229, 210)
PANEL_DEEP = (226, 212, 189)
TEXT = (58, 47, 37)
MUTED = (120, 105, 86)
GREEN = (56, 98, 68)
GREEN_LIGHT = (125, 161, 116)
GOLD = (164, 112, 38)
RED = (176, 76, 66)
LINE = (205, 190, 166)
SKY_TOP = (184, 220, 226)
SKY_LOW = (250, 235, 202)
CREAM = (255, 247, 230)
BROWN = (122, 88, 58)
SHADOW = (40, 46, 36, 70)

# 统一鼠标与输入法坐标，覆盖缩放、全屏及两侧留边。
display = None
canvas_size = C.WINDOW
pointer_transform = None


def pointer():
    pos = pygame.mouse.get_pos()
    pos=display.to_logical(pos) if display else pos
    return pointer_transform(pos) if pointer_transform else pos


class Fonts:
    def __init__(self):
        self.content_scale = 1.0
        candidates = [C.ROOT/"assets"/"font.ttf", Path("C:/Windows/Fonts/msyh.ttc"),
                      Path("C:/Windows/Fonts/simhei.ttf"), Path("/usr/share/fonts/opentype/noto/NotoSansCJK-Regular.ttc")]
        self.path = next((str(p) for p in candidates if p.exists()), None)
        if not self.path:
            self.path = pygame.font.match_font("microsoftyahei,simhei,notosanscjksc,wenquanyizenhei,arialunicode")
        self.cache = {}
        self.rendered = art.LRU(1024)

    def get(self, size=18):
        if size not in self.cache:
            self.cache[size] = pygame.font.Font(self.path, size)
        return self.cache[size]

    def render(self, text, size=18, color=TEXT):
        key = (str(text), size, tuple(color))
        return self.rendered.get(key, lambda: Glyph(
            self.get(size).render(str(text), True, color),
            lambda scale:self.get(max(1,round(size*scale))).render(str(text),True,color)))

    def text(self, surface, text, pos, size=18, color=TEXT):
        surface.blit(self.render(text, size, color), pos)

    def wrap(self, text, width, size=18):
        font = self.get(size)
        result = []
        for paragraph in str(text).split("\n"):
            line = ""
            for char in paragraph:
                if font.size(line+char)[0] > width and line:
                    result.append(line)
                    line = char
                else:
                    line += char
            result.append(line)
        return result


class Button:
    def __init__(self, rect, label, callback=None, enabled=True, tone=None, variant=None):
        self.rect = pygame.Rect(rect)
        self.label, self.callback, self.enabled, self.tone = label, callback, enabled, tone
        self.variant = variant

    def draw(self, surface, fonts, offset=(0, 0)):
        rect = self.rect.move(offset)
        hover = rect.collidepoint(pointer()) and self.enabled
        if self.variant:
            accent = self.tone or GREEN
            if not self.enabled:
                fill, border, fg = (239, 235, 225), (225, 219, 207), (155, 148, 134)
            elif self.variant == 'primary':
                fill, border, fg = art.lighten(accent, .10) if hover else accent, accent, PANEL
            else:
                fill, border, fg = ((232, 239, 227) if hover else PANEL), (205, 213, 196), self.tone or TEXT
            pygame.draw.rect(surface, fill, rect, border_radius=10)
            pygame.draw.rect(surface, border, rect, 1, border_radius=10)
            size = getattr(self, 'font_size', 16)
            while fonts.get(size).size(self.label)[0] > rect.width-24 and size > 11:
                size -= 1
            rendered = fonts.render(self.label, size, fg)
            surface.blit(rendered, rendered.get_rect(center=rect.center))
            return
        if not self.enabled:
            fill, border, fg = PANEL_LIGHT, LINE, (150, 136, 116)
            shadow = 0
        elif hover:
            fill = self.tone or GREEN
            border = art.lighten(fill, .22)
            fg = PANEL
            shadow = 70
        else:
            fill = PANEL_LIGHT
            border = self.tone or LINE
            fg = self.tone or TEXT
            shadow = 40
        if shadow:
            art.draw_shadow(surface, (rect.centerx, rect.bottom), (max(8, rect.width-8), max(6, rect.height//4)),
                            shadow, 4)
        art.clay_panel(surface, rect, fill, 10, border, shadow_alpha=0)
        pygame.draw.line(surface, art.lighten(fill, .10),
                         (rect.x+8, rect.y+2), (rect.right-8, rect.y+2), 1)
        size = 16
        while fonts.get(size).size(self.label)[0] > rect.width-14 and size > 11:
            size -= 1
        rendered = fonts.render(self.label, size, fg)
        surface.blit(rendered, rendered.get_rect(center=rect.center))

    def click(self, pos, offset=(0, 0)):
        if self.enabled and self.rect.move(offset).collidepoint(pos):
            if self.callback:
                self.callback()
            return True
        return False


class ChoiceCard(Button):
    def __init__(self,rect,label,image,callback,selected=False):
        super().__init__(rect,label,callback)
        self.image,self.selected=image,selected

    def draw(self,surface,fonts,offset=(0,0)):
        r=self.rect.move(offset)
        pygame.draw.rect(surface,(228,239,217) if self.selected else PANEL_LIGHT,r,border_radius=9)
        pygame.draw.rect(surface,GREEN if self.selected else LINE,r,2 if self.selected else 1,border_radius=9)
        surface.blit(self.image,self.image.get_rect(midbottom=(r.centerx,r.bottom-27)))
        text=fonts.render(self.label,13,GREEN if self.selected else TEXT)
        surface.blit(text,text.get_rect(midtop=(r.centerx,r.bottom-23)))


class ProgressBar:
    COLORS = {"健康": GREEN, "饱食": (186, 132, 46), "开心": (74, 138, 158),
              "清洁": (126, 112, 168), "好感": (196, 108, 126)}

    def __init__(self, rect, label, value, compact=False):
        self.rect, self.label, self.value = pygame.Rect(rect), label, value
        self.compact = compact

    def draw(self, surface, fonts, offset=(0, 0)):
        r = self.rect.move(offset)
        color = self.COLORS.get(self.label, GREEN)
        if self.value <= 25:
            color = RED
        if self.compact:
            fonts.text(surface, self.label, (r.x, r.y), 14, MUTED)
            bar = pygame.Rect(r.x+38, r.y+4, max(10, r.w-92), 12)
            value_size = 13
        else:
            fonts.text(surface, self.label, (r.x, r.y), 17, MUTED)
            bar = pygame.Rect(r.x+62, r.y+5, max(10, r.w-118), 13)
            value_size = 16
        pygame.draw.rect(surface, PANEL_DEEP, bar, border_radius=6)
        fill = bar.copy()
        fill.width = int(bar.width*max(0, min(100, self.value))/100)
        if fill.width:
            pygame.draw.rect(surface, color, fill, border_radius=6)
            if fill.width > 6:
                pygame.draw.line(surface, art.lighten(color, .28),
                                 (fill.x+4, fill.y+2), (fill.right-4, fill.y+2), 1)
        fonts.text(surface, f"{self.value:.1f}", (r.right-46, r.y), value_size, TEXT)


class TextBox:
    """中文输入使用 TEXTINPUT/IME，避免仅处理 KEYDOWN 导致无法输入中文。"""
    def __init__(self, rect, text="", limit=C.RENAME_MAX):
        self.rect, self.text, self.limit = pygame.Rect(rect), text, limit
        self.active = True
        self.composition = ""

    def handle(self, event, offset=(0, 0)):
        rect = self.rect.move(offset)
        if event.type == pygame.MOUSEBUTTONDOWN and event.button == 1:
            self.active = rect.collidepoint(event.pos)
        if self.active:
            pygame.key.set_text_input_rect(display.native_rect(rect) if display else rect)
            if event.type == pygame.TEXTINPUT:
                self.text = (self.text+event.text)[:self.limit]
                self.composition = ""
            elif event.type == pygame.TEXTEDITING:
                self.composition = event.text
            elif event.type == pygame.KEYDOWN and event.key == pygame.K_BACKSPACE:
                self.text = self.text[:-1]

    def draw(self, surface, fonts, offset=(0, 0)):
        rect = self.rect.move(offset)
        pygame.draw.rect(surface, BG, rect, border_radius=8)
        pygame.draw.rect(surface, GREEN if self.active else LINE, rect, 2 if self.active else 1, border_radius=8)
        old_clip = surface.get_clip()
        surface.set_clip(rect.inflate(-12, -4).clip(old_clip))
        fonts.text(surface, self.text+self.composition+("|" if self.active and pygame.time.get_ticks()%1000 < 500 else ""),
                   (rect.x+10, rect.y+9), 20, TEXT)
        surface.set_clip(old_clip)


class ScrollTextPanel:
    def __init__(self, rect):
        self.rect = pygame.Rect(rect)
        self.scroll = 0

    def handle(self, event, count):
        if event.type == pygame.MOUSEWHEEL and self.rect.collidepoint(pointer()):
            self.scroll = max(0, min(max(0, count-4), self.scroll-event.y*2))

    def draw(self, surface, fonts, lines):
        art.clay_panel(surface, self.rect, PANEL, 12, LINE)
        fonts.text(surface, "庄园手记 / 滚轮查看", (self.rect.x+16, self.rect.y+10), 16, GREEN)
        old_clip = surface.get_clip()
        surface.set_clip(self.rect.inflate(-24, -16).clip(old_clip))
        for index, line in enumerate(lines[self.scroll:self.scroll+4]):
            fonts.text(surface, line, (self.rect.x+16, self.rect.y+37+index*23), 15, MUTED)
        surface.set_clip(old_clip)


class Modal:
    """可滚动非阻塞弹窗，只截获输入，不暂停底层游戏与绘制。"""
    def __init__(self, title, fonts, on_close, on_back=None):
        self.is_side = False
        self.rect = pygame.Rect(120, 70, 1040, 720)
        self.body = pygame.Rect(152, 162, 976, 620)
        self.title, self.fonts = title, fonts
        self.blocks, self.buttons, self.inputs = [], [], []
        self.scroll, self.cursor = 0, 0
        self.close = Button((1094, 88, 64, 36), "关闭", on_close, variant='secondary')
        self.back = Button((1000, 88, 96, 36), "返回上页", on_back, variant='secondary') if on_back else None

    def layout(self):
        """内容较少时收拢窗口；长页面保留固定标题与滚动区域。"""
        height = min(canvas_size[1]-80, max(240, self.cursor+148))
        self.rect = pygame.Rect((canvas_size[0]-1040)//2, (canvas_size[1]-height)//2, 1040, height)
        self.body = pygame.Rect(self.rect.x+32, self.rect.y+92, 976, height-124)
        self.close.rect.topleft = (self.rect.right-96, self.rect.y+26)
        if self.back:
            self.back.rect.topleft = (self.close.rect.x-108, self.rect.y+26)
        self.scroll = min(self.scroll, self.max_scroll)

    @property
    def max_scroll(self):
        return max(0, self.cursor-self.body.height+16)

    def gap(self, amount=12):
        self.cursor += amount

    def text(self, text, color=TEXT, size=18):
        size = round(size*self.fonts.content_scale)
        for line in self.fonts.wrap(text, self.body.width-26, size):
            self.blocks.append((self.cursor, "text", (line, color, size)))
            self.cursor += max(size+9, self.fonts.get(size).get_linesize()+3)

    def heading(self, text):
        self.gap(20 if self.cursor else 2)
        size = round(20*self.fonts.content_scale)
        for line in self.fonts.wrap(text, self.body.width-38, size):
            self.blocks.append((self.cursor, 'heading', (line, size)))
            self.cursor += max(size+10, self.fonts.get(size).get_linesize()+3)
        self.gap(8)

    def stats(self, items):
        """等宽数据卡片，每项为（名称、数值、提示、颜色）。"""
        def draw(surface, rect):
            width = (rect.width-12*(len(items)-1))//len(items)
            for index, (label, value, hint, color) in enumerate(items):
                card = pygame.Rect(rect.x+index*(width+12), rect.y, width, rect.height)
                pygame.draw.rect(surface, (238, 242, 232), card, border_radius=12)
                self.fonts.text(surface, label, (card.x+16, card.y+10), 14, MUTED)
                self.fonts.text(surface, value, (card.x+16, card.y+32), 25, color)
                self.fonts.text(surface, hint, (card.x+16, card.y+66), 13, MUTED)
        self.custom(94, draw)

    def notice(self, text, color=GREEN):
        lines = self.fonts.wrap(text, self.body.width-64, 16)
        def draw(surface, rect):
            pygame.draw.rect(surface, (237, 241, 229), rect, border_radius=9)
            pygame.draw.rect(surface, color, (rect.x, rect.y+10, 3, rect.height-20), border_radius=1)
            for index, line in enumerate(lines):
                self.fonts.text(surface, line, (rect.x+16, rect.y+12+index*24), 16, color)
        self.custom(len(lines)*24+24, draw)

    def custom(self, height, draw_fn):
        """预留一块自定义绘制区域；draw_fn(surface, rect) 在裁剪区内调用。"""
        self.blocks.append((self.cursor, "custom", (height, draw_fn)))
        self.cursor += height+10

    def row(self, specs):
        if self.is_side and len(specs)>2:
            for start in range(0, len(specs), 2):
                self.row(specs[start:start+2])
            return
        count = len(specs)
        width = (self.body.width-24-(count-1)*10)//count
        for i, spec in enumerate(specs):
            label, callback, *rest = spec
            enabled = rest[0] if rest else True
            tone = rest[1] if len(rest)>1 else None
            variant = rest[2] if len(rest)>2 else 'secondary'
            self.buttons.append(Button((i*(width+10), self.cursor, width, 42), label, callback, enabled, tone, variant))
            self.buttons[-1].font_size = round(16*self.fonts.content_scale)
        self.cursor += 54

    def bar(self, label, value):
        self.blocks.append((self.cursor, "bar", ProgressBar((0, self.cursor, min(730,self.body.width-24), 29), label, value)))
        self.cursor += 33

    def choices(self,labels,current,image_fn,callback,columns=8):
        width=(self.body.w-24-(columns-1)*8)//columns
        for i,label in enumerate(labels):
            row,col=divmod(i,columns)
            self.buttons.append(ChoiceCard((col*(width+8),self.cursor+row*112,width,104),label,
                                          image_fn(i),lambda value=i:callback(value),i==current))
        self.cursor+=((len(labels)+columns-1)//columns)*112

    def input(self, value=""):
        box = TextBox((0, self.cursor, 700, 48), value)
        self.inputs.append(box)
        self.cursor += 62
        return box

    def offset(self):
        return self.body.x, self.body.y-self.scroll

    def handle(self, event):
        if event.type == pygame.MOUSEWHEEL and self.body.collidepoint(pointer()):
            self.scroll = max(0, min(self.max_scroll, self.scroll-event.y*42))
        if event.type == pygame.KEYDOWN:
            if event.key == pygame.K_ESCAPE:
                self.close.callback()
                return
            if event.key == pygame.K_PAGEDOWN:
                self.scroll = min(self.max_scroll, self.scroll+400)
            elif event.key == pygame.K_PAGEUP:
                self.scroll = max(0, self.scroll-400)
        for box in self.inputs:
            box.handle(event, self.offset())
        if event.type == pygame.MOUSEBUTTONDOWN and event.button == 1:
            if self.close.click(event.pos) or (self.back and self.back.click(event.pos)):
                return
            if self.body.collidepoint(event.pos):
                for button in list(self.buttons):
                    if button.click(event.pos, self.offset()):
                        break

    def draw(self, surface):
        if not self.is_side:
            surface.blit(art.shade_surface(surface.get_size(), (28, 36, 28, 150)), (0, 0))
        art.draw_shadow(surface, (self.rect.centerx, self.rect.bottom+4),
                        (self.rect.width-40, 46), 90, 12)
        surface.blit(art.rounded_gradient(self.rect.size, PANEL, (250, 243, 226), 18), self.rect.topleft)
        pygame.draw.rect(surface, LINE, self.rect, 1, border_radius=18)
        pygame.draw.line(surface, CREAM, (self.rect.x+16, self.rect.y+2),
                         (self.rect.right-16, self.rect.y+2), 2)
        self.fonts.text(surface, self.title, (self.rect.x+32, self.rect.y+25), 25, GREEN)
        pygame.draw.line(surface, (225, 219, 205), (self.rect.x+32, self.rect.y+76),
                         (self.rect.right-32, self.rect.y+76))
        self.close.draw(surface, self.fonts)
        if self.back:
            self.back.draw(surface, self.fonts)
        old_clip = surface.get_clip()
        surface.set_clip(self.body)
        ox, oy = self.offset()
        for y, kind, data in self.blocks:
            # 商店卡片/档案立绘可能超过45像素，部分滚出屏幕时仍需绘制可见部分。
            block_height = data[0] if kind == "custom" else 45
            if -block_height <= y-self.scroll <= self.body.height:
                if kind == "text":
                    text, color, size = data
                    self.fonts.text(surface, text, (ox, oy+y), size, color)
                elif kind == 'heading':
                    text, size = data
                    self.fonts.text(surface, text, (ox+12, oy+y), size, GREEN)
                    pygame.draw.rect(surface, GREEN_LIGHT, (ox, oy+y+5, 3, 18), border_radius=1)
                elif kind == "custom":
                    height, draw_fn = data
                    draw_fn(surface, pygame.Rect(ox+13, oy+y, self.body.width-26, height))
                else:
                    data.draw(surface, self.fonts, (ox, oy))
        for button in self.buttons:
            if -button.rect.h <= button.rect.y-self.scroll <= self.body.height:
                button.draw(surface, self.fonts, (ox, oy))
        for box in self.inputs:
            box.draw(surface, self.fonts, (ox, oy))
        surface.set_clip(old_clip)
        if self.max_scroll:
            track = pygame.Rect(self.body.right-7, self.body.top, 5, self.body.height)
            pygame.draw.rect(surface, PANEL_DEEP, track, border_radius=2)
            height = max(25, int(track.height*track.height/(self.cursor+16)))
            thumb = pygame.Rect(track.x, track.y+int((track.height-height)*self.scroll/self.max_scroll), 5, height)
            pygame.draw.rect(surface, GREEN, thumb, border_radius=2)
            self.fonts.text(surface, '滚轮 / PgUp · PgDn 翻页', (self.rect.x+32, self.rect.bottom-25), 12, MUTED)


class SidePanel(Modal):
    """详情面板只接管自身区域，场景和底栏继续可操作。"""
    def __init__(self, title, fonts, on_close, on_back=None):
        super().__init__(title, fonts, on_close, on_back)
        self.is_side = True
        self.layout()

    def layout(self):
        self.rect = pygame.Rect(canvas_size[0]-544, 100, 528, canvas_size[1]-268)
        self.body = pygame.Rect(self.rect.x+24, 184, 480, self.rect.h-118)
        self.close.rect.topleft = (self.rect.right-88, self.rect.y+24)
        if self.back:
            self.back.rect.topleft = (self.close.rect.x-108, self.rect.y+24)
        self.scroll = min(self.scroll, self.max_scroll)
