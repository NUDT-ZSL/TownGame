using Godot;
using System;

public partial class Main
{
    const float TownDragThreshold=5;
    MouseButton _townPointerButton;
    Vector2 _townPointerOrigin,_townPointerLast;
    bool _townCameraDragging;

    void BeginTownPointer(InputEventMouseButton button)
    {
        if(!_camera.IsInsideTree()||_management?.Visible==true||_townSearchWindow?.Visible==true||
            !GetViewport().GetVisibleRect().HasPoint(button.Position))return;
        _townPointerButton=button.ButtonIndex;
        _townPointerOrigin=_townPointerLast=button.Position;
    }

    void CancelTownPointer()
    {
        _townPointerButton=MouseButton.None;
        if(!_townCameraDragging)return;
        _townCameraDragging=false;
        Input.SetDefaultCursorShape(Input.CursorShape.Arrow);
    }

    bool ContinueTownPointer(InputEvent input)
    {
        if(_townPointerButton==MouseButton.None)return false;
        if(_management?.Visible==true||_townSearchWindow?.Visible==true){CancelTownPointer();return false;}
        if(input is InputEventMouseMotion motion){
            var mask=_townPointerButton switch{MouseButton.Left=>MouseButtonMask.Left,MouseButton.Middle=>MouseButtonMask.Middle,_=>MouseButtonMask.Right};
            if((motion.ButtonMask&mask)==0){CancelTownPointer();return false;}
            MoveTownPointer(motion.Position);
            return _townCameraDragging;
        }
        if(input is not InputEventMouseButton button)return false;
        if(button.ButtonIndex!=_townPointerButton||button.Pressed)return true;
        var viewport=GetViewport();
        bool onScene=viewport.GetVisibleRect().HasPoint(button.Position)&&viewport.GuiGetHoveredControl()==null;
        if(onScene)MoveTownPointer(button.Position);
        bool click=onScene&&!InPhotoMode&&!_townCameraDragging&&button.ButtonIndex==MouseButton.Left;
        CancelTownPointer();
        if(click)Select(button.Position);
        return true;
    }

    void MoveTownPointer(Vector2 point)
    {
        if(!_townCameraDragging){
            if(point.DistanceSquaredTo(_townPointerOrigin)<TownDragThreshold*TownDragThreshold)return;
            _townCameraDragging=true;
            StopResidentFollow();
            if(_cameraTransition>0){
                _center=new(_shownCamera.Target.X,0,_shownCamera.Target.Z);
                _yaw=_shownCamera.Yaw;
                _pitch=_shownCamera.Pitch;
                _distance=_shownCamera.Distance;
                _cameraTransition=0;
            }
            if(_townPointerButton!=MouseButton.Right){
                StopPetFollow();
                _portraitAnchor=null;
                _distance=Math.Max(_distance,5.5f);
            }
            ClearSceneHover();
            Input.SetDefaultCursorShape(Input.CursorShape.Drag);
        }
        var previous=_townPointerLast;
        _townPointerLast=point;
        if(point.IsEqualApprox(previous))return;
        if(_townPointerButton==MouseButton.Right){
            var orbit=TownOrbitDelta(point-previous);
            _yaw=Mathf.Wrap(_yaw+orbit.X,-Mathf.Pi,Mathf.Pi);
            _pitch=Math.Clamp(_pitch+orbit.Y,.25f,1.4f);
        }else{
            _center+=TownGroundDrag(previous,point);
            ClampTownCameraCenter();
        }
        UpdateCamera();
    }

    Vector3 TownGroundDrag(Vector2 previous,Vector2 point)
    {
        float height=Math.Max(1,GetViewport().GetVisibleRect().Size.Y);
        float unitsPerPixel=_buildCamera?_camera.Size/height:
            2*Math.Max(4,_camera.Position.DistanceTo(_shownCamera.Target))*Mathf.Tan(Mathf.DegToRad(_camera.Fov/2))/height;
        var pixels=point-previous;
        if(GroundUnderCursor(previous,.23f) is Vector3 before&&GroundUnderCursor(point,.23f) is Vector3 after){
            var grounded=before-after;
            if(grounded.Length()<=pixels.Length()*unitsPerPixel*4)return grounded;
        }
        // Keep panning predictable when the pointer is on the sky or near the horizon.
        var across=_camera.Basis.X;
        var forward=Vector3.Up.Cross(across);
        return (forward*pixels.Y-across*pixels.X)*unitsPerPixel;
    }

    void ClampTownCameraCenter()
    {
        _center.X=Math.Clamp(_center.X,0,WorldData.WorldWidth);
        _center.Z=Math.Clamp(_center.Z,0,WorldData.WorldDepth);
    }
}
