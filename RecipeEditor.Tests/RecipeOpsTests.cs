using System.Text.Json;
using System.Text.Json.Nodes;
using CUE4Parse.FileProvider;
using CUE4Parse.UE4.Versions;
using RecipeEditor.Core.Game;
using RecipeEditor.Core.Project;
using RecipeEditor.Core.Recipe;
using ScumModKit.Build;

namespace RecipeEditor.Tests;

[Collection("game")]
public sealed class RecipeOpsTests(GameFixture f)
{
    private static string FoodMod => GameFixture.FoodMod!;

    private static RecipeState Replay(RecipeInfo game, string id, IEnumerable<JsonNode> ops)
    {
        var s = RecipeState.FromGame(game);
        foreach (var op in ops)
        {
            using var d = JsonDocument.Parse(op.ToJsonString());
            s.Apply(d.RootElement);
        }
        return s;
    }

    [Fact]
    public void FoodModRecipesRegenerateToEquivalentOps()
    {
        if (!f.Available || GameFixture.FoodMod == null) return;
        var doc = new ProjectDocument(RecipeProject.Open(FoodMod), f.Ru!);
        foreach (var id in doc.Project.Modkit.Cooking!.Recipes.Keys)
        {
            var state = doc.State(id)!;
            var ops = doc.Ops(id);
            var again = Replay(doc.Base(id)!, id, ops);
            Assert.True(again.SameAs(Rebase(state, doc.Base(id)!.Id)), $"{id}: {string.Join(" ", ops.Select(o => o.ToJsonString()))}");
        }
    }

    private static RecipeState Rebase(RecipeState s, string id)
    {
        var c = s.Clone();
        return id == s.Id ? c : CloneWithId(c, id);
    }

    private static RecipeState CloneWithId(RecipeState s, string id)
    {
        var c = new RecipeState
        {
            Id = id, CookingTime = s.CookingTime, CookingTemperature = s.CookingTemperature, Utilities = [.. s.Utilities],
            Name = s.Name, Description = s.Description, Result = s.Result,
        };
        foreach (var g in Groups.All) c.Slots[g].AddRange(s.Slots[g].Select(x => x.Clone()));
        return c;
    }

    [Fact]
    public void PancakesOfFoodModAsTheEditorWritesThem()
    {
        if (!f.Available || GameFixture.FoodMod == null) return;
        var doc = new ProjectDocument(RecipeProject.Open(FoodMod), f.Ru!);
        var s = doc.State("Cook_Dish_Cake_Pancake")!;
        Assert.Equal(20, s.CookingTime);
        Assert.Equal(["Egg", "Огурец", "Крекеры"], s.Live(Groups.Main).Select(x => x.TitleSource));
        Assert.Equal(4, s.Main.Count(x => x.Removed));
        // the same edits done in the editor from the game's recipe
        var e = RecipeState.FromGame(f.Ru!.Recipe("Cook_Dish_Cake_Pancake")!);
        foreach (var t in new[] { "Flour", "Milk", "Water", "Baking Soda" }) e.Main.First(x => x.TitleSource == t).Removed = true;
        foreach (var (title, item) in new[] { ("Огурец", "/Game/ConZ_Files/Items/Food/Vegetables/Cucumber.Cucumber_C"), ("Крекеры", "/Game/ConZ_Files/Items/Food/Dry_Food/Crackers.Crackers_C") })
            e.Main.Add(new Slot { Title = title, TitleSource = title, TitleEdited = true, Options = [new SlotOption { Item = item, Whole = true, Usage = 1, Liters = 1 }] });
        e.CookingTime = 20;
        var relaxed = new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping, TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver() };
        var ops = RecipeOps.Generate(f.Ru.Recipe("Cook_Dish_Cake_Pancake")!, e).Select(o => o.ToJsonString(relaxed)).ToList();
        Assert.Equal(
        [
            """["set","CookingTime",20]""",
            """["add_ingredient","main","Огурец",[["/Game/ConZ_Files/Items/Food/Vegetables/Cucumber.Cucumber_C",true,1]]]""",
            """["add_ingredient","main","Крекеры",[["/Game/ConZ_Files/Items/Food/Dry_Food/Crackers.Crackers_C",true,1]]]""",
            """["remove","main","Baking Soda"]""", """["remove","main","Water"]""", """["remove","main","Milk"]""", """["remove","main","Flour"]""",
        ], ops);
        Assert.True(Replay(f.Ru.Recipe("Cook_Dish_Cake_Pancake")!, "x", RecipeOps.Generate(f.Ru.Recipe("Cook_Dish_Cake_Pancake")!, e)).SameAs(s));
    }

    [Fact]
    public void DuplicateTitlesUseIndexes()
    {
        if (!f.Available) return;
        var game = f.Ru!.Recipe("Cook_Dish_Cake_ApplePie")!; // two "Apple" slots, two "Walnut" ones
        var e = RecipeState.FromGame(game);
        var apples = e.Main.Where(x => x.TitleSource == e.Main[0].TitleSource).ToList();
        Assert.Equal(2, apples.Count);
        apples[1].Removed = true;
        apples[0].Options[0].Usage = 2;
        var ops = RecipeOps.Generate(game, e);
        Assert.All(ops, o => Assert.Equal(JsonValueKind.Number, JsonDocument.Parse(o.ToJsonString()).RootElement[2].ValueKind));
    }

    private const string Liquids = "/Game/ConZ_Files/GameResources/Food/Liquids";

    /// <summary>Liquids and per-variant amounts (modkit 0b70a74): litres, liquid variants, a liquid slot, items replaced by a liquid.</summary>
    [Fact]
    public void LiquidEditsBuildWithModkit()
    {
        if (!f.Available) return;
        var dir = Path.Combine(Path.GetTempPath(), "RecipeEditorTests", Guid.NewGuid().ToString("N"));
        try
        {
            var project = RecipeProject.Create(Path.Combine(dir, "LiquidTest"), "LiquidTest", 951, null);
            var doc = new ProjectDocument(project, f.Ru!);
            var id = "Cook_Dish_Cake_Pancake";
            var s = doc.State(id)!;
            s.Main.First(x => x.TitleSource == "Water").Options[0].Liters = 0.5;
            var milk = s.Main.First(x => x.TitleSource == "Milk");
            milk.Options.Add(new SlotOption { Item = null, Resource = $"{Liquids}/Wine.Wine_C", Whole = true, Usage = 1, Liters = 0.2 });
            var egg = s.Main.First(x => x.TitleSource == "Egg");
            egg.Options.Add(new SlotOption { Item = "/Game/ConZ_Files/Items/Food/MeatAndAnimalProducts/Cheese.Cheese_C", Whole = true, Usage = 2, Liters = 1 });
            var soda = s.Main.First(x => x.TitleSource == "Baking Soda");
            soda.Options = [new SlotOption { Resource = $"{Liquids}/Oil.Oil_C", Whole = true, Usage = 1, Liters = 0.1 }];
            soda.IsResource = true;
            s.Optional.Add(new Slot
            {
                Title = "Соевый соус", TitleSource = "Соевый соус", TitleEdited = true, IsResource = true,
                Options = [new SlotOption { Resource = $"{Liquids}/SoySauce.SoySauce_C", Whole = true, Usage = 1, Liters = 0.05 }],
            });
            doc.Set(s);
            doc.Save();

            var reopened = new ProjectDocument(RecipeProject.Open(project.Folder), f.Ru!);
            Assert.True(reopened.State(id)!.SameAs(s), string.Join(" ", reopened.Ops(id).Select(o => o.ToJsonString())));

            var stage = Path.Combine(dir, "stage");
            ModBuilder.Build(new BuildOptions
            {
                Project = project.ModkitPath, Stage = stage, Paks = f.Game!.Paks, KeyFile = GameFixture.KeyFile, NoPack = true,
            });
            using var provider = new DefaultFileProvider(stage, SearchOption.AllDirectories, new VersionContainer(EGame.GAME_UE4_27), StringComparer.OrdinalIgnoreCase);
            provider.Initialize();
            var b = RecipeState.FromGame(GameData.ReadRecipe(provider, $"stage/SCUM/Content/ConZ_Files/Cooking/Recipes/{id}")!);
            foreach (var g in Groups.All)
            {
                var want = s.Live(g).ToList();
                Assert.Equal(want.Count, b.Slots[g].Count);
                for (var i = 0; i < want.Count; i++)
                {
                    Assert.Equal(want[i].IsResource, b.Slots[g][i].IsResource);
                    Assert.Equal(want[i].Options.Count, b.Slots[g][i].Options.Count);
                    for (var j = 0; j < want[i].Options.Count; j++)
                    {
                        var (x, y) = (want[i].Options[j], b.Slots[g][i].Options[j]);
                        Assert.True(x.Item == y.Item && x.Resource == y.Resource && x.Whole == y.Whole && x.Usage == y.Usage
                                    && (!x.IsResource || Math.Abs(x.Liters - y.Liters) < 1e-6), $"{want[i].Title}[{j}]: {x} vs {y}");
                    }
                }
            }
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void WaterGetsMoreLitresAndMilk()
    {
        if (!f.Available) return;
        var game = f.Ru!.Recipe("Cook_Dish_Cake_Pancake")!;
        var e = RecipeState.FromGame(game);
        var water = e.Main.First(x => x.TitleSource == "Water");
        water.Options[0].Liters = 0.5;
        water.Options.Add(new SlotOption { Resource = $"{Liquids}/Milk.Milk_C", Whole = true, Usage = 1, Liters = 0.5 });
        var ops = RecipeOps.Generate(game, e).Select(o => o.ToJsonString()).ToList();
        Assert.Equal(["""["add_option","main","Water","",true,1,0.5,"/Game/ConZ_Files/GameResources/Food/Liquids/Milk.Milk_C"]""", """["liters","main","Water",0.5]"""], ops);
    }

    /// <summary>Every kind of edit the editor makes, saved by the editor and built by modkit; the built package reads back the same.</summary>
    [Fact]
    public void EditedProjectBuildsWithModkit()
    {
        if (!f.Available) return;
        var dir = Path.Combine(Path.GetTempPath(), "RecipeEditorTests", Guid.NewGuid().ToString("N"));
        try
        {
            var project = RecipeProject.Create(Path.Combine(dir, "BuildTest"), "BuildTest", 950, null);
            var doc = new ProjectDocument(project, f.Ru!);
            var id = "Cook_Dish_Cake_Pancake";
            var s = doc.State(id)!;
            s.CookingTime = 90;
            s.CookingTemperature = 180;
            s.Utilities = ["Cooking.Utility.Pan", "Cooking.Utility.Oven"];
            var flour = s.Main.First(x => x.TitleSource == "Flour");
            flour.Title = flour.TitleSource = "Мука или крекеры";
            flour.TitleEdited = true;
            flour.Options.Add(new SlotOption { Item = "/Game/ConZ_Files/Items/Food/Dry_Food/Crackers.Crackers_C", Whole = true, Usage = 1, Liters = 1 });
            flour.Options[0].Nutrient = 0.5;
            s.Main.First(x => x.TitleSource == "Water").Removed = true;
            var egg = s.Main.First(x => x.TitleSource == "Egg");
            egg.Removed = true; // moved to the optional ingredients
            s.Optional.Add(new Slot { Title = "Яйцо", TitleSource = "Яйцо", TitleEdited = true, Options = egg.Options.Select(o => o.Clone()).ToList() });
            s.Optional.Add(new Slot
            {
                Title = "Огурец", TitleSource = "Огурец", TitleEdited = true,
                Options = [new SlotOption { Item = "/Game/ConZ_Files/Items/Food/Vegetables/Cucumber.Cucumber_C", Whole = false, Usage = 2, Liters = 1, Nutrient = 0.75 }],
            });
            doc.Set(s);
            Assert.True(doc.IsDirty);
            doc.Save();
            Assert.False(doc.IsDirty);

            // round trip: the saved modkit.json gives the same recipe, other sections are still there
            var reopened = new ProjectDocument(RecipeProject.Open(project.Folder), f.Ru!);
            Assert.True(reopened.State(id)!.SameAs(s));
            var json = JsonNode.Parse(File.ReadAllText(project.ModkitPath))!;
            Assert.NotNull(json["clones"]);
            Assert.NotNull(json["edits"]);
            Assert.Equal("BuildTest", (string?)json["name"]);

            // modkit builds it (stage only) and the built recipe is what the editor shows
            var stage = Path.Combine(dir, "stage");
            ModBuilder.Build(new BuildOptions
            {
                Project = project.ModkitPath, Stage = stage, Paks = f.Game!.Paks, KeyFile = GameFixture.KeyFile, NoPack = true,
            });
            using var provider = new DefaultFileProvider(stage, SearchOption.AllDirectories, new VersionContainer(EGame.GAME_UE4_27), StringComparer.OrdinalIgnoreCase);
            provider.Initialize();
            var built = GameData.ReadRecipe(provider, $"stage/SCUM/Content/ConZ_Files/Cooking/Recipes/{id}"); // loose files: keyed by the folder name
            Assert.True(built != null, string.Join(" | ", provider.Files.Keys.Take(10)) + " / " + string.Join(",", Directory.EnumerateFiles(stage, "*", SearchOption.AllDirectories).Take(5)));
            var b = RecipeState.FromGame(built!);
            Assert.Equal(90, b.CookingTime);
            Assert.Equal(180, b.CookingTemperature);
            Assert.Equal(["Cooking.Utility.Pan", "Cooking.Utility.Oven"], b.Utilities);
            Assert.Equal(s.Live(Groups.Main).Select(x => x.ToString()), b.Main.Select(x => x.ToString()));
            Assert.Equal(s.Live(Groups.Optional).Select(x => x.ToString()), b.Optional.Select(x => x.ToString()));
            Assert.Equal(s.Live(Groups.Main).SelectMany(x => x.Options).Select(o => o.Nutrient), b.Main.SelectMany(x => x.Options).Select(o => o.Nutrient));
            Assert.Equal(s.Live(Groups.Optional).SelectMany(x => x.Options).Select(o => o.Nutrient), b.Optional.SelectMany(x => x.Options).Select(o => o.Nutrient));
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }
}
