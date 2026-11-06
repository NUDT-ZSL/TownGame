using Godot;
using System;
using System.Linq;

public partial class Main
{
    static readonly (string Phase,string Caption)[] FreightJourneyStages={
        ("boarding","司机上车"),("to_source","驶往装货"),("loading","装货"),
        ("to_destination","驶往交付"),("unloading","卸货交付"),("to_depot","返回庄园")
    };
    bool _freightReceiptsExpanded;

    Action<string,string,string> FreightJourneyControls(Container parent)
    {
        var phases=AutonomyLinks(parent);
        phases.Name="TownFreightJourneyStages";
        var chips=FreightJourneyStages.Select(stage=>{
            var chip=MarketLabel(phases,stage.Caption,13,TownTheme.Muted);
            chip.Name=$"TownFreightJourneyStage_{stage.Phase}";
            return chip;
        }).ToArray();
        var progressText=MarketLabel(parent,"",14,TownTheme.Muted,wrap:true);
        progressText.Name="TownFreightJourneyProgressText";
        var progress=new ProgressBar{Name="TownFreightJourneyProgress",MinValue=0,MaxValue=1,Step=0,ShowPercentage=false,
            CustomMinimumSize=new(0,7)};
        progress.AddThemeStyleboxOverride("background",TownTheme.Box("dce1e2",4,0));
        progress.AddThemeStyleboxOverride("fill",TownTheme.Box("789ead",4,0));
        parent.AddChild(progress);
        var stops=AutonomyLinks(parent);
        string[] stopZones={"estate","","market"};
        string[] captions={"定位回库点","定位装货点","定位交付点"};
        var buttons=captions.Select((caption,index)=>{
            var button=MarketButton(stops,caption,()=>{
                string zone=stopZones[index];
                if(FreightSceneStop(zone).Cell is Vector2I cell)
                    FocusFreightPoint(WorldData.Cell(cell),$"{AutonomyZoneName(zone)} · {caption[2..]} · 道路格 {cell.X}, {cell.Y}");
            },expand:false,accent:"地图");
            button.Name=$"TownFreightStop{index}";
            return button;
        }).ToArray();
        var routeInfo=MarketLabel(parent,"",14,TownTheme.Muted,wrap:true);
        routeInfo.Name="TownFreightStopSummary";
        string previousSource="",previousDestination="";
        long previousRevision=-1;
        int previousRoadRevision=-1;

        return (phase,source,destination)=>{
            for(int index=0;index<chips.Length;index++){
                var (stage,caption)=FreightJourneyStages[index];
                bool active=phase==stage;
                chips[index].Text=$"{(active?"● ":"")}{index+1}. {caption}";
                chips[index].AddThemeColorOverride("font_color",active?TownTheme.Leaf:TownTheme.Muted);
            }
            var current=FreightJourneyProgress(phase);
            progressText.Visible=progress.Visible=current.HasValue;
            if(current is { } step){progressText.Text=step.Caption;progress.Value=step.Ratio;}
            if(previousSource==source&&previousDestination==destination&&previousRevision==Game.WorldRevision&&previousRoadRevision==_roadTopologyVersion)return;
            previousSource=source;previousDestination=destination;previousRevision=Game.WorldRevision;previousRoadRevision=_roadTopologyVersion;
            stopZones[1]=source;stopZones[2]=destination;
            var reasons=new string[stopZones.Length];
            for(int index=0;index<stopZones.Length;index++){
                string zone=stopZones[index];
                var stop=FreightSceneStop(zone);
                buttons[index].Disabled=stop.Cell==null;
                buttons[index].TooltipText=stop.Cell is Vector2I cell?$"{AutonomyZoneName(zone)} · 道路格 {cell.X}, {cell.Y}":stop.Reason;
                reasons[index]=stop.Cell.HasValue?"":$"{captions[index][2..]} · {stop.Reason}";
            }
            routeInfo.Text=string.Join("\n",reasons.Where(reason=>reason.Length>0));
            routeInfo.Visible=routeInfo.Text.Length>0;
        };
    }

    (double Ratio,string Caption)? FreightJourneyProgress(string phase)
    {
        if(phase is "loading" or "unloading"){
            double worked=Math.Clamp(_freightWork,0,FreightDockWorkSeconds);
            return (worked/FreightDockWorkSeconds,$"{(phase=="loading"?"装货":"卸货")}进度 {worked:0.0} / {FreightDockWorkSeconds:0} 现实秒");
        }
        RoadAgent? mover=phase=="boarding"?DirectoryActor(NativeGame.S(Game.FreightState,"driver_id")):
            (phase is "to_source" or "to_destination" or "to_depot")&&_freightRoutePhase==phase&&_freightHasRoute?_freightTruck:null;
        if(mover==null||!GodotObject.IsInstanceValid(mover)||_routeRefreshPending.Contains(mover)||mover.Path.Count==0)return null;
        int passed=Math.Clamp(mover.Index,0,mover.Path.Count);
        return (passed/(double)mover.Path.Count,$"{(phase=="boarding"?"步行至驾驶室":"本段行车")} · 已经过 {passed} / {mover.Path.Count} 个路径点");
    }

    (Vector2I? Cell,string Reason) FreightSceneStop(string zone)
    {
        if(zone.Length==0)return (null,"尚未选定生产街区");
        if(!Data.Portals.ContainsKey(zone))return (null,"该街区缺少停靠位置");
        if(!Game.Effective(zone))return (null,AutonomyRoadReadiness(zone));
        var cell=FreightStop(zone);
        return Data.Roads.ContainsKey(cell)?(cell,""):(null,"该街区缺少可停靠道路");
    }

    void FocusFreightPoint(Vector3 position,string message)
    {
        RememberCameraSite(position);
        StopPetFollow();StopResidentFollow();EndPlacement();
        _portraitAnchor=null;_buildCamera=false;
        _center=position;_distance=35;
        _selection=message;
        _management?.Hide();
        UpdateCamera();
    }

    Action FreightReceiptControls(Container parent)
    {
        var card=MarketCard(parent,color:"edf1e8");
        var toggle=new CheckButton{Name="TownFreightReceiptToggle",ButtonPressed=_freightReceiptsExpanded};
        TownTheme.AccentButton(toggle,"自治",6);
        card.AddChild(toggle);
        var body=new VBoxContainer{Name="TownFreightReceiptRows",Visible=_freightReceiptsExpanded};
        card.AddChild(body);
        NativeGame.FreightReceipt[] previous=Array.Empty<NativeGame.FreightReceipt>();
        toggle.Toggled+=open=>{_freightReceiptsExpanded=open;body.Visible=open;Refresh();};
        return Refresh;

        void Refresh()
        {
            var receipts=Game.FreightReceipts();
            toggle.Text=$"最近交付回执 · {receipts.Length} 条";
            if(!body.Visible||body.GetChildCount()>0&&previous.SequenceEqual(receipts))return;
            previous=receipts;
            foreach(var row in body.GetChildren()){body.RemoveChild(row);row.QueueFree();}
            if(receipts.Length==0)MarketLabel(body,"暂无交付回执。居民司机实际完成卸货后会在这里留下记录。",14,TownTheme.Muted,wrap:true);
            foreach(var receipt in receipts){
                var entry=MarketCard(body,color:"f6f6ed");
                entry.GetParent().Name=$"TownFreightReceipt_{receipt.Trip}";
                MarketLabel(entry,$"第 {receipt.Trip} 趟 · {TownProjectClock(receipt.When)} · {receipt.Driver}",15,TownTheme.Ink,wrap:true);
                MarketLabel(entry,$"{AutonomyZoneName(receipt.Source)} → {AutonomyZoneName(receipt.Destination)} · {AutonomyResource(receipt.Item)} {receipt.Count} 份 · 公共收入 +{receipt.Gold:N0} 金币",
                    14,TownTheme.Muted,wrap:true);
            }
        }
    }
}
