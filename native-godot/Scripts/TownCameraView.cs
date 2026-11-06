using Godot;
using System;
using System.Linq;
using System.Text.Json.Nodes;

public partial class Main
{
    enum CameraPreset { Manor, Build, Pet }
    readonly record struct CameraFrame(Vector3 Target, float Yaw, float Pitch, float Distance);
    const float CameraTransitionTime=.4f;
    static readonly float[] ObstacleViewTurns={0,.7f,-.7f};
    CameraFrame _shownCamera, _cameraFrom, _cameraTo;
    float _cameraTransition;
    bool _cameraFrameReady, _buildCamera;
    Vector3? _portraitAnchor;
    float _portraitLift,_portraitRadius;

    MenuButton CameraViewPicker()
    {
        var picker=new MenuButton{Name="TownCameraPresets",Text="视角",Flat=false,TooltipText="左键或中键拖动平移，右键旋转，滚轮缩放。也可切换视角或保存机位。"};
        TownTheme.AccentButton(picker,"地图",6);
        var menu=picker.GetPopup();
        menu.AddItem("庄园全景",(int)CameraPreset.Manor);
        menu.AddItem("等距建造",(int)CameraPreset.Build);
        menu.AddItem("宠物特写",(int)CameraPreset.Pet);
        picker.AboutToPopup+=()=>RefreshCameraBookmarkMenu(menu);
        menu.IdPressed+=id=>{
            if(id is >=0 and <=2)SetCameraPreset((CameraPreset)id);
            else HandleCameraBookmarkMenu(id);
        };
        return picker;
    }

    void SetCameraPreset(CameraPreset preset)
    {
        StopResidentFollow();
        var start=_shownCamera;
        if(preset==CameraPreset.Pet){
            if(!LocatePet(_selectedPet)){
                var pet=GetChildren().OfType<PetAgent>().FirstOrDefault(actor=>!actor.IsQueuedForDeletion());
                if(pet==null||!LocatePet(pet.Ident)){
                    _selection="庄园暂无可查看的宠物";
                    return;
                }
            }
        }else{
            RememberCameraSite();
            StopPetFollow();
            _portraitAnchor=null;
            _buildCamera=preset==CameraPreset.Build;
            _yaw=Mathf.Pi/4;
            _pitch=_buildCamera?Mathf.Atan(1/Mathf.Sqrt(2)):.88f;
            _distance=_buildCamera?58:102;
            if(preset==CameraPreset.Manor)_center=WorldData.Expand(new(135,0,85));
        }
        if(!_cameraFrameReady)return;
        _cameraFrom=start;
        _cameraTo=DesiredCameraFrame();
        _cameraTransition=CameraTransitionTime;
    }

    void SetPetCameraFrame(PetAgent actor, JsonNode pet)
    {
        _buildCamera=false;
        _cameraTransition=0;
        _portraitAnchor=actor.Position;
        var bounds=PetMesh(pet,"idle",0).GetAabb();
        float ageScale=Game.Stage(pet) switch{"幼崽"=>.72f,"少年"=>.86f,"老年"=>.94f,_=>1};
        float scale=PetWorldScale(actor.Species)*ageScale;
        _portraitLift=Math.Max(.22f,bounds.GetCenter().Y*scale);
        _portraitRadius=bounds.Size.Length()*scale*.5f;
        _center=actor.Position;
        _yaw=actor.Rotation.Y+.38f;
        _pitch=.52f;
        _distance=Math.Max(MinimumPortraitDistance(),Math.Clamp(_portraitRadius*5,4.2f,12));
    }

    bool InPetCamera=>_followPet!=null||(_portraitAnchor is Vector3 anchor&&anchor.IsEqualApprox(_center));

    float MinimumPortraitDistance()
    {
        var viewport=GetViewport().GetVisibleRect().Size;
        float aspect=Math.Min(1,viewport.X/Math.Max(1,viewport.Y));
        float tangent=Mathf.Tan(Mathf.DegToRad(_camera.Fov*.5f))*.82f*Math.Max(.2f,aspect);
        return Math.Clamp(_portraitRadius/Mathf.Sin(Mathf.Atan(tangent)),3.2f,45);
    }

    CameraFrame DesiredCameraFrame()
    {
        float lift=InPetCamera?_portraitLift:0;
        return new(_center+Vector3.Up*lift,_yaw,_pitch,_distance);
    }

    void UpdateCamera(float delta=0)
    {
        var desired=DesiredCameraFrame();
        var frame=desired;
        if(!_reduceCameraMotion&&_cameraTransition>0&&desired==_cameraTo){
            _cameraTransition=Math.Max(0,_cameraTransition-delta);
            float t=Mathf.SmoothStep(0,1,1-_cameraTransition/CameraTransitionTime);
            frame=new(_cameraFrom.Target.Lerp(desired.Target,t),
                Mathf.LerpAngle(_cameraFrom.Yaw,desired.Yaw,t),
                Mathf.Lerp(_cameraFrom.Pitch,desired.Pitch,t),Mathf.Lerp(_cameraFrom.Distance,desired.Distance,t));
        }else _cameraTransition=0;
        _shownCamera=frame;
        _cameraFrameReady=true;
        float pitch=frame.Pitch;
        var offset=CameraOffset(frame,_buildCamera?Math.Max(frame.Distance,42):frame.Distance,pitch);
        _camera.Projection=_buildCamera?Camera3D.ProjectionType.Orthogonal:Camera3D.ProjectionType.Perspective;
        if(_buildCamera)_camera.Size=frame.Distance*1.08f;
        if(!_buildCamera&&frame.Distance<=120){
            float clear=CameraClearance(frame.Target,offset);
            float needed=InPetCamera?Math.Min(frame.Distance,MinimumPortraitDistance()):Math.Min(frame.Distance*.72f,12);
            if(clear<needed){
                float raisedPitch=Math.Min(1.38f,pitch+.55f);
                foreach(float turn in ObstacleViewTurns){
                    var raised=CameraOffset(frame with{Yaw=frame.Yaw+turn},frame.Distance,raisedPitch);
                    float raisedClear=CameraClearance(frame.Target,raised);
                    if(raisedClear>clear){offset=raised;clear=raisedClear;}
                    if(clear>=needed)break;
                }
            }
            offset=offset.Normalized()*clear;
        }
        _camera.Position=frame.Target+offset;
        _camera.LookAt(frame.Target,Vector3.Up);
    }

    static Vector3 CameraOffset(CameraFrame frame,float distance,float pitch)=>
        new Vector3(Mathf.Sin(frame.Yaw)*Mathf.Cos(pitch),Mathf.Sin(pitch),Mathf.Cos(frame.Yaw)*Mathf.Cos(pitch))*distance;

    float CameraClearance(Vector3 target,Vector3 offset)
    {
        var start=target+Vector3.Up*.18f;
        using var query=PhysicsRayQueryParameters3D.Create(start,target+offset,1);
        var hit=GetWorld3D().DirectSpaceState.IntersectRay(query);
        return hit.Count==0?offset.Length():Math.Max(.35f,target.DistanceTo(hit["position"].AsVector3())-.42f);
    }
}
