using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;

public partial class Main
{
    readonly Dictionary<string,int> _appearanceDrafts=new(StringComparer.Ordinal);

    void AppearanceControls()
    {
        Section("外观工坊","选伙伴 · 试搭配 · 保存外观");
        var identity=MarketCard(_managementBody!,color:"f2ece9");
        var pickerRow=AppearanceRow(identity);
        var search=new LineEdit{
            Name="AppearancePetSearch",PlaceholderText="搜索名字或物种",AccessibilityName="搜索外观工坊伙伴",
            ClearButtonEnabled=true,CustomMinimumSize=new(155,36),SizeFlagsHorizontal=Control.SizeFlags.ExpandFill
        };
        pickerRow.AddChild(search);
        var picker=AppearancePicker(pickerRow,"AppearancePetPicker","选择要编辑的伙伴",245);
        var caption=MarketLabel(identity,"",15,TownTheme.Ink,wrap:true);
        caption.Name="AppearancePetIdentity";
        var empty=MarketLabel(identity,"庄园暂无可编辑外观的动物。",14,TownTheme.Muted,wrap:true);

        var layout=new BoxContainer{Vertical=true};
        layout.AddThemeConstantOverride("separation",8);
        _managementBody!.AddChild(layout);
        var preview=MarketCard(layout,color:"eae7f1");
        preview.GetParent<Control>().SizeFlagsStretchRatio=1.18f;
        var tools=new VBoxContainer{SizeFlagsHorizontal=Control.SizeFlags.ExpandFill};
        tools.AddThemeConstantOverride("separation",8);
        layout.AddChild(tools);
        var status=MarketLabel(preview,"",15,TownTheme.Accent("外观").Darkened(.24f),wrap:true);
        status.Name="AppearanceDraftState";
        var container=new SubViewportContainer{CustomMinimumSize=new(0,116),Stretch=true,MouseFilter=Control.MouseFilterEnum.Ignore};
        preview.AddChild(container);
        var viewport=CreatePetPreviewViewport(container,new(360,184),SubViewport.UpdateMode.Once);
        viewport.AddChild(new WorldEnvironment{Environment=new Godot.Environment{
            BackgroundMode=Godot.Environment.BGMode.Color,BackgroundColor=new Color("eee8f2"),
            AmbientLightSource=Godot.Environment.AmbientSource.Color,AmbientLightColor=new Color("fff5e8"),AmbientLightEnergy=.75f
        }});
        var pivot=new Node3D{Name="AppearancePreviewPivot"};
        viewport.AddChild(pivot);
        var model=new MeshInstance3D{Name="AppearancePreviewModel"};
        pivot.AddChild(model);
        var camera=new Camera3D{Name="AppearancePreviewCamera",Projection=Camera3D.ProjectionType.Orthogonal,
            KeepAspect=Camera3D.KeepAspectEnum.Height,Current=true,
            Position=new(0,6,24)};
        viewport.AddChild(camera);
        camera.LookAt(Vector3.Zero,Vector3.Up);
        viewport.AddChild(new DirectionalLight3D{RotationDegrees=new(-42,-25,0),LightEnergy=1.25f});

        const int defaultAngle=28;
        var angles=AppearanceRow(preview);
        var angleLabel=MarketLabel(angles,"朝向 28°",14,TownTheme.Muted);
        var angle=new HSlider{Name="AppearancePreviewAngle",AccessibilityName="预览旋转角度",MinValue=0,MaxValue=359,
            Step=1,Value=defaultAngle,CustomMinimumSize=new(135,28),SizeFlagsHorizontal=Control.SizeFlags.ExpandFill};
        angles.AddChild(angle);
        var resetAngle=AppearanceButton(angles,"重置角度","AppearanceResetAngle");
        var actions=AppearanceRow(preview);
        var apply=AppearanceButton(actions,"保存外观","AppearanceApply");
        apply.AddThemeStyleboxOverride("normal",TownTheme.Box("d9d0e6",9,7));
        apply.AddThemeStyleboxOverride("hover",TownTheme.Box("ccc2dd",9,7));
        var compare=AppearanceButton(actions,"对照已保存","AppearanceCompare");
        compare.ToggleMode=true;
        var discard=AppearanceButton(actions,"丢弃草稿","AppearanceDiscard");
        var initial=AppearanceButton(actions,"预览初始外观","AppearanceInitial");
        initial.TooltipText="将伙伴的初始外观载入为草稿，保存后才会生效。";

        var choices=MarketCard(tools,color:"f7f1e6");
        MarketLabel(choices,"毛色与造型",17,TownTheme.Accent("外观").Darkened(.24f));
        var coats=AppearanceRow(choices);
        var swatches=Enumerable.Range(0,NativeAppearance.RabbitGroups[0].Length).Select(i=>{
            var button=AppearanceButton(coats,"",$"AppearanceCoat{i}");
            button.ToggleMode=true;
            return button;
        }).ToArray();
        var traits=AppearanceRow(choices);
        var traitOptions=Enumerable.Range(1,3).Select(i=>{
            var group=new VBoxContainer{SizeFlagsHorizontal=Control.SizeFlags.ExpandFill};
            group.AddThemeConstantOverride("separation",3);
            traits.AddChild(group);
            var label=MarketLabel(group,"",14,TownTheme.Muted);
            var option=AppearancePicker(group,$"AppearanceTrait{i}","选择造型",125);
            return (Label:label,Option:option);
        }).ToArray();
        MarketLabel(choices,"可切换伙伴继续试搭。草稿仅在本次游玩保留，保存后才会应用。",14,TownTheme.Muted,wrap:true);

        var favorites=MarketCard(tools,color:"f1ecf6");
        var favoriteTitle=MarketLabel(favorites,"",17,TownTheme.Accent("外观").Darkened(.24f),wrap:true);
        favoriteTitle.Name="AppearanceFavoritesStatus";
        var favoriteRow=AppearanceRow(favorites);
        var favoritePicker=AppearancePicker(favoriteRow,"AppearanceFavorites","选择同物种收藏",270);
        var saveFavorite=AppearanceButton(favoriteRow,"收藏当前搭配","AppearanceFavoriteAdd");
        var loadFavorite=AppearanceButton(favoriteRow,"载入为草稿","AppearanceFavoriteLoad");
        var removeFavorite=AppearanceButton(favoriteRow,"移除收藏","AppearanceFavoriteRemove");

        (string Id,string Caption)[]? shownPets=null;
        int[] shownFavorites=Array.Empty<int>();
        string favoriteSpecies="",paletteSpecies="";
        bool showSaved=false;
        Vector2 modelFrame=Vector2.One;
        (string Species,int Variant,bool Rare,bool Sick,string Stage)? rendered=null;

        search.TextChanged+=_=>Refresh();
        picker.ItemSelected+=index=>{
            if(shownPets==null||index<0||index>=shownPets.Length)return;
            _selectedPet=shownPets[index].Id;
            showSaved=false;
            Refresh();
        };
        for(int i=0;i<swatches.Length;i++){
            int coat=i;
            swatches[i].Pressed+=()=>SetTrait(0,coat);
        }
        for(int i=0;i<traitOptions.Length;i++){
            int group=i+1;
            traitOptions[i].Option.ItemSelected+=index=>SetTrait(group,(int)index);
        }
        angle.ValueChanged+=degrees=>{
            pivot.RotationDegrees=new(0,(float)degrees,0);
            angleLabel.Text=$"朝向 {degrees:0}°";
            viewport.RenderTargetUpdateMode=SubViewport.UpdateMode.Once;
        };
        pivot.RotationDegrees=new(0,defaultAngle,0);
        container.Resized+=FitCamera;
        layout.Resized+=AdaptLayout;
        AdaptLayout();
        resetAngle.Pressed+=()=>angle.Value=defaultAngle;
        compare.Toggled+=value=>{showSaved=value;Refresh();};
        discard.Pressed+=()=>{_appearanceDrafts.Remove(_selectedPet);showSaved=false;Refresh();};
        initial.Pressed+=()=>SetDraft(Game.InitialAppearance(_selectedPet));
        apply.Pressed+=()=>{
            if(!showSaved&&CurrentPet() is { } pet&&_appearanceDrafts.TryGetValue(_selectedPet,out int draft)&&draft!=Game.Appearance(pet))
                Act("pet_appearance",new JsonObject{{"pet_id",_selectedPet},{"variant",draft}});
        };
        saveFavorite.Pressed+=()=>{
            if(CurrentPet() is not { } pet)return;
            int draft=DraftFor(pet);
            if(!showSaved&&!shownFavorites.Contains(draft)&&shownFavorites.Length<12)ChangeFavorite(draft);
        };
        loadFavorite.Pressed+=()=>{if(SelectedFavorite() is int style)SetDraft(style);};
        removeFavorite.Pressed+=()=>{if(SelectedFavorite() is int style)ChangeFavorite(style);};

        _refreshManagementPage+=Refresh;
        identity.TreeExiting+=()=>_refreshManagementPage-=Refresh;
        Refresh();

        JsonNode? CurrentPet()=>Data.FindPet(_selectedPet) is { } pet&&Game.Active(pet)?pet:null;
        int DraftFor(JsonNode pet)=>_appearanceDrafts.GetValueOrDefault(_selectedPet,Game.Appearance(pet));
        int? SelectedFavorite()=>favoritePicker.Selected>=0&&favoritePicker.Selected<shownFavorites.Length
            ?shownFavorites[favoritePicker.Selected]:null;
        void ChangeFavorite(int style)
        {
            if(CurrentPet() is { } pet)
                Act("favorite_style",new JsonObject{{"species",NativeGame.S(pet,"species")},{"variant",style}});
        }

        void SetTrait(int group,int index)
        {
            if(showSaved||CurrentPet() is not { } pet)return;
            var groups=AppearanceGroups(NativeGame.S(pet,"species"));
            int step=NativeAppearance.Steps[group],draft=DraftFor(pet);
            int previous=draft/step%groups[group].Length;
            SetDraft(draft+(index-previous)*step);
        }

        void SetDraft(int variant)
        {
            if(CurrentPet() is not { } pet)return;
            if(variant==Game.Appearance(pet))_appearanceDrafts.Remove(_selectedPet);
            else _appearanceDrafts[_selectedPet]=variant;
            showSaved=false;
            Refresh();
        }

        void FitCamera()
        {
            float aspect=container.Size.X/Math.Max(1,container.Size.Y);
            camera.Size=Math.Max(modelFrame.Y,modelFrame.X/Math.Max(.1f,aspect))*1.09f;
            viewport.RenderTargetUpdateMode=SubViewport.UpdateMode.Once;
        }

        void AdaptLayout()
        {
            layout.Vertical=layout.Size.X<780+ExtraFontSize*16;
            container.CustomMinimumSize=new(0,layout.Vertical?116:268);
        }

        void Refresh()
        {
            var pets=Data.Pets.OfType<JsonNode>().Where(Game.Active).ToArray();
            var pet=CurrentPet()??pets.FirstOrDefault();
            string id=pet==null?"":NativeGame.S(pet,"pet_id");
            if(id!=_selectedPet){_selectedPet=id;showSaved=false;}
            string query=search.Text.Trim();
            var available=pets.Where(row=>NativeGame.S(row,"name").Contains(query,StringComparison.OrdinalIgnoreCase)
                ||NativeGame.S(row,"species").Contains(query,StringComparison.OrdinalIgnoreCase))
                .Select(row=>(Id:NativeGame.S(row,"pet_id"),Caption:AppearancePetCaption(row))).ToArray();
            if(shownPets==null||!shownPets.SequenceEqual(available)){
                shownPets=available;
                picker.Clear();
                foreach(var item in available)picker.AddItem(item.Caption);
                if(available.Length==0)picker.AddItem("没有匹配的伙伴");
                picker.Disabled=available.Length==0;
            }
            picker.Select(available.Length==0?0:Array.FindIndex(available,row=>row.Id==id));
            if(available.Length>0&&picker.Selected<0)picker.Text="选择匹配的伙伴";
            empty.Visible=pet==null;
            caption.Visible=pet!=null;
            layout.Visible=pet!=null;
            if(pet==null)return;

            string species=NativeGame.S(pet,"species"),stage=Game.Stage(pet);
            int saved=Game.Appearance(pet),draft=DraftFor(pet);
            bool dirty=saved!=draft;
            if(!dirty){_appearanceDrafts.Remove(id);showSaved=false;}
            int shown=showSaved?saved:draft;
            caption.Text=$"当前伙伴 · {AppearancePetCaption(pet)} · {stage}";
            status.Text=(dirty?(showSaved?"已保存外观 · 草稿仍保留":"未保存草稿"):"已保存外观")
                +" · "+NativeAppearance.Description(species,shown);
            compare.SetPressedNoSignal(showSaved);
            compare.Text=showSaved?"返回草稿":"对照已保存";
            apply.Disabled=!dirty||showSaved;
            compare.Disabled=discard.Disabled=!dirty;
            initial.Disabled=draft==Game.InitialAppearance(id);

            var groups=AppearanceGroups(species);
            if(paletteSpecies!=species){
                paletteSpecies=species;
                string[] labels=species=="兔子"?new[]{"花纹","耳型","额毛"}:new[]{"花纹","体态","点缀"};
                for(int i=0;i<swatches.Length;i++){
                    string name=groups[0][i];
                    swatches[i].Text=name;
                    swatches[i].AccessibilityName=$"基础毛色 {name}";
                    using var image=Image.CreateEmpty(14,14,false,Image.Format.Rgba8);
                    image.Fill(NativePetMesh.BaseCoat(species,i));
                    swatches[i].Icon=ImageTexture.CreateFromImage(image);
                }
                for(int i=0;i<traitOptions.Length;i++){
                    var (label,option)=traitOptions[i];
                    label.Text=labels[i];
                    option.AccessibilityName=labels[i];
                    option.Clear();
                    foreach(string value in groups[i+1])option.AddItem(value);
                }
            }
            for(int i=0;i<swatches.Length;i++){
                swatches[i].SetPressedNoSignal(shown%groups[0].Length==i);
                swatches[i].Disabled=showSaved;
            }
            for(int i=0;i<traitOptions.Length;i++){
                traitOptions[i].Option.Select(shown/NativeAppearance.Steps[i+1]%groups[i+1].Length);
                traitOptions[i].Option.Disabled=showSaved;
            }

            var visual=(Species:species,Variant:shown,Rare:NativeGame.B(pet,"rare"),
                Sick:NativeGame.S(pet,"illness_type")!=""||NativeGame.D(pet,"injury")>0,Stage:stage);
            if(rendered!=visual){
                rendered=visual;
                model.Mesh=_petModels.Get(species,shown,visual.Rare,visual.Sick,stage,"idle",0);
                var bounds=model.Mesh.GetAabb();
                model.Position=-bounds.GetCenter();
                var other=_petModels.Get(species,showSaved?draft:saved,visual.Rare,visual.Sick,stage,"idle",0).GetAabb();
                // A full turn sweeps the horizontal diagonal. The camera pitch projects part of it upward.
                modelFrame=PreviewFrame(bounds.Size).Max(PreviewFrame(other.Size));
                FitCamera();
            }

            var styles=Game.Meta["style_favorites"]?[species] is JsonArray rows
                ?rows.OfType<JsonValue>().Select(row=>row.TryGetValue<int>(out int value)?value:-1).Where(style=>style is >=0 and <384).ToArray()
                :Array.Empty<int>();
            if(favoriteSpecies!=species||!shownFavorites.SequenceEqual(styles)){
                int keep=favoriteSpecies==species?SelectedFavorite()??-1:-1;
                favoriteSpecies=species;
                shownFavorites=styles;
                favoritePicker.Clear();
                foreach(int style in styles)favoritePicker.AddItem(NativeAppearance.Description(species,style));
                if(styles.Length==0)favoritePicker.AddItem("暂无收藏");
                favoritePicker.Select(Math.Max(0,Array.IndexOf(styles,keep)));
            }
            favoriteTitle.Text=$"{species}的搭配收藏 · {styles.Length} / 12";
            favoritePicker.Disabled=loadFavorite.Disabled=removeFavorite.Disabled=styles.Length==0;
            bool favorite=styles.Contains(draft);
            saveFavorite.Text=favorite?"当前搭配已收藏":"收藏当前搭配";
            saveFavorite.Disabled=favorite||styles.Length>=12||showSaved;
            saveFavorite.TooltipText=styles.Length>=12?"收藏已满，可先移除一个搭配。":"收藏供同物种的伙伴使用。";
        }

        string AppearancePetCaption(JsonNode pet)=>$"{NativeGame.S(pet,"name")} · {NativeGame.S(pet,"species")} · {NativeGame.S(pet,"sex")}";

        Vector2 PreviewFrame(Vector3 dimensions)
        {
            float radialSpan=new Vector2(dimensions.X,dimensions.Z).Length();
            float pitch=Mathf.Abs(camera.Rotation.X);
            return new(radialSpan,dimensions.Y*Mathf.Cos(pitch)+radialSpan*Mathf.Sin(pitch));
        }
    }

    static string[][] AppearanceGroups(string species)=>species=="兔子"?NativeAppearance.RabbitGroups:NativeAppearance.OtherGroups;

    static HFlowContainer AppearanceRow(Container parent)
    {
        var row=new HFlowContainer();
        row.AddThemeConstantOverride("h_separation",7);
        row.AddThemeConstantOverride("v_separation",6);
        parent.AddChild(row);
        return row;
    }

    static Button AppearanceButton(Container parent,string text,string name)
    {
        var button=new Button{Name=name,Text=text,CustomMinimumSize=new(0,36)};
        TownTheme.AccentButton(button,"外观",7);
        parent.AddChild(button);
        return button;
    }

    static OptionButton AppearancePicker(Container parent,string name,string description,int width)
    {
        var options=new OptionButton{Name=name,AccessibilityName=description,TooltipText=description,FitToLongestItem=false,
            TextOverrunBehavior=TextServer.OverrunBehavior.TrimEllipsis,CustomMinimumSize=new(width,36),
            SizeFlagsHorizontal=Control.SizeFlags.ExpandFill};
        TownTheme.AccentButton(options,"外观",7);
        parent.AddChild(options);
        return options;
    }
}
