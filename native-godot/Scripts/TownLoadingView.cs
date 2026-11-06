using Godot;
using System;
using System.Threading.Tasks;

public partial class Main
{
    CanvasLayer? _townLoading;
    Label? _townLoadingStage;
    ProgressBar? _townLoadingProgress;
    Action? _townLoadingClose;

    void BeginTownLoading()
    {
        _townLoading=new CanvasLayer{Name="TownLoading",Layer=30};AddChild(_townLoading);
        var backdrop=new ColorRect{Color=new("e6ebdf"),Theme=TownTheme.Create()};
        _townLoading.AddChild(backdrop);
        backdrop.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        var center=new CenterContainer();backdrop.AddChild(center);
        center.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        var card=new PanelContainer{CustomMinimumSize=new(385,0)};
        card.AddThemeStyleboxOverride("panel",TownTheme.Box("fff9ee",16,20));
        center.AddChild(card);
        var column=new VBoxContainer();column.AddThemeConstantOverride("separation",12);card.AddChild(column);
        MarketLabel(column,"慢时光 · 宠物小镇",25,TownTheme.Leaf);
        _townLoadingStage=MarketLabel(column,"准备打开小镇",16,TownTheme.Muted);
        _townLoadingProgress=new ProgressBar{MaxValue=100,ShowPercentage=false,CustomMinimumSize=new(0,9)};
        _townLoadingProgress.AddThemeStyleboxOverride("background",TownTheme.Box("e2ded0",4,0));
        _townLoadingProgress.AddThemeStyleboxOverride("fill",TownTheme.Box("759b79",4,0));
        column.AddChild(_townLoadingProgress);
        GetTree().AutoAcceptQuit=false;
        _townLoadingClose=()=>GetTree().Quit();
        GetWindow().CloseRequested+=_townLoadingClose;
    }

    async Task TownLoadStage(string text,double progress)
    {
        if(_townLoadingStage==null)return;
        _townLoadingStage.Text=text;
        _townLoadingProgress!.Value=progress;
        await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
    }

    void EndTownLoading()
    {
        if(_townLoadingClose!=null){GetWindow().CloseRequested-=_townLoadingClose;_townLoadingClose=null;}
        _townLoading?.Hide();
        _townLoading?.QueueFree();
        _townLoading=null;_townLoadingStage=null;_townLoadingProgress=null;
    }
}
