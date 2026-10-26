// Updates of the editor itself: its latest GitHub release (tag v0.1.1b: numbers and a letter for the beta), and the
// swap of the running exe for the release's ScumRecipeEditor.exe (rename the running one, start the new one).
using System.Diagnostics;
using System.Text.RegularExpressions;

namespace RecipeEditor.Core.Settings;

public static class EditorUpdate
{
    public const string Repo = "AlexanderDyom/scum-recipe-editor";
    public const string ExeName = "ScumRecipeEditor.exe";

    /// <summary>"v0.1.1b" / "0.1.1" -> (0.1.1, "b"); null when the tag is not a version.</summary>
    public static (Version Version, string Suffix)? ParseTag(string? tag)
    {
        var m = Regex.Match(tag?.Trim() ?? "", @"^[vV]?(\d+)\.(\d+)(?:\.(\d+))?([A-Za-z][A-Za-z0-9.\-]*)?$");
        if (!m.Success) return null;
        var v = new Version(int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value), m.Groups[3].Success ? int.Parse(m.Groups[3].Value) : 0);
        return (v, m.Groups[4].Value.ToLowerInvariant());
    }

    /// <summary>
    /// The release <paramref name="tag"/> is newer than <paramref name="current"/> (both like 0.1.1b): higher numbers, or
    /// the same numbers and a later letter; a version without a letter (the final one) comes after its betas.
    /// </summary>
    public static bool IsNewer(string? tag, string? current)
    {
        if (ParseTag(tag) is not { } t || ParseTag(current) is not { } c) return false;
        var byNumbers = t.Version.CompareTo(c.Version);
        if (byNumbers != 0) return byNumbers > 0;
        if (t.Suffix == c.Suffix) return false;
        if (t.Suffix.Length == 0) return true;
        if (c.Suffix.Length == 0) return false;
        return string.CompareOrdinal(t.Suffix, c.Suffix) > 0;
    }

    /// <summary>The editor's latest release on GitHub (with its exe), or null (offline, none).</summary>
    public static Task<GitHubRelease?> LatestAsync(CancellationToken cancel = default) => MergerLink.LatestAsync(Repo, cancel, ExeName);

    /// <summary>
    /// Downloads the release's exe next to the running one, renames the running exe to *.old.exe (Windows allows that),
    /// puts the new one in its place and starts it. The caller then closes the program.
    /// </summary>
    public static async Task InstallAsync(GitHubRelease release)
    {
        if (release.ExeUrl == null) throw new InvalidOperationException("The release has no " + ExeName + ".");
        var exe = Environment.ProcessPath ?? throw new InvalidOperationException("Unknown program path.");
        var dir = Path.GetDirectoryName(exe)!;
        var name = Path.GetFileNameWithoutExtension(exe);
        var fresh = Path.Combine(dir, name + ".new.exe");
        var old = Path.Combine(dir, name + ".old.exe");
        using (var c = new HttpClient { Timeout = TimeSpan.FromMinutes(10) })
        {
            c.DefaultRequestHeaders.UserAgent.ParseAdd("ScumRecipeEditor");
            await using var s = await c.GetStreamAsync(release.ExeUrl);
            await using var f = File.Create(fresh);
            await s.CopyToAsync(f);
        }
        if (new FileInfo(fresh).Length < 1_000_000) throw new IOException("The downloaded file is too small to be the editor.");
        if (File.Exists(old)) File.Delete(old);
        File.Move(exe, old);
        File.Move(fresh, exe);
        Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true });
    }

    /// <summary>Removes the exe an update left behind (at the next start, when it is no longer running).</summary>
    public static void CleanUp()
    {
        try
        {
            if (Environment.ProcessPath is not { } exe) return;
            var old = Path.Combine(Path.GetDirectoryName(exe)!, Path.GetFileNameWithoutExtension(exe) + ".old.exe");
            if (File.Exists(old)) File.Delete(old);
        }
        catch (Exception)
        {
            // still locked: the next start removes it
        }
    }
}
