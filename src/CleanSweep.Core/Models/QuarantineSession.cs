using System.Text.Json.Serialization;

namespace CleanSweep.Core.Models;

public sealed class QuarantinedFileEntry
{
    public string OriginalPath { get; init; } = string.Empty;
    public string ArchiveEntryPath { get; init; } = string.Empty;
    public bool IsDirectory { get; init; }
    public long SizeBytes { get; init; }
    public string Sha256 { get; init; } = string.Empty;
}

public sealed class QuarantinedRegistryValue
{
    public string Name { get; init; } = string.Empty;
    public string Kind { get; init; } = "String";
    public string Data { get; init; } = string.Empty;
    public string[]? MultiStringData { get; init; }
}

public sealed class QuarantinedRegistryKey
{
    public string FullKeyPath { get; init; } = string.Empty;
    public bool Is32BitView { get; init; }
    public bool WasValueOnly { get; init; }
    public List<QuarantinedRegistryValue> Values { get; init; } = [];
    public List<QuarantinedRegistryKey> SubKeys { get; init; } = [];
}

public sealed class QuarantineSession
{
    public string SessionId { get; init; } = string.Empty;
    public string AppName { get; init; } = string.Empty;
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public List<QuarantinedFileEntry> Files { get; init; } = [];
    public List<QuarantinedRegistryKey> RegistryEntries { get; init; } = [];
    public List<string> Warnings { get; init; } = [];
    public long TotalBackedUpBytes { get; set; }

    [JsonIgnore]
    public string SessionDirectory { get; set; } = string.Empty;

    [JsonIgnore]
    public int TotalItemCount => Files.Count + RegistryEntries.Count;

    [JsonIgnore]
    public string FormattedSize => InstalledApp.FormatBytes(TotalBackedUpBytes);
}
