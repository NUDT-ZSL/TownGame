"""无窗口覆盖全部弹窗、庭院点击/拖拽、庄园地图、中文输入与 RNG 纯净性，并生成截图。"""
import os
import hashlib
import sys
import tempfile
import time
from contextlib import ExitStack
from unittest.mock import patch
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT))
import main  # 注册项目内可选的 .runtime 依赖目录。
os.environ["SDL_VIDEODRIVER"] = "dummy"
os.environ["SDL_AUDIODRIVER"] = "dummy"
import pygame
from db import Database, validate
from game import Game
from interface import ManorApp
from pet import Pet
from building import Building
import pet_art
import town_view
import event


def run():
    output = ROOT/".test-output"
    output.mkdir(exist_ok=True)
    with tempfile.TemporaryDirectory(dir=output) as folder, ExitStack() as cleanup:
        g = Game(Database(Path(folder)/"ui.db"), real_now=time.time(), seed=42)
        cleanup.callback(g.db.close)
        g.meta["coins"] = 10000
        import game_config as C
        for i, kind in enumerate(C.BUILDINGS):
            g.buildings[i] = Building(i, True, kind, 2)
        for species, bid in (("绵羊",1), ("鹦鹉",2), ("狐狸",0)):
            p = Pet.create(species,g.now,bid,g.rng,rare=species == "鹦鹉")
            g.pets[p.pet_id] = p
        # 最初建筑重新排布后，初始兔子仍住0号饲养小屋。
        p = g.living[0]
        p.birth_ts -= 9*C.DAY
        p.hunger, p.favor, p.status = 32, 60, "生病"
        p.last_stage = "成年"
        g.events.append(event.create_event(g.now, 50, g.rng))
        g.save()
        app = ManorApp(g)
        cleanup.callback(pygame.quit)
        # UI 动画/绘制不得消耗游戏随机数，否则会破坏存档确定性。
        rng_state = g.rng.getstate()
        app.draw()
        assert g.rng.getstate() == rng_state, "UI 绘制消耗了 game.rng"
        pygame.image.save(app.screen, str(output/"main.png"))
        pages = [(key,{}) for key in ("pets","shop","inventory","craft","orders","events","codex","achievements","memorial","logs","help","today","sanitation","town","map")]
        pages += [("zone",{"zone_id":key}) for key in C.TOWN_ZONES]
        pages += [(key,{"pet_id":p.pet_id}) for key in ("pet","feed","move","breed","rename")]
        pages += [("building", {"building_id":i}) for i in range(20)]
        pages += [("event",{"event_id":g.events[-1]["event_id"]}),
                  ("buy_pet",{"species":"猫咪"}), ("buy_pet",{"species":"小鸭","egg":True}),
                  ("medical",{"pet_id":p.pet_id}),
                  ("order_pet",{"order_id":next(o["order_id"] for o in g.orders if o["kind"] == "pet")}),
                  ("confirm",{"text":"确认测试", "action":"mode", "params":{}}),
                  ("message",{"title":"测试消息", "text":"中文文本正常显示。"})]
        for key,args in pages:
            app.close_modal()
            app.open(key,args)
            app.draw()
            if key == "pet":
                pygame.image.save(app.screen, str(output/"pet.png"))
            app.modal.scroll = app.modal.max_scroll
            app.draw()
        # 购买目录中每一种新伙伴都必须拥有完整动画，不能只增加配置却无法绘制。
        new_species = [s for s,spec in C.SPECIES.items() if spec.get("purchase_only", False)]
        sheet = pygame.Surface((1000, 480))
        sheet.fill((250, 243, 226))
        signatures = set()
        frames_checked = 0
        for i,species in enumerate(new_species):
            for stage in ("幼崽","少年","成年","老年"):
                for pose,count in pet_art.POSE_FRAMES.items():
                    for rare,sick in ((False,False),(True,False),(False,True)):
                        sprite = app.renderer.sprite(species, stage, rare, sick, pose, count-1, "day", 128)
                        assert pygame.mask.from_surface(sprite).count() > 100, f"{species}/{pose}没有图像"
                        frames_checked += 1
            for pose,count in pet_art.POSE_FRAMES.items():
                for frame in range(count):
                    app.renderer.sprite(species,"成年",False,False,pose,frame,"night",128)
                    frames_checked += 1
            portrait = app.renderer.portrait(species,"成年",height=154)
            signatures.add(hashlib.sha256(pygame.image.tostring(portrait,"RGBA")).digest())
            x,y = (i%4)*250, (i//4)*240
            sheet.blit(portrait,portrait.get_rect(midbottom=(x+125,y+165)))
            app.fonts.text(sheet,f"{species} · {g.pet_buy_price(species)}金币",(x+35,y+175),19,(75,98,65))
            app.fonts.text(sheet,C.SPECIES[species]["building"],(x+88,y+207),15,(105,112,96))
        assert len(signatures) == len(new_species), "新品种应拥有不同外观"
        pygame.image.save(sheet,str(output/"new-pets.png"))
        app.close_modal()
        app.open("shop")
        for scroll in range(0,app.modal.max_scroll+1,280):
            app.modal.scroll = scroll
            app.draw()
        app.modal.scroll = 0
        app.draw()
        pygame.image.save(app.screen,str(output/"shop.png"))
        assert g.rng.getstate() == rng_state, "目录和动画绘制消耗了 game.rng"
        # 真实按钮输入必须创建付费新品种，滚动后命中坐标也要正确。
        before_ids = set(g.pets)
        buy_button = next(b for b in app.modal.buttons if b.label.startswith("购买猫咪幼崽"))
        assert buy_button.enabled
        app.modal.scroll = max(0,min(app.modal.max_scroll,buy_button.rect.y-140))
        app.handle(pygame.event.Event(pygame.MOUSEBUTTONDOWN,button=1,pos=buy_button.rect.move(app.modal.offset()).center))
        assert app.page[0] == "buy_pet", "商店未打开性别选择"
        app.draw()
        pygame.image.save(app.screen,str(output/"sex-selection.png"))
        female_button = next(b for b in app.modal.buttons if b.label == "购买雌性（母）")
        app.handle(pygame.event.Event(pygame.MOUSEBUTTONDOWN,button=1,pos=female_button.rect.move(app.modal.offset()).center))
        assert any(g.pets[pid].species == "猫咪" for pid in set(g.pets)-before_ids), "商店购买新品种失败"
        assert all(g.pets[pid].sex == "雌" for pid in set(g.pets)-before_ids), "未按选择购买雌性"
        rng_state = g.rng.getstate()  # 实际购买会正常消耗随机数；只验证之后的纯绘制。
        # 庄园地图：独立覆盖弹窗，点击地块进入建筑页，返回地图，再截图。
        app.close_modal()
        app.open("manor_map")
        app.draw()
        pygame.image.save(app.screen, str(output/"manor-map.png"))
        mrect, bid = app.modal.hotspots[4]
        app.handle(pygame.event.Event(pygame.MOUSEBUTTONDOWN, button=1, pos=mrect.center))
        assert app.page == ("building", {"building_id":bid}), "地图地块点击失败"
        app.back()
        assert app.page[0] == "manor_map", "地图返回失败"
        app.close_modal()
        app.draw()
        assert g.rng.getstate() == rng_state, "UI 绘制消耗了 game.rng"
        # 单击宠物：焦点切换并抚摸；快速再点打开档案；右键也可打开档案。
        app.focus_pet_id = None
        app._sync_actors()
        # 排开站位，避免随机散步位置重叠后点到前景的另一位伙伴。
        for i, actor in enumerate(app.actor_order):
            actor.x, actor.y = 320+i*170, 600
        app.draw()
        rect, pid = app.pet_hits[0]
        app._last_pet_click = None
        before = g.pets[pid].happy
        app.handle(pygame.event.Event(pygame.MOUSEBUTTONDOWN, button=1, pos=rect.center))
        app.handle(pygame.event.Event(pygame.MOUSEBUTTONUP, button=1, pos=rect.center))
        assert app.focus_pet_id == pid, "点击宠物未切换焦点"
        assert app.page is None, "单击宠物不应直接打开档案"
        assert g.pets[pid].happy >= before, "抚摸互动失败"
        app.handle(pygame.event.Event(pygame.MOUSEBUTTONDOWN, button=1, pos=rect.center))
        app.handle(pygame.event.Event(pygame.MOUSEBUTTONUP, button=1, pos=rect.center))
        assert app.page == ("pet", {"pet_id":pid}), "双击宠物未打开档案"
        app.close_modal()
        app.handle(pygame.event.Event(pygame.MOUSEBUTTONDOWN, button=3, pos=rect.center))
        assert app.page == ("pet", {"pet_id":pid}), "右键宠物未打开档案"
        app.close_modal()
        # 拖拽喂食：从底栏取下食物，松手落在伙伴身上，库存减一。
        item = app.bottom.food_slots[0][1]
        if g.inventory.count(item) == 0:
            g.inventory.add(item, 3, g.now, max(g.storage_capacity, g.inventory.used+3))
        before = g.inventory.count(item)
        slot = app.bottom.food_slots[0][0]
        actor = app.actor_order[0]
        app.handle(pygame.event.Event(pygame.MOUSEBUTTONDOWN, button=1, pos=slot.center))
        assert app.drag and app.drag["item"] == item, "食物拖拽未开始"
        app.handle(pygame.event.Event(pygame.MOUSEBUTTONUP, button=1,
                                      pos=(int(actor.x), int(actor.y-20))))
        assert app.drag is None, "拖拽状态未清空"
        assert g.inventory.count(item) == before-1, "拖拽喂食未消耗库存"
        # Esc 取消拖拽并复位光标状态。
        app.handle(pygame.event.Event(pygame.MOUSEBUTTONDOWN, button=1, pos=slot.center))
        app.handle(pygame.event.Event(pygame.KEYDOWN, key=pygame.K_ESCAPE))
        assert app.drag is None, "Esc 未取消拖拽"
        app.open("rename", {"pet_id":p.pet_id})
        box = app.modal.inputs[0]
        box.text = ""
        app.handle(pygame.event.Event(pygame.TEXTINPUT, text="新团子"))
        assert box.text == "新团子", "中文输入失败"
        button = app.modal.buttons[0]
        app.handle(pygame.event.Event(pygame.MOUSEBUTTONDOWN, button=1, pos=button.rect.move(app.modal.offset()).center))
        assert g.pets[p.pet_id].name == "新团子", "保存中文名字失败"
        app.close_modal()
        old_mode = g.meta["mode"]
        app.handle(pygame.event.Event(pygame.MOUSEBUTTONDOWN, button=1, pos=app.mode_button.rect.center))
        assert g.meta["mode"] != old_mode, "切换时间按钮失败"
        # 住院/治疗走真实按钮路径，原伤势外观在档案与底栏均可见。
        p.illness_type, p.status, p.injury = "", "受伤", 25
        g.save()
        app.close_modal()
        app.open("medical",{"pet_id":p.pet_id})
        admit = next(b for b in app.modal.buttons if b.label == "送入宠物医院")
        app.handle(pygame.event.Event(pygame.MOUSEBUTTONDOWN,button=1,pos=admit.rect.move(app.modal.offset()).center))
        assert g.buildings[p.building_id].kind == "宠物医院", "就医按钮未送入住院"
        cure = next(b for b in app.modal.buttons if b.label == "接受治疗")
        with patch.dict(C.INJURY_TREATMENT,{"cure":1}):
            app.handle(pygame.event.Event(pygame.MOUSEBUTTONDOWN,button=1,pos=cure.rect.move(app.modal.offset()).center))
        assert not p.unwell, "医院治疗按钮未清除伤势"
        g.perform("move",pet_id=p.pet_id,building_id=0)
        other = next(q for q in g.living if q.species == p.species and q.sex != p.sex)
        for q in (p,other):
            q.birth_ts = g.now-q.lifespan_max*.3
            q.last_stage,q.favor,q.illness_budget = "成年",60,1e9
        g.save()
        app.close_modal()
        app.open("breed",{"pet_id":p.pet_id})
        pairing = next(b for b in app.modal.buttons if b.label == "结为伴侣")
        app.handle(pygame.event.Event(pygame.MOUSEBUTTONDOWN,button=1,pos=pairing.rect.move(app.modal.offset()).center))
        assert p.mate_id == other.pet_id and other.mate_id == p.pet_id, "伴侣按钮未建立双向关系"
        with patch.object(C,"CONCEPTION_CHANCE",1):
            app.act("breed",first_id=p.pet_id,second_id=other.pet_id)
        mother = p if p.sex == "雌" else other
        assert mother.pregnant, "异性同居未进入孕期"
        app.close_modal()
        app.open("pet",{"pet_id":mother.pet_id})
        app.draw()
        assert any(kind == "text" and "伴侣：" in data[0] for _,kind,data in app.modal.blocks)
        pygame.image.save(app.screen,str(output/"relationship.png"))
        mate_button = next(b for b in app.modal.buttons if b.label == "查看伴侣档案")
        app.handle(pygame.event.Event(pygame.MOUSEBUTTONDOWN,button=1,pos=mate_button.rect.move(app.modal.offset()).center))
        assert app.page == ("pet",{"pet_id":mother.mate_id}), "伴侣档案链接失效"
        mother.contract_illness(g.now,g.rng,"绝症")
        app.close_modal()
        app.open("medical",{"pet_id":mother.pet_id})
        app.draw()
        pygame.image.save(app.screen,str(output/"medical.png"))
        # 正常推进至排泄时刻，点击卫生告示牌雇佣管理员，再观察走动/挥扫帚/入库。
        app.close_modal()
        if g.meta["mode"] == "fast":
            g.perform("mode")
        with patch.multiple(C,EVENT_CHANCE=0,FIGHT_CHANCE=0,FIGHT_HUNGER_BONUS=0,FIGHT_UNHAPPY_BONUS=0,FIGHT_CROWD_BONUS=0):
            g.advance(g.meta["real_anchor"]+C.EXCRETION_INTERVAL)
        assert g.sanitation.quantity() > 0, "宠物没有排泄到地面"
        app.draw()
        app.handle(pygame.event.Event(pygame.MOUSEBUTTONDOWN,button=1,pos=app.courtyard.sanitation.sign_rect.center))
        app.handle(pygame.event.Event(pygame.MOUSEBUTTONUP,button=1,pos=app.courtyard.sanitation.sign_rect.center))
        assert app.page[0] == "sanitation", "庭院卫生告示牌无法打开"
        hire = next(b for b in app.modal.buttons if b.label.startswith("雇佣农场管理员"))
        app.handle(pygame.event.Event(pygame.MOUSEBUTTONDOWN,button=1,pos=hire.rect.move(app.modal.offset()).center))
        assert g.meta["farm_manager"] and g.meta["manager_job"], "管理员招聘按钮失败"
        job = dict(g.meta["manager_job"])
        app.close_modal()
        app.draw()
        hygiene = app.courtyard.sanitation
        pose_before = hygiene.manager_pose(g)
        g.advance(g.meta["real_anchor"]+C.MANAGER_TRAVEL_SECONDS/2)
        pose_after = hygiene.manager_pose(g)
        assert pose_before[:2] != pose_after[:2], "管理员没有走向粪便"
        g.advance(g.meta["real_anchor"]+C.MANAGER_TRAVEL_SECONDS/2)
        assert g.sanitation.status() == "清扫中", "管理员没有进入清扫阶段"
        app.toast_until = 0
        app.courtyard.tick(.25)
        rng_state = g.rng.getstate()
        app.draw()
        worker_rect = hygiene.manager_rect.clip(app.screen.get_rect())
        before = pygame.image.tostring(app.screen.subsurface(worker_rect),"RGB")
        app.courtyard.tick(.25)
        app.draw()
        after = pygame.image.tostring(app.screen.subsurface(worker_rect),"RGB")
        assert before != after, "扫帚没有动画"
        assert g.rng.getstate() == rng_state, "管理员动画消耗了游戏随机数"
        pygame.image.save(app.screen,str(output/"manager.png"))
        g.advance(g.meta["real_anchor"]+C.MANAGER_SWEEP_SECONDS)
        assert all(w["waste_id"] != job["waste_id"] for w in g.waste), "清扫后地面目标未移除"
        assert g.inventory.count("粪便") > 0 and g.meta["manager_cleaned"] > 0, "管理员未将粪便入库"
        for actor in app.actor_order:
            actor.x,actor.y = 225,660
        app.draw()
        assert hygiene.completion_until > hygiene.time, "清扫结束缺少完成动画标记"
        waste_rect,waste_id = hygiene.waste_hits[0]
        app.handle(pygame.event.Event(pygame.MOUSEBUTTONDOWN,button=1,pos=waste_rect.center))
        app.handle(pygame.event.Event(pygame.MOUSEBUTTONUP,button=1,pos=waste_rect.center))
        assert app.page == ("sanitation",{"waste_id":waste_id}), "点击粪便未打开清理/出售面板"
        before_coins = g.meta["coins"]
        sell = next(b for b in app.modal.buttons if b.label.startswith("就地出售"))
        app.modal.scroll = max(0,min(app.modal.max_scroll,sell.rect.y-180))
        app.handle(pygame.event.Event(pygame.MOUSEBUTTONDOWN,button=1,pos=sell.rect.move(app.modal.offset()).center))
        assert all(w["waste_id"] != waste_id for w in g.waste), "就地出售未移除粪便"
        assert g.meta["coins"] == before_coins+C.ITEMS["粪便"]["sell"], "就地出售没有金币收入"
        app.open("sanitation",push=False)
        app.draw()
        pygame.image.save(app.screen,str(output/"sanitation.png"))
        sell_all = next(b for b in app.modal.buttons if b.label.startswith("就地出售"))
        app.modal.scroll = max(0,min(app.modal.max_scroll,sell_all.rect.y-180))
        app.handle(pygame.event.Event(pygame.MOUSEBUTTONDOWN,button=1,pos=sell_all.rect.move(app.modal.offset()).center))
        assert g.sanitation.quantity() == 0, "全场粪便出售按钮失效"
        assert hygiene.manager_pose(g)[2] == "清扫完成", "空闲时未展示刚才的清扫完成动画"
        sell_stock = next(b for b in app.modal.buttons if b.label == "出售仓库全部粪便")
        app.modal.scroll = max(0,min(app.modal.max_scroll,sell_stock.rect.y-180))
        app.handle(pygame.event.Event(pygame.MOUSEBUTTONDOWN,button=1,pos=sell_stock.rect.move(app.modal.offset()).center))
        assert g.inventory.count("粪便") == 0, "仓库粪便销售按钮失效"
        fire = next(b for b in app.modal.buttons if b.label == "解雇农场管理员")
        app.modal.scroll = max(0,min(app.modal.max_scroll,fire.rect.y-180))
        app.handle(pygame.event.Event(pygame.MOUSEBUTTONDOWN,button=1,pos=fire.rect.move(app.modal.offset()).center))
        assert not g.meta["farm_manager"], "管理员解雇按钮失效"
        app.close_modal()
        app.draw()
        assert hygiene.manager_rect is None, "解雇后管理员仍然可见"
        # 大世界拖拽/缩放不能误触宠物或修改经济状态。
        app.courtyard.home()
        before_model = g.snapshot()
        camera = app.courtyard.camera
        before_camera = camera.x,camera.y
        app.handle(pygame.event.Event(pygame.MOUSEBUTTONDOWN,button=1,pos=(750,350)))
        app.handle(pygame.event.Event(pygame.MOUSEMOTION,pos=(600,310),rel=(-150,-40),buttons=(1,0,0)))
        app.handle(pygame.event.Event(pygame.MOUSEBUTTONUP,button=1,pos=(600,310)))
        assert camera.x == before_camera[0]+150 and camera.y == before_camera[1]+40, "拖拽摄像机没有移动"
        assert g.snapshot() == before_model and app.page is None, "拖拽误触了游戏操作"
        anchor = camera.screen_to_world((620,380))
        with patch("pygame.mouse.get_pos",return_value=(620,380)):
            app.handle(pygame.event.Event(pygame.MOUSEWHEEL,x=0,y=1))
        after = camera.screen_to_world((620,380))
        assert abs(anchor[0]-after[0]) < .001 and abs(anchor[1]-after[1]) < .001, "缩放没有保持鼠标锚点"
        app.focus_pet_id = None
        app._sync_actors()
        for i,actor in enumerate(app.actor_order):
            actor.x,actor.y = 330+i*150,600
        app._last_pet_click = None
        app.draw()
        rect,pid = app.pet_hits[0]
        app.handle(pygame.event.Event(pygame.MOUSEBUTTONDOWN,button=1,pos=rect.center))
        app.handle(pygame.event.Event(pygame.MOUSEBUTTONUP,button=1,pos=rect.center))
        assert app.focus_pet_id == pid, f"平移缩放后宠物命中错误: {rect.center=} {app.page=} {app.pan=} actual={app.focus_pet_id} target={pid} local={app.courtyard.screen_to_local(rect.center)} hit={app.courtyard.pet_at(rect.center,app.actor_order)}"
        item = app.bottom.food_slots[0][1]
        count = g.inventory.count(item)
        slot = app.bottom.food_slots[0][0]
        target = app.courtyard.local_to_screen((app.actor_order[0].x,app.actor_order[0].y-20))
        app.handle(pygame.event.Event(pygame.MOUSEBUTTONDOWN,button=1,pos=slot.center))
        app.handle(pygame.event.Event(pygame.MOUSEBUTTONUP,button=1,pos=target))
        assert g.inventory.count(item) == count-1, "平移缩放后拖拽喂食错误"
        app.handle(pygame.event.Event(pygame.KEYDOWN,key=pygame.K_HOME))
        assert (camera.x,camera.y,camera.zoom) == (C.FARM_WORLD_RECT[0],C.FARM_WORLD_RECT[1],1), "Home没有返回庄园"
        app.courtyard.focus_zone("market")
        point = camera.world_to_screen((2985,565))
        app.handle(pygame.event.Event(pygame.MOUSEBUTTONDOWN,button=1,pos=point))
        app.handle(pygame.event.Event(pygame.MOUSEBUTTONUP,button=1,pos=point))
        assert app.page == ("zone",{"zone_id":"market"}), "点击商业街没有打开街区管理"
        before_coins = g.meta["coins"]
        upgrade = next(b for b in app.modal.buttons if b.label.startswith("开放街区"))
        app.handle(pygame.event.Event(pygame.MOUSEBUTTONDOWN,button=1,pos=upgrade.rect.move(app.modal.offset()).center))
        assert g.town.level("market") == 1 and g.meta["coins"] == before_coins-C.TOWN_ZONES["market"]["cost"], "开放街区按钮错误"
        for key in ("homes","park","station","fields"):
            g.perform("upgrade_zone",zone_id=key)
        app.close_modal()
        before_positions = {k:(a.x,a.y) for k,a in app.courtyard.people.items()}
        for _ in range(360):
            app.courtyard.tick(.25,g)
        assert any((a.x,a.y) != before_positions.get(k) for k,a in app.courtyard.people.items()), "居民游客没有沿街道移动"
        app.courtyard.focus_zone("market")
        app.draw()
        assert app.courtyard.person_hits, "商业街没有可见的行人"
        human_rect,_ = next((r,pid) for r,pid in app.courtyard.person_hits if town_view.VIEWPORT.collidepoint(r.center) and not app.courtyard.overview_rect.collidepoint(r.center))
        human_id = app.courtyard.person_at(human_rect.center)
        app.handle(pygame.event.Event(pygame.MOUSEBUTTONDOWN,button=1,pos=human_rect.center))
        app.handle(pygame.event.Event(pygame.MOUSEBUTTONUP,button=1,pos=human_rect.center))
        assert app.page == ("person",{"person_id":human_id}), "行人档案无法打开"
        app.close_modal()
        app.toast_until = 0
        app.draw()
        pygame.image.save(app.screen,str(output/"town-market.png"))
        # 在不推进经济模型的情况下检查七种天气和四种昼夜画面。
        original_now,original_weather = g.now,g.meta["weather"]
        weather_signatures = set()
        for phase,hour in (("dawn",6),("day",11),("dusk",18),("night",22)):
            g.meta["sim_ts"] = (original_now//C.DAY+1)*C.DAY+hour*C.HOUR
            for kind in C.WEATHERS:
                g.meta["weather"] = kind
                app.courtyard.tick(.1,g)
                app.draw()
                weather_signatures.add(hashlib.sha256(pygame.image.tostring(app.screen.subsurface(pygame.Rect(430,250,350,250)),"RGB")).digest())
                if phase == "day" and kind in ("大风","暴雨","雨夹雪","降雪"):
                    name = {"大风":"wind","暴雨":"rain","雨夹雪":"sleet","降雪":"snow"}[kind]
                    pygame.image.save(app.screen,str(output/f"town-{name}.png"))
                if phase == "night" and kind == "晴天":
                    pygame.image.save(app.screen,str(output/"town-night.png"))
        g.meta["sim_ts"],g.meta["weather"] = original_now,original_weather
        assert len(weather_signatures) >= 20, "昼夜天气画面没有变化"
        app.open("map")
        app.draw()
        pygame.image.save(app.screen,str(output/"town-map.png"))
        app.close_modal()
        app.courtyard.focus_zone("park")
        app.draw()
        pygame.image.save(app.screen,str(output/"town-park.png"))
        # 固定小地图定位和相机边界，以及焦点丢失的拖拽取消。
        app.handle(pygame.event.Event(pygame.MOUSEBUTTONDOWN,button=1,pos=app.courtyard.overview_rect.center))
        camera.pan(100000,100000)
        assert camera.x == camera.y == 0
        camera.pan(-100000,-100000)
        assert camera.x <= C.WORLD_SIZE[0]-camera.width/camera.zoom+.01
        app.handle(pygame.event.Event(pygame.MOUSEBUTTONDOWN,button=1,pos=(600,360)))
        app.handle(pygame.event.Event(pygame.WINDOWFOCUSLOST))
        assert app.pan is None and app.drag is None, "窗口失焦后拖拽没有取消"
        app.courtyard.home()
        validate(g.snapshot())
        from build_smoke import exercise
        exercise(app,g,output)
        # 测试退出关闭事件以及退出时最终提交。
        app.handle(pygame.event.Event(pygame.QUIT))
        assert not app.running
        g.close()
        pygame.quit()
        print(f"UI smoke passed: {len(pages)} page states; courtyard click/double-click, right-click dossier, "
              f"drag feed, map overlay, RNG purity, Chinese input, rename, mode toggle, quit; "
              f"{frames_checked} pet animation states; town pan/zoom/hit tests, district economy, moving people, 28 weather/day phases; screenshots: {output}")


if __name__ == "__main__":
    run()
