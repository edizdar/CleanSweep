using CleanSweep.Core.Tracing;
using Xunit;

namespace CleanSweep.Tests;

public sealed class SnapshotDifferTests
{
    [Fact]
    public void ComputeDiff_DetectsAddedDirsFilesAndRegistryKeys_AndIgnoresExisting()
    {
        var before = new SystemSnapshot();
        before.Directories.Add(@"C:\Program Files\ExistingApp");
        before.Files.Add(@"C:\Users\u\Desktop\Existing.lnk");
        before.RegistryKeys.Add(@"HKCU\Software\ExistingApp");

        var after = new SystemSnapshot();
        after.Directories.Add(@"C:\Program Files\ExistingApp");
        after.Directories.Add(@"C:\Program Files\NewApp");
        after.Directories.Add(@"C:\Program Files\NewApp\bin"); // Nested inside NewApp -> should collapse!
        after.Files.Add(@"C:\Users\u\Desktop\Existing.lnk");
        after.Files.Add(@"C:\Users\u\Desktop\NewApp.lnk");
        after.Files.Add(@"C:\Program Files\NewApp\app.exe"); // Under NewApp -> covered by AddedDirectories
        after.RegistryKeys.Add(@"HKCU\Software\ExistingApp");
        after.RegistryKeys.Add(@"HKCU\Software\NewApp");

        var trace = SnapshotDiffer.ComputeDiff("NewApp", @"C:\Temp\setup.exe", before, after);

        Assert.Single(trace.AddedDirectories);
        Assert.Equal(@"C:\Program Files\NewApp", trace.AddedDirectories[0]);

        Assert.Single(trace.AddedFiles);
        Assert.Equal(@"C:\Users\u\Desktop\NewApp.lnk", trace.AddedFiles[0]);

        Assert.Single(trace.AddedRegistryKeys);
        Assert.Equal(@"HKCU\Software\NewApp", trace.AddedRegistryKeys[0]);
    }
}
