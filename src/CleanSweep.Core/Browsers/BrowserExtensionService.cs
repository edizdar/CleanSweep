using System.Text.Json;
using CleanSweep.Core.Models;
using CleanSweep.Core.Quarantine;
using CleanSweep.Core.Safety;

namespace CleanSweep.Core.Browsers;

public sealed class BrowserExtensionService
{
    public Task<IReadOnlyList<BrowserExtension>> ScanAllAsync(CancellationToken ct = default)
    {
        return Task.Run(() =>
        {
            var results = new List<BrowserExtension>();
            string localApp = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

            var chromiumRoots = new (string Browser, string UserDataDir)[]
            {
                ("Google Chrome", Path.Combine(localApp, "Google", "Chrome", "User Data")),
                ("Microsoft Edge", Path.Combine(localApp, "Microsoft", "Edge", "User Data")),
                ("Brave", Path.Combine(localApp, "BraveSoftware", "Brave-Browser", "User Data")),
                ("Vivaldi", Path.Combine(localApp, "Vivaldi", "User Data")),
                ("Opera", Path.Combine(appData, "Opera Software", "Opera Stable"))
            };

            foreach (var (browser, userData) in chromiumRoots)
            {
                ct.ThrowIfCancellationRequested();
                results.AddRange(ScanChromiumUserDataRoot(browser, userData, ct));
            }

            results.AddRange(ScanFirefoxProfiles(Path.Combine(appData, "Mozilla", "Firefox", "Profiles"), ct));

            return (IReadOnlyList<BrowserExtension>)results
                .OrderBy(e => e.Browser, StringComparer.OrdinalIgnoreCase)
                .ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }, ct);
    }

    public static List<BrowserExtension> ScanChromiumUserDataRoot(
        string browserName,
        string userDataRoot,
        CancellationToken ct = default)
    {
        var list = new List<BrowserExtension>();
        if (string.IsNullOrWhiteSpace(userDataRoot) || !Directory.Exists(userDataRoot))
            return list;

        var profileDirs = new List<string>();
        string defaultExt = Path.Combine(userDataRoot, "Default", "Extensions");
        if (Directory.Exists(defaultExt))
        {
            profileDirs.Add(Path.Combine(userDataRoot, "Default"));
        }
        else if (Directory.Exists(Path.Combine(userDataRoot, "Extensions")))
        {
            profileDirs.Add(userDataRoot);
        }

        try
        {
            foreach (string dir in Directory.EnumerateDirectories(userDataRoot, "Profile *"))
            {
                if (Directory.Exists(Path.Combine(dir, "Extensions")))
                    profileDirs.Add(dir);
            }
        }
        catch { }

        foreach (string profileDir in profileDirs.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            ct.ThrowIfCancellationRequested();
            string profileName = Path.GetFileName(profileDir);
            string extRoot = Path.Combine(profileDir, "Extensions");
            if (!Directory.Exists(extRoot)) continue;

            try
            {
                foreach (string extDir in Directory.EnumerateDirectories(extRoot))
                {
                    ct.ThrowIfCancellationRequested();
                    string extId = Path.GetFileName(extDir);
                    if (extId.Equals("Temp", StringComparison.OrdinalIgnoreCase)) continue;

                    string? latestVersionDir = Directory.EnumerateDirectories(extDir)
                        .OrderByDescending(d => Path.GetFileName(d), StringComparer.OrdinalIgnoreCase)
                        .FirstOrDefault();

                    if (latestVersionDir == null) continue;
                    string manifestPath = Path.Combine(latestVersionDir, "manifest.json");
                    if (!File.Exists(manifestPath)) continue;

                    try
                    {
                        using var doc = JsonDocument.Parse(File.ReadAllText(manifestPath));
                        var root = doc.RootElement;
                        string rawName = GetJsonString(root, "name");
                        string rawDesc = GetJsonString(root, "description");
                        string version = GetJsonString(root, "version");
                        string defaultLocale = GetJsonString(root, "default_locale");

                        string resolvedName = ResolveLocaleString(latestVersionDir, rawName, defaultLocale);
                        if (string.IsNullOrWhiteSpace(resolvedName)) resolvedName = extId;
                        string resolvedDesc = ResolveLocaleString(latestVersionDir, rawDesc, defaultLocale);

                        list.Add(new BrowserExtension
                        {
                            Id = extId,
                            Name = resolvedName,
                            Browser = browserName,
                            ProfileName = profileName,
                            Version = version,
                            Description = resolvedDesc,
                            ExtensionPath = extDir,
                            IsRemovable = true
                        });
                    }
                    catch { }
                }
            }
            catch { }
        }

        return list;
    }

    public static List<BrowserExtension> ScanFirefoxProfiles(string profilesRoot, CancellationToken ct = default)
    {
        var list = new List<BrowserExtension>();
        if (!Directory.Exists(profilesRoot)) return list;

        try
        {
            foreach (string profileDir in Directory.EnumerateDirectories(profilesRoot))
            {
                ct.ThrowIfCancellationRequested();
                string extJson = Path.Combine(profileDir, "extensions.json");
                if (!File.Exists(extJson)) continue;

                try
                {
                    using var doc = JsonDocument.Parse(File.ReadAllText(extJson));
                    if (!doc.RootElement.TryGetProperty("addons", out var addons) || addons.ValueKind != JsonValueKind.Array)
                        continue;

                    string profileName = Path.GetFileName(profileDir);
                    foreach (var addon in addons.EnumerateArray())
                    {
                        string location = GetJsonString(addon, "location");
                        if (!location.Equals("app-profile", StringComparison.OrdinalIgnoreCase)) continue;

                        string id = GetJsonString(addon, "id");
                        string version = GetJsonString(addon, "version");
                        string path = GetJsonString(addon, "path");
                        string name = id;
                        string desc = string.Empty;

                        if (addon.TryGetProperty("defaultLocale", out var loc))
                        {
                            string locName = GetJsonString(loc, "name");
                            if (!string.IsNullOrWhiteSpace(locName)) name = locName;
                            desc = GetJsonString(loc, "description");
                        }

                        list.Add(new BrowserExtension
                        {
                            Id = id,
                            Name = name,
                            Browser = "Mozilla Firefox",
                            ProfileName = profileName,
                            Version = version,
                            Description = desc,
                            ExtensionPath = path,
                            IsRemovable = !string.IsNullOrWhiteSpace(path)
                        });
                    }
                }
                catch { }
            }
        }
        catch { }

        return list;
    }

    public OperationResult RemoveExtension(BrowserExtension extension, QuarantineManager? quarantineManager = null)
    {
        var result = new OperationResult();
        if (string.IsNullOrWhiteSpace(extension.ExtensionPath))
        {
            result.Success = false;
            result.Summary = "Eklenti yolu bulunamadı.";
            return result;
        }

        string normPath = SafetyGuard.NormalizeFilePath(extension.ExtensionPath);
        if (SafetyGuard.IsProtectedFilePath(normPath))
        {
            result.Success = false;
            result.Summary = "Korunan sistem yolu olduğu için engellendi.";
            return result;
        }

        bool isDir = Directory.Exists(normPath);
        bool isFile = File.Exists(normPath);
        if (!isDir && !isFile)
        {
            result.Success = true;
            result.Summary = "Eklenti dosyaları zaten kaldırılmış.";
            return result;
        }

        if (quarantineManager != null)
        {
            var session = quarantineManager.CreateSession($"Ext_{extension.Browser}_{extension.Name}",
            [
                new LeftoverItem
                {
                    Category = isDir ? LeftoverCategory.Directory : LeftoverCategory.File,
                    Path = normPath
                }
            ]);
            result.QuarantineSessionId = session.SessionId;
        }

        try
        {
            if (isDir)
                Directory.Delete(normPath, recursive: true);
            else
                File.Delete(normPath);

            result.Success = true;
            result.Summary = $"'{extension.Name}' ({extension.Browser}) eklentisi kaldırıldı.";
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.Summary = $"Eklenti silinemedi (tarayıcı açık olabilir): {ex.Message}";
            result.Errors.Add(ex.Message);
        }

        return result;
    }

    public static string ResolveLocaleString(string versionDir, string rawValue, string? defaultLocale)
    {
        if (string.IsNullOrWhiteSpace(rawValue)) return string.Empty;
        if (!rawValue.StartsWith("__MSG_", StringComparison.Ordinal) || !rawValue.EndsWith("__", StringComparison.Ordinal))
            return rawValue;

        string key = rawValue[6..^2];
        string[] candidateLocales = new[] { "tr", defaultLocale, "en", "en_US", "en_GB" }
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray()!;

        foreach (string loc in candidateLocales)
        {
            string msgPath = Path.Combine(versionDir, "_locales", loc, "messages.json");
            if (!File.Exists(msgPath)) continue;

            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(msgPath));
                foreach (var prop in doc.RootElement.EnumerateObject())
                {
                    if (prop.Name.Equals(key, StringComparison.OrdinalIgnoreCase) &&
                        prop.Value.TryGetProperty("message", out var msgEl) &&
                        msgEl.ValueKind == JsonValueKind.String)
                    {
                        return msgEl.GetString() ?? rawValue;
                    }
                }
            }
            catch { }
        }

        return rawValue;
    }

    private static string GetJsonString(JsonElement el, string prop)
    {
        return el.TryGetProperty(prop, out var val) && val.ValueKind == JsonValueKind.String
            ? val.GetString() ?? string.Empty
            : string.Empty;
    }
}
