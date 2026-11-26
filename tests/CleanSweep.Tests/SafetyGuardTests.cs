using CleanSweep.Core.Models;
using CleanSweep.Core.Safety;
using Xunit;

namespace CleanSweep.Tests;

public sealed class SafetyGuardTests
{
    [Theory]
    [InlineData(@"C:\")]
    [InlineData(@"C:\Windows")]
    [InlineData(@"C:\Windows\System32")]
    [InlineData(@"C:\Program Files")]
    [InlineData(@"C:\Program Files (x86)")]
    [InlineData(@"C:\ProgramData")]
    [InlineData(@"C:\Users")]
    [InlineData(@"C:\Program Files\Microsoft")]
    public void ProtectedFilePaths_AreStrictlyBlocked(string path)
    {
        Assert.True(SafetyGuard.IsProtectedFilePath(path));
    }

    [Theory]
    [InlineData(@"C:\Program Files\MyCustomApp")]
    [InlineData(@"C:\Users\Test\AppData\Local\Discord")]
    public void SpecificAppPaths_AreAllowed(string path)
    {
        Assert.False(SafetyGuard.IsProtectedFilePath(path));
    }

    [Theory]
    [InlineData(@"HKLM")]
    [InlineData(@"HKLM\SOFTWARE")]
    [InlineData(@"HKLM\SOFTWARE\Microsoft")]
    [InlineData(@"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Run")]
    [InlineData(@"HKCU\Software\Microsoft")]
    [InlineData(@"HKLM\SYSTEM\CurrentControlSet\Services")]
    public void ProtectedRegistryKeys_AreStrictlyBlocked(string regPath)
    {
        Assert.True(SafetyGuard.IsProtectedRegistryPath(regPath, isValueTarget: false));
    }

    [Fact]
    public void SpecificRegistryValueUnderRun_IsAllowed_WhileKeyItselfIsBlocked()
    {
        const string runKey = @"HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
        Assert.True(SafetyGuard.IsProtectedRegistryPath(runKey, isValueTarget: false));
        Assert.False(SafetyGuard.IsProtectedRegistryPath(runKey, isValueTarget: true));

        var valueItem = new LeftoverItem
        {
            Category = LeftoverCategory.RegistryValue,
            Path = runKey,
            ValueName = "MyDummyApp"
        };
        Assert.True(SafetyGuard.IsItemAllowed(valueItem));
    }

    [Fact]
    public void WindowsAppsPackageBinaryDir_IsBlocked_WhileUserPackagesSubdirIsAllowed()
    {
        string windowsApps = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "WindowsApps", "Microsoft.OutlookForWindows_1.0");
        string userPkg = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Packages", "Microsoft.OutlookForWindows_8wekyb3d8bbwe");

        Assert.True(SafetyGuard.IsProtectedFilePath(windowsApps));
        Assert.False(SafetyGuard.IsProtectedFilePath(userPkg));
    }

    [Fact]
    public void GetOtherAppsFromSamePublisher_DetectsSharedPublisherCollisions()
    {
        var app1 = new InstalledApp { Id = "1", Name = "App One", Publisher = "Acme Corporation" };
        var app2 = new InstalledApp { Id = "2", Name = "App Two", Publisher = "Acme Corp." };
        var app3 = new InstalledApp { Id = "3", Name = "Other App", Publisher = "Other LLC" };

        var others = SafetyGuard.GetOtherAppsFromSamePublisher(app1, [app1, app2, app3]);
        Assert.Single(others);
        Assert.Equal("2", others[0].Id);
    }
}
