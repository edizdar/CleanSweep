namespace CleanSweep.Core.Models;

public sealed class ScanResult
{
    public InstalledApp TargetApp { get; init; } = new();
    public DateTimeOffset ScannedAt { get; init; } = DateTimeOffset.UtcNow;
    public TimeSpan Duration { get; init; }
    public List<LeftoverItem> Items { get; init; } = [];
    public List<string> Warnings { get; init; } = [];

    public int TotalCount => Items.Count;
    public int SafeCount => Items.Count(i => i.Risk == RiskLevel.Safe);
    public int ModerateCount => Items.Count(i => i.Risk == RiskLevel.Moderate);
    public int CautionCount => Items.Count(i => i.Risk == RiskLevel.Caution);
    public long TotalSizeBytes => Items.Sum(i => i.SizeBytes);
    public string FormattedTotalSize => InstalledApp.FormatBytes(TotalSizeBytes);
}
