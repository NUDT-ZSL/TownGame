using Godot;
using System;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;

public partial class Main
{
    void VerifyTownMapTools(Action<bool,string> check)
    {
        var original=Data.Snapshot;
        var savedLayers=new MapLayerPreferences(_mapShowsLandValues,_mapShowsConstruction,_mapShowsDisconnected,
            _mapShowsFreight,_mapShowsResidents,_mapResidentProfession,_mapResidentStatus);
        bool suppressWrites=_firstDayGuideSuppressWrites;
        string preferencesPath=Path.Combine(Path.GetTempPath(),$"town-map-{Guid.NewGuid():N}.cfg");
        try{
            Data.Root["snapshot"]=original.DeepClone();
            Data.Reindex();
            _firstDayGuideSuppressWrites=true;
            var expected=new MapLayerPreferences(true,false,false,true,true,"农夫","在家");
            check(ReadMapLayerPreferences(preferencesPath)==DefaultMapLayers,"没有地图偏好文件时未恢复默认图层");
            check(WriteMapLayerPreferences(preferencesPath,expected)==Error.Ok&&ReadMapLayerPreferences(preferencesPath)==expected,
                "地图图层和村民筛选偏好无法写入后重读");
            Game.Execute("map_pin",new JsonObject{{"operation","add"},{"name","地图定位验收"},{"note","溪边小径"},
                {"x",60},{"y",39},{"journal",true}});
            var pin=Game.MapPins().Single(entry=>entry.Name=="地图定位验收");
            ShowManagement("地图");
            var map=Find<TownMap>("TownNavigationMap");
            map.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.TopLeft);
            map.Size=new(620,400);
            map.ResetView();
            Press("TownMapZoomIn");
            check(Find<Label>("TownMapZoomScale").Text=="125%"&&!Find<Button>("TownMapZoomOut").Disabled,"缩放按钮未同步显示当前比例");
            var midpoint=map.ViewToWorld(map.Size/2);
            map._GuiInput(new InputEventKey{Keycode=Key.Right,Pressed=true});
            check(map.ViewToWorld(map.Size/2).X>midpoint.X,"键盘方向键没有平移地图");
            map._GuiInput(new InputEventKey{Keycode=Key.Home,Pressed=true});
            check(map.ZoomPercent==100&&Find<Button>("TownMapZoomOut").Disabled,"Home 未回到整镇概览");

            Find<CheckButton>("TownMapConstruction").ButtonPressed=false;
            var legend=Find<HFlowContainer>("TownMapLegend");
            check(legend.GetChildren().OfType<Label>().Any(label=>label.Text=="▲ 施工 · 已隐藏")&&
                legend.GetChildren().OfType<Label>().Any(label=>label.Text==$"⌖ 图钉 · {Game.MapPins().Length}"),"图例未反映图层开关或图钉数");
            Find<CheckButton>("TownMapHeat").ButtonPressed=true;
            Press("TownMapResetLayers");
            check(map.ShowConstruction&&!map.Heat&&Find<CheckButton>("TownMapDisconnected").ButtonPressed,"恢复默认图层未同步地图与开关");

            Find<Button>("TownMapPinListToggle").ButtonPressed=true;
            var search=Find<LineEdit>("TownMapPinSearch");
            search.Text="不存在的备注";
            search.EmitSignal(LineEdit.SignalName.TextChanged,search.Text);
            check(_managementBody!.FindChild("SelectMapPin"+pin.Id,true,false)==null,"图钉搜索仍显示不匹配的项");
            search.Text="溪边";
            search.EmitSignal(LineEdit.SignalName.TextChanged,search.Text);
            Press("SelectMapPin"+pin.Id);
            var context=Find<PanelContainer>("TownMapPinContext");
            check(context.Visible&&context.FindChildren("*","Label",true,false).OfType<Label>().Any(label=>label.Text.Contains(pin.Name))
                &&map.ZoomPercent>=220,"图钉结果未定位并显示地图内详情");
            Press("TownMapPinEdit");
            var editor=_management!.GetChildren().OfType<ConfirmationDialog>().SingleOrDefault(dialog=>dialog.Name=="TownMapPinEditor");
            check(editor?.Visible==true,"选中图钉详情无法打开编辑器");
            editor!.Hide();editor.QueueFree();
            Press("TownMapPinMove");
            check(map.PinPlacementEnabled&&map.PinMoveEnabled,"移动图钉未进入选址状态");
            var destination=new NativeGame.Cell(62,41);
            var world=WorldData.Cell(new(destination.X,destination.Y));
            var screen=map.WorldToView(new(world.X,world.Z));
            map._GuiInput(new InputEventMouseButton{Position=screen,ButtonIndex=MouseButton.Left,Pressed=true});
            map._GuiInput(new InputEventMouseButton{Position=screen,ButtonIndex=MouseButton.Left,Pressed=false});
            check(Game.MapPins().Contains(pin with{Cell=destination})&&!map.PinMoveEnabled&&context.Visible,"移动图钉后标记内容或位置异常");
            Press("TownMapClearPin");
            check(!context.Visible,"清除选中未收起图钉详情");
            GD.Print("TOWN_MAP_TOOLS_VERIFIED keyboard_zoom layer_counts preferences pin_find_move_clear");
        }finally{
            _firstDayGuideSuppressWrites=suppressWrites;
            ApplyMapLayerPreferences(savedLayers);
            Data.Root["snapshot"]=original;
            Data.Reindex();
            _management?.Hide();
            File.Delete(preferencesPath);
        }

        T Find<T>(string name) where T:Node=>(T)_managementBody!.FindChild(name,true,false)!;
        void Press(string name)=>Find<Button>(name).EmitSignal(BaseButton.SignalName.Pressed);
    }
}
