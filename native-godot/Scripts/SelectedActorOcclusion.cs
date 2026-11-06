using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

public partial class Main
{
    readonly Dictionary<MeshInstance3D,Material?> _actorOccluders=new();
    readonly Dictionary<BaseMaterial3D,BaseMaterial3D> _fadedActorMaterials=new();
    MeshInstance3D[]? _staticActorOccluders;
    ShaderMaterial? _actorOcclusionShader;
    double _occlusionScan;

    void UpdateSelectedActorOcclusion(double delta)
    {
        Vector3? target=SelectedActorOcclusionPoint();
        if(target==null||_camera.IsPositionBehind(target.Value)){
            RestoreActorOccluders();
            return;
        }
        var bounds=GetViewport().GetVisibleRect();
        var screen=_camera.UnprojectPosition(target.Value);
        if(!bounds.HasPoint(screen)){
            RestoreActorOccluders();
            return;
        }
        _occlusionScan-=delta;
        if(_occlusionScan<=0){
            _occlusionScan=.10;
            var from=_camera.Projection==Camera3D.ProjectionType.Orthogonal?_camera.ProjectRayOrigin(screen):_camera.GlobalPosition;
            var next=FindActorOccluders(from,target.Value).ToHashSet();
            foreach(var mesh in _actorOccluders.Keys.Where(mesh=>!next.Contains(mesh)).ToArray())RestoreActorOccluder(mesh);
            foreach(var mesh in next)if(!_actorOccluders.ContainsKey(mesh))FadeActorOccluder(mesh);
        }
        if(_actorOcclusionShader==null||_actorOccluders.Count==0)return;
        _actorOcclusionShader.SetShaderParameter("actor_occlusion_point",new Vector4(target.Value.X,target.Value.Y,target.Value.Z,1));
        _actorOcclusionShader.SetShaderParameter("actor_occlusion_aspect",bounds.Size.X/bounds.Size.Y);
        foreach(string name in new[]{"season_tint","winter","window_glow"})
            _actorOcclusionShader.SetShaderParameter(name,_material.GetShaderParameter(name));
    }

    Vector3? SelectedActorOcclusionPoint()
    {
        if(_benchmark||InPhotoMode||_placementAction.Length>0||_management?.Visible==true||_townSearchWindow?.Visible==true)return null;
        if(_selectedPet.Length>0&&FarmPet(_selectedPet) is PetAgent pet&&!pet.IsQueuedForDeletion())
            return pet.GlobalPosition+Vector3.Up*Math.Max(.20f,pet.NameplateHeight*.57f);
        if(_selectedSceneResident.Length>0&&DirectoryPerson(_selectedSceneResident) is { } person&&
            SelectedResidentMarker(person) is { Site:ResidentMarkerSite.Person or ResidentMarkerSite.Truck } resident)
            return resident.Anchor.GlobalPosition+Vector3.Up*(resident.Site==ResidentMarkerSite.Truck?1.5f:1.05f);
        return null;
    }

    IEnumerable<MeshInstance3D> FindActorOccluders(Vector3 from,Vector3 target)
    {
        _staticActorOccluders??=GetChildren().OfType<MeshInstance3D>().Where(ObstacleMesh)
            .Where(mesh=>!_dynamic.Values.Contains(mesh)).ToArray();
        var currentBuildings=_dynamic.Where(pair=>pair.Key.StartsWith("town_building:",StringComparison.Ordinal)
            ||pair.Key.StartsWith("building:",StringComparison.Ordinal)||pair.Key.StartsWith("decoration:",StringComparison.Ordinal)||pair.Key=="district:park")
            .Select(pair=>pair.Value).OfType<MeshInstance3D>();
        foreach(var mesh in _staticActorOccluders.Concat(currentBuildings)){
            if(!GodotObject.IsInstanceValid(mesh)||mesh.IsQueuedForDeletion()||!mesh.IsVisibleInTree()||!BlocksActorRay(mesh,from,target))continue;
            yield return mesh;
            foreach(var child in mesh.GetChildren().OfType<MeshInstance3D>())
                if(child.IsVisibleInTree()&&BlocksActorRay(child,from,target))yield return child;
        }
    }

    static bool ObstacleMesh(MeshInstance3D mesh)
    {
        if(mesh.Mesh==null)return false;
        var size=mesh.GetAabb().Size;
        return size.Y>.65f&&size.X<95&&size.Z<95;
    }

    static bool BlocksActorRay(MeshInstance3D mesh,Vector3 from,Vector3 target)
    {
        if(!ObstacleMesh(mesh))return false;
        var bounds=mesh.GetAabb();
        var inverse=mesh.GlobalTransform.AffineInverse();
        return bounds.IntersectsSegment(inverse*from,inverse*target);
    }

    void FadeActorOccluder(MeshInstance3D mesh)
    {
        Material? fade=null;
        switch(mesh.GetActiveMaterial(0)){
            case ShaderMaterial material when material.Shader==_material.Shader:
                fade=_actorOcclusionShader??=new ShaderMaterial{Shader=_material.Shader};
                break;
            case BaseMaterial3D material:
                if(!_fadedActorMaterials.TryGetValue(material,out var filtered)){
                    filtered=(BaseMaterial3D)material.Duplicate();
                    var color=material.AlbedoColor;color.A*=.46f;
                    filtered.AlbedoColor=color;
                    filtered.Transparency=BaseMaterial3D.TransparencyEnum.AlphaHash;
                    _fadedActorMaterials[material]=filtered;
                }
                fade=filtered;
                break;
        }
        if(fade==null)return;
        _actorOccluders[mesh]=mesh.MaterialOverride;
        mesh.MaterialOverride=fade;
    }

    void RestoreActorOccluders()
    {
        if(_actorOccluders.Count==0)return;
        foreach(var (mesh,material) in _actorOccluders)
            if(GodotObject.IsInstanceValid(mesh)&&!mesh.IsQueuedForDeletion())mesh.MaterialOverride=material;
        _actorOccluders.Clear();
        _occlusionScan=0;
    }

    void RestoreActorOccluder(MeshInstance3D mesh)
    {
        if(_actorOccluders.Remove(mesh,out var material)&&GodotObject.IsInstanceValid(mesh)&&!mesh.IsQueuedForDeletion())
            mesh.MaterialOverride=material;
    }
}
