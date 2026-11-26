using System.Text.Json.Serialization;

namespace CleanSweep.Core.Models;

public sealed class CommunityRule
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("publishers")]
    public List<string> Publishers { get; init; } = [];

    [JsonPropertyName("matchNames")]
    public List<string> MatchNames { get; init; } = [];

    [JsonPropertyName("paths")]
    public List<string> Paths { get; init; } = [];

    [JsonPropertyName("registryKeys")]
    public List<string> RegistryKeys { get; init; } = [];

    [JsonPropertyName("services")]
    public List<string> Services { get; init; } = [];

    [JsonPropertyName("scheduledTasks")]
    public List<string> ScheduledTasks { get; init; } = [];

    [JsonPropertyName("notes")]
    public string Notes { get; init; } = string.Empty;
}
