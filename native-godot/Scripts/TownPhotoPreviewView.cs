using Godot;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

public partial class Main
{
    void OpenTownPhotoPreview(TownPhotoAlbumFiles.Entry[] photos,int index,HashSet<string> favorites,bool canFavorite,
        Func<TownPhotoAlbumFiles.Entry,bool> flip,IReadOnlyDictionary<string,string> captions,
        Action<TownPhotoAlbumFiles.Entry,string>? saveCaption,Action closed)
    {
        var preview=new Window{Name="TownPhotoPreview",Title="照片预览",Theme=_panel.Theme,Visible=false,
            Borderless=true,TransparentBg=true,Transient=true,Exclusive=true,ContentScaleMode=Window.ContentScaleModeEnum.CanvasItems};
        _management!.AddChild(preview);
        var panel=new PanelContainer();
        panel.AddThemeStyleboxOverride("panel",TownTheme.Box("fff8eb",13,12));
        preview.AddChild(panel);
        panel.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        var column=new VBoxContainer();
        column.AddThemeConstantOverride("separation",8);
        panel.AddChild(column);
        var header=new HBoxContainer();column.AddChild(header);
        var title=MarketLabel(header,"照片预览",19,TownTheme.Leaf);
        title.SizeFlagsHorizontal=Control.SizeFlags.ExpandFill;
        title.VerticalAlignment=VerticalAlignment.Center;
        var close=MarketButton(header,"关闭",Close,expand:false,accent:"档案");
        close.TooltipText="Esc 关闭预览";
        var imageFrame=new PanelContainer{SizeFlagsVertical=Control.SizeFlags.ExpandFill};
        imageFrame.AddThemeStyleboxOverride("panel",TownTheme.Box("e6e1d5",9,7));
        column.AddChild(imageFrame);
        var canvas=new Control{Name="TownPhotoPreviewCanvas",CustomMinimumSize=new(0,116),ClipContents=true,
            MouseFilter=Control.MouseFilterEnum.Stop,TooltipText="滚轮缩放，放大后按住左键拖动"};
        imageFrame.AddChild(canvas);
        var image=new TextureRect{Name="TownPhotoPreviewImage",MouseFilter=Control.MouseFilterEnum.Ignore,
            ExpandMode=TextureRect.ExpandModeEnum.IgnoreSize,StretchMode=TextureRect.StretchModeEnum.Scale};
        canvas.AddChild(image);
        var zoomTools=new HFlowContainer();zoomTools.AddThemeConstantOverride("h_separation",7);column.AddChild(zoomTools);
        var zoomOut=PhotoActionButton(zoomTools,"−");zoomOut.Name="TownPhotoZoomOut";zoomOut.AccessibilityName="缩小照片";
        var zoomStatus=MarketLabel(zoomTools,"",14,TownTheme.Muted);zoomStatus.Name="TownPhotoZoomStatus";
        zoomStatus.VerticalAlignment=VerticalAlignment.Center;
        var zoomIn=PhotoActionButton(zoomTools,"+");zoomIn.Name="TownPhotoZoomIn";zoomIn.AccessibilityName="放大照片";
        var zoomReset=PhotoActionButton(zoomTools,"适合窗口");zoomReset.Name="TownPhotoZoomReset";
        var detail=MarketLabel(column,"",14,TownTheme.Muted,wrap:true);
        detail.Name="TownPhotoPreviewDetail";
        var caption=MarketLabel(column,"",15,TownTheme.Ink,wrap:true);
        caption.Name="TownPhotoPreviewCaption";
        var editor=new HFlowContainer{Visible=false};editor.AddThemeConstantOverride("h_separation",7);column.AddChild(editor);
        var field=new LineEdit{Name="TownPhotoCaptionEdit",PlaceholderText="给照片写一句说明，留空可清除",
            AccessibilityName="照片说明",MaxLength=TownPhotoAlbumFiles.CaptionLimit,ClearButtonEnabled=true,
            CustomMinimumSize=new(215,38),SizeFlagsHorizontal=Control.SizeFlags.ExpandFill};
        editor.AddChild(field);
        var save=PhotoActionButton(editor,"保存说明");save.Name="TownPhotoCaptionSave";
        var cancel=PhotoActionButton(editor,"取消");cancel.Name="TownPhotoCaptionCancel";
        var feedback=MarketLabel(column,"",14,TownTheme.Muted,wrap:true);
        feedback.Name="TownPhotoPreviewFeedback";
        feedback.Hide();
        var actions=new HFlowContainer();actions.AddThemeConstantOverride("h_separation",7);column.AddChild(actions);
        var previous=PhotoActionButton(actions,"上一张");
        previous.Name="TownPhotoPreviewPrevious";
        previous.TooltipText="← 上一张";
        var next=PhotoActionButton(actions,"下一张");
        next.Name="TownPhotoPreviewNext";
        next.TooltipText="→ 下一张";
        var favorite=PhotoActionButton(actions,"","存档");
        favorite.Name="TownPhotoPreviewFavorite";
        favorite.Disabled=!canFavorite;
        var original=MarketButton(actions,"打开原图",()=>{
            if(OS.ShellOpen(photos[index].Path)!=Error.Ok)Report("原图暂时无法打开。");
        },expand:false,accent:"档案");
        var edit=PhotoActionButton(actions,"编辑说明");edit.Name="TownPhotoCaptionButton";
        var export=PhotoActionButton(actions,"导出 PNG 副本","存档");export.Name="TownPhotoExport";
        float zoom=1;
        Vector2 pan=Vector2.Zero;
        bool dragging=false;
        previous.Pressed+=()=>Move(-1);
        next.Pressed+=()=>Move(1);
        favorite.Pressed+=FlipFavorite;
        zoomOut.Pressed+=()=>ChangeZoom(-1,canvas.Size/2);
        zoomIn.Pressed+=()=>ChangeZoom(1,canvas.Size/2);
        zoomReset.Pressed+=ResetZoom;
        canvas.Resized+=LayoutImage;
        canvas.GuiInput+=HandleImageInput;
        preview.FocusExited+=()=>dragging=false;
        edit.Pressed+=()=>SetEditing(!editor.Visible);
        cancel.Pressed+=()=>SetEditing(false);
        save.Pressed+=SaveCaption;
        field.TextSubmitted+=_=>SaveCaption();
        export.Pressed+=ChooseExport;
        preview.CloseRequested+=Close;
        preview.WindowInput+=input=>{
            if(input is not InputEventKey{Pressed:true,Echo:false} key||key.Keycode is not (Key.Left or Key.Right or Key.Escape))return;
            if(editor.Visible&&key.Keycode!=Key.Escape)return;
            preview.SetInputAsHandled();
            if(key.Keycode==Key.Escape){if(editor.Visible)SetEditing(false);else Close();}
            else Move(key.Keycode==Key.Left?-1:1);
        };
        Refresh();
        preview.PopupCenteredClamped(new(920,660),.92f);
        close.GrabFocus();

        void Move(int step)
        {
            int target=index+step;
            if(editor.Visible||target<0||target>=photos.Length)return;
            index=target;
            Refresh();
        }

        void Refresh()
        {
            var photo=photos[index];
            image.Texture=LoadTownPhotoTexture(photo.Path,1600);
            detail.Text=$"{index+1} / {photos.Length} · {photo.Description}";
            previous.Disabled=editor.Visible||index==0;
            next.Disabled=editor.Visible||index==photos.Length-1;
            original.Disabled=export.Disabled=image.Texture==null;
            edit.Disabled=saveCaption==null||image.Texture==null;
            edit.TooltipText=saveCaption==null?"照片说明文件暂时无法读取。":"修改相册中的照片说明";
            favorite.Text=PhotoFavoriteCaption(favorites.Contains(photo.FileName));
            RefreshCaption();
            ResetZoom();
            Report(image.Texture==null?"这张照片暂时无法读取。":"");
        }

        void RefreshCaption()
        {
            string text=captions.TryGetValue(photos[index].FileName,out var stored)?stored:"";
            caption.Text=text;
            caption.Visible=text.Length>0&&!editor.Visible;
        }

        void SetEditing(bool enabled)
        {
            if(enabled&&edit.Disabled)return;
            editor.Visible=enabled;
            edit.Text=enabled?"收起说明":"编辑说明";
            previous.Disabled=enabled||index==0;
            next.Disabled=enabled||index==photos.Length-1;
            RefreshCaption();
            Report("");
            if(enabled){field.Text=caption.Text;field.GrabFocus();field.CaretColumn=field.Text.Length;}
            else edit.GrabFocus();
        }

        void SaveCaption()
        {
            if(saveCaption==null||!editor.Visible)return;
            try{
                saveCaption(photos[index],field.Text);
                SetEditing(false);
                Report(caption.Text.Length>0?"已保存照片说明。":"已清除照片说明。");
            }catch(Exception error)when(error is IOException or UnauthorizedAccessException or InvalidDataException or JsonException){
                Report("照片说明未能保存。");
            }
        }

        void ChooseExport()
        {
            var photo=photos[index];
            var dialog=new FileDialog{Name="TownPhotoExportDialog",Title="导出原始照片副本",Theme=_panel.Theme,Transient=true,Exclusive=true,
                FileMode=FileDialog.FileModeEnum.SaveFile,Access=FileDialog.AccessEnum.Filesystem,
                CurrentFile=Path.GetFileNameWithoutExtension(photo.FileName)+"-副本.png",Filters=new[]{"*.png ; PNG 图片"}};
            preview.AddChild(dialog);
            dialog.Canceled+=dialog.QueueFree;
            dialog.FileSelected+=path=>{
                dialog.Hide();dialog.QueueFree();
                try{TownPhotoAlbumFiles.ExportOriginal(photo,path);Report("已导出原始照片副本 · "+Path.GetFileName(path));}
                catch(Exception error)when(error is IOException or UnauthorizedAccessException or ArgumentException){Report("导出失败 · "+error.Message);}
            };
            dialog.PopupCenteredClamped(new(720,460),.92f);
        }

        void ChangeZoom(int direction,Vector2 anchor)
        {
            if(image.Texture==null)return;
            float nextZoom=Math.Clamp(zoom*Mathf.Pow(1.25f,direction),1,4);
            if(Mathf.IsEqualApprox(nextZoom,zoom))return;
            Vector2 relative=anchor-canvas.Size/2;
            pan=relative-(relative-pan)*(nextZoom/zoom);
            zoom=nextZoom;
            LayoutImage();
        }

        void ResetZoom(){zoom=1;pan=Vector2.Zero;dragging=false;LayoutImage();}

        void LayoutImage()
        {
            zoomOut.Disabled=zoomReset.Disabled=image.Texture==null||zoom<=1;
            zoomIn.Disabled=image.Texture==null||zoom>=4;
            zoomStatus.Text=$"{zoom*100:0}%";
            if(image.Texture==null)return;
            Vector2 pixels=photos[index].Pixels;
            float scale=Math.Min(canvas.Size.X/pixels.X,canvas.Size.Y/pixels.Y)*zoom;
            Vector2 size=pixels*scale;
            Vector2 bound=((size-canvas.Size)/2).Max(Vector2.Zero);
            pan=pan.Clamp(-bound,bound);
            image.Size=size;
            image.Position=(canvas.Size-size)/2+pan;
        }

        void HandleImageInput(InputEvent input)
        {
            if(image.Texture==null)return;
            if(input is InputEventMouseButton button){
                if(button.Pressed&&button.ButtonIndex is MouseButton.WheelUp or MouseButton.WheelDown){
                    ChangeZoom(button.ButtonIndex==MouseButton.WheelUp?1:-1,button.Position);
                    canvas.AcceptEvent();
                }else if(button.ButtonIndex==MouseButton.Left){
                    dragging=button.Pressed&&zoom>1;
                    canvas.AcceptEvent();
                }
            }else if(input is InputEventMouseMotion motion&&dragging){
                dragging=(motion.ButtonMask&MouseButtonMask.Left)!=0;
                if(!dragging)return;
                pan+=motion.Relative;
                LayoutImage();
                canvas.AcceptEvent();
            }
        }

        void FlipFavorite()
        {
            var photo=photos[index];
            if(flip(photo))favorite.Text=PhotoFavoriteCaption(favorites.Contains(photo.FileName));
            else Report("照片收藏未能保存。");
        }

        void Report(string text){feedback.Text=text;feedback.Visible=text.Length>0;}

        void Close()
        {
            preview.Hide();
            preview.QueueFree();
            closed();
        }
    }
}
