using Godot;
using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

public partial class Main
{
    CanvasLayer _townHudLayer=null!;
    CanvasLayer? _photoLayer;
    Label? _photoFeedback,_photoLensLabel,_photoContext;
    HSlider? _photoLens;
    Button? _photoResetLens,_photoSettingsButton,_photoCaptureButton;
    VBoxContainer? _photoSettings;
    bool _takingPhoto,_beforePhotoPaused;
    float _beforePhotoFov;
    bool InPhotoMode=>_photoLayer?.Visible==true;
    string PhotoRoot=>OS.GetCmdlineUserArgs().FirstOrDefault(arg=>arg.StartsWith("--photo-output=",StringComparison.Ordinal))?[15..]
        ??(UseTownPreferences()?TownPhotoSlotPath(_saveSlot):"");

    static string TownPhotoSlotPath(int slot)=>ProjectSettings.GlobalizePath(slot==1?"user://photos":$"user://photos/slot-{slot}");

    static string PetPhotoFolder(string root,string id)=>
        Path.Combine(root,"pets",Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(id))));

    static string NewPhotoPath(string folder,DateTimeOffset date)
    {
        string stem=date.ToString("yyyyMMdd-HHmmss-fff",System.Globalization.CultureInfo.InvariantCulture);
        string path=Path.Combine(folder,stem+".png");
        for(int suffix=2;File.Exists(path);suffix++)path=Path.Combine(folder,$"{stem}-{suffix}.png");
        return path;
    }

    void TogglePhotoMode()
    {
        if(InPhotoMode){LeavePhotoMode();return;}
        CancelTownPointer();
        StopResidentFollow();
        _management?.Hide();
        EndPlacement();
        _beforePhotoPaused=Paused;
        _beforePhotoFov=_camera.Fov;
        SetTownPaused(true);
        if(_photoLayer==null)CreatePhotoTools();
        _photoLens!.SetValueNoSignal(_camera.Fov);
        _photoLens.Editable=!_buildCamera;
        _photoLens.TooltipText=_buildCamera?"等距视角可用滚轮调整构图":"调整镜头视野";
        _photoResetLens!.Disabled=_buildCamera;
        _photoCaptureButton!.TooltipText=$"{TownBindingText("capture")} 保存当前取景";
        ShowPhotoFeedback("");
        RefreshPhotoLens();
        RefreshPhotoContext();
        _townHudLayer.Hide();
        _photoLayer!.Show();
        PositionPhotoTools();
        RefreshPhotoTitle();
        UpdateTownPhotoPose();
    }

    void LeavePhotoMode()
    {
        if(!InPhotoMode||_takingPhoto)return;
        CancelTownPointer();
        SetTownPaused(_beforePhotoPaused);
        _camera.Fov=_beforePhotoFov;
        _photoLayer!.Hide();
        UpdateTownPhotoPose();
        _townHudLayer.Show();
    }

    void CreatePhotoTools()
    {
        _photoLayer=new CanvasLayer{Layer=12,Visible=false};
        AddChild(_photoLayer);
        var frame=new PanelContainer{Name="TownPhotoTools",Theme=_panel.Theme};
        _photoToolPanel=frame;
        frame.AddThemeStyleboxOverride("panel",TownTheme.Box("f8f2e6",12,10));
        _photoLayer.AddChild(frame);
        FitTownHudCard(frame);
        var host=GetWindow();
        host.SizeChanged+=PositionPhotoTools;
        TreeExiting+=()=>host.SizeChanged-=PositionPhotoTools;
        var body=new VBoxContainer();
        body.AddThemeConstantOverride("separation",7);
        frame.AddChild(body);
        var row=new HFlowContainer();
        row.AddThemeConstantOverride("h_separation",7);
        body.AddChild(row);
        MarketLabel(row,"拍照模式",18,TownTheme.Leaf).VerticalAlignment=VerticalAlignment.Center;
        _photoCaptureButton=MarketButton(row,"拍照",()=>_=CaptureTownPhoto(),expand:false,accent:"地图");
        _photoCaptureButton.Name="TownPhotoCapture";
        _photoSettingsButton=MarketButton(row,"取景设置",()=>{
            _photoSettings!.Visible=!_photoSettings.Visible;
            _photoSettingsButton!.Text=_photoSettings.Visible?"收起设置":"取景设置";
            PositionPhotoTools();
        },expand:false,accent:"外观");
        _photoSettingsButton.Name="TownPhotoSettingsToggle";
        var exit=MarketButton(row,"退出取景",LeavePhotoMode,expand:false,accent:"存档");
        exit.TooltipText="Esc 退出拍照模式";
        _photoContext=MarketLabel(body,"",14,TownTheme.Muted,wrap:true);
        _photoContext.Name="TownPhotoContext";
        _photoFeedback=MarketLabel(body,"",color:TownTheme.Muted,wrap:true);
        _photoFeedback.Name="TownPhotoFeedback";
        _photoFeedback.Hide();
        _photoSettings=new VBoxContainer{Name="TownPhotoSettings",Visible=false};
        _photoSettings.AddThemeConstantOverride("separation",7);
        body.AddChild(_photoSettings);
        var lens=new HFlowContainer();
        lens.AddThemeConstantOverride("h_separation",7);
        _photoSettings.AddChild(lens);
        _photoLensLabel=MarketLabel(lens,"视野");
        _photoLensLabel.VerticalAlignment=VerticalAlignment.Center;
        _photoLens=new HSlider{Name="TownPhotoLens",MinValue=30,MaxValue=90,Step=1,
            CustomMinimumSize=new(122,0),SizeFlagsVertical=Control.SizeFlags.ShrinkCenter};
        _photoLens.ValueChanged+=value=>{_camera.Fov=(float)value;RefreshPhotoLens();};
        lens.AddChild(_photoLens);
        _photoResetLens=MarketButton(lens,"还原视野",()=>_photoLens.Value=_beforePhotoFov,expand:false,accent:"地图");
        CreatePhotoComposition(_photoSettings);
    }

    void PositionPhotoTools()
    {
        if(_photoToolPanel==null)return;
        var viewport=GetViewport().GetVisibleRect();
        _photoToolPanel.Position=viewport.Position+new Vector2(18,14);
        _photoToolPanel.Scale=Vector2.One*UiScale;
        float available=(viewport.Size.X-36)/UiScale;
        float side=(TownPhotoGrid.Frame(viewport.Size,PhotoAspect).Position.X-26)/UiScale;
        float width=side>=165?Math.Min(440,side):Math.Min(available<1100?310:440,available);
        _photoToolPanel.CustomMinimumSize=new(width,0);
        _photoToolPanel.ResetSize();
    }

    void RefreshPhotoLens()
    {
        if(_photoLensLabel!=null)_photoLensLabel.Text=_buildCamera?"等距视角":$"视野 {_photoLens!.Value:0}°";
    }

    void RefreshPhotoContext()
    {
        if(_photoContext==null)return;
        string subject=FarmPet(_selectedPet)!=null&&Data.FindPet(_selectedPet) is { } pet?
            $"伙伴 · {NativeGame.S(pet,"name")}":"小镇场景";
        _photoContext.Text=$"{subject} · {PhotoAspects[_photoAspectIndex].Name}\n左键拖动 · 右键转动 · 滚轮缩放 · {TownBindingText("capture")} 拍照";
    }

    async Task CaptureTownPhoto()
    {
        string root=PhotoRoot;
        if(_takingPhoto||DisplayServer.GetName()=="headless")return;
        if(root.Length==0){
            if(InPhotoMode)ShowPhotoFeedback("当前预览没有设置照片保存位置。");
            return;
        }
        _takingPhoto=true;
        bool framed=InPhotoMode;
        Vector2I aspect=framed?PhotoAspect:Vector2I.Zero;
        if(framed){_photoToolPanel!.Hide();RefreshPhotoTitle();}
        string id=FarmPet(_selectedPet)!=null?_selectedPet:"";
        try{
            await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
            string folder=id.Length>0?PetPhotoFolder(root,id):root;
            Directory.CreateDirectory(folder);
            string path=NewPhotoPath(folder,DateTimeOffset.Now);
            using var photo=GetViewport().GetTexture().GetImage();
            var crop=TownPhotoGrid.Crop(photo.GetSize(),aspect);
            using var cropped=crop.Size!=photo.GetSize()?photo.GetRegion(crop):null;
            if((cropped??photo).SavePng(path)!=Error.Ok)throw new IOException("无法写入照片文件");
            if(InPhotoMode)ShowPhotoFeedback($"已保存 · {crop.Size.X} × {crop.Size.Y} · {Path.GetFileName(path)}");
            else _selection=$"照片已保存 · {Path.GetFileName(path)}";
        }catch(Exception error){
            if(InPhotoMode)ShowPhotoFeedback("照片保存失败 · "+error.Message);
            else _selection="照片保存失败 · "+error.Message;
        }finally{
            _takingPhoto=false;
            if(framed&&InPhotoMode){_photoToolPanel!.Show();RefreshPhotoFraming();}
        }
    }

    void ShowPhotoFeedback(string text)
    {
        _photoFeedback!.Text=text;
        _photoFeedback.Visible=text.Length>0;
    }

    async Task VerifyRenderedPhotoAlbum(string output)
    {
        var pet=FarmPets.First();
        LocatePet(pet.Ident);
        TogglePhotoMode();
        UpdateCamera();
        if(_photoSettings!.Visible||!_photoContext!.Text.Contains(TownBindingText("capture")))
            throw new InvalidOperationException("拍照主工具栏没有收起进阶控件或显示当前拍照键");
        _photoSettingsButton!.EmitSignal(BaseButton.SignalName.Pressed);
        if(!_photoSettings.Visible)throw new InvalidOperationException("拍照设置无法展开");
        ((CheckButton)_photoLayer!.FindChild("TownPhotoDate",true,false)!).ButtonPressed=true;
        ((CheckButton)_photoLayer.FindChild("TownPhotoName",true,false)!).ButtonPressed=true;
        ((CheckButton)_photoLayer.FindChild("TownPhotoLookAt",true,false)!).ButtonPressed=true;
        if(_photoGrid?.Visible!=true||_photoTitleCard?.Visible!=true||_photoPosingPet!=pet)
            throw new InvalidOperationException("取景工具没有应用三分线、题签或镜头朝向");
        string album=PetPhotoFolder(PhotoRoot,pet.Ident);
        var before=TownPhotoAlbumFiles.Photos(album).Select(file=>file.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var ratio=(OptionButton)_photoLayer.FindChild("TownPhotoAspect",true,false)!;
        SelectAspect(3);
        for(int frame=0;frame<3;frame++)await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
        var safeFrame=TownPhotoGrid.Frame(_photoGrid!.Size,PhotoAspect);
        var caption=_photoTitleCard!.GetRect();
        if(!safeFrame.HasPoint(caption.Position)||!safeFrame.HasPoint(caption.End))
            throw new InvalidOperationException("竖幅题签超出实际取景框");
        if(safeFrame.Position.X>=190*UiScale+26&&_photoToolPanel!.GetGlobalRect().End.X>safeFrame.Position.X)
            throw new InvalidOperationException("竖幅工具栏遮住了有足够留白的取景框");
        SaveViewportShot("ui-拍照取景.png");
        await CaptureTownPhoto();
        SelectAspect(2);
        await CaptureTownPhoto();
        var captured=TownPhotoAlbumFiles.ReadEntries(TownPhotoAlbumFiles.Photos(album).Where(file=>!before.Contains(file.Name)).ToArray());
        if(captured.Length!=2||!captured.Any(photo=>photo.Pixels.X==photo.Pixels.Y)||
            !captured.Any(photo=>photo.Pixels.X*4==photo.Pixels.Y*3))
            throw new InvalidOperationException("拍照未按方形和竖幅实际写出对应像素的 PNG");
        LeavePhotoMode();
        if(_photoPosingPet!=null)throw new InvalidOperationException("退出拍照后未恢复伙伴朝向");
        OpenPetArchive(_selectedPet);
        var albumButton=(Button)_managementBody!.FindChild("PetPhotoAlbum",true,false)!;
        albumButton.EmitSignal(BaseButton.SignalName.Pressed);
        var gallery=(VBoxContainer)_managementBody.FindChild("PetPhotoGallery",true,false)!;
        if(gallery.FindChildren("*","TextureRect",true,false).Count<2)
            throw new InvalidOperationException("伙伴档案没有显示已保存的照片");
        var filter=(OptionButton)gallery.FindChild("PetPhotoAspectFilter",true,false)!;
        filter.Select(3);filter.EmitSignal(OptionButton.SignalName.ItemSelected,3L);
        if(!gallery.FindChildren("*","Label",true,false).OfType<Label>().Any(label=>label.Text.Contains("像素")))
            throw new InvalidOperationException("相册竖幅筛选未显示照片尺寸");
        var favorite=(Button)gallery.FindChild("PetPhotoFavorite",true,false)!;
        if(!favorite.Text.StartsWith('★'))favorite.EmitSignal(BaseButton.SignalName.Pressed);
        var favorites=TownPhotoAlbumFiles.ReadFavorites(album);
        var separateAlbum=PetPhotoFolder(PhotoRoot,"unrelated-verification-pet");
        if(favorites.Count==0||TownPhotoAlbumFiles.ReadFavorites(separateAlbum).Count!=0)
            throw new InvalidOperationException("相册收藏未落盘或混入其他伙伴");
        ((CheckButton)gallery.FindChild("PetPhotoFavoritesOnly",true,false)!).ButtonPressed=true;
        if(gallery.FindChild("PetPhotoPreview",true,false)==null)throw new InvalidOperationException("仅收藏筛选未保留收藏照片");
        ((CheckButton)gallery.FindChild("PetPhotoFavoritesOnly",true,false)!).ButtonPressed=false;
        filter.Select(0);filter.EmitSignal(OptionButton.SignalName.ItemSelected,0L);
        for(int frame=0;frame<3;frame++)await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
        using(var shot=_management!.GetTexture().GetImage())shot.SavePng(Path.Combine(output,"ui-照片相册.png"));
        ((Button)gallery.FindChild("PetPhotoPreview",true,false)!).EmitSignal(BaseButton.SignalName.Pressed);
        var preview=(Window)_management.FindChild("TownPhotoPreview",true,false)!;
        var detail=(Label)preview.FindChild("TownPhotoPreviewDetail",true,false)!;
        if(!preview.Visible||!detail.Text.StartsWith("1 / ")||((TextureRect)preview.FindChild("TownPhotoPreviewImage",true,false)!).Texture==null)
            throw new InvalidOperationException("相册无法在游戏内预览大图");
        preview.EmitSignal(Window.SignalName.WindowInput,new InputEventKey{Pressed=true,Keycode=Key.Right});
        if(!detail.Text.StartsWith("2 / "))throw new InvalidOperationException("方向键未切换预览中的照片");
        for(int frame=0;frame<3;frame++)await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
        using(var shot=preview.GetTexture().GetImage())shot.SavePng(Path.Combine(output,"ui-照片大图.png"));
        preview.EmitSignal(Window.SignalName.WindowInput,new InputEventKey{Pressed=true,Keycode=Key.Escape});
        if(preview.Visible)throw new InvalidOperationException("Esc 未关闭照片预览");
        GD.Print("TOWN_PHOTO_ALBUM_VERIFIED responsive_tools square_portrait_png album_filter keyboard_viewer favorites_isolated");

        void SelectAspect(int index){ratio.Select(index);ratio.EmitSignal(OptionButton.SignalName.ItemSelected,(long)index);}
        void SaveViewportShot(string name){using var shot=GetViewport().GetTexture().GetImage();shot.SavePng(Path.Combine(output,name));}
    }
}
