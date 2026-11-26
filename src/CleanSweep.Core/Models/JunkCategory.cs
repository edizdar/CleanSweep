using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace CleanSweep.Core.Models;

public sealed class JunkCategory : INotifyPropertyChanged
{
    private bool _isSelected = true;
    private int _fileCount;
    private long _totalBytes;

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string Group { get; init; } = "Sistem";
    public bool DefaultSelected { get; init; } = true;
    public List<string> TargetPaths { get; init; } = [];
    public string? FilePattern { get; init; }
    public bool IsRecycleBin { get; init; }

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value) return;
            _isSelected = value;
            OnPropertyChanged();
        }
    }

    public int FileCount
    {
        get => _fileCount;
        set
        {
            if (_fileCount == value) return;
            _fileCount = value;
            OnPropertyChanged();
        }
    }

    public long TotalBytes
    {
        get => _totalBytes;
        set
        {
            if (_totalBytes == value) return;
            _totalBytes = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(FormattedSize));
        }
    }

    public string FormattedSize => TotalBytes <= 0 ? "0 B" : InstalledApp.FormatBytes(TotalBytes);

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed class JunkCleanSummary
{
    public int DeletedFiles { get; set; }
    public long FreedBytes { get; set; }
    public int SkippedLockedFiles { get; set; }
    public List<string> Errors { get; init; } = [];
    public string FormattedFreed => FreedBytes <= 0 ? "0 B" : InstalledApp.FormatBytes(FreedBytes);
}
