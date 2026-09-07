using Tabkeeper.Core;

namespace Tabkeeper.Core.Tests;

public sealed class TabkeeperConfigurationStoreTests : IDisposable
{
    private readonly string temporaryDirectory = Path.Combine(Path.GetTempPath(), "Tabkeeper.Tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void Load_CreatesDefaultConfigurationFile()
    {
        string configPath = Path.Combine(temporaryDirectory, "config.json");
        TabkeeperConfiguration configuration = new TabkeeperConfigurationStore(configPath).Load();

        Assert.True(File.Exists(configPath));
        Assert.True(configuration.Enabled);
        Assert.Equal(25, configuration.MaximumRestoredTabs);
        Assert.Equal(500, configuration.RestoreDelayMilliseconds);
    }

    [Fact]
    public void Load_RecordsErrorAndKeepsLastValidConfigurationWhenTheFileIsUnparseable()
    {
        string configPath = Path.Combine(temporaryDirectory, "config.json");
        TabkeeperConfigurationStore store = new TabkeeperConfigurationStore(configPath);
        store.Save(new TabkeeperConfiguration { MaximumRestoredTabs = 7 });

        // Re-read so the valid configuration is cached, then corrupt the file as a hand edit might.
        Assert.Equal(7, store.Load().MaximumRestoredTabs);
        File.WriteAllText(configPath, "{ not valid json ,, }");

        TabkeeperConfiguration afterCorruption = store.Load();

        Assert.NotNull(store.LastLoadError);
        Assert.Contains(configPath, store.LastLoadError);
        Assert.Equal(7, afterCorruption.MaximumRestoredTabs);

        // Repairing the file clears the error on the next read.
        store.Save(new TabkeeperConfiguration { MaximumRestoredTabs = 9 });
        Assert.Null(store.LastLoadError);
        Assert.Equal(9, store.Load().MaximumRestoredTabs);
    }

    [Fact]
    public void Load_UsesDefaultsAndRecordsErrorWhenTheFirstReadIsUnparseable()
    {
        string configPath = Path.Combine(temporaryDirectory, "config.json");
        Directory.CreateDirectory(temporaryDirectory);
        File.WriteAllText(configPath, "}{");

        TabkeeperConfiguration configuration = new TabkeeperConfigurationStore(configPath).Load();

        Assert.Equal(new TabkeeperConfiguration().MaximumRestoredTabs, configuration.MaximumRestoredTabs);
    }

    [Fact]
    public void BranchRuleConfiguration_RoundTripsRulesThroughJsonString()
    {
        List<BranchRuleConfiguration> rules = new List<BranchRuleConfiguration>
        {
            new BranchRuleConfiguration { Pattern = "feature/*", PinSetName = "feat", Enabled = true },
            new BranchRuleConfiguration { Pattern = "release/*", PinSetName = "rel", Enabled = false },
        };

        string json = BranchRuleConfiguration.SerializeList(rules);
        List<BranchRuleConfiguration> parsed = BranchRuleConfiguration.ParseList(json);

        Assert.Equal(2, parsed.Count);
        Assert.Equal("feature/*", parsed[0].Pattern);
        Assert.Equal("feat", parsed[0].PinSetName);
        Assert.False(parsed[1].Enabled);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("[]")]
    [InlineData("not json")]
    [InlineData("{\"pattern\":\"x\"}")]
    public void BranchRuleConfiguration_ParseList_ReturnsEmptyForBlankOrMalformedInput(string? json)
    {
        Assert.Empty(BranchRuleConfiguration.ParseList(json));
    }

    [Fact]
    public void GetMatchingRule_PrefersTheMostSpecificMatchingPattern()
    {
        TabkeeperConfigurationStore store = new TabkeeperConfigurationStore(Path.Combine(temporaryDirectory, "config.json"));
        store.Save(new TabkeeperConfiguration
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
        TabkeeperConfigurationStore store = new TabkeeperConfigurationStore(Path.Combine(temporaryDirectory, "config.json"));
        store.Save(new TabkeeperConfiguration
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

    private static BranchRuleConfiguration? GetMatchingRule(TabkeeperConfiguration configuration, string branchName)
    {
        return configuration.Rules
            .Where(rule => new BranchRule(rule.Pattern, rule.PinSetName, rule.Enabled).IsMatch(branchName))
            .OrderByDescending(rule => rule.Pattern.Length)
            .FirstOrDefault();
    }
}
