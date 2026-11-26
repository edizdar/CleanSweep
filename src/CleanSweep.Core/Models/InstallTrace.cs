namespace CleanSweep.Core.Models;

public sealed class InstallTrace
{
    public string TraceId { get; init; } = Guid.NewGuid().ToString("N")[..12];
    public string TraceName { get; init; } = string.Empty;
    public string? InstallerPath { get; init; }
    public DateTimeOffset StartedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset CompletedAt { get; set; } = DateTimeOffset.UtcNow;
    public List<string> AddedDirectories { get; init; } = [];
    public List<string> AddedFiles { get; init; } = [];
    public List<string> AddedRegistryKeys { get; init; } = [];
    public List<TracedRegistryValue> AddedRegistryValues { get; init; } = [];

    public int TotalChanges =>
        AddedDirectories.Count + AddedFiles.Count + AddedRegistryKeys.Count + AddedRegistryValues.Count;
}

public sealed class TracedRegistryValue
{
    public string KeyPath { get; init; } = string.Empty;
    public string ValueName { get; init; } = string.Empty;
}
