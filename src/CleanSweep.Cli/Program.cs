using System.Diagnostics;
using System.Text.Json;
using CleanSweep.Core.Cleaner;
using CleanSweep.Core.Discovery;
using CleanSweep.Core.Models;
using CleanSweep.Core.Quarantine;
using CleanSweep.Core.Scanning;
using CleanSweep.Core.Tracing;
using CleanSweep.Core.Uninstall;

namespace CleanSweep.Cli;

internal static class Program
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;

        if (args.Length == 0 || args[0] is "--help" or "-h" or "help")
        {
            PrintHelp();
            return 0;
        }

        if (args[0] is "--version" or "-v" or "version")
        {
            Console.WriteLine($"CleanSweep CLI v{GetAppVersion()}");
            Console.WriteLine("Geliştirici: Erol Dizdar | Web: https://www.eroldizdar.tr");
            return 0;
        }

        string command = args[0].ToLowerInvariant();
        string[] rest = args[1..];
        bool asJson = rest.Contains("--json", StringComparer.OrdinalIgnoreCase);

        try
        {
            return command switch
            {
                "list" => await HandleListAsync(rest, asJson),
                "export" => await HandleExportAsync(rest),
                "scan" => await HandleScanAsync(rest, asJson),
                "uninstall" => await HandleUninstallAsync(rest, asJson),
                "force-uninstall" => await HandleForceUninstallAsync(rest, asJson),
                "trace" => await HandleTraceAsync(rest, asJson),
                "quarantine" => HandleQuarantine(rest, asJson),
                "registry" => await HandleRegistryAsync(rest, asJson),
                "rules" => HandleRules(rest, asJson),
                _ => UnknownCommand(command)
            };
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Hata: {ex.Message}");
            return 1;
        }
    }

    private static async Task<int> HandleListAsync(string[] args, bool asJson)
    {
        string? sourceArg = GetOption(args, "--source");
        string? search = GetOption(args, "--search");

        AppSource? filter = sourceArg?.ToLowerInvariant() switch
        {
            "win32" => AppSource.Win32,
            "msix" => AppSource.Msix,
            "scoop" => AppSource.Scoop,
            "choco" or "chocolatey" => AppSource.Chocolatey,
            _ => null
        };

        var inventory = new AppInventoryService();
        var apps = await inventory.GetInstalledAppsAsync(filter);

        if (!string.IsNullOrWhiteSpace(search))
        {
            apps = apps.Where(a =>
                a.Name.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                a.Publisher.Contains(search, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        if (asJson)
        {
            Console.WriteLine(JsonSerializer.Serialize(apps, JsonOptions));
            return 0;
        }

        Console.WriteLine($"Toplam {apps.Count} uygulama bulundu:\n");
        Console.WriteLine($"{"Ad",-40} {"Kaynak",-10} {"Tip",-12} {"Sürüm",-16} {"Boyut",10}");
        Console.WriteLine(new string('-', 94));
        foreach (var app in apps)
        {
            Console.WriteLine($"{Truncate(app.Name, 39),-40} {app.Source,-10} {app.InstallerType,-12} {Truncate(app.Version, 15),-16} {app.FormattedSize,10}");
        }
        return 0;
    }

    private static async Task<int> HandleScanAsync(string[] args, bool asJson)
    {
        string? appQuery = GetPositional(args);
        if (string.IsNullOrWhiteSpace(appQuery))
        {
            Console.Error.WriteLine("Kullanım: cleansweep scan \"<uygulama-adı>\" [--json]");
            return 2;
        }

        var inventory = new AppInventoryService();
        var allApps = await inventory.GetInstalledAppsAsync();
        var target = FindTargetApp(allApps, appQuery) ?? new InstalledApp
        {
            Id = $"manual:{appQuery}",
            Name = appQuery,
            Source = AppSource.Win32
        };

        var engine = new LeftoverScanEngine();
        var scanResult = await engine.ScanAsync(target, allApps);

        if (asJson)
        {
            Console.WriteLine(JsonSerializer.Serialize(scanResult, JsonOptions));
            return 0;
        }

        Console.WriteLine($"Hedef: {scanResult.TargetApp.Name} (Süre: {scanResult.Duration.TotalMilliseconds:0} ms)");
        Console.WriteLine($"Bulgular: {scanResult.TotalCount} (Safe: {scanResult.SafeCount}, Moderate: {scanResult.ModerateCount}, Caution: {scanResult.CautionCount}) | Toplam Boyut: {scanResult.FormattedTotalSize}\n");

        foreach (string w in scanResult.Warnings)
            Console.WriteLine($"[UYARI] {w}");

        Console.WriteLine($"{"Risk",-9} {"Güven",5} {"Kategori",-15} {"Yol"}");
        Console.WriteLine(new string('-', 100));
        foreach (var item in scanResult.Items)
        {
            Console.WriteLine($"{item.Risk,-9} {item.Confidence,4}% {item.Category,-15} {item.DisplayPath}");
            Console.WriteLine($"   └─ Neden: {item.MatchReason}");
            if (item.LockingProcesses.Count > 0)
                Console.WriteLine($"   └─ Kilitleyen süreç: {item.LockingSummary}");
        }

        return 0;
    }

    private static async Task<int> HandleUninstallAsync(string[] args, bool asJson)
    {
        string? appQuery = GetPositional(args);
        if (string.IsNullOrWhiteSpace(appQuery))
        {
            Console.Error.WriteLine("Kullanım: cleansweep uninstall \"<uygulama-adı>\" [--quiet] [--clean-leftovers] [--min-confidence 80] [--no-quarantine] [--dry-run]");
            return 2;
        }

        bool quiet = HasFlag(args, "--quiet");
        bool cleanLeftovers = HasFlag(args, "--clean-leftovers");
        bool noQuarantine = HasFlag(args, "--no-quarantine");
        bool dryRun = HasFlag(args, "--dry-run");
        int minConfidence = int.TryParse(GetOption(args, "--min-confidence"), out int mc) ? mc : 80;

        var inventory = new AppInventoryService();
        var allApps = await inventory.GetInstalledAppsAsync();
        var target = FindTargetApp(allApps, appQuery);

        if (target == null)
        {
            Console.Error.WriteLine($"Uygulama bulunamadı: {appQuery}");
            return 1;
        }

        OperationResult? uninstallOutcome = null;
        if (!dryRun)
        {
            Console.WriteLine($"Yerleşik kaldırıcı başlatılıyor: {target.Name}...");
            uninstallOutcome = await UninstallExecutor.RunNativeUninstallerAsync(target, quiet);
            Console.WriteLine(uninstallOutcome.Summary);
        }
        else
        {
            Console.WriteLine($"[Dry-Run] {target.Name} için yerleşik kaldırıcı atlandı.");
        }

        OperationResult? cleanupOutcome = null;
        if (cleanLeftovers)
        {
            var engine = new LeftoverScanEngine();
            var scan = await engine.ScanAsync(target, allApps);
            var toClean = scan.Items.Where(i => i.Confidence >= minConfidence && i.Risk == RiskLevel.Safe).ToList();
            var executor = new CleanupExecutor();
            cleanupOutcome = executor.ExecuteCleanup(target.Name, toClean, new CleanupOptions
            {
                BackupToQuarantine = !noQuarantine,
                DryRun = dryRun,
                ScheduleRebootOnLocked = true
            });

            if (!asJson)
            {
                Console.WriteLine(cleanupOutcome.Summary);
                if (!string.IsNullOrEmpty(cleanupOutcome.QuarantineSessionId))
                    Console.WriteLine($"Karantina Oturumu: {cleanupOutcome.QuarantineSessionId}");
            }
        }

        if (asJson)
        {
            Console.WriteLine(JsonSerializer.Serialize(new { uninstallOutcome, cleanupOutcome }, JsonOptions));
        }

        return 0;
    }

    private static async Task<int> HandleForceUninstallAsync(string[] args, bool asJson)
    {
        string? query = GetPositional(args);
        if (string.IsNullOrWhiteSpace(query))
        {
            Console.Error.WriteLine("Kullanım: cleansweep force-uninstall \"<uygulama-veya-klasör>\" [--mode safe|moderate|advanced] [--kill-processes] [--no-quarantine] [--dry-run]");
            return 2;
        }

        string modeArg = GetOption(args, "--mode")?.ToLowerInvariant() ?? "advanced";
        ScanDepth depth = modeArg switch
        {
            "safe" => ScanDepth.Safe,
            "moderate" => ScanDepth.Moderate,
            _ => ScanDepth.Advanced
        };

        bool killProcs = HasFlag(args, "--kill-processes");
        bool noQuarantine = HasFlag(args, "--no-quarantine");
        bool dryRun = HasFlag(args, "--dry-run");

        var inventory = new AppInventoryService();
        var allApps = await inventory.GetInstalledAppsAsync();
        var target = FindTargetApp(allApps, query) ?? new InstalledApp
        {
            Id = $"forced:{Guid.NewGuid():N}",
            Name = Directory.Exists(query) ? Path.GetFileName(query.TrimEnd('\\')) : query,
            InstallLocation = Directory.Exists(query) ? query : string.Empty,
            Source = AppSource.Win32
        };

        var engine = new LeftoverScanEngine();
        var scan = await engine.ScanAsync(target, allApps, new ScanOptions
        {
            Depth = depth,
            IsForcedUninstall = true
        });

        var toClean = scan.Items.Where(i => i.Risk == RiskLevel.Safe && i.Confidence >= 85).ToList();
        var executor = new CleanupExecutor();
        var cleanupOutcome = executor.ExecuteCleanup(target.Name, toClean, new CleanupOptions
        {
            BackupToQuarantine = !noQuarantine,
            DryRun = dryRun,
            TerminateLockingProcesses = killProcs,
            TargetInstallLocation = target.InstallLocation,
            ScheduleRebootOnLocked = true
        });

        if (asJson)
        {
            Console.WriteLine(JsonSerializer.Serialize(new { scan, cleanupOutcome }, JsonOptions));
            return 0;
        }

        Console.WriteLine($"[Zorla Kaldırma - {depth}] Hedef: {target.Name} ({scan.Duration.TotalMilliseconds:0} ms)");
        Console.WriteLine($"Bulunan kalıntı: {scan.TotalCount} (Seçili Safe: {toClean.Count})");
        Console.WriteLine(cleanupOutcome.Summary);
        if (!string.IsNullOrEmpty(cleanupOutcome.QuarantineSessionId))
            Console.WriteLine($"Karantina Oturumu: {cleanupOutcome.QuarantineSessionId}");
        return cleanupOutcome.Success ? 0 : 1;
    }

    private static async Task<int> HandleTraceAsync(string[] args, bool asJson)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine("Kullanım: cleansweep trace <start|list|uninstall> ...");
            return 2;
        }

        string sub = args[0].ToLowerInvariant();
        string[] subArgs = args[1..];
        using var monitor = new InstallMonitorService();

        switch (sub)
        {
            case "list":
            {
                var traces = monitor.ListTraces();
                if (asJson)
                {
                    Console.WriteLine(JsonSerializer.Serialize(traces, JsonOptions));
                    return 0;
                }
                Console.WriteLine($"Kayıtlı İzler ({traces.Count}):\n");
                foreach (var t in traces)
                {
                    Console.WriteLine($"{t.TraceId} | {t.TraceName,-24} | {t.CompletedAt.LocalDateTime:yyyy-MM-dd HH:mm} | Değişiklik: {t.TotalChanges}");
                }
                return 0;
            }
            case "start":
            {
                string? name = GetPositional(subArgs);
                if (string.IsNullOrWhiteSpace(name))
                {
                    Console.Error.WriteLine("Kullanım: cleansweep trace start \"<iz-adı>\" [--installer \"<yol>\"]");
                    return 2;
                }
                string? installer = GetOption(subArgs, "--installer");
                Console.WriteLine("Başlangıç sistem anlık görüntüsü (snapshot) alınıyor...");
                monitor.OnLiveEvent += msg => Console.WriteLine($"  {msg}");
                await monitor.StartMonitoringAsync(name, installer);

                if (!string.IsNullOrWhiteSpace(installer) && File.Exists(installer))
                {
                    Console.WriteLine($"Kurulum başlatılıyor: {installer}");
                    using var proc = Process.Start(new ProcessStartInfo { FileName = installer, UseShellExecute = true });
                    if (proc != null) await proc.WaitForExitAsync();
                }
                else
                {
                    Console.WriteLine("İzleme aktif. Kurulumu tamamladıktan sonra Enter tuşuna basın...");
                    Console.ReadLine();
                }

                Console.WriteLine("Bitiş anlık görüntüsü alınıyor ve fark hesaplanıyor...");
                var trace = await monitor.StopAndSaveAsync();
                Console.WriteLine($"İz kaydedildi: {trace.TraceId} ({trace.TotalChanges} değişiklik).");
                return 0;
            }
            case "uninstall":
            {
                string? traceId = GetPositional(subArgs);
                if (string.IsNullOrWhiteSpace(traceId))
                {
                    Console.Error.WriteLine("Kullanım: cleansweep trace uninstall <trace-id> [--dry-run] [--no-quarantine]");
                    return 2;
                }
                var trace = monitor.ListTraces().FirstOrDefault(t => t.TraceId.Equals(traceId, StringComparison.OrdinalIgnoreCase));
                if (trace == null)
                {
                    Console.Error.WriteLine($"İz bulunamadı: {traceId}");
                    return 1;
                }
                var leftovers = InstallMonitorService.ConvertTraceToLeftovers(trace);
                var executor = new CleanupExecutor();
                var res = executor.ExecuteCleanup(trace.TraceName, leftovers, new CleanupOptions
                {
                    DryRun = HasFlag(subArgs, "--dry-run"),
                    BackupToQuarantine = !HasFlag(subArgs, "--no-quarantine")
                });
                Console.WriteLine(res.Summary);
                return res.Success ? 0 : 1;
            }
            default:
                return UnknownCommand($"trace {sub}");
        }
    }

    private static int HandleQuarantine(string[] args, bool asJson)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine("Kullanım: cleansweep quarantine <list|restore|purge> [session-id]");
            return 2;
        }

        string sub = args[0].ToLowerInvariant();
        string[] subArgs = args[1..];
        var manager = new QuarantineManager();

        switch (sub)
        {
            case "list":
            {
                var sessions = manager.ListSessions();
                if (asJson)
                {
                    Console.WriteLine(JsonSerializer.Serialize(sessions, JsonOptions));
                    return 0;
                }
                Console.WriteLine($"Karantina Oturumları ({sessions.Count}):\n");
                foreach (var s in sessions)
                {
                    Console.WriteLine($"{s.SessionId} | {s.AppName,-22} | {s.CreatedAt.LocalDateTime:yyyy-MM-dd HH:mm} | Öğe: {s.TotalItemCount} | {s.FormattedSize}");
                }
                return 0;
            }
            case "restore":
            {
                string? id = GetPositional(subArgs);
                if (string.IsNullOrWhiteSpace(id)) return 2;
                var res = manager.RestoreSession(id);
                Console.WriteLine(res.Summary);
                return res.Success ? 0 : 1;
            }
            case "purge":
            {
                string? id = GetPositional(subArgs);
                if (string.IsNullOrWhiteSpace(id)) return 2;
                bool ok = manager.PurgeSession(id);
                Console.WriteLine(ok ? "Karantina oturumu kalıcı olarak silindi." : "Oturum bulunamadı.");
                return ok ? 0 : 1;
            }
            default:
                return UnknownCommand($"quarantine {sub}");
        }
    }

    private static int HandleRules(string[] args, bool asJson)
    {
        string sub = args.Length > 0 ? args[0].ToLowerInvariant() : "list";
        if (sub != "list") return UnknownCommand($"rules {sub}");

        var repo = new RuleRepository();
        var rules = repo.LoadAllRules();
        if (asJson)
        {
            Console.WriteLine(JsonSerializer.Serialize(rules, JsonOptions));
            return 0;
        }

        Console.WriteLine($"Kural Klasörü: {repo.RulesDirectory}");
        Console.WriteLine($"Yüklenen Topluluk Kuralları ({rules.Count}):\n");
        foreach (var r in rules)
        {
            Console.WriteLine($"- {r.Name} [{r.Id}] | Yollar: {r.Paths.Count}, Registry: {r.RegistryKeys.Count}, Servis: {r.Services.Count}");
        }
        return 0;
    }

    private static async Task<int> HandleRegistryAsync(string[] args, bool asJson)
    {
        string sub = args.Length > 0 ? args[0].ToLowerInvariant() : "scan";
        var service = new RegistryCleanerService();
        var categories = RegistryCleanerService.BuildCategories();

        if (HasFlag(args, "--all"))
        {
            foreach (var c in categories)
                c.IsSelected = true;
        }

        if (sub == "scan")
        {
            var issues = await service.ScanAsync(categories, onlySelected: true);
            if (asJson)
            {
                Console.WriteLine(JsonSerializer.Serialize(issues, JsonOptions));
                return 0;
            }

            Console.WriteLine($"Toplam {issues.Count} kayıt defteri artığı bulundu:\n");
            foreach (var issue in issues)
            {
                Console.WriteLine($"[{issue.Risk}] [{issue.CategoryId}] {issue.DisplayPath}");
                Console.WriteLine($"    Sebep: {issue.Reason}");
            }
            return 0;
        }

        if (sub == "clean")
        {
            bool dryRun = HasFlag(args, "--dry-run");
            bool backup = !HasFlag(args, "--no-quarantine");
            var issues = await service.ScanAsync(categories, onlySelected: true);
            var res = await service.CleanSelectedAsync(issues, backupToQuarantine: backup, dryRun: dryRun);
            if (asJson)
            {
                Console.WriteLine(JsonSerializer.Serialize(res, JsonOptions));
            }
            else
            {
                Console.WriteLine(res.Summary);
                if (!string.IsNullOrEmpty(res.QuarantineSessionId))
                    Console.WriteLine($"Karantina Oturumu: {res.QuarantineSessionId}");
            }
            return res.Success ? 0 : 1;
        }

        Console.Error.WriteLine("Kullanım: cleansweep registry <scan|clean> [--all] [--dry-run] [--no-quarantine] [--json]");
        return 2;
    }

    private static async Task<int> HandleExportAsync(string[] args)
    {
        string? outputPath = GetPositional(args);
        if (string.IsNullOrWhiteSpace(outputPath))
        {
            Console.Error.WriteLine("Kullanım: cleansweep export <dosya.html|dosya.csv>");
            return 2;
        }

        string fullPath = Path.GetFullPath(outputPath);
        var inventory = new AppInventoryService();
        var apps = await inventory.GetInstalledAppsAsync();

        if (fullPath.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
            AppExportService.ExportToCsv(apps, fullPath);
        else
            AppExportService.ExportToHtml(apps, fullPath, GetAppVersion());

        Console.WriteLine($"Toplam {apps.Count} program dışa aktarıldı: {fullPath}");
        return 0;
    }

    private static InstalledApp? FindTargetApp(IReadOnlyList<InstalledApp> apps, string query)
    {
        return apps.FirstOrDefault(a => a.Name.Equals(query, StringComparison.OrdinalIgnoreCase))
            ?? apps.FirstOrDefault(a => a.Name.Contains(query, StringComparison.OrdinalIgnoreCase));
    }

    private static string? GetOption(string[] args, string flag)
    {
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i].Equals(flag, StringComparison.OrdinalIgnoreCase))
                return args[i + 1];
        }
        return null;
    }

    private static bool HasFlag(string[] args, string flag) =>
        args.Any(a => a.Equals(flag, StringComparison.OrdinalIgnoreCase));

    private static string? GetPositional(string[] args)
    {
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i].StartsWith("--"))
            {
                if (args[i] is "--source" or "--search" or "--min-confidence" or "--installer" or "--mode")
                    i++;
                continue;
            }
            return args[i];
        }
        return null;
    }

    private static string Truncate(string s, int max) =>
        s.Length <= max ? s : s[..(max - 1)] + "…";

    private static int UnknownCommand(string cmd)
    {
        Console.Error.WriteLine($"Bilinmeyen komut: {cmd}. Yardım için --help kullanın.");
        return 2;
    }

    private static string GetAppVersion() =>
        typeof(Program).Assembly.GetName().Version?.ToString(3) ?? "0.3.0";

    private static void PrintHelp()
    {
        Console.WriteLine($"""
CleanSweep CLI v{GetAppVersion()} — Açık Kaynak Modern Windows Kaldırıcı
Geliştirici: Erol Dizdar | Web: https://www.eroldizdar.tr

Kullanım:
  cleansweep --version
  cleansweep list [--source all|win32|msix|scoop|choco] [--search <metin>] [--json]
  cleansweep export <dosya.html|dosya.csv>
  cleansweep scan "<uygulama-adı>" [--json]
  cleansweep uninstall "<uygulama-adı>" [--quiet] [--clean-leftovers] [--min-confidence 80] [--no-quarantine] [--dry-run]
  cleansweep force-uninstall "<uygulama-veya-klasör>" [--mode safe|moderate|advanced] [--kill-processes] [--no-quarantine] [--dry-run]
  cleansweep trace start "<iz-adı>" [--installer "<exe-yolu>"]
  cleansweep trace list [--json]
  cleansweep trace uninstall <trace-id> [--dry-run] [--no-quarantine]
  cleansweep quarantine list [--json]
  cleansweep quarantine restore <session-id>
  cleansweep quarantine purge <session-id>
  cleansweep registry scan [--all] [--json]
  cleansweep registry clean [--all] [--dry-run] [--no-quarantine] [--json]
  cleansweep rules list [--json]
""");
    }
}
