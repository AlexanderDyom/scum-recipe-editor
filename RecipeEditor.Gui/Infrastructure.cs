// Same approach as ScumRegistryMerger (tools/registry_merger/Gui/Infrastructure.cs): embedded Strings/<code>.json,
// {l:T Key} bindings, settings in %APPDATA% with the AES key DPAPI-protected for the current Windows user.
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Interop;
using System.Windows.Markup;
using RecipeEditor.Core.Game;
using RecipeEditor.Core.Icons;
using RecipeEditor.Core.Project;
using RecipeEditor.Core.Settings;
using ScumModKit.Game;

namespace RecipeEditor.Gui;

/// <summary>UI strings of the current language (embedded Strings/&lt;code&gt;.json, English as fallback).</summary>
public sealed class Loc : INotifyPropertyChanged
{
    /// <summary>Every embedded Strings/&lt;code&gt;.json with its native name ("_name") and machine-translation mark ("_ai").</summary>
    public static (string Code, string Native, bool Ai)[] Languages { get; private set; } = Discover();

    /// <summary>The language list again (after a language file was added to the user folder).</summary>
    public static void Rediscover() => Languages = Discover();

    private static (string Code, string Native, bool Ai)[] Discover()
    {
        var first = new[] { "en", "ru", "uk" };
        var list = new List<(string, string, bool)>();
        foreach (var res in Assembly.GetExecutingAssembly().GetManifestResourceNames())
        {
            var m = System.Text.RegularExpressions.Regex.Match(res, @"\.Strings\.([a-z]{2,3}(-[A-Za-z]{2,4})?)\.json$");
            if (!m.Success) continue;
            var code = m.Groups[1].Value;
            var d = Load(code);
            list.Add((code, d.TryGetValue("_name", out var n) ? n : code, d.TryGetValue("_ai", out var ai) && ai == "true"));
        }
        // new languages that exist only as a user file
        if (Directory.Exists(UserFolder))
            foreach (var f in Directory.EnumerateFiles(UserFolder, "*.json"))
            {
                var code = Path.GetFileNameWithoutExtension(f);
                if (!System.Text.RegularExpressions.Regex.IsMatch(code, "^[a-z]{2,3}(-[A-Za-z]{2,4})?$") || list.Any(l => l.Item1 == code)) continue;
                var d = Load(code);
                list.Add((code, d.TryGetValue("_name", out var n) ? n : code, d.TryGetValue("_ai", out var ai) && ai == "true"));
            }
        return list.OrderBy(l => Array.IndexOf(first, l.Item1) is var i && i >= 0 ? i : first.Length)
            .ThenBy(l => l.Item2, StringComparer.CurrentCulture).ToArray();
    }

    public static bool IsAi(string code) => Languages.Any(l => l.Code == code && l.Ai);

    public static Loc Instance { get; } = new();

    private Dictionary<string, string> _strings = new();
    private readonly Dictionary<string, string> _english = Load("en");

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Code { get; private set; } = "en";
    public CultureInfo Culture { get; private set; } = CultureInfo.GetCultureInfo("en");

    public string this[string key] => _strings.TryGetValue(key, out var s) ? s : _english.TryGetValue(key, out var e) ? e : key;

    public static string T(string key) => Instance[key];
    public static string F(string key, params object[] args) => string.Format(Instance.Culture, Instance[key], args);

    /// <summary>Plural form of a "one|few|many" (or "one|other") string, formatted with n.</summary>
    public static string P(string key, int n)
    {
        var forms = Instance[key].Split('|');
        var n10 = n % 10;
        var n100 = n % 100;
        var i = Instance.Code switch
        {
            "ru" or "uk" or "hr" => n10 == 1 && n100 != 11 ? 0 : n10 is >= 2 and <= 4 && n100 is < 12 or > 14 ? 1 : 2,
            "pl" => n == 1 ? 0 : n10 is >= 2 and <= 4 && n100 is < 12 or > 14 ? 1 : 2,
            "cs" => n == 1 ? 0 : n is >= 2 and <= 4 ? 1 : 2,
            _ => n == 1 ? 0 : 1,
        };
        return string.Format(Instance.Culture, forms[Math.Min(i, forms.Length - 1)], n.ToString("N0", Instance.Culture));
    }

    /// <summary>The system UI language if it is one of ours, else English.</summary>
    public static string SystemLanguage()
    {
        var two = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.ToLowerInvariant();
        two = two switch { "ua" => "uk", "bs" or "sr" => "hr", "sk" => "cs", _ => two };
        return Languages.Any(l => l.Code == two) ? two : "en";
    }

    /// <summary>"auto" (or empty) = system language.</summary>
    public void Set(string? code)
    {
        Code = string.IsNullOrEmpty(code) || code == "auto" ? SystemLanguage() : code;
        _strings = Code == "en" ? _english : Load(Code);
        Culture = CultureInfo.GetCultureInfo(Code);
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
    }

    /// <summary>
    /// Language files of the user (%APPDATA%\ScumRecipeEditor\languages\&lt;code&gt;.json): corrections over the built-in
    /// strings, or a whole new language. "originals" holds copies of the built-in files to start from.
    /// </summary>
    public static string UserFolder => Path.Combine(AppSettings.Folder, "languages");

    /// <summary>Strings of a language: the built-in file with the user's file on top (non-empty values win).</summary>
    private static Dictionary<string, string> Load(string code)
    {
        var d = Embedded(code);
        var user = Path.Combine(UserFolder, code + ".json");
        if (File.Exists(user))
        {
            try
            {
                foreach (var (k, v) in JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(user)) ?? [])
                    if (!string.IsNullOrEmpty(v)) d[k] = v;
            }
            catch (Exception)
            {
                // a broken file: the built-in strings stay
            }
        }
        return d;
    }

    /// <summary>Writes the built-in language files into languages\originals (to copy, fix and load back).</summary>
    public static void ExportOriginals()
    {
        var dir = Path.Combine(UserFolder, "originals");
        Directory.CreateDirectory(dir);
        var asm = Assembly.GetExecutingAssembly();
        foreach (var res in asm.GetManifestResourceNames())
        {
            var m = System.Text.RegularExpressions.Regex.Match(res, @"\.Strings\.([a-z]{2,3}(-[A-Za-z]{2,4})?)\.json$");
            if (!m.Success) continue;
            using var s = asm.GetManifestResourceStream(res)!;
            using var f = File.Create(Path.Combine(dir, m.Groups[1].Value + ".json"));
            s.CopyTo(f);
        }
    }

    /// <summary>
    /// Takes a language file to try it: &lt;code&gt;.json (ru.json, de.json...) of "key": "text" pairs. Returns the language
    /// code and how many strings it has; FormatException for a file that is not one.
    /// </summary>
    public static (string Code, int Count) Import(string file)
    {
        var d = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(file)) ?? throw new FormatException();
        var code = d.TryGetValue("_code", out var c) ? c : Path.GetFileNameWithoutExtension(file).ToLowerInvariant();
        if (!System.Text.RegularExpressions.Regex.IsMatch(code, "^[a-z]{2,3}(-[A-Za-z]{2,4})?$")) throw new FormatException(code);
        Directory.CreateDirectory(UserFolder);
        File.Copy(file, Path.Combine(UserFolder, code + ".json"), true);
        Rediscover();
        return (code, d.Count(kv => !kv.Key.StartsWith('_')));
    }

    private static Dictionary<string, string> Embedded(string code)
    {
        var asm = Assembly.GetExecutingAssembly();
        var name = asm.GetManifestResourceNames().FirstOrDefault(n => n.EndsWith($".Strings.{code}.json", StringComparison.OrdinalIgnoreCase));
        if (name == null) return new();
        using var s = asm.GetManifestResourceStream(name)!;
        return JsonSerializer.Deserialize<Dictionary<string, string>>(s) ?? new();
    }
}

/// <summary>XAML: Text="{l:T Key}" — a live binding, so the text follows a language change.</summary>
public sealed class TExtension(string key) : MarkupExtension
{
    public string Key { get; set; } = key;

    public override object ProvideValue(IServiceProvider serviceProvider) =>
        new Binding($"[{Key}]") { Source = Loc.Instance, Mode = BindingMode.OneWay }.ProvideValue(serviceProvider);
}

/// <summary>The opened game (stock paks with the key) and its recipe data, shared by every window.</summary>
public static class Session
{
    public static GameProvider? Game { get; private set; }
    /// <summary>Recipes, books... in the program's language; loading takes a few seconds.</summary>
    public static Task<GameData>? Data { get; private set; }
    /// <summary>Every food item for the item picker (read after Data, in the background).</summary>
    public static Task<List<ItemInfo>>? Catalog { get; private set; }
    /// <summary>Every other item of the game (the picker's "inedible"), read after the catalog.</summary>
    public static Task<List<ItemInfo>>? Inedible { get; private set; }
    /// <summary>Icons from the game's textures, PNG cache in %LOCALAPPDATA%.</summary>
    public static IconCache? Icons { get; private set; }
    /// <summary>Every texture of the game's item icon folder ("all icons" of the icon picker).</summary>
    public static Task<List<string>>? IconTextures { get; private set; }
    public static string? Paks => Game?.Paks;
    public static bool ServerFound { get; private set; }

    public static event Action? Changed;

    /// <summary>Only the status bar needs redrawing (warning / error counters): the windows keep their data.</summary>
    public static event Action? StatusChanged;

    public static void NotifyChanged() => StatusChanged?.Invoke();

    /// <summary>Takes over an opened game (from the key check) and starts reading its data.</summary>
    public static void Start(GameProvider game)
    {
        if (!ReferenceEquals(Game, game)) Game?.Dispose();
        Game = game;
        Icons = new IconCache(game);
        IconLoader.Clear();
        IconTextures = Task.Run(() => GameData.IconTextures(game));
        ServerFound = GameLocator.FindInstall(InstallKind.Server) != null;
        Reload();
    }

    /// <summary>Re-reads the data (e.g. in another language).</summary>
    public static void Reload()
    {
        if (Game == null) return;
        var game = Game;
        var lang = Loc.Instance.Code;
        Data = Task.Run(() => GameData.Load(game, lang));
        Catalog = Data.ContinueWith(t => t.Result.LoadCatalog(game), TaskContinuationOptions.OnlyOnRanToCompletion);
        var data = Data;
        Inedible = Catalog.ContinueWith(_ => data.Result.LoadInedible(game), TaskContinuationOptions.OnlyOnRanToCompletion);
        Changed?.Invoke();
        Data.ContinueWith(_ => Application.Current.Dispatcher.BeginInvoke(() => Changed?.Invoke()));
    }

    // ------------------------------------------------------------------ ScumRegistryMerger and updates

    /// <summary>The installed ScumRegistryMerger.exe (found or chosen in the settings), or null.</summary>
    public static string? MergerExe { get; private set; }
    /// <summary>Its console part (builds the shared registry for the editor), or null.</summary>
    public static string? MergerCli { get; private set; }
    public static Version? MergerVersion { get; private set; }
    /// <summary>The latest merger release on GitHub (null: not checked, offline or none).</summary>
    public static GitHubRelease? MergerLatest { get; private set; }
    public static DateTime? MergerChecked { get; private set; }
    /// <summary>Problems of this session for the status bar: warnings (e.g. a shared registry with issues), errors.</summary>
    public static int Warnings { get; set; }
    public static int Errors { get; set; }

    /// <summary>Finds the merger again and, when allowed (or asked), checks GitHub for a newer one.</summary>
    public static async void RefreshMerger(bool checkOnline)
    {
        MergerExe = MergerLink.Find(App.Settings.MergerPath);
        MergerVersion = MergerLink.VersionOf(MergerExe);
        MergerCli = MergerLink.FindCli(MergerExe);
        Changed?.Invoke();
        if (!checkOnline) return;
        MergerLatest = await MergerLink.LatestAsync();
        MergerChecked = DateTime.Now;
        Changed?.Invoke();
    }

    public static bool MergerUpdateAvailable => MergerLatest != null && MergerVersion != null && MergerLatest.Version > MergerVersion;

    public static void Close()
    {
        Game?.Dispose();
        Game = null;
        Data = null;
        Catalog = null;
    }
}

/// <summary>Project folders from the settings that still hold a project, plus projects found in the projects root.</summary>
public static class ProjectList
{
    public static List<RecipeProject> Load(AppSettings settings)
    {
        var folders = new List<string>(settings.Projects);
        if (Directory.Exists(settings.NewProjectsRoot))
            folders.AddRange(Directory.EnumerateDirectories(settings.NewProjectsRoot).Where(d => File.Exists(Path.Combine(d, RecipeProject.ModkitFile))));
        var list = new List<RecipeProject>();
        foreach (var f in folders.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try { list.Add(RecipeProject.Open(f)); }
            catch (Exception) { /* moved or broken: not listed */ }
        }
        return list;
    }
}

internal static class Native
{
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    /// <summary>Dark title bar (Windows 10 2004+ / 11).</summary>
    public static void DarkTitleBar(Window w)
    {
        w.SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(w).Handle;
            var on = 1;
            if (DwmSetWindowAttribute(hwnd, 20, ref on, sizeof(int)) != 0) DwmSetWindowAttribute(hwnd, 19, ref on, sizeof(int));
        };
    }
}
