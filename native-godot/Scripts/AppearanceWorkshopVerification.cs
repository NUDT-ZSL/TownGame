using Godot;
using System;
using System.Linq;
using System.Text.Json.Nodes;

public partial class Main
{
    void VerifyAppearanceWorkshop(Action<bool,string> check)
    {
        var original=Data.Snapshot;
        var prior=(_selectedPet,_selection,_firstDayGuideSuppressWrites);
        var drafts=_appearanceDrafts.ToArray();
        try{
            Data.Root["snapshot"]=original.DeepClone();
            Data.Reindex();
            _firstDayGuideSuppressWrites=true;
            _appearanceDrafts.Clear();
            var pet=Game.Pets.OfType<JsonNode>().First(Game.Active);
            string id=NativeGame.S(pet,"pet_id"),species=NativeGame.S(pet,"species");
            const string otherId="appearance-verification",otherName="工坊验收伙伴";
            string otherSpecies=Game.Rules["SPECIES"]!.AsObject().Select(row=>row.Key).First(key=>key!=species);
            var other=pet.DeepClone();
            other["pet_id"]=otherId;
            other["name"]=otherName;
            other["species"]=otherSpecies;
            other["fostered"]=true;
            Game.Pets.Add(other);
            Game.Meta["pet_appearances"]![id]=0;
            Game.Meta["pet_appearances"]![otherId]=16;
            Game.Meta["pet_initial_appearances"]![id]=3;
            Game.Meta["style_favorites"]=new JsonObject{{species,new JsonArray(JsonValue.Create(5))}};
            _selectedPet=id;
            ShowManagement("外观");
            var picker=Find<OptionButton>("AppearancePetPicker");
            var identity=Find<Label>("AppearancePetIdentity");
            var status=Find<Label>("AppearanceDraftState");
            var apply=Find<Button>("AppearanceApply");
            var compare=Find<Button>("AppearanceCompare");
            var palette=Find<Button>("AppearanceCoat2");
            var model=Find<MeshInstance3D>("AppearancePreviewModel");
            var originalMesh=model.Mesh;
            check(identity.Text.Contains(NativeGame.S(pet,"name"))&&identity.Text.Contains(species),"外观工坊未标明所选伙伴身份");
            Query("无匹配验收搜索");
            check(picker.Disabled&&_selectedPet==id,"外观搜索无结果时丢失当前编辑对象");
            Query(otherName);
            check(picker.ItemCount==1&&picker.GetItemText(0).Contains(otherSpecies),"外观搜索未查到实际伙伴或缺少物种");
            Select(picker,0);
            check(_selectedPet==otherId&&identity.Text.Contains(otherName)&&Find<Button>("AppearanceFavoriteLoad").Disabled,
                "切换伙伴后仍使用上一物种的收藏");
            Query(NativeGame.S(pet,"name"));
            int index=Enumerable.Range(0,picker.ItemCount).First(item=>picker.GetItemText(item).Contains(species));
            Select(picker,index);
            check(_selectedPet==id&&palette.Icon!=null&&palette.Text==AppearanceGroups(species)[0][2],"颜色控件未随伙伴更新");

            Press("AppearanceCoat2");
            Select(Find<OptionButton>("AppearanceTrait1"),1);
            check(status.Text.Contains("未保存草稿")&&Game.Appearance(pet)==0&&!apply.Disabled&&model.Mesh!=originalMesh,
                "毛色与花纹没有进入独立预览草稿");
            var draftMesh=model.Mesh;
            var angle=Find<HSlider>("AppearancePreviewAngle");
            var pivot=Find<Node3D>("AppearancePreviewPivot");
            angle.Value=170;
            check(Mathf.IsEqualApprox(pivot.RotationDegrees.Y,170)&&model.Mesh==draftMesh,"旋转预览没有更新模型朝向或修改了草稿");
            Press("AppearanceResetAngle");
            check(Mathf.IsEqualApprox(pivot.RotationDegrees.Y,28),"重置角度没有恢复默认朝向");

            compare.ButtonPressed=true;
            check(model.Mesh==originalMesh&&apply.Disabled&&status.Text.Contains("草稿仍保留"),"对照已保存外观没有保留未提交草稿");
            compare.ButtonPressed=false;
            check(model.Mesh==draftMesh&&!apply.Disabled,"返回草稿后没有恢复原预览");
            Press("AppearanceDiscard");
            check(model.Mesh==originalMesh&&apply.Disabled&&Game.Appearance(pet)==0,"丢弃草稿改变了已保存外观");
            Press("AppearanceInitial");
            check(status.Text.Contains(NativeAppearance.Description(species,3))&&Game.Appearance(pet)==0,"预览初始外观提前写入存档");

            var favorite=Find<OptionButton>("AppearanceFavorites");
            check(favorite.ItemCount==1&&favorite.GetItemText(0)==NativeAppearance.Description(species,5),"旧存档的收藏样式未加载");
            Press("AppearanceFavoriteLoad");
            check(status.Text.Contains(NativeAppearance.Description(species,5))&&Game.Appearance(pet)==0,"载入收藏未遵循草稿保存流程");
            Press("AppearanceFavoriteRemove");
            check(Find<Button>("AppearanceFavoriteLoad").Disabled&&!apply.Disabled,"移除收藏导致当前草稿丢失");
            Press("AppearanceFavoriteAdd");
            check(Game.Meta["style_favorites"]![species]![0]!.GetValue<int>()==5&&!apply.Disabled,"收藏草稿改变了已保存外观");
            Press("AppearanceApply");
            check(Game.Appearance(pet)==5&&Game.Appearance(other)==16&&apply.Disabled&&status.Text.StartsWith("已保存外观"),
                "保存没有应用到正确伙伴或未原地清除草稿状态");
            GD.Print("APPEARANCE_WORKSHOP_VERIFIED pet_search palette rotate compare drafts legacy_favorites explicit_apply");
        }finally{
            _management?.Hide();
            Data.Root["snapshot"]=original;
            Data.Reindex();
            _businessRevision=-1;
            _appearanceDrafts.Clear();
            foreach(var pair in drafts)_appearanceDrafts.Add(pair.Key,pair.Value);
            (_selectedPet,_selection,_firstDayGuideSuppressWrites)=prior;
        }

        T Find<T>(string name) where T:Node=>(T)_managementBody!.FindChild(name,true,false)!;
        void Press(string name)=>Find<Button>(name).EmitSignal(BaseButton.SignalName.Pressed);
        void Query(string value)
        {
            var search=Find<LineEdit>("AppearancePetSearch");
            search.Text=value;
            search.EmitSignal(LineEdit.SignalName.TextChanged,value);
        }
        static void Select(OptionButton option,int index)
        {
            option.Select(index);
            option.EmitSignal(OptionButton.SignalName.ItemSelected,(long)index);
        }
    }
}
