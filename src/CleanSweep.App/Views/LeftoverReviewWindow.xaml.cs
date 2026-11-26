using System.Collections.ObjectModel;
using System.Windows;
using CleanSweep.Core.Models;
using CleanSweep.Core.Quarantine;
using CleanSweep.Core.Uninstall;

namespace CleanSweep.App.Views;

public partial class LeftoverReviewWindow : Window
{
    private readonly string _appName;
    private readonly string? _targetInstallLocation;
    private readonly ObservableCollection<LeftoverItem> _items;
    private readonly QuarantineManager _quarantineManager;

    public LeftoverReviewWindow(
        string appName,
        IEnumerable<LeftoverItem> items,
        IEnumerable<string> warnings,
        QuarantineManager quarantineManager,
        bool isForcedMode = false,
        string? targetInstallLocation = null,
        bool terminateProcessesDefault = false)
    {
        InitializeComponent();
        _appName = appName;
        _targetInstallLocation = targetInstallLocation;
        _items = new ObservableCollection<LeftoverItem>(items);
        _quarantineManager = quarantineManager;

        TitleText.Text = $"{appName} — Kalıntı Analizi";
        if (isForcedMode)
        {
            ForcedBanner.Visibility = Visibility.Visible;
            ChkKillProcesses.IsChecked = terminateProcessesDefault || isForcedMode;
        }
        else
        {
            ChkKillProcesses.IsChecked = terminateProcessesDefault;
        }

        WarningText.Text = string.Join(" | ", warnings);
        if (string.IsNullOrWhiteSpace(WarningText.Text))
            WarningText.Visibility = Visibility.Collapsed;

        ItemsGrid.ItemsSource = _items;
        UpdateKpis();
    }

    private void UpdateKpis()
    {
        KpiTotal.Text = _items.Count.ToString();
        KpiSafe.Text = _items.Count(i => i.Risk == RiskLevel.Safe).ToString();
        KpiModerate.Text = _items.Count(i => i.Risk == RiskLevel.Moderate).ToString();
        KpiCaution.Text = _items.Count(i => i.Risk == RiskLevel.Caution).ToString();
        KpiSize.Text = InstalledApp.FormatBytes(_items.Sum(i => i.SizeBytes));
    }

    private void SelectSafe_Click(object sender, RoutedEventArgs e)
    {
        foreach (var item in _items)
            item.IsSelected = item.Risk == RiskLevel.Safe;
        ItemsGrid.Items.Refresh();
    }

    private void SelectAll_Click(object sender, RoutedEventArgs e)
    {
        foreach (var item in _items)
            item.IsSelected = true;
        ItemsGrid.Items.Refresh();
    }

    private void ClearSelection_Click(object sender, RoutedEventArgs e)
    {
        foreach (var item in _items)
            item.IsSelected = false;
        ItemsGrid.Items.Refresh();
    }

    private void CleanSafeRecommended_Click(object sender, RoutedEventArgs e)
    {
        foreach (var item in _items)
            item.IsSelected = item.Risk == RiskLevel.Safe;
        ItemsGrid.Items.Refresh();
        ChkQuarantine.IsChecked = true;
        ExecuteCleanup_Click(sender, e);
    }

    private async void ExecuteCleanup_Click(object sender, RoutedEventArgs e)
    {
        var selected = _items.Where(i => i.IsSelected).ToList();
        if (selected.Count == 0)
        {
            MessageBox.Show("Lütfen temizlemek için en az bir kalıntı seçin.", "CleanSweep", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        bool dryRun = ChkDryRun.IsChecked == true;
        if (!dryRun)
        {
            var confirm = MessageBox.Show(
                $"{selected.Count} adet kalıntı öğe temizlenecek.\nKarantina yedeği: {(ChkQuarantine.IsChecked == true ? "Evet" : "Hayır")}\nOnaylıyor musunuz?",
                "Temizlik Onayı",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            if (confirm != MessageBoxResult.Yes) return;
        }

        var options = new CleanupOptions
        {
            BackupToQuarantine = ChkQuarantine.IsChecked == true,
            ScheduleRebootOnLocked = ChkReboot.IsChecked == true,
            TerminateLockingProcesses = ChkKillProcesses.IsChecked == true,
            TargetInstallLocation = _targetInstallLocation,
            DryRun = dryRun
        };

        IsEnabled = false;
        try
        {
            var executor = new CleanupExecutor(_quarantineManager);
            var result = await Task.Run(() => executor.ExecuteCleanup(_appName, selected, options));

            string details = result.Summary;
            if (!string.IsNullOrEmpty(result.QuarantineSessionId))
                details += $"\n\nKarantina Oturumu: {result.QuarantineSessionId}";
            if (result.Errors.Count > 0)
                details += "\n\nHata Detayları:\n- " + string.Join("\n- ", result.Errors.Take(5));

            MessageBox.Show(details, "Temizlik Sonucu", MessageBoxButton.OK,
                result.Success ? MessageBoxImage.Information : MessageBoxImage.Warning);

            if (!dryRun && result.Success)
                Close();
        }
        finally
        {
            IsEnabled = true;
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
