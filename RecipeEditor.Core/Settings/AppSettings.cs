// The editor's settings, %APPDATA%\ScumRecipeEditor\settings.json (same approach as ScumRegistryMerger). The AES key
// is stored only DPAPI-protected for the current Windows user (AesKeyProtected); the clear key is never serialized.
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ScumModKit.Game;

namespace RecipeEditor.Core.Settings;

public sealed class AppSettings
{
    public string Language { get; set; } = "auto";
    public InstallKind Kind { get; set; } = InstallKind.Client;
    public string? GameFolder { get; set; }
    public string? AesKeyProtected { get; set; }
    /// <summary>Project folders shown in the projects window (most recently opened first).</summary>
    public List<string> Projects { get; set; } = [];
    /// <summary>Where new projects go; default Documents\SCUM Recipe Editor.</summary>
    public string? ProjectsRoot { get; set; }
    /// <summary>Save the project by itself after every edit (else Ctrl+S / "Save").</summary>
    public bool AutoSave { get; set; }
    /// <summary>First pakchunk number of new projects.</summary>
    public int FirstPakNumber { get; set; } = 901;
    /// <summary>"Install into the game right after the build" is on when the build window opens.</summary>
    public bool InstallByDefault { get; set; } = true;
    /// <summary>ScumRegistryMerger.exe chosen by the user; null = found automatically.</summary>
    public string? MergerPath { get; set; }
    /// <summary>Check for updates of the editor and of ScumRegistryMerger at start.</summary>
    public bool CheckUpdates { get; set; } = true;
    /// <summary>Take the game folder and the AES key from ScumRegistryMerger's settings (one setup for both programs).</summary>
    public bool UseMergerSettings { get; set; } = true;

    /// <summary>
    /// With SCUM Server: show icons and 3D models from the SCUM client's paks (a server's textures are stripped). Only for
    /// the editor's view; the mod and the server are not touched. Null: not asked yet.
    /// </summary>
    public bool? VisualsFromClient { get; set; }

    public static string Folder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ScumRecipeEditor");
    public static string DefaultPath => Path.Combine(Folder, "settings.json");

    [JsonIgnore]
    public string NewProjectsRoot => ProjectsRoot ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "SCUM Recipe Editor");

    /// <summary>The key is required: the editor reads recipes, books and icons straight from the game's paks.</summary>
    [JsonIgnore]
    public bool IsConfigured => GameLocator.ResolvePaks(GameFolder) != null && !string.IsNullOrEmpty(AesKey);

    /// <summary>The key in the clear, for this process only: never serialized (settings.json holds AesKeyProtected).</summary>
    [JsonIgnore]
    public string? AesKey
    {
        get
        {
            if (string.IsNullOrEmpty(AesKeyProtected)) return null;
            try { return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(AesKeyProtected), null, DataProtectionScope.CurrentUser)); }
            catch (Exception) { return null; }
        }
        set => AesKeyProtected = string.IsNullOrWhiteSpace(value)
            ? null
            : Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(value.Trim()), null, DataProtectionScope.CurrentUser));
    }

    public static AppSettings Load(string? path = null)
    {
        path ??= DefaultPath;
        try
        {
            if (File.Exists(path)) return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path)) ?? new();
        }
        catch (Exception)
        {
            // unreadable settings: start over with the setup window
        }
        return new();
    }

    public string ToJson() => JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });

    public void Save(string? path = null)
    {
        path ??= DefaultPath;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, ToJson());
    }

    /// <summary>Puts a project folder first in the list.</summary>
    public void Remember(string folder)
    {
        Projects.RemoveAll(p => string.Equals(Path.GetFullPath(p), Path.GetFullPath(folder), StringComparison.OrdinalIgnoreCase));
        Projects.Insert(0, Path.GetFullPath(folder));
    }

    public AppSettings Clone()
    {
        var c = (AppSettings)MemberwiseClone();
        c.Projects = [.. Projects];
        return c;
    }
}
