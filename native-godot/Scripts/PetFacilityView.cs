using Godot;
using System;
using System.Linq;
using System.Text.Json.Nodes;

public partial class Main
{
    Node3D? _petFacilityScene;

    static Vector3 PetFacilityCenter(NativeGame.PetFacility facility)=>PetActivityCenter+new Vector3(((int)facility-2)*9,0,15.5f);
    internal static Vector3 PetFacilityUsePoint(NativeGame.PetFacility facility,string id)=>PetFacilityCenter(facility)+
        new Vector3((PersonHash(id)%5-2f)*.40f,0,facility==NativeGame.PetFacility.Splash?0:1.05f);

    void CreatePetFacilities()
    {
        _petFacilityScene=new Node3D{Name="PreferredPetFacilities"};AddChild(_petFacilityScene);
        foreach(var facility in Enum.GetValues<NativeGame.PetFacility>()){
            var area=new Node3D{Position=PetFacilityCenter(facility)};_petFacilityScene.AddChild(area);
            var model=new DistrictAccentMesh();
            model.Box(new(0,.035f,0),new(5,.07f,3.35f),facility==NativeGame.PetFacility.Dig?"cba473":"c2b485");
            switch(facility){
                case NativeGame.PetFacility.Climb:
                    foreach(float x in new[]{-1.6f,1.6f})model.Box(new(x,.84f,-.45f),new(.16f,1.65f,1.9f),"a98461");
                    for(int i=-2;i<=2;i++)model.Box(new(i*.60f,.7f+(.9f-Math.Abs(i)*.35f),-.42f),new(.65f,.15f,1.65f),"c2a577");
                    model.Box(new(0,.33f,.53f),new(1.9f,.62f,1.16f),"b38d60");
                    break;
                case NativeGame.PetFacility.Dig:
                    foreach(float x in new[]{-2.22f,2.22f})model.Box(new(x,.20f,0),new(.22f,.39f,3.03f),"aa8158");
                    foreach(float z in new[]{-1.48f,1.48f})model.Box(new(0,.19f,z),new(4.55f,.38f,.21f),"ad865a");
                    for(int i=-2;i<=2;i++)model.Round(new(i*.73f,.22f,-.56f+Math.Abs(i%2)*.62f),new(.84f,.42f,.7f),"a87c56");
                    break;
                case NativeGame.PetFacility.Splash:
                    model.Round(new(0,.18f,0),new(4.4f,.35f,2.9f),"719699");
                    model.Round(new(0,.31f,0),new(3.85f,.13f,2.38f),"88bbc5");
                    for(int i=-1;i<=1;i++)model.Box(new(i*.73f,.34f,i*.32f),new(.63f,.023f,.08f),"c7e1d9");
                    break;
                case NativeGame.PetFacility.Perch:
                    foreach(float x in new[]{-1.4f,1.4f})model.Box(new(x,1.05f,-.46f),new(.20f,2.04f,.22f),"916d4a");
                    model.Box(new(0,1.83f,-.46f),new(3.3f,.22f,.22f),"aa855a");
                    model.Box(new(0,1.17f,.1f),new(2.2f,.17f,.44f),"b69265");
                    model.Round(new(-1.4f,2.14f,-.46f),new(.8f,.42f,.60f),"a8b47e");
                    break;
                default:
                    foreach(float x in new[]{-1.5f,1.5f})model.Box(new(x,.66f,-.12f),new(.16f,1.26f,1.5f),"948268");
                    model.Box(new(0,1.31f,-.12f),new(3.36f,.23f,1.85f),"789976");
                    model.Box(new(0,.16f,.32f),new(3.2f,.22f,1.3f),"dbb68b");
                    break;
            }
            area.AddChild(new MeshInstance3D{Mesh=model.Finish()});
            area.AddChild(TownLifeSign(NativeGame.PetFacilityName(facility),facility is NativeGame.PetFacility.Climb or NativeGame.PetFacility.Perch?2.65f:1.72f));
        }
    }

    void PetFacilityArchive(JsonNode pet)
    {
        string id=NativeGame.S(pet,"pet_id");
        var card=MarketCard(_managementBody!,color:"eaf0e3");
        MarketLabel(card,"活动偏好",17,TownTheme.Accent("档案"));
        var preference=NativeGame.PreferredPetFacility(pet);
        var secondary=NativeGame.PersonalityPetFacility(pet);
        MarketLabel(card,$"常去 {NativeGame.PetFacilityName(preference)}"+(secondary!=preference?$" · 性格偏好 {NativeGame.PetFacilityName(secondary)}":""));
        var counts=Enum.GetValues<NativeGame.PetFacility>().Select(facility=>(Facility:facility,Count:Game.PetFacilityUseCount(id,facility)))
            .Where(row=>row.Count>0).OrderByDescending(row=>row.Count).ToArray();
        if(counts.Length>0)MarketLabel(card,string.Join(" · ",counts.Select(row=>$"{NativeGame.PetFacilityName(row.Facility)} {row.Count} 次")),color:TownTheme.Muted,wrap:true);
        var visits=Game.RecentPetFacilityUses(id);
        if(visits.Length==0)MarketLabel(card,"尚无到访记录。伙伴走到设施并开始活动后会自动记录。",color:TownTheme.Muted,wrap:true);
        foreach(var visit in visits)MarketLabel(card,$"{TownProjectClock(visit.When)} · {NativeGame.PetFacilityName(visit.Facility)}",color:TownTheme.Muted);
        MarketButton(card,"定位活动设施",()=>{
            var target=PetFacilityCenter(preference);RememberCameraSite(target);StopPetFollow();EndPlacement();
            _portraitAnchor=null;_buildCamera=false;_center=target;_pitch=.73f;_distance=32;_management?.Hide();
        },expand:false,accent:"地图");
    }
}

public partial class PetAgent
{
    NativeGame.PetFacility? _facilityIntent;
    bool _facilityVisited;

    bool ChoosePetFacility(JsonNode state)
    {
        var preferred=NativeGame.PreferredPetFacility(state);
        var personal=NativeGame.PersonalityPetFacility(state);
        var facility=personal!=preferred&&_random.NextDouble()<.30?personal:preferred;
        var target=Main.PetFacilityUsePoint(facility,Ident);
        var path=_navigation!.Route(Position,target,.85f);
        if(path.Count==0)return false;
        _facilityIntent=facility;
        _target=target;
        _intent=NativeGame.PetFacilityName(facility);
        _path.AddRange(path);
        return true;
    }

    bool UsePetFacility(JsonNode state)
    {
        if(_facilityIntent is not NativeGame.PetFacility facility||Position.DistanceSquaredTo(_target)>1.25f*1.25f)return false;
        if(!_facilityVisited){_facilityVisited=true;Town.Game.RecordPetFacilityUse(Ident,facility);}
        Activity=facility switch{
            NativeGame.PetFacility.Climb=>"在攀爬架玩耍",NativeGame.PetFacility.Dig=>"正在刨土",
            NativeGame.PetFacility.Splash=>"正在戏水",NativeGame.PetFacility.Perch=>"在栖架旁张望",_=>"在遮荫窝休息"
        };
        ShowPose(state,facility==NativeGame.PetFacility.Shelter?"sleep":facility==NativeGame.PetFacility.Splash?"bath":"pet");
        if(facility is NativeGame.PetFacility.Climb or NativeGame.PetFacility.Perch)_visual.Position=Vector3.Up*(.3f+.11f*Mathf.Sin((float)Time.GetTicksMsec()*.005f));
        return true;
    }
}
