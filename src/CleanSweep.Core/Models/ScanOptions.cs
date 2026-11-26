namespace CleanSweep.Core.Models;

public enum ScanDepth
{
    Safe,
    Moderate,
    Advanced
}

public sealed class ScanOptions
{
    public ScanDepth Depth { get; init; } = ScanDepth.Moderate;
    public bool IsForcedUninstall { get; init; }
}
