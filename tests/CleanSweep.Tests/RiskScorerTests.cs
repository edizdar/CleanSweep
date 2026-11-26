using CleanSweep.Core.Models;
using CleanSweep.Core.Safety;
using Xunit;

namespace CleanSweep.Tests;

public sealed class RiskScorerTests
{
    [Fact]
    public void InstallLocationExact_IsSafeHighConfidenceAndSelected()
    {
        var item = new LeftoverItem { Category = LeftoverCategory.Directory, Path = @"C:\Program Files\FooApp" };
        RiskScorer.ApplyScore(item, MatchStrength.InstallLocationExact);

        Assert.Equal(RiskLevel.Safe, item.Risk);
        Assert.True(item.Confidence >= 95);
        Assert.True(item.IsSelected);
        Assert.False(string.IsNullOrWhiteSpace(item.MatchReason));
    }

    [Fact]
    public void SharedPublisherWithOtherApps_IsDowngradedToCautionAndUnchecked()
    {
        var item = new LeftoverItem { Category = LeftoverCategory.Directory, Path = @"C:\ProgramData\Acme" };
        RiskScorer.ApplyScore(item, MatchStrength.SharedPublisherFolder, hasOtherSamePublisherApps: true);

        Assert.Equal(RiskLevel.Caution, item.Risk);
        Assert.True(item.Confidence < 50);
        Assert.False(item.IsSelected);
    }

    [Fact]
    public void CommunityRule_IsSafeAndSelected()
    {
        var item = new LeftoverItem { Category = LeftoverCategory.Directory, Path = @"C:\Users\u\AppData\Roaming\discord", RuleSource = "discord" };
        RiskScorer.ApplyScore(item, MatchStrength.CommunityRule);

        Assert.Equal(RiskLevel.Safe, item.Risk);
        Assert.Equal(90, item.Confidence);
        Assert.True(item.IsSelected);
    }
}
