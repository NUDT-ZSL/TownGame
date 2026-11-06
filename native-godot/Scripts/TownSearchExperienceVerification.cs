using Godot;
using System;
using System.Linq;
using System.Text.Json.Nodes;

public partial class Main
{
    void VerifyTownSearchExperience(Action<bool,string> check)
    {
        var recents=_recentTownSearch.ToArray();
        var view=(_center,_distance,_selection,_selectedPet,_portraitAnchor,_sceneInspection);
        try{
            var farm=TownSearchMatches("农田　浇水","页面").Single();
            check(farm.Title=="农田地块","跨字段或全角分隔词未找到农田设置");
            farm.Open();
            check(_tab=="环境"&&_managementEntryTarget=="farm-plots","功能搜索未跳到实际设置入口");
            var items=TownSearchMatches("物品","物品");
            check(items.Length==Game.Rules["ITEMS"]!.AsObject().Count&&items.Length>TownSearchPageSize,"搜索结果仍被截断");
            check(TownSearchMatches("清水","全部")[0].Title=="清水","精确商品名没有优先排列");

            OpenTownSearch();
            var window=_townSearchWindow!;
            var input=(LineEdit)window.FindChild("TownSearchQuery",true,false)!;
            var rows=(VBoxContainer)window.FindChild("TownSearchResults",true,false)!;
            var page=(Label)window.FindChild("TownSearchPage",true,false)!;
            var preview=(Label)window.FindChild("TownSearchPreviewName",true,false)!;
            var filter=(OptionButton)window.FindChild("TownSearchCategory",true,false)!;
            Query("物品");
            check(rows.GetChildCount()==TownSearchPageSize&&filter.GetItemText(Array.IndexOf(TownSearchCategories,"物品"))==$"物品 · {items.Length}",
                "搜索第一页或分类计数不正确");
            var next=(Button)window.FindChild("TownSearchNextPage",true,false)!;
            next.EmitSignal(BaseButton.SignalName.Pressed);
            check(page.Text.StartsWith("第 2 /")&&preview.Text==items[TownSearchPageSize].Title,"翻页没有更新选中结果预览");
            Query("不会匹配的小镇验收词");
            check(rows.GetChildCount()==0&&next.Disabled,"空搜索仍允许翻页或显示旧结果");

            var pet=Data.Pets.OfType<JsonNode>().First(entry=>Game.Active(entry)&&FarmPet(NativeGame.S(entry,"pet_id"))!=null);
            string petId=NativeGame.S(pet,"pet_id");
            Query(petId);
            var locate=(Button)window.FindChild("TownSearchAlternate",true,false)!;
            check(locate.Visible&&locate.Text=="定位到场景"&&preview.Text==NativeGame.S(pet,"name"),"伙伴结果缺少定位快捷操作");
            window.EmitSignal(Window.SignalName.WindowInput,new InputEventKey{Keycode=Key.Enter,Pressed=true,ShiftPressed=true});
            check(_townSearchWindow==null&&_selectedPet==petId&&_management?.Visible==false,"Shift+Enter 未定位当前搜索结果");
            GD.Print("TOWN_SEARCH_EXPERIENCE_VERIFIED pages multi_terms preview alternate pagination");

            void Query(string value){input.Text=value;input.EmitSignal(LineEdit.SignalName.TextChanged,value);}
        }finally{
            if(GodotObject.IsInstanceValid(_townSearchWindow))_townSearchWindow!.EmitSignal(Window.SignalName.CloseRequested);
            _recentTownSearch.Clear();_recentTownSearch.AddRange(recents);
            (_center,_distance,_selection,_selectedPet,_portraitAnchor,_sceneInspection)=view;
            _management?.Hide();
        }
    }
}
