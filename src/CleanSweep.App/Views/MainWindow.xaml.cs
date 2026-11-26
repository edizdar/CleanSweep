using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CleanSweep.App.Models;
using CleanSweep.App.ViewModels;
using CleanSweep.Core.Cleaner;
using CleanSweep.Core.Models;
using CleanSweep.Core.Tools;
using CleanSweep.Core.Tracing;
using CleanSweep.Core.Uninstall;
using Microsoft.Win32;

namespace CleanSweep.App.Views;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm = new();
    private bool _browsersLoaded;
    private bool _autorunLoaded;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _vm;

        if (_vm.IsAdministrator)
        {
            AdminBadge.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#14532D"));
            ElevateButton.Visibility = Visibility.Collapsed;
        }
        else
        {
            AdminBadge.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#78350F"));
        }

        Loaded += async (_, _) =>
        {
            _vm.RefreshAuxiliaryTabs();
            await _vm.RefreshAppsAsync();

            if (!string.IsNullOrWhiteSpace(App.ScreenshotOutputDir))
            {
                await CaptureAllScreenshotsAsync(App.ScreenshotOutputDir);
                Application.Current.Shutdown(0);
            }
        };

        _vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.IsExpertMode) && !_vm.IsExpertMode)
            {
                if (NavShredder.IsChecked == true || NavMonitor.IsChecked == true || NavRules.IsChecked == true)
                    NavHome.IsChecked = true;
            }
        };
    }

    private async void NavChanged(object sender, RoutedEventArgs e)
    {
        if (PanelHome == null) return;
        PanelHome.Visibility = NavHome.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        PanelApps.Visibility = NavApps.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        PanelBrowsers.Visibility = NavBrowsers.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        PanelAutorun.Visibility = NavAutorun.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        PanelJunk.Visibility = NavJunk.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        PanelShredder.Visibility = NavShredder.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        PanelTools.Visibility = NavTools.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        PanelMonitor.Visibility = NavMonitor.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        PanelQuarantine.Visibility = NavQuarantine.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        PanelRules.Visibility = NavRules.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;

        if (NavBrowsers.IsChecked == true && !_browsersLoaded)
        {
            _browsersLoaded = true;
            await _vm.RefreshExtensionsAsync();
        }
        else if (NavAutorun.IsChecked == true && !_autorunLoaded)
        {
            _autorunLoaded = true;
            await _vm.RefreshAutorunAsync();
        }
        else if (NavMonitor.IsChecked == true || NavQuarantine.IsChecked == true || NavRules.IsChecked == true)
        {
            _vm.RefreshAuxiliaryTabs();
        }
    }

    private void QuickGoApps_Click(object sender, RoutedEventArgs e) => NavApps.IsChecked = true;

    private void QuickGoJunk_Click(object sender, RoutedEventArgs e)
    {
        NavJunk.IsChecked = true;
        _vm.RecalculateJunkSummary();
    }

    private void QuickGoAutorun_Click(object sender, RoutedEventArgs e) => NavAutorun.IsChecked = true;
    private void QuickGoTools_Click(object sender, RoutedEventArgs e) => NavTools.IsChecked = true;

    private void SourceFilter_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton rb || rb.Tag is not string tag) return;
        _vm.SourceFilter = tag switch
        {
            "Win32" => AppSource.Win32,
            "Msix" => AppSource.Msix,
            "Scoop" => AppSource.Scoop,
            "Chocolatey" => AppSource.Chocolatey,
            _ => null
        };
    }

    private async void RefreshApps_Click(object sender, RoutedEventArgs e) => await _vm.RefreshAppsAsync();
    private void RefreshAux_Click(object sender, RoutedEventArgs e) => _vm.RefreshAuxiliaryTabs();

    private void ElevateButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            string? exe = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exe)) return;
            Process.Start(new ProcessStartInfo { FileName = exe, UseShellExecute = true, Verb = "runas" });
            Application.Current.Shutdown();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Yönetici yetkisi alınamadı: {ex.Message}", "CleanSweep", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void LaunchHunterMode_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
        var hunter = new HunterWindow(_vm.AllApps, _vm.ScanEngine, _vm.QuarantineManager, () =>
        {
            WindowState = WindowState.Normal;
            Activate();
        });
        hunter.Show();
    }

    private async void RowUninstall_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is AppDisplayItem item)
        {
            await UninstallSingleAppAsync(item.App);
        }
    }

    private async void UninstallAndScan_Click(object sender, RoutedEventArgs e)
    {
        if (AppsGrid.SelectedItem is not AppDisplayItem item)
        {
            MessageBox.Show("Lütfen listeden bir program seçin veya yanındaki 'Kaldır' butonuna tıklayın.", "CleanSweep", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        await UninstallSingleAppAsync(item.App);
    }

    private async Task UninstallSingleAppAsync(InstalledApp app)
    {
        if (MessageBox.Show($"'{app.Name}' kaldırılacak ve ardından tüm artık dosya ve kayıt defteri anahtarları taranacak.\nDevam edilsin mi?",
                "Kaldırma Onayı", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        _vm.IsBusy = true;
        _vm.StatusMessage = $"{app.Name} kaldırılıyor...";
        try
        {
            var uninstallRes = await UninstallExecutor.RunNativeUninstallerAsync(app, preferQuiet: false);
            bool forcedFallback = false;

            if (!uninstallRes.Success)
            {
                var fallbackChoice = MessageBox.Show(
                    $"Yerleşik kaldırıcı başarısız oldu:\n{uninstallRes.Summary}\n\nNormal yollarla kaldırılamayan bu program için ZORLA KALDIRMA (Derin Tarama + Kilitli Süreç Sonlandırma) moduna geçmek ister misiniz?",
                    "Yerleşik Kaldırıcı Başarısız",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (fallbackChoice != MessageBoxResult.Yes)
                {
                    await _vm.RefreshAppsAsync();
                    return;
                }
                forcedFallback = true;
            }

            _vm.StatusMessage = $"{app.Name} için artık dosyalar ve kayıt defteri taranıyor...";
            var scanOptions = new ScanOptions
            {
                Depth = ScanDepth.Advanced,
                IsForcedUninstall = forcedFallback
            };
            var scan = await _vm.ScanEngine.ScanAsync(app, _vm.AllApps, scanOptions);

            if (scan.Items.Count > 0)
            {
                new LeftoverReviewWindow(
                    app.Name,
                    scan.Items,
                    scan.Warnings,
                    _vm.QuarantineManager,
                    isForcedMode: forcedFallback,
                    targetInstallLocation: app.InstallLocation,
                    terminateProcessesDefault: forcedFallback)
                { Owner = this }.ShowDialog();
            }
            else
            {
                MessageBox.Show($"{uninstallRes.Summary}\nEk kalıntı tespit edilmedi.", "CleanSweep", MessageBoxButton.OK, MessageBoxImage.Information);
            }

            await _vm.RefreshAppsAsync();
            _vm.RefreshAuxiliaryTabs();
        }
        finally
        {
            _vm.IsBusy = false;
        }
    }

    private void ClearAppChecks_Click(object sender, RoutedEventArgs e)
    {
        foreach (var item in _vm.FilteredApps)
            item.IsChecked = false;
    }

    private async void BatchQuietUninstall_Click(object sender, RoutedEventArgs e)
    {
        var selected = _vm.FilteredApps.Where(d => d.IsChecked).Select(d => d.App).ToList();
        if (selected.Count == 0)
            selected = AppsGrid.SelectedItems.Cast<AppDisplayItem>().Select(d => d.App).ToList();

        if (selected.Count == 0)
        {
            MessageBox.Show("Toplu kaldırma için en az bir programın kutucuğunu işaretleyin.", "CleanSweep", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (MessageBox.Show($"{selected.Count} program sırayla kaldırılacak ve ardından tüm artık dosya + kayıt defteri anahtarları taranıp size sunulacak.\nOnaylıyor musunuz?",
                "Toplu Kaldırma Onayı", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        _vm.IsBusy = true;
        var combinedLeftovers = new Dictionary<string, LeftoverItem>(StringComparer.OrdinalIgnoreCase);
        var combinedWarnings = new List<string>();
        int failedUninstalls = 0;

        try
        {
            for (int idx = 0; idx < selected.Count; idx++)
            {
                var app = selected[idx];
                _vm.StatusMessage = $"[{idx + 1}/{selected.Count}] Kaldırılıyor: {app.Name}...";
                var res = await UninstallExecutor.RunNativeUninstallerAsync(app, preferQuiet: true);
                if (!res.Success) failedUninstalls++;

                _vm.StatusMessage = $"[{idx + 1}/{selected.Count}] Artıklar taranıyor: {app.Name}...";
                var scan = await _vm.ScanEngine.ScanAsync(app, _vm.AllApps, new ScanOptions
                {
                    Depth = ScanDepth.Advanced,
                    IsForcedUninstall = !res.Success
                });

                foreach (var item in scan.Items)
                {
                    string key = $"{item.Category}|{item.Path}|{item.ValueName ?? string.Empty}";
                    if (!combinedLeftovers.ContainsKey(key))
                        combinedLeftovers[key] = item;
                }
                combinedWarnings.AddRange(scan.Warnings);
            }

            if (combinedLeftovers.Count > 0)
            {
                new LeftoverReviewWindow(
                    $"Toplu Kaldırma ({selected.Count} Program)",
                    combinedLeftovers.Values.OrderByDescending(i => i.Confidence).ToList(),
                    combinedWarnings.Distinct(),
                    _vm.QuarantineManager,
                    isForcedMode: failedUninstalls > 0,
                    terminateProcessesDefault: failedUninstalls > 0)
                { Owner = this }.ShowDialog();
            }
            else
            {
                MessageBox.Show($"{selected.Count} program kaldırıldı. Ek kalıntı tespit edilmedi.", "Toplu Kaldırma", MessageBoxButton.OK, MessageBoxImage.Information);
            }

            await _vm.RefreshAppsAsync();
            _vm.RefreshAuxiliaryTabs();
        }
        finally
        {
            _vm.IsBusy = false;
        }
    }

    private async void PreviewScan_Click(object sender, RoutedEventArgs e)
    {
        if (AppsGrid.SelectedItem is not AppDisplayItem item)
        {
            MessageBox.Show("Lütfen taranacak uygulamayı seçin.", "CleanSweep", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        await RunScanDialogAsync(item.App, new ScanOptions { Depth = ScanDepth.Advanced, IsForcedUninstall = false }, isForcedMode: false, terminateDefault: false);
    }

    private async void ForcedUninstall_Click(object sender, RoutedEventArgs e)
    {
        var prefilled = (AppsGrid.SelectedItem as AppDisplayItem)?.App;
        var dlg = new ForcedUninstallWindow(prefilled) { Owner = this };
        if (dlg.ShowDialog() == true && dlg.TargetApp != null)
        {
            await RunScanDialogAsync(
                dlg.TargetApp,
                new ScanOptions { Depth = dlg.SelectedDepth, IsForcedUninstall = true },
                isForcedMode: true,
                terminateDefault: dlg.TerminateProcesses);
            await _vm.RefreshAppsAsync();
        }
    }

    private async Task RunScanDialogAsync(
        InstalledApp target,
        ScanOptions? options = null,
        bool isForcedMode = false,
        bool terminateDefault = false)
    {
        _vm.IsBusy = true;
        _vm.StatusMessage = $"{target.Name} kalıntıları derin analiz ediliyor...";
        try
        {
            var scan = await _vm.ScanEngine.ScanAsync(target, _vm.AllApps, options);
            new LeftoverReviewWindow(
                target.Name,
                scan.Items,
                scan.Warnings,
                _vm.QuarantineManager,
                isForcedMode: isForcedMode,
                targetInstallLocation: target.InstallLocation,
                terminateProcessesDefault: terminateDefault)
            { Owner = this }.ShowDialog();
            _vm.RefreshAuxiliaryTabs();
        }
        finally
        {
            _vm.IsBusy = false;
            _vm.StatusMessage = "Hazır.";
        }
    }

    private void OpenAppInstallDir_Click(object sender, RoutedEventArgs e)
    {
        if (AppsGrid.SelectedItem is not AppDisplayItem item || string.IsNullOrWhiteSpace(item.InstallLocation) || !Directory.Exists(item.InstallLocation))
        {
            MessageBox.Show("Bu uygulama için geçerli bir kurulum klasörü bulunamadı.", "CleanSweep", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        Process.Start(new ProcessStartInfo { FileName = item.InstallLocation, UseShellExecute = true });
    }

    private void CopyAppRegPath_Click(object sender, RoutedEventArgs e)
    {
        if (AppsGrid.SelectedItem is AppDisplayItem item && !string.IsNullOrWhiteSpace(item.RegistryKeyPath))
        {
            Clipboard.SetText(item.RegistryKeyPath);
            _vm.StatusMessage = $"Kayıt defteri yolu kopyalandı: {item.RegistryKeyPath}";
        }
    }

    // Browser Extensions
    private async void ScanBrowsers_Click(object sender, RoutedEventArgs e) => await _vm.RefreshExtensionsAsync();

    private async void RemoveExtension_Click(object sender, RoutedEventArgs e)
    {
        if (BrowsersGrid.SelectedItem is not BrowserExtension ext)
        {
            MessageBox.Show("Lütfen kaldırılacak eklentiyi seçin.", "CleanSweep", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (MessageBox.Show($"'{ext.Name}' ({ext.Browser}) eklentisi karantinaya yedeklenip kaldırılsın mı?\nNot: Tarayıcınız açıksa önce kapatmanız gerekebilir.",
                "Eklenti Kaldırma", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        var res = _vm.BrowserService.RemoveExtension(ext, _vm.QuarantineManager);
        MessageBox.Show(res.Summary, "Tarayıcı Eklentisi", MessageBoxButton.OK, res.Success ? MessageBoxImage.Information : MessageBoxImage.Warning);
        await _vm.RefreshExtensionsAsync();
        _vm.RefreshAuxiliaryTabs();
    }

    private void OpenExtensionFolder_Click(object sender, RoutedEventArgs e)
    {
        if (BrowsersGrid.SelectedItem is BrowserExtension ext && Directory.Exists(ext.ExtensionPath))
            Process.Start(new ProcessStartInfo { FileName = ext.ExtensionPath, UseShellExecute = true });
    }

    // Autorun
    private async void RefreshAutorun_Click(object sender, RoutedEventArgs e) => await _vm.RefreshAutorunAsync();

    private void AutorunSwitch_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is AutorunEntry entry)
            ToggleAutorunEntry(entry, !entry.IsEnabled);
    }

    private void AutorunGrid_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (AutorunGrid.SelectedItem is AutorunEntry entry)
            ToggleAutorunEntry(entry, !entry.IsEnabled);
    }

    private void EnableAutorun_Click(object sender, RoutedEventArgs e) => ToggleAutorun(true);
    private void DisableAutorun_Click(object sender, RoutedEventArgs e) => ToggleAutorun(false);

    private void ToggleAutorun(bool enabled)
    {
        if (AutorunGrid.SelectedItem is AutorunEntry entry)
            ToggleAutorunEntry(entry, enabled);
    }

    private void ToggleAutorunEntry(AutorunEntry entry, bool targetEnabled)
    {
        bool ok = _vm.AutorunService.SetEnabled(entry, targetEnabled);
        if (!ok)
        {
            MessageBox.Show("Değişiklik yapılamadı (yönetici yetkisi gerekebilir).", "Başlangıç Yöneticisi", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _vm.StatusMessage = targetEnabled
            ? $"'{entry.Name}' açılışta çalıştırılacak şekilde açıldı."
            : $"'{entry.Name}' açılışta çalıştırılmayacak şekilde kapatıldı.";
    }

    private async void DeleteAutorun_Click(object sender, RoutedEventArgs e)
    {
        if (AutorunGrid.SelectedItem is not AutorunEntry entry) return;
        if (MessageBox.Show($"'{entry.Name}' başlangıç kaydı kalıcı olarak silinsin mi?", "Onay", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        bool ok = _vm.AutorunService.DeleteEntry(entry);
        if (!ok)
            MessageBox.Show("Kayıt silinemedi (yönetici yetkisi gerekebilir).", "Başlangıç Yöneticisi", MessageBoxButton.OK, MessageBoxImage.Warning);
        await _vm.RefreshAutorunAsync();
    }

    private void OpenAutorunLocation_Click(object sender, RoutedEventArgs e)
    {
        if (AutorunGrid.SelectedItem is not AutorunEntry entry) return;
        string? dir = Path.GetDirectoryName(entry.ExecutablePath);
        if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
            Process.Start(new ProcessStartInfo { FileName = dir, UseShellExecute = true });
    }

    // Junk Cleaner
    private void SelectRecommendedJunk_Click(object sender, RoutedEventArgs e)
    {
        foreach (var cat in _vm.JunkCategories)
            cat.IsSelected = cat.DefaultSelected;
        _vm.RecalculateJunkSummary();
    }

    private void SelectAllJunk_Click(object sender, RoutedEventArgs e)
    {
        foreach (var cat in _vm.JunkCategories)
            cat.IsSelected = true;
        _vm.RecalculateJunkSummary();
    }

    private void ClearJunkSelection_Click(object sender, RoutedEventArgs e)
    {
        foreach (var cat in _vm.JunkCategories)
            cat.IsSelected = false;
        _vm.RecalculateJunkSummary();
    }

    private async void ScanJunk_Click(object sender, RoutedEventArgs e) => await _vm.ScanJunkAsync();

    private async void CleanJunk_Click(object sender, RoutedEventArgs e)
    {
        var selected = _vm.JunkCategories.Where(c => c.IsSelected).ToList();
        if (selected.Count == 0)
        {
            MessageBox.Show("Lütfen en az bir kategori seçin.", "Temizleyici", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (MessageBox.Show($"{selected.Count} kategorideki geçici dosyalar temizlensin mi?", "Temizlik Onayı", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        _vm.IsBusy = true;
        _vm.StatusMessage = "Gereksiz dosyalar temizleniyor...";
        try
        {
            var summary = await _vm.JunkService.CleanSelectedAsync(selected);
            MessageBox.Show($"Temizlik tamamlandı!\nSilinen dosya: {summary.DeletedFiles}\nKazanılan alan: {summary.FormattedFreed}\nAtlanan kilitli dosya: {summary.SkippedLockedFiles}",
                "Gereksiz Dosya Temizleyici", MessageBoxButton.OK, MessageBoxImage.Information);
            await _vm.ScanJunkAsync();
        }
        finally
        {
            _vm.IsBusy = false;
        }
    }

    // File Shredder
    private void Shredder_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void Shredder_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] files)
        {
            foreach (string f in files)
            {
                if (!_vm.ShredTargets.Contains(f))
                    _vm.ShredTargets.Add(f);
            }
        }
    }

    private void AddShredFile_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog { Multiselect = true, Title = "Kalıcı Olarak Yok Edilecek Dosyaları Seçin" };
        if (dlg.ShowDialog() == true)
        {
            foreach (string f in dlg.FileNames)
                if (!_vm.ShredTargets.Contains(f)) _vm.ShredTargets.Add(f);
        }
    }

    private void AddShredFolder_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog { Title = "Kalıcı Olarak Yok Edilecek Klasörü Seçin" };
        if (dlg.ShowDialog() == true && !_vm.ShredTargets.Contains(dlg.FolderName))
            _vm.ShredTargets.Add(dlg.FolderName);
    }

    private void ClearShredList_Click(object sender, RoutedEventArgs e) => _vm.ShredTargets.Clear();

    private async void ExecuteShred_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.ShredTargets.Count == 0)
        {
            MessageBox.Show("Lütfen önce yok edilecek dosya veya klasör ekleyin.", "Dosya Öğütücü", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        int passNum = int.Parse(((ComboBoxItem)ShredModeCombo.SelectedItem).Tag.ToString()!);
        var mode = (ShredPassMode)passNum;

        if (MessageBox.Show($"{_vm.ShredTargets.Count} öğe {passNum} geçişli yazma ile KURTARILAMAZ şekilde silinecek.\nEmin misiniz?",
                "Kalıcı Silme Onayı", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        _vm.IsBusy = true;
        var progress = new Progress<string>(msg => _vm.StatusMessage = msg);
        try
        {
            var res = await _vm.ShredderService.ShredAsync(_vm.ShredTargets.ToList(), mode, progress);
            MessageBox.Show(res.Summary, "Dosya Öğütücü", MessageBoxButton.OK, res.Success ? MessageBoxImage.Information : MessageBoxImage.Warning);
            _vm.ShredTargets.Clear();
        }
        finally
        {
            _vm.IsBusy = false;
            _vm.StatusMessage = "Hazır.";
        }
    }

    // Windows Tools
    private void LaunchWindowsTool_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is WindowsToolItem tool)
        {
            try { WindowsToolsCatalog.Launch(tool); }
            catch (Exception ex) { MessageBox.Show($"Araç başlatılamadı: {ex.Message}", "CleanSweep", MessageBoxButton.OK, MessageBoxImage.Warning); }
        }
    }

    // Monitor & Traces
    private void BrowseInstaller_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog { Filter = "Kurulum Dosyaları (*.exe;*.msi)|*.exe;*.msi|Tüm Dosyalar (*.*)|*.*" };
        if (dlg.ShowDialog() == true)
        {
            _vm.InstallerPath = dlg.FileName;
            if (string.IsNullOrWhiteSpace(_vm.TraceName))
                _vm.TraceName = Path.GetFileNameWithoutExtension(dlg.FileName);
        }
    }

    private async void StartMonitoring_Click(object sender, RoutedEventArgs e)
    {
        string name = string.IsNullOrWhiteSpace(_vm.TraceName) ? "KurulumIzi" : _vm.TraceName.Trim();
        _vm.LiveTraceEvents.Clear();
        _vm.IsBusy = true;
        _vm.StatusMessage = "Sistem başlangıç anlık görüntüsü (snapshot) alınıyor...";
        try
        {
            await _vm.MonitorService.StartMonitoringAsync(name, _vm.InstallerPath);
            _vm.IsMonitoring = true;
            StartMonitorBtn.IsEnabled = false;
            StopMonitorBtn.IsEnabled = true;
            _vm.StatusMessage = "İzleme aktif! Kurulumu tamamlayıp '2. İzlemeyi Bitir' butonuna basın.";

            if (!string.IsNullOrWhiteSpace(_vm.InstallerPath) && File.Exists(_vm.InstallerPath))
                Process.Start(new ProcessStartInfo { FileName = _vm.InstallerPath, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"İzleme başlatılamadı: {ex.Message}", "CleanSweep", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally { _vm.IsBusy = false; }
    }

    private async void StopMonitoring_Click(object sender, RoutedEventArgs e)
    {
        _vm.IsBusy = true;
        _vm.StatusMessage = "Bitiş anlık görüntüsü alınıyor ve fark hesaplanıyor...";
        try
        {
            var trace = await _vm.MonitorService.StopAndSaveAsync();
            _vm.IsMonitoring = false;
            StartMonitorBtn.IsEnabled = true;
            StopMonitorBtn.IsEnabled = false;
            _vm.RefreshAuxiliaryTabs();
            _vm.StatusMessage = $"İz kaydedildi: {trace.TraceId} ({trace.TotalChanges} değişiklik).";
            MessageBox.Show($"Kurulum izi kaydedildi.\nID: {trace.TraceId}\nToplam değişiklik: {trace.TotalChanges}", "Kurulum İzleyici", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"İz kaydedilemedi: {ex.Message}", "CleanSweep", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally { _vm.IsBusy = false; }
    }

    private void UninstallFromTrace_Click(object sender, RoutedEventArgs e)
    {
        if (TracesGrid.SelectedItem is not InstallTrace trace)
        {
            MessageBox.Show("Lütfen bir kurulum izi seçin.", "CleanSweep", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var items = InstallMonitorService.ConvertTraceToLeftovers(trace);
        new LeftoverReviewWindow(trace.TraceName, items, [], _vm.QuarantineManager) { Owner = this }.ShowDialog();
        _vm.RefreshAuxiliaryTabs();
    }

    private void DeleteTrace_Click(object sender, RoutedEventArgs e)
    {
        if (TracesGrid.SelectedItem is not InstallTrace trace) return;
        _vm.MonitorService.DeleteTrace(trace.TraceId);
        _vm.RefreshAuxiliaryTabs();
    }

    // Quarantine
    private void RestoreQuarantine_Click(object sender, RoutedEventArgs e)
    {
        if (QuarantineGrid.SelectedItem is not QuarantineSession session)
        {
            MessageBox.Show("Lütfen geri yüklenecek karantina oturumunu seçin.", "CleanSweep", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var res = _vm.QuarantineManager.RestoreSession(session.SessionId);
        MessageBox.Show(res.Summary, "Karantina Geri Yükleme", MessageBoxButton.OK, res.Success ? MessageBoxImage.Information : MessageBoxImage.Warning);
        _vm.RefreshAuxiliaryTabs();
    }

    private void OpenQuarantineFolder_Click(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(_vm.QuarantineManager.QuarantineRoot);
        Process.Start(new ProcessStartInfo { FileName = _vm.QuarantineManager.QuarantineRoot, UseShellExecute = true });
    }

    private void PurgeQuarantine_Click(object sender, RoutedEventArgs e)
    {
        if (QuarantineGrid.SelectedItem is not QuarantineSession session) return;
        if (MessageBox.Show($"'{session.SessionId}' kalıcı olarak silinsin mi?", "Onay", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
        {
            _vm.QuarantineManager.PurgeSession(session.SessionId);
            _vm.RefreshAuxiliaryTabs();
        }
    }

    // Rules
    private void OpenRulesFolder_Click(object sender, RoutedEventArgs e)
    {
        if (Directory.Exists(_vm.RulesDirectory))
            Process.Start(new ProcessStartInfo { FileName = _vm.RulesDirectory, UseShellExecute = true });
    }

    private async void ScanSelectedRule_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.SelectedRule == null)
        {
            MessageBox.Show("Lütfen soldaki listeden bir kural seçin.", "CleanSweep", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var syntheticApp = new InstalledApp
        {
            Id = $"rule:{_vm.SelectedRule.Id}",
            Name = _vm.SelectedRule.Name,
            Publisher = _vm.SelectedRule.Publishers.FirstOrDefault() ?? string.Empty,
            Source = AppSource.Win32
        };
        await RunScanDialogAsync(syntheticApp);
    }

    private async Task CaptureAllScreenshotsAsync(string outputDir)
    {
        Directory.CreateDirectory(outputDir);

        async Task RenderAsync(string fileName)
        {
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);
            await Task.Delay(250);
            UpdateLayout();

            int width = Math.Max(1, (int)Math.Round(ActualWidth));
            int height = Math.Max(1, (int)Math.Round(ActualHeight));
            var rtb = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(this);

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(rtb));
            using var fs = File.Create(Path.Combine(outputDir, fileName));
            encoder.Save(fs);
        }

        NavHome.IsChecked = true;
        await RenderAsync("01-ana-sayfa.png");

        NavApps.IsChecked = true;
        await RenderAsync("02-program-kaldir.png");

        NavAutorun.IsChecked = true;
        await _vm.RefreshAutorunAsync();
        await RenderAsync("03-acilis-programlari.png");

        NavJunk.IsChecked = true;
        await _vm.ScanJunkAsync();
        await RenderAsync("04-bilgisayari-temizle.png");

        NavTools.IsChecked = true;
        await RenderAsync("05-windows-araclari.png");
    }
}
