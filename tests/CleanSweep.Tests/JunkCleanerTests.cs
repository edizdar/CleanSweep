using CleanSweep.Core.Cleaner;
using CleanSweep.Core.Models;
using Xunit;

namespace CleanSweep.Tests;

public sealed class JunkCleanerTests : IDisposable
{
    private readonly string _tempRoot;

    public JunkCleanerTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"CleanSweepJunkTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempRoot);
    }

    [Fact]
    public void CleanDirectoryContentsSafe_DeletesFilesAndSubdirs()
    {
        string sub = Path.Combine(_tempRoot, "cache");
        Directory.CreateDirectory(sub);
        File.WriteAllText(Path.Combine(_tempRoot, "a.tmp"), "12345");
        File.WriteAllText(Path.Combine(sub, "b.tmp"), "67890");

        var summary = new JunkCleanSummary();
        JunkCleanerService.CleanDirectoryContentsSafe(_tempRoot, "*", recursive: true, summary);

        Assert.Equal(2, summary.DeletedFiles);
        Assert.Equal(10, summary.FreedBytes);
        Assert.Empty(Directory.EnumerateFileSystemEntries(_tempRoot));
    }

    [Fact]
    public async Task ScanAllAsync_CompletesReadOnlyWithoutErrors()
    {
        var service = new JunkCleanerService();
        var categories = await service.ScanAllAsync();

        Assert.True(categories.Count >= 7);
        Assert.Contains(categories, c => c.Id == "user-temp");
        Assert.Contains(categories, c => c.Id == "browser-cache");
    }

    [Fact]
    public async Task ScanCategoriesAsync_OnlySelected_SkipsUncheckedCategoriesAndPreservesSelection()
    {
        string dirSelected = Path.Combine(_tempRoot, "Selected");
        string dirUnselected = Path.Combine(_tempRoot, "Unselected");
        Directory.CreateDirectory(dirSelected);
        Directory.CreateDirectory(dirUnselected);
        File.WriteAllText(Path.Combine(dirSelected, "a.tmp"), "123456");
        File.WriteAllText(Path.Combine(dirUnselected, "b.tmp"), "123456789");

        var cats = new List<JunkCategory>
        {
            new() { Id = "sel", Name = "Sel", IsSelected = true, TargetPaths = [dirSelected] },
            new() { Id = "unsel", Name = "Unsel", IsSelected = false, TargetPaths = [dirUnselected] }
        };

        var service = new JunkCleanerService();
        await service.ScanCategoriesAsync(cats, onlySelected: true);

        Assert.True(cats[0].IsSelected);
        Assert.Equal(1, cats[0].FileCount);
        Assert.Equal(6, cats[0].TotalBytes);

        Assert.False(cats[1].IsSelected);
        Assert.Equal(0, cats[1].FileCount);
        Assert.Equal(0, cats[1].TotalBytes);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_tempRoot)) Directory.Delete(_tempRoot, recursive: true); } catch { }
    }
}
