using CleanSweep.Core.Discovery;
using CleanSweep.Core.Models;
using CleanSweep.Core.Tools;
using Xunit;

namespace CleanSweep.Tests;

public sealed class AppExportAndHealthTests
{
    [Fact]
    public void FormatInstallDate_FormatsYyyyMmDdAndHandlesFallbacks()
    {
        Assert.Equal("26.11.2026", AppExportService.FormatInstallDate("20261126"));
        Assert.Equal("-", AppExportService.FormatInstallDate(null));
        Assert.Equal("-", AppExportService.FormatInstallDate("   "));
        Assert.Equal("2026-11-26", AppExportService.FormatInstallDate("2026-11-26"));
    }

    [Fact]
    public void ExportToCsv_WritesHeaderAndEscapesSpecialCharacters()
    {
        string tempCsv = Path.Combine(Path.GetTempPath(), $"cs_export_{Guid.NewGuid():N}.csv");
        try
        {
            InstalledApp[] apps =
            [
                new InstalledApp
                {
                    Name = "Test, \"App\"",
                    Publisher = "Acme, Inc.",
                    Version = "1.2.3",
                    Source = AppSource.Win32,
                    EstimatedSizeBytes = 2048,
                    InstallDate = "20261126",
                    InstallLocation = @"C:\Program Files\TestApp"
                }
            ];

            AppExportService.ExportToCsv(apps, tempCsv);
            string content = File.ReadAllText(tempCsv);

            Assert.Contains("Program Adı,Üretici / Yayıncı,Sürüm,Kaynak,Boyut,Kurulum Tarihi,Kurulum Yolu", content);
            Assert.Contains("\"Test, \"\"App\"\"\",\"Acme, Inc.\",1.2.3,Win32,2 KB,26.11.2026,C:\\Program Files\\TestApp", content);
        }
        finally
        {
            if (File.Exists(tempCsv)) File.Delete(tempCsv);
        }
    }

    [Fact]
    public void ExportToHtml_IncludesEncodedAppsAndDeveloperAttribution()
    {
        string tempHtml = Path.Combine(Path.GetTempPath(), $"cs_export_{Guid.NewGuid():N}.html");
        try
        {
            InstalledApp[] apps =
            [
                new InstalledApp
                {
                    Name = "App <Script>",
                    Publisher = "Vendor & Co",
                    Version = "2.0.0",
                    Source = AppSource.Msix,
                    EstimatedSizeBytes = 1048576,
                    InstallDate = "20260115",
                    InstallLocation = @"C:\Apps\Demo"
                }
            ];

            AppExportService.ExportToHtml(apps, tempHtml, "0.6.0");
            string html = File.ReadAllText(tempHtml);

            Assert.Contains("App &lt;Script&gt;", html);
            Assert.Contains("Vendor &amp; Co", html);
            Assert.Contains("15.01.2026", html);
            Assert.Contains("Geliştirici: Erol Dizdar", html);
            Assert.Contains("https://www.eroldizdar.tr", html);
        }
        finally
        {
            if (File.Exists(tempHtml)) File.Delete(tempHtml);
        }
    }

    [Fact]
    public void UpdateCheckService_IsNewerVersion_ComparesSemVerCorrectly()
    {
        Assert.True(UpdateCheckService.IsNewerVersion("v0.5.0", "v0.6.0"));
        Assert.True(UpdateCheckService.IsNewerVersion("0.5.0", "1.0.0"));
        Assert.False(UpdateCheckService.IsNewerVersion("v0.6.0", "v0.6.0"));
        Assert.False(UpdateCheckService.IsNewerVersion("v0.6.0", "v0.5.9"));
        Assert.False(UpdateCheckService.IsNewerVersion("invalid", "v0.6.0"));
    }

    [Fact]
    public void SystemHealthService_GetSystemDiskStatus_ReturnsValidDriveInfo()
    {
        var disk = SystemHealthService.GetSystemDiskStatus();

        Assert.False(string.IsNullOrWhiteSpace(disk.DriveName));
        Assert.True(disk.TotalBytes > 0);
        Assert.InRange(disk.UsedPercent, 0.0, 100.0);
        Assert.Contains("Boş:", disk.SummaryText);
    }
}
