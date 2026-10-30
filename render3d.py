"""离屏 GPU 三维渲染：深度遮挡、实体光照、阴影贴图和逐像素拾取。"""
import math
from array import array
from collections import OrderedDict
import pygame
from camera3d import dot


TRANSFORM = '''
in vec3 in_position;
in vec3 in_normal;
in vec3 in_color;
in float in_emission;
in vec3 offset;
in float angle;
in float scale;
in float ident;
uniform float elapsed;
vec3 rotate(vec3 p) {
    float c=cos(angle),s=sin(angle);
    return vec3(c*p.x-s*p.y,s*p.x+c*p.y,p.z);
}
vec3 animated_position() {
    vec3 p=in_position*scale;
    if(in_emission < -.5 && in_emission > -1.5) {
        float bend=max(0.,in_position.z-30.)/110.;
        p.x+=sin(elapsed*1.4+offset.x*.017+offset.y*.013)*bend*bend*3.;
        p.y+=cos(elapsed*1.1+offset.x*.013)*bend*bend*1.5;
    }
    p=rotate(p)+offset;
    if(in_emission < -1.5) p.z+=sin(p.x*.035+elapsed*1.8)*.7+cos(p.y*.045-elapsed*1.3)*.5;
    return p;
}
vec3 light_position(vec3 p) {
    vec3 v=p-vec3(3000,1920,0);
    return vec3(dot(v,vec3(.894427,-.447214,0))/4300.,
                dot(v,vec3(-.365148,-.730297,.577350))/4300.,
                dot(v,vec3(-.258199,-.516398,-.816497))/6500.);
}
'''
VERTEX = '''#version 330
''' + TRANSFORM + '''
uniform vec3 eye,right,up,forward;
uniform vec2 focal;
out vec3 normal,color,world;
out float emission;
flat out float object_id;
void main() {
    world=animated_position();
    vec3 v=world-eye;
    float depth=dot(v,forward);
    gl_Position=vec4(dot(v,right)*focal.x,dot(v,up)*focal.y,
                     1.0010005*depth-10.0050025,depth);
    normal=rotate(in_normal);color=in_color;emission=in_emission;object_id=ident;
}
'''
FRAGMENT = '''#version 330
in vec3 normal,color,world;
in float emission;
flat in float object_id;
uniform sampler2D shadow_map;
uniform float night;
uniform vec3 eye;
uniform float elapsed;
layout(location=0) out vec4 out_color;
layout(location=1) out vec4 out_id;
''' + TRANSFORM[TRANSFORM.index('vec3 light_position'):] + '''
void main() {
    vec3 n=normalize(normal),sun=normalize(vec3(.316,.632,1.));
    vec3 lp=light_position(world)*.5+.5;
    float shadow=0.;
    if (lp.x>0. && lp.x<1. && lp.y>0. && lp.y<1.) {
        float bias=max(.00025,.0009*(1.-dot(n,sun)));
        for(int x=-1;x<=1;x++) for(int y=-1;y<=1;y++)
            shadow+=(lp.z-bias>texture(shadow_map,lp.xy+vec2(x,y)/2048.).r)?1.:0.;
        shadow/=9.;
    }
    float diffuse=max(0.,dot(n,sun));
    float lighting=.57+.43*diffuse*(1.-shadow*.72);
    vec3 c=color*lighting*mix(vec3(1.04,1.01,.96),vec3(.60,.69,.91),night);
    float specular=pow(max(0.,dot(n,normalize(sun+normalize(eye-world)))),24.)*.035;
    c+=specular*(1.-night*.6);
    c=mix(c,mix(color,vec3(1.,.82,.48),.75),max(0.,emission)*night*.85);
    if(emission < -1.5) {
        float ripple=sin(length(world.xy-vec2(595.,1645.))*.15-elapsed*2.2+sin(world.y*.025)*.5);
        c+=vec3(.08,.13,.14)*smoothstep(.88,1.,ripple);
    }
    float fog=clamp((distance(world,eye)-1800.)/7000.,0.,.28);
    c=mix(c,mix(vec3(.78,.85,.77),vec3(.35,.43,.54),night),fog);
    out_color=vec4(c,1.);
    float id=object_id;
    out_id=vec4(mod(id,256.),mod(floor(id/256.),256.),floor(id/65536.),255.)/255.;
}
'''
SHADOW_VERTEX = '''#version 330
''' + TRANSFORM + '''
void main() {gl_Position=vec4(light_position(animated_position()),1.);}
'''


class Renderer3D:
    def __init__(self,software=False):
        self.ctx=None;self.failure=None;self.cache=OrderedDict();self.size=None
        self.framebuffer=None;self.targets=[];self.ids=b'';self.keys=[];self.pick_surface=None;self.backend='软件三维'
        if not software:
            try:
                import moderngl
                self.gl=moderngl;self.ctx=moderngl.create_standalone_context(require=330)
                self.program=self.ctx.program(vertex_shader=VERTEX,fragment_shader=FRAGMENT)
                self.shadow_program=self.ctx.program(vertex_shader=SHADOW_VERTEX,fragment_shader='#version 330\nvoid main() {}')
                self.shadow_texture=self.ctx.depth_texture((2048,2048));self.shadow_texture.compare_func=''
                self.shadow_texture.repeat_x=self.shadow_texture.repeat_y=False
                self.shadow_fbo=self.ctx.framebuffer(depth_attachment=self.shadow_texture)
                self.backend='GPU · '+self.ctx.info.get('GL_RENDERER','OpenGL 3.3')
            except Exception as error:
                self.failure=str(error)
                self.close();self.ctx=None

    def _resize(self,size):
        if size==self.size:return
        if self.framebuffer:self.framebuffer.release()
        for item in self.targets:item.release()
        self.size=size
        color=self.ctx.texture(size,4);ids=self.ctx.texture(size,4);depth=self.ctx.depth_renderbuffer(size)
        self.targets=[color,ids,depth];self.framebuffer=self.ctx.framebuffer([color,ids],depth)

    def _mesh(self,mesh):
        if mesh in self.cache:
            value=self.cache.pop(mesh);self.cache[mesh]=value;return value
        vbo=self.ctx.buffer(mesh.data.tobytes());instances=self.ctx.buffer(reserve=24)
        vao=self.ctx.vertex_array(self.program,[(vbo,'3f 3f 3f 1f','in_position','in_normal','in_color','in_emission'),
                 (instances,'3f 1f 1f 1f /i','offset','angle','scale','ident')])
        shadow=self.ctx.vertex_array(self.shadow_program,[(vbo,'3f 24x 1f','in_position','in_emission'),
                    (instances,'3f 1f 1f 4x /i','offset','angle','scale')])
        value=(vbo,instances,vao,shadow);self.cache[mesh]=value
        return value

    def draw(self,camera,objects,night=False,elapsed=None):
        self.elapsed=pygame.time.get_ticks()/1000 if elapsed is None else elapsed
        self.keys=[None];groups={}
        for mesh,position,angle,scale,key in objects:
            ident=0
            if key is not None:self.keys.append(key);ident=len(self.keys)-1
            groups.setdefault(mesh,array('f')).extend((*position,angle,scale,ident))
        if self.ctx is None:return self._software(camera,groups,night)
        # 小幅超采样改善细树枝、屋檐和道路边缘，UI 保持原生像素。
        size=(round(camera.width*1.15),round(camera.height*1.15));self._resize(size)
        commands=[]
        for mesh,data in groups.items():
            vbo,buffer,vao,shadow=self._mesh(mesh);raw=data.tobytes()
            buffer.orphan(len(raw));buffer.write(raw);commands.append((vao,shadow,len(data)//6))
        self.ctx.enable(self.gl.DEPTH_TEST);self.ctx.disable(self.gl.BLEND|self.gl.CULL_FACE)
        self.program['elapsed'].value=self.elapsed
        self.shadow_program['elapsed'].value=self.elapsed
        self.shadow_fbo.use();self.ctx.viewport=(0,0,2048,2048);self.shadow_fbo.clear(depth=1.)
        for vao,shadow,count in commands:shadow.render(instances=count)
        self.framebuffer.use();self.ctx.viewport=(0,0,*size)
        self.framebuffer.clear(*((.30,.38,.49,1.) if night else (.78,.85,.77,1.)),depth=1.)
        eye,right,up,forward,focal=camera.basis()
        for name,value in (('eye',eye),('right',right),('up',up),('forward',forward),
                           ('focal',(2*focal/camera.width,2*focal/camera.height)),('night',float(night)),('shadow_map',0)):
            self.program[name].value=value
        self.shadow_texture.use(0)
        for vao,shadow,count in commands:vao.render(instances=count)
        pixels=self.framebuffer.read(components=3,alignment=1)
        self.ids=self.framebuffer.read(attachment=1,components=4,alignment=1)
        image=pygame.image.frombytes(pixels,size,'RGB');image=pygame.transform.flip(image,False,True)
        image=pygame.transform.smoothscale(image,(camera.width,camera.height))
        # 保留当帧使用的资源，淘汰旧季节/旧样式，防止长期游玩积累显存。
        for mesh in list(self.cache):
            if len(self.cache)<=640:break
            if mesh not in groups:
                for item in self.cache.pop(mesh):item.release()
        return image

    def pick(self,pos,camera):
        if not camera.vx<=pos[0]<camera.vx+camera.width or not camera.vy<=pos[1]<camera.vy+camera.height:return None
        if self.ctx is None:
            if self.pick_surface is None:return None
            ident=self.pick_surface.get_at((int(pos[0]-camera.vx),int(pos[1]-camera.vy)))
            ident=ident.r+ident.g*256+ident.b*65536
        elif not self.ids:return None
        else:
            x=min(self.size[0]-1,int((pos[0]-camera.vx)*self.size[0]/camera.width))
            y=self.size[1]-1-min(self.size[1]-1,int((pos[1]-camera.vy)*self.size[1]/camera.height))
            offset=(y*self.size[0]+x)*4;r,g,b=self.ids[offset:offset+3];ident=r+g*256+b*65536
        return self.keys[ident] if 0<ident<len(self.keys) else None

    def _software(self,camera,groups,night):
        image=pygame.Surface((camera.width,camera.height));image.fill((191,211,190) if not night else (74,94,119))
        self.pick_surface=pygame.Surface(image.get_size());self.pick_surface.fill((0,0,0));triangles=[]
        eye=camera.basis()[0];sun=(.258,.516,.816)
        for mesh,data in groups.items():
            for index in range(0,len(data),6):
                ox,oy,oz,angle,scale,ident=data[index:index+6];c,s=math.cos(angle),math.sin(angle)
                low,high=mesh.bounds;corners=[]
                for x in (low[0],high[0]):
                    for y in (low[1],high[1]):
                        for z in (low[2],high[2]):
                            q=camera.project((ox+(x*c-y*s)*scale,oy+(x*s+y*c)*scale,oz+z*scale))
                            if q:corners.append(q)
                if not corners:continue
                if max(p[0] for p in corners)<camera.vx or min(p[0] for p in corners)>camera.vx+camera.width or max(p[1] for p in corners)<camera.vy or min(p[1] for p in corners)>camera.vy+camera.height:continue
                for k in range(0,len(mesh.data),30):
                    vertices=[];depth=0;points=[]
                    for j in (k,k+10,k+20):
                        x,y,z=mesh.data[j:j+3];p=(ox+(x*c-y*s)*scale,oy+(x*s+y*c)*scale,oz+z*scale)
                        material=mesh.data[j+9]
                        if -1.5<material<-.5:
                            bend=max(0,z-30)/110
                            bx=math.sin(self.elapsed*1.4+ox*.017+oy*.013)*bend*bend*3
                            by=math.cos(self.elapsed*1.1+ox*.013)*bend*bend*1.5
                            p=(p[0]+bx*c-by*s,p[1]+bx*s+by*c,p[2])
                        elif material<-1.5:
                            p=(p[0],p[1],p[2]+math.sin(p[0]*.035+self.elapsed*1.8)*.7+math.cos(p[1]*.045-self.elapsed*1.3)*.5)
                        q=camera.project(p)
                        if q is None:break
                        vertices.append((q[0]-camera.vx,q[1]-camera.vy));depth+=q[2];points.append(p)
                    if len(vertices)!=3:continue
                    if max(p[0] for p in vertices)<0 or min(p[0] for p in vertices)>camera.width or max(p[1] for p in vertices)<0 or min(p[1] for p in vertices)>camera.height:continue
                    nx,ny,nz=mesh.data[k+3:k+6];normal=(nx*c-ny*s,nx*s+ny*c,nz)
                    shade=.57+.43*max(0,dot(normal,sun));rgb=mesh.data[k+6:k+9]
                    color=tuple(max(0,min(255,int(v*255*shade*(.7 if night else 1)))) for v in rgb)
                    triangles.append((depth,vertices,color,int(ident)))
        for _,vertices,color,ident in sorted(triangles,key=lambda row:-row[0]):
            pygame.draw.polygon(image,color,vertices);pygame.draw.polygon(self.pick_surface,(ident%256,ident//256%256,ident//65536),vertices)
        return image

    def close(self):
        for values in self.cache.values():
            for item in values:item.release()
        self.cache.clear()
        for name in ('framebuffer','shadow_fbo','shadow_texture','program','shadow_program'):
            item=getattr(self,name,None)
            if item:item.release();setattr(self,name,None)
        for item in self.targets:item.release()
        self.targets=[]
        if self.ctx:self.ctx.release();self.ctx=None
