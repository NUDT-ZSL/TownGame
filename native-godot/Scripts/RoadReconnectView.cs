using Godot;
using System;
using System.Linq;
using System.Text.Json.Nodes;

public partial class Main
{
    Action<NativeGame.Cell> AddRoadReconnectControls(TownMap map,Action<Vector2> locate)
    {
        var card=MarketCard(_managementBody!,color:"f4ecda");
        var panel=card.GetParent<PanelContainer>();
        panel.Name="RoadReconnectPreview";
        panel.Hide();
        MarketLabel(card,"接通断开道路",18,TownTheme.Accent("建造"));
        var details=MarketLabel(card,"",color:TownTheme.Ink,wrap:true);
        details.Name="RoadReconnectDetails";
        var actions=new HFlowContainer();
        actions.AddThemeConstantOverride("h_separation",8);
        card.AddChild(actions);
        NativeGame.Cell? selected=null;
        Vector2 selectedPoint=default;
        NativeGame.ReconnectPreview? preview=null;
        string style="asphalt";
        long revision=-1;
        int coins=-1;
        JsonObject? state=null;
        var confirm=new Button{Name="ConfirmRoadReconnect",Text="确认铺路"};
        TownTheme.AccentButton(confirm,"建造",6);
        actions.AddChild(confirm);
        confirm.Pressed+=Confirm;
        var locateButton=MarketButton(actions,"定位现场",()=>locate(selectedPoint),expand:false,accent:"地图");
        locateButton.Name="LocateDisconnectedRoad";
        MarketButton(actions,"关闭预览",()=>{
            selected=null;
            panel.Hide();
            map.ShowRepairPreview(null,Array.Empty<NativeGame.Cell>());
        },expand:false,accent:"地图");

        map.DisconnectedRoadSelected+=point=>{
            var grid=WorldData.Grid(new(point.X,0,point.Y));
            SelectRoad(new(grid.X,grid.Y));
            Callable.From(()=>{
                if(GodotObject.IsInstanceValid(panel)&&panel.IsInsideTree())_managementScroll?.EnsureControlVisible(panel);
            }).CallDeferred();
        };
        _refreshManagementPage+=()=>Refresh();
        return cell=>{
            map.FocusMapPin(cell);
            SelectRoad(cell);
            _managementFocusTarget=panel;
        };

        void SelectRoad(NativeGame.Cell cell)
        {
            selected=cell;
            var point=WorldData.Cell(new(cell.X,cell.Y));
            selectedPoint=new(point.X,point.Z);
            style=Data.Roads.TryGetValue(new(cell.X,cell.Y),out var road)?NativeGame.S(road,"road_style","asphalt"):"asphalt";
            panel.Show();
            Refresh(true);
        }

        void Refresh(bool force=false)
        {
            if(selected is not NativeGame.Cell cell)return;
            int balance=NativeGame.I(Data.Meta,"coins");
            if(!force&&revision==Game.WorldRevision&&balance==coins&&ReferenceEquals(state,Data.Snapshot))return;
            revision=Game.WorldRevision;
            coins=balance;
            state=Data.Snapshot;
            preview=Game.PreviewReconnect(cell,style);
            map.ShowRepairPreview(cell,preview.Cells);
            confirm.Disabled=preview.Reason.Length>0||preview.Cells.Length==0;
            confirm.Text=preview.Cells.Length>0?$"确认铺路 · {preview.Cost} 金币":"确认铺路";
            if(preview.Cells.Length==0){details.Text=preview.Reason;return;}
            string regions=preview.Districts.Length>0?$"\n可恢复街区通行 · {string.Join("、",preview.Districts)}":"";
            string buildings=preview.Buildings.Length>0?$"\n可恢复 {preview.Buildings.Length} 处建筑入口 · {string.Join("、",preview.Buildings.Take(5))}{(preview.Buildings.Length>5?" 等":"")}":"\n该路段暂未服务建筑入口。";
            string material=NativeGame.S(Game.Spec("ROAD_STYLES",style),"name",style);
            details.Text=$"新增 {preview.Cells.Length} 格{material} · {preview.Cost} 金币 · 金色格为施工范围{regions}{buildings}"+
                (preview.Reason.Length>0?$"\n{preview.Reason}":"");
        }

        void Confirm()
        {
            if(selected is not NativeGame.Cell cell||preview==null||preview.Cells.Length==0||preview.Reason.Length>0)return;
            var fresh=Game.PreviewReconnect(cell,style);
            if(fresh.Reason.Length>0||fresh.Cost!=preview.Cost||!fresh.Cells.SequenceEqual(preview.Cells)
                ||!fresh.Districts.SequenceEqual(preview.Districts)||!fresh.Buildings.SequenceEqual(preview.Buildings)){
                Refresh(true);
                SetManagementFeedback("道路状态有变化，请核对更新后的预览。",true);
                return;
            }
            Act("road_stroke",new JsonObject{
                ["style"]=style,["cells"]=new JsonArray(fresh.Cells.Select(c=>(JsonNode)new JsonArray(c.X,c.Y)).ToArray())
            });
        }
    }
}
