using RecipeEditor.Core.Settings;

namespace RecipeEditor.Tests;

public sealed class SettingsTests
{
    [Fact]
    public void KeyIsStoredOnlyProtected()
    {
        var key = "0x" + string.Concat(Enumerable.Range(0, 32).Select(i => (i * 7 % 256).ToString("X2")));
        var s = new AppSettings { AesKey = key };
        var json = s.ToJson();
        Assert.DoesNotContain(key[2..], json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\"AesKey\"", json);
        Assert.Contains("\"AesKeyProtected\"", json);

        var path = Path.Combine(Path.GetTempPath(), "RecipeEditorTests", Guid.NewGuid().ToString("N"), "settings.json");
        try
        {
            s.Save(path);
            Assert.Equal(key, AppSettings.Load(path).AesKey);
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(path)!, true);
        }
    }
}
