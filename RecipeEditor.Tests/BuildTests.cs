using RecipeEditor.Core.Build;
using RecipeEditor.Core.Project;
using RecipeEditor.Core.Recipe;

namespace RecipeEditor.Tests;

/// <summary>Builds into a folder only: installing into ~mods (and the shared registry) is checked in the program, not here.</summary>
[Collection("game")]
public sealed class BuildTests(GameFixture f)
{
    [Fact]
    public void ProjectBuildsIntoAFolderWithTheKeyAsText()
    {
        if (!f.Available) return;
        var dir = Path.Combine(Path.GetTempPath(), "RecipeEditorTests", Guid.NewGuid().ToString("N"));
        try
        {
            var project = RecipeProject.Create(Path.Combine(dir, "FolderBuild"), "FolderBuild", 952, null);
            var doc = new ProjectDocument(project, f.Ru!);
            var s = doc.State("Cook_Dish_Cake_Pancake")!;
            s.CookingTime = 30;
            s.Main.First(x => x.TitleSource == "Milk").Removed = true;
            // an optional slot of the mod: its item is in the spawn commands too
            var cucumber = f.Ru!.Items.Keys.First(k => k.EndsWith("/Cucumber.Cucumber_C", StringComparison.OrdinalIgnoreCase));
            s.Optional.Add(new Slot { Title = "Cucumber", TitleSource = "Cucumber", TitleEdited = true, Options = [new SlotOption { Item = cucumber, Whole = true, Usage = 1, Liters = 1 }] });
            // a liquid of the mod: the bottle that holds it goes into the spawn commands
            s.Optional.Add(new Slot { Title = "Alcohol", TitleSource = "Alcohol", TitleEdited = true, IsResource = true,
                Options = [new SlotOption { Resource = "/Game/ConZ_Files/GameResources/Food/Liquids/IsopropylAlcohol.IsopropylAlcohol_C", Whole = true, Usage = 1, Liters = 0.25 }] });
            doc.Set(s);
            doc.Save();

            var steps = new List<BuildStep>();
            var report = ProjectBuilder.Run(RecipeProject.Open(project.Folder), new BuildRequest
            {
                Paks = f.Game!.Paks, AesKey = f.Key,
                Install = false, OutputFolder = Path.Combine(dir, "release"),
            }, new SyncProgress<BuildStep>(steps.Add));

            Assert.True(report.Ok, string.Join("\n", steps.Select(x => $"{x.Key} {x.State} {x.Text} {x.Detail}")));
            Assert.Equal(["clones", "clones", "edits", "check", "check", "copy"], steps.Select(x => x.Key));
            Assert.Equal("0", steps.Last(x => x.Key == "check").Result); // no problems
            Assert.True(File.Exists(Path.Combine(dir, "release", "pakchunk952-FolderBuild_P.pak")));
            Assert.True(File.Exists(Path.Combine(dir, "release", "pakchunk952-FolderBuild_P.sig")));
            Assert.True(report.PakSize > 1000);

            f.Ru!.LoadCatalog(f.Game);
            f.Ru.LoadInedible(f.Game);
            var spawn = ProjectBuilder.SpawnCommands(new ProjectDocument(RecipeProject.Open(project.Folder), f.Ru!));
            Assert.Contains("#SpawnItem Isopropyl_alcohol 1", spawn);
            Assert.Contains("#SpawnItem Book_Cooking_Cake 1", spawn);
            Assert.Contains("#SpawnItem WhiteFlour 1", spawn);
            Assert.Contains("#SpawnItem Cucumber 1", spawn);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }
}

[Collection("game")]
public sealed class BookTests(GameFixture f)
{
    [Fact]
    public void BooksAndKnownListsSaveAndBuild()
    {
        if (!f.Available) return;
        var dir = Path.Combine(Path.GetTempPath(), "RecipeEditorTests", Guid.NewGuid().ToString("N"));
        try
        {
            var project = RecipeProject.Create(Path.Combine(dir, "BookTest"), "BookTest", 953, null);
            var doc = new ProjectDocument(project, f.Ru!);
            var id = doc.CreateOwnBook("Книга теста", "Описание", "Book_Cooking_Pizza", ["Cook_Dish_Cake_Pancake"]);
            Assert.Equal("Book_Cooking_BookTest", id);
            doc.AddToBook(id, "Cook_Dish_Grilled_Potato");
            doc.AddToBook("Book_Cooking_Cake", "Cook_Dish_Pizza_Calzone");
            doc.AddToBook("Book_Cooking_Cake", "Cook_Dish_Cake_Pancake"); // already in the stock book: nothing to add
            doc.AddKnown("Cook_Dish_EggRolls");
            doc.AddKnown("Cook_Drink_Tea"); // already known: nothing to add
            Assert.True(doc.IsDirty);
            doc.Save();
            Assert.False(doc.IsDirty);

            var m = RecipeProject.Open(project.Folder).Modkit;
            Assert.Equal(["Cook_Dish_Cake_Pancake", "Cook_Dish_Grilled_Potato"], m.Cooking!.OwnBooks[id]);
            Assert.Equal(["Cook_Dish_Pizza_Calzone"], m.Cooking.Books["Book_Cooking_Cake"]);
            Assert.Equal(["Cook_Dish_EggRolls"], m.Cooking.KnownByDefault);
            Assert.Equal(["Книга теста", "Описание"], m.Cooking.ItemTexts[$"{ProjectDocument.BookFolder}/{id}_ES"]);
            Assert.Single(m.Clones);
            Assert.Equal("Book_Cooking_Pizza", new ProjectDocument(RecipeProject.Open(project.Folder), f.Ru!).OwnBookTemplate(id));

            // the catalog now shows the hidden ones as learnable
            var c = RecipeCatalog.Build(f.Ru!, RecipeProject.Open(project.Folder));
            Assert.Equal(2, c.Entries.Count(e => e.Group == RecipeGroupKind.Hidden));

            var stage = Path.Combine(dir, "stage");
            ScumModKit.Build.ModBuilder.Build(new ScumModKit.Build.BuildOptions
            {
                Project = project.ModkitPath, Stage = stage, Paks = f.Game!.Paks, Key = f.Key, NoPack = true,
            });
            var books = Path.Combine(stage, "SCUM", "Content", "ConZ_Files", "Items", "Books", "Cookbooks");
            Assert.True(File.Exists(Path.Combine(books, id + ".uasset")));
            Assert.True(File.Exists(Path.Combine(books, id + "_ES.uasset")));
            Assert.True(File.Exists(Path.Combine(books, "Book_Cooking_Cake.uasset")));
            Assert.True(File.Exists(Path.Combine(stage, "SCUM", "Content", "ConZ_Files", "Skills", "CookingSkill.uasset")));
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }
}

/// <summary>IProgress that reports on the calling thread (Progress&lt;T&gt; posts to the thread pool).</summary>
internal sealed class SyncProgress<T>(Action<T> report) : IProgress<T>
{
    public void Report(T value) => report(value);
}

public sealed class MergerOutputTests
{
    [Fact]
    public void ReadsHowEveryModStands()
    {
        const string output = """
            Paks: F:\Games\Steam\steamapps\common\SCUM\SCUM\Content\Paks
            game registry: readable
              pakchunk900-ChestMod_P.pak: 408 new records, Included
              pakchunk901-FoodMod_P.pak: 14 new records, Missing, new
              pakchunk902-Editor_Test_P.pak: no registry, NotNeeded
              ZZ_Yani_Expedition_Backpacks_1.3.2_P.pak: 13 new records, Included
              pakchunk900-ChestMod_P.pak: 408 added
            merged: 127377 records (base: game registry)
            """;
        var mods = RecipeEditor.Core.Build.ProjectBuilder.ParseMods(output);
        Assert.Equal(
        [
            ("pakchunk900-ChestMod_P.pak", "Included"), ("pakchunk901-FoodMod_P.pak", "Missing"),
            ("pakchunk902-Editor_Test_P.pak", "NotNeeded"), ("ZZ_Yani_Expedition_Backpacks_1.3.2_P.pak", "Included"),
        ], mods);
        Assert.Equal("FoodMod", RecipeEditor.Core.Build.ProjectBuilder.DisplayName("pakchunk901-FoodMod_P.pak"));
        Assert.Equal("ModsRegistry", RecipeEditor.Core.Build.ProjectBuilder.DisplayName("pakchunk999-ModsRegistry_999_P.pak"));
    }

    /// <summary>ScumRegistryMerger with game references marks mods built for another game version.</summary>
    [Fact]
    public void FindsModsOfAnotherGameVersion()
    {
        const string output = """
            game registry: readable
            game version 1a2b: mods checked against the game's own registry
              pakchunk900-ChestMod_P.pak: 408 new records, Included
              pakchunk905-OldMod_P.pak: 20 new records, Missing, BUILT FOR ANOTHER GAME VERSION (12 records of another version, 3 game records missing)
            """;
        var mods = RecipeEditor.Core.Build.ProjectBuilder.ParseMods(output);
        Assert.Equal([("pakchunk900-ChestMod_P.pak", "Included"), ("pakchunk905-OldMod_P.pak", "Missing")], mods);
        Assert.Equal(["pakchunk905-OldMod_P.pak"], RecipeEditor.Core.Build.ProjectBuilder.OtherVersionMods(output));
    }
}

[Collection("game")]
public sealed class NewRecipeTests(GameFixture f)
{
    [Fact]
    public void NewRecipesFromScratchAndCopiesBuild()
    {
        if (!f.Available) return;
        var dir = Path.Combine(Path.GetTempPath(), "RecipeEditorTests", Guid.NewGuid().ToString("N"));
        try
        {
            var project = RecipeProject.Create(Path.Combine(dir, "NewTest"), "NewTest", 954, null);
            var doc = new ProjectDocument(project, f.Ru!);
            Assert.False(ProjectDocument.IsEnglishName("Безумный салат"));
            Assert.Equal("CrazySalad2", ProjectDocument.NameKey("crazy salad 2"));

            // from scratch: the template's slots go, one own slot comes
            // with the pancake's icon; the own book gets the pizza's
            var pancakeIcon = f.Ru!.ItemIcon(f.Ru.Recipe("Cook_Dish_Cake_Pancake")!.Result)!;
            var saladIcon = f.Ru.ItemIcon(f.Ru.Recipe(doc.ScratchTemplate())!.Result)!;
            var scratch = doc.CreateRecipe(doc.ScratchTemplate(), "Crazy Salad", "Eggs and crackers.", fromScratch: true, learnFrom: "Book_Cooking_Pizza", icon: pancakeIcon);
            var dish = "/Game/ConZ_Files/Items/Food/CookedFood/Dish_NewTest_CrazySalad.Dish_NewTest_CrazySalad_C";
            Assert.Equal(pancakeIcon, doc.ItemIcon(dish));
            Assert.True(doc.HasOwnIcon(dish));
            var book = doc.CreateOwnBook("Test Cookbook", "", "Book_Cooking_BBQ", ["Cook_Dish_Salad_Tomato"]);
            var bookClass = ProjectDocument.ClassOf($"{ProjectDocument.BookFolder}/{book}");
            var pizzaIcon = f.Ru.ItemIcon(ProjectDocument.ClassOf($"{ProjectDocument.BookFolder}/Book_Cooking_Pizza"))!;
            Assert.Equal(f.Ru.ItemIcon(ProjectDocument.ClassOf($"{ProjectDocument.BookFolder}/Book_Cooking_BBQ")), doc.ItemIcon(bookClass));
            doc.SetIcon(bookClass, pizzaIcon);
            Assert.Equal("Cook_Dish_NewTest_CrazySalad", scratch);
            var s = doc.State(scratch)!;
            Assert.Empty(s.Live(Groups.Main));
            s.Main.Add(new Slot { Title = "Egg", TitleSource = "Egg", TitleEdited = true, Options = [new SlotOption { Item = "/Game/ConZ_Files/Items/Food/MeatAndAnimalProducts/Egg.Egg_C", Whole = true, Usage = 1, Liters = 1 }] });
            doc.Set(s);

            // a copy of a game recipe, known from the start
            var copy = doc.CreateRecipe("Cook_Dish_Cake_Pancake", "Big Pancakes", "", fromScratch: false, learnFrom: "known");
            Assert.Equal("Cook_Dish_NewTest_BigPancakes", copy);
            doc.Save();

            var m = RecipeProject.Open(project.Folder).Modkit;
            Assert.Equal([scratch, copy], m.Cooking!.NewRecipes);
            Assert.Equal(5, m.Clones.Count);
            Assert.Equal(2, m.Edits.Count(e => e.TryGetProperty("editor", out var ed) && ed.GetString() == "icon"));
            Assert.Contains(scratch, m.Cooking.Books["Book_Cooking_Pizza"]);
            Assert.Contains(copy, m.Cooking.KnownByDefault);
            var reopened = new ProjectDocument(RecipeProject.Open(project.Folder), f.Ru!);
            Assert.True(reopened.State(scratch)!.SameAs(s));
            Assert.Equal("Crazy Salad", reopened.State(scratch)!.Name);
            Assert.Equal(pancakeIcon, reopened.ItemIcon(dish));
            Assert.Equal(pizzaIcon, reopened.ItemIcon(bookClass));
            var catalog = RecipeCatalog.Build(f.Ru!, RecipeProject.Open(project.Folder));
            Assert.Equal(["Big Pancakes", "Crazy Salad"], catalog.Entries.Where(e => e.IsNew).Select(e => e.Name).Order());

            var stage = Path.Combine(dir, "stage");
            ScumModKit.Build.ModBuilder.Build(new ScumModKit.Build.BuildOptions
            {
                Project = project.ModkitPath, Stage = stage, Paks = f.Game!.Paks, Key = f.Key, NoPack = true,
            });
            Assert.Empty(RecipeEditor.Core.Build.ProjectBuilder.CheckPackages(stage));
            using var provider = new CUE4Parse.FileProvider.DefaultFileProvider(stage, SearchOption.AllDirectories, new CUE4Parse.UE4.Versions.VersionContainer(CUE4Parse.UE4.Versions.EGame.GAME_UE4_27), StringComparer.OrdinalIgnoreCase);
            provider.Initialize();
            var built = RecipeEditor.Core.Game.GameData.ReadRecipe(provider, $"stage/SCUM/Content/ConZ_Files/Cooking/Recipes/{scratch}")!;
            Assert.Equal(["Egg"], built.Main.Select(i => i.TitleSource));
            Assert.Empty(built.Optional);
            Assert.EndsWith("Dish_NewTest_CrazySalad.Dish_NewTest_CrazySalad_C", built.Result);
            // the clones' _ES name the chosen icons (soft references: package and object path in the name map)
            var es = System.Text.Encoding.ASCII.GetString(File.ReadAllBytes(Path.Combine(stage, "SCUM", "Content", "ConZ_Files", "Items", "Food", "CookedFood", "Dish_NewTest_CrazySalad_ES.uasset")));
            Assert.Contains(pancakeIcon, es);
            Assert.DoesNotContain(saladIcon.Split('.')[0], es);
            var bookEs = System.Text.Encoding.ASCII.GetString(File.ReadAllBytes(Path.Combine(stage, "SCUM", "Content", "ConZ_Files", "Items", "Books", "Cookbooks", book + "_ES.uasset")));
            Assert.Contains(pizzaIcon, bookEs);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }
}

[Collection("game")]
public sealed class ServerModTests(GameFixture f)
{
    /// <summary>Only cooking time / temperature: the server alone is enough; an ingredient slot needs the players too.</summary>
    [Fact]
    public void TellsWhenTheServerAloneIsEnough()
    {
        if (!f.Available) return;
        var dir = Path.Combine(Path.GetTempPath(), "RecipeEditorTests", Guid.NewGuid().ToString("N"));
        try
        {
            var project = RecipeProject.Create(Path.Combine(dir, "ServerMod"), "ServerMod", 953, null);
            Assert.True(ProjectBuilder.ServerAlone(RecipeProject.Open(project.Folder)));
            var doc = new ProjectDocument(project, f.Ru!);
            var s = doc.State("Cook_Dish_Salad_Fruit")!;
            s.CookingTime = 300;
            s.CookingTemperature = 30;
            doc.Set(s);
            doc.Save();
            Assert.True(ProjectBuilder.ServerAlone(RecipeProject.Open(project.Folder)));
            s = doc.State("Cook_Dish_Salad_Fruit")!;
            s.Optional.Add(new Slot { Title = "Egg", TitleSource = "Egg", TitleEdited = true, Options = [new SlotOption { Item = "/Game/ConZ_Files/Items/Food/MeatAndAnimalProducts/Egg.Egg_C", Whole = true, Usage = 1, Liters = 1 }] });
            doc.Set(s);
            doc.Save();
            Assert.False(ProjectBuilder.ServerAlone(RecipeProject.Open(project.Folder)));
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }
}
