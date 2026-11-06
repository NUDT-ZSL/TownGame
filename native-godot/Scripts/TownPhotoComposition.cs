using Godot;
using System;

public partial class TownPhotoGrid : Control
{
    internal Vector2I Aspect {get;set;}
    internal bool Thirds {get;set;}=true;

    internal static Rect2 Frame(Vector2 size,Vector2I aspect)
    {
        if(aspect.X<=0||aspect.Y<=0||size.X<=0||size.Y<=0)return new(Vector2.Zero,size);
        float unit=Math.Min(size.X/aspect.X,size.Y/aspect.Y);
        var framed=(Vector2)aspect*unit;
        return new((size-framed)*.5f,framed);
    }

    internal static Rect2I Crop(Vector2I size,Vector2I aspect)
    {
        if(aspect.X<=0||aspect.Y<=0)return new(Vector2I.Zero,size);
        int units=Math.Min(size.X/aspect.X,size.Y/aspect.Y);
        if(units<1)return new(Vector2I.Zero,size);
        var framed=aspect*units;
        return new((size-framed)/2,framed);
    }

    public override void _Draw()
    {
        var frame=Frame(Size,Aspect);
        if(Aspect!=Vector2I.Zero){
            Color mask=new(0,0,0,.48f);
            DrawRect(new Rect2(0,0,Size.X,frame.Position.Y),mask);
            DrawRect(new Rect2(0,frame.End.Y,Size.X,Math.Max(0,Size.Y-frame.End.Y)),mask);
            DrawRect(new Rect2(0,frame.Position.Y,frame.Position.X,frame.Size.Y),mask);
            DrawRect(new Rect2(frame.End.X,frame.Position.Y,Math.Max(0,Size.X-frame.End.X),frame.Size.Y),mask);
            DrawRect(frame,new Color(1,.98f,.92f,.86f),false,1);
        }
        if(!Thirds)return;
        for(int line=1;line<3;line++){
            float x=frame.Position.X+frame.Size.X*line/3,y=frame.Position.Y+frame.Size.Y*line/3;
            Guide(new(x,frame.Position.Y),new(x,frame.End.Y));
            Guide(new(frame.Position.X,y),new(frame.End.X,y));
        }
    }

    void Guide(Vector2 from,Vector2 to)
    {
        DrawLine(from,to,new Color(0,0,0,.45f),2);
        DrawLine(from,to,new Color(1,.98f,.92f,.67f),1);
    }
}

public partial class Main
{
    PanelContainer? _photoToolPanel,_photoTitleCard;
    Label? _photoTitle;
    TownPhotoGrid? _photoGrid;
    PetAgent? _photoPosingPet;
    bool _photoGridOn=true,_photoDateOn,_photoNameOn,_photoLookAt;
    int _photoAspectIndex;
    static readonly (string Name,Vector2I Aspect)[] PhotoAspects={
        ("原画面",Vector2I.Zero),("宽幅 16:9",new(16,9)),("方形 1:1",Vector2I.One),("竖幅 3:4",new(3,4))
    };
    Vector2I PhotoAspect=>PhotoAspects[_photoAspectIndex].Aspect;

    void CreatePhotoComposition(Container body)
    {
        _photoGrid=new TownPhotoGrid{Name="TownPhotoGrid",MouseFilter=Control.MouseFilterEnum.Ignore,ZIndex=-2};
        _photoLayer!.AddChild(_photoGrid);
        _photoGrid.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _photoTitleCard=new PanelContainer{Name="TownPhotoCaption",MouseFilter=Control.MouseFilterEnum.Ignore,Visible=false,
            Theme=_panel.Theme,CustomMinimumSize=new(120,42),ClipContents=true};
        _photoTitleCard.AddThemeStyleboxOverride("panel",TownTheme.Box("fbf3e3eb",10,10));
        _photoLayer.AddChild(_photoTitleCard);
        _photoTitle=new Label{MouseFilter=Control.MouseFilterEnum.Ignore,HorizontalAlignment=HorizontalAlignment.Center,
            TextOverrunBehavior=TextServer.OverrunBehavior.TrimEllipsis};
        _photoTitleCard.AddChild(_photoTitle);
        _photoGrid.Resized+=RefreshPhotoFraming;

        var formats=new HFlowContainer();formats.AddThemeConstantOverride("h_separation",8);body.AddChild(formats);
        MarketLabel(formats,"成片比例").VerticalAlignment=VerticalAlignment.Center;
        var ratio=MarketOptions(formats,Array.ConvertAll(PhotoAspects,format=>format.Name),_photoAspectIndex);
        ratio.Name="TownPhotoAspect";
        ratio.TooltipText="只保存取景框内的内容，辅助线和遮罩不会出现在成片中。";
        ratio.ItemSelected+=index=>{_photoAspectIndex=(int)index;RefreshPhotoFraming();RefreshPhotoContext();PositionPhotoTools();};
        var options=new HFlowContainer();options.AddThemeConstantOverride("h_separation",9);body.AddChild(options);
        Toggle("TownPhotoThirds","三分线",_photoGridOn,value=>{_photoGridOn=value;RefreshPhotoFraming();});
        Toggle("TownPhotoDate","游戏日期",_photoDateOn,value=>{_photoDateOn=value;RefreshPhotoTitle();});
        Toggle("TownPhotoName","伙伴名字",_photoNameOn,value=>{_photoNameOn=value;RefreshPhotoTitle();});
        Toggle("TownPhotoLookAt","看向镜头",_photoLookAt,value=>{_photoLookAt=value;UpdateTownPhotoPose();});

        void Toggle(string name,string caption,bool active,Action<bool> change)
        {
            var button=new CheckButton{Name=name,Text=caption,ButtonPressed=active};
            button.Toggled+=value=>change(value);
            options.AddChild(button);
        }
    }

    void RefreshPhotoFraming()
    {
        if(_photoGrid==null)return;
        _photoGrid.Aspect=PhotoAspect;
        _photoGrid.Thirds=_photoGridOn;
        _photoGrid.Visible=!_takingPhoto&&(_photoGridOn||PhotoAspect!=Vector2I.Zero);
        _photoGrid.QueueRedraw();
        var frame=TownPhotoGrid.Frame(_photoGrid.Size,PhotoAspect);
        if(frame.Size.X<=0||frame.Size.Y<=0)return;
        var card=_photoTitleCard!;
        card.Size=new(Math.Clamp(frame.Size.X-28,120,284),Math.Max(42,card.GetCombinedMinimumSize().Y));
        card.Position=frame.End-card.Size-new Vector2(14,14);
    }

    void RefreshPhotoTitle()
    {
        if(_photoTitleCard==null)return;
        string name=_photoNameOn&&FarmPet(_selectedPet)!=null&&Data.FindPet(_selectedPet) is { } pet?NativeGame.S(pet,"name"):"";
        double start=NativeGame.D(Game.Meta,"start_ts");
        string date=_photoDateOn?$"第 {1+(int)Math.Max(0,(Game.Now-start)/NativeGame.Day)} 天 · {GameClockText}":"";
        _photoTitle!.Text=name.Length==0?date:date.Length==0?name:name+"  ·  "+date;
        _photoTitleCard.Visible=_photoTitle.Text.Length>0;
        RefreshPhotoFraming();
    }

    void UpdateTownPhotoPose()
    {
        var pet=InPhotoMode&&_photoLookAt?FarmPet(_selectedPet):null;
        if(_photoPosingPet!=pet&&GodotObject.IsInstanceValid(_photoPosingPet))_photoPosingPet!.SetPhotoFacing(null);
        _photoPosingPet=pet;
        pet?.SetPhotoFacing(_camera.GlobalPosition);
    }
}
