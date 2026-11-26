using System.Collections.Concurrent;
using System.Text.Json;
using CleanSweep.Core.Models;
using CleanSweep.Core.Safety;
using CleanSweep.Core.Scanning;

namespace CleanSweep.Core.Tracing;

public sealed class InstallMonitorService : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public string TracesRoot { get; }

    private SystemSnapshot? _beforeSnapshot;
    private string _activeTraceName = string.Empty;
    private string? _activeInstallerPath;
    private readonly List<FileSystemWatcher> _watchers = [];
    private readonly ConcurrentBag<string> _liveFiles = [];
    private readonly ConcurrentBag<string> _liveDirs = [];

    public bool IsMonitoring => _beforeSnapshot != null;
    public event Action<string>? OnLiveEvent;

    public InstallMonitorService(string? customTracesRoot = null)
    {
        TracesRoot = customTracesRoot ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CleanSweep",
            "Traces");
    }

    public Task StartMonitoringAsync(string traceName, string? installerPath = null)
    {
        if (IsMonitoring)
            throw new InvalidOperationException("Zaten aktif bir kurulum izleme oturumu çalışıyor.");

        return Task.Run(() =>
        {
            _activeTraceName = traceName;
            _activeInstallerPath = installerPath;
            _liveFiles.Clear();
            _liveDirs.Clear();
            _beforeSnapshot = SystemSnapshot.Capture();

            string[] watchRoots =
            [
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
            ];

            foreach (string root in watchRoots.Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    var watcher = new FileSystemWatcher(root)
                    {
                        IncludeSubdirectories = true,
                        NotifyFilter = NotifyFilters.DirectoryName | NotifyFilters.FileName,
                        EnableRaisingEvents = true
                    };

                    watcher.Created += (_, e) =>
                    {
                        if (ShouldIgnoreNoise(e.FullPath)) return;
                        if (Directory.Exists(e.FullPath))
                            _liveDirs.Add(e.FullPath);
                        else
                            _liveFiles.Add(e.FullPath);

                        OnLiveEvent?.Invoke($"+ {e.FullPath}");
                    };

                    _watchers.Add(watcher);
                }
                catch { }
            }
        });
    }

    public Task<InstallTrace> StopAndSaveAsync()
    {
        if (_beforeSnapshot == null)
            throw new InvalidOperationException("Aktif bir kurulum izleme oturumu yok.");

        return Task.Run(() =>
        {
            DisposeWatchers();
            var after = SystemSnapshot.Capture();
            var trace = SnapshotDiffer.ComputeDiff(
                _activeTraceName,
                _activeInstallerPath,
                _beforeSnapshot,
                after,
                _liveFiles,
                _liveDirs);

            _beforeSnapshot = null;

            Directory.CreateDirectory(TracesRoot);
            string fileName = $"{trace.TraceId}_{Sanitize(trace.TraceName)}.trace.json";
            string filePath = Path.Combine(TracesRoot, fileName);
            File.WriteAllText(filePath, JsonSerializer.Serialize(trace, JsonOptions));

            return trace;
        });
    }

    public IReadOnlyList<InstallTrace> ListTraces()
    {
        var list = new List<InstallTrace>();
        if (!Directory.Exists(TracesRoot)) return list;

        foreach (string file in Directory.EnumerateFiles(TracesRoot, "*.trace.json"))
        {
            try
            {
                var trace = JsonSerializer.Deserialize<InstallTrace>(File.ReadAllText(file), JsonOptions);
                if (trace != null) list.Add(trace);
            }
            catch { }
        }

        return list.OrderByDescending(t => t.StartedAt).ToList();
    }

    public bool DeleteTrace(string traceId)
    {
        if (!Directory.Exists(TracesRoot)) return false;
        foreach (string file in Directory.EnumerateFiles(TracesRoot, $"{traceId}_*.trace.json"))
        {
            File.Delete(file);
            return true;
        }
        return false;
    }

    public static List<LeftoverItem> ConvertTraceToLeftovers(InstallTrace trace)
    {
        var items = new List<LeftoverItem>();

        foreach (string dir in trace.AddedDirectories)
        {
            if (SafetyGuard.IsProtectedFilePath(dir) || !Directory.Exists(dir)) continue;
            var item = new LeftoverItem
            {
                Category = LeftoverCategory.Directory,
                Path = dir,
                SizeBytes = FileSystemScanner.ComputeDirectorySizeSafe(dir)
            };
            RiskScorer.ApplyScore(item, MatchStrength.TraceExact);
            items.Add(item);
        }

        foreach (string file in trace.AddedFiles)
        {
            if (SafetyGuard.IsProtectedFilePath(file) || !File.Exists(file)) continue;
            var item = new LeftoverItem
            {
                Category = LeftoverCategory.File,
                Path = file,
                SizeBytes = FileSystemScanner.GetFileSizeSafe(file)
            };
            RiskScorer.ApplyScore(item, MatchStrength.TraceExact);
            items.Add(item);
        }

        foreach (string regKey in trace.AddedRegistryKeys)
        {
            if (SafetyGuard.IsProtectedRegistryPath(regKey)) continue;
            var item = new LeftoverItem
            {
                Category = LeftoverCategory.RegistryKey,
                Path = regKey
            };
            RiskScorer.ApplyScore(item, MatchStrength.TraceExact);
            items.Add(item);
        }

        foreach (var regVal in trace.AddedRegistryValues)
        {
            if (SafetyGuard.IsProtectedRegistryPath(regVal.KeyPath, isValueTarget: true)) continue;
            var item = new LeftoverItem
            {
                Category = LeftoverCategory.RegistryValue,
                Path = regVal.KeyPath,
                ValueName = regVal.ValueName
            };
            RiskScorer.ApplyScore(item, MatchStrength.TraceExact);
            items.Add(item);
        }

        return items;
    }

    private static bool ShouldIgnoreNoise(string path)
    {
        return path.Contains(@"\CleanSweep\", StringComparison.OrdinalIgnoreCase) ||
               path.Contains(@"\Temp\", StringComparison.OrdinalIgnoreCase) ||
               path.Contains(@"\Microsoft\Windows\", StringComparison.OrdinalIgnoreCase);
    }

    private static string Sanitize(string s)
    {
        char[] invalid = Path.GetInvalidFileNameChars();
        string cleaned = new(s.Where(c => !invalid.Contains(c) && c != ' ').Take(24).ToArray());
        return string.IsNullOrWhiteSpace(cleaned) ? "trace" : cleaned;
    }

    private void DisposeWatchers()
    {
        foreach (var w in _watchers)
        {
            try
            {
                w.EnableRaisingEvents = false;
                w.Dispose();
            }
            catch { }
        }
        _watchers.Clear();
    }

    public void Dispose() => DisposeWatchers();
}
