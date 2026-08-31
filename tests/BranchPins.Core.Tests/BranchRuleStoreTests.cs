using BranchPins.Core;

namespace BranchPins.Core.Tests;

public sealed class BranchPinsConfigurationStoreTests : IDisposable
{
    private readonly string temporaryDirectory = Path.Combine(Path.GetTempPath(), "BranchPins.Tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void Load_CreatesDefaultConfigurationFile()
    {
        string configPath = Path.Combine(temporaryDirectory, "config.json");
        BranchPinsConfiguration configuration = new BranchPinsConfigurationStore(configPath).Load();

        Assert.True(File.Exists(configPath));
        Assert.True(configuration.Enabled);
        Assert.Equal(25, configuration.MaximumRestoredTabs);
        Assert.Equal(500, configuration.RestoreDelayMilliseconds);
    }

    [Fact]
    public void GetMatchingRule_PrefersTheMostSpecificMatchingPattern()
    {
        BranchPinsConfigurationStore store = new BranchPinsConfigurationStore(Path.Combine(temporaryDirectory, "config.json"));
        store.Save(new BranchPinsConfiguration
        {
            Rules = new List<BranchRuleConfiguration>
            {
                new BranchRuleConfiguration { Pattern = "feature/*", PinSetName = "all-features", Enabled = true },
                new BranchRuleConfiguration { Pattern = "feature/login", PinSetName = "login", Enabled = true },
            },
        });

        BranchRuleConfiguration? rule = GetMatchingRule(store.Load(), "feature/login");

        Assert.NotNull(rule);
        Assert.Equal("login", rule.PinSetName);
    }

    [Fact]
    public void GetMatchingRule_ReturnsDisabledRule()
    {
        BranchPinsConfigurationStore store = new BranchPinsConfigurationStore(Path.Combine(temporaryDirectory, "config.json"));
        store.Save(new BranchPinsConfiguration
        {
            Rules = new List<BranchRuleConfiguration> { new BranchRuleConfiguration { Pattern = "release/*", PinSetName = "release", Enabled = false } },
        });

        BranchRuleConfiguration? rule = GetMatchingRule(store.Load(), "release/1.0");

        Assert.NotNull(rule);
        Assert.False(rule.Enabled);
    }

    public void Dispose()
    {
        if (Directory.Exists(temporaryDirectory))
        {
            Directory.Delete(temporaryDirectory, recursive: true);
        }
    }

    private static BranchRuleConfiguration? GetMatchingRule(BranchPinsConfiguration configuration, string branchName)
    {
        return configuration.Rules
            .Where(rule => new BranchRule(rule.Pattern, rule.PinSetName, rule.Enabled).IsMatch(branchName))
            .OrderByDescending(rule => rule.Pattern.Length)
            .FirstOrDefault();
    }
}
