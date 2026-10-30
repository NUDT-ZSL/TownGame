"""装饰的非模态编辑工具，预览不修改模型。"""
import pygame
import art
import ui
from decorations import CATALOG,placement_reason
from town_grid import world_to_cell,cell_center
from tree_art import tree_sprite,profile

_SPRITES=art.LRU(24)


def sprite(kind,orientation=0,season='春',light='day',identity='preview'):
    if kind.startswith('tree_'):
        species,variant,height=profile(identity,kind[5:])
        return tree_sprite(species,variant,season,light,height,orientation)
    def build():
        s=pygame.Surface((80,96),pygame.SRCALPHA)
        art.draw_shadow(s,(40,86),(58,14),35,4)
        if kind=='flowers':
            art.clay_panel(s,(12,63,56,22),(169,130,93),7,shadow_alpha=0)
            pygame.draw.ellipse(s,(111,156,102),(12,50,56,24))
            for i,(x,y) in enumerate(((23,57),(38,49),(55,58),(40,65))):
                for dx,dy in ((-4,0),(4,0),(0,-4),(0,4)):
                    pygame.draw.circle(s,((229,158,148),(236,204,128))[i%2],(x+dx,y+dy),4)
                pygame.draw.circle(s,(247,227,156),(x,y),2)
        elif kind=='lamp':
            pygame.draw.line(s,(87,108,98),(40,30),(40,84),6)
            pygame.draw.ellipse(s,(87,108,98),(29,78,22,9))
            art.draw_glow(s,(40,26),23,(252,225,143),65)
            pygame.draw.rect(s,(251,225,151),(28,14,24,26),border_radius=6)
            pygame.draw.polygon(s,(82,110,101),[(24,15),(40,6),(56,15)])
        elif kind=='bench':
            for x in (20,59):pygame.draw.line(s,(91,108,96),(x,60),(x,85),5)
            for y in (45,54,67):pygame.draw.rect(s,(178,128,88),(12,y,56,7),border_radius=3)
        elif kind=='fence':
            for x in (13,40,67):
                pygame.draw.rect(s,(211,185,135),(x-4,48,8,38),border_radius=3)
                pygame.draw.polygon(s,(230,207,158),[(x-4,49),(x,42),(x+4,49)])
            for y in (59,74):pygame.draw.line(s,(219,194,148),(10,y),(70,y),5)
        else:
            pygame.draw.line(s,(150,119,83),(40,30),(40,85),6)
            pygame.draw.polygon(s,(205,171,120),[(16,30),(55,30),(65,40),(55,50),(16,50)])
            pygame.draw.line(s,(249,237,204),(27,40),(48,40),3)
        if orientation>=2:s=pygame.transform.flip(s,True,False)
        if orientation%2:
            image=pygame.transform.smoothscale(s,(46,96))
            s=pygame.Surface((80,96),pygame.SRCALPHA);s.blit(image,(9,0))
        return s
    return _SPRITES.get((kind,orientation),build)


class DecorationController:
    def __init__(self,app):
        self.app=app;self.active=False;self.kind='flowers';self.orientation=0;self.moving=None;self.pan=None;self.category='objects'

    def enter(self):
        self.app.close_modal();self.app.builder.exit();self.app.pan=None;self.app.drag=None;self.active=True
        self.app.notify('装饰可放在庄园外围草地和已开放街区；点击已有装饰可移动，R旋转，Ctrl+Z撤销。')

    def panel(self):
        return pygame.Rect(self.app.screen.get_width()-320,160,296,452)

    def set_category(self,category):
        self.category=category
        self.choose(next(key for key in CATALOG if key.startswith('tree_')==(category=='trees')))

    def choose(self,kind):
        self.kind=kind;self.moving=None

    def buttons(self):
        r=self.panel()
        buttons=[ui.Button((r.x+12+i*140,r.y+44,132,30),label,lambda c=category:self.set_category(c),
                           variant='primary' if self.category==category else 'secondary')
                 for i,(category,label) in enumerate((('objects','庭院装饰'),('trees','树木种类')))]
        rows=[(key,value) for key,value in CATALOG.items() if key.startswith('tree_')==(self.category=='trees')]
        for i,(key,(name,cost)) in enumerate(rows):
            image=sprite(key,0,self.app.game.season,self.app.courtyard.light)
            image=pygame.transform.smoothscale(image,(round(58*image.get_width()/image.get_height()),58))
            buttons.append(ui.ChoiceCard((r.x+12+i%2*140,r.y+84+i//2*92,132,86),f'{name} {cost}金币',image,
                                         lambda k=key:self.choose(k),self.kind==key))
        buttons += [ui.Button((r.x+12,r.y+366,272,32),'撤销 Ctrl+Z',lambda:self.app.act('decoration',operation='undo'),variant='secondary'),
                    ui.Button((r.x+12,r.y+408,272,32),'退出装饰 D',self.exit,variant='secondary')]
        return buttons

    def exit(self):self.active=False;self.moving=None;self.pan=None

    def handle(self,event):
        app=self.app;view=app.courtyard
        if event.type==pygame.KEYDOWN:
            if event.key==pygame.K_ESCAPE:
                if self.moving:self.moving=None
                else:self.exit()
            elif event.key==pygame.K_r:self.orientation=(self.orientation+1)%4
            elif event.key==pygame.K_z and getattr(event,'mod',0)&pygame.KMOD_CTRL:app.act('decoration',operation='undo')
            elif event.key==pygame.K_DELETE and self.moving:
                if app.act('decoration',operation='remove',decoration_id=self.moving):self.moving=None
            return
        pos=getattr(event,'pos',ui.pointer())
        if event.type==pygame.MOUSEWHEEL and view.viewport.collidepoint(pos) and not self.panel().collidepoint(pos):
            view.camera.zoom_at(pos,1.12**event.y)
        elif event.type==pygame.MOUSEBUTTONDOWN:
            if event.button==1:
                for button in self.buttons():
                    if button.click(pos):return
            if not view.viewport.collidepoint(pos) or self.panel().collidepoint(pos):return
            if event.button in (2,3):self.pan=pos;return
            if event.button!=1:return
            if view.fixed_click(pos):return
            cx,cy=world_to_cell(*view.camera.screen_to_world(pos))
            existing=next((d for d in app.game.meta.get('decorations',[]) if (d['cx'],d['cy'])==(cx,cy)),None)
            if existing is None and not self.moving:
                ident=view.decoration_at(pos)
                existing=next((d for d in app.game.meta.get('decorations',[]) if d['id']==ident),None)
            if existing and not self.moving:
                self.moving=existing['id'];self.kind=existing['kind'];self.orientation=existing['orientation']
                self.category='trees' if self.kind.startswith('tree_') else 'objects'
                app.notify('已选中装饰：点击新位置移动，R旋转后点击放下，Delete拆除，Esc取消。')
            elif app.act('decoration',operation='move' if self.moving else 'place',kind=self.kind,cx=cx,cy=cy,
                         orientation=self.orientation,decoration_id=self.moving):self.moving=None
        elif event.type==pygame.MOUSEMOTION and self.pan:
            view.camera.pan(pos[0]-self.pan[0],pos[1]-self.pan[1]);self.pan=pos
        elif event.type==pygame.MOUSEBUTTONUP:self.pan=None

    def draw(self,surface):
        app=self.app;view=app.courtyard;pos=ui.pointer();reason=''
        if view.viewport.collidepoint(pos) and not self.panel().collidepoint(pos):
            cx,cy=world_to_cell(*view.camera.screen_to_world(pos));reason=placement_reason(app.game,cx,cy,self.moving)
            if not self.moving and CATALOG[self.kind][1]>app.game.meta['coins']:reason='金币不足'
            rect=view.project_rect((cx*48,cy*48,48,48))
            old=surface.get_clip();surface.set_clip(view.viewport)
            if getattr(view,'is_3d',False):
                view.wire_preview(surface,(cx*48,cy*48,48,48),120 if self.kind.startswith('tree_') else 90 if self.kind=='lamp' else 40,
                                  ui.RED if reason else ui.GREEN)
            else:
                image=view.scaled(sprite(self.kind,self.orientation,app.game.season,view.light,self.moving or 'preview')).copy();image.set_alpha(150)
                anchor=view.camera.world_to_screen(((cx+.5)*48,(cy+1)*48))
                surface.blit(image,image.get_rect(midbottom=anchor))
                pygame.draw.rect(surface,ui.RED if reason else ui.GREEN,rect,2)
            surface.set_clip(old)
        r=self.panel();art.clay_panel(surface,r,ui.PANEL,12,ui.LINE)
        app.fonts.text(surface,'自由装饰 · 移动 / 旋转',(r.x+14,r.y+12),18,ui.GREEN)
        for button in self.buttons():button.draw(surface,app.fonts)
        tips=pygame.Rect(16,app.screen.get_height()-210,1000,50)
        pygame.draw.rect(surface,ui.PANEL,tips,border_radius=8)
        app.fonts.text(surface,('不能放置：'+reason) if reason else ('选择新位置，点击放下' if self.moving else '点击草地摆放；点击已有装饰可移动'),
                       (tips.x+12,tips.y+4),16,ui.RED if reason else ui.GREEN)
        app.fonts.text(surface,'R旋转 · Delete拆除 · Ctrl+Z撤销 · ' + ('中键移图 · 右键转视角' if getattr(view,'is_3d',False) else '中键/右键移图') + ' · Esc取消',(tips.x+12,tips.y+27),14,ui.MUTED)
