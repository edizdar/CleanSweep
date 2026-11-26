using System.Text.Json;
using CleanSweep.Core.Models;

namespace CleanSweep.Core.Scanning;

public sealed class RuleRepository
{
    public string RulesDirectory { get; }

    public RuleRepository(string? rulesDirectory = null)
    {
        RulesDirectory = rulesDirectory ?? ResolveDefaultRulesDirectory();
    }

    public static string ResolveDefaultRulesDirectory()
    {
        string baseRules = Path.Combine(AppContext.BaseDirectory, "rules");
        if (Directory.Exists(baseRules)) return baseRules;

        string? current = AppContext.BaseDirectory;
        for (int i = 0; i < 6 && !string.IsNullOrEmpty(current); i++)
        {
            string candidate = Path.Combine(current, "rules");
            if (Directory.Exists(candidate)) return candidate;
            current = Directory.GetParent(current)?.FullName;
        }

        return baseRules;
    }

    public IReadOnlyList<CommunityRule> LoadAllRules()
    {
        var rules = new List<CommunityRule>();
        if (!Directory.Exists(RulesDirectory)) return rules;

        foreach (string file in Directory.EnumerateFiles(RulesDirectory, "*.json"))
        {
            string fileName = Path.GetFileName(file);
            if (fileName.Equals("schema.json", StringComparison.OrdinalIgnoreCase)) continue;

            try
            {
                string json = File.ReadAllText(file);
                var rule = JsonSerializer.Deserialize<CommunityRule>(json);
                if (rule != null && !string.IsNullOrWhiteSpace(rule.Id))
                {
                    rules.Add(rule);
                }
            }
            catch
            {
                // Skip malformed rule files gracefully
            }
        }

        return rules.OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public IReadOnlyList<CommunityRule> FindMatchingRules(InstalledApp app)
    {
        var all = LoadAllRules();
        var matches = new List<CommunityRule>();

        foreach (var rule in all)
        {
            bool nameMatch = rule.MatchNames.Any(mn =>
                app.Name.Equals(mn, StringComparison.OrdinalIgnoreCase) ||
                app.Name.Contains(mn, StringComparison.OrdinalIgnoreCase));

            bool ruleNameMatch = app.Name.Equals(rule.Name, StringComparison.OrdinalIgnoreCase);

            if (nameMatch || ruleNameMatch)
            {
                matches.Add(rule);
            }
        }

        return matches;
    }

    public static string ExpandPathVariables(string templatePath)
    {
        if (string.IsNullOrWhiteSpace(templatePath)) return string.Empty;

        string result = templatePath;
        var customVars = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["%PROGRAMFILES%"] = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            ["%PROGRAMFILES(X86)%"] = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            ["%PROGRAMDATA%"] = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            ["%APPDATA%"] = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            ["%LOCALAPPDATA%"] = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            ["%USERPROFILE%"] = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
        };

        foreach (var kvp in customVars)
        {
            if (!string.IsNullOrEmpty(kvp.Value))
                result = result.Replace(kvp.Key, kvp.Value, StringComparison.OrdinalIgnoreCase);
        }

        return Environment.ExpandEnvironmentVariables(result);
    }
}
