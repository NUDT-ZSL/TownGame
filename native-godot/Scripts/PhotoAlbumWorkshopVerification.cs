using Godot;
using System;
using System.IO;
using System.Linq;
using System.Text.Json;

public partial class Main
{
    void VerifyPhotoAlbumWorkshop(Action<bool,string> check)
    {
        string temp=Path.Combine(Path.GetTempPath(),"TownGame-photo-workshop-"+Guid.NewGuid().ToString("N"));
        string folder=Path.Combine(temp,"pets","album");
        Directory.CreateDirectory(folder);
        Window? preview=null;
        try{
            using(var fixture=Image.CreateEmpty(32,24,false,Image.Format.Rgba8)){
                fixture.Fill(new Color("819f79"));
                for(int day=4;day<=6;day++)
                    check(fixture.SavePng(Path.Combine(folder,$"202605{day:00}-101112-000.png"))==Error.Ok,"验收照片无法写入");
            }
            var photos=TownPhotoAlbumFiles.ReadEntries(TownPhotoAlbumFiles.Photos(folder));
            check(photos.Length==3&&TownPhotoAlbumFiles.ReadCaptions(folder).Count==0,"旧相册无法在没有说明文件时打开");
            var featured=photos[1];
            TownPhotoAlbumFiles.WriteFavorites(folder,new(StringComparer.OrdinalIgnoreCase){featured.FileName});
            byte[] original=File.ReadAllBytes(featured.Path);

            ShowManagement("档案","companions");
            var gallery=new VBoxContainer();_managementBody!.AddChild(gallery);
            CreatePetPhotoGallery(gallery,folder,TownPhotoAlbumFiles.Photos(folder),"");
            var sort=Find<OptionButton>(gallery,"PetPhotoSort");
            Select(sort,1);
            Press(gallery,"PetPhotoPreview");
            preview=Find<Window>(_management!,"TownPhotoPreview");
            check(Find<Label>(preview,"TownPhotoPreviewDetail").Text.Contains("2026-05-04"),"最早拍摄排序未决定预览顺序");
            ClosePreview();
            Select(sort,2);
            Press(gallery,"PetPhotoPreview");
            preview=Find<Window>(_management!,"TownPhotoPreview");
            var detail=Find<Label>(preview,"TownPhotoPreviewDetail");
            check(detail.Text.Contains("2026-05-05"),"收藏优先未置顶收藏照片");
            Press(preview,"TownPhotoCaptionButton");
            var field=Find<LineEdit>(preview,"TownPhotoCaptionEdit");
            field.Text=" 柳岸散步快照 ";
            preview.EmitSignal(Window.SignalName.WindowInput,new InputEventKey{Pressed=true,Keycode=Key.Right});
            check(detail.Text.Contains("2026-05-05"),"编辑说明时误切换到另一张照片");
            Press(preview,"TownPhotoCaptionSave");
            check(TownPhotoAlbumFiles.ReadCaptions(folder)[featured.FileName]=="柳岸散步快照"&&
                TownPhotoAlbumFiles.ReadCaptions(Path.Combine(temp,"pets","other")).Count==0&&original.SequenceEqual(File.ReadAllBytes(featured.Path)),
                "说明未随相册保存、影响其他相册或改变原图");

            var canvas=Find<Control>(preview,"TownPhotoPreviewCanvas");canvas.Size=new(300,190);
            var image=Find<TextureRect>(preview,"TownPhotoPreviewImage");
            var zoom=Find<Label>(preview,"TownPhotoZoomStatus");
            Press(preview,"TownPhotoZoomIn");
            check(canvas.ClipContents&&zoom.Text=="125%"&&image.Size.Y>canvas.Size.Y,"放大预览未按比例显示或超出边框");
            var beforePan=image.Position;
            canvas.EmitSignal(Control.SignalName.GuiInput,new InputEventMouseButton{ButtonIndex=MouseButton.Left,Pressed=true});
            canvas.EmitSignal(Control.SignalName.GuiInput,new InputEventMouseMotion{ButtonMask=MouseButtonMask.Left,Relative=new Vector2(28,20)});
            canvas.EmitSignal(Control.SignalName.GuiInput,new InputEventMouseButton{ButtonIndex=MouseButton.Left});
            check(image.Position.X>beforePan.X&&image.Position.Y>beforePan.Y,"放大后无法拖动照片查看细节");
            Press(preview,"TownPhotoZoomReset");
            check(zoom.Text=="100%"&&(image.Position+image.Size/2).IsEqualApprox(canvas.Size/2),"适合窗口未恢复居中");

            string export=Path.Combine(temp,"副本.png");
            if(DisplayServer.GetName()!="headless"){
                Press(preview,"TownPhotoExport");
                Find<FileDialog>(preview,"TownPhotoExportDialog").EmitSignal(FileDialog.SignalName.FileSelected,export);
                check(Find<Label>(preview,"TownPhotoPreviewFeedback").Text.Contains("已导出"),"导出副本未提供结果反馈");
            }else TownPhotoAlbumFiles.ExportOriginal(featured,export);
            check(original.SequenceEqual(File.ReadAllBytes(export)),"导出没有保留 PNG 原始字节");
            bool protectedFile=false;
            try{TownPhotoAlbumFiles.ExportOriginal(photos[0],export);}
            catch(IOException){protectedFile=true;}
            check(protectedFile&&original.SequenceEqual(File.ReadAllBytes(export)),"重复导出覆盖了已有文件");

            Press(preview,"TownPhotoPreviewFavorite");
            preview.EmitSignal(Window.SignalName.WindowInput,new InputEventKey{Pressed=true,Keycode=Key.Right});
            check(detail.Text.Contains("2026-05-06"),"预览中的收藏更改破坏了原筛选顺序");
            ClosePreview();
            var search=Find<LineEdit>(gallery,"PetPhotoSearch");
            search.Text="快照 2026-05-05";search.EmitSignal(LineEdit.SignalName.TextChanged,search.Text);
            check(gallery.FindChildren("PetPhotoPreview","Button",true,false).Count==1&&
                Find<Label>(gallery,"PetPhotoPageStatus").Text.Contains("1 张照片"),"说明和日期组合搜索没有命中唯一照片");

            TownPhotoAlbumFiles.SetCaption(folder,featured,"");
            check(TownPhotoAlbumFiles.ReadCaptions(folder).Count==0,"清空说明后仍保留旧内容");
            string sidecar=Path.Combine(folder,"captions.json");
            File.WriteAllText(sidecar,"{ 损坏的验收内容");
            bool protectedCaption=false;
            try{TownPhotoAlbumFiles.SetCaption(folder,featured,"覆盖测试");}
            catch(JsonException){protectedCaption=true;}
            check(protectedCaption&&File.ReadAllText(sidecar)=="{ 损坏的验收内容","写说明时覆盖了无法读取的资料");
            GD.Print("PHOTO_ALBUM_WORKSHOP_VERIFIED captions isolated query sort viewer_zoom_pan byte_preserving_export no_overwrite");
        }finally{
            if(GodotObject.IsInstanceValid(preview))preview!.Hide();
            _management?.Hide();
            Directory.Delete(temp,true);
        }

        static T Find<T>(Node root,string name)where T:Node=>(T)root.FindChild(name,true,false)!;
        static void Press(Node root,string name)=>Find<Button>(root,name).EmitSignal(BaseButton.SignalName.Pressed);
        static void Select(OptionButton control,int item){control.Select(item);control.EmitSignal(OptionButton.SignalName.ItemSelected,(long)item);}
        void ClosePreview()
        {
            preview!.EmitSignal(Window.SignalName.CloseRequested);
            _management!.RemoveChild(preview);
            preview=null;
        }
    }
}
