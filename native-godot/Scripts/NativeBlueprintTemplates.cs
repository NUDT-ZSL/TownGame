using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;

public sealed partial class NativeGame
{
    const string BlueprintTemplatesKey="native_blueprint_templates";
    const int BlueprintTemplateLimit=8;
    internal sealed record BlueprintTemplate(string Id,string Name,BlueprintStep[] Steps);

    internal BlueprintTemplate[] SavedBlueprintTemplates()=>Meta[BlueprintTemplatesKey] is JsonArray templates
        ?templates.OfType<JsonObject>().Select(template=>new BlueprintTemplate(S(template,"id"),S(template,"name"),ReadBlueprintTemplateSteps(template))).ToArray()
        :Array.Empty<BlueprintTemplate>();

    static BlueprintStep[] ReadBlueprintTemplateSteps(JsonNode template)
    {
        if(template["steps"] is not JsonArray steps||steps.Count==0||steps.Count>BlueprintLimit)
            throw new InvalidOperationException("模板施工项目数量无效");
        return steps.Select(step=>step is JsonObject row&&row["args"] is JsonObject data
            ?new BlueprintStep(S(row,"action"),data):throw new InvalidOperationException("模板施工项目无效")).ToArray();
    }

    string BlueprintTemplateCommand(JsonObject args)
    {
        var templates=(Meta[BlueprintTemplatesKey]??=new JsonArray()).AsArray();
        string operation=S(args,"operation");
        if(operation=="delete"){
            var target=templates.OfType<JsonObject>().FirstOrDefault(template=>S(template,"id")==S(args,"id"));
            if(target==null)throw new InvalidOperationException("模板不存在");
            templates.Remove(target);
            return "已删除施工模板";
        }
        if(operation!="save")throw new InvalidOperationException("模板操作无效");
        string name=S(args,"name").Trim();
        if(name.Length==0||name.Length>24)throw new InvalidOperationException("请输入 1–24 字的模板名称");
        if(templates.Count>=BlueprintTemplateLimit)throw new InvalidOperationException($"最多保存 {BlueprintTemplateLimit} 个模板");
        if(templates.OfType<JsonObject>().Any(template=>string.Equals(S(template,"name"),name,StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("已有同名模板");
        var steps=ReadBlueprintTemplateSteps(args);
        foreach(var step in steps)ValidateTemplateStep(step);
        templates.Add(new JsonObject{{"id",NextId("blueprint-template")},{"name",name},{"steps",args["steps"]!.DeepClone()}});
        return $"已保存施工模板 · {name}";
    }

    void ValidateTemplateStep(BlueprintStep step)
    {
        var data=step.Args;
        bool allowed=step.Action switch{
            "road_stroke"=>!B(data,"remove")&&Rules["ROAD_STYLES"]?[S(data,"style")]!=null&&data["cells"] is JsonArray {Count:>0} cells
                &&cells.Count<=125*80&&cells.All(cell=>cell is JsonArray {Count:2}),
            "build_town"=>Rules["CATALOG"]?[S(data,"catalog_key")]!=null,
            "build_manor"=>Rules["BUILDINGS"]?[S(data,"kind")]!=null&&Rows("buildings").OfType<JsonNode>().Any(plot=>I(plot,"building_id")==I(data,"building_id",-1)),
            "decoration"=>S(data,"operation")=="place"&&Rules["DECORATIONS"]?[S(data,"kind")]!=null,
            _=>false
        };
        if(!allowed)throw new InvalidOperationException("模板包含无效的施工项目");
    }

    internal BlueprintStep[] MoveBlueprintTemplate(BlueprintTemplate template,Cell target,int manorPlot)
    {
        foreach(var step in template.Steps)ValidateTemplateStep(step);
        var coordinates=template.Steps.SelectMany(step=>step.Action switch{
            "road_stroke"=>step.Args["cells"]!.AsArray().Select(cell=>new Cell(cell![0]!.GetValue<int>(),cell[1]!.GetValue<int>())),
            "build_town" or "decoration"=>new[]{new Cell(I(step.Args,"cx"),I(step.Args,"cy"))},
            _=>Array.Empty<Cell>()
        }).ToArray();
        int dx=coordinates.Length>0?target.X-coordinates.Min(cell=>cell.X):0;
        int dy=coordinates.Length>0?target.Y-coordinates.Min(cell=>cell.Y):0;
        var plots=template.Steps.Where(step=>step.Action=="build_manor").Select(step=>I(step.Args,"building_id")).ToArray();
        int plotX=plots.Length>0?manorPlot%5-plots.Min(plot=>plot%5):0;
        int plotY=plots.Length>0?manorPlot/5-plots.Min(plot=>plot/5):0;
        if(plots.Length>0&&manorPlot<0)throw new InvalidOperationException("请为庄园建筑选择起始地块");
        return template.Steps.Select(step=>{
            var data=step.Args.DeepClone().AsObject();
            if(step.Action=="road_stroke")
                foreach(var cell in data["cells"]!.AsArray()){
                    var moved=CheckCell(cell![0]!.GetValue<int>()+dx,cell[1]!.GetValue<int>()+dy);
                    cell[0]=moved.X;
                    cell[1]=moved.Y;
                }
            else if(step.Action=="build_manor"){
                int id=I(data,"building_id"),x=id%5+plotX,y=id/5+plotY;
                if(x<0||x>=5||y<0||!Rows("buildings").OfType<JsonNode>().Any(plot=>I(plot,"building_id")==y*5+x))
                    throw new InvalidOperationException("模板超出庄园地块范围");
                data["building_id"]=y*5+x;
            }else{
                var moved=CheckCell(I(data,"cx")+dx,I(data,"cy")+dy);
                data["cx"]=moved.X;
                data["cy"]=moved.Y;
            }
            return new BlueprintStep(step.Action,data);
        }).ToArray();

        static Cell CheckCell(int x,int y)=>x is >=0 and <125&&y is >=0 and <80
            ?new(x,y):throw new InvalidOperationException("模板超出小镇地图范围");
    }
}
