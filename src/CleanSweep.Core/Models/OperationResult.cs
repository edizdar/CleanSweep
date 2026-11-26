namespace CleanSweep.Core.Models;

public sealed class ItemExecutionOutcome
{
    public string Target { get; init; } = string.Empty;
    public LeftoverCategory Category { get; init; }
    public bool Succeeded { get; init; }
    public bool ScheduledForReboot { get; init; }
    public string Message { get; init; } = string.Empty;
}

public sealed class OperationResult
{
    public bool Success { get; set; }
    public string Summary { get; set; } = string.Empty;
    public string? QuarantineSessionId { get; set; }
    public List<ItemExecutionOutcome> Outcomes { get; init; } = [];
    public List<string> Errors { get; init; } = [];
}

public sealed class CleanupOptions
{
    public bool BackupToQuarantine { get; init; } = true;
    public bool DryRun { get; init; }
    public bool ScheduleRebootOnLocked { get; init; } = true;
    public bool TerminateLockingProcesses { get; init; }
    public string? TargetInstallLocation { get; init; }
}
