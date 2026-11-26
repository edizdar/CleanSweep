using CleanSweep.Core.Models;
using CleanSweep.Core.Safety;

namespace CleanSweep.Core.Tracing;

public static class SnapshotDiffer
{
    public static InstallTrace ComputeDiff(
        string traceName,
        string? installerPath,
        SystemSnapshot before,
        SystemSnapshot after,
        IEnumerable<string>? liveAddedFiles = null,
        IEnumerable<string>? liveAddedDirs = null)
    {
        var addedDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string dir in after.Directories)
        {
            if (!before.Directories.Contains(dir) && !SafetyGuard.IsProtectedFilePath(dir))
                addedDirs.Add(dir);
        }
        if (liveAddedDirs != null)
        {
            foreach (string dir in liveAddedDirs)
            {
                string norm = SafetyGuard.NormalizeFilePath(dir);
                if (!before.Directories.Contains(norm) && !SafetyGuard.IsProtectedFilePath(norm) && Directory.Exists(norm))
                    addedDirs.Add(norm);
            }
        }

        // Collapse child dirs if parent dir is already in addedDirs
        var topAddedDirs = CollapseNestedPaths(addedDirs);

        var addedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string file in after.Files)
        {
            if (!before.Files.Contains(file) && !SafetyGuard.IsProtectedFilePath(file) && !IsUnderAnyDir(file, topAddedDirs))
                addedFiles.Add(file);
        }
        if (liveAddedFiles != null)
        {
            foreach (string file in liveAddedFiles)
            {
                string norm = SafetyGuard.NormalizeFilePath(file);
                if (!before.Files.Contains(norm) && !SafetyGuard.IsProtectedFilePath(norm) && !IsUnderAnyDir(norm, topAddedDirs) && File.Exists(norm))
                    addedFiles.Add(norm);
            }
        }

        var addedRegKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string key in after.RegistryKeys)
        {
            if (!before.RegistryKeys.Contains(key) && !SafetyGuard.IsProtectedRegistryPath(key))
                addedRegKeys.Add(key);
        }

        var addedRegValues = new List<TracedRegistryValue>();
        foreach (var kvp in after.RegistryValues)
        {
            before.RegistryValues.TryGetValue(kvp.Key, out var beforeVals);
            foreach (string valName in kvp.Value)
            {
                if (beforeVals == null || !beforeVals.Contains(valName))
                {
                    addedRegValues.Add(new TracedRegistryValue
                    {
                        KeyPath = kvp.Key,
                        ValueName = valName
                    });
                }
            }
        }

        return new InstallTrace
        {
            TraceName = traceName,
            InstallerPath = installerPath,
            StartedAt = before.CapturedAt,
            CompletedAt = after.CapturedAt,
            AddedDirectories = topAddedDirs.OrderBy(d => d, StringComparer.OrdinalIgnoreCase).ToList(),
            AddedFiles = addedFiles.OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToList(),
            AddedRegistryKeys = addedRegKeys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase).ToList(),
            AddedRegistryValues = addedRegValues
        };
    }

    private static List<string> CollapseNestedPaths(IEnumerable<string> paths)
    {
        var sorted = paths.OrderBy(p => p.Length).ToList();
        var result = new List<string>();
        foreach (string p in sorted)
        {
            if (!IsUnderAnyDir(p, result))
                result.Add(p);
        }
        return result;
    }

    private static bool IsUnderAnyDir(string path, IEnumerable<string> dirs)
    {
        foreach (string d in dirs)
        {
            if (path.Equals(d, StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith(d + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }
}
