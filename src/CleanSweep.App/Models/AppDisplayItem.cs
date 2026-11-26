using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using CleanSweep.Core.Models;

namespace CleanSweep.App.Models;

public sealed class AppDisplayItem : INotifyPropertyChanged
{
    private bool _isChecked;

    public event PropertyChangedEventHandler? PropertyChanged;

    public InstalledApp App { get; }
    public ImageSource? Icon { get; }

    public bool IsChecked
    {
        get => _isChecked;
        set
        {
            if (_isChecked == value) return;
            _isChecked = value;
            OnPropertyChanged();
        }
    }

    public string Name => App.Name;
    public string Publisher => App.Publisher;
    public string Version => App.Version;
    public AppSource Source => App.Source;
    public InstallerType InstallerType => App.InstallerType;
    public string FormattedSize => App.FormattedSize;
    public string InstallLocation => App.InstallLocation;
    public string UninstallString => App.UninstallString;
    public string RegistryKeyPath => App.RegistryKeyPath;

    public AppDisplayItem(InstalledApp app, ImageSource? icon)
    {
        App = app;
        Icon = icon;
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
