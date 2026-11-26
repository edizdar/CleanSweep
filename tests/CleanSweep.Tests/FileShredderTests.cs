using CleanSweep.Core.Cleaner;
using Xunit;

namespace CleanSweep.Tests;

public sealed class FileShredderTests : IDisposable
{
    private readonly string _tempRoot;

    public FileShredderTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"CleanSweepShredTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempRoot);
    }

    [Fact]
    public async Task ShredAsync_Dod3Pass_DestroysFilesAndNestedDirectories()
    {
        string sub = Path.Combine(_tempRoot, "SecretFolder");
        Directory.CreateDirectory(sub);
        string file = Path.Combine(sub, "secret.dat");
        await File.WriteAllBytesAsync(file, new byte[4096]);

        var shredder = new FileShredderService();
        var result = await shredder.ShredAsync([sub], ShredPassMode.Dod3Pass);

        Assert.True(result.Success);
        Assert.False(File.Exists(file));
        Assert.False(Directory.Exists(sub));
    }

    [Fact]
    public async Task ShredAsync_RefusesProtectedWindowsDirectory()
    {
        var shredder = new FileShredderService();
        var result = await shredder.ShredAsync([@"C:\Windows"], ShredPassMode.Fast1Pass);

        Assert.False(result.Success);
        Assert.NotEmpty(result.Errors);
        Assert.True(Directory.Exists(@"C:\Windows"));
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_tempRoot)) Directory.Delete(_tempRoot, recursive: true); } catch { }
    }
}
