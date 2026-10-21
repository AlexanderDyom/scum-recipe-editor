// The recipe list of the main window: the game's recipes with the project's edits on top (new recipes, recipes added
// to books or to the known-from-start list), grouped: new in the mod / by cookbook / known from the start / hidden.
using System.Text.Json;
using RecipeEditor.Core.Game;

namespace RecipeEditor.Core.Project;

public enum RecipeGroupKind { New, Book, Known, Hidden }

public sealed class RecipeEntry
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    /// <summary>The game's recipe (for a new one: its template), or null.</summary>
    public RecipeInfo? Game { get; init; }
    public RecipeGroupKind Group { get; init; }
    /// <summary>Cookbook of a Book group (stock or the mod's own).</summary>
    public string? GroupBook { get; init; }
    /// <summary>Names of every cookbook that teaches it (stock and the mod's own).</summary>
    public IReadOnlyList<string> BookNames { get; init; } = [];
    public bool KnownByDefault { get; init; }
    public bool IsNew { get; init; }
    public bool IsChanged { get; init; }
    /// <summary>Hidden in the stock game (no cookbook, not known from the start).</summary>
    public bool HiddenInGame { get; init; }
    /// <summary>Lower-case name, id and ingredient titles / item classes for the search box.</summary>
    public string SearchText { get; init; } = "";
    /// <summary>The mod's description (["text", "Description", …]) or the game's.</summary>
    public string Description { get; init; } = "";

    public bool Matches(string? query)
    {
        if (string.IsNullOrWhiteSpace(query)) return true;
        return query.Trim().ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries).All(SearchText.Contains);
    }
}

public sealed class RecipeCatalog
{
    public IReadOnlyList<RecipeEntry> Entries { get; }
    /// <summary>Group order: new, cookbooks (stock order, then own books), known, hidden.</summary>
    public IReadOnlyList<(RecipeGroupKind Kind, string? Book, string Title)> Groups { get; }

    private RecipeCatalog(List<RecipeEntry> entries, List<(RecipeGroupKind, string?, string)> groups)
    {
        Entries = entries;
        Groups = groups;
    }

    /// <summary>Recipes as the game shows them with the project applied; technical recipes (Cook_Empty, Cook_Pizza) are left out.</summary>
    public static RecipeCatalog Build(GameData game, RecipeProject? project, Func<string, string?>? names = null)
    {
        var cooking = project?.Modkit.Cooking;
        var newIds = new List<string>(cooking?.NewRecipes ?? []);
        var changed = new HashSet<string>(project?.ChangedRecipes ?? [], StringComparer.OrdinalIgnoreCase);
        var known = new HashSet<string>(game.KnownByDefault, StringComparer.OrdinalIgnoreCase);
        known.UnionWith(cooking?.KnownByDefault ?? []);

        // cookbook -> recipes, stock with the mod's additions, then the mod's own books
        var books = new List<(string Id, string Name, HashSet<string> Recipes)>();
        foreach (var b in game.Books)
        {
            var set = new HashSet<string>(b.Recipes, StringComparer.OrdinalIgnoreCase);
            if (cooking?.Books.TryGetValue(b.Id, out var extra) == true) set.UnionWith(extra);
            books.Add((b.Id, b.Name, set));
        }
        foreach (var (id, recipes) in cooking?.OwnBooks ?? [])
            books.Add((id, OwnBookName(project!, id), new HashSet<string>(recipes, StringComparer.OrdinalIgnoreCase)));

        var entries = new List<RecipeEntry>();
        IEnumerable<string> ids = game.Recipes.Where(r => !r.Technical).Select(r => r.Id)
            .Concat(newIds.Where(n => game.Recipe(n) == null));
        foreach (var id in ids)
        {
            var isNew = newIds.Contains(id, StringComparer.OrdinalIgnoreCase);
            var info = game.Recipe(id) ?? (isNew ? TemplateOf(game, project!, id) : null);
            var inBooks = books.Where(b => b.Recipes.Contains(id)).ToList();
            var isKnown = known.Contains(id);
            var kind = isNew ? RecipeGroupKind.New
                : inBooks.Count > 0 ? RecipeGroupKind.Book
                : isKnown ? RecipeGroupKind.Known
                : RecipeGroupKind.Hidden;
            var name = names?.Invoke(id) ?? (isNew || changed.Contains(id) ? TextOp(cooking, id, "RecipeName") : null) ?? info?.Name ?? id;
            entries.Add(new RecipeEntry
            {
                Id = id, Name = name, Game = info, Group = kind, GroupBook = kind == RecipeGroupKind.Book ? inBooks[0].Id : null,
                BookNames = inBooks.Select(b => b.Name).ToList(), KnownByDefault = isKnown, IsNew = isNew,
                IsChanged = !isNew && changed.Contains(id), HiddenInGame = game.Recipe(id)?.Hidden == true,
                Description = TextOp(cooking, id, "Description") ?? info?.Description ?? "",
                SearchText = SearchText(game, id, name, info),
            });
        }

        var groups = new List<(RecipeGroupKind, string?, string)>();
        if (entries.Any(e => e.Group == RecipeGroupKind.New)) groups.Add((RecipeGroupKind.New, null, ""));
        foreach (var b in books.Where(b => entries.Any(e => e.GroupBook == b.Id))) groups.Add((RecipeGroupKind.Book, b.Id, b.Name));
        if (entries.Any(e => e.Group == RecipeGroupKind.Known)) groups.Add((RecipeGroupKind.Known, null, ""));
        if (entries.Any(e => e.Group == RecipeGroupKind.Hidden)) groups.Add((RecipeGroupKind.Hidden, null, ""));

        // within a group: by name, in the current language
        var order = groups.Select((g, i) => (g.Item1, g.Item2, i)).ToDictionary(g => (g.Item1, g.Item2 ?? ""), g => g.i);
        entries = entries.OrderBy(e => order[(e.Group, e.GroupBook ?? "")])
            .ThenBy(e => e.Name, StringComparer.Create(System.Globalization.CultureInfo.GetCultureInfo(game.Language), true)).ToList();
        return new RecipeCatalog(entries, groups);
    }

    private static string SearchText(GameData game, string id, string name, RecipeInfo? info)
    {
        var parts = new List<string> { name, id };
        if (info != null)
            foreach (var i in info.Main.Concat(info.Optional))
            {
                parts.Add(i.Title);
                parts.Add(i.TitleSource);
                parts.AddRange(i.Options.Select(o => o.ClassName));
                parts.AddRange(i.Options.Where(o => o.Item != null).Select(o => game.ItemName(o.Item)));
            }
        return string.Join("\n", parts).ToLowerInvariant();
    }

    /// <summary>The recipe a new recipe was cloned from (clones: template -> new).</summary>
    private static RecipeInfo? TemplateOf(GameData game, RecipeProject project, string id)
    {
        foreach (var c in project.Modkit.Clones)
            if (c.New != null && c.Template != null && c.New.EndsWith("/" + id, StringComparison.OrdinalIgnoreCase))
                return game.Recipe(c.Template.Split('/').Last());
        return null;
    }

    /// <summary>["text", property, value] of a recipe in modkit.json (the last one wins).</summary>
    private static string? TextOp(ScumModKit.Build.Ops.CookingConfig? cooking, string recipe, string property)
    {
        if (cooking == null || !cooking.Recipes.TryGetValue(recipe, out var ops)) return null;
        string? text = null;
        foreach (var op in ops)
            if (op.ValueKind == JsonValueKind.Array && op.GetArrayLength() >= 3 && op[0].GetString() == "text" && op[1].GetString() == property)
                text = op[2].GetString();
        return text;
    }

    private static string OwnBookName(RecipeProject project, string book)
    {
        foreach (var (es, texts) in project.Modkit.Cooking?.ItemTexts ?? [])
            if (es.EndsWith("/" + book + "_ES", StringComparison.OrdinalIgnoreCase) && texts.Count > 0) return texts[0];
        return book;
    }
}
