// A project = a mod = a folder with modkit.json (ScumModKit.Build.ModProject, the file modkit build reads) and
// editor.json (what only the editor needs: description, dates). The project name is the mod name and gives the pak
// name pakchunkNNN-<Name>_P.
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using ScumModKit.Build;

namespace RecipeEditor.Core.Project;

/// <summary>editor.json: data of the editor's UI, never read by modkit.</summary>
public sealed class EditorState
{
    public string? Description { get; set; }
    public DateTime Created { get; set; } = DateTime.Now;
    /// <summary>Recipe selected when the project was closed.</summary>
    public string? SelectedRecipe { get; set; }
}

public sealed partial class RecipeProject
{
    public const string ModkitFile = "modkit.json";
    public const string EditorFile = "editor.json";
    /// <summary>Mod paks of our projects start at pakchunk901 (900 = ScumStudio map exports, 999 = the shared registry).</summary>
    public const int FirstNumber = 901;

    public string Folder { get; }
    public ModProject Modkit { get; private set; }

    /// <summary>Reads modkit.json again (after the editor saved it).</summary>
    public void Reload() => Modkit = ModProject.Load(ModkitPath);
    public EditorState Editor { get; }

    /// <summary>
    /// A project the editor did not create (no editor.json, e.g. Food_Mod built by hand) is opened read-only until the
    /// editor can save every kind of edit it holds (stage 2).
    /// </summary>
    public bool ReadOnly { get; }

    public string ModkitPath => Path.Combine(Folder, ModkitFile);
    public string Name => Modkit.Name ?? Path.GetFileName(Folder);
    /// <summary>pakchunk901-FoodMod_P (without .pak).</summary>
    public string Pak => Modkit.Pak ?? PakName(Name, FirstNumber);
    public int? Number => PakNumber(Pak);

    /// <summary>Recipes the mod adds (cooking.newRecipes).</summary>
    public IReadOnlyList<string> NewRecipes => Modkit.Cooking?.NewRecipes ?? [];
    /// <summary>Stock recipes the mod edits.</summary>
    public IEnumerable<string> ChangedRecipes => (Modkit.Cooking?.Recipes.Keys ?? Enumerable.Empty<string>())
        .Where(r => !NewRecipes.Contains(r, StringComparer.OrdinalIgnoreCase));
    public IReadOnlyCollection<string> OwnBooks => Modkit.Cooking?.OwnBooks.Keys ?? (IReadOnlyCollection<string>)[];

    private RecipeProject(string folder, ModProject modkit, EditorState editor, bool readOnly)
    {
        Folder = folder;
        Modkit = modkit;
        Editor = editor;
        ReadOnly = readOnly;
    }

    /// <summary>Opens a project folder (or its modkit.json).</summary>
    public static RecipeProject Open(string folderOrFile)
    {
        var path = Path.GetFullPath(folderOrFile);
        var folder = File.Exists(path) ? Path.GetDirectoryName(path)! : path;
        var modkitPath = Path.Combine(folder, ModkitFile);
        if (!File.Exists(modkitPath)) throw new FileNotFoundException($"No {ModkitFile} in {folder}.", modkitPath);
        var modkit = ModProject.Load(modkitPath);
        var editorPath = Path.Combine(folder, EditorFile);
        EditorState? editor = null;
        if (File.Exists(editorPath))
        {
            try { editor = JsonSerializer.Deserialize<EditorState>(File.ReadAllText(editorPath), Json); }
            catch (JsonException) { /* broken editor.json: the project itself is modkit.json */ }
        }
        return new RecipeProject(folder, modkit, editor ?? new EditorState(), readOnly: !File.Exists(editorPath));
    }

    /// <summary>Creates &lt;parent&gt;\&lt;name&gt; with an empty modkit.json (no edits) and editor.json.</summary>
    public static RecipeProject Create(string folder, string name, int number, string? description)
    {
        if (!IsValidName(name)) throw new ArgumentException("Latin letters, digits and _ only.", nameof(name));
        folder = Path.GetFullPath(folder);
        if (File.Exists(Path.Combine(folder, ModkitFile))) throw new IOException($"{folder} already holds a project.");
        Directory.CreateDirectory(folder);
        var modkit = new JsonObject
        {
            ["about"] = "SCUM Recipe Editor project (modkit build reads this file).",
            ["name"] = name,
            ["pak"] = PakName(name, number),
            ["stage"] = "build/stage",
            ["clones"] = new JsonArray(),
            ["cooking"] = new JsonObject
            {
                ["recipes"] = new JsonObject(), ["itemTexts"] = new JsonObject(), ["newRecipes"] = new JsonArray(),
                ["books"] = new JsonObject(), ["ownBooks"] = new JsonObject(), ["knownByDefault"] = new JsonArray(),
            },
            ["edits"] = new JsonArray(),
        };
        File.WriteAllText(Path.Combine(folder, ModkitFile), modkit.ToJsonString(Json) + "\n");
        var editor = new EditorState { Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim() };
        File.WriteAllText(Path.Combine(folder, EditorFile), JsonSerializer.Serialize(editor, Json) + "\n");
        return Open(folder);
    }

    /// <summary>The pak in ~mods (installed build), or null.</summary>
    public FileInfo? InstalledPak(string? paks)
    {
        if (paks == null) return null;
        var f = new FileInfo(Path.Combine(paks, "~mods", Pak + ".pak"));
        return f.Exists ? f : null;
    }

    public static bool IsValidName(string? name) => !string.IsNullOrEmpty(name) && name.Length <= 40 && NameRegex().IsMatch(name);

    public static string PakName(string name, int number) => $"pakchunk{number}-{name}_P";

    public static int? PakNumber(string? pak) =>
        pak != null && PakRegex().Match(Path.GetFileName(pak)) is { Success: true } m ? int.Parse(m.Groups[1].Value) : null;

    /// <summary>
    /// First pak number ≥ 901 used neither by a pak in ~mods (pakchunkNNN-…) nor by another project. 999 is skipped (the
    /// shared registry).
    /// </summary>
    public static int FirstFreeNumber(string? paks, IEnumerable<RecipeProject> projects, int start = FirstNumber)
    {
        var used = new HashSet<int>(projects.Select(p => p.Number).Where(n => n != null).Select(n => n!.Value));
        var mods = paks == null ? null : Path.Combine(paks, "~mods");
        if (mods != null && Directory.Exists(mods))
            foreach (var f in Directory.EnumerateFiles(mods, "*.pak"))
                if (PakNumber(f) is { } n) used.Add(n);
        var number = Math.Max(start, 1);
        while (used.Contains(number) || number == 999) number++;
        return number;
    }

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    [GeneratedRegex("^[A-Za-z0-9_]+$")]
    private static partial Regex NameRegex();

    [GeneratedRegex(@"^pakchunk(\d+)-", RegexOptions.IgnoreCase)]
    private static partial Regex PakRegex();
}
