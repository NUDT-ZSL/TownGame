"""与Pygame无关的摄像机：拖拽平移、指针锚定缩放、坐标变换和边界限制。"""
class Camera:
    def __init__(self, world_size, viewport=(0,88,1280,616), position=(1160,760)):
        self.world_w,self.world_h = world_size
        self.vx,self.vy,self.width,self.height = viewport
        self.x,self.y = position
        self.zoom = 1.0
        self.clamp()

    def clamp(self):
        self.x = max(0,min(self.x,max(0,self.world_w-self.width/self.zoom)))
        self.y = max(0,min(self.y,max(0,self.world_h-self.height/self.zoom)))

    def world_to_screen(self, pos):
        return self.vx+(pos[0]-self.x)*self.zoom,self.vy+(pos[1]-self.y)*self.zoom

    def screen_to_world(self, pos):
        return self.x+(pos[0]-self.vx)/self.zoom,self.y+(pos[1]-self.vy)/self.zoom

    def pan(self, dx, dy):
        self.x -= dx/self.zoom
        self.y -= dy/self.zoom
        self.clamp()

    def center(self, pos):
        self.x,self.y = pos[0]-self.width/self.zoom/2,pos[1]-self.height/self.zoom/2
        self.clamp()

    def zoom_at(self, pos, factor):
        anchor = self.screen_to_world(pos)
        self.zoom = max(.60,min(1.40,self.zoom*factor))
        self.x = anchor[0]-(pos[0]-self.vx)/self.zoom
        self.y = anchor[1]-(pos[1]-self.vy)/self.zoom
        self.clamp()
