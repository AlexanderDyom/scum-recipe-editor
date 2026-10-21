// The installed game and the AES key of its paks; the key is never printed. The game: env SCUM_GAME, else the SCUM
// install found in the Steam libraries. The key: env SCUM_AES_KEY_FILE, else key.txt at the workspace root (git-ignored).
// Without the game or the key the game tests pass without checking anything (see Available); tests of Food_Mod
// (a project of the scum_mod workspace) likewise pass when it is not there (see FoodMod).
using RecipeEditor.Core.Game;
using ScumModKit.Game;

namespace RecipeEditor.Tests;

public sealed class GameFixture : IDisposable
{
    /// <summary>The scum_mod workspace (a folder with Food_Mod and tools), or null outside it.</summary>
    public static readonly string? RepoRoot = FindRepoRoot();

    /// <summary>File with the AES key, or null.</summary>
    public static string? KeyFile =>
        Environment.GetEnvironmentVariable("SCUM_AES_KEY_FILE") is { Length: > 0 } k ? k
        : RepoRoot != null ? Path.Combine(RepoRoot, "key.txt") : null;

    /// <summary>The Food_Mod project of the workspace, or null outside it.</summary>
    public static string? FoodMod => RepoRoot != null ? Path.Combine(RepoRoot, "Food_Mod") : null;

    public GameProvider? Game { get; }
    public GameData? Ru { get; }

    public GameFixture()
    {
        var folder = Environment.GetEnvironmentVariable("SCUM_GAME") ?? GameLocator.FindInstall(InstallKind.Client);
        if (KeyFile is not { } keyFile || !File.Exists(keyFile) || folder == null || GameLocator.ResolvePaks(folder) == null) return;
        Game = GameProvider.Open(folder, File.ReadLines(keyFile).First());
        Ru = GameData.Load(Game, "ru");
    }

    public bool Available => Ru != null;

    /// <summary>The AES key (for builds in tests); only when Available.</summary>
    public string Key => File.ReadLines(KeyFile!).First();

    public void Dispose() => Game?.Dispose();

    private static string? FindRepoRoot()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
            if (Directory.Exists(Path.Combine(d.FullName, "Food_Mod")) && Directory.Exists(Path.Combine(d.FullName, "tools"))) return d.FullName;
        return null;
    }
}

[CollectionDefinition("game")]
public sealed class GameCollection : ICollectionFixture<GameFixture>;
