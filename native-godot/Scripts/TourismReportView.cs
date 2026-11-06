using Godot;
using System;
using System.Linq;

public partial class Main
{
    const int TouristPageSize=5;
    int _tourismFilter,_tourismSort,_touristPage;
    string _tourismAttraction="";
    bool _tourismItinerariesOpen,_tourismReceiptsOpen;

    Action TourismReportControls(Container parent)
    {
        var header=Section("游客游览与收入","到达景点后结算消费")!;
        var heading=header.GetParent<Control>().GetParent<Control>();
        heading.Name="TownTourismHeading";
        if(_managementEntryTarget=="tourism")_managementFocusTarget=heading;
        var card=MarketCard(parent,color:"e9eee1");
        var totals=MarketLabel(card,"",17,TownTheme.Ink,wrap:true);
        totals.Name="TownTourismTotals";
        var live=MarketLabel(card,"",14,TownTheme.Muted,wrap:true);
        live.Name="TownTourismLive";
        var progress=MarketLabel(card,"",14,TownTheme.Muted,wrap:true);
        var arrivalRate=new ProgressBar{Name="TownTourismArrivalRate",MinValue=0,MaxValue=1,Step=0,ShowPercentage=false,
            CustomMinimumSize=new(0,8),MouseFilter=Control.MouseFilterEnum.Ignore};
        arrivalRate.AddThemeStyleboxOverride("background",TownTheme.Box("dce4d7",4,0));
        arrivalRate.AddThemeStyleboxOverride("fill",TownTheme.Box("6e987e",4,0));
        card.AddChild(arrivalRate);

        var choices=AutonomyLinks(card);
        var status=MarketOptions(choices,new[]{"全部景点","开放且连通","未开放或断路","有游览记录"},_tourismFilter);
        status.Name="TownTourismFilter";
        status.AccessibilityName="按景点开放状态或游览记录筛选";
        TownTheme.AccentButton(status,"自治",6);
        var sort=MarketOptions(choices,new[]{"到达次数优先","收入优先","计划次数优先"},_tourismSort);
        sort.Name="TownTourismSort";
        sort.AccessibilityName="景点排序";
        TownTheme.AccentButton(sort,"自治",6);
        var attraction=MarketOptions(choices,Array.Empty<string>(),-1);
        attraction.Name="TownTourismAttraction";
        attraction.AccessibilityName="选择景点";
        TownTheme.AccentButton(attraction,"自治",6);
        var empty=MarketLabel(card,"暂无符合筛选条件的景点。",14,TownTheme.Muted,wrap:true);
        var detail=MarketCard(card,color:"fbf8ed");
        var availability=MarketLabel(detail,"",16,TownTheme.Ink,wrap:true);
        availability.Name="TownTourismEligibility";
        var records=MarketLabel(detail,"",14,TownTheme.Muted,wrap:true);
        var blocked=MarketLabel(detail,"",14,TownTheme.Muted,wrap:true);
        blocked.Name="TownTourismBlocks";
        var actions=AutonomyLinks(detail);
        var map=MarketButton(actions,"地图查看街区",()=>OpenAutonomyDistrict(_tourismAttraction),expand:false,accent:"地图");
        map.Name="TownTourismMap";
        var scene=MarketButton(actions,"定位街区入口",()=>{
            if(Data.Portals.TryGetValue(_tourismAttraction,out var portal))
                FocusResidentSite(WorldData.Cell(portal),58,$"{AutonomyZoneName(_tourismAttraction)} · 街区入口");
        },expand:false,accent:"地图");
        scene.Name="TownTourismScene";
        var refreshItineraries=TourismItineraryControls(card);
        var refreshReceipts=TourismReceiptControls(card);
        (string Zone,string Caption)[] shown=Array.Empty<(string,string)>();
        status.ItemSelected+=index=>{_tourismFilter=(int)index;Refresh();};
        sort.ItemSelected+=index=>{_tourismSort=(int)index;Refresh();};
        attraction.ItemSelected+=index=>{_tourismAttraction=shown[(int)index].Zone;Refresh();};

        Refresh();
        return Refresh;

        void Refresh()
        {
            var reports=Game.TourismReports();
            var visitors=Game.TouristItineraries();
            long planned=reports.Sum(report=>(long)report.Planned),arrived=reports.Sum(report=>(long)report.Arrived);
            totals.Text=$"累计计划 {planned} 次 · 到达 {arrived} 次 · 收入 {reports.Sum(report=>(long)report.Coins)} 金币";
            int OnStage(NativeGame.TouristStage stage)=>visitors.Count(visitor=>visitor.Stage==stage);
            live.Text=$"当前 · 在途 {OnStage(NativeGame.TouristStage.EnRoute)} · 受阻 {OnStage(NativeGame.TouristStage.Blocked)} · 参观 {OnStage(NativeGame.TouristStage.Visiting)} · 返程 {OnStage(NativeGame.TouristStage.Leaving)}";
            arrivalRate.Visible=planned>0;
            arrivalRate.Value=planned==0?0:Math.Clamp(arrived/(double)planned,0,1);
            progress.Text=planned==0?"还没有已登记的游览计划。":$"累计到达率 {arrivalRate.Value:P0} · 按已登记计划统计";

            var destinations=NativeGame.TouristDestinations.Select(zone=>{
                var report=reports.FirstOrDefault(report=>report.Zone==zone);
                return (Zone:zone,Issue:Game.TouristDestinationReason(zone),Report:report);
            }).ToArray();
            var filtered=destinations.Where(destination=>_tourismFilter switch{
                1=>destination.Issue.Length==0,2=>destination.Issue.Length>0,3=>destination.Report.Planned>0,_=>true
            }).OrderByDescending(destination=>_tourismSort switch{
                1=>destination.Report.Coins,2=>destination.Report.Planned,_=>destination.Report.Arrived
            }).ToArray();
            var menu=filtered.Select(destination=>{
                string metric=_tourismSort switch{1=>$"{destination.Report.Coins} 金币",2=>$"计划 {destination.Report.Planned} 次",_=>$"到达 {destination.Report.Arrived} 次"};
                return (destination.Zone,$"{AutonomyZoneName(destination.Zone)} · {metric}");
            }).ToArray();
            if(!shown.SequenceEqual(menu)){
                shown=menu;
                attraction.Clear();
                foreach(var option in shown)attraction.AddItem(option.Caption);
            }
            int index=Array.FindIndex(shown,option=>option.Zone==_tourismAttraction);
            if(index<0&&shown.Length>0)index=0;
            attraction.Select(index);
            attraction.Disabled=empty.Visible=index<0;
            detail.GetParent<Control>().Visible=index>=0;
            if(index>=0){
                var selected=filtered[index];
                _tourismAttraction=selected.Zone;
                var report=selected.Report;
                string condition=selected.Issue.Length==0?"开放且道路连通":selected.Issue;
                availability.Text=$"{AutonomyZoneName(selected.Zone)} · {condition}";
                availability.AddThemeColorOverride("font_color",selected.Issue.Length==0?TownTheme.Leaf:new Color("873d31"));
                records.Text=$"计划 {report.Planned} 次 · 到达 {report.Arrived} 次 · 收入 {report.Coins} 金币";
                blocked.Text=$"未成行 · 未开放 {report.Closed} · 道路受阻 {report.Disconnected} · 其他停留到期 {report.Timeouts}";
                scene.Disabled=!Data.Portals.ContainsKey(selected.Zone);
            }
            refreshItineraries(visitors);
            refreshReceipts();
        }
    }

    Action<NativeGame.TouristItinerary[]> TourismItineraryControls(Container parent)
    {
        var card=MarketCard(parent,color:"f6f3e8");
        var toggle=new CheckButton{Name="TownTourismItineraries",ButtonPressed=_tourismItinerariesOpen};
        TownTheme.AccentButton(toggle,"自治",6);
        card.AddChild(toggle);
        var section=new VBoxContainer{Visible=_tourismItinerariesOpen};
        card.AddChild(section);
        var pager=AutonomyLinks(section);
        var back=new Button{Name="TownTourismPrevious",Text="上一页"};
        TownTheme.AccentButton(back,"自治",6);pager.AddChild(back);
        var page=MarketLabel(pager,"",14,TownTheme.Muted);
        var next=new Button{Name="TownTourismNext",Text="下一页"};
        TownTheme.AccentButton(next,"自治",6);pager.AddChild(next);
        var rows=new VBoxContainer{Name="TownTourismItineraryRows"};
        section.AddChild(rows);
        var latest=Array.Empty<NativeGame.TouristItinerary>();
        (NativeGame.TouristItinerary Visitor,string Timing,bool CanLocate)[]? rendered=null;
        back.Pressed+=()=>ChangePage(-1);
        next.Pressed+=()=>ChangePage(1);
        toggle.Toggled+=open=>{_tourismItinerariesOpen=open;section.Visible=open;Draw();};
        return visitors=>{latest=visitors;toggle.Text=$"当前旅人 · {visitors.Length} 位";Draw();};

        void ChangePage(int offset){_touristPage+=offset;Draw();}

        void Draw()
        {
            if(!section.Visible)return;
            int pages=(latest.Length+TouristPageSize-1)/TouristPageSize;
            _touristPage=Math.Clamp(_touristPage,0,Math.Max(0,pages-1));
            back.Disabled=_touristPage==0;next.Disabled=_touristPage+1>=pages;
            page.Text=pages==0?"0 / 0 页":$"第 {_touristPage+1} / {pages} 页";
            var visible=latest.Skip(_touristPage*TouristPageSize).Take(TouristPageSize).Select(visitor=>(Visitor:visitor,
                Timing:visitor.Until>Game.Now?DeadlineText(visitor.Until-Game.Now):"即将结束",
                CanLocate:DirectoryActor(visitor.Id) is { } actor&&IsVisibleSceneResident(actor))).ToArray();
            if(rendered!=null&&rendered.SequenceEqual(visible))return;
            rendered=visible;
            foreach(var row in rows.GetChildren()){rows.RemoveChild(row);row.QueueFree();}
            if(visible.Length==0)MarketLabel(rows,"暂无在镇旅人。",14,TownTheme.Muted);
            foreach(var (visitor,timing,canLocate) in visible){
                var entry=MarketCard(rows);
                entry.GetParent().Name=$"TownTourist_{visitor.Id}";
                string activity=visitor.Stage switch{
                    NativeGame.TouristStage.Visiting=>"已到达 · 参观中",NativeGame.TouristStage.Blocked=>"在途受阻",
                    NativeGame.TouristStage.Leaving=>"前往车站返程",_=>"前往景点"
                };
                MarketLabel(entry,$"{visitor.Name} · {activity}",16,TownTheme.Ink,wrap:true);
                string deadline=visitor.Stage==NativeGame.TouristStage.Leaving?"返程":"停留";
                string destination=Data.Portals.ContainsKey(visitor.Destination)?AutonomyZoneName(visitor.Destination):"未知地点";
                MarketLabel(entry,$"目的地 {destination} · {deadline} {timing}",14,TownTheme.Muted,wrap:true);
                if(visitor.Issue.Length>0)MarketLabel(entry,visitor.Issue,14,new Color("873d31"),wrap:true);
                var locate=MarketButton(entry,"定位旅人",()=>{
                    if(DirectoryActor(visitor.Id) is { } current&&IsVisibleSceneResident(current))
                        FocusResidentSite(current.Position,24,$"{visitor.Name} · {activity} · {destination}");
                    else SetManagementFeedback("这位旅人的场景位置暂不可用。",true);
                },expand:false,accent:"地图");
                locate.Name="TownTouristLocate";
                locate.Disabled=!canLocate;
                locate.TooltipText=canLocate?"跳到旅人当前所在位置":"旅人的场景位置暂不可用";
            }
        }
    }

    Action TourismReceiptControls(Container parent)
    {
        var toggle=new CheckButton{Name="TownTourismReceipts",ButtonPressed=_tourismReceiptsOpen};
        TownTheme.AccentButton(toggle,"自治",6);
        parent.AddChild(toggle);
        var rows=new VBoxContainer{Name="TownTourismReceiptRows",Visible=_tourismReceiptsOpen};
        parent.AddChild(rows);
        NativeGame.TouristReceipt[]? rendered=null;
        toggle.Toggled+=open=>{_tourismReceiptsOpen=open;rows.Visible=open;Refresh();};
        return Refresh;

        void Refresh()
        {
            var receipts=Game.TouristReceipts();
            toggle.Text=$"最近到达明细 · {receipts.Length} 次";
            if(!rows.Visible||rendered!=null&&rendered.SequenceEqual(receipts))return;
            rendered=receipts;
            foreach(var row in rows.GetChildren()){rows.RemoveChild(row);row.QueueFree();}
            if(receipts.Length==0)MarketLabel(rows,"暂无带明细的到达记录。",14,TownTheme.Muted);
            foreach(var receipt in receipts){
                var entry=MarketCard(rows,color:"f9f6ed");
                MarketLabel(entry,$"{receipt.Name} → {AutonomyZoneName(receipt.Zone)} · +{receipt.Coins} 金币",15,TownTheme.Ink,wrap:true);
                MarketLabel(entry,$"{TownProjectClock(receipt.ArrivedAt)} 到达",14,TownTheme.Muted);
            }
        }
    }
}
