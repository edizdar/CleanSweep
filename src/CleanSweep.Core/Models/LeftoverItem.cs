namespace CleanSweep.Core.Models;

public enum LeftoverCategory
{
    File,
    Directory,
    RegistryKey,
    RegistryValue,
    Service,
    ScheduledTask,
    EnvironmentPath,
    FirewallRule
}

public enum RiskLevel
{
    Safe,
    Moderate,
    Caution
}

public sealed class LockingProcessInfo
{
    public int Pid { get; init; }
    public string ProcessName { get; init; } = string.Empty;
    public string AppName { get; init; } = string.Empty;
}

public sealed class LeftoverItem
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public LeftoverCategory Category { get; init; }
    public string Path { get; init; } = string.Empty;
    public string? ValueName { get; init; }
    public long SizeBytes { get; set; }
    public int Confidence { get; set; }
    public RiskLevel Risk { get; set; }
    public string MatchReason { get; set; } = string.Empty;
    public string? RuleSource { get; set; }
    public bool IsSelected { get; set; }
    public List<LockingProcessInfo> LockingProcesses { get; init; } = [];

    public string DisplayPath => string.IsNullOrEmpty(ValueName) ? Path : $"{Path} [{ValueName}]";
    public string FormattedSize => InstalledApp.FormatBytes(SizeBytes);
    public string LockingSummary => LockingProcesses.Count == 0
        ? string.Empty
        : string.Join(", ", LockingProcesses.Select(p => $"{p.ProcessName} ({p.Pid})"));
}
