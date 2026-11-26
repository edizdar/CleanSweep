using CleanSweep.Core.Models;
using CleanSweep.Core.Quarantine;
using CleanSweep.Core.Safety;
using CleanSweep.Core.Scanning;
using CleanSweep.Core.Uninstall;
using Microsoft.Win32;
using Xunit;

namespace CleanSweep.Tests;

public sealed class ForcedUninstallTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly string _uninstallRegSubKey;
    private readonly string _muiValName;
    private const string MuiCacheSubKey = @"Software\Classes\Local Settings\Software\Microsoft\Windows\Shell\MuiCache";

    public ForcedUninstallTests()
    {
        string id = Guid.NewGuid().ToString("N")[..8];
        _tempRoot = Path.Combine(Path.GetTempPath(), $"CleanSweepForcedTest_{id}");
        Directory.CreateDirectory(_tempRoot);
        _uninstallRegSubKey = $@"Software\Microsoft\Windows\CurrentVersion\Uninstall\CleanSweepStubborn_{id}";
        _muiValName = Path.Combine(_tempRoot, "StubbornApp", "stubborn.exe.FriendlyAppName");
    }

    [Theory]
    [InlineData("explorer", true)]
    [InlineData("svchost.exe", true)]
    [InlineData("lsass", true)]
    [InlineData("csrss", true)]
    [InlineData("CleanSweep", true)]
    [InlineData("StubbornDummyApp", false)]
    public void IsProtectedProcessName_BlocksCriticalProcesses(string procName, bool expectedProtected)
    {
        Assert.Equal(expectedProtected, SafetyGuard.IsProtectedProcessName(procName));
    }

    [Fact]
    public void ForcedUninstallScan_PromotesBrokenUninstallKey_AndDetectsMuiCache()
    {
        string appDir = Path.Combine(_tempRoot, "StubbornApp");
        Directory.CreateDirectory(appDir);

        using (var uninstKey = Registry.CurrentUser.CreateSubKey(_uninstallRegSubKey))
        {
            uninstKey.SetValue("DisplayName", "Stubborn Test App");
            uninstKey.SetValue("InstallLocation", appDir);
            uninstKey.SetValue("UninstallString", Path.Combine(appDir, "missing_uninst.exe"));
        }

        using (var muiKey = Registry.CurrentUser.CreateSubKey(MuiCacheSubKey))
        {
            muiKey.SetValue(_muiValName, "Stubborn Test App");
        }

        var target = new InstalledApp
        {
            Id = "forced-test",
            Name = "Stubborn Test App",
            InstallLocation = appDir,
            RegistryKeyPath = $@"HKCU\{_uninstallRegSubKey}"
        };

        var engine = new LeftoverScanEngine();

        // Normal preview scan keeps Uninstall key Moderate & unselected
        var normalScan = engine.Scan(target, [], new ScanOptions { Depth = ScanDepth.Moderate, IsForcedUninstall = false });
        var normalUninst = normalScan.Items.FirstOrDefault(i => i.Path.EndsWith(_uninstallRegSubKey, StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(normalUninst);
        Assert.Equal(RiskLevel.Moderate, normalUninst!.Risk);
        Assert.False(normalUninst.IsSelected);

        // Forced Uninstall Advanced scan promotes Uninstall key to Safe & selected, and finds MuiCache
        var forcedScan = engine.Scan(target, [], new ScanOptions { Depth = ScanDepth.Advanced, IsForcedUninstall = true });
        var forcedUninst = forcedScan.Items.FirstOrDefault(i => i.Path.EndsWith(_uninstallRegSubKey, StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(forcedUninst);
        Assert.Equal(RiskLevel.Safe, forcedUninst!.Risk);
        Assert.True(forcedUninst.Confidence >= 95);
        Assert.True(forcedUninst.IsSelected);

        Assert.Contains(forcedScan.Items, i =>
            i.Category == LeftoverCategory.RegistryValue &&
            string.Equals(i.ValueName, _muiValName, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void CleanupExecutor_DeletesReadOnlyHiddenSystemDirectory_WithQuarantineBackup()
    {
        string stubbornDir = Path.Combine(_tempRoot, "LockedAttrApp");
        Directory.CreateDirectory(stubbornDir);
        string stubbornFile = Path.Combine(stubbornDir, "locked.cfg");
        File.WriteAllText(stubbornFile, "stubborn-data");
        File.SetAttributes(stubbornFile, FileAttributes.ReadOnly | FileAttributes.Hidden | FileAttributes.System);
        File.SetAttributes(stubbornDir, FileAttributes.ReadOnly);

        var qm = new QuarantineManager(Path.Combine(_tempRoot, "Quarantine"));
        var executor = new CleanupExecutor(qm);

        var res = executor.ExecuteCleanup("StubbornAttr",
            [new LeftoverItem { Category = LeftoverCategory.Directory, Path = stubbornDir }],
            new CleanupOptions { BackupToQuarantine = true, TerminateLockingProcesses = false });

        Assert.True(res.Success);
        Assert.False(Directory.Exists(stubbornDir));
        Assert.False(string.IsNullOrEmpty(res.QuarantineSessionId));

        // Restore verifies quarantine fidelity
        var restore = qm.RestoreSession(res.QuarantineSessionId!);
        Assert.True(restore.Success);
        Assert.True(File.Exists(stubbornFile));
        Assert.Equal("stubborn-data", File.ReadAllText(stubbornFile));
    }

    public void Dispose()
    {
        try { Registry.CurrentUser.DeleteSubKeyTree(_uninstallRegSubKey, throwOnMissingSubKey: false); } catch { }
        try
        {
            using var muiKey = Registry.CurrentUser.OpenSubKey(MuiCacheSubKey, writable: true);
            muiKey?.DeleteValue(_muiValName, throwOnMissingValue: false);
        }
        catch { }
        try
        {
            if (Directory.Exists(_tempRoot))
            {
                foreach (string f in Directory.EnumerateFiles(_tempRoot, "*", SearchOption.AllDirectories))
                    try { File.SetAttributes(f, FileAttributes.Normal); } catch { }
                Directory.Delete(_tempRoot, recursive: true);
            }
        }
        catch { }
    }
}
