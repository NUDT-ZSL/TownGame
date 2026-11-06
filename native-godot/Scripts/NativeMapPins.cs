using System;
using System.Linq;
using System.Text.Json.Nodes;

public sealed partial class NativeGame
{
    const string MapPinsKey="native_map_pins";
    internal const int MapPinLimit=24,MapPinNameLimit=24,MapPinNoteLimit=120;
    internal readonly record struct MapPin(string Id,Cell Cell,string Name,string Note,bool InJournal);

    internal MapPin[] MapPins()=>Meta[MapPinsKey] is JsonArray pins?pins.OfType<JsonObject>()
        .Select(pin=>new MapPin(S(pin,"id"),new(I(pin,"x"),I(pin,"y")),S(pin,"name"),S(pin,"note"),B(pin,"journal")))
        .Where(pin=>pin.Id.Length>0&&pin.Name.Length>0&&ValidPinCell(pin.Cell)).Take(MapPinLimit).ToArray():Array.Empty<MapPin>();

    static bool ValidPinCell(Cell cell)=>cell.X is >=0 and <125&&cell.Y is >=0 and <80;

    string MapPinCommand(JsonObject args)
    {
        string operation=S(args,"operation");
        if(operation is not ("add" or "edit" or "delete" or "move"))throw new InvalidOperationException("未知图钉操作");
        var pins=(Meta[MapPinsKey]??=new JsonArray()).AsArray();
        string id=S(args,"id");
        var existing=pins.OfType<JsonObject>().FirstOrDefault(pin=>S(pin,"id")==id);
        if(operation=="delete"){
            if(existing==null)throw new InvalidOperationException("这枚图钉已移除");
            pins.Remove(existing);
            return "图钉已移除";
        }
        if(operation=="move"){
            if(existing==null)throw new InvalidOperationException("这枚图钉已移除");
            var cell=new Cell(I(args,"x",-1),I(args,"y",-1));
            if(!ValidPinCell(cell))throw new InvalidOperationException("图钉位置超出小镇地图");
            existing["x"]=cell.X;
            existing["y"]=cell.Y;
            return "图钉已移动";
        }
        string name=S(args,"name").Trim(),note=S(args,"note").Trim();
        if(name.Length is 0 or >MapPinNameLimit||name.Any(char.IsControl))throw new InvalidOperationException($"请输入 1–{MapPinNameLimit} 字的图钉名称");
        if(note.Length>MapPinNoteLimit)throw new InvalidOperationException($"备注最多 {MapPinNoteLimit} 字");
        if(operation=="add"){
            var cell=new Cell(I(args,"x",-1),I(args,"y",-1));
            if(!ValidPinCell(cell))throw new InvalidOperationException("图钉位置超出小镇地图");
            if(pins.Count>=MapPinLimit)throw new InvalidOperationException($"最多保存 {MapPinLimit} 枚图钉");
            existing=new JsonObject{{"id",NextId("map-pin")},{"x",cell.X},{"y",cell.Y}};
            pins.Add(existing);
        }
        if(existing==null)throw new InvalidOperationException("这枚图钉已移除");
        existing["name"]=name;
        existing["note"]=note;
        existing["journal"]=B(args,"journal");
        return operation=="add"?"已放置图钉":"图钉已更新";
    }
}
