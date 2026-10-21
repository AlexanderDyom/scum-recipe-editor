// The installed game, read-only: its encrypted stock paks mounted through CUE4Parse with the AES key given as a string
// (the editor keeps the key DPAPI-protected and never writes it in the clear). The key is never printed or logged.
using CUE4Parse.Encryption.Aes;
using CUE4Parse.FileProvider;
using CUE4Parse.UE4.Objects.Core.Misc;
using CUE4Parse.UE4.Versions;
using ScumModKit.Game;

namespace RecipeEditor.Core.Game;

public sealed class GameProvider : IDisposable
{
    public const string RegistryPath = "SCUM/AssetRegistry.bin";

    /// <summary>The game's Paks folder (…\SCUM\Content\Paks).</summary>
    public string Paks { get; }

    /// <summary>Stock paks only (top folder, not ~mods): what the unmodded game sees.</summary>
    public DefaultFileProvider Provider { get; }

    private GameProvider(string paks, DefaultFileProvider provider)
    {
        Paks = paks;
        Provider = provider;
    }

    /// <summary>
    /// Mounts the stock paks of a game folder (install root, Content, or Paks itself) with <paramref name="aesKey"/>
    /// (64 hex digits, optional 0x). InvalidOperationException when the key does not open the paks.
    /// </summary>
    public static GameProvider Open(string gameFolder, string aesKey)
    {
        var paks = GameLocator.ResolvePaks(gameFolder) ?? throw new DirectoryNotFoundException($"No SCUM Paks folder at {gameFolder}.");
        var key = NormalizeKey(aesKey) ?? throw new FormatException("The AES key is empty.");
        var provider = new DefaultFileProvider(paks, SearchOption.TopDirectoryOnly, new VersionContainer(EGame.GAME_UE4_27), StringComparer.OrdinalIgnoreCase);
        try
        {
            provider.Initialize();
            provider.SubmitKey(new FGuid(), new FAesKey(key));
        }
        catch
        {
            provider.Dispose();
            throw;
        }
        if (provider.MountedVfs.Count == 0 || !provider.Files.ContainsKey(RegistryPath))
        {
            provider.Dispose();
            throw new InvalidOperationException("The AES key does not open the game's paks.");
        }
        return new GameProvider(paks, provider);
    }

    /// <summary>"0x" + 64 upper-case hex digits, or null for an empty key; FormatException otherwise (the key is not shown).</summary>
    public static string? NormalizeKey(string? key)
    {
        key = (key ?? "").Trim();
        if (key.Length == 0) return null;
        if (key.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) key = key[2..];
        if (key.Length != 64 || !key.All(Uri.IsHexDigit)) throw new FormatException("The AES key must be 64 hex digits (optionally starting with 0x).");
        return "0x" + key.ToUpperInvariant();
    }

    public void Dispose() => Provider.Dispose();
}
