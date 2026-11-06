using Godot;
using System;
using System.Collections.Generic;

public partial class Main
{
    const string MapLayerPreferencesPath="user://town-map-layers.cfg";
    readonly record struct MapLayerPreferences(bool Heat,bool Projects,bool Broken,bool Freight,bool Residents,string Profession,string Status);
    static readonly MapLayerPreferences DefaultMapLayers=new(false,true,true,true,false,"","");
    bool _mapPreferencesLoaded;

    void LoadMapLayerPreferences()
    {
        if(_mapPreferencesLoaded)return;
        _mapPreferencesLoaded=true;
        if(UseTownPreferences())ApplyMapLayerPreferences(ReadMapLayerPreferences(MapLayerPreferencesPath));
    }

    void ApplyMapLayerPreferences(MapLayerPreferences preferences)
    {
        (_mapShowsLandValues,_mapShowsConstruction,_mapShowsDisconnected,_mapShowsFreight,_mapShowsResidents)=
            (preferences.Heat,preferences.Projects,preferences.Broken,preferences.Freight,preferences.Residents);
        (_mapResidentProfession,_mapResidentStatus)=(preferences.Profession,preferences.Status);
    }

    static MapLayerPreferences ReadMapLayerPreferences(string path)
    {
        using var config=new ConfigFile();
        if(config.Load(path)!=Error.Ok)return DefaultMapLayers;
        return new(config.GetValue("layers","heat",false).AsBool(),config.GetValue("layers","projects",true).AsBool(),
            config.GetValue("layers","broken",true).AsBool(),config.GetValue("layers","freight",true).AsBool(),
            config.GetValue("layers","residents",false).AsBool(),config.GetValue("residents","profession","").AsString(),
            config.GetValue("residents","status","").AsString());
    }

    static Error WriteMapLayerPreferences(string path,MapLayerPreferences preferences)
    {
        using var config=new ConfigFile();
        config.SetValue("layers","heat",preferences.Heat);
        config.SetValue("layers","projects",preferences.Projects);
        config.SetValue("layers","broken",preferences.Broken);
        config.SetValue("layers","freight",preferences.Freight);
        config.SetValue("layers","residents",preferences.Residents);
        config.SetValue("residents","profession",preferences.Profession);
        config.SetValue("residents","status",preferences.Status);
        return config.Save(path);
    }

    void SaveMapLayerPreferences()
    {
        if(!UseTownPreferences())return;
        var preferences=new MapLayerPreferences(_mapShowsLandValues,_mapShowsConstruction,_mapShowsDisconnected,
            _mapShowsFreight,_mapShowsResidents,_mapResidentProfession,_mapResidentStatus);
        if(WriteMapLayerPreferences(MapLayerPreferencesPath,preferences)!=Error.Ok)
            SetManagementFeedback("地图图层偏好暂时无法保存。",true);
    }

    void AddMapZoomTools(HFlowContainer toolbar,TownMap map)
    {
        var zoomOut=MarketButton(toolbar,"−",()=>map.ChangeZoom(-1),expand:false,accent:"地图");
        zoomOut.Name="TownMapZoomOut";
        zoomOut.AccessibilityName="缩小地图";
        zoomOut.TooltipText="缩小地图（减号键）";
        var scale=MarketLabel(toolbar,"100%",color:TownTheme.Ink);
        scale.Name="TownMapZoomScale";
        scale.HorizontalAlignment=HorizontalAlignment.Center;
        scale.VerticalAlignment=VerticalAlignment.Center;
        scale.CustomMinimumSize=new(65,0);
        var zoomIn=MarketButton(toolbar,"+",()=>map.ChangeZoom(1),expand:false,accent:"地图");
        zoomIn.Name="TownMapZoomIn";
        zoomIn.AccessibilityName="放大地图";
        zoomIn.TooltipText="放大地图（加号键）";
        var reset=MarketButton(toolbar,"全镇概览",map.ResetView,expand:false,accent:"地图");
        reset.Name="TownMapReset";
        reset.TooltipText="恢复整镇地图（Home 键）";
        map.ViewChanged+=Refresh;
        Refresh();

        void Refresh()
        {
            scale.Text=$"{map.ZoomPercent}%";
            zoomOut.Disabled=!map.CanZoomOut;
            zoomIn.Disabled=!map.CanZoomIn;
        }
    }

    void AddMapLayerControls(HFlowContainer toolbar,HFlowContainer residentFilters,TownMap map)
    {
        var toggles=new List<(CheckButton Button,bool Default)>();
        Add("TownMapConstruction","施工项目","商店",_mapShowsConstruction,true,"三角标记可打开工程",on=>map.ShowConstruction=_mapShowsConstruction=on);
        Add("TownMapDisconnected","断开道路","事件",_mapShowsDisconnected,true,"菱形标记可预览接线",on=>map.ShowDisconnected=_mapShowsDisconnected=on);
        Add("TownMapFreight","货运路线","地图",_mapShowsFreight,true,"方形是货车，蓝线是剩余路线",on=>map.ShowFreight=_mapShowsFreight=on);
        Add("TownMapHeat","地价热图","环境",_mapShowsLandValues,false,"绿色为低地价，橙红色为高地价",on=>map.Heat=_mapShowsLandValues=on);
        Add("TownMapResidents","村民","家庭",_mapShowsResidents,false,"按职业或当前状态查看村民",on=>{
            _mapShowsResidents=on;
            residentFilters.Visible=on;
            map.SetResidentLayer(on);
        });
        var resetFilters=ResidentMapFilters(residentFilters,map,_mapShowsResidents,SaveMapLayerPreferences);
        var reset=MarketButton(toolbar,"恢复默认图层",()=>{
            ApplyMapLayerPreferences(DefaultMapLayers);
            foreach(var (button,initial) in toggles)button.SetPressedNoSignal(initial);
            map.ShowConstruction=map.ShowDisconnected=map.ShowFreight=true;
            map.Heat=false;
            residentFilters.Hide();
            resetFilters();
            map.SetResidentLayer(false);
            SaveMapLayerPreferences();
        },expand:false,accent:"地图");
        reset.Name="TownMapResetLayers";

        void Add(string name,string caption,string accent,bool enabled,bool initial,string tip,Action<bool> set)
        {
            var toggle=new CheckButton{Name=name,Text=caption,ButtonPressed=enabled,TooltipText=tip};
            TownTheme.AccentButton(toggle,accent,6);
            toggle.Toggled+=on=>{set(on);map.RefreshData();SaveMapLayerPreferences();};
            toolbar.AddChild(toggle);
            toggles.Add((toggle,initial));
        }
    }

    void AddMapLayerLegend(TownMap map)
    {
        var legend=new HFlowContainer{Name="TownMapLegend"};
        legend.AddThemeConstantOverride("h_separation",12);
        legend.AddThemeConstantOverride("v_separation",3);
        _managementBody!.AddChild(legend);
        var projects=Legend("▲ 施工","955020","施工工程数量");
        var broken=Legend("◆ 断路","a44133","不通庄园的独立路段数量");
        var freight=Legend("■ 货车","2d5990","当前可定位的货车");
        var residents=Legend("● 村民","52657e","符合当前筛选的可定位村民");
        var pins=Legend("⌖ 图钉","76517c","当前存档的地图图钉");
        Legend("━ 道路","536b57","连通庄园的道路");
        Legend("▧ 建筑","8e6638","建筑占地");
        Legend("≈ 水域","407993","水域");
        Legend("◈ 镜头","245f60","三维镜头的焦点和朝向");
        var heat=Legend("地价 · 低 → 高","975138","绿色为低地价，橙红色为高地价");
        map.LayersChanged+=Refresh;
        Refresh();

        Label Legend(string caption,string ink,string explanation)
        {
            var key=new Label{Text=caption,TooltipText=explanation,MouseFilter=Control.MouseFilterEnum.Pass};
            key.AddThemeFontSizeOverride("font_size",14+ExtraFontSize);
            key.AddThemeColorOverride("font_color",TownTheme.HighContrast?TownTheme.Ink:new Color(ink));
            legend.AddChild(key);
            return key;
        }

        void Refresh()
        {
            var counts=map.LayerCounts();
            Set(projects,"▲ 施工",map.ShowConstruction,counts.Projects);
            Set(broken,"◆ 断路",map.ShowDisconnected,counts.Disconnected);
            Set(freight,"■ 货车",map.ShowFreight,counts.Freight);
            Set(residents,"● 村民",_mapShowsResidents,counts.Residents);
            Set(pins,"⌖ 图钉",true,counts.Pins);
            heat.Visible=map.Heat;
        }

        static void Set(Label label,string caption,bool enabled,int count)
        {
            label.Text=enabled?$"{caption} · {(count==0?"暂无":count.ToString())}":$"{caption} · 已隐藏";
            label.Modulate=enabled||TownTheme.HighContrast?Colors.White:new Color("afafaf");
        }
    }
}
