namespace CleanSweep.Core.Models;

public sealed class BrowserExtension
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Browser { get; init; } = string.Empty;
    public string ProfileName { get; init; } = string.Empty;
    public string Version { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string ExtensionPath { get; init; } = string.Empty;
    public bool IsRemovable { get; init; } = true;
}
