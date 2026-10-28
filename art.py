"""程序化伪 3D 绘制工具：渐变、软阴影、黏土面板、图标与缓存。

本模块不访问 Game/SQLite，也不加载任何外部图片，所有素材都由 pygame 图元生成。
高开销的形状（渐变、阴影、图标）按参数缓存，供庭院、HUD 与弹窗共用。
"""
from collections import OrderedDict
import math
import pygame


class LRU:
    """简单的 LRU 缓存；factory 只在未命中时执行。"""

    def __init__(self, maxsize=128):
        self.maxsize = maxsize
        self.data = OrderedDict()

    def get(self, key, factory):
        try:
            value = self.data.pop(key)
        except KeyError:
            value = factory()
            if len(self.data) >= self.maxsize:
                self.data.popitem(last=False)
        self.data[key] = value
        return value

    def clear(self):
        self.data.clear()


_GRADIENTS = LRU(128)
_SHADOWS = LRU(96)
_GLOWS = LRU(64)
_ICONS = LRU(256)
_COTTAGES = LRU(64)
_SHADES = LRU(4)
_MISC = LRU(128)


def clamp(value, low=0.0, high=1.0):
    return max(low, min(high, value))


def mix(first, second, t):
    """在两个颜色之间插值；颜色可以是 RGB 或 RGBA。"""
    t = clamp(t)
    if len(first) < len(second):
        first = tuple(first)+ (255,)*(len(second)-len(first))
    if len(second) < len(first):
        second = tuple(second)+ (255,)*(len(first)-len(second))
    return tuple(int(round(a+(b-a)*t)) for a, b in zip(first, second))


def lighten(color, amount=.18):
    return mix(color, (255, 255, 255, 255)[:len(color)], amount)


def darken(color, amount=.18):
    return mix(color, (0, 0, 0, 255)[:len(color)], amount)


def with_alpha(color, alpha):
    return (color[0], color[1], color[2], int(alpha))


def vertical_gradient(size, top, bottom):
    size = (max(1, int(size[0])), max(1, int(size[1])))
    key = ("v", size, tuple(top), tuple(bottom))
    def build():
        w, h = size
        alpha = (len(top) > 3 and top[3] < 255) or (len(bottom) > 3 and bottom[3] < 255)
        surface = pygame.Surface(size, pygame.SRCALPHA) if alpha else pygame.Surface(size)
        for y in range(h):
            pygame.draw.line(surface, mix(top, bottom, y/max(1, h-1)), (0, y), (w-1, y))
        return surface
    return _GRADIENTS.get(key, build)


def horizontal_gradient(size, left, right):
    size = (max(1, int(size[0])), max(1, int(size[1])))
    key = ("h", size, tuple(left), tuple(right))
    def build():
        w, h = size
        alpha = (len(left) > 3 and left[3] < 255) or (len(right) > 3 and right[3] < 255)
        surface = pygame.Surface(size, pygame.SRCALPHA) if alpha else pygame.Surface(size)
        for x in range(w):
            pygame.draw.line(surface, mix(left, right, x/max(1, w-1)), (x, 0), (x, h-1))
        return surface
    return _GRADIENTS.get(key, build)


def radial_gradient(radius, inner, outer, steps=20):
    radius = max(1, int(radius))
    key = ("r", radius, tuple(inner), tuple(outer), steps)
    def build():
        surface = pygame.Surface((radius*2, radius*2), pygame.SRCALPHA)
        for i in range(steps):
            t = i/max(1, steps-1)
            color = mix(outer, inner, t)
            r = int(radius*(1-t*0.98)) or 1
            pygame.draw.circle(surface, color, (radius, radius), r)
        return surface
    return _GRADIENTS.get(key, build)


def rounded_gradient(size, top, bottom, radius=12):
    size = (max(1, int(size[0])), max(1, int(size[1])))
    radius = int(min(radius, size[0]//2, size[1]//2))
    key = ("rg", size, tuple(top), tuple(bottom), radius)
    def build():
        surface = vertical_gradient(size, top, bottom).convert_alpha()
        mask = pygame.Surface(size, pygame.SRCALPHA)
        pygame.draw.rect(mask, (255, 255, 255, 255), mask.get_rect(), border_radius=radius)
        surface.blit(mask, (0, 0), special_flags=pygame.BLEND_RGBA_MULT)
        return surface
    return _GRADIENTS.get(key, build)


def shadow_surface(size, alpha=80, spread=8, color=(38, 30, 22)):
    """返回一个四周渐隐的椭圆阴影；坐标原点即最外层。"""
    w, h = max(2, int(size[0])), max(2, int(size[1]))
    key = ("shadow", (w, h), int(alpha), int(spread), tuple(color))
    def build():
        surface = pygame.Surface((w+spread*2, h+spread*2), pygame.SRCALPHA)
        layers = 7
        for i in range(layers):
            t = i/(layers-1)
            rw = int(w+spread*2*(1-t))
            rh = int(h+spread*2*(1-t))
            a = int(alpha*(0.10+0.90*t))
            pygame.draw.ellipse(surface, with_alpha(color, a),
                                (surface.get_width()//2-rw//2, surface.get_height()//2-rh//2, rw, rh))
        return surface
    return _SHADOWS.get(key, build)


def draw_shadow(surface, center, size, alpha=80, spread=8):
    shadow = shadow_surface(size, alpha, spread)
    surface.blit(shadow, shadow.get_rect(center=center))


def glow_surface(radius, color, alpha=90):
    key = ("glow", int(radius), tuple(color), int(alpha))
    def build():
        return radial_gradient(radius, with_alpha(color, alpha), with_alpha(color, 0))
    return _GLOWS.get(key, build)


def draw_glow(surface, center, radius, color, alpha=90):
    glow = glow_surface(radius, color, alpha)
    surface.blit(glow, glow.get_rect(center=center))


def shade_surface(size, color):
    key = ("shade", tuple(size), tuple(color))
    def build():
        surface = pygame.Surface(size, pygame.SRCALPHA)
        surface.fill(color)
        return surface
    return _SHADES.get(key, build)


def clay_panel(surface, rect, fill, radius=12, border=None, shadow_alpha=45,
               top=None, bottom=None, lift=3):
    """奶油/黏土质感圆角面板：柔和投影 + 渐变填充 + 细描边。"""
    rect = pygame.Rect(rect)
    if shadow_alpha:
        draw_shadow(surface, (rect.centerx, rect.bottom-2),
                    (max(8, rect.width-10), max(6, rect.height//3)), shadow_alpha, 5)
    top = fill if top is None else top
    bottom = darken(fill, .10) if bottom is None else bottom
    surface.blit(rounded_gradient(rect.size, top, bottom, radius), rect.topleft)
    if border:
        pygame.draw.rect(surface, border, rect, 1, border_radius=radius)
    return rect


def clay_blob(surface, rect, fill, rim=True, shine=70):
    """带亮面、暗面和轮廓的黏土色块，用于宠物/道具的实体感。"""
    rect = pygame.Rect(rect)
    base = rounded_gradient(rect.size, lighten(fill, .12), darken(fill, .14),
                            min(rect.width, rect.height)//2)
    mask = pygame.Surface(rect.size, pygame.SRCALPHA)
    pygame.draw.ellipse(mask, (255, 255, 255, 255), mask.get_rect())
    base.blit(mask, (0, 0), special_flags=pygame.BLEND_RGBA_MULT)
    surface.blit(base, rect.topleft)
    pygame.draw.ellipse(surface, darken(fill, .35), rect, 1)
    if rim:
        highlight = pygame.Rect(rect.x+rect.width*.18, rect.y+rect.height*.12,
                                rect.width*.38, rect.height*.22)
        pygame.draw.ellipse(surface, with_alpha((255, 255, 255), shine), highlight)


def _polygon_surface(size, points, color, ss=3):
    w, h = int(size[0]), int(size[1])
    surface = pygame.Surface((w*ss, h*ss), pygame.SRCALPHA)
    prepared = [(w*ss/2+x*ss, h*ss/2+y*ss) for x, y in points]
    pygame.draw.polygon(surface, color, prepared)
    return pygame.transform.smoothscale(surface, (w, h))


def heart_surface(size, color):
    key = ("heart", tuple(size), tuple(color))
    def build():
        raw = []
        for i in range(64):
            t = 2*math.pi*i/64
            raw.append((16*math.sin(t)**3,
                        -(13*math.cos(t)-5*math.cos(2*t)-2*math.cos(3*t)-math.cos(4*t))))
        xs = [p[0] for p in raw]
        ys = [p[1] for p in raw]
        scale = min(size[0]*.94/max(1e-6, max(xs)-min(xs)), size[1]*.94/max(1e-6, max(ys)-min(ys)))
        cx, cy = (max(xs)+min(xs))/2, (max(ys)+min(ys))/2
        points = [((x-cx)*scale, (y-cy)*scale) for x, y in raw]
        return _polygon_surface(size, points, color)
    return _MISC.get(key, build)


def draw_heart(surface, center, size, color):
    heart = heart_surface((int(size), int(size)), color)
    surface.blit(heart, heart.get_rect(center=center))


def star_points(cx, cy, outer, inner, count=5, rotation=-math.pi/2):
    points = []
    for i in range(count*2):
        radius = outer if i % 2 == 0 else inner
        angle = rotation+math.pi*i/count
        points.append((cx+math.cos(angle)*radius, cy+math.sin(angle)*radius))
    return points


def draw_star(surface, center, size, color):
    pygame.draw.polygon(surface, color, star_points(center[0], center[1], size, size*.45))


def draw_sparkle(surface, center, size, color):
    x, y = center
    pygame.draw.polygon(surface, color, [(x, y-size), (x+size*.28, y-size*.28),
                                         (x+size, y), (x+size*.28, y+size*.28),
                                         (x, y+size), (x-size*.28, y+size*.28),
                                         (x-size, y), (x-size*.28, y-size*.28)])


def coin_surface(radius, color=(233, 178, 74), rim=(176, 116, 38)):
    key = ("coin", int(radius), tuple(color), tuple(rim))
    def build():
        ss = 3
        size = radius*2
        surface = pygame.Surface((size*ss, size*ss), pygame.SRCALPHA)
        c = size*ss//2
        pygame.draw.circle(surface, rim, (c, c), radius*ss)
        pygame.draw.circle(surface, color, (c, c), int(radius*ss*.88))
        pygame.draw.circle(surface, lighten(color, .25), (int(c-radius*.25*ss), int(c-radius*.3*ss)),
                           int(radius*.34*ss))
        pygame.draw.polygon(surface, rim, [(c, c-int(radius*.5*ss)),
                                           (c+int(radius*.42*ss), c),
                                           (c, c+int(radius*.5*ss)),
                                           (c-int(radius*.42*ss), c)])
        return pygame.transform.smoothscale(surface, (size, size))
    return _ICONS.get(key, build)


def draw_coin(surface, center, radius):
    coin = coin_surface(radius)
    surface.blit(coin, coin.get_rect(center=center))


def draw_item_icon(surface, item, rect, alpha=None):
    """用几何图形绘制物品图标；rect 建议接近正方形。"""
    rect = pygame.Rect(rect)
    icon = item_icon_surface(item, rect.size)
    if alpha is not None:
        icon = icon.copy()
        icon.set_alpha(alpha)
    surface.blit(icon, rect.topleft)


def item_icon_surface(item, size):
    w, h = max(8, int(size[0])), max(8, int(size[1]))
    key = ("item", item, (w, h))
    def build():
        ss = 2
        surface = pygame.Surface((w*ss, h*ss), pygame.SRCALPHA)
        def ellipse(color, cx, cy, rw, rh):
            pygame.draw.ellipse(surface, color, (int(cx-rw/2), int(cy-rh/2), int(rw), int(rh)))
        def circle(color, cx, cy, r):
            pygame.draw.circle(surface, color, (int(cx), int(cy)), int(r))
        cx, cy = w*ss/2, h*ss/2
        unit = min(w, h)*ss
        if item in ("普通饲料", "高级饲料"):
            ellipse((150, 108, 66), cx, cy+unit*.20, unit*.78, unit*.34)
            ellipse((186, 140, 92), cx, cy+unit*.13, unit*.76, unit*.28)
            for dx, dy in ((-.18, -.02), (0, -.08), (.18, -.02), (-.08, .04), (.10, .05)):
                circle((214, 170, 110) if item == "高级饲料" else (122, 84, 50),
                       cx+unit*dx, cy+unit*(dy-.05), unit*.065)
            if item == "高级饲料":
                draw_star(surface, (cx+unit*.20, cy-unit*.22), unit*.13, (246, 206, 110))
        elif item == "鲜草":
            for dx, lean in ((-.20, -.10), (0, 0), (.20, .10)):
                pygame.draw.polygon(surface, (100, 158, 82), [
                    (cx+unit*dx-unit*.05, cy+unit*.32),
                    (cx+unit*(dx+lean), cy-unit*.32),
                    (cx+unit*dx+unit*.05, cy+unit*.32)])
            ellipse((128, 178, 98), cx, cy+unit*.33, unit*.5, unit*.14)
        elif item in ("零食", "特制零食"):
            circle((206, 160, 98), cx, cy, unit*.36)
            circle((228, 190, 134), cx-unit*.10, cy-unit*.12, unit*.16)
            for dx, dy in ((-.16, .08), (.10, -.14), (.16, .14), (-.06, -.20)):
                circle((126, 90, 56), cx+unit*dx, cy+unit*dy, unit*.045)
            if item == "特制零食":
                draw_heart(surface, (cx, cy+unit*.04), unit*.20, (206, 104, 118))
        elif item == "腐败物":
            ellipse((92, 104, 62), cx, cy+unit*.06, unit*.62, unit*.50)
            for dx, dy, r in ((-.14, -.12, .10), (.10, -.16, .08), (.16, .08, .07), (-.18, .10, .06)):
                circle((128, 138, 82), cx+unit*dx, cy+unit*dy, unit*r)
            for dx in (-.18, .02, .20):
                pygame.draw.arc(surface, (150, 160, 110),
                                (cx+unit*dx-unit*.10, cy-unit*.52, unit*.20, unit*.30),
                                0.3, 2.4, max(1, int(unit*.02)))
        elif item == "清水":
            pygame.draw.polygon(surface, (108, 176, 214), [
                (cx, cy-unit*.42), (cx+unit*.28, cy+unit*.08),
                (cx, cy+unit*.34), (cx-unit*.28, cy+unit*.08)])
            circle((150, 202, 230), cx, cy+unit*.10, unit*.20)
        elif item == "沐浴露":
            ellipse((176, 214, 206), cx, cy+unit*.16, unit*.44, unit*.42)
            pygame.draw.rect(surface, (122, 166, 170),
                             (int(cx-unit*.16), int(cy-unit*.36), int(unit*.32), int(unit*.26)),
                             border_radius=int(unit*.07))
            pygame.draw.rect(surface, (88, 124, 128),
                             (int(cx-unit*.10), int(cy-unit*.48), int(unit*.20), int(unit*.14)),
                             border_radius=int(unit*.04))
            circle((255, 255, 255), cx-unit*.10, cy+unit*.10, unit*.10)
        elif item == "药品":
            pygame.draw.rect(surface, (242, 238, 226),
                             (int(cx-unit*.36), int(cy-unit*.22), int(unit*.72), int(unit*.44)),
                             border_radius=int(unit*.20))
            pygame.draw.rect(surface, (190, 84, 78),
                             (int(cx-unit*.07), int(cy-unit*.16), int(unit*.14), int(unit*.32)),
                             border_radius=int(unit*.03))
            pygame.draw.rect(surface, (190, 84, 78),
                             (int(cx-unit*.16), int(cy-unit*.07), int(unit*.32), int(unit*.14)),
                             border_radius=int(unit*.03))
        elif item == "谷物":
            pygame.draw.line(surface, (150, 118, 58), (cx, cy+unit*.42), (cx, cy-unit*.34),
                             max(1, int(unit*.05)))
            for i in range(4):
                y = cy-unit*.28+i*unit*.15
                for sign in (-1, 1):
                    ellipse((212, 178, 92), cx+sign*unit*.12, y, unit*.16, unit*.09)
        elif item in ("兔毛", "绒毛"):
            for dx, dy, r in ((-.14, .02, .20), (0, -.10, .24), (.14, .02, .20), (0, .10, .20)):
                circle((246, 240, 228), cx+unit*dx, cy+unit*dy, unit*r)
        elif item == "羊奶":
            pygame.draw.rect(surface, (244, 246, 240),
                             (int(cx-unit*.22), int(cy-unit*.18), int(unit*.44), int(unit*.50)),
                             border_radius=int(unit*.08))
            pygame.draw.rect(surface, (160, 196, 222),
                             (int(cx-unit*.13), int(cy-unit*.40), int(unit*.26), int(unit*.22)),
                             border_radius=int(unit*.05))
            draw_heart(surface, (cx, cy+unit*.08), unit*.16, (128, 172, 210))
        elif item == "鸟蛋":
            ellipse((246, 238, 214), cx, cy+unit*.03, unit*.44, unit*.58)
            ellipse((255, 252, 242), cx-unit*.08, cy-unit*.08, unit*.16, unit*.20)
        else:
            ellipse((196, 164, 118), cx, cy, unit*.60, unit*.52)
            pygame.draw.arc(surface, (140, 108, 70),
                            (cx-unit*.20, cy-unit*.10, unit*.40, unit*.30), 0.2, 2.9, 2)
        return pygame.transform.smoothscale(surface, (w, h))
    return _ICONS.get(key, build)


def cottage_surface(size, kind, level=1, base_color=(195, 158, 119), winter=False, night=False):
    """按建筑类型绘制伪 3D 小屋。"""
    w, h = max(24, int(size[0])), max(20, int(size[1]))
    key = ("cottage", (w, h), kind, level, tuple(base_color), winter, night)
    def build():
        ss = 2
        surface = pygame.Surface((w*ss, h*ss), pygame.SRCALPHA)
        W, H = w*ss, h*ss
        # 地面阴影
        pygame.draw.ellipse(surface, (52, 40, 26, 60),
                            (int(W*.06), int(H*.82), int(W*.88), int(H*.16)))
        wall = mix(base_color, (255, 250, 238), .55)
        wall_dark = darken(wall, .14)
        roof = darken(base_color, .10)
        wall_rect = pygame.Rect(int(W*.16), int(H*.44), int(W*.68), int(H*.42))
        pygame.draw.rect(surface, wall, wall_rect, border_radius=int(W*.06))
        pygame.draw.rect(surface, wall_dark, (wall_rect.right-int(W*.16), wall_rect.y,
                                              int(W*.16), wall_rect.height),
                         border_radius=int(W*.06))
        # 屋顶
        if kind in ("鸟舍", "寄养屋"):
            pygame.draw.ellipse(surface, roof, (int(W*.12), int(H*.26), int(W*.76), int(H*.44)))
        elif kind in ("仓库", "加工坊"):
            pygame.draw.polygon(surface, roof, [(int(W*.10), int(H*.46)), (int(W*.90), int(H*.46)),
                                                (int(W*.86), int(H*.30)), (int(W*.14), int(H*.30))])
        else:
            pygame.draw.polygon(surface, roof, [(int(W*.08), int(H*.48)), (int(W*.50), int(H*.12)),
                                                (int(W*.92), int(H*.48))])
        pygame.draw.polygon(surface, lighten(roof, .10),
                            [(int(W*.16), int(H*.44)), (int(W*.50), int(H*.16)), (int(W*.50), int(H*.44))])
        if winter:
            pygame.draw.polygon(surface, (246, 248, 250),
                                [(int(W*.18), int(H*.42)), (int(W*.50), int(H*.18)),
                                 (int(W*.82), int(H*.42)), (int(W*.50), int(H*.30))])
        # 门窗
        door = pygame.Rect(int(W*.43), int(H*.62), int(W*.16), int(H*.24))
        pygame.draw.rect(surface, darken(wall, .32), door, border_radius=int(W*.03))
        window_color = (255, 214, 126) if night else (196, 226, 228)
        for cx in (.28, .70):
            pygame.draw.rect(surface, window_color,
                             (int(W*cx-W*.06), int(H*.56), int(W*.12), int(H*.12)),
                             border_radius=int(W*.02))
            pygame.draw.rect(surface, darken(wall, .24),
                             (int(W*cx-W*.06), int(H*.56), int(W*.12), int(H*.12)), 2,
                             border_radius=int(W*.02))
        # 类型标记
        if kind == "宠物医院":
            pygame.draw.rect(surface, (190, 84, 78),
                             (int(W*.16), int(H*.30), int(W*.12), int(H*.04)))
            pygame.draw.rect(surface, (190, 84, 78),
                             (int(W*.20), int(H*.26), int(W*.04), int(H*.12)))
        elif kind == "商店":
            for i in range(3):
                pygame.draw.rect(surface, (214, 120, 92) if i % 2 == 0 else (246, 238, 222),
                                 (int(W*(.18+i*.22)), int(H*.50), int(W*.18), int(H*.09)))
        elif kind == "游乐区":
            pygame.draw.line(surface, (150, 116, 74), (int(W*.78), int(H*.78)),
                             (int(W*.92), int(H*.46)), 3)
            pygame.draw.circle(surface, (214, 164, 92), (int(W*.92), int(H*.46)), int(W*.06))
        elif kind == "加工坊":
            pygame.draw.rect(surface, darken(wall, .26),
                             (int(W*.66), int(H*.10), int(W*.10), int(H*.20)))
        elif kind == "寄养屋":
            draw_heart(surface, (W*.50, H*.34), W*.16, (214, 120, 118))
        # 等级小灯
        for i in range(min(5, max(1, int(level)))):
            pygame.draw.circle(surface, (244, 208, 122),
                               (int(W*(.24+i*.13)), int(H*.94)), max(1, int(W*.018)))
        return pygame.transform.smoothscale(surface, (w, h))
    return _COTTAGES.get(key, build)


def draw_cottage(surface, rect, kind, level, base_color, winter=False, night=False):
    rect = pygame.Rect(rect)
    surface.blit(cottage_surface(rect.size, kind, level, base_color, winter, night), rect.topleft)


def draw_drop(surface, center, size, color):
    x, y = center
    pygame.draw.polygon(surface, color, [(x, y-size), (x+size*.62, y+size*.10),
                                         (x, y+size), (x-size*.62, y+size*.10)])
    pygame.draw.circle(surface, lighten(color, .25), (x, y+size*.25), max(1, int(size*.34)))


def draw_zzz(surface, center, size, color):
    font = pygame.font.Font(None, max(10, int(size)))
    for i in range(3):
        glyph = font.render("Z", True, color)
        glyph = pygame.transform.smoothscale(glyph, (max(6, int(size*.5*(1+i*.35))),)*2)
        surface.blit(glyph, (center[0]+i*int(size*.30), center[1]-i*int(size*.42)))


def ring(surface, center, radius, color, width=3):
    pygame.draw.circle(surface, color, (int(center[0]), int(center[1])), int(radius), width)
