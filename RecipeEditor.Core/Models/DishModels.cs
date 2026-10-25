// 3D models of the game's dishes, read from the user's game for the "3D model" view: which mesh and materials a dish
// shows (ItemMesh0 of its blueprint) and which its recipe shows in the cookware while cooking (CookProgressMesh), and
// the geometry of LOD0 with the colour texture of each material. Nothing is written to the game.
using CUE4Parse.UE4.Assets.Exports;
using CUE4Parse.UE4.Assets.Exports.Material;
using CUE4Parse.UE4.Assets.Exports.StaticMesh;
using CUE4Parse.UE4.Objects.UObject;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RecipeEditor.Core.Game;
using RecipeEditor.Core.Icons;

namespace RecipeEditor.Core.Models;

/// <summary>
/// The look of a stock dish: its class, a recipe that makes it, the mesh and override materials of the item (null = the
/// mesh's own material for that slot) and the mesh and material shown in the cookware (null when cooked elsewhere); the
/// cookware material goes into slot CookSlot of that mesh only (a baking tray keeps its own, the cake gets the dish's).
/// </summary>
public sealed record DishModel(string Dish, string Recipe, string Mesh, IReadOnlyList<string?> Materials, string? CookMesh, string? CookMaterial, int CookSlot = 0)
{
    /// <summary>Where the recipe cooks (last part of the tags: Pot, Pan, Oven...).</summary>
    public IReadOnlyList<string> Utilities { get; init; } = [];

    /// <summary>The recipe's own placement of the cookware look (UseMeshRelativeTransform), else null: the cookware's offset.</summary>
    public Placement? CookPlacement { get; init; }

    /// <summary>SM_Pizza_Slice: the mesh's name without its folder.</summary>
    public string MeshName => ShortName(Mesh);

    public static string ShortName(string path) => path.Split('/').Last().Split('.')[0];
}

/// <summary>A transform in UE units (cm, Z up): translation, rotation (quaternion), scale.</summary>
public sealed record Placement(double X, double Y, double Z, double Qx = 0, double Qy = 0, double Qz = 0, double Qw = 1, double Sx = 1, double Sy = 1, double Sz = 1);

/// <summary>
/// A cooking vessel the cookware look is shown in (the pot, the pan): its mesh and materials, where the look sits in it
/// (the slot's location + CookMeshVisualOffset of CookingUtilityItemTag) and the slot's radius.
/// </summary>
public sealed record Cookware(string Tag, string Mesh, IReadOnlyList<string?> Materials, Placement Spot, double Radius);

/// <summary>A part of a mesh drawn with one material: triangles (indices into the mesh's vertices) and the colour texture.</summary>
public sealed record MeshPart(int[] Indices, string? Material, string? TexturePng);

/// <summary>The first full LOD of a static mesh in UE units (cm, Z up): positions, normals and UVs per vertex, three floats / two per vertex.</summary>
public sealed class MeshData
{
    public required string Name { get; init; }
    public required float[] Positions { get; init; }
    public required float[] Normals { get; init; }
    public required float[] Uvs { get; init; }
    public required IReadOnlyList<MeshPart> Parts { get; init; }
    public int Triangles => Parts.Sum(p => p.Indices.Length / 3);

    /// <summary>A copy moved by <paramref name="p"/>: scaled, rotated (quaternion), then translated (UE order).</summary>
    public MeshData Transformed(Placement p)
    {
        var pos = new float[Positions.Length];
        var nrm = new float[Normals.Length];
        for (var i = 0; i < pos.Length; i += 3)
        {
            var (x, y, z) = Rotate(p, Positions[i] * p.Sx, Positions[i + 1] * p.Sy, Positions[i + 2] * p.Sz);
            (pos[i], pos[i + 1], pos[i + 2]) = ((float)(x + p.X), (float)(y + p.Y), (float)(z + p.Z));
            var (nx, ny, nz) = Rotate(p, Normals[i], Normals[i + 1], Normals[i + 2]);
            (nrm[i], nrm[i + 1], nrm[i + 2]) = ((float)nx, (float)ny, (float)nz);
        }
        return new MeshData { Name = Name, Positions = pos, Normals = nrm, Uvs = Uvs, Parts = Parts };
    }

    private static (double, double, double) Rotate(Placement q, double x, double y, double z)
    {
        // v' = v + 2w(u×v) + 2u×(u×v), u = (qx, qy, qz)
        var (cx, cy, cz) = (q.Qy * z - q.Qz * y, q.Qz * x - q.Qx * z, q.Qx * y - q.Qy * x);
        var (dx, dy, dz) = (q.Qy * cz - q.Qz * cy, q.Qz * cx - q.Qx * cz, q.Qx * cy - q.Qy * cx);
        return (x + 2 * (q.Qw * cx + dx), y + 2 * (q.Qw * cy + dy), z + 2 * (q.Qw * cz + dz));
    }

    /// <summary>A flat mesh of a few vertices (the 25 cm quad of soups and stews): drawn on a level surface.</summary>
    public bool IsFlatQuad
    {
        get
        {
            if (Positions.Length / 3 is < 3 or > 8) return false;
            var z = Positions[2];
            for (var i = 2; i < Positions.Length; i += 3)
                if (Math.Abs(Positions[i] - z) > 0.01f) return false;
            return true;
        }
    }

    /// <summary>
    /// The flat quad as a disc of <paramref name="radius"/> around the quad's centre, with the quad's texture mapping (the
    /// liquid in a round pot: the quad's corners would stick out through its walls).
    /// </summary>
    public MeshData AsDisc(double radius, int segments = 48)
    {
        // the quad's UVs are an affine function of x, y: solve it from three of its corners
        int a = 0, b = 1, c = 2;
        double Ax(int i) => Positions[i * 3];
        double Ay(int i) => Positions[i * 3 + 1];
        var det = (Ax(b) - Ax(a)) * (Ay(c) - Ay(a)) - (Ax(c) - Ax(a)) * (Ay(b) - Ay(a));
        (double, double) Uv(double x, double y)
        {
            if (Math.Abs(det) < 1e-9) return (0.5, 0.5);
            var s = ((x - Ax(a)) * (Ay(c) - Ay(a)) - (Ax(c) - Ax(a)) * (y - Ay(a))) / det;
            var t = ((Ax(b) - Ax(a)) * (y - Ay(a)) - (x - Ax(a)) * (Ay(b) - Ay(a))) / det;
            return (Uvs[a * 2] + s * (Uvs[b * 2] - Uvs[a * 2]) + t * (Uvs[c * 2] - Uvs[a * 2]),
                    Uvs[a * 2 + 1] + s * (Uvs[b * 2 + 1] - Uvs[a * 2 + 1]) + t * (Uvs[c * 2 + 1] - Uvs[a * 2 + 1]));
        }
        var n = Positions.Length / 3;
        double cx = 0, cy = 0;
        for (var i = 0; i < n; i++) { cx += Ax(i) / n; cy += Ay(i) / n; }
        var z = Positions[2];
        var pos = new List<float> { (float)cx, (float)cy, z };
        var uv = new List<float>();
        var (u0, v0) = Uv(cx, cy);
        uv.Add((float)u0); uv.Add((float)v0);
        var idx = new List<int>();
        for (var k = 0; k <= segments; k++)
        {
            var ang = 2 * Math.PI * k / segments;
            var (x, y) = (cx + radius * Math.Cos(ang), cy + radius * Math.Sin(ang));
            pos.AddRange([(float)x, (float)y, z]);
            var (u, v) = Uv(x, y);
            uv.AddRange([(float)u, (float)v]);
            if (k > 0) idx.AddRange([0, k, k + 1]);
        }
        var normals = Enumerable.Range(0, pos.Count / 3).SelectMany(_ => new[] { 0f, 0f, 1f }).ToArray();
        var first = Parts.FirstOrDefault();
        return new MeshData { Name = Name, Positions = [.. pos], Normals = normals, Uvs = [.. uv], Parts = [new MeshPart([.. idx], first?.Material, first?.TexturePng)] };
    }
}

public static class DishModels
{
    /// <summary>Every dish a stock recipe makes, once, with its looks (technical recipes left out), in recipe order.</summary>
    public static List<DishModel> Load(GameProvider game, GameData data)
    {
        var list = new List<DishModel>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in data.Recipes.Where(r => !r.Technical && r.Result != null))
        {
            if (!seen.Add(r.Result!)) continue;
            try
            {
                if (Read(game, r.Result!, r.Id) is { } m) list.Add(m);
            }
            catch (Exception)
            {
                // a dish that cannot be read is simply not offered
            }
        }
        return list;
    }

    /// <summary>The looks of a dish class (/Game/.../Dish_X.Dish_X_C) and of the recipe that makes it; null when it has no mesh.</summary>
    public static DishModel? Read(GameProvider game, string dish, string recipe)
    {
        lock (game.Sync)
        {
            var p = game.Provider;
            if (!p.TryLoadPackage(dish.Split('.')[0], out var pkg)) return null;
            var comp = pkg.GetExports().FirstOrDefault(e => e.Name == "ItemMesh0");
            var mesh = Path(comp?.GetOrDefault<FPackageIndex>("StaticMesh"));
            if (mesh == null) return null;
            var mats = comp!.GetOrDefault<FPackageIndex[]>("OverrideMaterials")?.Select(Path).ToList() ?? [];

            string? cookMesh = null, cookMat = null;
            var cookSlot = 0;
            List<string> utilities = [];
            Placement? placement = null;
            if (p.TryLoadPackage($"{GameData.RecipeFolder}/{recipe}", out var rp) && rp.GetExports().FirstOrDefault(e => e.ExportType == "CookingRecipe") is { } re)
            {
                cookMesh = Soft(re.GetOrDefault<FSoftObjectPath>("CookProgressMesh"));
                cookMat = Soft(re.GetOrDefault<FSoftObjectPath>("CookProgressMeshMaterial"));
                cookSlot = re.GetOrDefault("CookProgressMeshMaterialSlot", 0);
                var j = JObject.Parse(JsonConvert.SerializeObject(re))["Properties"];
                utilities = (j?["Utility"] as JArray ?? []).Select(t => ((string?)t ?? "").Split('.').Last()).Where(t => t.Length > 0).ToList();
                if ((bool?)j?["UseMeshRelativeTransform"] == true && j?["CookProgressMeshRelativeTransform"] is JObject xf) placement = ReadTransform(xf);
            }
            return new DishModel(dish, recipe, mesh, mats, cookMesh, cookMat, cookSlot) { Utilities = utilities, CookPlacement = placement };
        }
    }

    private static Placement ReadTransform(JObject xf)
    {
        double D(JToken? t, string k, double d) => (double?)t?[k] ?? d;
        var (t, r, s) = (xf["Translation"], xf["Rotation"], xf["Scale3D"]);
        return new Placement(D(t, "X", 0), D(t, "Y", 0), D(t, "Z", 0), D(r, "X", 0), D(r, "Y", 0), D(r, "Z", 0), D(r, "W", 1), D(s, "X", 1), D(s, "Y", 1), D(s, "Z", 1));
    }

    /// <summary>The game's items for the vessels the editor shows a cookware look in.</summary>
    private static readonly Dictionary<string, string> CookwareItems = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Pot"] = "/Game/ConZ_Files/Items/Equipment/Active_Items/Pot1",
        ["Pan"] = "/Game/ConZ_Files/Items/Equipment/Active_Items/Pan",
    };

    /// <summary>The first vessel of <paramref name="utilities"/> the editor can show (pot, pan), or null.</summary>
    public static string? CookwareFor(IEnumerable<string> utilities) => utilities.FirstOrDefault(CookwareItems.ContainsKey);

    /// <summary>A vessel (Pot, Pan): its item's mesh and its cooking slot (CookingUtilityItemTag); null when it cannot be read.</summary>
    public static Cookware? ReadCookware(GameProvider game, string tag)
    {
        if (!CookwareItems.TryGetValue(tag, out var item)) return null;
        lock (game.Sync)
        {
            if (!game.Provider.TryLoadPackage(item, out var pkg)) return null;
            var exports = pkg.GetExports().ToList();
            var comp = exports.FirstOrDefault(e => e.Name == "ItemMesh0");
            var mesh = Path(comp?.GetOrDefault<FPackageIndex>("StaticMesh"));
            var slot = exports.Where(e => e.ExportType == "CookingUtilityItemTag")
                .Select(e => JObject.Parse(JsonConvert.SerializeObject(e))["Properties"]?["Slots"]?.FirstOrDefault()).FirstOrDefault(x => x != null);
            if (mesh == null || slot == null) return null;
            double D(string a, string b) => (double?)slot[a]?[b] ?? 0;
            var spot = new Placement(D("LocationOffset", "X") + D("CookMeshVisualOffset", "X"), D("LocationOffset", "Y") + D("CookMeshVisualOffset", "Y"),
                D("LocationOffset", "Z") + D("CookMeshVisualOffset", "Z"));
            var mats = comp!.GetOrDefault<FPackageIndex[]>("OverrideMaterials")?.Select(Path).ToList() ?? [];
            return new Cookware(tag, mesh, mats, spot, (double?)slot["CircularShapeRadius"] ?? 10);
        }
    }

    /// <summary>Materials of the cookware look: nothing over the mesh's own but the dish's in its slot.</summary>
    public static IReadOnlyList<string?> CookMaterials(DishModel m)
    {
        var list = new string?[Math.Max(0, m.CookSlot) + 1];
        list[^1] = m.CookMaterial;
        return list;
    }

    /// <summary>
    /// LOD0 of a mesh (/Game/.../SM_X.SM_X) with <paramref name="materials"/> over its own ones (null or missing = its own),
    /// textures as PNG from <paramref name="icons"/>' cache; null when the mesh cannot be read.
    /// </summary>
    public static MeshData? LoadMesh(GameProvider game, IconCache icons, string mesh, IReadOnlyList<string?>? materials = null)
    {
        UStaticMesh? sm;
        List<(int Material, int[] Indices)> sections;
        float[] pos, nrm, uv;
        var slots = new List<string?>();
        lock (game.Sync)
        {
            if (!game.Provider.TryLoadPackageObject<UStaticMesh>(mesh, out sm) || sm.RenderData?.LODs is not { Length: > 0 } lods) return null;
            // the first LOD with geometry (a few dishes ship LOD0 stripped: SkipLod)
            var lod = lods.FirstOrDefault(l => !l.SkipLod && l.PositionVertexBuffer?.Verts.Length > 0);
            if (lod == null) return null;
            if (lod.PositionVertexBuffer is not { } pb || lod.VertexBuffer is not { } vb || lod.IndexBuffer is not { } ib || lod.Sections == null) return null;
            var n = pb.Verts.Length;
            pos = new float[n * 3];
            nrm = new float[n * 3];
            uv = new float[n * 2];
            for (var i = 0; i < n; i++)
            {
                var v = pb.Verts[i];
                (pos[i * 3], pos[i * 3 + 1], pos[i * 3 + 2]) = (v.X, v.Y, v.Z);
                if (i < vb.UV.Length)
                {
                    var item = vb.UV[i];
                    if (item.Normal.Length > 2)
                    {
                        var t = (System.Numerics.Vector3)item.Normal[2]; // TangentZ = the normal
                        (nrm[i * 3], nrm[i * 3 + 1], nrm[i * 3 + 2]) = (t.X, t.Y, t.Z);
                    }
                    if (item.UV.Length > 0) (uv[i * 2], uv[i * 2 + 1]) = (item.UV[0].U, item.UV[0].V);
                }
            }
            sections = lod.Sections.Select(s =>
            {
                var idx = new int[s.NumTriangles * 3];
                for (var k = 0; k < idx.Length; k++) idx[k] = ib[s.FirstIndex + k];
                return (s.MaterialIndex, idx);
            }).ToList();
            foreach (var m in sm.StaticMaterials ?? []) slots.Add(m.MaterialInterface?.GetPathName() is { } mp ? GamePath(mp) : null);
        }

        var parts = new List<MeshPart>();
        foreach (var (mi, idx) in sections)
        {
            var material = materials != null && mi < materials.Count && materials[mi] != null ? materials[mi] : mi < slots.Count ? slots[mi] : null;
            parts.Add(new MeshPart(idx, material, ColourTexture(game, material) is { } tex ? icons.Png(tex, 512) : null));
        }
        return new MeshData { Name = DishModel.ShortName(mesh), Positions = pos, Normals = nrm, Uvs = uv, Parts = parts };
    }

    /// <summary>The colour texture of a material (parameter Diffuse.../BaseColor/Albedo, else a texture named *_D), up its parents.</summary>
    public static string? ColourTexture(GameProvider game, string? material)
    {
        if (material == null) return null;
        lock (game.Sync)
        {
            if (!game.Provider.TryLoadPackageObject<UMaterialInterface>(material, out var m)) return null;
            for (var depth = 0; m != null && depth < 8; depth++)
            {
                if (m is UMaterialInstanceConstant c)
                {
                    var values = c.TextureParameterValues.Select(t => (Name: t.ParameterInfo.Name.Text, Tex: t.ParameterValue.ResolvedObject?.GetPathName() is { } tp ? GamePath(tp) : null)).Where(t => t.Tex != null).ToList();
                    var hit = values.FirstOrDefault(t => t.Name.Contains("Diffuse", StringComparison.OrdinalIgnoreCase) || t.Name.Contains("BaseColor", StringComparison.OrdinalIgnoreCase)
                                                         || t.Name.Contains("Albedo", StringComparison.OrdinalIgnoreCase)).Tex
                              ?? values.FirstOrDefault(t => DishModel.ShortName(t.Tex!).EndsWith("_D", StringComparison.OrdinalIgnoreCase)).Tex;
                    if (hit != null) return hit;
                    m = c.Parent as UMaterialInterface;
                }
                else return null; // a base material: its textures are shader inputs, not the dish's colour
            }
            return null;
        }
    }

    private static string? Path(FPackageIndex? i) => i is { IsNull: false } && i.ResolvedObject is { } o ? GamePath(o.GetPathName()) : null;

    /// <summary>SCUM/Content/A/B.B -> /Game/A/B.B (CUE4Parse names objects of loaded packages by their file path).</summary>
    public static string GamePath(string path) => path.StartsWith("SCUM/Content/", StringComparison.OrdinalIgnoreCase) ? "/Game/" + path["SCUM/Content/".Length..] : path;

    private static string? Soft(FSoftObjectPath p) => string.IsNullOrEmpty(p.AssetPathName.Text) || p.AssetPathName.Text == "None" ? null : p.AssetPathName.Text;
}
