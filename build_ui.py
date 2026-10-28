"""非模态建设工具：预览不落盘，鼠标释放才通过ManorApp.act提交。"""
import pygame
import art
import ui
import town_art
import town_catalog as K
from town_grid import world_to_cell, cell_rect, cell_center, iter_visible_cells, line_cells, zone_for, RoadGrid

TOOLS=('道路','住宅','商业','产业','公共','拆除')


class BuildController:
    def __init__(self,app):
        self.app=app; self.active=False; self.tool=0; self.style='asphalt'; self.family='classic'
        self.selected=None; self.orientation=None; self.stroke=None; self.pan=None; self.pointer=(650,390)
        self.grid_lines=True; self.land_overlay=False; self.scroll=0; self.catalog_hits=[]
        self.panel=pygame.Rect(956,160,300,335)
        self.tool_buttons=[ui.Button((16+i*125,716,117,35),f'{i+1} {name}',lambda n=i:self.select_tool(n)) for i,name in enumerate(TOOLS)]
        self.exit_button=ui.Button((1133,716,120,35),'退出建设 B',self.exit)
        self.toggles=[ui.Button((793,716,100,35),'格线',self.toggle_grid),ui.Button((902,716,100,35),'地价',self.toggle_land),
                      ui.Button((1010,716,113,35),'撤销 Ctrl+Z',lambda:self.app.act('undo_roads'))]
        self.guide_button=ui.Button((1070,812,175,31),'收起建设指引',lambda:self.app.act('dismiss_build_guide'))

    def toggle_grid(self): self.grid_lines=not self.grid_lines
    def toggle_land(self): self.land_overlay=not self.land_overlay

    def enter(self):
        self.app.close_modal(); self.app.menu_open=False; self.app.pan=None; self.app.drag=None
        self.active=True; self.stroke=None; self.pan=None
        self.app.notify('建设：左键铺路/放置，右键或中键移图，滚轮缩放，Esc取消。')

    def exit(self):
        self.active=False; self.stroke=None; self.pan=None; self.app.pan=None

    def toggle(self):
        self.exit() if self.active else self.enter()

    def select_tool(self,n):
        self.tool=n; self.stroke=None; self.scroll=0; self.orientation=None
        if n in (1,2,3,4):
            candidates=[k for k,s in K.CATALOG.items() if s['category']==TOOLS[n]]
            if not candidates: return
            self.selected=candidates[0]; self.family=K.CATALOG[self.selected]['family']
        else:
            self.selected=None

    def cell(self,pos=None):
        return world_to_cell(*self.app.courtyard.camera.screen_to_world(pos or self.pointer))

    def rows(self):
        if self.tool==0: return list(K.ROAD_STYLES)
        return [k for k,s in K.CATALOG.items() if s['category']==TOOLS[self.tool] and s['family']==self.family] if self.tool in (1,2,3,4) else []

    def _choose(self,key):
        if self.tool==0: self.style=key
        else: self.selected=key; self.orientation=None

    def handle(self,event):
        if event.type==getattr(pygame,'WINDOWFOCUSLOST',-1):
            self.stroke=self.pan=None; return True
        if event.type==pygame.KEYDOWN:
            if event.key==pygame.K_ESCAPE:
                if self.stroke: self.stroke=None
                elif self.selected: self.selected=None
                else: self.exit()
            elif pygame.K_1<=event.key<=pygame.K_6: self.select_tool(event.key-pygame.K_1)
            elif event.key==pygame.K_DELETE: self.select_tool(5)
            elif event.key==pygame.K_r:
                if self.selected:
                    x,y=self.cell(); current=self.app.game.construction.building_preview(self.selected,x,y,self.orientation)['building']['orientation']
                    self.orientation=(current+1)%4
            elif event.key==pygame.K_z and getattr(event,'mod',pygame.key.get_mods())&pygame.KMOD_CTRL: self.app.act('undo_roads')
            elif event.key==pygame.K_HOME: self.app.courtyard.home()
            elif event.key==pygame.K_F1: self.app.open('help')
            return True
        if event.type==pygame.MOUSEMOTION:
            self.pointer=event.pos
            if self.pan:
                self.app.courtyard.camera.pan(event.pos[0]-self.pan[0],event.pos[1]-self.pan[1]); self.pan=event.pos
            elif self.stroke and self.app.courtyard.camera.vy<=event.pos[1]<704 and not self.panel.collidepoint(event.pos):
                self.stroke+=line_cells(self.stroke[-1],self.cell(event.pos))[1:]
            return True
        if event.type==pygame.MOUSEWHEEL:
            pos=pygame.mouse.get_pos()
            if self.panel.collidepoint(pos): self.scroll=max(0,min(max(0,len(self.rows())-3),self.scroll-event.y))
            elif 88<=pos[1]<704: self.app.courtyard.camera.zoom_at(pos,1.12**event.y)
            return True
        if event.type==pygame.MOUSEBUTTONDOWN:
            self.pointer=event.pos
            if event.button in (2,3) and 88<=event.pos[1]<704:
                self.stroke=None; self.pan=event.pos
                if event.button==3: self.selected=None
                return True
            if event.button!=1: return True
            if event.pos[1]<88: self.app.hud.handle(event.pos); return True
            for button in self.tool_buttons+self.toggles+[self.exit_button]:
                if button.click(event.pos): return True
            if not self.app.game.meta['build_guide_dismissed'] and self.guide_button.click(event.pos): return True
            if self.panel.collidepoint(event.pos):
                if event.pos[1]<225 and self.tool in (1,2,3,4):
                    index=max(0,min(4,(event.pos[0]-self.panel.x-10)//56))
                    self.family=list(K.FAMILIES)[index]; self.scroll=0
                    self.selected=next(iter(self.rows()),None)
                else:
                    for rect,key in self.catalog_hits:
                        if rect.collidepoint(event.pos): self._choose(key); break
                return True
            if not 88<=event.pos[1]<704: return True
            if self.app.courtyard.fixed_click(event.pos): return True
            if self.tool in (0,5):
                if self.tool==5:
                    cx,cy=self.cell(event.pos)
                    ident=next((b['building_id'] for b in self.app.game.town_buildings if b['cx']<=cx<b['cx']+b['w'] and b['cy']<=cy<b['cy']+b['h']),None)
                    if ident:
                        self.app._confirm('拆除这栋玩家建筑？施工中返还15%，完工返还30%。','remove_town_building',building_id=ident)
                        return True
                self.stroke=[self.cell(event.pos)]
            elif self.selected:
                x,y=self.cell(event.pos)
                self.app.act('place_town_building',catalog_key=self.selected,cx=x,cy=y,orientation=self.orientation)
            else:
                self.app.notify('请先在右侧目录选择建筑')
            return True
        if event.type==pygame.MOUSEBUTTONUP:
            if event.button in (2,3): self.pan=None
            elif event.button==1 and self.stroke is not None:
                cells=self.stroke; self.stroke=None
                self.app.act('remove_roads' if self.tool==5 else 'build_roads',cells=cells,**({'style':self.style} if self.tool==0 else {}))
            return True
        return True

    def _preview(self):
        game=self.app.game
        if self.tool in (0,5):
            p=game.construction.road_preview(self.stroke or [self.cell()],self.style,self.tool==5)
            text=f"本次{p['total']}格 / 可{'拆除' if self.tool==5 else '铺设'}{len(p['valid'])}格 / 跳过{len(p['invalid'])}格 / {'返还' if p['cost']<0 else '费用'}{abs(p['cost'])}金币"
            reason=p['invalid'][0][1] if p['invalid'] else '合法，松手后整笔提交'
            if p['cost']>game.meta['coins']:
                reason=f"资金不足，还差{p['cost']-game.meta['coins']}金币"
            p['affordable']=p['cost']<=game.meta['coins']
            return p,text,reason
        if self.selected:
            x,y=self.cell(); p=game.construction.building_preview(self.selected,x,y,self.orientation)
            if not p['reason'] and p['cost']>game.meta['coins']:
                p['reason']=f"资金不足，还差{p['cost']-game.meta['coins']}金币"
            return p,f"{K.CATALOG[self.selected]['name']} / {p['building']['w']}×{p['building']['h']}格 / {p['cost']}金币 / 朝向{('北','东','南','西')[p['building']['orientation']]}",p['reason'] or '临街合法，单击开始施工'
        return None,'请选择建筑','右键可取消幽灵；Esc退出建设'

    def draw(self,surface):
        g=self.app.game; view=self.app.courtyard; f=self.app.fonts
        overlay=pygame.Surface((1280,616),pygame.SRCALPHA)
        if self.grid_lines or self.land_overlay:
            for c in iter_visible_cells(view.camera):
                r=view.project_rect(cell_rect(*c)).move(0,-88)
                if self.land_overlay:
                    score=g.construction.land_value(c)
                    color=((195,118,92),(217,171,110),(221,207,146),(167,195,125),(112,177,130))[max(0,min(4,(score+4)//3))]
                    pygame.draw.rect(overlay,(*color,75),r)
                if self.grid_lines: pygame.draw.rect(overlay,(247,248,223,63),r,1)
        p,text,reason=self._preview()
        if p:
            if 'valid' in p:
                union=dict(g.construction.grid.roads)
                for cell in p['valid']: union[cell]=dict(cx=cell[0],cy=cell[1],road_style=self.style)
                preview_grid=RoadGrid(list(union.values()))
                for cell in p['valid']:
                    rect=view.project_rect(cell_rect(*cell)).move(0,-88)
                    if self.tool==0:
                        tile=town_art.road_tile(self.style,preview_grid.mask(cell),g.season,view.light,g.weather.current)
                        overlay.blit(pygame.transform.scale(tile,rect.size),rect)
                    color=(83,183,117,180) if p['affordable'] else (211,90,77,180)
                    pygame.draw.rect(overlay,color if self.tool==0 else (223,133,89,140),rect,2 if self.tool==0 else 0)
                for cell,_ in p['invalid']: pygame.draw.rect(overlay,(207,88,82,95),view.project_rect(cell_rect(*cell)).move(0,-88))
            else:
                for cell in p['cells']:
                    pygame.draw.rect(overlay,(203,92,79,110) if p['reason'] else (88,182,120,110),view.project_rect(cell_rect(*cell)).move(0,-88))
                for cell in p['frontage']:
                    pygame.draw.rect(overlay,(255,229,149,170),view.project_rect(cell_rect(*cell)).move(0,-88),3)
                b=p['building']; sprite=town_art.building_sprite(b['catalog_key'],b['orientation'],view.light,g.season,g.weather.current)
                sprite=pygame.transform.smoothscale(sprite,(round(sprite.get_width()*view.camera.zoom),round(sprite.get_height()*view.camera.zoom))).copy(); sprite.set_alpha(130)
                anchor=view.camera.world_to_screen(((b['cx']+b['w']/2)*48,(b['cy']+b['h'])*48+8))
                overlay.blit(sprite,sprite.get_rect(midbottom=(anchor[0],anchor[1]-88)))
                cx,cy=view.camera.world_to_screen(cell_center(b['cx'],b['cy']))
                dx,dy=((0,-1),(1,0),(0,1),(-1,0))[b['orientation']]
                pygame.draw.line(overlay,(251,227,145),(cx,cy-88),(cx+dx*25,cy-88+dy*25),4)
        for fixed in (view.legend_rect,view.home_button,view.overview_rect):
            overlay.fill((0,0,0,0),fixed.move(0,-88))
        surface.blit(overlay,(0,88))
        art.clay_panel(surface,(0,704,1280,156),ui.PANEL,0,ui.LINE)
        for i,button in enumerate(self.tool_buttons):
            button.tone=ui.GREEN if i==self.tool else None; button.draw(surface,f)
        for button in self.toggles+[self.exit_button]: button.draw(surface,f)
        f.text(surface,text,(20,762),18,ui.GREEN)
        f.text(surface,reason,(20,793),16,ui.RED if p and (p.get('reason') or not p.get('valid',True) or not p.get('affordable',True)) else ui.MUTED)
        zone=zone_for(self.cell())
        status='道路通达' if not zone or g.construction.connected(zone) else '道路未连通'
        f.text(surface,f"金币 {g.meta['coins']} · 人口 {len(g.people)} · {status} · R旋转 / 右键中键移图 / 滚轮缩放",(20,829),13,ui.MUTED)
        if not g.meta['build_guide_dismissed']:
            f.text(surface,'①拖动铺路 → ②临街建住宅 → ③等待施工并观察车流',(525,796),14,ui.GOLD)
            self.guide_button.draw(surface,f)
        art.clay_panel(surface,self.panel,ui.PANEL,14,ui.LINE,shadow_alpha=25)
        f.text(surface,'建设目录 · '+TOOLS[self.tool],(970,170),19,ui.GREEN)
        if self.tool in (1,2,3,4):
            for i,(key,name) in enumerate(K.FAMILIES.items()):
                r=pygame.Rect(966+i*56,202,52,25)
                pygame.draw.rect(surface,(213,227,195) if self.family==key else ui.PANEL_LIGHT,r,border_radius=5)
                f.text(surface,name,(r.x+9,r.y+4),13,ui.GREEN)
        self.catalog_hits=[]
        rows=self.rows()
        for i,key in enumerate(rows[self.scroll:self.scroll+3]):
            rect=pygame.Rect(968,236+i*76,276,68); self.catalog_hits.append((rect,key))
            chosen=key==(self.style if self.tool==0 else self.selected)
            art.clay_panel(surface,rect,(233,239,216) if chosen else ui.PANEL_LIGHT,8,ui.GREEN if chosen else ui.LINE,shadow_alpha=0)
            if self.tool==0:
                tile=town_art.road_tile(key,5,g.season,view.light,g.weather.current); surface.blit(tile,(rect.x+5,rect.y+8))
                spec=K.ROAD_STYLES[key]; f.text(surface,spec['name'],(rect.x+61,rect.y+7),17,ui.GREEN)
                f.text(surface,f"{spec['cost']}金币/格 · 自动拼接",(rect.x+61,rect.y+35),13,ui.MUTED)
            else:
                spec=K.CATALOG[key]; image=town_art.building_sprite(key,0,'day',g.season,'晴天')
                image=pygame.transform.smoothscale(image,(48,58)); surface.blit(image,(rect.x+4,rect.y+4))
                f.text(surface,spec['name'],(rect.x+58,rect.y+5),16,ui.GREEN)
                f.text(surface,f"{spec['footprint'][0]}×{spec['footprint'][1]}格 · {spec['cost']}金币 · {spec['build_hours']}游戏小时",(rect.x+58,rect.y+28),11,ui.MUTED)
                f.text(surface,f"人口+{spec['population']} · 吸引{spec['attraction']:+g} · 日税{spec['tax']}",(rect.x+58,rect.y+47),11,ui.MUTED)
        if not rows: f.text(surface,'拖动擦除道路；点击玩家建筑拆除。' if self.tool==5 else '此分类暂无该风格，请切换页签。',(970,250),13,ui.MUTED)
        f.text(surface,'目录内滚轮翻页 · 相同路面不重复收费',(970,474),12,ui.MUTED)
