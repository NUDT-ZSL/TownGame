using Godot;
using System;
using System.Linq;

public partial class FinanceChart : Control
{
    internal readonly record struct Bucket(string Label,string Range,long Income,long Expense);
    Bucket[] _buckets=Array.Empty<Bucket>();
    int _selected=-1;
    const float Left=28,Right=14;
    float Step=>Math.Max(1,(Size.X-Left-Right)/Math.Max(1,_buckets.Length));
    internal event Action<int>? BucketSelected;

    internal void SetBuckets(Bucket[] buckets,int selected)
    {
        if(_selected==selected&&_buckets.SequenceEqual(buckets))return;
        _buckets=buckets;
        _selected=selected;
        QueueRedraw();
    }

    public override string _GetTooltip(Vector2 point)
    {
        int index=BucketAt(point);
        if(index<0)return "点击时间段，或聚焦后使用左右方向键查看收支。";
        var bucket=_buckets[index];
        return $"{bucket.Range}\n收入 {bucket.Income:N0} · 支出 {bucket.Expense:N0} · 净额 {bucket.Income-bucket.Expense:+#,0;-#,0;0}";
    }

    public override void _GuiInput(InputEvent input)
    {
        if(_buckets.Length==0)return;
        int index=input switch{
            InputEventMouseButton {ButtonIndex:MouseButton.Left,Pressed:true} button=>BucketAt(button.Position),
            InputEventKey {Pressed:true,Keycode:Key.Left}=>_selected<0?_buckets.Length-1:Math.Max(0,_selected-1),
            InputEventKey {Pressed:true,Keycode:Key.Right}=>_selected<0?_buckets.Length-1:Math.Min(_buckets.Length-1,_selected+1),
            _=>-1
        };
        if(index<0)return;
        AcceptEvent();
        BucketSelected?.Invoke(index);
    }

    int BucketAt(Vector2 point)=>point.X>=Left&&point.X<Size.X-Right&&point.Y>=18&&point.Y<Size.Y
        ?Math.Min(_buckets.Length-1,Mathf.FloorToInt((point.X-Left)/Step)):-1;

    public override void _Draw()
    {
        var ink=TownTheme.Ink;
        var incomeInk=new Color(TownTheme.HighContrast?"205c53":"6b9e75");
        var expenseInk=new Color(TownTheme.HighContrast?"7b2839":"c4745b");
        float axis=Size.Y*.53f;
        DrawRect(new Rect2(Vector2.Zero,Size),new Color("f4f0e5"));
        if(_buckets.Length==0)return;
        long max=Math.Max(1,_buckets.Max(bucket=>Math.Max(bucket.Income,bucket.Expense)));
        if(_selected>=0)
            DrawRect(new Rect2(Left+_selected*Step,20,Step,Size.Y-42),new Color("dbe8dc"));
        DrawLine(new(Left,axis),new(Size.X-Right,axis),TownTheme.Muted);
        var font=GetThemeDefaultFont();
        DrawString(font,new(8,axis-8),"+",fontSize:15,modulate:ink);
        DrawString(font,new(8,axis+19),"−",fontSize:15,modulate:ink);
        DrawString(font,new(Left,17),$"单段最大 {(_buckets.Any(bucket=>bucket.Income>0||bucket.Expense>0)?max:0):N0} 金币",fontSize:13,modulate:TownTheme.Muted);
        int labelEvery=Math.Max(1,Mathf.CeilToInt(42/Step));
        for(int i=0;i<_buckets.Length;i++){
            var bucket=_buckets[i];
            float width=Math.Max(1,Step*.66f),x=Left+i*Step+(Step-width)/2;
            float up=(float)((double)bucket.Income/max)*(axis-27),down=(float)((double)bucket.Expense/max)*(Size.Y-axis-26);
            if(up>0)DrawRect(new Rect2(x,axis-up,width,up),incomeInk);
            if(down>0)DrawRect(new Rect2(x,axis,width,down),expenseInk);
            if(i==_selected)DrawRect(new Rect2(Left+i*Step,20,Step,Size.Y-42),TownTheme.Leaf,false,1);
            if(i%labelEvery==0||i==_selected){
                float labelWidth=font.GetStringSize(bucket.Label,fontSize:12).X;
                float labelX=Math.Clamp(Left+i*Step+(Step-labelWidth)/2,2,Math.Max(2,Size.X-labelWidth-2));
                DrawString(font,new(labelX,Size.Y-7),bucket.Label,fontSize:12,modulate:TownTheme.Muted);
            }
        }
    }
}
