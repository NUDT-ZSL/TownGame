using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

public partial class Main
{
    Node3D? _neighborResultScene;
    string _neighborResultKey="";
    readonly Dictionary<string,Vector3> _neighborResultSites=new();

    void RefreshNeighborhoodResults()
    {
        var results=Game.NeighborhoodResults();
        string signature=_layoutSignature+"|"+string.Join("|",results.Select(row=>row.Publisher+row.Zone+row.Kind));
        if(signature==_neighborResultKey)return;
        _neighborResultKey=signature;
        _neighborResultScene?.QueueFree();
        _neighborResultScene=null;
        _neighborResultSites.Clear();
        if(results.Length==0)return;
        var scene=new Node3D{Name="NeighborhoodResults"};
        AddChild(scene);
        _neighborResultScene=scene;
        var occupied=new HashSet<Vector2I>();
        foreach(var result in results){
            string zone=Data.Portals.ContainsKey(result.Zone)?result.Zone:"estate";
            var available=OpenDistrictCells(zone).Skip(2).Where(cell=>!occupied.Contains(cell)).ToArray();
            if(available.Length==0)available=OpenDistrictCells("estate").Skip(2).Where(cell=>!occupied.Contains(cell)).ToArray();
            if(available.Length==0)continue;
            var cell=available[0];
            occupied.Add(cell);
            Vector3 site=WorldData.Cell(cell);
            _neighborResultSites[result.Publisher]=site;
            var model=new DistrictAccentMesh();
            string title=NativeGame.NeighborhoodResultLabel(result.Kind);
            float labelHeight=2;
            switch(result.Kind){
                case "walk":
                    for(int i=-3;i<=3;i++){
                        float angle=i*.33f;
                        model.Round(new(i*.82f,.07f,MathF.Sin(angle)*.75f),new(.78f,.13f,.86f),i%2==0?"e3d1ac":"cfbb99");
                    }
                    model.Box(new(0,.8f,-.8f),new(.14f,1.55f,.14f),"896647");
                    model.Box(new(0,1.47f,-.8f),new(1.18f,.29f,.14f),"6b9580");
                    break;
                case "garden":
                    model.Box(new(0,.24f,0),new(3.8f,.48f,2.2f),"98634a");
                    model.Box(new(0,.49f,0),new(3.57f,.05f,2.01f),"766752");
                    for(int x=-2;x<=2;x++)for(int z=-1;z<=1;z++){
                        model.Round(new(x*.55f,.64f,z*.52f),new(.31f,.36f,.35f),"7f9a65");
                        model.Round(new(x*.55f,.85f,z*.52f),new(.24f,.14f,.23f),(x+z)%2==0?"f4d38a":"d999aa");
                    }
                    break;
                case "stall":
                    model.Box(new(0,.55f,0),new(2.7f,1.1f,1.2f),"ae7953");
                    foreach(float x in new[]{-1.18f,1.18f})model.Box(new(x,1.5f,-.42f),new(.12f,1.95f,.12f),"8e6248");
                    for(int i=-2;i<=2;i++)model.Box(new(i*.55f,2.38f,0),new(.56f,.12f,1.50f),i%2==0?"ebc286":"86ac98");
                    labelHeight=2.75f;
                    break;
                default:
                    model.Box(new(0,.69f,0),new(2.95f,.20f,.78f),"ad7653");
                    model.Box(new(0,1.05f,-.35f),new(2.95f,.63f,.12f),"bd895e");
                    foreach(float x in new[]{-1.15f,1.15f})model.Box(new(x,.29f,0),new(.19f,.64f,.61f),"777f70");
                    break;
            }
            var marker=new MeshInstance3D{Mesh=model.Finish(),Position=site};
            scene.AddChild(marker);
            marker.AddChild(new Label3D{Position=Vector3.Up*labelHeight,Text=title,Billboard=BaseMaterial3D.BillboardModeEnum.Enabled,
                FontSize=30,PixelSize=.017f,Modulate=new Color("fff2cd")});
        }
    }

    void LocateNeighborhoodResult(string id)
    {
        RefreshNeighborhoodResults();
        if(!_neighborResultSites.TryGetValue(id,out var site)){
            SetManagementFeedback("街区暂无空闲地点显示成果。");
            return;
        }
        StopPetFollow();
        StopResidentFollow();
        _center=site;_distance=30;
        _management?.Hide();
    }
}
