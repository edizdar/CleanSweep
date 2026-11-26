using CleanSweep.Core.Cleaner;
using CleanSweep.Core.Models;
using CleanSweep.Core.Quarantine;
using CleanSweep.Core.Safety;
using Microsoft.Win32;
using Xunit;

namespace CleanSweep.Tests;

public sealed class RegistryCleanerTests
{
    [Fact]
    public void BuildCategories_ReturnsSevenSafeCategories()
    {
        var categories = RegistryCleanerService.BuildCategories();

        Assert.Equal(7, categories.Count);
        Assert.Contains(categories, c => c.Id == "mui-appcompat" && c.IsSelected && c.Risk == RiskLevel.Safe);
        Assert.Contains(categories, c => c.Id == "broken-uninstall" && c.IsSelected && c.Risk == RiskLevel.Moderate);
        Assert.Contains(categories, c => c.Id == "broken-startup" && c.IsSelected && c.Risk == RiskLevel.Safe);
        Assert.Contains(categories, c => c.Id == "broken-apppaths" && c.IsSelected && c.Risk == RiskLevel.Safe);
        Assert.Contains(categories, c => c.Id == "missing-shareddlls" && c.IsSelected && c.Risk == RiskLevel.Safe);
        Assert.Contains(categories, c => c.Id == "orphaned-firewall" && !c.IsSelected && c.Risk == RiskLevel.Safe);
        Assert.Contains(categories, c => c.Id == "empty-software-keys" && !c.IsSelected && c.Risk == RiskLevel.Moderate);
    }

    [Fact]
    public void IsMissingLocalFile_ValidatesPathsAndProtectsSystemLocations()
    {
        string tempFile = Path.GetTempFileName();
        try
        {
            Assert.False(RegistryCleanerService.IsMissingLocalFile(tempFile));

            string missingInTemp = Path.Combine(Path.GetTempPath(), $"cleansweep_missing_{Guid.NewGuid():N}.exe");
            Assert.True(RegistryCleanerService.IsMissingLocalFile(missingInTemp));

            string missingInWindows = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", $"missing_{Guid.NewGuid():N}.exe");
            Assert.False(RegistryCleanerService.IsMissingLocalFile(missingInWindows));

            Assert.False(RegistryCleanerService.IsMissingLocalFile(@"\\fileserver\share\missing.exe"));
            Assert.False(RegistryCleanerService.IsMissingLocalFile(@"%NONEXISTENT_ENV_12345%\app.exe"));
            Assert.False(RegistryCleanerService.IsMissingLocalFile(@"C:\Program Files\WindowsApps\TestPkg\app.exe"));
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task ScanAsync_WhenAllCategoriesUnselected_SkipsScanning()
    {
        var service = new RegistryCleanerService();
        var cats = RegistryCleanerService.BuildCategories();
        foreach (var c in cats) c.IsSelected = false;

        var results = await service.ScanAsync(cats, onlySelected: true);

        Assert.Empty(results);
        Assert.All(cats, c => Assert.Equal(0, c.FoundCount));
    }

    [Fact]
    public async Task EmptySoftwareKey_ScanCleanAndQuarantineRestore_RoundTrips()
    {
        string testSubKey = $@"Software\CleanSweep_Test_{Guid.NewGuid():N}";
        string emptyChildName = "OrphanEmptyVendor";
        string quarantineDir = Path.Combine(Path.GetTempPath(), $"cs_reg_quarantine_{Guid.NewGuid():N}");

        try
        {
            using (var parent = Registry.CurrentUser.CreateSubKey(testSubKey))
            using (parent.CreateSubKey(emptyChildName))
            {
            }

            var cat = new RegistryCleanCategory
            {
                Id = "empty-software-keys",
                Name = "Boş Yazılım Kayıt Anahtarları",
                Risk = RiskLevel.Moderate,
                IsSelected = true
            };

            var found = RegistryCleanerService.ScanEmptySoftwareKeys(RegistryHive.CurrentUser, testSubKey, cat, CancellationToken.None);
            var targetIssue = Assert.Single(found, i => i.TargetDescription.Equals(emptyChildName, StringComparison.OrdinalIgnoreCase));
            Assert.True(SafetyGuard.IsItemAllowed(targetIssue.ToLeftoverItem()));

            targetIssue.IsSelected = true;
            var qm = new QuarantineManager(quarantineDir);
            var service = new RegistryCleanerService();

            var cleanResult = await service.CleanSelectedAsync([targetIssue], backupToQuarantine: true, dryRun: false, quarantineManager: qm);
            Assert.True(cleanResult.Success, cleanResult.Summary);
            Assert.False(string.IsNullOrWhiteSpace(cleanResult.QuarantineSessionId));

            using (var verifyDeleted = Registry.CurrentUser.OpenSubKey($@"{testSubKey}\{emptyChildName}"))
            {
                Assert.Null(verifyDeleted);
            }

            var restoreOutcome = qm.RestoreSession(cleanResult.QuarantineSessionId!);
            Assert.True(restoreOutcome.Success, restoreOutcome.Summary);

            using (var verifyRestored = Registry.CurrentUser.OpenSubKey($@"{testSubKey}\{emptyChildName}"))
            {
                Assert.NotNull(verifyRestored);
            }
        }
        finally
        {
            try { Registry.CurrentUser.DeleteSubKeyTree(testSubKey, throwOnMissingSubKey: false); } catch { }
            try { if (Directory.Exists(quarantineDir)) Directory.Delete(quarantineDir, recursive: true); } catch { }
        }
    }
}
