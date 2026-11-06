using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;

public partial class Main
{
    bool CanShowResidenceInterior(JsonNode home)=>Game.IsResidentialHome(home)&&NativeGame.S(home,"status")=="ready";

    void ResidenceInteriorControls(JsonNode home)
    {
        string id=NativeGame.S(home,"building_id");
        if(!CanShowResidenceInterior(home))return;
        var card=MarketCard(_managementBody!,color:"ede6db");
        var bar=new HBoxContainer();
        card.AddChild(bar);
        MarketLabel(bar,"住宅室内",18,TownTheme.Accent("家庭")).SizeFlagsHorizontal=Control.SizeFlags.ExpandFill;
        MarketButton(bar,"返回家庭资料",()=>SelectResidence(id),expand:false,accent:"家庭");
        MarketLabel(card,ResidenceName(home),14,TownTheme.Muted);

        var tools=new HFlowContainer();tools.AddThemeConstantOverride("h_separation",7);card.AddChild(tools);
        var views=MarketOptions(tools,new[]{"全屋","餐桌","床铺"},0);views.Name="ResidenceInteriorPreset";
        views.AccessibilityName="室内镜头";
        var people=MarketOptions(tools,new[]{"选择在家成员"},0);people.Name="ResidenceInteriorMemberFocus";
        people.AccessibilityName="聚焦在家的成员";
        var reset=HousingButton(tools,"回到全屋","ResidenceInteriorReset","地图");

        var screen=new SubViewportContainer{Stretch=true,CustomMinimumSize=new(0,260),MouseFilter=Control.MouseFilterEnum.Ignore};
        card.AddChild(screen);
        var view=new SubViewport{OwnWorld3D=true,RenderTargetUpdateMode=SubViewport.UpdateMode.Once,Size=new(512,260),Msaa3D=SceneAntialiasing};
        screen.AddChild(view);
        screen.Resized+=()=>view.RenderTargetUpdateMode=SubViewport.UpdateMode.Once;
        view.AddChild(new WorldEnvironment{Environment=new Godot.Environment{
            BackgroundMode=Godot.Environment.BGMode.Color,BackgroundColor=new Color("e6ded0"),
            AmbientLightSource=Godot.Environment.AmbientSource.Color,AmbientLightColor=new Color("fff2dd"),AmbientLightEnergy=.72f}});
        view.AddChild(new DirectionalLight3D{RotationDegrees=new(-50,-30,0),LightEnergy=.78f});
        float roomWidth=NativeGame.I(home,"w")>=3?8:6.6f;
        float roomDepth=NativeGame.I(home,"h")>=3?5.8f:4.8f;
        var cutaway=new DistrictAccentMesh();
        string wall=NativeGame.S(home,"catalog_key").Contains("wood",StringComparison.Ordinal)?"ce9c69":"edd7b5";
        cutaway.Box(new(0,0,0),new(roomWidth,.24f,roomDepth),"c8a477");
        cutaway.Box(new(0,1.15f,-roomDepth/2),new(roomWidth,2.3f,.20f),wall);
        cutaway.Box(new(-roomWidth/2,1.15f,0),new(.20f,2.3f,roomDepth),wall);
        cutaway.Box(new(0,1.48f,-roomDepth/2+.12f),new(1.30f,1.05f,.055f),"a8d1d0");
        cutaway.Box(new(0,1.48f,-roomDepth/2+.16f),new(.09f,1.13f,.05f),"f0dabc");
        cutaway.Box(new(0,1.48f,-roomDepth/2+.16f),new(1.38f,.09f,.05f),"f0dabc");
        cutaway.Box(new(-1.46f,.145f,.22f),new(2.7f,.03f,2.4f),"ab806b");
        cutaway.Box(new(-1.48f,.87f,-.35f),new(1.52f,.15f,.94f),"7b5944");
        foreach(float x in new[]{-2.11f,-.85f})
            foreach(float z in new[]{-.65f,-.05f})cutaway.Box(new(x,.47f,z),new(.15f,.71f,.15f),"7b5944");
        var seats=new[]{new Vector3(-2.55f,.38f,-.35f),new Vector3(-.42f,.38f,-.35f),
            new Vector3(-1.48f,.38f,.50f),new Vector3(-1.48f,.38f,-1.20f)};
        foreach(var seat in seats){
            cutaway.Round(seat+Vector3.Down*.14f,new(.50f,.39f,.50f),"a87554");
            var plate=new Vector3(-1.48f,.97f,-.35f)+(seat-new Vector3(-1.48f,seat.Y,-.35f))*.36f;
            cutaway.Round(plate,new(.31f,.06f,.29f),"f3d7a8");
        }
        cutaway.Box(new(1.8f,.28f,-.75f),new(1.70f,.34f,2.2f),"936147");
        cutaway.Box(new(1.8f,.52f,-.75f),new(1.64f,.15f,2.16f),"f4e5cd");
        cutaway.Box(new(1.8f,.61f,-.25f),new(1.64f,.09f,1.10f),"80a58e");
        cutaway.Box(new(1.8f,.65f,-1.44f),new(.79f,.16f,.42f),"f8e6c8");
        view.AddChild(new MeshInstance3D{Mesh=cutaway.Finish()});
        var occupants=new Node3D();
        view.AddChild(occupants);
        var camera=new Camera3D{Name="ResidenceInteriorCamera",Position=new(7,7,9),Projection=Camera3D.ProjectionType.Orthogonal,Size=7.6f};
        view.AddChild(camera);camera.LookAt(new(0,.48f,0));camera.Current=true;
        var ring=new MeshInstance3D{Name="ResidenceInteriorFocusRing",Visible=false,Mesh=new TorusMesh{InnerRadius=.34f,OuterRadius=.47f,Rings=20,RingSegments=10},
            MaterialOverride=new StandardMaterial3D{AlbedoColor=new Color("a5782e"),ShadingMode=BaseMaterial3D.ShadingModeEnum.Unshaded}};
        view.AddChild(ring);
        var status=MarketLabel(card,"",14,TownTheme.Muted,wrap:true);status.Name="ResidenceInteriorPresence";
        var viewStatus=MarketLabel(card,"",14,TownTheme.Ink,wrap:true);viewStatus.Name="ResidenceInteriorViewStatus";
        string visualKey="",selectedPerson="";
        string[] occupantsIds=Array.Empty<string>();
        var poses=new Dictionary<string,(string Name,Vector3 Position)>(StringComparer.Ordinal);
        views.ItemSelected+=_=>{selectedPerson="";people.Select(0);RefreshCamera();};
        people.ItemSelected+=index=>{
            selectedPerson=index>0&&index<=occupantsIds.Length?occupantsIds[(int)index-1]:"";
            if(selectedPerson.Length>0)views.Select(0);
            RefreshCamera();
        };
        reset.Pressed+=()=>{selectedPerson="";views.Select(0);people.Select(0);RefreshCamera();};

        void Refresh()
        {
            var members=ResidenceOccupants(id);
            double hour=(Game.Now%NativeGame.Day)/NativeGame.Hour;
            bool eating=hour>=20&&hour<21.5,rest=hour>=22||hour<6.3;
            string activity=eating?"在家吃饭":rest?"在家休息":"在家活动";
            string key=activity+string.Join("|",members.Select(person=>NativeGame.S(person,"person_id")+NativeGame.S(person,"name")+Game.ResidentLifeStage(person)));
            if(visualKey==key)return;
            visualKey=key;
            foreach(var child in occupants.GetChildren()){occupants.RemoveChild(child);child.QueueFree();}
            poses.Clear();
            occupantsIds=members.Select(person=>NativeGame.S(person,"person_id")).ToArray();
            people.Clear();people.AddItem("选择在家成员");
            people.Disabled=members.Length==0;
            status.Text=members.Length==0?"目前没有成员在家":$"在家 {members.Length} 位 · {activity} · "+string.Join("、",members.Select(person=>NativeGame.S(person,"name")));
            for(int i=0;i<members.Length;i++){
                var person=members[i];
                string personId=NativeGame.S(person,"person_id"),job=NativeGame.S(person,"profession");
                float childScale=Game.Now-NativeGame.D(person,"born_ts",Game.Now)<7*NativeGame.Day?.7f:1;
                bool atTable=eating&&i<seats.Length;
                Vector3 position=new(-2.25f+i%4*1.2f,.14f,1.20f+i/4*.55f);
                if(rest&&i==0)position=new(1.8f,.77f,-.98f);
                if(atTable)position=seats[i];
                string name=NativeGame.S(person,"name");
                people.AddItem(name);
                poses[personId]=(name,position);
                var actor=new MeshInstance3D{Mesh=ResidentOutfitBody(job,ResidentOutfitShade(personId),0),Scale=Vector3.One*childScale*.72f,
                    Position=position};
                if(rest&&i==0)actor.RotationDegrees=new(-90,0,0);
                if(atTable)actor.Rotation=new(-.14f,Mathf.Atan2(-1.48f-position.X,-.35f-position.Z),0);
                occupants.AddChild(actor);
            }
            int focus=Array.IndexOf(occupantsIds,selectedPerson);
            if(focus<0)selectedPerson="";
            people.Select(focus+1);
            RefreshCamera();
        }

        void RefreshCamera()
        {
            var center=new Vector3(0,.48f,0);
            float size=7.6f;
            string mode="全屋";
            if(poses.TryGetValue(selectedPerson,out var pose)){
                center=pose.Position+Vector3.Up*.44f;size=3.7f;mode=pose.Name;
                ring.Position=new(pose.Position.X,.20f,pose.Position.Z);
                ring.Show();
            }else{
                ring.Hide();
                if(views.Selected==1){center=new(-1.48f,.52f,-.35f);size=4.1f;mode="餐桌";}
                if(views.Selected==2){center=new(1.8f,.45f,-.75f);size=4.1f;mode="床铺";}
            }
            camera.Position=center+new Vector3(7,6.52f,9);
            camera.LookAt(center);
            camera.Size=size;
            viewStatus.Text=$"镜头 · {mode}";
            view.RenderTargetUpdateMode=SubViewport.UpdateMode.Once;
        }
        Refresh();
        _refreshManagementPage+=Refresh;
    }

    JsonNode[] ResidenceOccupants(string homeId)=>Data.People.OfType<JsonNode>().Where(person=>{
        if(NativeGame.S(person,"role")!="居民")return false;
        var actor=Agents.FirstOrDefault(agent=>agent.Ident==NativeGame.S(person,"person_id"));
        return actor!=null?actor.InsideHome&&actor.InsideHomeBuildingId==homeId:
            NativeGame.B(person,"at_home")&&NativeGame.S(person,"inside_home_building_id",NativeGame.S(person,"home_building_id"))==homeId;
    }).ToArray();
}
