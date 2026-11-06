using Godot;
using System;
using System.Linq;
using System.Text.Json.Nodes;

public partial class Main
{
    void VerifyTownUiExpansion(Action<bool,string> check)
    {
        var view=(_center,_distance,_selection,_portraitAnchor,_sceneInspection,_pinnedSceneInspection);
        var snapshot=Data.Snapshot.DeepClone();
        var args=_placementArgs;
        bool snap=_snapPlacementToRoad;
        _management?.Hide();
        try{
            var road=Game.ConnectedRoads.First();
            check(Game.BuildCellConflicts(road),"建造冲突未识别已连通道路");
            check(NativeGame.EntranceDirection(0)==new NativeGame.Cell(0,1)&&NativeGame.EntranceDirection(1)==new NativeGame.Cell(-1,0),"正门方向未随建筑旋转");
            const string catalog="cream_home";
            var footprint=Game.Spec("CATALOG",catalog)["footprint"]!;
            int width=footprint[0]!.GetValue<int>(),depth=footprint[1]!.GetValue<int>();
            _placementArgs=new JsonObject{{"catalog_key",catalog},{"orientation",0}};
            var raw=Game.ConnectedRoads.Select(road=>new NativeGame.Cell(road.X-width/2,road.Y-depth-1)).First(cell=>
                IsMapCell(new(cell.X,cell.Y))&&!Game.HasFrontRoad(cell,width,depth,0)&&Game.PlacementReason(catalog,cell.X,cell.Y+1)=="");
            _snapPlacementToRoad=true;
            var snapped=AssistedTownPlacement(new(raw.X,raw.Y));
            check(snapped.Snapped&&snapped.Cell==new Vector2I(raw.X,raw.Y+1),"选址没有吸附到一格内的正门道路");
            _snapPlacementToRoad=false;
            check(AssistedTownPlacement(new(raw.X,raw.Y)).Cell==new Vector2I(raw.X,raw.Y),"关闭吸附后仍改动选址");
            UpdatePlacementGuide(new(road.X,road.Y),2,2,"verify-placement-guide",new[]{road},new(1,0));
            check(_placementGuide is {Visible:true,Mesh:ImmediateMesh mesh}&&mesh.GetSurfaceCount()==2,"格线、冲突格或入口箭头缺少可绘制几何");
            EndPlacement();

            var resident=ResidentDirectoryEntries().First(entry=>DirectoryActor(entry.Id)?.Visual.Mesh!=null);
            var vehicle=Agents.First(actor=>actor.IsCar&&actor.Visual.Mesh!=null);
            var residentTarget=new ScenePick("person",resident.Id,DirectoryActor(resident.Id)!.Visual,default);
            var vehicleTarget=new ScenePick("vehicle",vehicle.Ident,vehicle.Visual,default);
            ScenePick? accepted=null;
            ShowScenePickChoices(new(240,180),new[]{residentTarget,vehicleTarget},target=>accepted=target);
            check(_scenePickChooser?.Visible==true&&_scenePickedMesh==residentTarget.Mesh,"重叠轮选未高亮当前对象");
            HandleScenePickChooserInput(new InputEventKey{Pressed=true,Keycode=Key.Down});
            HandleScenePickChooserInput(new InputEventKey{Pressed=true,Keycode=Key.Enter});
            check(accepted==vehicleTarget&&_scenePickChooser?.Visible==false,"键盘轮选没有选中正确车辆");
            SetScenePickedOutline(null);

            FocusSceneInspection("person",resident.Id,true);
            var pet=Data.Pets.OfType<JsonNode>().First();
            TrackSceneInspectorSelection(new("pet",NativeGame.S(pet,"pet_id"),null,default));
            UpdateSceneInspector(0);
            check(_sceneInspector?.Visible==true&&_sceneInspectorTitle!.Text.Contains(resident.Name),"固定的场景资料被新选中的伙伴覆盖");

            var result=TownSearchMatches(resident.Id,"村民").Single(entry=>entry.Id==resident.Id);
            check(result.Detail.Contains(resident.Profession)&&TownSearchMatches(resident.Id,"物品").Length==0,"搜索类别或村民职业区分失效");
            RecordTownSearchRecent(result);
            check(TownSearchMatches("","村民").Any(entry=>entry.Id==resident.Id),"最近搜索记录无法按类别打开");

            Game.Execute("map_pin",new JsonObject{{"operation","add"},{"name","验收图钉"},{"note","留给下一次施工"},
                {"x",56},{"y",41},{"journal",true}});
            var pin=Game.MapPins().Single(entry=>entry.Name=="验收图钉");
            var clone=new NativeGame(Data.Root.DeepClone().AsObject());
            check(clone.MapPins().Contains(pin),"图钉未随存档重载");
            ShowManagement("地图");
            var map=(TownMap)_managementBody!.FindChild("TownNavigationMap",true,false)!;
            map.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.TopLeft);
            map.Size=new(630,400);
            map.FocusMapPin(pin.Cell);
            map.RefreshData();
            var center=WorldData.Cell(new(pin.Cell.X,pin.Cell.Y));
            var screen=map.WorldToView(new(center.X,center.Z));
            string? selected=null;
            map.PinSelected+=id=>selected=id;
            map._GuiInput(new InputEventMouseButton{ButtonIndex=MouseButton.Left,Pressed=true,Position=screen});
            map._GuiInput(new InputEventMouseButton{ButtonIndex=MouseButton.Left,Pressed=false,Position=screen});
            check(selected==pin.Id,"点击地图图钉没有选择该标记");
            var dialog=_management!.GetChildren().OfType<ConfirmationDialog>().FirstOrDefault(node=>node.Name=="TownMapPinEditor");
            dialog?.Hide();dialog?.QueueFree();
            ShowManagement("今日待办");
            var locate=_managementBody.FindChild("LocateJournalPin"+pin.Id,true,false) as Button;
            check(locate!=null,"图钉未进入今日待办");
            locate!.EmitSignal(BaseButton.SignalName.Pressed);
            check(_management.Visible==false&&_center.IsEqualApprox(center),"待办图钉没有定位场景坐标");
            GD.Print("TOWN_UI_EXPANSION_VERIFIED guides pick inspector search pins");
        }finally{
            Data.Root["snapshot"]=snapshot;
            (_center,_distance,_selection,_portraitAnchor,_sceneInspection,_pinnedSceneInspection)=view;
            _placementArgs=args;
            _snapPlacementToRoad=snap;
            EndPlacement();
            _management?.Hide();
            CloseScenePickChooser(true);
            SetScenePickedOutline(null);
            UpdateSceneInspector(0);
        }
    }
}
