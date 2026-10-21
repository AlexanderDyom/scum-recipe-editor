// What the editor shows about the stock game: cooking recipes, cookbooks, the recipes known from the start
// (CookingSkill._recipesKnownByDefault) and cooking utilities, with texts in the game's localization (ru/en...).
// How a recipe becomes available to a player: Food_Mod/Food_Mod.md, "Как рецепт становится доступен".
using CUE4Parse.UE4.AssetRegistry;
using CUE4Parse.UE4.Localization;
using CUE4Parse.UE4.Readers;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace RecipeEditor.Core.Game;

/// <summary>A variant that satisfies an ingredient: an item class (object path) or a liquid resource.</summary>
public sealed record IngredientOption(string? Item, string? Resource, bool Whole, int Usage, double Liters, double Nutrient = 1)
{
    /// <summary>Item or resource class without path and _C: Cucumber, Milk.</summary>
    public string ClassName => ShortClass(Item ?? Resource);

    public static string ShortClass(string? path)
    {
        if (string.IsNullOrEmpty(path) || path == "None") return "";
        var s = path.Split('/').Last();
        s = s.Contains('.') ? s[(s.IndexOf('.') + 1)..] : s;
        return s.EndsWith("_C") ? s[..^2] : s;
    }
}

/// <summary>A slot of the cooking window: its title (localized) and the variants that fit.</summary>
public sealed record Ingredient(string Title, string TitleSource, bool IsResource, IReadOnlyList<IngredientOption> Options);

public sealed class RecipeInfo
{
    /// <summary>Package / primary asset name: Cook_Dish_Cake_Pancake.</summary>
    public required string Id { get; init; }
    public required string Name { get; init; }
    public string NameSource { get; init; } = "";
    public string Description { get; init; } = "";
    /// <summary>Object path of the dish class (ResultingFood.Item).</summary>
    public string? Result { get; init; }
    /// <summary>Gameplay tags: Cooking.Utility.Pan...</summary>
    public IReadOnlyList<string> Utilities { get; init; } = [];
    public IReadOnlyList<string> Categories { get; init; } = [];
    /// <summary>Seconds; null = the recipe relies on the game's default.</summary>
    public double? CookingTime { get; init; }
    /// <summary>°C; null = default.</summary>
    public double? CookingTemperature { get; init; }
    public IReadOnlyList<Ingredient> Main { get; init; } = [];
    public IReadOnlyList<Ingredient> Optional { get; init; } = [];

    /// <summary>Stock cookbooks that teach it.</summary>
    public List<string> Books { get; } = [];
    public bool KnownByDefault { get; set; }
    /// <summary>Cook_Empty and Cook_Pizza: in the registry, but not real recipes (no dish).</summary>
    public bool Technical => GameData.TechnicalRecipes.Contains(Id);
    /// <summary>Nobody can learn it in the stock game: no cookbook and not known by default.</summary>
    public bool Hidden => !Technical && Books.Count == 0 && !KnownByDefault;

    internal RecipeInfo WithName(string name) => new()
    {
        Id = Id, Name = name, NameSource = NameSource, Description = Description, Result = Result, Utilities = Utilities,
        Categories = Categories, CookingTime = CookingTime, CookingTemperature = CookingTemperature, Main = Main, Optional = Optional,
    };
}

public sealed record BookInfo(string Id, string Name, string Description, IReadOnlyList<string> Recipes);

public sealed class GameData
{
    public const string RecipeFolder = "SCUM/Content/ConZ_Files/Cooking/Recipes";
    public const string BookFolder = "SCUM/Content/ConZ_Files/Items/Books/Cookbooks";
    public const string SkillPackage = "SCUM/Content/ConZ_Files/Skills/CookingSkill";
    public const string UtilityFolder = "SCUM/Content/ConZ_Files/Cooking/Data/CookingUtilityUIData";

    public static readonly HashSet<string> TechnicalRecipes = new(StringComparer.OrdinalIgnoreCase) { "Cook_Empty", "Cook_Pizza" };

    /// <summary>Utility tag (last part) -> CookingUtilityUIData_X where they differ.</summary>
    private static readonly Dictionary<string, string> UtilityData = new() { ["BBQGrid"] = "GrillGrid" };

    /// <summary>Language of the texts: "ru", "en"...</summary>
    public string Language { get; private init; } = "en";
    /// <summary>Every recipe package of the game (113 with the two technical ones), by id.</summary>
    public IReadOnlyList<RecipeInfo> Recipes { get; private init; } = [];
    public IReadOnlyList<BookInfo> Books { get; private init; } = [];
    public IReadOnlyList<string> KnownByDefault { get; private init; } = [];
    /// <summary>CookingRecipe entries in the game's AssetRegistry.bin.</summary>
    public int RegistryRecipeCount { get; private init; }
    /// <summary>Utility tag last part (Pan, Oven...) -> localized name.</summary>
    public IReadOnlyDictionary<string, string> UtilityNames { get; private init; } = new Dictionary<string, string>();

    /// <summary>Utility tag last part -> icon texture of its CookingUtilityUIData.</summary>
    public IReadOnlyDictionary<string, string> UtilityIcons { get; private init; } = new Dictionary<string, string>();

    /// <summary>Items recipes use or make, and the cookbooks, by object path of the class.</summary>
    public IReadOnlyDictionary<string, ItemInfo> Items => _items;

    /// <summary>Thread-safe: LoadCatalog adds the items it reads (icons and names of items no stock recipe uses).</summary>
    private System.Collections.Concurrent.ConcurrentDictionary<string, ItemInfo> _items = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Caption of an item (object path of its class), else its class name.</summary>
    public string ItemName(string? item) =>
        item != null && Items.TryGetValue(item, out var n) && n.Name.Length > 0 ? n.Name : IngredientOption.ShortClass(item).Replace('_', ' ');

    /// <summary>Icon texture of an item, or null.</summary>
    public string? ItemIcon(string? item) => item != null && Items.TryGetValue(item, out var n) ? n.Icon : null;

    // ------------------------------------------------------------------ items for the picker

    /// <summary>Folders of /Game/ConZ_Files/Items/Food -> picker category.</summary>
    private static readonly Dictionary<string, ItemCategory> FoodFolders = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Vegetables"] = ItemCategory.Vegetables, ["Edible_plants"] = ItemCategory.Vegetables, ["Mushrooms_Edible"] = ItemCategory.Vegetables,
        ["Mushrooms_Poisonous"] = ItemCategory.Vegetables, ["Fruit"] = ItemCategory.Fruit, ["Wild_Fruit"] = ItemCategory.Fruit,
        ["Nuts_and_Seeds"] = ItemCategory.Fruit, ["MeatAndAnimalProducts"] = ItemCategory.MeatFish, ["Canned_Food"] = ItemCategory.Canned,
        ["Dry_Food"] = ItemCategory.Dry, ["MiscFood"] = ItemCategory.Dry, ["Candy"] = ItemCategory.Dry, ["Vitamins"] = ItemCategory.Dry,
        ["CookedFood"] = ItemCategory.Dishes,
    };

    private const string ItemsFolder = "/Game/ConZ_Files/Items/";
    private const string DrinkFolder = "/Game/ConZ_Files/Items/Drink/";

    private static readonly string[] DairyWords = ["Milk", "Cheese", "Butter", "Cream", "Yog", "Kefir"];

    public static ItemCategory CategoryOf(string itemClass)
    {
        var parts = itemClass.Split('/');
        // bottles, cans, spirits (Items/Drink): drinks, with the liquids
        if (itemClass.StartsWith(DrinkFolder, StringComparison.OrdinalIgnoreCase)) return ItemCategory.Liquids;
        var i = Array.FindIndex(parts, p => p.Equals("Food", StringComparison.OrdinalIgnoreCase));
        if (i < 0) return itemClass.StartsWith(ItemsFolder, StringComparison.OrdinalIgnoreCase) ? ItemCategory.Inedible : ItemCategory.Other;
        if (i + 1 >= parts.Length - 1 || !FoodFolders.TryGetValue(parts[i + 1], out var c)) return ItemCategory.Other;
        // milk, cheese, butter, cream and yoghurt sit with meat in MeatAndAnimalProducts: the picker shows them as dairy (eggs stay with meat)
        if (c == ItemCategory.MeatFish && DairyWords.Any(w => parts[^1].Contains(w, StringComparison.OrdinalIgnoreCase))) return ItemCategory.Dairy;
        return c;
    }

    /// <summary>
    /// Every food item of the game (Items/Food, without body parts) plus every item a recipe uses: what the item picker
    /// offers. Reads a few hundred _ES packages (a few seconds), so the editor loads it in the background.
    /// </summary>
    public List<ItemInfo> LoadCatalog(GameProvider game)
    {
        var loc = Localization.Load(game, Language);
        var list = new Dictionary<string, ItemInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (var cls in ItemClasses(game, k => k.StartsWith("SCUM/Content/ConZ_Files/Items/Food/", StringComparison.OrdinalIgnoreCase)
                                                   || k.StartsWith("SCUM/Content/ConZ_Files/Items/Drink/", StringComparison.OrdinalIgnoreCase)))
            if (!list.ContainsKey(cls)) list[cls] = _items.GetOrAdd(cls, c => ReadItem(game, loc, c));
        foreach (var (cls, info) in Items)
            if (!list.ContainsKey(cls) && !cls.Contains("/Books/", StringComparison.OrdinalIgnoreCase)) list[cls] = info;
        return Sorted(list.Values);
    }

    /// <summary>
    /// Every other item of the game (tools, parts, clothes, weapons...): the "inedible" category of the picker. A recipe
    /// slot takes any item (checked in game 2026-10-21: bolts, one from a stack or a whole box). About two thousand
    /// _ES packages, so the editor reads them in the background after the food.
    /// </summary>
    public List<ItemInfo> LoadInedible(GameProvider game)
    {
        var loc = Localization.Load(game, Language);
        var list = new List<ItemInfo>();
        foreach (var cls in ItemClasses(game, k => k.StartsWith("SCUM/Content/ConZ_Files/Items/", StringComparison.OrdinalIgnoreCase)
                                                   && !k.StartsWith("SCUM/Content/ConZ_Files/Items/Food/", StringComparison.OrdinalIgnoreCase)
                                                   && !k.StartsWith("SCUM/Content/ConZ_Files/Items/Drink/", StringComparison.OrdinalIgnoreCase)))
        {
            try { list.Add(_items.GetOrAdd(cls, c => ReadItem(game, loc, c))); }
            catch (Exception) { /* a package CUE4Parse cannot read: not offered */ }
        }
        return Sorted(list);
    }

    /// <summary>Item classes (/Game/.../X.X_C) of the _ES packages whose file path passes the filter (food: no body parts).</summary>
    private static IEnumerable<string> ItemClasses(GameProvider game, Func<string, bool> filter) => game.Provider.Files.Keys
        .Where(k => k.EndsWith("_ES.uasset", StringComparison.OrdinalIgnoreCase) && filter(k) && !k.Contains("/BodyParts/", StringComparison.OrdinalIgnoreCase))
        .Select(k =>
        {
            var package = "/Game/" + k["SCUM/Content/".Length..^"_ES.uasset".Length];
            return $"{package}.{package.Split('/').Last()}_C";
        });

    private List<ItemInfo> Sorted(IEnumerable<ItemInfo> items) =>
        items.Where(i => i.Name.Length > 0).OrderBy(i => i.Name, StringComparer.Create(System.Globalization.CultureInfo.GetCultureInfo(Language), true)).ToList();

    public const string IconFolder = "SCUM/Content/ConZ_Files/Items/Item_Icons/";

    /// <summary>Every texture of the game's item icon folder (/Game/.../T.T), sorted by name: "all icons" of the icon picker.</summary>
    public static List<string> IconTextures(GameProvider game) => game.Provider.Files.Keys
        .Where(k => k.StartsWith(IconFolder, StringComparison.OrdinalIgnoreCase) && k.EndsWith(".uasset", StringComparison.OrdinalIgnoreCase))
        .Select(k =>
        {
            var package = "/Game/" + k["SCUM/Content/".Length..^".uasset".Length];
            return $"{package}.{package[(package.LastIndexOf('/') + 1)..]}";
        })
        .OrderBy(t => t[(t.LastIndexOf('/') + 1)..], StringComparer.OrdinalIgnoreCase).ToList();

    private Dictionary<string, RecipeInfo>? _byId;
    public RecipeInfo? Recipe(string id) => (_byId ??= Recipes.ToDictionary(r => r.Id, StringComparer.OrdinalIgnoreCase)).GetValueOrDefault(id);
    public BookInfo? Book(string id) => Books.FirstOrDefault(b => b.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

    public string UtilityName(string tag)
    {
        var last = tag.Split('.').Last();
        return UtilityNames.TryGetValue(last, out var n) ? n : last;
    }

    /// <summary>Quick check for the setup window: CookingRecipe entries in the registry and stock cookbooks.</summary>
    public static (int Recipes, int Books) Count(GameProvider game)
    {
        var p = game.Provider;
        var reg = new FAssetRegistryState(new FByteArchive("AssetRegistry.bin", p.Files[GameProvider.RegistryPath].Read(), p.Versions));
        var recipes = reg.PreallocatedAssetDataBuffers.Count(a => a.AssetClass.Text == "CookingRecipe");
        return (recipes, BookIds(game).Count);
    }

    private static List<string> BookIds(GameProvider game) => game.Provider.Files.Keys
        .Where(k => k.StartsWith(BookFolder + "/Book_Cooking_", StringComparison.OrdinalIgnoreCase) && k.EndsWith(".uasset", StringComparison.OrdinalIgnoreCase))
        .Select(k => Path.GetFileNameWithoutExtension(k))
        .Where(n => !n.EndsWith("_ES", StringComparison.OrdinalIgnoreCase))
        .OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();

    /// <summary>Reads everything (a few seconds). <paramref name="language"/>: two-letter code of the program's language.</summary>
    public static GameData Load(GameProvider game, string language)
    {
        var p = game.Provider;
        var loc = Localization.Load(game, language);

        var (registryCount, _) = Count(game);

        var recipes = p.Files.Keys
            .Where(k => k.StartsWith(RecipeFolder + "/", StringComparison.OrdinalIgnoreCase) && k.EndsWith(".uasset", StringComparison.OrdinalIgnoreCase)
                        && k.IndexOf('/', RecipeFolder.Length + 1) < 0)
            .Select(k => Path.GetFileNameWithoutExtension(k))
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .Select(id => ReadRecipe(game, loc, id))
            .Where(r => r != null).Select(r => r!).ToList();
        // a few recipe names have no translation (Moussaka...): the game's caption of the dish has one
        if (loc.Language != "en")
            for (var i = 0; i < recipes.Count; i++)
                if (recipes[i].Name == recipes[i].NameSource && recipes[i].Result is { } res && ItemCaption(game, loc, res) is { Length: > 0 } caption)
                    recipes[i] = recipes[i].WithName(caption);
        var byId = recipes.ToDictionary(r => r.Id, StringComparer.OrdinalIgnoreCase);

        var books = new List<BookInfo>();
        foreach (var id in BookIds(game))
        {
            var list = new List<string>();
            foreach (var e in Exports(game, $"{BookFolder}/{id}"))
                if ((string?)e["Type"] == "CookingRecipeCollectionItemTag" && e["Properties"]?["_recipes"] is JArray arr)
                    list.AddRange(arr.Select(x => (string?)x["PrimaryAssetName"]).Where(x => !string.IsNullOrEmpty(x)).Select(x => x!));
            list = list.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var es = Exports(game, $"{BookFolder}/{id}_ES").Select(e => e["Properties"]).FirstOrDefault(pr => pr?["Caption"] != null);
            books.Add(new BookInfo(id, loc.Text(es?["Caption"]) is { Length: > 0 } c ? c : id, loc.Text(es?["Description"]), list));
            foreach (var r in list)
                if (byId.TryGetValue(r, out var info)) info.Books.Add(id);
        }

        var known = new List<string>();
        foreach (var e in Exports(game, SkillPackage))
            if (e["Properties"]?["_recipesKnownByDefault"] is JArray arr)
                known.AddRange(arr.Select(x => (string?)x["PrimaryAssetName"]).Where(x => !string.IsNullOrEmpty(x)).Select(x => x!));
        foreach (var r in known)
            if (byId.TryGetValue(r, out var info)) info.KnownByDefault = true;

        var utilities = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var utilityIcons = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var tag in recipes.SelectMany(r => r.Utilities).Distinct())
        {
            var last = tag.Split('.').Last();
            var data = UtilityData.GetValueOrDefault(last, last);
            var ui = Exports(game, $"{UtilityFolder}/CookingUtilityUIData_{data}").Select(e => e["Properties"]).FirstOrDefault(p => p?["Name"] != null);
            var name = ui == null ? null : loc.Text(ui["Name"]);
            if ((string?)Get(Get(ui, "Icon"), "AssetPathName") is { } uicon && uicon != "None") utilityIcons[last] = uicon;
            utilities[last] = string.IsNullOrEmpty(name) ? last : name;
        }

        // captions of every item a recipe uses or makes (search by ingredient, slot captions)
        var items = new Dictionary<string, ItemInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in recipes.SelectMany(r => r.Main.Concat(r.Optional)).SelectMany(i => i.Options).Select(o => o.Item)
                     .Concat(recipes.Select(r => r.Result)).Where(i => i != null).Distinct(StringComparer.OrdinalIgnoreCase))
            items[item!] = ReadItem(game, loc, item!);
        foreach (var liquid in ReadLiquids(game, loc)) items[liquid.Class] = liquid;
        // liquids without a name of their own (DrinkableWater): the title of a stock slot that asks only for them ("Вода")
        foreach (var slot in recipes.SelectMany(r => r.Main.Concat(r.Optional)).Where(i => i.Options.Count == 1 && i.Options[0].Resource != null))
        {
            var res = slot.Options[0].Resource!;
            var have = items.GetValueOrDefault(res);
            if (have == null || have.Name == IngredientOption.ShortClass(res))
                items[res] = (have ?? new ItemInfo(res, "", "", null, ItemCategory.Liquids, true)) with { Name = slot.Title };
        }
        foreach (var b in books)
            items[$"/Game/ConZ_Files/Items/Books/Cookbooks/{b.Id}.{b.Id}_C"] = ReadItem(game, loc, $"/Game/ConZ_Files/Items/Books/Cookbooks/{b.Id}.{b.Id}_C");

        return new GameData
        {
            Language = loc.Language, Recipes = recipes, Books = books, KnownByDefault = known, RegistryRecipeCount = registryCount,
            UtilityNames = utilities, _items = new(items, StringComparer.OrdinalIgnoreCase), UtilityIcons = utilityIcons,
        };
    }

    private static RecipeInfo? ReadRecipe(GameProvider game, Localization loc, string id) => ReadRecipe(game.Provider, loc, $"{RecipeFolder}/{id}", id);

    /// <summary>A recipe package read from any provider (e.g. a mod stage folder), with source strings as texts.</summary>
    public static RecipeInfo? ReadRecipe(CUE4Parse.FileProvider.IFileProvider provider, string package) =>
        ReadRecipe(provider, new Localization(), package, Path.GetFileName(package));

    private static RecipeInfo? ReadRecipe(CUE4Parse.FileProvider.IFileProvider provider, Localization loc, string package, string id)
    {
        var e = Exports(provider, package).FirstOrDefault(x => (string?)x["Type"] == "CookingRecipe");
        if (e?["Properties"] is not JObject pr) return null;
        return new RecipeInfo
        {
            Id = id,
            Name = loc.Text(pr["RecipeName"]) is { Length: > 0 } n ? n : id,
            NameSource = Localization.Source(pr["RecipeName"]),
            Description = loc.Text(pr["Description"]),
            Result = (string?)Get(Get(pr["ResultingFood"], "Item"), "AssetPathName") is { } res && res != "None" ? res : null,
            Utilities = Strings(pr["Utility"]),
            Categories = Strings(pr["Category"]),
            CookingTime = (double?)pr["CookingTime"],
            CookingTemperature = (double?)pr["CookingTemperature"],
            Main = Ingredients(loc, pr["MainIngredients"]),
            Optional = Ingredients(loc, pr["OptionalIngredients"]),
        };
    }

    /// <summary>Localized caption of an item class (/Game/.../Dish_X.Dish_X_C -> its Dish_X_ES package), or "".</summary>
    private static string ItemCaption(GameProvider game, Localization loc, string itemClass) => ReadItem(game, loc, itemClass).Name;

    /// <summary>Caption, description and icon of an item class from its _ES package (empty texts when there is none).</summary>
    private static ItemInfo ReadItem(GameProvider game, Localization loc, string itemClass)
    {
        var package = itemClass.Split('.')[0];
        JToken? es = null;
        string? icon = null, caption = null, description = null;
        double? weight = null;
        int? uses = null;
        var esPackage = package.StartsWith("/Game/", StringComparison.OrdinalIgnoreCase) ? "SCUM/Content/" + package[6..] + "_ES" : null;
        // an _ES that is a child of another (Mutant_Buck_Steak_ES of a steak) keeps only what differs: the rest is the parent's
        for (var depth = 0; esPackage != null && depth < 4; depth++)
        {
            var exports = Exports(game, esPackage);
            es = exports.Select(e => e["Properties"]).FirstOrDefault(pr => pr?["Caption"] != null || pr?["GeneralPurposeIcon"] != null);
            caption ??= es?["Caption"] is { } c ? loc.Text(c) : null;
            description ??= es?["Description"] is { } d ? loc.Text(d) : null;
            weight ??= (double?)Get(es, "Weight");
            uses ??= exports.Select(e => (int?)Get(e["Properties"], "MaxQuantity")).FirstOrDefault(q => q != null);
            icon ??= (string?)Get(Get(es, "GeneralPurposeIcon"), "AssetPathName") ?? (string?)Get(Get(es, "GridInventoryIcon"), "AssetPathName");
            if (icon is not (null or "None") && caption != null) break;
            var parent = (string?)Get(exports.FirstOrDefault(e => (string?)e["Type"] == "BlueprintGeneratedClass")?["SuperStruct"], "ObjectPath");
            esPackage = parent != null && parent.StartsWith("SCUM/Content/", StringComparison.OrdinalIgnoreCase) ? parent[..parent.LastIndexOf('.')] : null;
        }
        return new ItemInfo(itemClass, caption ?? "", description ?? "", icon is null or "None" ? null : icon, CategoryOf(itemClass), Weight: weight, Uses: uses);
    }

    /// <summary>
    /// Object path of a resource class from CUE4Parse's reference (ObjectName "BlueprintGeneratedClass'Milk_C'", ObjectPath
    /// "SCUM/Content/X/Milk.0" or "/Game/X/Milk"): /Game/X/Milk.Milk_C, the form modkit's ops take.
    /// </summary>
    internal static string? ResourcePath(JToken? objectName, JToken? objectPath)
    {
        var name = (string?)objectName;
        if (string.IsNullOrEmpty(name)) return null;
        if (name.Contains('\'')) name = name.Split('\'')[1];
        var path = (string?)objectPath ?? "";
        var dot = path.LastIndexOf('.');
        if (dot > path.LastIndexOf('/')) path = path[..dot];
        if (path.StartsWith("SCUM/Content/", StringComparison.OrdinalIgnoreCase)) path = "/Game/" + path["SCUM/Content/".Length..];
        return path.StartsWith("/Game/", StringComparison.OrdinalIgnoreCase) ? $"{path}.{name}" : name;
    }

    /// <summary>Liquids recipes can ask for (GameResources/Food/Liquids and the waters), with their names in the game.</summary>
    private static IEnumerable<ItemInfo> ReadLiquids(GameProvider game, Localization loc)
    {
        foreach (var f in game.Provider.Files.Keys.Where(k => k.EndsWith(".uasset", StringComparison.OrdinalIgnoreCase) &&
                     (k.StartsWith("SCUM/Content/ConZ_Files/GameResources/Food/Liquids/", StringComparison.OrdinalIgnoreCase) ||
                      (k.StartsWith("SCUM/Content/ConZ_Files/GameResources/", StringComparison.OrdinalIgnoreCase) && k.IndexOf('/', "SCUM/Content/ConZ_Files/GameResources/".Length) < 0
                       && k.Contains("Water", StringComparison.OrdinalIgnoreCase)))))
        {
            var package = f[..^".uasset".Length];
            var name = package.Split('/').Last();
            var cdo = Exports(game, package).FirstOrDefault(e => ((string?)e["Name"])?.StartsWith("Default__") == true);
            var title = loc.Text(cdo?["Properties"]?["ResourceName"]);
            var props = cdo?["Properties"];
            if (props?["ResourceName"] == null && props?["CookingIngredientTag"] == null) continue; // not a liquid a recipe can use
            // the game's own picture of the liquid (water: a bottle, milk / oil: their trader icons)
            var image = (string?)Get(Get(props, "ResourceImage"), "AssetPathName");
            yield return new ItemInfo($"/Game/{package["SCUM/Content/".Length..]}.{name}_C", title.Length > 0 ? title : name, "", image is null or "None" ? null : image, ItemCategory.Liquids, true);
        }
    }

    /// <summary>A child of a JSON object; null for a missing value, a JSON null or a non-object.</summary>
    private static JToken? Get(JToken? t, string key) => t is JObject o && o[key] is { Type: not JTokenType.Null } v ? v : null;

    private static List<string> Strings(JToken? t) => t is JArray a ? a.Select(x => (string?)x).Where(x => !string.IsNullOrEmpty(x)).Select(x => x!).ToList() : [];

    private static List<Ingredient> Ingredients(Localization loc, JToken? t)
    {
        if (t is not JArray arr) return [];
        return arr.Select(i => new Ingredient(
            loc.Text(i["IngredientTitle"]),
            Localization.Source(i["IngredientTitle"]),
            (bool?)i["IsResource"] ?? false,
            (i["PossibleIngredient"] as JArray ?? []).Select(o =>
            {
                var item = (string?)Get(o["Item"], "AssetPathName");
                var res = ResourcePath(Get(o["ResourceType"], "ObjectName"), Get(o["ResourceType"], "ObjectPath"));
                return new IngredientOption(item is null or "None" ? null : item, res, (bool?)o["ShouldConsumeWhole"] ?? false,
                    (int?)o["Usage"] ?? 1, (double?)o["Liters"] ?? 0, (double?)o["NutrientInclusionFactor"] ?? 1);
            }).ToList())).ToList();
    }

    /// <summary>Exports of a package as JSON (CUE4Parse's own serialization), empty when it does not exist.</summary>
    internal static List<JObject> Exports(GameProvider game, string package) => Exports(game.Provider, package);

    internal static List<JObject> Exports(CUE4Parse.FileProvider.IFileProvider provider, string package)
    {
        if (!provider.TryLoadPackage(package, out var pkg)) return [];
        return pkg.GetExports().Select(e => JObject.Parse(JsonConvert.SerializeObject(e))).ToList();
    }
}

/// <summary>The game's Game.locres for one culture: FText (namespace, key) -> translation, source string as fallback.</summary>
internal sealed class Localization
{
    public string Language { get; private init; } = "en";
    private readonly Dictionary<(string, string), string> _texts = new();

    /// <summary>Culture folders of SCUM/Content/Localization/Game by two-letter code.</summary>
    private static readonly Dictionary<string, string> Cultures = new()
    {
        ["en"] = "en-US", ["ru"] = "ru-RU", ["de"] = "de-DE", ["pl"] = "pl-PL", ["fr"] = "fr-FR", ["es"] = "es-ES", ["pt"] = "pt-BR",
        ["tr"] = "tr-TR", ["ja"] = "ja-JP", ["ko"] = "ko-KR", ["th"] = "th-TH", ["zh"] = "zh-Hans-CN",
    };

    public static Localization Load(GameProvider game, string language)
    {
        var code = Cultures.ContainsKey(language) ? language : "en";
        var loc = new Localization { Language = code };
        var path = $"SCUM/Content/Localization/Game/{Cultures[code]}/Game.locres";
        if (!game.Provider.Files.TryGetValue(path, out var file)) return loc;
        var res = new FTextLocalizationResource(new FByteArchive(path, file.Read(), game.Provider.Versions));
        foreach (var (ns, entries) in res.Entries)
            foreach (var (key, entry) in entries)
                loc._texts[(ns.Str, key.Str)] = entry.LocalizedString;
        return loc;
    }

    /// <summary>Localized text of a serialized FText (Namespace / Key / SourceString), or "" for null.</summary>
    public string Text(JToken? t)
    {
        if (t == null || t.Type == JTokenType.Null) return "";
        if (t.Type == JTokenType.String) return (string?)t ?? "";
        var ns = (string?)t["Namespace"] ?? "";
        var key = (string?)t["Key"];
        if (key != null && _texts.TryGetValue((ns, key), out var s) && s.Length > 0) return s;
        return (string?)t["LocalizedString"] ?? Source(t);
    }

    /// <summary>The string modkit matches titles against: SourceString, or the text of a culture-invariant FText.</summary>
    public static string Source(JToken? t)
    {
        if (t is not JObject o) return t?.Type == JTokenType.String ? (string?)t ?? "" : "";
        return (string?)o["SourceString"] ?? (string?)o["CultureInvariantString"] ?? (string?)o["LocalizedString"] ?? "";
    }
}

/// <summary>Categories of the item picker, in their display order (Other: only under "All").</summary>
public enum ItemCategory { Vegetables, Fruit, MeatFish, Dairy, Canned, Dry, Liquids, Dishes, Inedible, Other }

/// <summary>An item of the game: object path of its class, caption and description (game language), icon texture.</summary>
public sealed record ItemInfo(string Class, string Name, string Description, string? Icon, ItemCategory Category, bool IsLiquid = false, double? Weight = null, int? Uses = null)
{
    public string ClassName => IngredientOption.ShortClass(Class);

    /// <summary>Folder of the item in the game: Items/Food/Vegetables.</summary>
    public string Folder => Class.StartsWith("/Game/ConZ_Files/") ? string.Join("/", Class["/Game/ConZ_Files/".Length..].Split('.')[0].Split('/').SkipLast(1)) : "";
}
