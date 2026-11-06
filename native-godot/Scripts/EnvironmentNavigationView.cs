using Godot;
using System.Collections.Generic;
using System.Threading.Tasks;

public partial class Main
{
    readonly List<(string Name,Control Target)> _environmentSections=new();
    MenuButton? _environmentSectionMenu;

    void AddEnvironmentSectionMenu(Container header)
    {
        var menu=new MenuButton{Name="EnvironmentSectionMenu",Text="设置目录",Flat=false,Visible=false,
            FocusMode=Control.FocusModeEnum.All,SizeFlagsVertical=Control.SizeFlags.ShrinkCenter,
            TooltipText="直接跳到画质、键盘、镜头、声音、农场或时间计划"};
        TownTheme.AccentButton(menu,"环境",5);
        header.AddChild(menu);
        _environmentSectionMenu=menu;
        menu.AboutToPopup+=()=>{
            var popup=menu.GetPopup();
            popup.Clear();
            for(int i=0;i<_environmentSections.Count;i++)popup.AddItem(_environmentSections[i].Name,i);
        };
        menu.GetPopup().IdPressed+=id=>{
            if(id<0||id>=_environmentSections.Count)return;
            _=RevealManagementSection(_environmentSections[(int)id].Target);
        };
    }

    void RegisterEnvironmentSection(string name,Control section)=>_environmentSections.Add((name,section));

    async Task RevealManagementSection(Control target)
    {
        await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
        if(!GodotObject.IsInstanceValid(target)||!target.IsVisibleInTree()||_management?.Visible!=true)return;
        _managementScroll!.ScrollVertical=Mathf.RoundToInt(target.GlobalPosition.Y-_managementBody!.GlobalPosition.Y);
    }
}
