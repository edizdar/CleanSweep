using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using CleanSweep.App.Helpers;
using CleanSweep.App.Models;
using CleanSweep.Core.Browsers;
using CleanSweep.Core.Cleaner;
using CleanSweep.Core.Discovery;
using CleanSweep.Core.Models;
using CleanSweep.Core.Native;
using CleanSweep.Core.Quarantine;
using CleanSweep.Core.Scanning;
using CleanSweep.Core.Startup;
using CleanSweep.Core.Tools;
using CleanSweep.Core.Tracing;

namespace CleanSweep.App.ViewModels;

public sealed class MainViewModel : INotifyPropertyChanged
{
    private readonly AppInventoryService _inventoryService = new();
    private readonly LeftoverScanEngine _scanEngine = new();
    private readonly QuarantineManager _quarantineManager = new();
    private readonly RuleRepository _ruleRepository = new();
    private readonly InstallMonitorService _monitorService = new();
    private readonly BrowserExtensionService _browserService = new();
    private readonly AutorunService _autorunService = new();
    private readonly JunkCleanerService _junkService = new();
    private readonly RegistryCleanerService _registryService = new();
    private readonly FileShredderService _shredderService = new();

    private List<AppDisplayItem> _allAppItems = [];
    private List<InstalledApp> _allApps = [];
    private string _searchQuery = string.Empty;
    private string _toolsSearchQuery = string.Empty;
    private AppSource? _sourceFilter;
    private bool _isBusy;
    private bool _isExpertMode;
    private string _statusMessage = "Hazır.";
    private string _traceName = string.Empty;
    private string _installerPath = string.Empty;
    private bool _isMonitoring;
    private AppDisplayItem? _selectedApp;
    private QuarantineSession? _selectedQuarantineSession;
    private CommunityRule? _selectedRule;
    private string _junkTotalSummary = "Taramak istediğiniz kategorileri işaretleyip 'Seçili Kategorileri Tara' butonuna basın.";
    private string _registrySummaryText = "Taramak istediğiniz kayıt defteri kategorilerini seçip 'Seçili Kategorileri Tara' butonuna basın.";

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<AppDisplayItem> FilteredApps { get; } = [];
    public ObservableCollection<BrowserExtension> BrowserExtensions { get; } = [];
    public ObservableCollection<AutorunEntry> AutorunEntries { get; } = [];
    public ObservableCollection<JunkCategory> JunkCategories { get; } = [];
    public ObservableCollection<RegistryCleanCategory> RegistryCategories { get; } = [];
    public ObservableCollection<RegistryIssueItem> RegistryIssues { get; } = [];
    public ObservableCollection<string> ShredTargets { get; } = [];
    public ObservableCollection<WindowsToolItem> WindowsTools { get; } = new(WindowsToolsCatalog.GetAll());
    public ObservableCollection<WindowsToolItem> FilteredWindowsTools { get; } = new(WindowsToolsCatalog.GetAll());
    public ObservableCollection<QuarantineSession> QuarantineSessions { get; } = [];
    public ObservableCollection<string> SelectedQuarantineDetails { get; } = [];
    public ObservableCollection<InstallTrace> SavedTraces { get; } = [];
    public ObservableCollection<CommunityRule> CommunityRules { get; } = [];
    public ObservableCollection<string> SelectedRuleDetails { get; } = [];
    public ObservableCollection<string> LiveTraceEvents { get; } = [];

    public bool IsAdministrator => NativeFileOps.IsRunningAsAdministrator();
    public string AdminStatusText => IsAdministrator ? "Yönetici Yetkisi Aktif" : "Standart Kullanıcı";
    public string AppVersion { get; } = "v" + (typeof(MainViewModel).Assembly.GetName().Version?.ToString(3) ?? "0.3.0");
    public string WindowTitle => $"CleanSweep {AppVersion} — Açık Kaynak Derin Kaldırıcı";

    public bool IsExpertMode
    {
        get => _isExpertMode;
        set
        {
            if (_isExpertMode == value) return;
            _isExpertMode = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ModeToggleText));
        }
    }

    public string ModeToggleText => IsExpertMode ? "Uzman Modu: Açık" : "Kolay Mod (Önerilen)";

    public string TotalAppsCountText => _allApps.Count == 0 ? "Yükleniyor..." : $"{_allApps.Count} program bulundu";
    public string ActiveAutorunCountText => $"{AutorunEntries.Count} başlangıç öğesi";
    public string QuarantineCountText => QuarantineSessions.Count == 0 ? "Henüz yedek yok" : $"{QuarantineSessions.Count} geri yüklenebilir yedek";
    public string ExtensionsCountText => BrowserExtensions.Count == 0 ? "Tarayıcıları tara" : $"{BrowserExtensions.Count} eklenti bulundu";

    public string SearchQuery
    {
        get => _searchQuery;
        set
        {
            if (_searchQuery == value) return;
            _searchQuery = value;
            OnPropertyChanged();
            ApplyFilter();
        }
    }

    public string ToolsSearchQuery
    {
        get => _toolsSearchQuery;
        set
        {
            if (_toolsSearchQuery == value) return;
            _toolsSearchQuery = value;
            OnPropertyChanged();
            ApplyToolsFilter();
        }
    }

    public AppSource? SourceFilter
    {
        get => _sourceFilter;
        set
        {
            if (_sourceFilter == value) return;
            _sourceFilter = value;
            OnPropertyChanged();
            ApplyFilter();
        }
    }

    public bool IsBusy
    {
        get => _isBusy;
        set { _isBusy = value; OnPropertyChanged(); }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set { _statusMessage = value; OnPropertyChanged(); }
    }

    public string TraceName
    {
        get => _traceName;
        set { _traceName = value; OnPropertyChanged(); }
    }

    public string InstallerPath
    {
        get => _installerPath;
        set { _installerPath = value; OnPropertyChanged(); }
    }

    public bool IsMonitoring
    {
        get => _isMonitoring;
        set { _isMonitoring = value; OnPropertyChanged(); }
    }

    public AppDisplayItem? SelectedApp
    {
        get => _selectedApp;
        set { _selectedApp = value; OnPropertyChanged(); }
    }

    public QuarantineSession? SelectedQuarantineSession
    {
        get => _selectedQuarantineSession;
        set
        {
            _selectedQuarantineSession = value;
            OnPropertyChanged();
            UpdateQuarantinePreview();
        }
    }

    public CommunityRule? SelectedRule
    {
        get => _selectedRule;
        set
        {
            _selectedRule = value;
            OnPropertyChanged();
            UpdateRulePreview();
        }
    }

    public string JunkTotalSummary
    {
        get => _junkTotalSummary;
        set { _junkTotalSummary = value; OnPropertyChanged(); }
    }

    public string RegistrySummaryText
    {
        get => _registrySummaryText;
        set { _registrySummaryText = value; OnPropertyChanged(); }
    }

    public bool HasNoTraces => SavedTraces.Count == 0;
    public bool HasNoQuarantine => QuarantineSessions.Count == 0;
    public bool HasNoExtensions => BrowserExtensions.Count == 0;
    public bool HasNoRegistryIssues => RegistryIssues.Count == 0;

    public string RulesDirectory => _ruleRepository.RulesDirectory;

    public IReadOnlyList<InstalledApp> AllApps => _allApps;
    public QuarantineManager QuarantineManager => _quarantineManager;
    public InstallMonitorService MonitorService => _monitorService;
    public LeftoverScanEngine ScanEngine => _scanEngine;
    public BrowserExtensionService BrowserService => _browserService;
    public AutorunService AutorunService => _autorunService;
    public JunkCleanerService JunkService => _junkService;
    public RegistryCleanerService RegistryService => _registryService;
    public FileShredderService ShredderService => _shredderService;

    public MainViewModel()
    {
        _monitorService.OnLiveEvent += evt =>
        {
            System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
            {
                if (LiveTraceEvents.Count > 500) LiveTraceEvents.RemoveAt(0);
                LiveTraceEvents.Add(evt);
            });
        };

        foreach (var cat in JunkCleanerService.BuildCategories())
        {
            cat.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName is nameof(JunkCategory.IsSelected) or nameof(JunkCategory.TotalBytes))
                    RecalculateJunkSummary();
            };
            JunkCategories.Add(cat);
        }

        foreach (var regCat in RegistryCleanerService.BuildCategories())
        {
            regCat.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(RegistryCleanCategory.IsSelected))
                    RecalculateRegistrySummary();
            };
            RegistryCategories.Add(regCat);
        }
    }

    public async Task RefreshAppsAsync()
    {
        IsBusy = true;
        StatusMessage = "Bilgisayarınızdaki programlar listeleniyor...";
        try
        {
            var raw = (await _inventoryService.GetInstalledAppsAsync()).ToList();
            _allApps = raw;
            _allAppItems = await Task.Run(() =>
                raw.Select(a => new AppDisplayItem(a, IconExtractor.GetIcon(a.DisplayIcon, a.InstallLocation, a.UninstallString))).ToList());

            ApplyFilter();
            OnPropertyChanged(nameof(TotalAppsCountText));
            StatusMessage = $"{_allApps.Count} program hazır.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Programlar yüklenirken hata: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task RefreshExtensionsAsync()
    {
        IsBusy = true;
        StatusMessage = "Tarayıcı eklentileri taranıyor...";
        try
        {
            var exts = await _browserService.ScanAllAsync();
            BrowserExtensions.Clear();
            foreach (var ext in exts) BrowserExtensions.Add(ext);
            OnPropertyChanged(nameof(HasNoExtensions));
            OnPropertyChanged(nameof(ExtensionsCountText));
            StatusMessage = $"{exts.Count} tarayıcı eklentisi bulundu.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task RefreshAutorunAsync()
    {
        IsBusy = true;
        StatusMessage = "Tüm başlangıç programları ve görevleri taranıyor...";
        try
        {
            var list = await Task.Run(() => _autorunService.GetEntries());
            AutorunEntries.Clear();
            foreach (var item in list) AutorunEntries.Add(item);
            OnPropertyChanged(nameof(ActiveAutorunCountText));
            StatusMessage = $"{list.Count} başlangıç öğesi listelendi (Kayıt Defteri, Başlangıç Klasörü, Windows Uygulamaları ve Zamanlanmış Görevler).";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void ApplyToolsFilter()
    {
        FilteredWindowsTools.Clear();
        IEnumerable<WindowsToolItem> query = WindowsTools;
        if (!string.IsNullOrWhiteSpace(_toolsSearchQuery))
        {
            query = query.Where(t =>
                t.Title.Contains(_toolsSearchQuery, StringComparison.OrdinalIgnoreCase) ||
                t.Description.Contains(_toolsSearchQuery, StringComparison.OrdinalIgnoreCase) ||
                t.Category.Contains(_toolsSearchQuery, StringComparison.OrdinalIgnoreCase));
        }
        foreach (var t in query)
            FilteredWindowsTools.Add(t);
    }

    public async Task ScanJunkAsync()
    {
        var selectedCats = JunkCategories.Where(c => c.IsSelected).ToList();
        if (selectedCats.Count == 0)
        {
            foreach (var c in JunkCategories)
            {
                c.FileCount = 0;
                c.TotalBytes = 0;
            }
            JunkTotalSummary = "Hiçbir kategori seçili değil. Lütfen taramak için en az bir kutucuğu işaretleyin.";
            StatusMessage = "Tarama için seçili kategori yok.";
            return;
        }

        IsBusy = true;
        StatusMessage = $"Seçili {selectedCats.Count} kategori taranıyor...";
        try
        {
            await _junkService.ScanCategoriesAsync(JunkCategories, onlySelected: true);
            RecalculateJunkSummary();
            long selectedTotal = JunkCategories.Where(c => c.IsSelected).Sum(c => c.TotalBytes);
            StatusMessage = $"Tarama tamamlandı: Seçili kategorilerde {InstalledApp.FormatBytes(selectedTotal)} bulundu.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    public void RecalculateJunkSummary()
    {
        int count = JunkCategories.Count(c => c.IsSelected);
        long total = JunkCategories.Where(c => c.IsSelected).Sum(c => c.TotalBytes);
        JunkTotalSummary = count == 0
            ? "Seçili kategori yok (0 B)."
            : $"Seçili ({count} kategori) temizlenebilir alan: {InstalledApp.FormatBytes(total)}";
    }

    public async Task ScanRegistryAsync()
    {
        var selectedCats = RegistryCategories.Where(c => c.IsSelected).ToList();
        if (selectedCats.Count == 0)
        {
            foreach (var c in RegistryCategories) c.FoundCount = 0;
            RegistryIssues.Clear();
            OnPropertyChanged(nameof(HasNoRegistryIssues));
            RegistrySummaryText = "Hiçbir kategori seçili değil. Lütfen taramak için en az bir kutucuğu işaretleyin.";
            StatusMessage = "Kayıt defteri taraması için seçili kategori yok.";
            return;
        }

        IsBusy = true;
        StatusMessage = $"Kayıt defteri artıkları taranıyor ({selectedCats.Count} kategori)...";
        try
        {
            var issues = await _registryService.ScanAsync(RegistryCategories, onlySelected: true);
            RegistryIssues.Clear();
            foreach (var issue in issues)
            {
                issue.PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName == nameof(RegistryIssueItem.IsSelected))
                        RecalculateRegistrySummary();
                };
                RegistryIssues.Add(issue);
            }
            OnPropertyChanged(nameof(HasNoRegistryIssues));
            RecalculateRegistrySummary();
            StatusMessage = $"Kayıt defteri taraması tamamlandı: {RegistryIssues.Count} artık kayıt bulundu.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    public void RecalculateRegistrySummary()
    {
        int catCount = RegistryCategories.Count(c => c.IsSelected);
        if (RegistryIssues.Count == 0)
        {
            RegistrySummaryText = catCount == 0
                ? "Seçili kategori yok."
                : $"Seçili {catCount} kategori taranmaya hazır veya temiz.";
            return;
        }

        int selectedIssues = RegistryIssues.Count(i => i.IsSelected);
        RegistrySummaryText = $"Toplam {RegistryIssues.Count} artık kayıt bulundu ({selectedIssues} öğe temizlik için seçili).";
    }

    public void RefreshAuxiliaryTabs()
    {
        QuarantineSessions.Clear();
        foreach (var s in _quarantineManager.ListSessions())
            QuarantineSessions.Add(s);
        OnPropertyChanged(nameof(HasNoQuarantine));
        OnPropertyChanged(nameof(QuarantineCountText));

        SavedTraces.Clear();
        foreach (var t in _monitorService.ListTraces())
            SavedTraces.Add(t);
        OnPropertyChanged(nameof(HasNoTraces));

        CommunityRules.Clear();
        foreach (var r in _ruleRepository.LoadAllRules())
            CommunityRules.Add(r);
        if (SelectedRule == null && CommunityRules.Count > 0)
            SelectedRule = CommunityRules[0];
    }

    private void UpdateQuarantinePreview()
    {
        SelectedQuarantineDetails.Clear();
        if (SelectedQuarantineSession == null) return;

        foreach (var f in SelectedQuarantineSession.Files)
            SelectedQuarantineDetails.Add($"[{(f.IsDirectory ? "Klasör" : "Dosya")}] {f.OriginalPath}");
        foreach (var r in SelectedQuarantineSession.RegistryEntries)
            SelectedQuarantineDetails.Add($"[Registry] {r.FullKeyPath}");
    }

    private void UpdateRulePreview()
    {
        SelectedRuleDetails.Clear();
        if (SelectedRule == null) return;

        SelectedRuleDetails.Add($"Uygulama: {SelectedRule.Name} (ID: {SelectedRule.Id})");
        if (!string.IsNullOrWhiteSpace(SelectedRule.Notes))
            SelectedRuleDetails.Add($"Not: {SelectedRule.Notes}");
        foreach (string p in SelectedRule.Paths)
            SelectedRuleDetails.Add($"[Yol] {p}");
        foreach (string rk in SelectedRule.RegistryKeys)
            SelectedRuleDetails.Add($"[Registry] {rk}");
        foreach (string sv in SelectedRule.Services)
            SelectedRuleDetails.Add($"[Servis] {sv}");
        foreach (string st in SelectedRule.ScheduledTasks)
            SelectedRuleDetails.Add($"[Görev] {st}");
    }

    private void ApplyFilter()
    {
        FilteredApps.Clear();
        IEnumerable<AppDisplayItem> query = _allAppItems;

        if (_sourceFilter.HasValue)
            query = query.Where(a => a.Source == _sourceFilter.Value);

        if (!string.IsNullOrWhiteSpace(_searchQuery))
        {
            query = query.Where(a =>
                a.Name.Contains(_searchQuery, StringComparison.OrdinalIgnoreCase) ||
                a.Publisher.Contains(_searchQuery, StringComparison.OrdinalIgnoreCase));
        }

        foreach (var app in query)
            FilteredApps.Add(app);
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
