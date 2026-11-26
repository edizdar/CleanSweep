using System.IO;
using System.Windows;
using CleanSweep.Core.Models;
using Microsoft.Win32;

namespace CleanSweep.App.Views;

public partial class ForcedUninstallWindow : Window
{
    private readonly InstalledApp? _prefilled;

    public InstalledApp? TargetApp { get; private set; }
    public ScanDepth SelectedDepth { get; private set; } = ScanDepth.Advanced;
    public bool TerminateProcesses { get; private set; } = true;

    public ForcedUninstallWindow(InstalledApp? prefilledApp = null)
    {
        InitializeComponent();
        _prefilled = prefilledApp;

        if (_prefilled != null)
        {
            TxtAppName.Text = _prefilled.Name;
            TxtFolder.Text = _prefilled.InstallLocation;
            if (!string.IsNullOrWhiteSpace(_prefilled.DisplayIcon))
                TxtExe.Text = _prefilled.DisplayIcon.Split(',')[0].Trim().Trim('"');
        }
    }

    private void PickFolder_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog { Title = "Uygulama Kurulum Klasörünü Seçin" };
        if (dlg.ShowDialog() == true)
        {
            TxtFolder.Text = dlg.FolderName;
            if (string.IsNullOrWhiteSpace(TxtAppName.Text))
                TxtAppName.Text = Path.GetFileName(dlg.FolderName);
        }
    }

    private void PickExe_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = "Uygulama .exe Dosyasını Seçin",
            Filter = "Uygulamalar (*.exe)|*.exe|Tüm Dosyalar (*.*)|*.*"
        };
        if (dlg.ShowDialog() == true)
        {
            TxtExe.Text = dlg.FileName;
            if (string.IsNullOrWhiteSpace(TxtFolder.Text))
                TxtFolder.Text = Path.GetDirectoryName(dlg.FileName) ?? string.Empty;
            if (string.IsNullOrWhiteSpace(TxtAppName.Text))
                TxtAppName.Text = Path.GetFileNameWithoutExtension(dlg.FileName);
        }
    }

    private void Scan_Click(object sender, RoutedEventArgs e)
    {
        string name = TxtAppName.Text.Trim();
        string folder = TxtFolder.Text.Trim();

        if (string.IsNullOrWhiteSpace(name) && string.IsNullOrWhiteSpace(folder))
        {
            MessageBox.Show("Lütfen en az bir uygulama adı veya kurulum klasörü belirtin.", "CleanSweep", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (string.IsNullOrWhiteSpace(name))
            name = Path.GetFileName(folder);

        SelectedDepth = RbSafe.IsChecked == true ? ScanDepth.Safe :
                        RbModerate.IsChecked == true ? ScanDepth.Moderate :
                        ScanDepth.Advanced;
        TerminateProcesses = ChkKillProcesses.IsChecked == true;

        TargetApp = new InstalledApp
        {
            Id = _prefilled?.Id ?? $"forced:{Guid.NewGuid():N}",
            Name = name,
            Publisher = _prefilled?.Publisher ?? string.Empty,
            InstallLocation = folder,
            DisplayIcon = TxtExe.Text.Trim(),
            RegistryKeyPath = _prefilled?.RegistryKeyPath ?? string.Empty,
            Source = _prefilled?.Source ?? AppSource.Win32
        };

        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
