using System;
using System.Buffers.Binary;
using System.Text;

public static class NativeAppearance
{
    public static readonly string[][] RabbitGroups={new[]{"奶油","雪白","可可","银灰","焦糖","炭灰","燕麦","肉桂"},new[]{"纯色","眼罩斑","额头白纹","花斑"},new[]{"立耳","垂耳","单侧垂耳"},new[]{"无额毛","一撮","两撮","三撮"}};
    public static readonly string[][] OtherGroups={new[]{"原色","雪白","可可","银灰","焦糖","炭灰","燕麦","肉桂"},new[]{"纯色","背斑","浅色纹","斑点"},new[]{"标准","纤细","圆润"},new[]{"无点缀","领结","徽章","小花"}};
    public static readonly int[] Steps={1,8,32,96};
    public static string Description(string species,int variant){var groups=species=="兔子"?RabbitGroups:OtherGroups;return string.Join(" · ",System.Linq.Enumerable.Select(System.Linq.Enumerable.Range(0,4),i=>groups[i][variant/Steps[i]%groups[i].Length]));}
    // BLAKE2b(digest_size=8)，与旧版identity_seed('rabbit:'+id)逐字节兼容。
    static readonly ulong[] Iv={0x6a09e667f3bcc908,0xbb67ae8584caa73b,0x3c6ef372fe94f82b,0xa54ff53a5f1d36f1,0x510e527fade682d1,0x9b05688c2b3e6c1f,0x1f83d9abfb41bd6b,0x5be0cd19137e2179};
    static readonly byte[,] Sigma={{0,1,2,3,4,5,6,7,8,9,10,11,12,13,14,15},{14,10,4,8,9,15,13,6,1,12,0,2,11,7,5,3},{11,8,12,0,5,2,15,13,10,14,3,6,7,1,9,4},{7,9,3,1,13,12,11,14,2,6,5,10,4,0,15,8},{9,0,5,7,2,4,10,15,14,1,11,12,6,8,3,13},{2,12,6,10,0,11,8,3,4,13,7,5,15,14,1,9},{12,5,1,15,14,13,4,10,0,7,6,3,9,2,8,11},{13,11,7,14,12,1,3,9,5,0,15,4,8,6,2,10},{6,15,14,9,11,3,0,8,12,2,13,7,1,4,10,5},{10,2,8,4,7,6,1,5,15,11,9,14,3,12,13,0}};
    static ulong Rotate(ulong x,int n)=>(x>>n)|(x<<(64-n));
    public static int Default(string id)
    {
        if(id=="")return 0;byte[] input=Encoding.UTF8.GetBytes("rabbit:"+id);var h=(ulong[])Iv.Clone();h[0]^=0x01010008;
        for(int offset=0;offset<input.Length;offset+=128){
            int count=Math.Min(128,input.Length-offset);var block=new byte[128];Array.Copy(input,offset,block,0,count);var m=new ulong[16];for(int i=0;i<16;i++)m[i]=BinaryPrimitives.ReadUInt64LittleEndian(block.AsSpan(i*8));
            var v=new ulong[16];Array.Copy(h,v,8);Array.Copy(Iv,0,v,8,8);v[12]^=(ulong)(offset+count);if(offset+count==input.Length)v[14]=~v[14];
            void G(int a,int b,int c,int d,ulong x,ulong y){unchecked{v[a]+=v[b]+x;v[d]=Rotate(v[d]^v[a],32);v[c]+=v[d];v[b]=Rotate(v[b]^v[c],24);v[a]+=v[b]+y;v[d]=Rotate(v[d]^v[a],16);v[c]+=v[d];v[b]=Rotate(v[b]^v[c],63);}}
            for(int round=0;round<12;round++){int r=round%10;G(0,4,8,12,m[Sigma[r,0]],m[Sigma[r,1]]);G(1,5,9,13,m[Sigma[r,2]],m[Sigma[r,3]]);G(2,6,10,14,m[Sigma[r,4]],m[Sigma[r,5]]);G(3,7,11,15,m[Sigma[r,6]],m[Sigma[r,7]]);G(0,5,10,15,m[Sigma[r,8]],m[Sigma[r,9]]);G(1,6,11,12,m[Sigma[r,10]],m[Sigma[r,11]]);G(2,7,8,13,m[Sigma[r,12]],m[Sigma[r,13]]);G(3,4,9,14,m[Sigma[r,14]],m[Sigma[r,15]]);}
            for(int i=0;i<8;i++)h[i]^=v[i]^v[i+8];
        }
        Span<byte> digest=stackalloc byte[8];BinaryPrimitives.WriteUInt64LittleEndian(digest,h[0]);return (int)(BinaryPrimitives.ReadUInt64BigEndian(digest)%384);
    }
}
