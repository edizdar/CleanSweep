using CleanSweep.Core.Models;
using CleanSweep.Core.Quarantine;
using CleanSweep.Core.Uninstall;
using Microsoft.Win32;
using Xunit;

namespace CleanSweep.Tests;

public sealed class QuarantineManagerTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly string _quarantineRoot;
    private readonly string _regSubKey;
    private readonly string _regFullPath;

    public QuarantineManagerTests()
    {
        string id = Guid.NewGuid().ToString("N")[..8];
        _tempRoot = Path.Combine(Path.GetTempPath(), $"CleanSweepTest_{id}");
        _quarantineRoot = Path.Combine(_tempRoot, "Quarantine");
        Directory.CreateDirectory(_tempRoot);

        _regSubKey = $@"Software\CleanSweep_UnitTests_{id}";
        _regFullPath = $@"HKCU\{_regSubKey}";
    }

    [Fact]
    public void CreateSession_Delete_AndRestore_RestoresFilesAndTypedRegistryValues()
    {
        // Arrange files
        string appDir = Path.Combine(_tempRoot, "FakeApp");
        string subDir = Path.Combine(appDir, "Sub");
        Directory.CreateDirectory(subDir);
        string file1 = Path.Combine(appDir, "config.txt");
        string file2 = Path.Combine(subDir, "data.bin");
        File.WriteAllText(file1, "CleanSweep Test Content");
        File.WriteAllBytes(file2, [1, 2, 3, 4, 5]);

        // Arrange typed registry key under HKCU
        using (var key = Registry.CurrentUser.CreateSubKey(_regSubKey))
        {
            key.SetValue("StrVal", "Hello", RegistryValueKind.String);
            key.SetValue("DwordVal", 42, RegistryValueKind.DWord);
            key.SetValue("BinVal", new byte[] { 0xAA, 0xBB, 0xCC }, RegistryValueKind.Binary);
            key.SetValue("MultiVal", new[] { "one", "two" }, RegistryValueKind.MultiString);
        }

        var items = new List<LeftoverItem>
        {
            new() { Category = LeftoverCategory.Directory, Path = appDir },
            new() { Category = LeftoverCategory.RegistryKey, Path = _regFullPath }
        };

        var manager = new QuarantineManager(_quarantineRoot);
        var executor = new CleanupExecutor(manager);

        // Act: cleanup with quarantine enabled
        var cleanupRes = executor.ExecuteCleanup("FakeApp", items, new CleanupOptions
        {
            BackupToQuarantine = true,
            DryRun = false
        });

        Assert.True(cleanupRes.Success);
        Assert.False(string.IsNullOrEmpty(cleanupRes.QuarantineSessionId));
        Assert.False(Directory.Exists(appDir));
        Assert.Null(Registry.CurrentUser.OpenSubKey(_regSubKey));

        // Act: restore from quarantine
        var restoreRes = manager.RestoreSession(cleanupRes.QuarantineSessionId!);
        Assert.True(restoreRes.Success);

        // Assert files restored with exact contents
        Assert.True(File.Exists(file1));
        Assert.Equal("CleanSweep Test Content", File.ReadAllText(file1));
        Assert.Equal(new byte[] { 1, 2, 3, 4, 5 }, File.ReadAllBytes(file2));

        // Assert registry values & kinds restored faithfully
        using var restoredKey = Registry.CurrentUser.OpenSubKey(_regSubKey);
        Assert.NotNull(restoredKey);
        Assert.Equal("Hello", restoredKey!.GetValue("StrVal"));
        Assert.Equal(42, restoredKey.GetValue("DwordVal"));
        Assert.Equal(RegistryValueKind.DWord, restoredKey.GetValueKind("DwordVal"));
        Assert.Equal(new byte[] { 0xAA, 0xBB, 0xCC }, (byte[])restoredKey.GetValue("BinVal")!);
        Assert.Equal(new[] { "one", "two" }, (string[])restoredKey.GetValue("MultiVal")!);
    }

    public void Dispose()
    {
        try { Registry.CurrentUser.DeleteSubKeyTree(_regSubKey, throwOnMissingSubKey: false); } catch { }
        try { if (Directory.Exists(_tempRoot)) Directory.Delete(_tempRoot, recursive: true); } catch { }
    }
}
