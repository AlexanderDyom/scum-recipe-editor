using RecipeEditor.Core.Project;
using ScumModKit.Build;

namespace RecipeEditor.Tests;

[Collection("game")]
public sealed class ProjectTests(GameFixture f)
{
    private static string FoodMod => GameFixture.FoodMod!;

    private static string TempDir() => Path.Combine(Path.GetTempPath(), "RecipeEditorTests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void OpensFoodModReadOnly()
    {
        if (GameFixture.FoodMod == null) return; // outside the scum_mod workspace
        var before = File.ReadAllBytes(Path.Combine(FoodMod, "modkit.json"));
        var p = RecipeProject.Open(FoodMod);
        Assert.Equal("FoodMod", p.Name);
        Assert.Equal("pakchunk901-FoodMod_P", p.Pak);
        Assert.Equal(901, p.Number);
        Assert.True(p.ReadOnly);
        Assert.Equal(["Cook_Dish_FoodMod_CrazySalad", "Cook_Dish_FoodMod_CrackerPizza"], p.NewRecipes);
        Assert.Equal(5, p.ChangedRecipes.Count());
        Assert.Equal(["Book_Cooking_Claude"], p.OwnBooks);
        Assert.Equal(before, File.ReadAllBytes(Path.Combine(FoodMod, "modkit.json")));
        Assert.False(File.Exists(Path.Combine(FoodMod, RecipeProject.EditorFile)));
    }

    [Fact]
    public void CreatedProjectIsAModkitProject()
    {
        if (GameFixture.FoodMod == null) return;
        var dir = TempDir();
        try
        {
            var other = RecipeProject.Open(FoodMod);
            var n = RecipeProject.FirstFreeNumber(null, [other]);
            Assert.Equal(902, n);
            var p = RecipeProject.Create(Path.Combine(dir, "Claude_Kitchen"), "Claude_Kitchen", n, "Для Nexus");
            Assert.False(p.ReadOnly);
            Assert.Equal("pakchunk902-Claude_Kitchen_P", p.Pak);
            Assert.Equal("Для Nexus", RecipeProject.Open(p.Folder).Editor.Description);
            var m = ModProject.Load(p.ModkitPath);
            Assert.Equal("Claude_Kitchen", m.Name);
            Assert.Empty(m.Clones);
            Assert.Empty(m.Cooking!.Recipes);
            Assert.Equal(903, RecipeProject.FirstFreeNumber(null, [other, p]));
            Assert.Throws<IOException>(() => RecipeProject.Create(p.Folder, "Claude_Kitchen", n, null));
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }

    [Theory]
    [InlineData("Food_Mod", true)]
    [InlineData("Claude2", true)]
    [InlineData("Кухня", false)]
    [InlineData("a b", false)]
    [InlineData("a-b", false)]
    [InlineData("", false)]
    public void ProjectNames(string name, bool ok) => Assert.Equal(ok, RecipeProject.IsValidName(name));

    [Fact]
    public void FreeNumberSkipsModsAndProjects()
    {
        var dir = TempDir();
        try
        {
            Directory.CreateDirectory(Path.Combine(dir, "~mods"));
            foreach (var pak in new[] { "pakchunk901-A_P.pak", "pakchunk902-B_P.pak", "pakchunk999-ModsRegistry_3_P.pak" })
                File.WriteAllBytes(Path.Combine(dir, "~mods", pak), []);
            Assert.Equal(903, RecipeProject.FirstFreeNumber(dir, []));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void CatalogGroupsFoodMod()
    {
        if (!f.Available || GameFixture.FoodMod == null) return;
        var c = RecipeCatalog.Build(f.Ru!, RecipeProject.Open(FoodMod));
        Assert.Equal(113, c.Entries.Count);
        Assert.Equal(["Безумный салат", "Пицца на крекерах"], c.Entries.Where(e => e.IsNew).Select(e => e.Name).Order());
        Assert.Equal(5, c.Entries.Count(e => e.IsChanged));
        Assert.Equal(5, c.Entries.Count(e => e.Group == RecipeGroupKind.Hidden));
        Assert.Equal(28, c.Entries.Count(e => e.Group == RecipeGroupKind.Known));
        Assert.Equal(RecipeGroupKind.New, c.Groups[0].Kind);
        Assert.DoesNotContain(c.Groups, g => g.Book == "Book_Cooking_Claude"); // its recipes are all new: listed under "new"
        Assert.Contains("Книга Клода", c.Entries.First(e => e.Id == "Cook_Dish_FoodMod_CrazySalad").BookNames);

        var game = RecipeCatalog.Build(f.Ru!, null);
        Assert.Equal(111, game.Entries.Count);
        Assert.Contains(game.Entries, e => e.Id == "Cook_Dish_Cake_Pancake" && e.Matches("блин"));
        Assert.Contains(game.Entries.Where(e => e.Matches("огурец")), e => e.Id == "Cook_Dish_Salad_Cucumber");
        Assert.DoesNotContain(game.Entries, e => e.Id is "Cook_Empty" or "Cook_Pizza");
    }
}
