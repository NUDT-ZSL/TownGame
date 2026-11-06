using Godot;
using System.Collections.Generic;

public partial class Main
{
    const int FarmRoutesPerTick=6, FarmOccupancyLimit=24000;
    readonly Dictionary<Vector2I,bool> _farmOccupancy=new();
    string _farmOccupancyLayout="";
    ulong _farmRouteTick=ulong.MaxValue,_farmInvalidationTick=ulong.MaxValue;
    int _farmRoutesUsed;

    internal bool TryScheduleFarmRoutes(int count=1)
    {
        ulong frame=Engine.GetPhysicsFrames();
        if(_farmRouteTick!=frame){_farmRouteTick=frame;_farmRoutesUsed=0;}
        if(_farmRoutesUsed+count>FarmRoutesPerTick)return false;
        _farmRoutesUsed+=count;
        return true;
    }

    internal bool IsFarmCellClear(Vector2I cell,FarmNavigation navigation)
    {
        if(_farmOccupancyLayout!=FarmLayoutSignature){
            _farmOccupancyLayout=FarmLayoutSignature;
            _farmOccupancy.Clear();
        }
        if(_farmOccupancy.TryGetValue(cell,out bool clear))return clear;
        if(_farmOccupancy.Count>=FarmOccupancyLimit)_farmOccupancy.Clear();
        return _farmOccupancy[cell]=navigation.Clear(new(cell.X,0,cell.Y));
    }

    internal void InvalidateFarmOccupancy()
    {
        ulong frame=Engine.GetPhysicsFrames();
        if(frame==_farmInvalidationTick)return;
        _farmInvalidationTick=frame;
        _farmOccupancy.Clear();
    }
}
