using System.Xml.Linq;
using CleanSweep.Core.Models;
using CleanSweep.Core.Safety;
using Microsoft.Win32;

namespace CleanSweep.Core.Scanning;

public static class SystemArtifactScanner
{
    public static List<LeftoverItem> Scan(
        InstalledApp target,
        IReadOnlyList<CommunityRule> matchedRules,
        CancellationToken ct)
    {
        var items = new List<LeftoverItem>();
        string installLoc = SafetyGuard.NormalizeFilePath(target.InstallLocation);

        ScanServices(installLoc, matchedRules, items, ct);
        ScanScheduledTasks(installLoc, matchedRules, items, ct);
        ScanEnvironmentPath(installLoc, items);
        ScanFirewallRules(installLoc, items);

        return items;
    }

    private static void ScanServices(
        string installLoc,
        IReadOnlyList<CommunityRule> rules,
        List<LeftoverItem> items,
        CancellationToken ct)
    {
        var ruleServices = rules.SelectMany(r => r.Services).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrEmpty(installLoc) && ruleServices.Count == 0) return;

        try
        {
            using var servicesKey = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services", writable: false);
            if (servicesKey == null) return;

            foreach (string svcName in servicesKey.GetSubKeyNames())
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    using var svc = servicesKey.OpenSubKey(svcName, writable: false);
                    if (svc == null) continue;

                    string imagePath = svc.GetValue("ImagePath")?.ToString() ?? string.Empty;
                    bool matchesLoc = !string.IsNullOrEmpty(installLoc) &&
                                      imagePath.Contains(installLoc, StringComparison.OrdinalIgnoreCase);
                    bool matchesRule = ruleServices.Contains(svcName);

                    if (matchesLoc || matchesRule)
                    {
                        var item = new LeftoverItem
                        {
                            Category = LeftoverCategory.Service,
                            Path = svcName,
                            RuleSource = matchesRule ? rules.First(r => r.Services.Contains(svcName, StringComparer.OrdinalIgnoreCase)).Id : null
                        };
                        RiskScorer.ApplyScore(
                            item,
                            matchesLoc ? MatchStrength.PathReferenceExact : MatchStrength.CommunityRule,
                            customReason: matchesLoc
                                ? $"Windows Servisi ({svcName}) doğrudan uygulamanın kurulum klasöründeki yürütülebilir dosyaya bağlı."
                                : $"Topluluk kuralında belirtilen Windows Servisi ({svcName}).");
                        items.Add(item);
                    }
                }
                catch { }
            }
        }
        catch { }
    }

    private static void ScanScheduledTasks(
        string installLoc,
        IReadOnlyList<CommunityRule> rules,
        List<LeftoverItem> items,
        CancellationToken ct)
    {
        var ruleTasks = rules.SelectMany(r => r.ScheduledTasks).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrEmpty(installLoc) && ruleTasks.Count == 0) return;

        string tasksDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "Tasks");
        if (!Directory.Exists(tasksDir)) return;

        try
        {
            foreach (string file in Directory.EnumerateFiles(tasksDir, "*", SearchOption.AllDirectories))
            {
                ct.ThrowIfCancellationRequested();
                string relative = "\\" + Path.GetRelativePath(tasksDir, file);
                if (relative.StartsWith(@"\Microsoft\Windows\", StringComparison.OrdinalIgnoreCase))
                    continue;

                string taskName = Path.GetFileName(file);
                bool matchesRule = ruleTasks.Contains(taskName) || ruleTasks.Contains(relative);
                bool matchesLoc = false;

                if (!string.IsNullOrEmpty(installLoc))
                {
                    try
                    {
                        var doc = XDocument.Load(file);
                        string commands = string.Join(" ", doc.Descendants().Where(e => e.Name.LocalName == "Command").Select(e => e.Value));
                        if (commands.Contains(installLoc, StringComparison.OrdinalIgnoreCase))
                            matchesLoc = true;
                    }
                    catch { }
                }

                if (matchesLoc || matchesRule)
                {
                    var item = new LeftoverItem
                    {
                        Category = LeftoverCategory.ScheduledTask,
                        Path = relative
                    };
                    RiskScorer.ApplyScore(
                        item,
                        matchesLoc ? MatchStrength.PathReferenceExact : MatchStrength.CommunityRule,
                        customReason: matchesLoc
                            ? "Zamanlanmış Görev (Scheduled Task) uygulamanın kurulum yolundaki bir komutu çalıştırıyor."
                            : "Topluluk kuralında belirtilen Zamanlanmış Görev.");
                    items.Add(item);
                }
            }
        }
        catch { }
    }

    private static void ScanEnvironmentPath(string installLoc, List<LeftoverItem> items)
    {
        if (string.IsNullOrEmpty(installLoc)) return;

        CheckPathScope(EnvironmentVariableTarget.User, @"HKCU\Environment", installLoc, items);
        CheckPathScope(EnvironmentVariableTarget.Machine, @"HKLM\SYSTEM\CurrentControlSet\Control\Session Manager\Environment", installLoc, items);
    }

    private static void CheckPathScope(EnvironmentVariableTarget target, string regScope, string installLoc, List<LeftoverItem> items)
    {
        try
        {
            string? pathVar = Environment.GetEnvironmentVariable("PATH", target);
            if (string.IsNullOrWhiteSpace(pathVar)) return;

            foreach (string segment in pathVar.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                string normSeg = SafetyGuard.NormalizeFilePath(segment);
                if (normSeg.Equals(installLoc, StringComparison.OrdinalIgnoreCase) ||
                    normSeg.StartsWith(installLoc + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                {
                    var item = new LeftoverItem
                    {
                        Category = LeftoverCategory.EnvironmentPath,
                        Path = normSeg,
                        ValueName = regScope
                    };
                    RiskScorer.ApplyScore(item, MatchStrength.PathReferenceExact,
                        customReason: $"Ortam değişkeni (PATH - {target}) içindeki kalıntı yol.");
                    items.Add(item);
                }
            }
        }
        catch { }
    }

    private static void ScanFirewallRules(string installLoc, List<LeftoverItem> items)
    {
        if (string.IsNullOrEmpty(installLoc)) return;
        const string fwKeyPath = @"SYSTEM\CurrentControlSet\Services\SharedAccess\Parameters\FirewallPolicy\FirewallRules";
        try
        {
            using var fwKey = Registry.LocalMachine.OpenSubKey(fwKeyPath, writable: false);
            if (fwKey == null) return;

            foreach (string ruleId in fwKey.GetValueNames())
            {
                string ruleVal = fwKey.GetValue(ruleId)?.ToString() ?? string.Empty;
                if (ruleVal.Contains(installLoc, StringComparison.OrdinalIgnoreCase))
                {
                    string ruleName = ExtractRuleName(ruleVal) ?? ruleId;
                    var item = new LeftoverItem
                    {
                        Category = LeftoverCategory.FirewallRule,
                        Path = $@"HKLM\{fwKeyPath}",
                        ValueName = ruleId
                    };
                    RiskScorer.ApplyScore(item, MatchStrength.PathReferenceExact,
                        customReason: $"Windows Güvenlik Duvarı kuralı ({ruleName}) uygulamanın kurulum yoluna bağlı.");
                    items.Add(item);
                }
            }
        }
        catch { }
    }

    private static string? ExtractRuleName(string rulePayload)
    {
        foreach (string part in rulePayload.Split('|'))
        {
            if (part.StartsWith("Name=", StringComparison.OrdinalIgnoreCase))
                return part[5..];
        }
        return null;
    }
}
