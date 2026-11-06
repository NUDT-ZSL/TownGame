using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

public partial class Main
{
    sealed record ManagementVisit(string Tab,string Target,int Scroll,Action Restore);
    readonly List<ManagementVisit> _managementHistory=new();
    Button? _managementBack;
    int? _managementRestoredScroll;

    void RecordManagementVisit(string tab,string target,bool opening)
    {
        if(_managementRestoredScroll.HasValue)return;
        if(opening){_managementHistory.Clear();return;}
        if(tab==_tab&&target==_managementEntryTarget)return;
        var selection=(_selectedPet,_selectedResidence,_buildTownId);
        var orders=(_orderFilter,_orderHistoryPage);
        var supplies=(_orderSupplyRoute,_careSupplyRoute,_marketSection,_marketFilter,_marketSearch,_marketBuyQuantity,_marketSellQuantity);
        var residents=(_residentDirectoryOpen,_residentDirectoryQuery,_residentDirectoryFilter,_residentDirectoryPage);
        string previous=_tab;
        _managementHistory.Add(new(previous,_managementEntryTarget,_managementScroll?.ScrollVertical??0,()=>{
            if(previous is "宠物" or "档案" or "外观")_selectedPet=selection._selectedPet;
            if(previous=="建造")_buildTownId=selection._buildTownId;
            if(previous=="订单")(_orderFilter,_orderHistoryPage)=orders;
            if(previous=="商店")(_orderSupplyRoute,_careSupplyRoute,_marketSection,_marketFilter,_marketSearch,_marketBuyQuantity,_marketSellQuantity)=supplies;
            if(previous=="家庭"){
                _selectedResidence=selection._selectedResidence;
                (_residentDirectoryOpen,_residentDirectoryQuery,_residentDirectoryFilter,_residentDirectoryPage)=residents;
            }
        }));
        if(_managementHistory.Count>24)_managementHistory.RemoveAt(0);
    }

    void GoBackInManagement()
    {
        if(_managementHistory.Count==0||_management?.Visible!=true)return;
        var visit=_managementHistory[^1];
        _managementHistory.RemoveAt(_managementHistory.Count-1);
        _managementRestoredScroll=visit.Scroll;
        try{ShowManagement(visit.Tab,visit.Target,visit.Restore);}
        finally{_managementRestoredScroll=null;}
    }

    void AddManagementBackButton(Container header)
    {
        _managementBack=new Button{Name="ManagementBack",Text="‹ 返回",TooltipText="回到上一步（Alt+←）",
            SizeFlagsVertical=Control.SizeFlags.ShrinkCenter,Visible=false};
        TownTheme.AccentButton(_managementBack,"地图",5);
        _managementBack.Pressed+=GoBackInManagement;
        header.AddChild(_managementBack);
    }

    void UpdateManagementBack()
    {
        if(_managementBack==null)return;
        _managementBack.Visible=_managementHistory.Count>0;
        if(_managementHistory.Count==0)return;
        var visit=_managementHistory[^1];
        string detail=visit.Target.Split(':',2)[0] switch{
            "residents"=>"村民名册","residence"=>"家庭住宅","interior"=>"住宅室内",
            "display"=>"界面显示","keybindings"=>"快捷键设置","farm-plots"=>"农田地块",
            "district" when visit.Tab=="地图"=>"街区地图","public-stock"=>"公共仓库",
            "codex"=>"物种图鉴","companion" or "companions"=>"伙伴档案","records"=>"小镇记录",
            "companion-shop"=>"商店伙伴","tourism"=>"游客游览","projects"=>"工程名册","scheduled-expenses"=>"计划支出",
            "time-plan"=>"时间计划","workshop-queue"=>"加工队列",
            "weekend-festivals"=>"周末活动","pending-events"=>"待处理事件","decor"=>"装饰目录","placed-decor"=>"已摆放装饰",
            "supply"=>"商店物资","recipe"=>"加工坊","supply-watch"=>"物资预警",
            "care"=>"伙伴照护",_=>visit.Tab
        };
        _managementBack.Text="‹ "+detail;
        _managementBack.AccessibilityName="返回「"+detail+"」";
        _managementBack.TooltipText=$"{_managementBack.AccessibilityName}（Alt+←）\n可返回 {_managementHistory.Count} 步";
    }

    void VerifyManagementReturn(Action<bool,string> check)
    {
        var resident=Data.People.FirstOrDefault(row=>row!=null&&NativeGame.S(row,"role")=="居民"&&Game.ResidentHome(row)!=null)!;
        string id=NativeGame.S(resident,"person_id"),home=NativeGame.S(Game.ResidentHome(resident)!,"building_id");
        var oldFilter=_orderFilter;
        _management?.Hide();
        try{
            _orderFilter=OrderFilter.Missing;
            ShowManagement("订单","验收订单");
            OpenResidentDirectoryEntry(id);
            SelectResidence(home);
            check(_managementHistory.Count==2&&_managementBack?.Visible==true&&_managementEntryTarget=="residence:"+home,
                "订单、居民和住宅之间的跳转未建立历史");
            _managementBack!.EmitSignal(BaseButton.SignalName.Pressed);
            check(_tab=="家庭"&&_managementEntryTarget=="residents:"+id&&_residentDirectoryQuery==NativeGame.S(resident,"name"),"返回住宅之前的居民筛选失败");
            _orderFilter=OrderFilter.Active;
            _managementBack.EmitSignal(BaseButton.SignalName.Pressed);
            check(_tab=="订单"&&_managementEntryTarget=="验收订单"&&_orderFilter==OrderFilter.Missing&&!_managementBack.Visible,
                "返回订单未恢复筛选或历史栈未出栈");
        }finally{_orderFilter=oldFilter;_management?.Hide();}
    }
}
