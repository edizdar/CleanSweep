using CleanSweep.Core.Startup;
using CleanSweep.Core.Tools;
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

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(2, true)]
    [InlineData(3, false)]
    [InlineData(4, true)]
    public void IsMsixStartupStateEnabled_ParsesStatesCorrectly(int state, bool expected)
    {
        Assert.Equal(expected, AutorunService.IsMsixStartupStateEnabled(state));
    }

    [Fact]
    public void GetEntries_ReadsSystemStartupEntriesWithoutThrowing()
    {
        var svc = new AutorunService();
        var entries = svc.GetEntries();
        Assert.NotNull(entries);
    }

    [Fact]
    public void WindowsToolsCatalog_ContainsAtLeast30Tools()
    {
        var tools = WindowsToolsCatalog.GetAll();
        Assert.True(tools.Count >= 30);
    }
}
