using Godot;
using System;
using System.Linq;
using System.Collections.Generic;
using System.Text.Json.Nodes;

public partial class Main
{
    FarmManagerAgent? _farmManager;
    readonly Dictionary<string,MeshInstance3D> _wasteViews=new();
    double _farmViewTimer;
    Label? _farmManagerInformation;
    string FarmManagerSummary=>$"农场管理员：{NativeGame.S(Data.Meta,"manager_status")} · 已清扫{NativeGame.I(Data.Meta,"manager_cleaned")}份 · 已喂食{NativeGame.I(Data.Meta,"manager_fed")}次";
    public string FarmLayoutSignature=>_layoutSignature;
    public PetAgent? FarmPet(string id)=>GetChildren().OfType<PetAgent>().FirstOrDefault(p=>p.Ident==id&&!p.IsQueuedForDeletion());
    public Vector3 WastePosition(JsonNode pile)
    {
        if(pile["native_x"]==null){
            string id=NativeGame.S(pile,"waste_id");uint hash=2166136261;foreach(char c in id){hash^=c;hash*=16777619;}
            var pet=FarmPet(NativeGame.S(pile,"pet_id"));
            int building=NativeGame.I(pile,"building_id");
            var position=pet?.Position??WorldData.Expand(new Vector3(110+building%5*19,0,103+building/5*2));
            position+=new Vector3((hash%101/100f-.5f)*1.2f,0,((hash/101)%101/100f-.5f)*1.2f);
            pile["native_x"]=position.X;pile["native_z"]=position.Z;
        }
        return new((float)NativeGame.D(pile,"native_x"),0,(float)NativeGame.D(pile,"native_z"));
    }
    public Vector3? FarmTaskTarget(JsonNode job)
    {
        if(NativeGame.S(job,"kind","clean")=="feed")return FarmPet(NativeGame.S(job,"pet_id"))?.Position;
        var pile=Game.Rows("waste").FirstOrDefault(w=>NativeGame.S(w!,"waste_id")==NativeGame.S(job,"waste_id"));
        return pile==null?null:WastePosition(pile);
    }
    void UpdateFarmView(double delta)
    {
        _farmViewTimer-=delta;if(_farmViewTimer>0)return;_farmViewTimer=.25;
        if(_farmManagerInformation!=null&&GodotObject.IsInstanceValid(_farmManagerInformation)&&!_farmManagerInformation.IsQueuedForDeletion())_farmManagerInformation.Text=FarmManagerSummary;
        bool hired=NativeGame.B(Data.Meta,"farm_manager");
        if(hired&&(_farmManager==null||!GodotObject.IsInstanceValid(_farmManager))){
            var saved=new Vector3((float)NativeGame.D(Data.Meta,"manager_native_x",184*WorldData.LayoutScale),0,(float)NativeGame.D(Data.Meta,"manager_native_z",109*WorldData.LayoutScale));
            _farmManager=new FarmManagerAgent{Town=this,Position=saved};_farmManager.SetMeta("kind","manager");_farmManager.SetMeta("ident","farm-manager");AddChild(_farmManager);
        }else if(!hired&&_farmManager!=null){_farmManager.QueueFree();_farmManager=null;}
        var piles=Game.Rows("waste").Take(200).ToArray();var ids=piles.Select(w=>NativeGame.S(w!,"waste_id")).ToHashSet();
        foreach(var id in _wasteViews.Keys.Where(id=>!ids.Contains(id)).ToArray()){_wasteViews[id].QueueFree();_wasteViews.Remove(id);}
        foreach(var pile in piles){string id=NativeGame.S(pile!,"waste_id");if(_wasteViews.ContainsKey(id))continue;
            var view=new MeshInstance3D{Mesh=Prototype("decoration:waste"),Position=WastePosition(pile!),Scale=Vector3.One*.8f};AddChild(view);_wasteViews[id]=view;
            var body=new StaticBody3D{CollisionLayer=8,CollisionMask=0};body.SetMeta("kind","waste");body.SetMeta("ident",id);
            body.AddChild(new CollisionShape3D{Shape=new SphereShape3D{Radius=.45f},Position=Vector3.Up*.2f});view.AddChild(body);
        }
    }
    void SelectFarmManager()
    {
        _selectedPet="";
        _selection=$"农场管理员 {NativeGame.S(Game.Rules,"MANAGER_NAME","小林")}\n{NativeGame.S(Data.Meta,"manager_status")}\n已清扫 {NativeGame.I(Data.Meta,"manager_cleaned")} 份 · 已喂食 {NativeGame.I(Data.Meta,"manager_fed")} 次\n清扫后粪便入库，喂食消耗普通饲料。";
    }
    void LocateFarmManager()
    {
        if(_farmManager==null){_selection="请先雇佣农场管理员。";ShowManagement("环境");return;}
        _center=_farmManager.Position;_distance=24;_management?.Hide();SelectFarmManager();
    }
}
