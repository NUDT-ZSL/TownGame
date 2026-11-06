using Godot;
using System;
using System.Linq;
using System.Text.Json.Nodes;

public partial class Main
{
    void AddPetHealthTimeline(Func<JsonNode> currentPet)
    {
        var card=MarketCard(_managementBody!,color:"f0eee3");
        var panel=card.GetParent<PanelContainer>();
        panel.Name="PetHealthTimeline";
        if(_managementEntryTarget=="care:health")_managementFocusTarget=panel;
        var header=new HFlowContainer();
        header.AddThemeConstantOverride("h_separation",6);
        card.AddChild(header);
        MarketLabel(header,"健康记录",17,TownTheme.Accent("照护")).VerticalAlignment=VerticalAlignment.Center;
        var days=MarketOptions(header,new[]{"最近 3 游戏日","最近 7 游戏日"},1);
        days.Name="PetHealthRange";
        var summary=MarketLabel(card,"",14,TownTheme.Ink,wrap:true);
        summary.Name="PetHealthSummary";
        var values=MarketLabel(card,"",14,TownTheme.Muted,wrap:true);
        values.Name="PetHealthValues";
        var warning=MarketLabel(card,"",14,TownTheme.Accent("事件"),wrap:true);
        days.ItemSelected+=_=>Refresh();
        void Refresh()
        {
            var pet=currentPet();
            var history=Game.PetHealthTimeline(pet);
            var current=history[^1];
            var samples=history.Where(sample=>sample.Day>=current.Day-(days.Selected==0?2:6)).ToArray();
            summary.Text=samples.Length>1
                ?$"已记录 {samples.Length} 天 · 最低健康 {samples.Min(sample=>sample.Health):0} · 较第 {samples[0].Day:0} 天 {current.Health-samples[0].Health:+0.0;-0.0;0.0}"
                :"这个时段暂无之前的记录。下方为当前状态。";
            values.Text=string.Join("\n",samples.Select(sample=>
                $"第 {sample.Day:0} 天{(sample.Day==current.Day?" · 当前":"")}   健康 {sample.Health:0}   饱食 {sample.Hunger:0}   清洁 {sample.Cleanliness:0}"));
            var risks=Game.PetCareRisks(pet).ToArray();
            warning.Text=risks.Length==0?"当前没有额外照护提醒":"建议留意 · "+string.Join(" · ",risks);
        }
        Refresh();
        _refreshManagementPage+=Refresh;
    }

    Action BuildIncubatorOverview(Container parent)
    {
        var card=MarketCard(parent,color:"f7eddf");
        if(_managementEntryTarget=="incubator")_managementFocusTarget=card.GetParent<Control>();
        var heading=new HBoxContainer();
        card.AddChild(heading);
        MarketLabel(heading,"孵化台",17,TownTheme.Accent("宠物")).SizeFlagsHorizontal=Control.SizeFlags.ExpandFill;
        MarketButton(heading,"场景定位",LocateIncubator,expand:false,accent:"宠物");
        MarketLabel(card,"购买宠物蛋会预留育幼床位，倒计时结束后自动入住。",color:TownTheme.Muted,wrap:true);
        var status=MarketLabel(card,"",14,TownTheme.Muted,wrap:true);
        void Refresh()
        {
            var eggs=Game.IncubatorEggs();
            var lines=eggs.Select(egg=>
                $"{egg.Species}蛋 · {Math.Clamp((Game.Now-egg.Start)/Math.Max(1,egg.Due-egg.Start),0,1):P0} · 约 {Math.Max(0,(egg.Due-Game.Now)/NativeGame.Hour):0.0} 小时 · 预留地块 {egg.Home}");
            var last=Game.RecentHatches().FirstOrDefault();
            string summary=$"台位 {eggs.Length}/{NativeGame.IncubatorCapacity}"+(eggs.Length==0?" · 当前无待孵化宠物蛋":"\n"+string.Join("\n",lines));
            if(last!=null)summary+="\n最近破壳 · "+NativeGame.S(last,"name");
            status.Text=summary;
        }
        Refresh();
        return Refresh;
    }

    static readonly Vector3 IncubatorPoint=PetActivityCenter+new Vector3(20,0,7.1f);
    Node3D? _incubatorModel;
    string _incubatorVisualKey="";
    void LocateIncubator()
    {
        StopPetFollow();
        StopResidentFollow();
        _center=IncubatorPoint;_distance=32;
        _management?.Hide();
    }

    void RefreshIncubatorModel()
    {
        var eggs=Game.IncubatorEggs();
        string key=string.Join("|",eggs.Select(egg=>egg.Id));
        if(_incubatorModel!=null&&key==_incubatorVisualKey)return;
        _incubatorVisualKey=key;
        _incubatorModel?.QueueFree();
        var mesh=new DistrictAccentMesh();
        mesh.Box(new(0,.62f,0),new(3.2f,.25f,1.35f),"aa7a53");
        foreach(float x in new[]{-1.35f,1.35f})mesh.Box(new(x,.28f,0),new(.16f,.59f,.76f),"7b5a43");
        for(int i=0;i<NativeGame.IncubatorCapacity;i++){
            float x=(i-1)*.96f;
            mesh.Round(new(x,.8f,0),new(.77f,.22f,.79f),"cf9e69");
            if(i<eggs.Length){
                mesh.Round(new(x,1.05f,0),new(.42f,.55f,.41f),i==1?"cbe2d1":"f9e0b0");
                mesh.Round(new(x-.09f,1.12f,.18f),new(.10f,.10f,.045f),"d29175");
            }
        }
        var station=new MeshInstance3D{Name="EstateIncubator",Position=IncubatorPoint,Mesh=mesh.Finish()};
        AddChild(station);
        _incubatorModel=station;
        var hit=new StaticBody3D{CollisionLayer=8,CollisionMask=0};
        hit.SetMeta("kind","incubator");
        hit.SetMeta("ident","estate");
        station.AddChild(hit);
        hit.AddChild(new CollisionShape3D{Position=new(0,.68f,0),Shape=new BoxShape3D{Size=new(3.2f,1.36f,1.35f)}});
        station.AddChild(new Label3D{Position=Vector3.Up*1.55f,Text=$"孵化台 · {eggs.Length}/{NativeGame.IncubatorCapacity}",
            Billboard=BaseMaterial3D.BillboardModeEnum.Enabled,FontSize=30,PixelSize=.018f});
    }
}
