using Godot;
using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;

public partial class FarmManagerAgent : CharacterBody3D
{
    public Main Town=null!;
    public string TaskId="",Activity="待命";
    public int CompletedVisits;
    public float WalkedDistance;
    public readonly MeshInstance3D Visual=new();
    readonly Label3D _label=new(){FontSize=28,PixelSize=.014f,Billboard=BaseMaterial3D.BillboardModeEnum.Enabled,Position=new(0,2.5f,0),Modulate=new Color("ffe4a3")};
    readonly Node3D _tool=new();
    readonly Node3D _food=new();
    FarmNavigation _navigation=null!;
    List<Vector3> _path=new();int _index;
    double _think,_working,_stuck,_animation,_savePosition,_repath;
    bool _arrived;
    Vector3 _lastTarget;
    public override void _Ready()
    {
        CollisionLayer=2;CollisionMask=7;SafeMargin=.02f;
        AddChild(new CollisionShape3D{Shape=new CapsuleShape3D{Radius=.3f,Height=1.8f},Position=Vector3.Up*.9f});
        Visual.Mesh=Town.Prototype("person-0");AddChild(Visual);
        // Separate reusable tools make the current job readable without replacing exported assets.
        MeshInstance3D Part(Mesh mesh,Vector3 position,string color)=>new(){Mesh=mesh,Position=position,MaterialOverride=new StandardMaterial3D{AlbedoColor=new Color(color)}};
        Visual.AddChild(Part(new CylinderMesh{TopRadius=.34f,BottomRadius=.34f,Height=.05f},new(0,1.73f,0),"d8bd81"));
        Visual.AddChild(Part(new CylinderMesh{TopRadius=.22f,BottomRadius=.24f,Height=.18f},new(0,1.83f,0),"ddc892"));
        Visual.AddChild(_tool);_tool.Position=new(.42f,.7f,.24f);
        _tool.AddChild(Part(new CylinderMesh{TopRadius=.025f,BottomRadius=.025f,Height=1.3f},Vector3.Zero,"906e47"));
        _tool.AddChild(Part(new BoxMesh{Size=new(.4f,.14f,.22f)},new(0,-.62f,0),"c8ae75"));
        Visual.AddChild(_food);_food.Position=new(.38f,.75f,.28f);
        _food.AddChild(Part(new CylinderMesh{TopRadius=.2f,BottomRadius=.16f,Height=.14f},Vector3.Zero,"668e94"));
        _food.AddChild(Part(new CylinderMesh{TopRadius=.17f,BottomRadius=.17f,Height=.03f},Vector3.Up*.08f,"ccad60"));
        _food.Hide();_label.Font=new SystemFont{FontNames=new[]{"Microsoft YaHei"}};AddChild(_label);_navigation=new(Town);
    }
    public override void _ExitTree(){_navigation?.Dispose();}
    void Face(Vector3 direction){direction.Y=0;if(direction.LengthSquared()>.001f)Rotation=new(0,Mathf.Atan2(direction.X,direction.Z),0);}
    void ResetTask(string id){TaskId=id;_path.Clear();_index=0;_arrived=false;_working=0;_think=0;_stuck=0;Visual.Rotation=Vector3.Zero;}
    void Retry(string reason){Town.Game.RetryManagerTask(TaskId,reason);ResetTask("");Activity=reason;Velocity=Vector3.Zero;}
    public override void _PhysicsProcess(double delta)
    {
        if(Town.Paused)return;
        var game=Town.Game;if(!NativeGame.B(game.Meta,"farm_manager"))return;
        _animation+=delta;_think-=delta;_savePosition+=delta;_repath-=delta;
        if(_savePosition>=.5){game.Meta["manager_native_x"]=Position.X;game.Meta["manager_native_z"]=Position.Z;_savePosition=0;}
        if(_think<=0){game.RefreshManagerTasks();_think=.5;}
        JsonObject? job=game.CurrentManagerJob;
        string id=job==null?"":NativeGame.S(job,"job_id");if(id!=TaskId)ResetTask(id);
        if(job==null){Activity=NativeGame.S(game.Meta,"manager_status","待命");_label.Text="农场管理员 · "+Activity;Visual.Mesh=Town.Prototype("person-0");Visual.Rotation=Vector3.Zero;_food.Hide();_tool.Show();Velocity=Vector3.Zero;return;}
        bool feed=NativeGame.S(job,"kind")=="feed";_food.Visible=feed;_tool.Visible=!feed;
        var target=Town.FarmTaskTarget(job);
        if(target==null){Retry("目标暂不可达，稍后重试");return;}
        float reach=feed?1.65f:1.1f;float distance=Position.DistanceTo(target.Value);
        if(feed&&distance<4&&!_arrived)Town.FarmPet(NativeGame.S(job,"pet_id"))?.React("idle");
        if(_arrived&&distance>reach+.6f){_arrived=false;_working=0;_path.Clear();_think=0;}
        if(distance<=reach&&_navigation.CanInteract(Position,target.Value)){
            if(!_arrived){if(!game.ArriveManagerTask(TaskId)){ResetTask("");return;}_arrived=true;_working=0;}
            Face(target.Value-Position);Velocity=Vector3.Zero;_working+=delta;
            Activity=feed?"喂食中":"清扫中";game.Meta["manager_status"]=Activity;
            if(feed)Town.FarmPet(NativeGame.S(job,"pet_id"))?.React("eat");
            Visual.Mesh=Town.Prototype("person-0");Visual.Rotation=new(feed?-.12f:.15f+(float)Math.Sin(_animation*7)*.12f,0,0);
            _tool.Rotation=new(0,0,(float)Math.Sin(_animation*7)*.35f);
            if(_working>=(feed?2.5:3.5)){
                if(game.CompleteManagerTask(TaskId))CompletedVisits++;
                ResetTask("");
            }
        }else{
            Activity=feed?"前往喂食":"前往清扫";game.Meta["manager_status"]=Activity;
            if(_path.Count==0||(_lastTarget.DistanceTo(target.Value)>2&&_repath<=0)){
                _path=_navigation.Route(Position,target.Value,reach*.85f);_index=0;_lastTarget=target.Value;_repath=.8;
                if(_path.Count==0){Retry("通道被挡，等待清理");return;}
            }
            while(_index<_path.Count&&Position.DistanceTo(_path[_index])<.18f)_index++;
            if(_index>=_path.Count){_path.Clear();return;}
            var direction=(_path[_index]-Position);direction.Y=0;direction=direction.Normalized();
            var before=Position;Velocity=direction*2.8f+Vector3.Down*2;MoveAndSlide();Position=new(Position.X,Math.Max(0,Position.Y),Position.Z);
            var travelled=Position-before;travelled.Y=0;WalkedDistance+=travelled.Length();
            if(travelled.Length()<(float)delta*.25f)_stuck+=delta;else _stuck=Math.Max(0,_stuck-delta*.5);
            if(_stuck>10){Retry("通道拥堵，稍后重试");return;}
            if(_stuck>2&&_repath<=0){_navigation.Invalidate();_path.Clear();_repath=.8;}
            Face(travelled);Visual.Rotation=Vector3.Zero;_tool.Rotation=Vector3.Zero;
            Visual.Mesh=Town.Prototype("person-"+((int)(_animation*7)%4));
        }
        _label.Text="农场管理员 · "+Activity;
    }
}
