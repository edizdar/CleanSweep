using CleanSweep.Core.Browsers;
using CleanSweep.Core.Quarantine;
using Xunit;

namespace CleanSweep.Tests;

public sealed class BrowserExtensionTests : IDisposable
{
    private readonly string _tempRoot;

    public BrowserExtensionTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"CleanSweepBrowserTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempRoot);
    }

    [Fact]
    public void ScanChromiumProfile_ResolvesLocaleMessages_AndRemovesWithQuarantine()
    {
        string userData = Path.Combine(_tempRoot, "UserData");
        string extDir = Path.Combine(userData, "Default", "Extensions", "abcdefg123");
        string verDir = Path.Combine(extDir, "1.2.3");
        string localeDir = Path.Combine(verDir, "_locales", "en");
        Directory.CreateDirectory(localeDir);

        File.WriteAllText(Path.Combine(verDir, "manifest.json"), """
        {
          "name": "__MSG_extName__",
          "version": "1.2.3",
          "description": "Test extension",
          "default_locale": "en"
        }
        """);

        File.WriteAllText(Path.Combine(localeDir, "messages.json"), """
        {
          "extName": { "message": "My Awesome Blocker" }
        }
        """);

        var found = BrowserExtensionService.ScanChromiumUserDataRoot("Google Chrome", userData);
        Assert.Single(found);
        Assert.Equal("My Awesome Blocker", found[0].Name);
        Assert.Equal("1.2.3", found[0].Version);

        var qm = new QuarantineManager(Path.Combine(_tempRoot, "Quarantine"));
        var svc = new BrowserExtensionService();
        var removeRes = svc.RemoveExtension(found[0], qm);

        Assert.True(removeRes.Success);
        Assert.False(Directory.Exists(extDir));
        Assert.False(string.IsNullOrEmpty(removeRes.QuarantineSessionId));

        // Verify quarantine restore brings extension back
        var restoreRes = qm.RestoreSession(removeRes.QuarantineSessionId!);
        Assert.True(restoreRes.Success);
        Assert.True(File.Exists(Path.Combine(verDir, "manifest.json")));
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_tempRoot)) Directory.Delete(_tempRoot, recursive: true); } catch { }
    }
}
