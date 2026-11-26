using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace CleanSweep.Core.Models;

public sealed class RegistryCleanCategory : INotifyPropertyChanged
{
    private bool _isSelected;
    private int _foundCount;

    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public bool DefaultSelected { get; init; }
    public RiskLevel Risk { get; init; } = RiskLevel.Safe;

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected != value)
            {
                _isSelected = value;
                OnPropertyChanged();
            }
        }
    }

    public int FoundCount
    {
        get => _foundCount;
        set
        {
            if (_foundCount != value)
            {
                _foundCount = value;
                OnPropertyChanged();
            }
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed class RegistryIssueItem : INotifyPropertyChanged
{
    private bool _isSelected;

    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public string CategoryId { get; init; } = string.Empty;
    public string CategoryName { get; init; } = string.Empty;
    public LeftoverCategory ItemType { get; init; } = LeftoverCategory.RegistryValue;
    public string RegistryPath { get; init; } = string.Empty;
    public string? ValueName { get; init; }
    public string TargetDescription { get; init; } = string.Empty;
    public string Reason { get; init; } = string.Empty;
    public RiskLevel Risk { get; init; } = RiskLevel.Safe;

    public string DisplayPath => string.IsNullOrEmpty(ValueName)
        ? RegistryPath
        : $"{RegistryPath} [{ValueName}]";

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected != value)
            {
                _isSelected = value;
                OnPropertyChanged();
            }
        }
    }

    public LeftoverItem ToLeftoverItem() => new()
    {
        Id = Id,
        Category = ItemType,
        Path = RegistryPath,
        ValueName = ValueName,
        SizeBytes = 0,
        Confidence = Risk == RiskLevel.Safe ? 95 : 80,
        Risk = Risk,
        MatchReason = Reason,
        RuleSource = $"RegistryCleaner:{CategoryId}",
        IsSelected = IsSelected
    };

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
