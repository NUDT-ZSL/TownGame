using Godot;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

public partial class Main
{
    void AddPetPhotoAlbum(string petId)
    {
        string root=PhotoRoot;
        string folder=root.Length>0?PetPhotoFolder(root,petId):"";
        FileInfo[] files;
        string readIssue="";
        try{files=TownPhotoAlbumFiles.Photos(folder);}
        catch(Exception error)when(error is IOException or UnauthorizedAccessException){files=Array.Empty<FileInfo>();readIssue="相册暂时无法读取。";}
        var album=new VBoxContainer{Name="PetPhotoGallery",Visible=false};
        album.AddThemeConstantOverride("separation",7);
        var toggle=ActionButton($"照片相册 · {files.Length} 张",()=>{
            album.Visible=!album.Visible;
            if(album.Visible&&album.GetChildCount()==0)CreatePetPhotoGallery(album,folder,files,readIssue);
            if(album.Visible)_=RevealPhotoAlbum(album);
        });
        toggle.Name="PetPhotoAlbum";
        _managementBody!.AddChild(album);
    }

    void CreatePetPhotoGallery(VBoxContainer album,string folder,FileInfo[] files,string readIssue)
    {
        var photos=TownPhotoAlbumFiles.ReadEntries(files);
        if(photos.Length==0){
            string message=readIssue.Length>0?readIssue:files.Length>0?"相册中的照片无法读取。":
                $"在场景中选中这位伙伴，按 {TownBindingText("photo")} 取景，再按 {TownBindingText("capture")} 拍照。";
            MarketLabel(album,message,color:TownTheme.Muted,wrap:true);
            return;
        }
        HashSet<string> favorites;
        bool canFavorite=true;
        try{favorites=TownPhotoAlbumFiles.ReadFavorites(folder);}
        catch(Exception error)when(error is IOException or UnauthorizedAccessException or InvalidDataException or JsonException){
            favorites=new(StringComparer.OrdinalIgnoreCase);canFavorite=false;
            MarketLabel(album,"收藏文件暂时无法读取。",color:TownTheme.Muted);
        }
        Dictionary<string,string> captions;
        bool canCaption=true;
        try{captions=TownPhotoAlbumFiles.ReadCaptions(folder);}
        catch(Exception error)when(error is IOException or UnauthorizedAccessException or InvalidDataException or JsonException){
            captions=new(StringComparer.OrdinalIgnoreCase);canCaption=false;
            MarketLabel(album,"照片说明暂时无法读取，原图仍可查看。",color:TownTheme.Muted,wrap:true);
        }
        var filters=new HFlowContainer();filters.AddThemeConstantOverride("h_separation",8);album.AddChild(filters);
        filters.AddThemeConstantOverride("v_separation",6);
        var search=new LineEdit{Name="PetPhotoSearch",PlaceholderText="搜索说明、日期或文件名",AccessibilityName="搜索照片",
            ClearButtonEnabled=true,CustomMinimumSize=new(196,36),SizeFlagsHorizontal=Control.SizeFlags.ExpandFill};
        filters.AddChild(search);
        var orientation=MarketOptions(filters,new[]{"全部比例","横幅","方形","竖幅"},0);
        orientation.Name="PetPhotoAspectFilter";
        var order=MarketOptions(filters,new[]{"最新拍摄","最早拍摄","收藏优先"},0);
        order.Name="PetPhotoSort";
        var favoritesOnly=new CheckButton{Name="PetPhotoFavoritesOnly",Text="仅收藏",Disabled=!canFavorite};
        filters.AddChild(favoritesOnly);
        const int pageSize=4;
        int page=0;
        var pages=new HFlowContainer();pages.AddThemeConstantOverride("h_separation",8);album.AddChild(pages);
        var previous=PhotoActionButton(pages,"上一页");
        var status=MarketLabel(pages,"",color:TownTheme.Muted);
        status.Name="PetPhotoPageStatus";
        status.VerticalAlignment=VerticalAlignment.Center;
        var next=PhotoActionButton(pages,"下一页");
        var grid=new HFlowContainer();
        grid.AddThemeConstantOverride("h_separation",8);
        grid.AddThemeConstantOverride("v_separation",8);
        album.AddChild(grid);
        previous.Pressed+=()=>Render(page-1);
        next.Pressed+=()=>Render(page+1);
        search.TextChanged+=_=>Render(0);
        orientation.ItemSelected+=_=>Render(0);
        order.ItemSelected+=_=>Render(0);
        favoritesOnly.Toggled+=_=>Render(0);
        Render(0);

        void Render(int target)
        {
            foreach(var child in grid.GetChildren()){grid.RemoveChild(child);child.QueueFree();}
            var selected=TownPhotoAlbumFiles.SelectEntries(photos,(TownPhotoAlbumFiles.Orientation)orientation.Selected,
                favoritesOnly.ButtonPressed,(TownPhotoAlbumFiles.SortOrder)order.Selected,search.Text,favorites,captions);
            int totalPages=(selected.Length+pageSize-1)/pageSize;
            page=Math.Clamp(target,0,Math.Max(0,totalPages-1));
            status.Text=selected.Length==0?"0 张照片":$"第 {page+1} / {totalPages} 页 · {selected.Length} 张照片";
            previous.Disabled=page==0;
            next.Disabled=page+1>=totalPages;
            if(selected.Length==0){
                MarketLabel(grid,"没有符合筛选的照片。",color:TownTheme.Muted).VerticalAlignment=VerticalAlignment.Center;
                MarketButton(grid,"清除筛选",()=>{search.Text="";orientation.Select(0);favoritesOnly.SetPressedNoSignal(false);Render(0);},expand:false,accent:"档案");
                return;
            }
            foreach(var photo in selected.Skip(page*pageSize).Take(pageSize)){
                var card=MarketCard(grid,210,"f6efe3");
                card.AddChild(new TextureRect{Texture=LoadTownPhotoTexture(photo.Path,260),CustomMinimumSize=new(202,122),
                    ExpandMode=TextureRect.ExpandModeEnum.IgnoreSize,StretchMode=TextureRect.StretchModeEnum.KeepAspectCentered,
                    MouseFilter=Control.MouseFilterEnum.Ignore});
                MarketLabel(card,photo.Description,13,TownTheme.Muted,wrap:true);
                if(captions.TryGetValue(photo.FileName,out string? caption))MarketLabel(card,caption,14,TownTheme.Ink,wrap:true);
                var actions=new HFlowContainer();
                actions.AddThemeConstantOverride("h_separation",6);
                card.AddChild(actions);
                var preview=MarketButton(actions,"查看大图",()=>OpenTownPhotoPreview(selected,Array.IndexOf(selected,photo),favorites,
                    canFavorite,Flip,captions,canCaption?SaveCaption:null,()=>{if(GodotObject.IsInstanceValid(album))Render(page);}),expand:false,accent:"档案");
                preview.Name="PetPhotoPreview";
                var favorite=PhotoActionButton(actions,PhotoFavoriteCaption(favorites.Contains(photo.FileName)),"存档");
                favorite.Pressed+=()=>{
                    if(!Flip(photo))return;
                    if(favoritesOnly.ButtonPressed||order.Selected==(int)TownPhotoAlbumFiles.SortOrder.Favorite)Render(page);
                    else RefreshFavorite();
                };
                favorite.Name="PetPhotoFavorite";
                favorite.Disabled=!canFavorite;
                RefreshFavorite();

                void RefreshFavorite()
                {
                    favorite.Text=PhotoFavoriteCaption(favorites.Contains(photo.FileName));
                    favorite.AccessibilityName=favorite.Text+" · "+photo.Description;
                }
            }
        }

        bool Flip(TownPhotoAlbumFiles.Entry photo)
        {
            bool marked=!favorites.Remove(photo.FileName);
            if(marked)favorites.Add(photo.FileName);
            try{TownPhotoAlbumFiles.WriteFavorites(folder,favorites);return true;}
            catch(Exception error)when(error is IOException or UnauthorizedAccessException){
                if(marked)favorites.Remove(photo.FileName);else favorites.Add(photo.FileName);
                SetManagementFeedback("照片收藏未能保存。",true);
                return false;
            }
        }

        void SaveCaption(TownPhotoAlbumFiles.Entry photo,string text)
        {
            string caption=TownPhotoAlbumFiles.SetCaption(folder,photo,text);
            if(caption.Length>0)captions[photo.FileName]=caption;
            else captions.Remove(photo.FileName);
        }
    }

    static string PhotoFavoriteCaption(bool active)=>active?"★ 取消收藏":"☆ 收藏";

    static Button PhotoActionButton(Control parent,string text,string accent="档案")
    {
        var button=new Button{Text=text,CustomMinimumSize=new(0,38)};
        TownTheme.AccentButton(button,accent);
        parent.AddChild(button);
        return button;
    }

    static Texture2D? LoadTownPhotoTexture(string path,int maximumSide)
    {
        if(!File.Exists(path))return null;
        try{
            using var photo=Image.LoadFromFile(path);
            if(photo==null||photo.IsEmpty())return null;
            float scale=Math.Min(1,(float)maximumSide/Math.Max(photo.GetWidth(),photo.GetHeight()));
            if(scale<1)photo.Resize(Math.Max(1,(int)(photo.GetWidth()*scale)),Math.Max(1,(int)(photo.GetHeight()*scale)),Image.Interpolation.Lanczos);
            return ImageTexture.CreateFromImage(photo);
        }catch(Exception error)when(error is IOException or UnauthorizedAccessException){return null;}
    }

    async Task RevealPhotoAlbum(Control album)
    {
        await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
        if(!GodotObject.IsInstanceValid(album)||!album.IsVisibleInTree()||_managementScroll==null||_managementBody==null)return;
        int top=Mathf.RoundToInt(album.GlobalPosition.Y-_managementBody.GlobalPosition.Y);
        _managementScroll.ScrollVertical=Math.Max(0,top-8);
    }
}
