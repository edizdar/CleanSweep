using CleanSweep.Core.Startup;
using Xunit;

namespace CleanSweep.Tests;

public sealed class AutorunServiceTests
{
    [Theory]
    [InlineData(null, true)]
    [InlineData(new byte[] { 0x02, 0x00, 0x00, 0x00 }, true)]
    [InlineData(new byte[] { 0x06, 0x00, 0x00, 0x00 }, true)]
    [InlineData(new byte[] { 0x03, 0x00, 0x00, 0x00 }, false)]
    [InlineData(new byte[] { 0x07, 0x00, 0x00, 0x00 }, false)]
    public void IsApprovedEnabled_ParsesWindowsStartupApprovedBytes(byte[]? raw, bool expected)
    {
        Assert.Equal(expected, AutorunService.IsApprovedEnabled(raw));
    }

    [Fact]
    public void GetEntries_ReadsSystemStartupEntriesWithoutThrowing()
    {
        var svc = new AutorunService();
        var entries = svc.GetEntries();
        Assert.NotNull(entries);
    }
}
