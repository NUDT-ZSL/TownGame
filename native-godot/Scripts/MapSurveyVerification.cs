using Godot;
using System;
using System.Linq;

public partial class Main
{
    void VerifyMapSurvey(Action<bool,string> check)
    {
        var oldView=_rememberedMapView;
        var scene=(_center,_distance,_selection,_previousCameraSite);
        var past=_cameraPast.ToArray();var forward=_cameraForward.ToArray();
        string snapshot=Data.Snapshot.ToJsonString();
        try{
            _rememberedMapView=null;
            var map=OpenMap();
            var linked=Game.ConnectedRoads;
            var first=linked.First(cell=>linked.Contains(new(cell.X+1,cell.Y)));
            var second=new NativeGame.Cell(first.X+1,first.Y);
            var focus=WorldData.Cell(new(first.X,first.Y));
            _center=focus;
            Press("TownMapSceneCamera");
            check(map.SceneCameraCell==first&&map.ZoomPercent>=165,"镜头位置未聚焦地图上的真实坐标");
            var view=map.CurrentMapView;
            ShowManagement("日志");
            map=OpenMap();
            check(map.CurrentMapView==view,"切换页面后没有恢复地图缩放和中心");

            var pin=Find<CheckButton>("TownMapAddPin");
            pin.ButtonPressed=true;
            SelectMode(1);
            check(!pin.ButtonPressed&&!map.PinPlacementEnabled&&map.ToolMode==TownMap.SurveyMode.Inspect,"地块工具与放置图钉同时处理地图点击");
            var x=Find<LineEdit>("TownMapCoordinateX");var y=Find<LineEdit>("TownMapCoordinateY");
            x.Text="125";y.Text=first.Y.ToString();
            x.EmitSignal(LineEdit.SignalName.TextChanged,x.Text);
            check(Find<Button>("TownMapCoordinateGo").Disabled,"越界坐标仍可查找");
            x.Text=first.X.ToString();x.EmitSignal(LineEdit.SignalName.TextChanged,x.Text);
            Press("TownMapCoordinateGo");
            check(map.InspectedCell==first&&Find<Label>("TownMapSurveyText").Text.Contains("已连通庄园")&&_center==focus,
                "坐标查找未显示真实道路或直接改变三维镜头");
            map.ResetView();
            Click(second);
            check(map.InspectedCell==second&&x.Text==second.X.ToString()&&Find<Button>("TownMapCopyCoordinates").Disabled==false,
                "地图点击未检查实际地块并更新可复制坐标");

            SelectMode(2);
            Click(first);Click(second);
            check(map.MeasuredRoad is {Road:float length}&&Mathf.IsEqualApprox(length,WorldData.GridSize)&&
                Find<Label>("TownMapSurveyText").Text.Contains("道路 8 米"),"相邻道路格没有按真实道路中心测距");
            Press("TownMapClearMeasure");
            var offRoad=(from row in Enumerable.Range(0,80) from column in Enumerable.Range(0,125)
                let tile=new NativeGame.Cell(column,row) where !Data.Roads.ContainsKey(new(column,row)) select tile).First();
            Click(first);Click(offRoad);
            check(map.MeasuredRoad is {Road:null}&&Find<Label>("TownMapSurveyText").Text.Contains("两个道路格"),"非道路测距没有说明缺少道路端点");

            map=OpenMap($"cell:{second.X},{second.Y}");
            check(map.InspectedCell==second&&_managementFocusTarget?.Name=="TownMapSurveyPanel","地块入口未跳到对应的地图卡片");
            Press("TownMapInspectScene");
            check(_management?.Visible==false&&_center==WorldData.Cell(new(second.X,second.Y))&&Data.Snapshot.ToJsonString()==snapshot,
                "从地块跳到场景时位置错误或勘察改变了存档");
            GD.Print("MAP_SURVEY_VERIFIED scene_camera tile_coords remembered_view road_distance readonly");
        }finally{
            _management?.Hide();
            _rememberedMapView=oldView;
            (_center,_distance,_selection,_previousCameraSite)=scene;
            _cameraPast.Clear();_cameraPast.AddRange(past);
            _cameraForward.Clear();_cameraForward.AddRange(forward);
        }

        T Find<T>(string name) where T:Node=>(T)_managementBody!.FindChild(name,true,false)!;
        void Press(string name)=>Find<Button>(name).EmitSignal(BaseButton.SignalName.Pressed);
        void SelectMode(long id)=>Find<MenuButton>("TownMapSurveyMenu").GetPopup().EmitSignal(PopupMenu.SignalName.IdPressed,id);
        TownMap OpenMap(string target="")
        {
            ShowManagement("地图",target);
            var opened=Find<TownMap>("TownNavigationMap");
            opened.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.TopLeft);
            opened.Size=new(620,400);
            return opened;
        }
        void Click(NativeGame.Cell cell)
        {
            var position=WorldData.Cell(new(cell.X,cell.Y));
            var current=Find<TownMap>("TownNavigationMap");
            var point=current.WorldToView(new(position.X,position.Z));
            current._GuiInput(new InputEventMouseButton{Position=point,Pressed=true,ButtonIndex=MouseButton.Left});
            current._GuiInput(new InputEventMouseButton{Position=point,Pressed=false,ButtonIndex=MouseButton.Left});
        }
    }
}
