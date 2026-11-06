using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

public partial class Main
{
    readonly HashSet<string> _residentFavorites=new(StringComparer.Ordinal);
    bool _residentFavoritesLoaded;

    static string ResidentFavoritesPath(int slot)=>$"user://town-resident-favorites-{slot}.cfg";

    void LoadResidentFavorites()
    {
        if(_residentFavoritesLoaded)return;
        _residentFavoritesLoaded=true;
        if(!UseTownPreferences()||ExplicitSavePath!=null)return;
        using var config=new ConfigFile();
        if(config.Load(ResidentFavoritesPath(_saveSlot))==Error.Ok)
            _residentFavorites.UnionWith(ReadResidentFavorites(config));
    }

    string[] ReadResidentFavorites(ConfigFile config)=>config.GetValue("residents","favorites",Array.Empty<string>())
        .AsStringArray().Where(id=>DirectoryPerson(id)!=null).Distinct(StringComparer.Ordinal).ToArray();

    void ToggleResidentFavorite(string id)
    {
        if(DirectoryPerson(id)==null)return;
        if(!_residentFavorites.Remove(id))_residentFavorites.Add(id);
        if(!UseTownPreferences()||ExplicitSavePath!=null)return;
        using var config=new ConfigFile();
        config.SetValue("residents","favorites",_residentFavorites.OrderBy(value=>value,StringComparer.Ordinal).ToArray());
        if(config.Save(ResidentFavoritesPath(_saveSlot))!=Error.Ok)
            SetManagementFeedback("村民收藏设置暂时无法写入，仍可在本次游玩使用。",true);
    }
}
