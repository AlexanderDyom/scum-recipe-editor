// An opened project's recipes for editing: each recipe is the game's (or, for a new recipe, its template's) with the
// project's ops applied; edited recipes get their ops regenerated on save. modkit.json is rewritten as a JSON tree, so
// every other section (clones, edits, books, item texts...) and every untouched recipe keeps its ops as they are.
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using RecipeEditor.Core.Game;
using RecipeEditor.Core.Recipe;

namespace RecipeEditor.Core.Project;

public sealed class ProjectDocument
{
    private readonly Dictionary<string, RecipeState> _states = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, RecipeState> _saved = new(StringComparer.OrdinalIgnoreCase);

    public RecipeProject Project { get; }
    public GameData Game { get; }

    public ProjectDocument(RecipeProject project, GameData game)
    {
        Project = project;
        Game = game;
        foreach (var e in project.Modkit.Edits)
            if (IconOfEdit(e) is { } icon) _icons[icon.Package] = icon.Icon;
    }

    /// <summary>The game recipe a recipe starts from: itself, or the template a new recipe was cloned from.</summary>
    public RecipeInfo? Base(string id)
    {
        var r = Game.Recipe(id);
        if (r != null) return r;
        foreach (var c in Project.Modkit.Clones)
            if (c.New != null && c.Template != null && c.New.EndsWith("/" + id, StringComparison.OrdinalIgnoreCase))
                return Game.Recipe(c.Template.Split('/').Last());
        return null;
    }

    /// <summary>The game's recipe as a state (for a new recipe: its template under the new id).</summary>
    public RecipeState? GameState(string id)
    {
        var b = Base(id);
        if (b == null) return null;
        var s = RecipeState.FromGame(b);
        return s.Id == id ? s : Rename(s, id);
    }

    /// <summary>The recipe with the project's ops (the copy the editor changes), or null when there is no game recipe.</summary>
    public RecipeState? State(string id)
    {
        if (_states.TryGetValue(id, out var s)) return s;
        s = GameState(id);
        if (s == null) return null;
        if (Project.Modkit.Cooking?.Recipes.TryGetValue(id, out var ops) == true) s.Apply(ops);
        _states[id] = s;
        _saved[id] = s.Clone();
        return s;
    }

    /// <summary>Recipes changed since they were loaded or saved.</summary>
    public IEnumerable<string> Dirty => _states.Where(kv => !kv.Value.SameAs(_saved[kv.Key]) || SlotMarksChanged(kv.Key)).Select(kv => kv.Key);

    public bool IsDirty => ListsDirty || Dirty.Any();

    private bool SlotMarksChanged(string id) =>
        Groups.All.Any(g => _states[id].Slots[g].Count != _saved[id].Slots[g].Count ||
                            _states[id].Slots[g].Zip(_saved[id].Slots[g]).Any(p => p.First.Removed != p.Second.Removed));

    /// <summary>Back to the game's recipe (keeping the mod's name, description and result of a new recipe).</summary>
    public void Reset(string id)
    {
        var s = State(id);
        var g = GameState(id);
        if (s == null || g == null) return;
        g.Name = s.Name;
        g.Description = s.Description;
        g.Result = s.Result;
        _states[id] = g;
    }

    /// <summary>Unsaved edits of another document of the same project (the game data was read again, e.g. in another language).</summary>
    public void TakeEditsFrom(ProjectDocument old)
    {
        foreach (var id in old.Dirty.ToList())
        {
            State(id); // loads the saved state in this document's language
            _states[id] = old._states[id];
        }
        if (old.ListsDirty)
        {
            _icons.Clear();
            foreach (var (k, v) in old._icons) _icons[k] = v;
            ListsDirty = true;
        }
    }

    /// <summary>Puts a changed copy in place (the editor works on clones for undo).</summary>
    public void Set(RecipeState state) => _states[state.Id] = state;

    /// <summary>Ops the project would store for a recipe now.</summary>
    public List<JsonNode> Ops(string id) => RecipeOps.Generate(Base(id)!, Renamed(State(id)!, Base(id)!.Id));

    /// <summary>Writes the ops of every changed recipe into modkit.json (other content untouched). Read-only projects refuse.</summary>
    public void Save()
    {
        if (Project.ReadOnly) throw new InvalidOperationException("This project is opened read-only.");
        var root = JsonNode.Parse(File.ReadAllText(Project.ModkitPath), documentOptions: new JsonDocumentOptions
        {
            CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true,
        })!.AsObject();
        var cooking = root["cooking"] as JsonObject ?? (JsonObject)(root["cooking"] = new JsonObject());
        var recipes = cooking["recipes"] as JsonObject ?? (JsonObject)(cooking["recipes"] = new JsonObject());
        var dirty = Dirty.ToList();
        foreach (var id in dirty)
        {
            var ops = Ops(id);
            var isNew = Project.NewRecipes.Contains(id, StringComparer.OrdinalIgnoreCase);
            if (ops.Count == 0 && !isNew) recipes.Remove(id);
            else recipes[id] = new JsonArray([.. ops]);
        }
        if (ListsDirty)
        {
            // books, own books, known from the start, item texts and clones: what the Books section edits
            var c = Cooking;
            cooking["books"] = JsonSerializer.SerializeToNode(c.Books, Json);
            cooking["ownBooks"] = JsonSerializer.SerializeToNode(c.OwnBooks, Json);
            cooking["knownByDefault"] = JsonSerializer.SerializeToNode(c.KnownByDefault, Json);
            cooking["newRecipes"] = JsonSerializer.SerializeToNode(c.NewRecipes, Json);
            cooking["itemTexts"] = JsonSerializer.SerializeToNode(c.ItemTexts, Json);
            root["clones"] = new JsonArray(Project.Modkit.Clones.Select(CloneNode).ToArray());
            // the editor's icon edits are made anew; every other edit stays as it is
            var edits = (root["edits"] as JsonArray)?.Where(e => e?["editor"]?.GetValue<string>() != IconEditor).Select(e => e!.DeepClone()).ToList() ?? [];
            edits.AddRange(IconEdits());
            if (edits.Count > 0 || root["edits"] != null) root["edits"] = new JsonArray([.. edits]);
        }
        var text = root.ToJsonString(Json) + "\n";
        File.WriteAllText(Project.ModkitPath, text);
        Project.Reload();
        foreach (var id in dirty) _saved[id] = _states[id].Clone();
        ListsDirty = false;
    }

    private static JsonNode CloneNode(ScumModKit.Build.CloneSpec c)
    {
        var o = new JsonObject();
        if (c.Template != null) o["template"] = c.Template;
        if (c.New != null) o["new"] = c.New;
        if (c.With.Count > 0) o["with"] = new JsonArray(c.With.Select(w => (JsonNode)JsonValue.Create(w)!).ToArray());
        if (c.Packages.Count > 0)
            o["packages"] = new JsonArray(c.Packages.Select(p => (JsonNode)new JsonObject { ["old"] = p.Old, ["new"] = p.New }).ToArray());
        return o;
    }

    // ------------------------------------------------------------------ books and "known from the start"

    public const string BookFolder = "/Game/ConZ_Files/Items/Books/Cookbooks";

    /// <summary>Cookbooks, own books, known-from-start list and texts were changed and are not saved yet.</summary>
    public bool ListsDirty { get; private set; }

    /// <summary>The project's cooking section, edited in place (saved by Save).</summary>
    public ScumModKit.Build.Ops.CookingConfig Cooking => Project.Modkit.Cooking ??= new ScumModKit.Build.Ops.CookingConfig();

    /// <summary>Recipes a stock cookbook teaches in the mod: its own and the ones the mod adds.</summary>
    public IEnumerable<string> BookRecipes(string book) =>
        (Game.Book(book)?.Recipes ?? []).Concat(Cooking.Books.GetValueOrDefault(book) ?? []).Distinct(StringComparer.OrdinalIgnoreCase);

    /// <summary>Recipes known from the start in the mod (the game's 35 + the mod's).</summary>
    public IEnumerable<string> KnownRecipes => Game.KnownByDefault.Concat(Cooking.KnownByDefault).Distinct(StringComparer.OrdinalIgnoreCase);

    public void AddToBook(string book, string recipe)
    {
        if (Cooking.OwnBooks.TryGetValue(book, out var own)) { if (!own.Contains(recipe)) own.Add(recipe); }
        else if (Game.Book(book)?.Recipes.Contains(recipe, StringComparer.OrdinalIgnoreCase) != true)
        {
            var list = Cooking.Books.TryGetValue(book, out var l) ? l : Cooking.Books[book] = [];
            if (!list.Contains(recipe)) list.Add(recipe);
        }
        ListsDirty = true;
    }

    /// <summary>Removes a recipe the mod put into a book (the game's own recipes of a stock book stay: a mod only adds).</summary>
    public void RemoveFromBook(string book, string recipe)
    {
        if (Cooking.OwnBooks.TryGetValue(book, out var own)) own.Remove(recipe);
        else if (Cooking.Books.TryGetValue(book, out var l))
        {
            l.Remove(recipe);
            if (l.Count == 0) Cooking.Books.Remove(book);
        }
        ListsDirty = true;
    }

    public void AddKnown(string recipe)
    {
        if (!KnownRecipes.Contains(recipe, StringComparer.OrdinalIgnoreCase)) Cooking.KnownByDefault.Add(recipe);
        ListsDirty = true;
    }

    public void RemoveKnown(string recipe)
    {
        Cooking.KnownByDefault.RemoveAll(r => r.Equals(recipe, StringComparison.OrdinalIgnoreCase));
        ListsDirty = true;
    }

    /// <summary>Caption and description of an own book (its _ES texts in modkit.json).</summary>
    public (string Name, string Description) OwnBookTexts(string book)
    {
        var t = Cooking.ItemTexts.GetValueOrDefault($"{BookFolder}/{book}_ES");
        return (t is { Count: > 0 } ? t[0] : book, t is { Count: > 1 } ? t[1] : "");
    }

    public void SetOwnBookTexts(string book, string name, string description)
    {
        Cooking.ItemTexts[$"{BookFolder}/{book}_ES"] = [name, description];
        ListsDirty = true;
    }

    /// <summary>The stock cookbook an own book was cloned from (its cover, model and where it is found).</summary>
    public string? OwnBookTemplate(string book) =>
        Project.Modkit.Clones.FirstOrDefault(c => c.New?.EndsWith("/" + book, StringComparison.OrdinalIgnoreCase) == true)?.Template?.Split('/').Last();

    public void SetOwnBookTemplate(string book, string template)
    {
        var clone = Project.Modkit.Clones.FirstOrDefault(c => c.New?.EndsWith("/" + book, StringComparison.OrdinalIgnoreCase) == true);
        if (clone != null) clone.Template = $"{BookFolder}/{template}";
        ListsDirty = true;
    }

    /// <summary>
    /// A new cookbook of the mod: a clone of a stock book (cover, model, where it is found) with its own name and
    /// recipes. Returns its id: Book_Cooking_&lt;Project&gt;, then _2, _3...
    /// </summary>
    public string CreateOwnBook(string name, string description, string template, IEnumerable<string> recipes)
    {
        var baseId = "Book_Cooking_" + Project.Name;
        var id = baseId;
        for (var n = 2; Cooking.OwnBooks.ContainsKey(id) || Game.Book(id) != null; n++) id = $"{baseId}_{n}";
        Project.Modkit.Clones.Add(new ScumModKit.Build.CloneSpec { Template = $"{BookFolder}/{template}", New = $"{BookFolder}/{id}", With = ["_ES"] });
        Cooking.OwnBooks[id] = [.. recipes];
        Cooking.ItemTexts[$"{BookFolder}/{id}_ES"] = [name, description];
        ListsDirty = true;
        return id;
    }

    // ------------------------------------------------------------------ new recipes

    public const string RecipeFolder = "/Game/ConZ_Files/Cooking/Recipes";

    /// <summary>Names of new recipes, dishes and books: English for now (other languages come with the mod's localization).</summary>
    public static bool IsEnglishName(string? name) =>
        !string.IsNullOrWhiteSpace(name) && name.Length <= 60 && System.Text.RegularExpressions.Regex.IsMatch(name, @"^[A-Za-z0-9 '&()\-,.!]+$");

    /// <summary>"Crazy salad 2" -> "CrazySalad2": the part of the package names that comes from the name.</summary>
    public static string NameKey(string name) =>
        string.Concat(System.Text.RegularExpressions.Regex.Split(name, "[^A-Za-z0-9]+").Where(w => w.Length > 0).Select(w => char.ToUpperInvariant(w[0]) + w[1..]));

    /// <summary>The stock recipe a recipe "from scratch" is cloned from: a simple one with main and optional slots.</summary>
    public string ScratchTemplate() =>
        Game.Recipe("Cook_Dish_Salad_Tomato")?.Id
        ?? Game.Recipes.First(r => !r.Technical && r.Main.Count > 0 && r.Optional.Count > 0 && r.Result != null).Id;

    /// <summary>
    /// A new recipe of the mod with its own dish: clones of the template recipe and of its dish (+ _ES), registered as a new
    /// recipe, named in English, learnable from a cookbook (own or the game's) or known from the start
    /// (<paramref name="learnFrom"/> = book id, or "known"). From scratch: every slot of the template is removed (the
    /// editor hides them), the user adds his own. Returns the recipe id.
    /// </summary>
    public string CreateRecipe(string template, string name, string description, bool fromScratch, string learnFrom, string? icon = null)
    {
        if (!IsEnglishName(name)) throw new ArgumentException("English letters, digits and spaces only.", nameof(name));
        var tpl = Game.Recipe(template) ?? throw new ArgumentException($"No recipe {template}.");
        var dishTemplate = tpl.Result?.Split('.')[0] ?? throw new InvalidOperationException($"{template} makes no dish.");
        var key = NameKey(name);
        var prefix = Project.Name;
        string recipeId = $"Cook_Dish_{prefix}_{key}", dishId = $"Dish_{prefix}_{key}";
        for (var n = 2; Game.Recipe(recipeId) != null || Cooking.NewRecipes.Contains(recipeId, StringComparer.OrdinalIgnoreCase); n++)
            (recipeId, dishId) = ($"Cook_Dish_{prefix}_{key}{n}", $"Dish_{prefix}_{key}{n}");
        var dishPackage = dishTemplate[..(dishTemplate.LastIndexOf('/') + 1)] + dishId;

        Project.Modkit.Clones.Add(new ScumModKit.Build.CloneSpec { Template = dishTemplate, New = dishPackage, With = ["_ES"] });
        Project.Modkit.Clones.Add(new ScumModKit.Build.CloneSpec { Template = $"{RecipeFolder}/{template}", New = $"{RecipeFolder}/{recipeId}" });
        Cooking.NewRecipes.Add(recipeId);
        Cooking.ItemTexts[dishPackage + "_ES"] = [name, description];
        if (learnFrom == "known") AddKnown(recipeId);
        else AddToBook(learnFrom, recipeId);
        if (icon != null) SetIcon(ClassOf(dishPackage), icon);

        var game = GameState(recipeId)!;
        var s = game.Clone();
        s.Name = name;
        s.Description = description;
        s.Result = $"{dishPackage}.{dishId}_C";
        if (fromScratch)
            foreach (var g in Groups.All)
                foreach (var slot in s.Slots[g]) slot.Removed = true;
        _saved[recipeId] = game; // so the new recipe counts as changed until saved
        _states[recipeId] = s;
        ListsDirty = true;
        return recipeId;
    }

    public void DeleteOwnBook(string book)
    {
        Project.Modkit.Clones.RemoveAll(c => c.New?.EndsWith("/" + book, StringComparison.OrdinalIgnoreCase) == true);
        Cooking.OwnBooks.Remove(book);
        Cooking.ItemTexts.Remove($"{BookFolder}/{book}_ES");
        _icons.Remove($"{BookFolder}/{book}_ES");
        ListsDirty = true;
    }

    // ------------------------------------------------------------------ icons of new dishes and own books

    /// <summary>Marks the edits the editor makes for icons (made anew on every save).</summary>
    private const string IconEditor = "icon";

    /// <summary>_ES package of a cloned item -> the icon texture chosen for it (/Game/Folder/T.T).</summary>
    private readonly Dictionary<string, string> _icons = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>/Game/A/B.B_C -> /Game/A/B.</summary>
    public static string PackageOf(string itemClass) => itemClass.Split('.')[0];

    /// <summary>/Game/A/B -> /Game/A/B.B_C.</summary>
    public static string ClassOf(string package) => $"{package}.{package[(package.LastIndexOf('/') + 1)..]}_C";

    /// <summary>The game item a cloned item of the mod copies (its class), or null for anything else.</summary>
    public string? TemplateClass(string? itemClass)
    {
        if (itemClass == null) return null;
        var package = PackageOf(itemClass);
        var t = Project.Modkit.Clones.FirstOrDefault(c => c.New?.Equals(package, StringComparison.OrdinalIgnoreCase) == true)?.Template;
        return t == null ? null : ClassOf(t);
    }

    /// <summary>Icon of an item as the mod shows it: the one chosen in the editor, else its template's (a clone), else the game's.</summary>
    public string? ItemIcon(string? itemClass)
    {
        if (itemClass == null) return null;
        if (_icons.TryGetValue(PackageOf(itemClass) + "_ES", out var icon)) return icon;
        return Game.ItemIcon(TemplateClass(itemClass) ?? itemClass);
    }

    /// <summary>The item (new dish, own book) has an icon of its own, not its template's.</summary>
    public bool HasOwnIcon(string? itemClass) => itemClass != null && _icons.ContainsKey(PackageOf(itemClass) + "_ES");

    /// <summary>
    /// Icon of a cloned item (a new dish, an own book); null or the template's icon = the template's again. In the build
    /// the clone's _ES gets its soft reference to the template's icon renamed (name map: package and object path).
    /// </summary>
    public void SetIcon(string itemClass, string? icon)
    {
        var template = TemplateClass(itemClass) ?? throw new InvalidOperationException($"{itemClass} is not an item of the mod.");
        var key = PackageOf(itemClass) + "_ES";
        if (icon == null || icon.Equals(Game.ItemIcon(template), StringComparison.OrdinalIgnoreCase)) _icons.Remove(key);
        else if (!icon.StartsWith("/Game/", StringComparison.Ordinal) || !icon.Contains('.')) throw new ArgumentException($"Expected /Game/Folder/Texture.Texture: {icon}.");
        else _icons[key] = icon;
        ListsDirty = true;
    }

    /// <summary>{"op": "rename", "editor": "icon", "package": _ES, "exact": {template icon package: new, template icon: new}} per icon.</summary>
    private IEnumerable<JsonNode> IconEdits()
    {
        foreach (var (es, icon) in _icons.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            var old = Game.ItemIcon(TemplateClass(ClassOf(es[..^"_ES".Length])));
            if (old == null || !old.Contains('.')) continue; // the clone is gone, or its template has no icon
            yield return new JsonObject
            {
                ["op"] = "rename", ["editor"] = IconEditor, ["package"] = es,
                ["exact"] = new JsonObject { [PackageOf(old)] = PackageOf(icon), [old] = icon },
            };
        }
    }

    private static (string Package, string Icon)? IconOfEdit(JsonElement e)
    {
        if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty("editor", out var ed) || ed.GetString() != IconEditor) return null;
        if (!e.TryGetProperty("package", out var p) || !e.TryGetProperty("exact", out var x) || x.ValueKind != JsonValueKind.Object) return null;
        var icon = x.EnumerateObject().Select(o => o.Value.GetString()).FirstOrDefault(v => v?.Contains('.') == true);
        return icon == null || p.GetString() is not { } package ? null : (package, icon);
    }

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping, TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver(),
    };

    private static RecipeState Rename(RecipeState s, string id)
    {
        var c = new RecipeState { Id = id, CookingTime = s.CookingTime, CookingTemperature = s.CookingTemperature, Utilities = [.. s.Utilities] };
        foreach (var g in Groups.All) c.Slots[g].AddRange(s.Slots[g].Select(x => x.Clone()));
        return c;
    }

    /// <summary>The state under the base recipe's id with everything else kept (Generate compares against the base).</summary>
    private static RecipeState Renamed(RecipeState s, string id)
    {
        if (s.Id == id) return s;
        var c = Rename(s, id);
        c.Name = s.Name;
        c.Description = s.Description;
        c.Result = s.Result;
        foreach (var (k, v) in s.OtherNumbers) c.OtherNumbers[k] = v;
        return c;
    }
}
