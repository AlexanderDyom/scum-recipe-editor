using RecipeEditor.Core.Recipe;
using RecipeEditor.Core.Icons;
using RecipeEditor.Core.Models;

namespace RecipeEditor.Tests;

[Collection("game")]
public sealed class ModelTests(GameFixture f)
{
    /// <summary>Every dish of a stock recipe has a mesh that reads, with triangles and a colour texture.</summary>
    [Fact]
    public void EveryDishModelReads()
    {
        if (!f.Available) return;
        var models = DishModels.Load(f.Game!, f.Ru!);
        Assert.True(models.Count > 90, $"{models.Count} dishes");
        Assert.Equal(models.Count, models.Select(m => m.Dish).Distinct(StringComparer.OrdinalIgnoreCase).Count());

        var icons = new IconCache(f.Game!, Path.Combine(Path.GetTempPath(), "RecipeEditorTests", "icons"));
        var noTexture = new List<string>();
        foreach (var m in models)
        {
            var mesh = DishModels.LoadMesh(f.Game!, icons, m.Mesh, m.Materials);
            Assert.True(mesh != null, $"{m.Dish}: {m.Mesh}");
            Assert.True(mesh!.Triangles > 0, m.Mesh);
            Assert.Equal(mesh.Positions.Length / 3 * 2, mesh.Uvs.Length);
            if (mesh.Parts.All(p => p.TexturePng == null)) noTexture.Add(m.Dish);
        }
        Console.WriteLine($"{models.Count} dish models; without any colour texture: {string.Join(", ", noTexture)}");
        Assert.True(noTexture.Count <= models.Count / 10, string.Join(", ", noTexture));
    }

    /// <summary>A soup in a bowl: the soup is an override material; in the pot the recipe shows a quad with the soup's material.</summary>
    [Fact]
    public void SoupKeepsItsMaterials()
    {
        if (!f.Available) return;
        var soup = DishModels.Read(f.Game!, "/Game/ConZ_Files/Items/Food/CookedFood/Dish_Soup_Chicken.Dish_Soup_Chicken_C", "Cook_Dish_Soup_Chicken")!;
        Assert.Equal("SM_Soup_Bowl_Item", soup.MeshName);
        Assert.Contains(soup.Materials, m => m?.EndsWith("MI_Chicken_Soup_Item.MI_Chicken_Soup_Item") == true);
        Assert.Equal("SM_Quad25cm", DishModel.ShortName(soup.CookMesh!));
        Assert.EndsWith("MI_Chicken_Soup.MI_Chicken_Soup", soup.CookMaterial);
        // a baking tray: the cake's material goes into slot 1, the tray keeps its own (slot 0)
        var cake = DishModels.Read(f.Game!, f.Ru!.Recipe("Cook_Dish_Cake_CreamSlice")!.Result!, "Cook_Dish_Cake_CreamSlice")!;
        Assert.Equal(1, cake.CookSlot);
        Assert.Equal([null, cake.CookMaterial], DishModels.CookMaterials(cake));

        var icons = new IconCache(f.Game!, Path.Combine(Path.GetTempPath(), "RecipeEditorTests", "icons"));
        var mesh = DishModels.LoadMesh(f.Game!, icons, soup.Mesh, soup.Materials)!;
        Assert.Contains(mesh.Parts, p => p.Material == soup.Materials[0] && p.TexturePng != null);
    }
}

[Collection("game")]
public sealed class ModelBuildTests(GameFixture f)
{
    private const string Dishes = "/Game/ConZ_Files/Items/Food/CookedFood/";

    /// <summary>
    /// Models of new dishes: "from scratch" with a soup's model (the dish copies the soup, the recipe the soup's recipe:
    /// its look in the pot), a copied recipe with a pizza's model, and a model changed after the recipe was made (the own
    /// icon stays). The built packages name the chosen meshes and materials.
    /// </summary>
    [Fact]
    public void ChosenModelsBuild()
    {
        if (!f.Available) return;
        var dir = Path.Combine(Path.GetTempPath(), "RecipeEditorTests", Guid.NewGuid().ToString("N"));
        try
        {
            var project = RecipeEditor.Core.Project.RecipeProject.Create(Path.Combine(dir, "ModelTest"), "ModelTest", 955, null);
            var doc = new RecipeEditor.Core.Project.ProjectDocument(project, f.Ru!);
            const string soup = Dishes + "Dish_Soup_Chicken.Dish_Soup_Chicken_C", pizza = Dishes + "Dish_Pizza_Fungi.Dish_Pizza_Fungi_C";
            const string drumstick = Dishes + "Dish_Grilled_Drumsticks.Dish_Grilled_Drumsticks_C";

            var template = doc.ScratchTemplate(soup);
            Assert.Equal("Cook_Dish_Soup_Chicken", template); // it has main and optional slots
            var a = doc.CreateRecipe(template, "Soup Look", "", fromScratch: true, learnFrom: "known", model: soup);
            var b = doc.CreateRecipe("Cook_Dish_Cake_Pancake", "Pizza Look", "", fromScratch: false, learnFrom: "known", model: pizza);
            var pancakeIcon = f.Ru!.ItemIcon(f.Ru.Recipe("Cook_Dish_Cake_Pancake")!.Result)!;
            var c = doc.CreateRecipe(doc.ScratchTemplate(), "Late Look", "", fromScratch: true, learnFrom: "known", icon: pancakeIcon);
            var cDish = doc.State(c)!.Result!;
            doc.SetDishModel(cDish, drumstick);
            Assert.Equal(drumstick, doc.DishModel(cDish));
            Assert.Equal(pancakeIcon, doc.ItemIcon(cDish)); // the own icon stays
            Assert.Equal(f.Ru.ItemIcon(soup), doc.ItemIcon(doc.State(a)!.Result)); // no own icon: the model's
            // the cookware look of recipes already made: ScumModKit copyProperties from the game's recipe of the dish
            const string burger = Dishes + "Dish_Grilled_Burger.Dish_Grilled_Burger_C";
            Assert.Equal(f.Ru.Recipe("Cook_Dish_Cake_Pancake")!.Result, doc.CookLook(b));
            doc.SetCookLook(b, soup);    // pancake (no material) -> quad with the soup's material, slot and transform
            doc.SetCookLook(c, burger);  // tomato salad's quad with a material -> burger mesh without one
            Assert.Equal(soup, doc.CookLook(b));
            Assert.Equal(soup, doc.CookLook(a)); // its template is the soup's recipe: no edit
            doc.SetCookLook(a, soup);
            foreach (var id in new[] { a, c })
            {
                var s = doc.State(id)!;
                s.Main.Add(new Slot { Title = "Egg", TitleSource = "Egg", TitleEdited = true, Options = [new SlotOption { Item = "/Game/ConZ_Files/Items/Food/MeatAndAnimalProducts/Egg.Egg_C", Whole = true, Usage = 1, Liters = 1 }] });
                doc.Set(s);
            }
            doc.Save();
            var saved = RecipeEditor.Core.Project.RecipeProject.Open(project.Folder).Modkit.Edits
                .Where(e => e.TryGetProperty("editor", out var ed) && ed.GetString() == "cook").ToList();
            Assert.Equal(2, saved.Count);
            Assert.Equal(burger, new RecipeEditor.Core.Project.ProjectDocument(RecipeEditor.Core.Project.RecipeProject.Open(project.Folder), f.Ru).CookLook(c));

            var stage = Path.Combine(dir, "stage");
            ScumModKit.Build.ModBuilder.Build(new ScumModKit.Build.BuildOptions { Project = project.ModkitPath, Stage = stage, Paks = f.Game!.Paks, Key = f.Key, NoPack = true });
            Assert.Empty(RecipeEditor.Core.Build.ProjectBuilder.CheckPackages(stage));
            using var provider = new CUE4Parse.FileProvider.DefaultFileProvider(stage, SearchOption.AllDirectories, new CUE4Parse.UE4.Versions.VersionContainer(CUE4Parse.UE4.Versions.EGame.GAME_UE4_27), StringComparer.OrdinalIgnoreCase);
            provider.Initialize();

            (string Mesh, string Materials) Look(string id)
            {
                var name = id.Replace("Cook_Dish_", "Dish_");
                Assert.True(provider.TryLoadPackage($"stage/SCUM/Content/ConZ_Files/Items/Food/CookedFood/{name}", out var pkg), name);
                var comp = pkg.GetExports().First(e => e.Name == "ItemMesh0");
                var mesh = comp.GetOrDefault<CUE4Parse.UE4.Objects.UObject.FPackageIndex>("StaticMesh")!.Name;
                var mats = comp.GetOrDefault<CUE4Parse.UE4.Objects.UObject.FPackageIndex[]>("OverrideMaterials")?.Select(m => m.IsNull ? "-" : m.Name) ?? [];
                return (mesh, string.Join(",", mats));
            }
            Assert.Equal(("SM_Soup_Bowl_Item", "MI_Chicken_Soup_Item"), Look(a));
            Assert.Equal(("SM_Pizza_Slice", "MI_Funghi,MI_Funghi"), Look(b));
            Assert.Equal("SM_Chicken_Drumstick_Item", Look(c).Mesh);

            // "from scratch" with the soup's look in the pot: the recipe is a clone of the soup's
            var built = RecipeEditor.Core.Game.GameData.ReadRecipe(provider, $"stage/SCUM/Content/ConZ_Files/Cooking/Recipes/{a}")!;
            Assert.Equal(["Egg"], built.Main.Select(i => i.TitleSource));
            Assert.Empty(built.Optional);
            var recipe = System.Text.Encoding.ASCII.GetString(File.ReadAllBytes(Path.Combine(stage, "SCUM", "Content", "ConZ_Files", "Cooking", "Recipes", a + ".uasset")));
            Assert.Contains("MI_Chicken_Soup.MI_Chicken_Soup", recipe);

            (string? Mesh, string? Material, bool Transform) Cook(string id)
            {
                Assert.True(provider.TryLoadPackage($"stage/SCUM/Content/ConZ_Files/Cooking/Recipes/{id}", out var pkg), id);
                var r = pkg.GetExports().First(e => e.ExportType == "CookingRecipe");
                string? Soft(string p) => r.GetOrDefault<CUE4Parse.UE4.Objects.UObject.FSoftObjectPath>(p).AssetPathName.Text is { Length: > 0 } t && t != "None" ? t.Split('.').Last() : null;
                return (Soft("CookProgressMesh"), Soft("CookProgressMeshMaterial"), r.Properties.Any(p => p.Name.Text == "CookProgressMeshRelativeTransform"));
            }
            Assert.Equal(("SM_Quad25cm", "MI_Chicken_Soup", false), Cook(b) with { Transform = false });
            Assert.Equal(("SM_Burger_Cooking_Item", null, false), Cook(c));
            Assert.Equal(f.Ru.Recipe("Cook_Dish_Cake_Pancake")!.Main.Count, RecipeEditor.Core.Game.GameData.ReadRecipe(provider, $"stage/SCUM/Content/ConZ_Files/Cooking/Recipes/{b}")!.Main.Count);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }
}

[Collection("game")]
public sealed class ServerVisualsTests(GameFixture f)
{
    /// <summary>SCUM Server names icons (its _ES) but ships no pixels; the client's paks draw the same texture.</summary>
    [Fact]
    public void ServerIconsComeFromTheClient()
    {
        if (!f.Available || ScumModKit.Game.GameLocator.FindInstall(ScumModKit.Game.InstallKind.Server) is not { } serverFolder) return;
        using var server = RecipeEditor.Core.Game.GameProvider.Open(serverFolder, f.Key);
        var data = RecipeEditor.Core.Game.GameData.Load(server, "ru");
        var icon = data.ItemIcon(data.Recipe("Cook_Dish_Salad_Fruit")!.Result)!;
        var root = Path.Combine(Path.GetTempPath(), "RecipeEditorTests", Guid.NewGuid().ToString("N"));
        try
        {
            Assert.Null(new IconCache(server, root).Png(icon));
            Assert.NotNull(new IconCache(f.Game!, root).Png(icon));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }
}
