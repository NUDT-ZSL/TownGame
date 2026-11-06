using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

public partial class Main
{
    readonly record struct PortraitModel(string Species,int Style,bool Rare,bool Sick,string Stage,int Width,int Height,string Background);
    readonly record struct PortraitKey(PortraitModel Model,Viewport.Msaa Samples);
    sealed class PortraitTarget(TextureRect image,PortraitModel model)
    {
        public TextureRect Image {get;}=image;
        public PortraitModel Model {get;}=model;
        public PortraitKey? Displayed {get;set;}
    }
    const int PortraitLimit=96;
    readonly List<PortraitTarget> _portraitTargets=new();
    readonly Dictionary<PortraitKey,ImageTexture> _portraitTextures=new();
    readonly Queue<PortraitKey> _portraitOrder=new();
    SubViewport? _portraitViewport;
    Godot.Environment? _portraitLighting;
    MeshInstance3D? _portraitMesh;
    Camera3D? _portraitCamera;
    bool _portraitDrawing;

    TextureRect QueuePetPortrait(string species,int width,int height,string background,JsonNode? pet)
    {
        var model=new PortraitModel(species,pet==null?0:Game.Appearance(pet),pet!=null&&NativeGame.B(pet,"rare"),
            pet!=null&&(NativeGame.S(pet,"illness_type")!=""||NativeGame.D(pet,"injury")>0),
            pet==null?"成年":Game.Stage(pet),width,height,background);
        var picture=new TextureRect{Name="PetPortrait",CustomMinimumSize=new(width,height),MouseFilter=Control.MouseFilterEnum.Ignore,
            ExpandMode=TextureRect.ExpandModeEnum.IgnoreSize,StretchMode=TextureRect.StretchModeEnum.KeepAspectCentered};
        var target=new PortraitTarget(picture,model);
        _portraitTargets.Add(target);
        picture.TreeExiting+=()=>_portraitTargets.Remove(target);
        UseCachedPortrait(target,new(model,SceneAntialiasing));
        return picture;
    }

    bool UseCachedPortrait(PortraitTarget target,PortraitKey key)
    {
        if(target.Displayed==key)return true;
        if(!_portraitTextures.TryGetValue(key,out var texture))return false;
        target.Image.Texture=texture;
        target.Displayed=key;
        return true;
    }

    bool PortraitOnScreen(Control image)
    {
        if(!image.IsVisibleInTree())return false;
        for(Node? parent=image.GetParent();parent!=null;parent=parent.GetParent())
            if(parent is ScrollContainer scroll)return scroll.GetGlobalRect().Intersects(image.GetGlobalRect());
        return true;
    }

    void RefreshPetPortraits()
    {
        if(_management?.Visible!=true||_portraitDrawing||DisplayServer.GetName()=="headless")return;
        foreach(var target in _portraitTargets){
            if(!PortraitOnScreen(target.Image))continue;
            var key=new PortraitKey(target.Model,SceneAntialiasing);
            if(UseCachedPortrait(target,key))continue;
            _=RenderPetPortrait(key);
            break;
        }
    }

    async Task RenderPetPortrait(PortraitKey key)
    {
        _portraitDrawing=true;
        try{
            if(_portraitViewport==null){
                _portraitViewport=new SubViewport{Name="TownPortraitRenderer",OwnWorld3D=true,
                    RenderTargetUpdateMode=SubViewport.UpdateMode.Disabled};
                AddChild(_portraitViewport);
                _portraitLighting=new Godot.Environment{BackgroundMode=Godot.Environment.BGMode.Color,
                    AmbientLightSource=Godot.Environment.AmbientSource.Color,AmbientLightColor=new("fff5dc"),AmbientLightEnergy=.65f};
                _portraitViewport.AddChild(new WorldEnvironment{Environment=_portraitLighting});
                _portraitMesh=new MeshInstance3D{Scale=Vector3.One*.5f,Rotation=new(0,.25f,0)};
                _portraitViewport.AddChild(_portraitMesh);
                _portraitCamera=new Camera3D{Fov=40,Current=true,KeepAspect=Camera3D.KeepAspectEnum.Height};
                _portraitViewport.AddChild(_portraitCamera);
                _portraitViewport.AddChild(new DirectionalLight3D{RotationDegrees=new(-40,-30,0),LightEnergy=1.4f});
            }
            var model=key.Model;
            _portraitViewport.Size=new(model.Width,model.Height);
            _portraitViewport.Msaa3D=key.Samples;
            _portraitLighting!.BackgroundColor=new Color(model.Background);
            var mesh=_petModels.Get(model.Species,model.Style,model.Rare,model.Sick,model.Stage,"idle",0);
            _portraitMesh!.Mesh=mesh;
            var bounds=mesh.GetAabb();
            var transform=_portraitMesh.Transform;
            var focus=transform*bounds.GetCenter();
            var direction=new Vector3(.55f,.35f,1.6f).Normalized();
            var right=Vector3.Up.Cross(direction).Normalized();
            var up=direction.Cross(right);
            float vertical=Mathf.Tan(Mathf.DegToRad(_portraitCamera!.Fov*.5f))*.87f;
            float horizontal=vertical*model.Width/model.Height;
            float distance=1;
            var vertices=((ArrayMesh)mesh).SurfaceGetArrays(0)[(int)Mesh.ArrayType.Vertex].AsVector3Array();
            foreach(var vertex in vertices){
                var point=transform*vertex-focus;
                distance=Math.Max(distance,point.Dot(direction)+Math.Max(Math.Abs(point.Dot(right))/horizontal,Math.Abs(point.Dot(up))/vertical));
            }
            _portraitCamera.LookAtFromPosition(focus+direction*distance,focus,Vector3.Up);
            _portraitViewport.RenderTargetUpdateMode=SubViewport.UpdateMode.Once;
            await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
            using var image=_portraitViewport.GetTexture().GetImage();
            if(image.IsEmpty())return;
            _portraitTextures[key]=ImageTexture.CreateFromImage(image);
            _portraitOrder.Enqueue(key);
            if(_portraitOrder.Count>PortraitLimit)_portraitTextures.Remove(_portraitOrder.Dequeue());
            foreach(var target in _portraitTargets)
                if(target.Model==model&&key.Samples==SceneAntialiasing)UseCachedPortrait(target,key);
        }catch(Exception error){GD.PushWarning($"伙伴头像生成失败 · {error.Message}");}
        finally{_portraitDrawing=false;}
    }

    bool VisiblePortraitsReady()=>_portraitTargets.Where(target=>PortraitOnScreen(target.Image))
        .All(target=>target.Displayed==new PortraitKey(target.Model,SceneAntialiasing));
}
