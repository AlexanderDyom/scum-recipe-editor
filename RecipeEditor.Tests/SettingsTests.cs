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

public sealed class EditorUpdateTests
{
    /// <summary>Tags with a beta letter (v0.1.1b) are versions; newer = higher numbers, a later letter, or the final one.</summary>
    [Fact]
    public void ComparesBetaVersions()
    {
        Assert.Equal((new Version(0, 1, 1), "b"), EditorUpdate.ParseTag("v0.1.1b"));
        Assert.Equal((new Version(1, 2, 0), ""), EditorUpdate.ParseTag("v1.2.0"));
        Assert.Null(EditorUpdate.ParseTag("latest"));
        Assert.True(EditorUpdate.IsNewer("v0.1.1b", "0.1.0b"));
        Assert.True(EditorUpdate.IsNewer("v0.1.2b", "0.1.1b"));
        Assert.True(EditorUpdate.IsNewer("v0.1.1c", "0.1.1b"));
        Assert.True(EditorUpdate.IsNewer("v0.1.1", "0.1.1b"));
        Assert.False(EditorUpdate.IsNewer("v0.1.1b", "0.1.1b"));
        Assert.False(EditorUpdate.IsNewer("v0.1.0b", "0.1.1b"));
        Assert.False(EditorUpdate.IsNewer("v0.1.1b", "0.1.1"));
    }

    /// <summary>"What's new" once after an update: not on a first install, not again, not after going back to an older exe.</summary>
    [Fact]
    public void WhatsNewOnlyAfterAnUpdate()
    {
        Assert.True(EditorUpdate.ShowWhatsNew(null, "0.1.3b", existingUser: true)); // from 0.1.2b, which stored no version
        Assert.False(EditorUpdate.ShowWhatsNew(null, "0.1.3b", existingUser: false)); // first install
        Assert.True(EditorUpdate.ShowWhatsNew("0.1.3b", "0.1.4b", existingUser: true));
        Assert.False(EditorUpdate.ShowWhatsNew("0.1.3b", "0.1.3b", existingUser: true));
        Assert.False(EditorUpdate.ShowWhatsNew("0.1.4b", "0.1.3b", existingUser: true));
    }

    /// <summary>A release on GitHub comes with its description (needs the internet; offline the test checks nothing).</summary>
    [Fact]
    public async Task ReleaseComesWithItsDescription()
    {
        var release = await EditorUpdate.ReleaseAsync("0.1.2b");
        if (release == null) return;
        Assert.Equal("v0.1.2b", release.Tag);
        Assert.Contains("New in 0.1.2b", release.Notes);
        Assert.NotNull(release.ExeUrl);
        Assert.Null(await EditorUpdate.ReleaseAsync("0.0.1x"));
    }
}
