using Godot;
using System;
using System.Collections.Generic;

public partial class Main
{
    readonly Dictionary<(ArrayMesh Source, string District, int Variant), ArrayMesh> _districtPalettes = new();
    static readonly (string Wall, string Roof)[] ManorPalettes =
    {
        ("ecd9b6", "7a9177"),
        ("e8cda9", "a66e55"),
        ("ece0c4", "728e9c"),
        ("ead7be", "947c91"),
    };

    // Dress the landmark after its gameplay collision has been registered.
    void ApplyDistrictArt(MeshInstance3D landmark, string district, int variant = 0)
    {
        var bounds = landmark.Mesh.GetAabb();
        if (bounds.Size.Y < (district == "ranch" ? 3f : 8f)) return;
        int style = district == "estate" ? variant % ManorPalettes.Length
            : Mathf.RoundToInt(landmark.Position.X * .07f + landmark.Position.Z * .13f) & 3;
        (string Wall, string Roof)? palette = district switch
        {
            "estate" => ManorPalettes[style],
            "market" => ("f1c79f", "aa5b51"),
            "industry" => (style % 2 == 0 ? "b57560" : "a88575", "587482"),
            "homes" => ("e6d5ce", "8e81a0"),
            "fields" => ("edd7a7", "9c7855"),
            "station" => ("ecd2a1", "668f88"),
            "ranch" => ("dec5a1", style % 2 == 0 ? "92765d" : "a37357"),
            "city" => (style % 2 == 0 ? "ced9d1" : "dbcdd7", style % 2 == 0 ? "546f80" : "706887"),
            _ => null,
        };
        if (palette is { } colors && landmark.Mesh is ArrayMesh mesh)
        {
            var key = (mesh, district, style);
            if (!_districtPalettes.TryGetValue(key, out var tinted))
                _districtPalettes[key] = tinted = TintDistrictHouse(mesh, colors.Wall, colors.Roof);
            landmark.Mesh = tinted;
        }

        var accents = new DistrictAccentMesh();
        float width = bounds.Size.X, front = bounds.End.Z, roof = bounds.End.Y;
        switch (district)
        {
            case "estate":
                DressManor(accents, width, front, roof, style);
                break;
            case "market":
                DressMarket(accents, width, front, roof);
                AddMarketSign(landmark, front);
                break;
            case "industry":
                DressIndustry(accents, width, front, roof, style);
                break;
            case "homes":
                accents.Box(new(0, .23f, front - .5f), new(width * .31f, .24f, .9f), "c4b6c6");
                accents.Box(new(-width * .36f, .58f, front - .7f), new(1.25f, .65f, .8f), "ac816c");
                accents.Round(new(-width * .36f, 1.05f, front - .7f), new(1.1f, .7f, .68f), "8a9d7f");
                break;
            case "fields":
                for (int i = 0; i < 5; i++)
                {
                    float x = width * (-.39f + i * .075f);
                    accents.Box(new(x, .32f, front - .65f), new(.62f, .36f, 1.2f), "a98059");
                    accents.Round(new(x, .68f, front - .65f), new(.58f, .52f, 1.1f), i % 2 == 0 ? "9cad66" : "d1b366");
                }
                break;
            case "station":
                accents.Box(new(0, .30f, front - .65f), new(width * .83f, .20f, 1.1f), "b8bfac");
                for (int i = -3; i <= 3; i++)
                    accents.Box(new(i * width * .108f, .42f, front - .20f), new(.95f, .055f, .12f), "e5c47b");
                break;
            case "ranch":
                accents.Box(new(0,.22f,front-.40f),new(width*.60f,.15f,.67f),"ae8c69");
                for(int index=0;index<3;index++){
                    float x=width*(-.42f+index*.084f);
                    accents.Box(new(x,.47f,front-.56f),new(.83f,.64f,.72f),index%2==0?"d5b469":"bea05a");
                    accents.Box(new(x,.79f,front-.56f),new(.10f,.08f,.73f),"947848");
                }
                break;
            case "city":
                accents.Box(new(0, roof - .38f, 0), new(width * .62f, .26f, .44f), "7facae");
                for (int i = -2; i <= 2; i++)
                    accents.Box(new(i * width * .13f, .30f, front - .68f), new(width * .11f, .17f, .88f),
                        i % 2 == 0 ? "8dabb3" : "b0c1c3");
                break;
        }
        if (accents.Finish() is ArrayMesh accentMesh)
            landmark.AddChild(new MeshInstance3D { Name = "DistrictArt", Mesh = accentMesh });
    }

    void CreateParkShelter()
    {
        var gate=Data.Portals["park"];
        foreach(var offset in new[]{new Vector2I(-1,2),new Vector2I(0,-2),new Vector2I(0,3),new Vector2I(-1,4)}){
            var cell=gate+offset;
            if(Data.Roads.ContainsKey(cell)||_natural.Exists(entry=>entry.Cell==cell&&entry.Node.Visible)
                ||Game.RoadReason(new(cell.X,cell.Y),"garden")!="")continue;
            var shelter=new DistrictAccentMesh();
            shelter.Box(new(0,.13f,0),new(5.5f,.24f,4.5f),"a78a67");
            for(int x=-1;x<=1;x+=2)
                for(int z=-1;z<=1;z+=2)
                    shelter.Box(new(x*2.1f,1.55f,z*1.55f),new(.24f,2.8f,.24f),"87694d");
            shelter.Box(new(0,3.02f,-.96f),new(5.15f,.19f,2.23f),"658b7b",-.15f);
            shelter.Box(new(0,3.02f,.96f),new(5.15f,.19f,2.23f),"769b88",.15f);
            shelter.Box(new(0,.72f,-1.14f),new(3.3f,.18f,.70f),"c6a074");
            shelter.Box(new(0,1.02f,-1.43f),new(3.3f,.37f,.13f),"b38a64");
            var node=new MeshInstance3D{Name="ParkShelter",Mesh=shelter.Finish(),Position=WorldData.Cell(cell)};
            AddChild(node);
            _dynamic["district:park"]=node;
            return;
        }
    }

    static ArrayMesh TintDistrictHouse(ArrayMesh source, string wall, string roof)
    {
        var arrays = source.SurfaceGetArrays(0);
        var vertices = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
        var normals = arrays[(int)Mesh.ArrayType.Normal].AsVector3Array();
        var colors = arrays[(int)Mesh.ArrayType.Color].AsColorArray();
        var wallInk = new Color(wall);
        var roofInk = new Color(roof);
        // The exported landmark geometry is unindexed. Select large wall faces and
        // upper sloped roof faces while preserving door, glass and trim vertices.
        for (int i = 0; i + 2 < vertices.Length; i += 3)
        {
            float low = Math.Min(vertices[i].Y, Math.Min(vertices[i + 1].Y, vertices[i + 2].Y));
            float high = Math.Max(vertices[i].Y, Math.Max(vertices[i + 1].Y, vertices[i + 2].Y));
            Color ink;
            if (Math.Abs(normals[i].Y) < .04f && low is >= .6f and <= .85f && high > 6.5f) ink = wallInk;
            else if (normals[i].Y is > .25f and < .98f && low > 7f && high - low > 1f) ink = roofInk;
            else continue;
            for (int vertex = i; vertex < i + 3; vertex++) colors[vertex] = colors[vertex].Lerp(ink, .83f);
        }
        arrays[(int)Mesh.ArrayType.Color] = colors;
        var result = new ArrayMesh();
        result.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        result.SurfaceSetMaterial(0, source.SurfaceGetMaterial(0));
        return result;
    }

    static void DressManor(DistrictAccentMesh mesh, float width, float front, float roof, int style)
    {
        string timber = style % 2 == 0 ? "9a7051" : "886758";
        string blossoms = style % 2 == 0 ? "edb2a0" : "c5acd5";
        float foundation = front - 1.1f;
        mesh.Box(new(0, .61f, front - .37f), new(width * .23f, .09f, .6f), "e1cd9d");
        for (int side = -1; side <= 1; side += 2)
        {
            float x = side * width * .40f;
            mesh.Box(new(x, .45f, foundation), new(width * .15f, .52f, 1.07f), timber);
            mesh.Box(new(x, .69f, foundation), new(width * .16f, .13f, 1.16f), "b28560");
            mesh.Round(new(x, .80f, foundation), new(width * .14f, .49f, .91f), "809f68");
            for (int i = -1; i <= 1; i++)
                mesh.Round(new(x + i * .32f, 1.09f, foundation + .16f), new(.30f, .29f, .42f),
                    i == 0 ? "f5d48b" : blossoms);
        }
        float treeX = width * (style % 2 == 0 ? -.42f : .42f);
        mesh.Box(new(treeX, 1.33f, foundation - .21f), new(.25f, 1.37f, .36f), timber);
        mesh.Round(new(treeX, 2.20f, foundation - .21f), new(1.45f, 1.80f, 1.95f), "779b72");
        mesh.Round(new(treeX - .34f, 2.44f, foundation - .05f), new(.96f, 1.22f, 1.23f), "92ac75");
        for (int i = -1; i <= 1; i++)
            mesh.Round(new(treeX + i * .36f, 2.23f + i * .27f, foundation + .59f), new(.23f, .29f, .34f),
                style % 2 == 0 ? "db8a63" : "dba94f");
        if (style % 2 == 0)
        {
            mesh.Box(new(0, roof - .96f, 1.38f), new(1.35f, 1.20f, 1.0f), "d6bf9b");
            mesh.Box(new(0, roof - .97f, 1.90f), new(.64f, .64f, .12f), "74958e");
            mesh.Box(new(0, roof - .31f, 1.42f), new(1.67f, .23f, 1.27f), ManorPalettes[style].Roof, .18f);
        }
        else
        {
            mesh.Box(new(-1.50f, roof - .27f, -.58f), new(.73f, 1.43f, .86f), "b47c62");
            mesh.Box(new(-1.50f, roof + .46f, -.58f), new(.91f, .23f, 1.04f), "91644e");
        }
    }

    static void DressMarket(DistrictAccentMesh mesh, float width, float front, float roof)
    {
        const int stripes = 9;
        float canopy = width * .61f, panel = canopy / stripes;
        float y = roof * .555f;
        for (int i = 0; i < stripes; i++)
        {
            float x = -canopy / 2 + (i + .5f) * panel;
            string fabric = i % 2 == 0 ? "bd6457" : "f4e4bd";
            mesh.Box(new(x, y + .29f, front - 1.14f), new(panel + .015f, .13f, 2.25f), fabric, .055f);
            mesh.Box(new(x, y - .08f, front + .045f), new(panel + .015f, .42f, .13f), fabric);
        }
        mesh.Box(new(0, y + 1.0f, front - 1.79f), new(width * .34f, 1.00f, .17f), "8a5142");
        mesh.Box(new(0, .34f, front - .34f), new(width * .72f, .20f, .84f), "cba289");
        for (int row = 0; row < 2; row++)
            for (int column = -4; column <= 4; column++)
            {
                float tile = width * .073f;
                mesh.Box(new(column * tile, .075f, front + .12f + row * .52f),
                    new(tile * .96f, .10f, .49f), (column + row) % 2 == 0 ? "cfac8b" : "e2c7a1");
            }
        for (int side = -1; side <= 1; side += 2)
        {
            float x = side * width * .40f;
            mesh.Box(new(x, .42f, front - .87f), new(1.17f, .65f, .91f), "916747");
            mesh.Round(new(x, .86f, front - .87f), new(1.13f, .56f, .84f), side < 0 ? "d99761" : "a9af6e");
        }
    }

    static void AddMarketSign(MeshInstance3D landmark, float front)
    {
        landmark.AddChild(new Label3D
        {
            Name = "MarketSign", Text = "晨光市集", FontSize = 35, PixelSize = .021f,
            Font = new SystemFont { FontNames = new[] { "Microsoft YaHei", "Noto Sans CJK SC" } },
            OutlineSize = 2, Modulate = new Color("fff1d0"), OutlineModulate = new Color("703b32"),
            Position = new(0, landmark.Mesh.GetAabb().End.Y * .555f + 1.0f, front - 1.67f),
        });
    }

    static void DressIndustry(DistrictAccentMesh mesh, float width, float front, float roof, int style)
    {
        float tower = width * (style % 2 == 0 ? -.24f : .24f);
        mesh.Box(new(tower, roof - .05f, -.78f), new(1.22f, 4.1f, 1.59f), "a86855");
        mesh.Box(new(tower, roof + 1.89f, -.78f), new(1.48f, .35f, 1.88f), "5e6266");
        mesh.Box(new(tower, roof + 2.09f, -.78f), new(1.04f, .08f, 1.38f), "494f54");
        mesh.Box(new(-tower * .65f, roof - 1.56f, -.27f), new(width * .25f, .69f, 2.31f), "74878c");
        mesh.Box(new(-tower * .65f, roof - 1.13f, -.27f), new(width * .27f, .20f, 2.46f), "526c76");
        mesh.Box(new(width * .34f, 2.2f, front - 1.48f), new(.37f, 4.1f, .58f), "667c85");
        mesh.Box(new(width * .26f, 4.23f, front - 1.48f), new(width * .19f, .37f, .58f), "667c85");
        for (int i = 0; i < 3; i++)
        {
            float x = width * (-.40f + .07f * i);
            mesh.Box(new(x, .65f, front - .81f), new(.95f, .80f, 1.23f), i % 2 == 0 ? "9d7453" : "65777a");
            mesh.Box(new(x, 1.06f, front - .81f), new(1.03f, .13f, 1.31f), "c29b6f");
        }
        for (int i = -2; i <= 2; i++)
            mesh.Box(new(i * width * .086f, .40f, front - .34f), new(.57f, .05f, .21f), "e5bb71");
    }

    sealed class DistrictAccentMesh
    {
        sealed record Primitive(Vector3[] Points, Vector3[] Normals, int[] Indices)
        {
            public static Primitive From(PrimitiveMesh mesh)
            {
                using (mesh)
                {
                    var arrays = mesh.GetMeshArrays();
                    var points = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
                    var indices = arrays[(int)Mesh.ArrayType.Index].AsInt32Array();
                    if (indices.Length == 0)
                    {
                        indices = new int[points.Length];
                        for (int i = 0; i < indices.Length; i++) indices[i] = i;
                    }
                    return new(points, arrays[(int)Mesh.ArrayType.Normal].AsVector3Array(), indices);
                }
            }
        }

        static readonly Primitive Cube = Primitive.From(new BoxMesh { Size = Vector3.One });
        static readonly Primitive Crown = Primitive.From(new SphereMesh { Radius = .5f, Height = 1f, RadialSegments = 7, Rings = 4 });
        static readonly StandardMaterial3D Ink = new() { VertexColorUseAsAlbedo = true, Roughness = .94f };
        readonly List<(Primitive Shape, Vector3 Center, Vector3 Size, Color Tint, float Tilt)> _pieces = new();

        public void Box(Vector3 center, Vector3 size, string tint, float tilt = 0) =>
            _pieces.Add((Cube, center, size, new Color(tint), tilt));
        public void Round(Vector3 center, Vector3 size, string tint) =>
            _pieces.Add((Crown, center, size, new Color(tint), 0));

        public ArrayMesh? Finish()
        {
            if (_pieces.Count == 0) return null;
            using var surface = new SurfaceTool();
            surface.Begin(Mesh.PrimitiveType.Triangles);
            surface.SetMaterial(Ink);
            foreach (var (shape, center, size, tint, tilt) in _pieces)
            {
                var basis = new Basis(Vector3.Right, tilt);
                foreach (int index in shape.Indices)
                {
                    surface.SetColor(tint);
                    surface.SetNormal((basis * (shape.Normals[index] / size)).Normalized());
                    surface.AddVertex(center + basis * (shape.Points[index] * size));
                }
            }
            return surface.Commit();
        }
    }
}
