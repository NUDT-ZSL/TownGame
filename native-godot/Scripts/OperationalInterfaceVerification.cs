using Godot;
using System;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;

public partial class Main
{
    void VerifyOperationalInterface(Action<bool,string> check)
    {
        var market=(_marketSection,_marketFilter,_marketSearch,_marketBuyQuantity,_orderSupplyRoute,_careSupplyRoute);
        var catalog=(_buildWorkspace,_buildCatalogQuery,_buildCatalogCategory,_buildCatalogBudgetOnly,_buildTownType,_buildTownId);
        var gold=Game.Meta["coins"]?.DeepClone();
        string temp=Path.Combine(Path.GetTempPath(),$"town-slot-ui-{Guid.NewGuid():N}");
        T ControlOnPage<T>(string name) where T:Control=>(T)_managementBody!.FindChild(name,true,false)!;
        void Press(string name)=>ControlOnPage<Button>(name).EmitSignal(BaseButton.SignalName.Pressed);
        try{
            const string item="清水";
            int price=Game.BuyPrice(item);
            int stored=Game.Rows("inventory").OfType<JsonNode>().Sum(batch=>NativeGame.I(batch,"quantity"));
            int free=Game.Storage-stored;
            Game.Meta["coins"]=price*2;
            _marketSection=1;_marketFilter=0;_marketSearch="";_marketBuyQuantity=5;
            _orderSupplyRoute=null;_careSupplyRoute=null;
            ShowManagement("商店");
            Press("MarketBuyPreset10");
            var quantity=ControlOnPage<SpinBox>("MarketBuyQuantity");
            var buy=ControlOnPage<Button>($"MarketBuy_{item}");
            string reason=free<10?"仓库空间不足":"金币不足";
            check(quantity.Value==10&&buy.Disabled&&ControlOnPage<Label>($"MarketBuyHint_{item}").Text.Contains(reason),
                "快捷份数没有更新报价或显示金币限制");
            var limit=ControlOnPage<Button>($"MarketBuyLimit_{item}");
            int max=Math.Clamp(free,0,2);
            if(max>0)limit.EmitSignal(BaseButton.SignalName.Pressed);
            check((max>0?quantity.Value==max&&!buy.Disabled:limit.Disabled)&&NativeGame.I(Game.Meta,"coins")==price*2&&
                Game.Rows("inventory").OfType<JsonNode>().Sum(batch=>NativeGame.I(batch,"quantity"))==stored,
                "填入购买上限应使用可支付数量并等待玩家确认");
            check(MarketPurchaseLimit(price,1,price*2,99)==1&&MarketPurchaseLimit(price,0,price*2,99)==0,
                "购买上限未遵守仓库剩余空间");

            _buildWorkspace=2;_buildCatalogQuery="";_buildCatalogCategory=0;_buildCatalogBudgetOnly=false;
            ShowManagement("建造");
            var search=ControlOnPage<LineEdit>("TownCatalogSearch");
            var picker=ControlOnPage<OptionButton>("TownCatalogPicker");
            int unfiltered=picker.ItemCount;
            search.Text="不存在的施工项目验收";
            search.EmitSignal(LineEdit.SignalName.TextChanged,search.Text);
            check(picker.ItemCount==0&&ControlOnPage<Button>("TownCatalogScene").Disabled,"目录无匹配时仍可进入施工");
            Press("TownCatalogReset");
            check(search.Text==""&&picker.ItemCount==unfiltered&&unfiltered>0&&!ControlOnPage<Button>("TownCatalogScene").Disabled,
                "清除筛选后未恢复施工目录");
            var budget=ControlOnPage<CheckButton>("TownCatalogBudget");
            budget.ButtonPressed=true;
            check(picker.ItemCount==Game.Rules["CATALOG"]!.AsObject().Count(pair=>NativeGame.I(pair.Value!,"cost")<=price*2),
                "目录预算筛选未使用实际施工费和金币余额");

            ShowManagement("存档");
            check(ControlOnPage<Label>("TownSaveWriteStatus").Text==TownSaveStatus&&
                ControlOnPage<Label>("TownSaveWriteDetail").Text.Length>0,"存档页未显示当前写入状态");
            Directory.CreateDirectory(temp);
            string save=Path.Combine(temp,"town.json"),photos=Path.Combine(temp,"photos");
            Directory.CreateDirectory(Path.Combine(photos,"slot-2"));
            File.WriteAllText(Path.Combine(photos,"scene.png"),"test");
            File.WriteAllText(Path.Combine(photos,"slot-2","other.png"),"test");
            Data.Save(save);
            new NativeSnapshotHistory(save).Record(DateTimeOffset.UtcNow,NativeSnapshotHistory.SnapshotKind.Manual);
            string files=DescribeSlotFiles(save,photos);
            check(files.Contains("主档写于")&&files.Contains("恢复点文件 1 个")&&files.Contains("照片 1 张"),
                "槽位预览未读取主档、恢复点或将其他槽照片计入本槽");
            GD.Print("OPERATIONAL_INTERFACE_VERIFIED purchase_limits catalog_filters slot_file_isolation");
        }finally{
            Game.Meta["coins"]=gold;
            (_marketSection,_marketFilter,_marketSearch,_marketBuyQuantity,_orderSupplyRoute,_careSupplyRoute)=market;
            (_buildWorkspace,_buildCatalogQuery,_buildCatalogCategory,_buildCatalogBudgetOnly,_buildTownType,_buildTownId)=catalog;
            if(Directory.Exists(temp))Directory.Delete(temp,true);
            _management?.Hide();
        }
    }
}
