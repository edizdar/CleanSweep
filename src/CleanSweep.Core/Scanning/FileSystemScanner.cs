using CleanSweep.Core.Models;
using CleanSweep.Core.Native;
using CleanSweep.Core.Safety;

namespace CleanSweep.Core.Scanning;

public static class FileSystemScanner
{
    public static List<LeftoverItem> Scan(
        InstalledApp target,
        IReadOnlyList<string> searchTokens,
        bool hasOtherSamePublisherApps,
        CancellationToken cancellationToken,
        ScanOptions? options = null)
    {
        options ??= new ScanOptions();
        var items = new List<LeftoverItem>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // 1. Exact InstallLocation check
        if (!string.IsNullOrWhiteSpace(target.InstallLocation))
        {
            string normInstall = SafetyGuard.NormalizeFilePath(target.InstallLocation);
            if (!SafetyGuard.IsProtectedFilePath(normInstall) && Directory.Exists(normInstall) && seen.Add(normInstall))
            {
                var item = new LeftoverItem
                {
                    Category = LeftoverCategory.Directory,
                    Path = normInstall,
                    SizeBytes = ComputeDirectorySizeSafe(normInstall, cancellationToken),
                    LockingProcesses = ProbeDirectoryLocks(normInstall)
                };
                RiskScorer.ApplyScore(item, MatchStrength.InstallLocationExact);
                items.Add(item);
            }
        }

        // 1b. MSIX user package data folder (%LOCALAPPDATA%\Packages\<PackageFamilyName>)
        if (target.Source == AppSource.Msix && !string.IsNullOrWhiteSpace(target.PackageFamilyName))
        {
            string pkgData = SafetyGuard.NormalizeFilePath(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Packages",
                target.PackageFamilyName));
            if (!SafetyGuard.IsProtectedFilePath(pkgData) && Directory.Exists(pkgData) && seen.Add(pkgData))
            {
                var item = new LeftoverItem
                {
                    Category = LeftoverCategory.Directory,
                    Path = pkgData,
                    SizeBytes = ComputeDirectorySizeSafe(pkgData, cancellationToken),
                    LockingProcesses = ProbeDirectoryLocks(pkgData)
                };
                RiskScorer.ApplyScore(item, MatchStrength.InstallLocationExact,
                    customReason: "Microsoft Store (MSIX) uygulamasının kullanıcı veri ve önbellek klasörü.");
                items.Add(item);
            }
        }

        // 2. Targeted roots
        string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string[] roots =
        [
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            Path.Combine(userProfile, "AppData", "LocalLow"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs"),
            Environment.GetFolderPath(Environment.SpecialFolder.Programs),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms)
        ];

        int maxDepth = options.Depth == ScanDepth.Advanced ? 3 : 2;
        string normAppName = target.Name.Trim();
        string normPub = SafetyGuard.NormalizePublisher(target.Publisher);

        foreach (string root in roots.Where(r => !string.IsNullOrWhiteSpace(r) && Directory.Exists(r)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            ScanDirectoryLevel(root, depth: 0, maxDepth, target, normAppName, normPub, searchTokens, hasOtherSamePublisherApps, seen, items, cancellationToken);
        }

        // 2b. Advanced: also scan %TEMP% and %USERPROFILE% top-level leftovers
        if (options.Depth == ScanDepth.Advanced)
        {
            string[] extraShallowRoots = [Path.GetTempPath(), userProfile];
            foreach (string extraRoot in extraShallowRoots.Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                ScanDirectoryLevel(extraRoot, depth: 0, maxDepth: 1, target, normAppName, normPub, searchTokens, hasOtherSamePublisherApps, seen, items, cancellationToken);
            }
        }

        // 3. Desktop & Start Menu shortcuts (.lnk files)
        string[] shortcutRoots =
        [
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory),
            Environment.GetFolderPath(Environment.SpecialFolder.Programs),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"Microsoft\Internet Explorer\Quick Launch")
        ];

        foreach (string sRoot in shortcutRoots.Where(r => !string.IsNullOrWhiteSpace(r) && Directory.Exists(r)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                foreach (string lnk in Directory.EnumerateFiles(sRoot, "*.lnk", SearchOption.AllDirectories))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    string fileName = Path.GetFileNameWithoutExtension(lnk);
                    if (IsExactOrStrongTokenMatch(fileName, normAppName, searchTokens, out bool isExact))
                    {
                        string normLnk = SafetyGuard.NormalizeFilePath(lnk);
                        if (!SafetyGuard.IsProtectedFilePath(normLnk) && seen.Add(normLnk))
                        {
                            var item = new LeftoverItem
                            {
                                Category = LeftoverCategory.File,
                                Path = normLnk,
                                SizeBytes = GetFileSizeSafe(normLnk)
                            };
                            RiskScorer.ApplyScore(
                                item,
                                isExact ? MatchStrength.ExactAppNameMatch : MatchStrength.PartialTokenMatch,
                                hasOtherSamePublisherApps,
                                isExact ? "Uygulama adıyla eşleşen kısayol dosyası." : "Uygulama adı parçacığıyla eşleşen kısayol dosyası.");
                            items.Add(item);
                        }
                    }
                }
            }
            catch { }
        }

        return items;
    }

    private static void ScanDirectoryLevel(
        string currentDir,
        int depth,
        int maxDepth,
        InstalledApp target,
        string normAppName,
        string normPub,
        IReadOnlyList<string> searchTokens,
        bool hasOtherSamePublisherApps,
        HashSet<string> seen,
        List<LeftoverItem> items,
        CancellationToken ct)
    {
        if (depth >= maxDepth) return;

        IEnumerable<string> subDirs;
        try
        {
            subDirs = Directory.EnumerateDirectories(currentDir);
        }
        catch
        {
            return;
        }

        foreach (string dir in subDirs)
        {
            ct.ThrowIfCancellationRequested();
            string normDir = SafetyGuard.NormalizeFilePath(dir);
            if (seen.Contains(normDir)) continue;

            string dirName = Path.GetFileName(normDir);
            if (string.IsNullOrWhiteSpace(dirName)) continue;

            bool isProtected = SafetyGuard.IsProtectedFilePath(normDir);

            if (!isProtected && IsExactOrStrongTokenMatch(dirName, normAppName, searchTokens, out bool isExact))
            {
                seen.Add(normDir);
                var item = new LeftoverItem
                {
                    Category = LeftoverCategory.Directory,
                    Path = normDir,
                    SizeBytes = ComputeDirectorySizeSafe(normDir, ct),
                    LockingProcesses = ProbeDirectoryLocks(normDir)
                };
                RiskScorer.ApplyScore(item, isExact ? MatchStrength.ExactAppNameMatch : MatchStrength.PartialTokenMatch, hasOtherSamePublisherApps);
                items.Add(item);
                continue;
            }

            if (!isProtected && !string.IsNullOrEmpty(normPub) &&
                SafetyGuard.NormalizePublisher(dirName).Equals(normPub, StringComparison.OrdinalIgnoreCase))
            {
                if (!hasOtherSamePublisherApps)
                {
                    seen.Add(normDir);
                    var item = new LeftoverItem
                    {
                        Category = LeftoverCategory.Directory,
                        Path = normDir,
                        SizeBytes = ComputeDirectorySizeSafe(normDir, ct),
                        LockingProcesses = ProbeDirectoryLocks(normDir)
                    };
                    RiskScorer.ApplyScore(item, MatchStrength.SharedPublisherFolder, hasOtherSamePublisherApps: false);
                    items.Add(item);
                    continue;
                }
                else
                {
                    ScanDirectoryLevel(normDir, depth + 1, maxDepth, target, normAppName, normPub, searchTokens, hasOtherSamePublisherApps, seen, items, ct);
                    continue;
                }
            }

            if (depth + 1 < maxDepth)
            {
                ScanDirectoryLevel(normDir, depth + 1, maxDepth, target, normAppName, normPub, searchTokens, hasOtherSamePublisherApps, seen, items, ct);
            }
        }
    }

    private static bool IsExactOrStrongTokenMatch(string candidateName, string appName, IReadOnlyList<string> tokens, out bool isExact)
    {
        isExact = false;
        if (candidateName.Equals(appName, StringComparison.OrdinalIgnoreCase))
        {
            isExact = true;
            return true;
        }

        string compactCandidate = candidateName.Replace(" ", "").Replace("-", "").Replace("_", "");
        string compactApp = appName.Replace(" ", "").Replace("-", "").Replace("_", "");
        if (compactApp.Length >= 4 && compactCandidate.Equals(compactApp, StringComparison.OrdinalIgnoreCase))
        {
            isExact = true;
            return true;
        }

        foreach (string token in tokens)
        {
            if (token.Length < 4) continue;
            if (candidateName.Equals(token, StringComparison.OrdinalIgnoreCase))
            {
                isExact = true;
                return true;
            }
            if (token.Length >= 6 && candidateName.Contains(token, StringComparison.OrdinalIgnoreCase))
            {
                isExact = false;
                return true;
            }
        }

        return false;
    }

    public static long ComputeDirectorySizeSafe(string dirPath, CancellationToken ct = default)
    {
        long total = 0;
        try
        {
            var stack = new Stack<string>();
            stack.Push(dirPath);
            while (stack.Count > 0)
            {
                ct.ThrowIfCancellationRequested();
                string current = stack.Pop();
                try
                {
                    foreach (string file in Directory.EnumerateFiles(current))
                    {
                        total += GetFileSizeSafe(file);
                    }
                    foreach (string sub in Directory.EnumerateDirectories(current))
                    {
                        var attrs = File.GetAttributes(sub);
                        if ((attrs & FileAttributes.ReparsePoint) == 0)
                            stack.Push(sub);
                    }
                }
                catch { }
            }
        }
        catch { }
        return total;
    }

    public static long GetFileSizeSafe(string filePath)
    {
        try { return new FileInfo(filePath).Length; }
        catch { return 0; }
    }

    private static List<LockingProcessInfo> ProbeDirectoryLocks(string dirPath)
    {
        try
        {
            string[] executables = Directory.EnumerateFiles(dirPath, "*.exe", SearchOption.TopDirectoryOnly)
                .Concat(Directory.EnumerateFiles(dirPath, "*.dll", SearchOption.TopDirectoryOnly))
                .Take(12)
                .ToArray();
            return RestartManagerHelper.GetLockingProcesses(executables);
        }
        catch
        {
            return [];
        }
    }
}
