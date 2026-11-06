using Godot;
using System.Linq;

public partial class Main
{
    readonly bool _useLegacyShadows=OS.GetCmdlineUserArgs().Contains("--legacy-shadows");
    int _townShadowBand=-1;

    float TownShadowDistance=>_residentialSun?.DirectionalShadowMaxDistance??0;

    void UpdateTownShadowProfile()
    {
        if(_useLegacyShadows||_residentialSun==null||!_cameraFrameReady)return;
        float cameraDistance=_camera.GlobalPosition.DistanceTo(_shownCamera.Target);
        // Overlapping bands avoid changing the shadow projection back and forth
        // while the camera eases near a boundary or steps around a wall.
        int band=cameraDistance<20||_townShadowBand==0&&cameraDistance<27?0:
            cameraDistance<70||_townShadowBand==1&&cameraDistance<83?1:2;
        if(band==_townShadowBand)return;
        _townShadowBand=band;
        _residentialSun.DirectionalShadowMode=ActiveGraphics.ShadowSplits==2?DirectionalLight3D.ShadowMode.Parallel2Splits:DirectionalLight3D.ShadowMode.Parallel4Splits;
        _residentialSun.DirectionalShadowMaxDistance=(band switch{0=>96,1=>176,_=>240})*ActiveGraphics.ShadowDistance;
        _residentialSun.DirectionalShadowSplit1=band==0?.13f:.10f;
        _residentialSun.DirectionalShadowSplit2=band==0?.32f:.25f;
        _residentialSun.DirectionalShadowSplit3=.56f;
        _residentialSun.DirectionalShadowBlendSplits=true;
        _residentialSun.DirectionalShadowFadeStart=band==0?.66f:.76f;
        _residentialSun.ShadowNormalBias=band==0?1.6f:2;
        _residentialSun.ShadowOpacity=.90f;
    }
}
