using RecipeEditor.Core.Game;

namespace RecipeEditor.Tests;

[Collection("game")]
public sealed class GameDataTests(GameFixture f)
{
    // Counts are at least those of the game as researched (October 2026: 113 recipes, 35 known); updates add recipes
    // (the Halloween update: Cook_Dish_Soup_CrowStew, 114 and 36).
    [Fact]
    public void ReadsEveryRecipeOfTheGame()
    {
        if (!f.Available) return;
        var d = f.Ru!;
        Assert.True(d.RegistryRecipeCount >= 113, $"{d.RegistryRecipeCount} recipes in the registry");
        Assert.Equal(d.RegistryRecipeCount, d.Recipes.Count);
        Assert.Equal(d.Recipes.Count - 2, d.Recipes.Count(r => !r.Technical));
        Assert.All(d.Recipes.Where(r => !r.Technical), r => Assert.NotEmpty(r.Main));
        Assert.Equal((d.Recipes.Count, d.Books.Count), GameData.Count(f.Game!));
    }

    [Fact]
    public void BooksKnownAndHiddenMatchTheResearch()
    {
        if (!f.Available) return;
        var d = f.Ru!;
        Assert.True(d.Books.Count >= 9, $"{d.Books.Count} books");
        Assert.True(d.Books.Sum(b => b.Recipes.Count) >= 78);
        Assert.True(d.KnownByDefault.Count >= 35, $"{d.KnownByDefault.Count} known");
        Assert.True(d.Recipes.Count(r => r.KnownByDefault && r.Books.Count > 0) >= 7);
        Assert.Equal(
            ["Cook_Dish_EggRolls", "Cook_Dish_Grilled_Drumsticks", "Cook_Dish_Grilled_Potato", "Cook_Dish_Pizza_Calzone", "Cook_Dish_Pizza_Pepperoni"],
            d.Recipes.Where(r => r.Hidden).Select(r => r.Id).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void TextsAreInRussian()
    {
        if (!f.Available) return;
        var d = f.Ru!;
        var pancake = d.Recipe("Cook_Dish_Cake_Pancake")!;
        Assert.Equal("Блинчики", pancake.Name);
        Assert.Equal("Pancakes", pancake.NameSource);
        Assert.Equal(240, pancake.CookingTime);
        Assert.Null(pancake.CookingTemperature);
        Assert.Equal(["Мука", "Яйцо", "Молоко", "Вода", "Пищевая сода"], pancake.Main.Select(i => i.Title));
        Assert.Equal("Сковорода", d.UtilityName(pancake.Utilities[0]));
        Assert.Equal("Книга с рецептами пиццы", d.Book("Book_Cooking_Pizza")!.Name);
        // a name left in English is one the game does not translate yet (Corpse Soup of the Halloween update)
        Assert.All(d.Recipes.Where(r => !r.Technical && r.Name.All(c => c < 128)), r => Assert.Equal(r.NameSource, r.Name));
        Assert.True(d.Recipes.Count(r => !r.Technical && r.Name.All(c => c < 128)) <= 3);
    }

    /// <summary>The Halloween update (2026-10-28): the Cauldron and its Corpse Soup.</summary>
    [Fact]
    public void ReadsTheCauldronAndItsCorpseSoup()
    {
        if (!f.Available || f.Ru!.Recipe("Cook_Dish_Soup_CrowStew") is not { } soup) return;
        Assert.Equal(["Cooking.Utility.Cauldron"], soup.Utilities);
        Assert.Contains("Cooking.Utility.Cauldron", f.Ru.Recipe("Cook_Dish_Soup_Meat")!.Utilities);
        // the game names it in English only, and so does the editor
        Assert.Equal("Cauldron", f.Ru.UtilityName("Cooking.Utility.Cauldron"));
        // its corpses have their _ES as ES_CrowCorpse, ES_SeagullCorpse
        var d = f.Ru;
        Assert.NotNull(d.ItemIcon("/Game/ConZ_Files/Items/Food/BodyParts/Animals/Crow/CrowCorpse.CrowCorpse_C"));
        Assert.NotNull(d.ItemIcon("/Game/ConZ_Files/Items/Food/BodyParts/Animals/Seagull/SeagullCorpse.SeagullCorpse_C"));
        Assert.NotEmpty(d.ItemName("/Game/ConZ_Files/Items/Food/BodyParts/Animals/Crow/CrowCorpse.CrowCorpse_C"));
    }

    [Fact]
    public void WrongKeyIsRefused()
    {
        if (!f.Available) return;
        Assert.ThrowsAny<Exception>(() => GameProvider.Open(f.Game!.Paks, new string('0', 64)));
        Assert.Throws<FormatException>(() => GameProvider.NormalizeKey("0x1234"));
    }
}

[Collection("game")]
public sealed class IconAndCatalogTests(GameFixture f)
{
    [Fact]
    public void IconsComeFromTheGameAsPng()
    {
        if (!f.Available) return;
        var dir = Path.Combine(Path.GetTempPath(), "RecipeEditorTests", Guid.NewGuid().ToString("N"));
        try
        {
            var icons = new RecipeEditor.Core.Icons.IconCache(f.Game!, dir);
            var cucumber = f.Ru!.ItemIcon("/Game/ConZ_Files/Items/Food/Vegetables/Cucumber.Cucumber_C");
            Assert.NotNull(cucumber);
            var png = icons.Png(cucumber);
            Assert.NotNull(png);
            var bytes = File.ReadAllBytes(png!);
            Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, bytes[..4]);
            Assert.Equal(128, System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(16)));
            Assert.Equal(png, icons.Png(cucumber)); // cached
            // every dish of the game has an icon
            var missing = f.Ru.Recipes.Where(r => !r.Technical && f.Ru.ItemIcon(r.Result) == null).Select(r => r.Id).ToList();
            Assert.Empty(missing);
            // every variant of every stock recipe: an item icon, or for a liquid in any container the liquid's ResourceImage
            var noIcon = f.Ru.Recipes.SelectMany(r => r.Main.Concat(r.Optional)).SelectMany(i => i.Options)
                .Where(o => (f.Ru.ItemIcon(o.Item) ?? f.Ru.ItemIcon(o.Resource)) == null)
                .Select(o => o.Item ?? o.Resource).Distinct().ToList();
            Assert.Empty(noIcon);
            Assert.NotNull(icons.Png(f.Ru.ItemIcon("/Game/ConZ_Files/GameResources/DrinkableWater.DrinkableWater_C")));
            Assert.Equal("Вода", f.Ru.ItemName("/Game/ConZ_Files/GameResources/DrinkableWater.DrinkableWater_C"));
            Assert.Equal("Молоко", f.Ru.ItemName("/Game/ConZ_Files/GameResources/Food/Liquids/Milk.Milk_C"));
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void CatalogHasFoodWithCategories()
    {
        if (!f.Available) return;
        var items = f.Ru!.LoadCatalog(f.Game!);
        Assert.True(items.Count > 300, $"{items.Count} items");
        var cucumber = items.First(i => i.ClassName == "Cucumber");
        Assert.Equal("Огурец", cucumber.Name);
        Assert.Equal(ItemCategory.Vegetables, cucumber.Category);
        Assert.Equal(ItemCategory.Dairy, items.First(i => i.ClassName == "Cheese").Category);
        Assert.True(cucumber.Weight > 0);
        Assert.Contains(items, i => i.Category == ItemCategory.Dishes);
    }

    /// <summary>
    /// A new dish or own book gets its icon by renaming the soft reference in its _ES (a clone of the template's), so
    /// every dish a recipe makes and every cookbook must name its icon in its own _ES, not inherit it.
    /// </summary>
    [Fact]
    public void DishesAndBooksNameTheirIconsInTheirOwnEs()
    {
        if (!f.Available) return;
        var game = f.Ru!;
        var items = game.Recipes.Where(r => r.Result != null).Select(r => r.Result!)
            .Concat(game.Books.Select(b => RecipeEditor.Core.Project.ProjectDocument.ClassOf($"{RecipeEditor.Core.Project.ProjectDocument.BookFolder}/{b.Id}")))
            .Distinct().ToList();
        var missing = new List<string>();
        foreach (var item in items)
        {
            var icon = game.ItemIcon(item);
            var es = "SCUM/Content/" + item.Split('.')[0]["/Game/".Length..] + "_ES.uasset";
            var bytes = f.Game!.Provider.Files.TryGetValue(es, out var file) ? file.Read() : null;
            if (icon == null || bytes == null || !System.Text.Encoding.ASCII.GetString(bytes).Contains(icon)) missing.Add(item);
        }
        Assert.Empty(missing);
        var all = GameData.IconTextures(f.Game!);
        Assert.True(all.Count > 1000, $"checked {items.Count}, icons {all.Count}");
        Assert.Contains(game.ItemIcon(items[0]), all);
    }

    /// <summary>Every other item of the game for the "inedible" category: bolts among them, drinks with the liquids.</summary>
    [Fact]
    public void InedibleItemsAreEveryOtherItem()
    {
        if (!f.Available) return;
        var game = GameData.Load(f.Game!, "ru");
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var inedible = game.LoadInedible(f.Game!);
        var seconds = watch.Elapsed.TotalSeconds;
        Assert.True(inedible.Count > 1500, $"{inedible.Count} items");
        Assert.All(inedible, i => Assert.Equal(ItemCategory.Inedible, i.Category));
        var bolts = inedible.Single(i => i.ClassName == "Bolts");
        Assert.Equal("/Game/ConZ_Files/Items/Equipment/Bolts.Bolts_C", bolts.Class);
        Assert.NotNull(bolts.Icon);
        Assert.True(bolts.Uses > 1);
        Assert.Equal(ItemCategory.Liquids, GameData.CategoryOf("/Game/ConZ_Files/Items/Drink/Beer.Beer_C"));
        Assert.DoesNotContain(game.LoadCatalog(f.Game!), i => i.Category == ItemCategory.Inedible && i.Class.Contains("/Food/"));
        Assert.True(seconds < 120, $"{seconds:0} s");
        Console.WriteLine($"inedible: {inedible.Count} items in {seconds:0.0} s");
    }

    /// <summary>A bottle knows its liquid; a liquid without a picture of its own stays without one (isopropyl alcohol).</summary>
    [Fact]
    public void ContainersKnowTheirLiquid()
    {
        if (!f.Available) return;
        var game = GameData.Load(f.Game!, "ru");
        const string liquid = "/Game/ConZ_Files/GameResources/Food/Liquids/IsopropylAlcohol.IsopropylAlcohol_C";
        Assert.Null(game.ItemIcon(liquid)); // the game has no picture for it
        game.LoadCatalog(f.Game!);
        game.LoadInedible(f.Game!);
        var bottle = game.Items["/Game/ConZ_Files/Items/Medications/Isopropyl_alcohol.Isopropyl_alcohol_C"];
        Assert.Equal(liquid, bottle.Holds);
        Assert.Null(game.ItemIcon(liquid));
        Assert.NotNull(bottle.Icon);
        Assert.True(game.Items[liquid].IsLiquid);
    }

}
