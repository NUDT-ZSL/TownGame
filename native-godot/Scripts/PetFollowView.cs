using Godot;
using System;

public partial class Main
{
    PetAgent? _followPet;
    Button? _followPetButton;

    void LocateActivityArea()
    {
        RememberCameraSite(PetActivityCenter);
        StopPetFollow();
        _center=PetActivityCenter;
        _yaw=.48f;
        _pitch=.68f;
        _distance=42;
        _selection="宠物活动区 · 点击食盆、浴盆或玩具进行照护";
        _management?.Hide();
    }

    bool StartPetFollow(string id)
    {
        var actor=FarmPet(id);
        if(actor==null||!LocatePet(id))return false;
        _followPet=actor;
        _distance=Math.Max(6,_distance);
        if(_followPetButton!=null)_followPetButton.Text="退出跟随";
        return true;
    }

    void StopPetFollow()
    {
        if(_followPet==null)return;
        _followPet=null;
        if(_followPetButton!=null)_followPetButton.Text="跟随宠物";
    }

    void UpdatePetFollow(double delta)
    {
        if(_followPet==null)return;
        if(!GodotObject.IsInstanceValid(_followPet)||_followPet.IsQueuedForDeletion()||_followPet.Ident!=_selectedPet){
            StopPetFollow();
            return;
        }
        _center=_center.Lerp(_followPet.Position,1-Mathf.Exp(-12*(float)delta));
    }

}
