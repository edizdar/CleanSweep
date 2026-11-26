using CleanSweep.Core.Models;
using CleanSweep.Core.Safety;
using CleanSweep.Core.Scanning;
using Xunit;

namespace CleanSweep.Tests;

public sealed class RuleRepositoryTests
{
    [Fact]
    public void LoadAllRules_LoadsShippedRulesAndValidatesSafety()
    {
        string repoRulesDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "rules"));
        var repo = new RuleRepository(repoRulesDir);
        var rules = repo.LoadAllRules();

        Assert.True(rules.Count >= 6);
        Assert.Contains(rules, r => r.Id == "discord");
        Assert.Contains(rules, r => r.Id == "vscode");

        foreach (var rule in rules)
        {
            Assert.False(string.IsNullOrWhiteSpace(rule.Id));
            Assert.False(string.IsNullOrWhiteSpace(rule.Name));
            foreach (string rawPath in rule.Paths)
            {
                string expanded = RuleRepository.ExpandPathVariables(rawPath);
                Assert.False(expanded.Contains("%APPDATA%", StringComparison.OrdinalIgnoreCase));
                Assert.False(SafetyGuard.IsProtectedFilePath(expanded), $"Rule {rule.Id} contains protected path: {expanded}");
            }
            foreach (string reg in rule.RegistryKeys)
            {
                Assert.False(SafetyGuard.IsProtectedRegistryPath(reg), $"Rule {rule.Id} contains protected registry key: {reg}");
            }
        }
    }

    [Fact]
    public void FindMatchingRules_MatchesDiscordApp()
    {
        string repoRulesDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "rules"));
        var repo = new RuleRepository(repoRulesDir);
        var matches = repo.FindMatchingRules(new InstalledApp { Name = "Discord", Publisher = "Discord Inc." });

        Assert.Contains(matches, m => m.Id == "discord");
    }
}
