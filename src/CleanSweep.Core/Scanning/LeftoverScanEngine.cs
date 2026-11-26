using System.Diagnostics;
using CleanSweep.Core.Models;
using CleanSweep.Core.Native;
using CleanSweep.Core.Safety;

namespace CleanSweep.Core.Scanning;

public sealed class LeftoverScanEngine
{
    private readonly RuleRepository _ruleRepository;

    public LeftoverScanEngine(RuleRepository? ruleRepository = null)
    {
        _ruleRepository = ruleRepository ?? new RuleRepository();
    }

    public Task<ScanResult> ScanAsync(
        InstalledApp target,
        IReadOnlyList<InstalledApp>? allInstalledApps = null,
        CancellationToken cancellationToken = default)
    {
        return ScanAsync(target, allInstalledApps, new ScanOptions(), cancellationToken);
    }

    public Task<ScanResult> ScanAsync(
        InstalledApp target,
        IReadOnlyList<InstalledApp>? allInstalledApps,
        ScanOptions? options,
        CancellationToken cancellationToken = default)
    {
        return Task.Run(() => Scan(target, allInstalledApps ?? [], options ?? new ScanOptions(), cancellationToken), cancellationToken);
    }

    public ScanResult Scan(
        InstalledApp target,
        IReadOnlyList<InstalledApp> allInstalledApps,
        CancellationToken cancellationToken = default)
    {
        return Scan(target, allInstalledApps, new ScanOptions(), cancellationToken);
    }

    public ScanResult Scan(
        InstalledApp target,
        IReadOnlyList<InstalledApp> allInstalledApps,
        ScanOptions options,
        CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        var warnings = new List<string>();
        var items = new List<LeftoverItem>();

        var otherSamePub = SafetyGuard.GetOtherAppsFromSamePublisher(target, allInstalledApps);
        bool hasOtherSamePub = otherSamePub.Count > 0;
        var tokens = SafetyGuard.ExtractSearchTokens(target);
        var matchedRules = _ruleRepository.FindMatchingRules(target);

        // 1. Community Rules
        foreach (var rule in matchedRules)
        {
            foreach (string rawPath in rule.Paths)
            {
                string expanded = SafetyGuard.NormalizeFilePath(RuleRepository.ExpandPathVariables(rawPath));
                if (SafetyGuard.IsProtectedFilePath(expanded)) continue;

                if (Directory.Exists(expanded))
                {
                    var item = new LeftoverItem
                    {
                        Category = LeftoverCategory.Directory,
                        Path = expanded,
                        RuleSource = rule.Id,
                        SizeBytes = FileSystemScanner.ComputeDirectorySizeSafe(expanded, cancellationToken)
                    };
                    RiskScorer.ApplyScore(item, MatchStrength.CommunityRule);
                    items.Add(item);
                }
                else if (File.Exists(expanded))
                {
                    var item = new LeftoverItem
                    {
                        Category = LeftoverCategory.File,
                        Path = expanded,
                        RuleSource = rule.Id,
                        SizeBytes = FileSystemScanner.GetFileSizeSafe(expanded),
                        LockingProcesses = RestartManagerHelper.GetLockingProcesses(expanded)
                    };
                    RiskScorer.ApplyScore(item, MatchStrength.CommunityRule);
                    items.Add(item);
                }
            }

            foreach (string rawReg in rule.RegistryKeys)
            {
                string normReg = SafetyGuard.NormalizeRegistryPath(rawReg);
                if (SafetyGuard.IsProtectedRegistryPath(normReg)) continue;

                if (RegistryKeyExists(normReg))
                {
                    var item = new LeftoverItem
                    {
                        Category = LeftoverCategory.RegistryKey,
                        Path = normReg,
                        RuleSource = rule.Id
                    };
                    RiskScorer.ApplyScore(item, MatchStrength.CommunityRule);
                    items.Add(item);
                }
            }
        }

        // 2. Filesystem scan
        items.AddRange(FileSystemScanner.Scan(target, tokens, hasOtherSamePub, cancellationToken, options));

        // 3. Registry scan
        items.AddRange(RegistryScanner.Scan(target, tokens, hasOtherSamePub, cancellationToken, options));

        // 4. System artifacts (Services, ScheduledTasks, PATH, Firewall)
        items.AddRange(SystemArtifactScanner.Scan(target, matchedRules, cancellationToken));

        // 5. Deduplicate, apply Safe-mode filter if requested, & enforce SafetyGuard hard wall
        var deduped = new Dictionary<string, LeftoverItem>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in items)
        {
            if (!SafetyGuard.IsItemAllowed(item)) continue;
            if (options.Depth == ScanDepth.Safe && item.Confidence < 85) continue;

            string key = $"{item.Category}|{item.Path}|{item.ValueName ?? string.Empty}";
            if (!deduped.TryGetValue(key, out var existing) || item.Confidence > existing.Confidence)
            {
                deduped[key] = item;
            }
        }

        if (hasOtherSamePub)
        {
            warnings.Add($"Aynı yayıncıya ({target.Publisher}) ait {otherSamePub.Count} farklı uygulama tespit edildi; ortak klasörler 'Caution' olarak işaretlendi.");
        }

        sw.Stop();
        return new ScanResult
        {
            TargetApp = target,
            ScannedAt = DateTimeOffset.UtcNow,
            Duration = sw.Elapsed,
            Items = deduped.Values
                .OrderByDescending(i => i.Confidence)
                .ThenBy(i => i.Category)
                .ThenBy(i => i.Path, StringComparer.OrdinalIgnoreCase)
                .ToList(),
            Warnings = warnings
        };
    }

    private static bool RegistryKeyExists(string regPath)
    {
        if (!RegistryExportHelper.TryParseRegistryPath(regPath, out var hive, out string subKey, out _))
            return false;

        try
        {
            using var k64 = Microsoft.Win32.RegistryKey.OpenBaseKey(hive, Microsoft.Win32.RegistryView.Registry64).OpenSubKey(subKey);
            if (k64 != null) return true;
            using var k32 = Microsoft.Win32.RegistryKey.OpenBaseKey(hive, Microsoft.Win32.RegistryView.Registry32).OpenSubKey(subKey);
            return k32 != null;
        }
        catch
        {
            return false;
        }
    }
}
