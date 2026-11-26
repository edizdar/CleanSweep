using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace CleanSweep.Core.Models;

public enum AutorunScope
{
    HkcuRun,
    HklmRun,
    HklmWow64Run,
    UserStartupFolder,
    CommonStartupFolder
}

public sealed class AutorunEntry : INotifyPropertyChanged
{
    private bool _isEnabled;

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Command { get; init; } = string.Empty;
    public string ExecutablePath { get; init; } = string.Empty;
    public string Location { get; init; } = string.Empty;
    public AutorunScope Scope { get; init; }
    public string RegistryPath { get; init; } = string.Empty;
    public string FilePath { get; init; } = string.Empty;

    public bool IsEnabled
    {
        get => _isEnabled;
        set
        {
            if (_isEnabled == value) return;
            _isEnabled = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(StatusText));
        }
    }

    public string StatusText => IsEnabled ? "Aktif" : "Devre Dışı";

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
