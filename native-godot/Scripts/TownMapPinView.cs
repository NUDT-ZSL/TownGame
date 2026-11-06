using Godot;
using System;
using System.Linq;
using System.Text.Json.Nodes;

public partial class Main
{
    void AddMapPinTools(Container toolbar,TownMap map)
    {
        var place=new CheckButton{Name="TownMapAddPin",Text="放置图钉",TooltipText="开启后点击地图上的位置，再填写名称和备注。"};
        TownTheme.AccentButton(place,"地图",6);
        toolbar.AddChild(place);
        string selectedId="",movingId="";
        NativeGame.MapPin? selected=null;
        var card=MarketCard(_managementBody!,color:"eee7f0");
        var panel=card.GetParent<PanelContainer>();
        panel.Name="TownMapPinContext";
        panel.Hide();
        var title=MarketLabel(card,"",18,TownTheme.Accent("家庭"),wrap:true);
        var note=MarketLabel(card,"",color:TownTheme.Muted,wrap:true);
        var actions=new HFlowContainer();card.AddChild(actions);
        MarketButton(actions,"定位到场景",()=>LocateMapPin(selectedId),expand:false,accent:"地图");
        var edit=MarketButton(actions,"编辑图钉",()=>{if(selected is NativeGame.MapPin pin)OpenMapPinEditor(pin.Cell,pin);},expand:false,accent:"档案");
        edit.Name="TownMapPinEdit";
        var move=MarketButton(actions,"移到新位置",()=>{
            if(selected is not NativeGame.MapPin pin)return;
            movingId=pin.Id;
            place.SetPressedNoSignal(true);
            RefreshPlacement(true);
            Reveal(map);
        },expand:false,accent:"地图");
        move.Name="TownMapPinMove";
        var clear=MarketButton(actions,"清除选中",ClearPinSelection,expand:false,accent:"地图");
        clear.Name="TownMapClearPin";
        place.Toggled+=RefreshPlacement;
        map.ToolModeChanged+=()=>{
            if(map.ToolMode==TownMap.SurveyMode.Navigate)return;
            ClearPinSelection();
            if(!place.ButtonPressed)return;
            place.SetPressedNoSignal(false);
            RefreshPlacement(false);
        };
        map.PinRequested+=point=>{
            string moving=movingId;
            place.SetPressedNoSignal(false);
            RefreshPlacement(false);
            int x=Mathf.FloorToInt(point.X/WorldData.GridSize),y=Mathf.FloorToInt(point.Y/WorldData.GridSize);
            if(moving.Length==0)OpenMapPinEditor(new(x,y));
            else Act("map_pin",new JsonObject{{"operation","move"},{"id",moving},{"x",x},{"y",y}});
        };
        map.PinSelected+=id=>SelectPin(id,false);
        map.ResidentsSelected+=residents=>{if(residents.Length>0)ClearPinSelection();};
        _refreshManagementPage+=RefreshSelectedPin;
        AddMapPinList(id=>SelectPin(id,true));
        if(_managementEntryTarget.StartsWith("pin:",StringComparison.Ordinal))SelectPin(_managementEntryTarget[4..],true);

        void RefreshPlacement(bool active)
        {
            if(!active)movingId="";
            if(active)map.SetSurveyMode(TownMap.SurveyMode.Navigate);
            map.PinPlacementEnabled=active;
            map.PinMoveEnabled=movingId.Length>0;
            place.Text=movingId.Length>0?"取消移动":active?"取消放置":"放置图钉";
            place.TooltipText=movingId.Length>0?"点击地图选择图钉的新位置。再次按此按钮可取消。":"开启后点击地图上的位置，再填写名称和备注。";
            map.RefreshPointer();
        }

        void SelectPin(string id,bool jump)
        {
            map.ClearResidentSelection();
            selectedId=id;
            RefreshSelectedPin();
            if(selected is not NativeGame.MapPin pin)return;
            if(jump)map.FocusMapPin(pin.Cell);
            Reveal(jump?map:panel);
        }

        void ClearPinSelection()
        {
            selectedId="";
            selected=null;
            map.HighlightPin("");
            panel.Hide();
            if(movingId.Length==0)return;
            place.SetPressedNoSignal(false);
            RefreshPlacement(false);
        }

        void RefreshSelectedPin()
        {
            if(selectedId.Length==0)return;
            var pin=Game.MapPins().FirstOrDefault(entry=>entry.Id==selectedId);
            if(pin.Id==null){ClearPinSelection();return;}
            if(selected==pin)return;
            selected=pin;
            title.Text=$"图钉 · {pin.Name} · ({pin.Cell.X}, {pin.Cell.Y})";
            note.Text=string.Join(" · ",new[]{pin.Note,pin.InJournal?"已加入今日待办":""}.Where(value=>value.Length>0));
            note.Visible=note.Text.Length>0;
            map.HighlightPin(pin.Id);
            panel.Show();
        }

        void Reveal(Control control)=>Callable.From(()=>{
            if(GodotObject.IsInstanceValid(control)&&control.IsVisibleInTree())_managementScroll?.EnsureControlVisible(control);
        }).CallDeferred();
    }

    void AddMapPinList(Action<string> select)
    {
        var card=MarketCard(_managementBody!,color:"e8eef0");
        var toggle=MarketButton(card,"查看已保存的图钉",()=>{},accent:"地图");
        toggle.Name="TownMapPinListToggle";
        toggle.ToggleMode=true;
        var list=new VBoxContainer{Name="TownMapPinList",Visible=false};
        card.AddChild(list);
        var query=new LineEdit{Name="TownMapPinSearch",PlaceholderText="按图钉名称或备注查找",ClearButtonEnabled=true};
        list.AddChild(query);
        var results=new VBoxContainer{Name="TownMapPinResults"};list.AddChild(results);
        NativeGame.MapPin[] current=Array.Empty<NativeGame.MapPin>();
        toggle.Toggled+=shown=>{list.Visible=shown;if(shown)Refresh(true);};
        query.TextChanged+=_=>Refresh(true);
        _refreshManagementPage+=()=>Refresh(false);
        Refresh(false);

        void Refresh(bool force)
        {
            var pins=Game.MapPins().OrderBy(pin=>pin.Name,StringComparer.CurrentCultureIgnoreCase).ThenBy(pin=>pin.Cell.X).ThenBy(pin=>pin.Cell.Y).ToArray();
            toggle.Text=$"已保存的图钉 · {pins.Length}";
            if(!list.Visible||(!force&&current.SequenceEqual(pins)))return;
            current=pins;
            foreach(var node in results.GetChildren()){results.RemoveChild(node);node.QueueFree();}
            if(pins.Length==0){MarketLabel(results,"开启「放置图钉」后在地图上选择位置。",color:TownTheme.Muted,wrap:true);return;}
            string term=query.Text.Trim();
            var matches=pins.Where(pin=>pin.Name.Contains(term,StringComparison.CurrentCultureIgnoreCase)||pin.Note.Contains(term,StringComparison.CurrentCultureIgnoreCase)).ToArray();
            if(matches.Length==0)MarketLabel(results,"没有找到符合条件的图钉。",color:TownTheme.Muted);
            foreach(var pin in matches){
                var row=new HFlowContainer();
                results.AddChild(row);
                var marker=MarketButton(row,$"{pin.Name} · ({pin.Cell.X}, {pin.Cell.Y})",()=>select(pin.Id),expand:false,accent:"地图");
                marker.Name="SelectMapPin"+pin.Id;
                marker.TooltipText=pin.Note.Length>0?pin.Note:"在地图上查看图钉";
                MarketButton(row,"定位场景",()=>LocateMapPin(pin.Id),expand:false,accent:"地图");
            }
        }
    }

    void OpenMapPinEditor(NativeGame.Cell cell,NativeGame.MapPin? existing=null)
    {
        if(_management==null)return;
        var dialog=new ConfirmationDialog{Name="TownMapPinEditor",Title=existing==null?"放置小镇图钉":"小镇图钉",
            Transient=true,Exclusive=true,OkButtonText="保存",CancelButtonText="取消",Theme=_management.Theme};
        _management.AddChild(dialog);
        var form=new VBoxContainer{CustomMinimumSize=new(330,0)};
        form.AddThemeConstantOverride("separation",8);
        dialog.AddChild(form);
        MarketLabel(form,$"位置 ({cell.X}, {cell.Y})",color:TownTheme.Muted);
        var name=new LineEdit{Name="TownMapPinName",Text=existing?.Name??"",PlaceholderText="例如，下次扩建牧场",MaxLength=NativeGame.MapPinNameLimit};
        form.AddChild(name);
        var note=new LineEdit{Name="TownMapPinNote",Text=existing?.Note??"",PlaceholderText="备注，可留空",MaxLength=NativeGame.MapPinNoteLimit};
        form.AddChild(note);
        var journal=new CheckButton{Text="放入今日待办",ButtonPressed=existing?.InJournal??true};
        form.AddChild(journal);
        if(existing is NativeGame.MapPin pin){
            var actions=new HFlowContainer();form.AddChild(actions);
            MarketButton(actions,"定位到场景",()=>{Close();LocateMapPin(pin.Id);},expand:false,accent:"地图");
            MarketButton(actions,"删除图钉",()=>{
                Close();
                ConfirmManagementAction("删除图钉",$"删除「{pin.Name}」？","删除",()=>Act("map_pin",new JsonObject{{"operation","delete"},{"id",pin.Id}}));
            },expand:false,accent:"事件");
        }
        var save=dialog.GetOkButton();
        save.Disabled=name.Text.Trim().Length==0;
        name.TextChanged+=value=>save.Disabled=value.Trim().Length==0;
        dialog.Canceled+=Close;
        dialog.Confirmed+=()=>{
            if(name.Text.Trim().Length==0){Close();return;}
            var args=new JsonObject{{"operation",existing==null?"add":"edit"},{"id",existing?.Id??""},
                {"x",cell.X},{"y",cell.Y},{"name",name.Text},{"note",note.Text},{"journal",journal.ButtonPressed}};
            Close();
            Act("map_pin",args);
        };
        dialog.PopupCentered(new(405,225));
        name.GrabFocus();

        void Close(){dialog.Hide();dialog.QueueFree();}
    }

    void LocateMapPin(string id)
    {
        var pin=Game.MapPins().FirstOrDefault(entry=>entry.Id==id);
        if(pin.Id==null)return;
        var destination=WorldData.Cell(new(pin.Cell.X,pin.Cell.Y));
        RememberCameraSite(destination);
        StopPetFollow();
        StopResidentFollow();
        EndPlacement();
        _portraitAnchor=null;
        _center=destination;
        _distance=58;
        _selection=pin.Note.Length==0?$"图钉 · {pin.Name}":$"图钉 · {pin.Name}\n{pin.Note}";
        _management?.Hide();
    }

    void MapPinJournalControls()
    {
        var pins=Game.MapPins().Where(pin=>pin.InJournal).ToArray();
        if(pins.Length==0)return;
        var card=MarketCard(_managementBody!,color:"e8eef0");
        MarketLabel(card,"我标记的地点",17,TownTheme.Leaf);
        foreach(var pin in pins){
            string caption=pin.Note.Length==0?pin.Name:$"{pin.Name} · {pin.Note}";
            var button=MarketButton(card,$"⌖ {caption}",()=>LocateMapPin(pin.Id),accent:"地图");
            button.Name="LocateJournalPin"+pin.Id;
            button.ClipText=true;
            button.TooltipText=caption;
        }
    }
}
