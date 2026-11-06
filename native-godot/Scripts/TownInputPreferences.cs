using Godot;
using System.Collections.Generic;
using System.Linq;

public partial class Main
{
    readonly record struct TownKey(Key Key,bool Ctrl=false)
    {
        public string Caption=>(Ctrl?"Ctrl+":"")+OS.GetKeycodeString(Key);
    }
    const string TownInputPath="user://town-input.cfg";
    static readonly (string Id,string Label,TownKey Default,bool Movement)[] TownActions={
        ("up","镜头向前",new(Key.W),true),("down","镜头向后",new(Key.S),true),
        ("left","镜头向左",new(Key.A),true),("right","镜头向右",new(Key.D),true),
        ("save","保存进度",new(Key.F5),false),("photo","拍照模式",new(Key.F10),false),
        ("capture","拍摄照片",new(Key.F12),false),("map","打开地图",new(Key.M),false),
        ("search","搜索小镇",new(Key.F,true),false),("undo","撤销最近道路操作",new(Key.Z,true),false),
        ("rotate","旋转施工预览",new(Key.R),false)
    };
    Dictionary<string,TownKey> _townBindings=TownActions.ToDictionary(action=>action.Id,action=>action.Default);

    void InitializeTownBindings()
    {
        if(!UseTownPreferences())return;
        using var config=new ConfigFile();
        if(config.Load(TownInputPath)!=Error.Ok)return;
        _townCameraInput=ReadTownCameraInput(config);
        var saved=TownActions.ToDictionary(action=>action.Id,action=>new TownKey(
            (Key)config.GetValue(action.Id,"key",(long)action.Default.Key).AsInt64(),
            config.GetValue(action.Id,"ctrl",action.Default.Ctrl).AsBool()));
        if(TownActions.All(action=>ValidTownKey(saved[action.Id],action.Movement))&&saved.Values.Distinct().Count()==saved.Count)
            _townBindings=saved;
    }

    static Key TownPhysicalKey(InputEventKey key)=>key.PhysicalKeycode!=Key.None?key.PhysicalKeycode:key.Keycode;
    static bool ValidTownKey(TownKey key,bool movement)=>System.Enum.IsDefined(key.Key)&&
        key.Key is not (Key.None or Key.Escape or Key.Shift or Key.Ctrl or Key.Meta or Key.Alt or Key.Tab)&&
        (!movement||!key.Ctrl);
    bool MatchTownInput(InputEventKey key,string action)=>!key.AltPressed&&!key.MetaPressed&&!key.ShiftPressed&&
        _townBindings[action]==new TownKey(TownPhysicalKey(key),key.CtrlPressed);
    bool TownMovementPressed(string action)=>!Input.IsPhysicalKeyPressed(Key.Ctrl)&&!Input.IsPhysicalKeyPressed(Key.Alt)&&
        !Input.IsPhysicalKeyPressed(Key.Meta)&&Input.IsPhysicalKeyPressed(_townBindings[action].Key);
    string TownBindingText(string action)=>_townBindings[action].Caption;
    string TownMovementText=>string.Join(" / ",new[]{"up","left","down","right"}.Select(TownBindingText));

    void SaveTownBindings()
    {
        if(!UseTownPreferences())return;
        using var config=new ConfigFile();
        foreach(var binding in _townBindings){
            config.SetValue(binding.Key,"key",(long)binding.Value.Key);
            config.SetValue(binding.Key,"ctrl",binding.Value.Ctrl);
        }
        WriteTownCameraInput(config);
        if(config.Save(TownInputPath)!=Error.Ok)SetManagementFeedback("快捷键设置未能保存。",true);
    }

    Control TownInputControls()
    {
        var card=MarketCard(_managementBody!,color:"eee9f3");
        MarketLabel(card,"键盘快捷键",19,TownTheme.Leaf);
        MarketLabel(card,"选择一项后可按下新键或恢复默认键。已有按键重合时，可确认交换。",color:TownTheme.Muted,wrap:true);
        var buttons=new Dictionary<string,Button>();
        AddGroup("镜头移动",true);
        AddGroup("小镇操作",false);
        var reset=MarketButton(card,"恢复全部默认快捷键",()=>{
            _townBindings=TownActions.ToDictionary(action=>action.Id,action=>action.Default);
            Refresh();
        },expand:false,accent:"环境");
        reset.SizeFlagsHorizontal=Control.SizeFlags.ShrinkBegin;
        if(_managementEntryTarget=="keybindings")_managementFocusTarget=card;
        return card.GetParent<Control>();

        void AddGroup(string caption,bool movement)
        {
            MarketLabel(card,caption,15,TownTheme.Muted);
            var grid=new HFlowContainer();
            grid.AddThemeConstantOverride("h_separation",8);
            grid.AddThemeConstantOverride("v_separation",6);
            card.AddChild(grid);
            foreach(var action in TownActions.Where(action=>action.Movement==movement)){
                var button=MarketButton(grid,$"{action.Label} · {TownBindingText(action.Id)}",
                    ()=>OpenTownKeyCapture(action.Id,action.Label,action.Movement,Refresh),expand:false,accent:"地图");
                button.Name="TownInput_"+action.Id;
                buttons[action.Id]=button;
            }
        }

        void Refresh()
        {
            foreach(var action in TownActions)buttons[action.Id].Text=$"{action.Label} · {TownBindingText(action.Id)}";
            SaveTownBindings();
        }
    }

    void OpenTownKeyCapture(string id,string label,bool movement,System.Action saved)
    {
        var current=_townBindings[id];
        var defaults=TownActions.First(action=>action.Id==id).Default;
        var dialog=new AcceptDialog{Name="TownKeyCapture",Title=$"设置 · {label}",
            DialogText=$"当前按键 · {current.Caption}。按下新键，或按 Esc 返回。",OkButtonText="取消",
            DialogAutowrap=true,Transient=true,Exclusive=true,Theme=_panel.Theme};
        _management!.AddChild(dialog);
        var restore=dialog.AddButton($"恢复 {defaults.Caption}",false,"restore");
        restore.Name="TownKeyRestore";
        restore.Disabled=current==defaults;
        var swap=dialog.AddButton("交换按键",true,"swap");
        swap.Name="TownKeySwap";
        swap.Hide();
        TownTheme.AccentButton(restore,"环境",5);
        TownTheme.AccentButton(swap,"地图",5);
        TownTheme.AccentButton(dialog.GetOkButton(),"存档",5);
        (string Id,TownKey Key)? pending=null;
        dialog.Confirmed+=Close;
        dialog.Canceled+=Close;
        dialog.CustomAction+=action=>{
            if(action=="restore")TryChange(defaults);
            else if(action=="swap"&&pending is { } other&&_townBindings[other.Id]==other.Key&&_townBindings[id]==current){
                _townBindings[other.Id]=current;
                Apply(other.Key);
            }
        };
        dialog.WindowInput+=input=>{
            if(input is not InputEventKey {Pressed:true,Echo:false} key)return;
            var pressed=TownPhysicalKey(key);
            if(pressed==Key.Tab||(pressed is Key.Enter or Key.KpEnter or Key.Space)&&dialog.GuiGetFocusOwner() is Button)return;
            dialog.SetInputAsHandled();
            if(pressed==Key.Escape){Close();return;}
            var next=new TownKey(pressed,key.CtrlPressed);
            if(key.AltPressed||key.MetaPressed||key.ShiftPressed||!ValidTownKey(next,movement)){
                pending=null;swap.Hide();
                dialog.DialogText=movement?"请选择一个单键。":"请选择单键或 Ctrl 加单键。";
                return;
            }
            TryChange(next);
        };
        dialog.PopupCenteredClamped(new(464,174),.94f);
        var capture=dialog.GetLabel();
        capture.FocusMode=Control.FocusModeEnum.All;
        capture.GrabFocus();

        void TryChange(TownKey next)
        {
            var conflict=TownActions.FirstOrDefault(action=>action.Id!=id&&_townBindings[action.Id]==next);
            if(conflict.Id==null){Apply(next);return;}
            bool canSwap=ValidTownKey(current,conflict.Movement);
            pending=canSwap?(conflict.Id,next):null;
            swap.Visible=canSwap;
            dialog.DialogText=canSwap
                ?$"{next.Caption} 已用于「{conflict.Label}」。交换后该操作改用 {current.Caption}。"
                :$"{next.Caption} 已用于「{conflict.Label}」，该操作不能用 {current.Caption}。请选择其他键。";
        }

        void Apply(TownKey next)
        {
            _townBindings[id]=next;
            saved();
            Close();
        }

        void Close(){dialog.Hide();dialog.QueueFree();}
    }
}
