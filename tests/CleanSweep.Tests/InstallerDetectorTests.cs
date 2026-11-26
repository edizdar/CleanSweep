using CleanSweep.Core.Discovery;
using CleanSweep.Core.Models;
using Xunit;

namespace CleanSweep.Tests;

public sealed class InstallerDetectorTests
{
    [Fact]
    public void DetectsMsiAndGeneratesSilentCommand()
    {
        var (type, quiet) = Win32RegistryProvider.DetectInstallerAndQuietCommand(
            "MsiExec.exe /I{12345678-1234-1234-1234-1234567890AB}",
            string.Empty,
            isWindowsInstaller: true,
            installLocation: string.Empty);

        Assert.Equal(InstallerType.Msi, type);
        Assert.Equal("msiexec.exe /x {12345678-1234-1234-1234-1234567890AB} /qn /norestart", quiet);
    }

    [Fact]
    public void DetectsInnoSetupAndGeneratesVerySilentCommand()
    {
        var (type, quiet) = Win32RegistryProvider.DetectInstallerAndQuietCommand(
            "\"C:\\Program Files\\TestApp\\unins000.exe\"",
            string.Empty,
            isWindowsInstaller: false,
            installLocation: @"C:\Program Files\TestApp");

        Assert.Equal(InstallerType.InnoSetup, type);
        Assert.Contains("/VERYSILENT", quiet);
    }

    [Fact]
    public void DetectsSquirrelAndGeneratesQuietCommand()
    {
        var (type, quiet) = Win32RegistryProvider.DetectInstallerAndQuietCommand(
            "\"C:\\Users\\u\\AppData\\Local\\Discord\\Update.exe\" --uninstall",
            string.Empty,
            isWindowsInstaller: false,
            installLocation: string.Empty);

        Assert.Equal(InstallerType.Squirrel, type);
        Assert.Contains("--uninstall -s", quiet);
    }

    [Fact]
    public void DetectsNsisAndGeneratesSlashS()
    {
        var (type, quiet) = Win32RegistryProvider.DetectInstallerAndQuietCommand(
            "\"C:\\Program Files\\MyApp\\uninstall.exe\"",
            string.Empty,
            isWindowsInstaller: false,
            installLocation: string.Empty);

        Assert.Equal(InstallerType.Nsis, type);
        Assert.EndsWith("/S", quiet);
    }
}
