// The editor and ScumRegistryMerger work together: the editor finds the installed merger, shows its version and the
// latest one on GitHub, can update it, and can take the game folder and AES key from the merger's settings. The shared
// registry is built by the installed program (players download it from GitHub), never by a copy inside the editor.
using System.Diagnostics;
using System.Net.Http;
using System.Text.Json;

namespace RecipeEditor.Core.Settings;

public sealed record GitHubRelease(Version Version, string Tag, string PageUrl, string? ExeUrl, DateTime Published, string? CliUrl = null);

public static class MergerLink
{
    public const string Repo = "AlexanderDyom/scum-registry-merger";
    public const string PageUrl = "https://github.com/" + Repo;
    public const string ExeName = "ScumRegistryMerger.exe";

    /// <summary>The merger's settings file (its game folder and DPAPI-protected key, same Windows user).</summary>
    public static string SettingsPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ScumRegistryMerger", "settings.json");

    /// <summary>
    /// The installed ScumRegistryMerger.exe: the chosen path, else next to the editor or in a parent folder (a repo
    /// checkout keeps it in tools\registry_merger\publish), else the usual install folders. Null when not found.
    /// </summary>
    public static string? Find(string? chosen)
    {
        if (!string.IsNullOrEmpty(chosen) && File.Exists(chosen)) return Path.GetFullPath(chosen);
        var candidates = new List<string>();
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
        {
            candidates.Add(Path.Combine(d.FullName, ExeName));
            candidates.Add(Path.Combine(d.FullName, "ScumRegistryMerger", ExeName));
            candidates.Add(Path.Combine(d.FullName, "tools", "registry_merger", "publish", ExeName));
        }
        foreach (var root in new[] { Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.Desktop })
        {
            var r = Environment.GetFolderPath(root);
            candidates.Add(Path.Combine(r, "Programs", "ScumRegistryMerger", ExeName));
            candidates.Add(Path.Combine(r, "ScumRegistryMerger", ExeName));
            candidates.Add(Path.Combine(r, ExeName));
        }
        return candidates.FirstOrDefault(File.Exists);
    }

    public const string CliName = "ScumRegistryMerger.Cli.exe";

    /// <summary>Where "Download" installs it (no admin rights needed).</summary>
    public static string InstallFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "ScumRegistryMerger");

    /// <summary>
    /// The console part that builds the registry for the editor: next to the window app (as the release ships them),
    /// or, in a repository checkout, tools\registry_merger\Cli\bin\Release\net8.0.
    /// </summary>
    public static string? FindCli(string? guiExe)
    {
        var candidates = new List<string>();
        if (guiExe != null) candidates.Add(Path.Combine(Path.GetDirectoryName(guiExe)!, CliName));
        candidates.Add(Path.Combine(InstallFolder, CliName));
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
            candidates.Add(Path.Combine(d.FullName, "tools", "registry_merger", "Cli", "bin", "Release", "net8.0", CliName));
        if (guiExe != null)
            for (var d = new DirectoryInfo(Path.GetDirectoryName(guiExe)!); d != null; d = d.Parent)
                candidates.Add(Path.Combine(d.FullName, "Cli", "bin", "Release", "net8.0", CliName));
        return candidates.FirstOrDefault(File.Exists);
    }

    /// <summary>
    /// Downloads the latest release (window app and console part) into <see cref="InstallFolder"/>, or over an installed
    /// copy. Returns the window app's path.
    /// </summary>
    public static async Task<string> InstallAsync(GitHubRelease release, string? overExe = null, IProgress<string>? log = null)
    {
        var folder = overExe != null ? Path.GetDirectoryName(overExe)! : InstallFolder;
        Directory.CreateDirectory(folder);
        using var c = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd("ScumRecipeEditor");
        foreach (var (name, url) in new[] { (ExeName, release.ExeUrl), (CliName, release.CliUrl) })
        {
            if (url == null) continue;
            log?.Report(name);
            var target = Path.Combine(folder, name);
            var fresh = target + ".new";
            await using (var s = await c.GetStreamAsync(url))
            await using (var f = File.Create(fresh))
                await s.CopyToAsync(f);
            File.Move(fresh, target, true);
        }
        return Path.Combine(folder, ExeName);
    }

    /// <summary>Version of an installed merger (file version), or null.</summary>
    public static Version? VersionOf(string? exe)
    {
        if (exe == null || !File.Exists(exe)) return null;
        var v = FileVersionInfo.GetVersionInfo(exe);
        return new Version(v.FileMajorPart, v.FileMinorPart, Math.Max(v.FileBuildPart, 0));
    }

    /// <summary>The latest release on GitHub, or null (offline, no release).</summary>
    public static async Task<GitHubRelease?> LatestAsync(string repo = Repo, CancellationToken cancel = default, string exeName = ExeName)
    {
        try
        {
            using var c = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
            c.DefaultRequestHeaders.UserAgent.ParseAdd("ScumRecipeEditor");
            using var resp = await c.GetAsync($"https://api.github.com/repos/{repo}/releases/latest", cancel);
            if (!resp.IsSuccessStatusCode) return null;
            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(cancel));
            var root = doc.RootElement;
            var tag = root.GetProperty("tag_name").GetString() ?? "";
            if (EditorUpdate.ParseTag(tag) is not { } parsed) return null; // v1.2.0, v0.1.1b
            var v = parsed.Version;
            string? exe = null, cli = null;
            foreach (var a in root.GetProperty("assets").EnumerateArray())
            {
                var name = a.GetProperty("name").GetString() ?? "";
                if (name.Equals(exeName, StringComparison.OrdinalIgnoreCase)) exe = a.GetProperty("browser_download_url").GetString();
                if (name.Equals(CliName, StringComparison.OrdinalIgnoreCase)) cli = a.GetProperty("browser_download_url").GetString();
            }
            var published = root.TryGetProperty("published_at", out var p) && p.TryGetDateTime(out var d) ? d : DateTime.MinValue;
            return new GitHubRelease(new Version(v.Major, v.Minor, Math.Max(v.Build, 0)), tag,
                root.GetProperty("html_url").GetString() ?? $"https://github.com/{repo}/releases", exe, published, cli);
        }
        catch (Exception) when (!cancel.IsCancellationRequested)
        {
            return null;
        }
    }

    /// <summary>Game folder and AES key from ScumRegistryMerger's settings (DPAPI, the same Windows user); nulls when absent.</summary>
    public static (string? GameFolder, string? AesKey) ReadMergerSettings()
    {
        try
        {
            if (!File.Exists(SettingsPath)) return (null, null);
            var s = AppSettings.Load(SettingsPath); // same field names: GameFolder, AesKeyProtected
            return (s.GameFolder, s.AesKey);
        }
        catch (Exception)
        {
            return (null, null);
        }
    }
}
