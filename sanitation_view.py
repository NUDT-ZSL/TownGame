"""地面粪便和可见农场管理员。绘制只读取模型，不执行收集、扣费或随机判病。"""
import math
import time
import pygame
import art
import ui
import game_config as C


class SanitationView:
    def __init__(self, fonts, bounds):
        self.fonts,self.bounds = fonts,bounds
        self.time = 0
        self.waste_hits = []
        self.manager_rect = None
        self.sign_rect = pygame.Rect(24,605,172,76)
        self.last_seen_cleaned = None
        self.completion_until = 0
        self.pile_sprite=None

    def tick(self, dt):
        self.time += dt

    def point(self, u, v):
        return self.bounds.left+u*self.bounds.width, self.bounds.top+v*self.bounds.height

    def waste_at(self, pos):
        for rect,ident in reversed(self.waste_hits):
            if rect.collidepoint(pos):
                return ident
        return None

    def hoverable(self, pos):
        return self.sign_rect.collidepoint(pos) or bool(self.waste_at(pos)) or bool(self.manager_rect and self.manager_rect.collidepoint(pos))

    def draw_ground(self, surface, game):
        if self.pile_sprite is None:
            sprite=pygame.Surface((44,44),pygame.SRCALPHA); x,y=21,31
            art.draw_shadow(sprite,(x,y+2),(30,10),55,3)
            art.clay_blob(sprite,pygame.Rect(x-13,y-11,27,14),(135,92,54),shine=24)
            art.clay_blob(sprite,pygame.Rect(x-9,y-18,19,12),(151,107,65),shine=24)
            art.clay_blob(sprite,pygame.Rect(x-4,y-24,9,10),(165,119,75),shine=24)
            self.pile_sprite=sprite
        self.waste_hits = []
        piles = sorted(game.waste,key=lambda w:(w["created_ts"],w["waste_id"]))[:C.MAX_DRAWN_WASTE]
        mouse = pygame.mouse.get_pos()
        for pile in piles:
            x,y = self.point(pile["u"],pile["v"])
            x,y = int(x),int(y)
            rect = pygame.Rect(x-17,y-25,34,32)
            self.waste_hits.append((rect,pile["waste_id"]))
            stale = game.now-pile["created_ts"] >= C.WASTE_UNSAFE_AFTER
            if rect.collidepoint(mouse):
                pygame.draw.ellipse(surface,ui.GOLD,(x-19,y-9,38,17),2)
            surface.blit(self.pile_sprite,(x-21,y-31))
            if stale:
                for i in range(2):
                    sx = x-5+i*11
                    sy = y-31-math.sin(self.time*2+i)*3
                    pygame.draw.lines(surface,(112,132,70),False,[(sx,int(sy)),(sx+3,int(sy-5)),(sx,int(sy-10))],2)

    def manager_pose(self, game):
        meta = game.meta
        cleaned = meta["manager_cleaned"]
        if self.last_seen_cleaned is not None and cleaned > self.last_seen_cleaned:
            self.completion_until = self.time+2.0
        self.last_seen_cleaned = cleaned
        if not meta["farm_manager"]:
            self.manager_rect = None
            return None
        job = meta["manager_job"]
        if job:
            # 在两次逻辑刷新间插值，让走动平滑；资源只在游戏服务完成工单后变化。
            extra = max(0,time.time()-meta["real_anchor"])*(C.SPEED if meta["mode"] == "fast" else 1)
            now = min(job["end_ts"],game.now+extra)
            ratio = max(0,min(1,(now-job["start_ts"])/(job["sweep_ts"]-job["start_ts"])))
            u = job["from_u"]+(job["u"]-job["from_u"])*ratio
            v = job["from_v"]+(job["v"]-job["from_v"])*ratio
            state = "前往清扫" if now < job["sweep_ts"] else "清扫中"
        else:
            u = max(.03,min(.97,meta["manager_u"]+math.sin(self.time*.35)*.035))
            v = max(.10,min(.92,meta["manager_v"]+math.cos(self.time*.35)*.04))
            state = "清扫完成" if self.time < self.completion_until else game.sanitation.status()
        x,y = self.point(u,v)
        # 脚步停在目标左侧，让扫帚刷毛覆盖粪便，而不是站在粪便上。
        x -= 34
        self.manager_rect = pygame.Rect(int(x-88),int(y-120),176,128)
        return x,y,state

    def draw_manager(self, surface, pose, actors=()):
        if pose is None:
            return
        x,y,state = pose
        x,y = int(x),int(y)
        walking = state in ("前往清扫","巡查")
        leg = math.sin(self.time*7)*5 if walking else 0
        sweeping = state in ("清扫中","清扫完成")
        sweep = math.sin(self.time*9)*12 if sweeping else math.sin(self.time*3)*4
        art.draw_shadow(surface,(x,y+3),(43,13),55,4)
        # 靴子、工作服和围裙。
        for sign in (-1,1):
            foot = int(x+sign*9+leg*sign)
            pygame.draw.line(surface,(69,95,88),(x+sign*8,y-25),(foot,y-4),9)
            pygame.draw.ellipse(surface,(94,76,56),(foot-6,y-8,16,10))
        art.clay_blob(surface,pygame.Rect(x-17,y-57,35,36),(111,164,151),shine=28)
        pygame.draw.rect(surface,(226,208,155),(x-10,y-47,21,24),border_radius=5)
        pygame.draw.line(surface,(214,190,137),(x-7,y-54),(x-7,y-33),3)
        pygame.draw.line(surface,(214,190,137),(x+8,y-54),(x+8,y-33),3)
        pygame.draw.line(surface,(228,191,153),(x-13,y-46),(x-22,y-31),8)
        hand = (x+21,y-33)
        pygame.draw.line(surface,(228,191,153),(x+13,y-45),hand,8)
        # 草帽、脸、五官与棕色发沿。
        art.clay_blob(surface,pygame.Rect(x-14,y-78,29,27),(242,209,169),shine=24)
        pygame.draw.arc(surface,(105,78,52),(x-14,y-81,29,19),0,math.pi,5)
        pygame.draw.ellipse(surface,(197,164,98),(x-25,y-81,51,10))
        art.clay_blob(surface,pygame.Rect(x-17,y-94,34,18),(226,199,133),shine=28)
        pygame.draw.line(surface,(132,105,66),(x-16,y-81),(x+16,y-81),4)
        pygame.draw.circle(surface,(69,59,46),(x-5,y-66),2)
        pygame.draw.circle(surface,(69,59,46),(x+6,y-66),2)
        pygame.draw.arc(surface,(165,105,85),(x-4,y-64,10,8),math.pi,2*math.pi,2)
        # 扫帚与清扫中的尘粒；动作不修改粪便数据。
        end = (int(x+37+sweep),y-1)
        pygame.draw.line(surface,(126,92,55),hand,end,4)
        pygame.draw.polygon(surface,(205,175,105),[(end[0]-4,end[1]-13),(end[0]+4,end[1]-13),(end[0]+13,end[1]+3),(end[0]-12,end[1]+3)])
        for i in range(5):
            pygame.draw.line(surface,(154,121,67),(end[0],end[1]-10),(end[0]-10+i*5,end[1]+2),1)
        if sweeping:
            for i in range(5):
                px = end[0]-15+(i*11+int(self.time*16))%40
                py = y-6-((i*7+int(self.time*20))%15)
                pygame.draw.circle(surface,(216,194,143),(px,py),2)
        text = f"管理员{C.MANAGER_NAME} · {state}"
        font = self.fonts.get(13)
        width = font.size(text)[0]+16
        tag = pygame.Rect(x-width//2,y-117,width,22)
        # 避开当前选中/悬停宠物的姓名条，保持管理员身份与状态可读。
        occupied = []
        for actor in actors:
            if actor.selected or actor.hovered:
                label = f"{actor.name} · {actor.stage}"+(" · 已选中" if actor.selected else "")
                rect = pygame.Rect(0,0,self.fonts.get(14).size(label)[0]+22,26)
                rect.midbottom = (int(actor.x),int(actor.y-actor.drawn_height-4))
                occupied.append(rect.inflate(8,8))
        for _ in range(4):
            if not any(tag.colliderect(rect) for rect in occupied):
                break
            tag.y -= 28
        self.manager_rect = self.manager_rect.union(tag)
        art.clay_panel(surface,tag,(255,249,228),7,ui.GREEN,shadow_alpha=15)
        self.fonts.text(surface,text,(tag.x+8,tag.y+3),13,ui.GREEN)

    def draw_sign(self, surface, game):
        r = self.sign_rect
        art.clay_panel(surface,r,(255,249,228),10,ui.GREEN,shadow_alpha=24)
        self.fonts.text(surface,"农场卫生 / 点击管理",(r.x+10,r.y+8),15,ui.GREEN)
        self.fonts.text(surface,f"地面粪便 {game.sanitation.quantity()} 份",(r.x+10,r.y+33),15,ui.TEXT)
        label = game.sanitation.status()
        if len(label) > 12:
            label = label[:11]+"…"
        self.fonts.text(surface,"管理员："+label,(r.x+10,r.y+55),12,ui.MUTED)
