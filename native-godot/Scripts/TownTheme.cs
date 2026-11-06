using Godot;
using System;

// Shared colours and controls for the game HUD and every management page.
public static class TownTheme
{
    public static bool HighContrast {get;set;}
    public static Color Ink=>new(HighContrast?"14251d":"233c32");
    public static Color Muted=>new(HighContrast?"304338":"4d6356");
    public static Color Leaf=>new(HighContrast?"234635":"38674e");
    static readonly string[] PetColors={"f6e8dc","f1e5eb","e8eef5","e8f1df","f7edce","eee7f5"};

    public static string AnimalColor(int index)=>PetColors[Math.Abs(index)%PetColors.Length];
    public static Color Accent(string tab)=>tab switch{
        "宠物"=>new Color("a85f68"),"家庭"=>new Color("936a8e"),
        "外观"=>new Color("786d9e"),"照护"=>new Color("4c8981"),
        "档案"=>new Color("6e788f"),"商店"=>new Color("a7773f"),
        "订单"=>new Color("637fa0"),"事件"=>new Color("ad6e62"),
        "财务"=>new Color("54866c"),"日志"=>new Color("8b7482"),
        "建造"=>new Color("3f7a78"),"地图"=>new Color("5a83a2"),
        "自治"=>new Color("7e8b51"),"环境"=>new Color("4d8b69"),
        "存档"=>new Color("927554"),_=>Leaf
    };
    public static void AccentButton(BaseButton button,string tab,int padding=10)
    {
        button.SetMeta("town_accent",tab);
        button.SetMeta("town_accent_padding",padding);
        string normal=Accent(tab).Lerp(new Color("fffaf0"),.83f).ToHtml();
        button.AddThemeStyleboxOverride("normal",Box(normal,9,padding));
        button.AddThemeStyleboxOverride("hover",Box(new Color(normal).Darkened(.06f).ToHtml(),9,padding));
        button.AddThemeStyleboxOverride("pressed",Box(Accent(tab).Darkened(HighContrast?.56f:.26f).ToHtml(),9,padding));
        button.AddThemeStyleboxOverride("hover_pressed",Box(Accent(tab).Darkened(HighContrast?.48f:.19f).ToHtml(),9,padding));
        button.AddThemeStyleboxOverride("disabled",Box("eeeadd",9,padding));
        button.AddThemeColorOverride("font_color",Ink);
        button.AddThemeColorOverride("font_hover_color",Ink);
        button.AddThemeColorOverride("font_focus_color",Ink);
        button.AddThemeColorOverride("font_pressed_color",Colors.White);
        button.AddThemeColorOverride("font_hover_pressed_color",Colors.White);
        button.AddThemeColorOverride("font_disabled_color",Muted);
        var focus=Box("fffaf0",9,0);
        focus.DrawCenter=false;
        focus.BorderColor=HighContrast?Ink:Accent(tab).Darkened(.18f);
        focus.SetBorderWidthAll(HighContrast?3:2);
        button.AddThemeStyleboxOverride("focus",focus);
    }

    public static StyleBoxFlat Box(string color,int radius=12,int margin=10)
    {
        return new StyleBoxFlat{
            BgColor=new Color(color),CornerRadiusTopLeft=radius,CornerRadiusTopRight=radius,
            CornerRadiusBottomLeft=radius,CornerRadiusBottomRight=radius,
            ContentMarginLeft=margin,ContentMarginRight=margin,
            ContentMarginTop=margin,ContentMarginBottom=margin
        };
    }

    public static Theme Create()
    {
        var font=new SystemFont{
            FontNames=new[]{"Microsoft YaHei","Noto Sans CJK SC"},
            Antialiasing=TextServer.FontAntialiasing.Gray,
            Hinting=TextServer.Hinting.Normal
        };
        var theme=new Theme{DefaultFont=font,DefaultFontSize=16};
        theme.SetColor("font_color","Label",Ink);
        theme.SetColor("font_color","Button",Ink);
        theme.SetColor("font_hover_color","Button",Ink);
        theme.SetColor("font_pressed_color","Button",new Color("fff9e9"));
        theme.SetColor("font_disabled_color","Button",Muted);
        theme.SetColor("font_color","OptionButton",Ink);
        theme.SetColor("font_hover_color","OptionButton",Ink);
        theme.SetColor("font_pressed_color","OptionButton",Ink);
        theme.SetColor("font_color","LineEdit",Ink);
        theme.SetColor("font_placeholder_color","LineEdit",Muted);
        theme.SetColor("font_color","SpinBox",Ink);
        theme.SetColor("font_color","CheckButton",Ink);
        theme.SetColor("font_color","PopupMenu",Ink);
        theme.SetColor("font_hover_color","PopupMenu",Ink);
        theme.SetColor("font_disabled_color","PopupMenu",Muted);
        theme.SetStylebox("panel","PopupMenu",Box("faf5e8",9,6));
        theme.SetStylebox("hover","PopupMenu",Box("e2ebdc",6,4));
        theme.SetStylebox("panel","AcceptDialog",Box("faf5e8",10,14));
        theme.SetStylebox("scroll","VScrollBar",Box("ede9dd",5,4));
        theme.SetStylebox("grabber","VScrollBar",Box("a3b1a2",5,4));
        theme.SetStylebox("grabber_highlight","VScrollBar",Box("879d8a",5,4));
        theme.SetStylebox("grabber_pressed","VScrollBar",Box("6e8d74",5,4));
        foreach(string kind in new[]{"Button","OptionButton"}){
            theme.SetStylebox("normal",kind,Box("e6edda"));
            theme.SetStylebox("hover",kind,Box("d9e7cc"));
            theme.SetStylebox("pressed",kind,Box("44775b"));
            theme.SetStylebox("hover_pressed",kind,Box("538768"));
            theme.SetStylebox("disabled",kind,Box("eeeadd"));
        }
        foreach(string kind in new[]{"LineEdit","SpinBox"}){
            theme.SetStylebox("normal",kind,Box("fffdf4"));
            theme.SetStylebox("read_only",kind,Box("eeeadd"));
            theme.SetStylebox("focus",kind,Box("d9e7cc"));
        }
        return theme;
    }
}
