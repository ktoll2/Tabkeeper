using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

namespace BranchPins.Core;

/// <summary>
/// All user-configurable Branch Pins behavior, stored in local <c>config.json</c>.
/// </summary>
[DataContract]
public sealed class BranchPinsConfiguration
{
    [DataMember(Name = "enabled")]
        public bool Enabled { get; set; } = true;

    [DataMember(Name = "pollingIntervalSeconds")]
        public int PollingIntervalSeconds { get; set; } = 2;

    [DataMember(Name = "maximumRestoredTabs")]
        public int MaximumRestoredTabs { get; set; } = 25;

    [DataMember(Name = "restoreDelayMilliseconds")]
        public int RestoreDelayMilliseconds { get; set; } = 500;

    [DataMember(Name = "preserveActiveDocument")]
        public bool PreserveActiveDocument { get; set; } = true;

    [DataMember(Name = "missingFileBehavior")]
        public string MissingFileBehavior { get; set; } = "skip";

    [DataMember(Name = "pinScope")]
        public string PinScope { get; set; } = "repository";

    [DataMember(Name = "detachedHeadBehavior")]
        public string DetachedHeadBehavior { get; set; } = "keepCommitPins";

    [DataMember(Name = "rules")]
        public List<BranchRuleConfiguration> Rules { get; set; } = new List<BranchRuleConfiguration>();

    /// <summary>
    /// Gets the polling interval constrained to the supported one-to-sixty second range.
    /// </summary>
    public int GetPollingIntervalSeconds() => Clamp(PollingIntervalSeconds, 1, 60);

    /// <summary>
    /// Gets the restore limit constrained to the supported one-to-one-hundred tab range.
    /// </summary>
    public int GetMaximumRestoredTabs() => Clamp(MaximumRestoredTabs, 1, 100);

    /// <summary>
    /// Gets the restore delay constrained to the supported zero-to-five-second range.
    /// </summary>
    public int GetRestoreDelayMilliseconds() => Clamp(RestoreDelayMilliseconds, 0, 5000);

    /// <summary>
    /// Gets a value indicating whether missing saved files should display a Visual Studio message.
    /// </summary>
    public bool ShowsMissingFileMessage => string.Equals(MissingFileBehavior, "showMessage", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Gets a value indicating whether pin management is restricted to the solution directory.
    /// </summary>
    public bool UsesSolutionDirectoryScope => string.Equals(PinScope, "solutionDirectory", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Gets a value indicating whether detached HEAD clears pins instead of restoring commit-specific pins.
    /// </summary>
    public bool ClearsDetachedHeadPins => string.Equals(DetachedHeadBehavior, "clearPins", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Constrains a numeric configuration value to an inclusive supported range.
    /// </summary>
    /// <param name="value">The requested value.</param>
    /// <param name="minimum">The inclusive minimum.</param>
    /// <param name="maximum">The inclusive maximum.</param>
    /// <returns>The requested value constrained to the specified range.</returns>
    private static int Clamp(int value, int minimum, int maximum) => value < minimum ? minimum : value > maximum ? maximum : value;
}

[DataContract]
/// <summary>
/// Configures a branch glob, its shared pin-set name, and whether synchronization is enabled.
/// </summary>
public sealed class BranchRuleConfiguration
{
    [DataMember(Name = "pattern")]
        public string Pattern { get; set; } = string.Empty;

    [DataMember(Name = "pinSetName")]
        public string PinSetName { get; set; } = string.Empty;

    [DataMember(Name = "enabled")]
        public bool Enabled { get; set; } = true;
}

/// <summary>
/// Loads and saves the complete local <c>config.json</c> document.
/// </summary>
public sealed class BranchPinsConfigurationStore
{
    private readonly string filePath;

    /// <summary>
    /// Initializes a store for one local configuration file.
    /// </summary>
    /// <param name="filePath">The absolute configuration file path.</param>
    public BranchPinsConfigurationStore(string filePath)
    {
        this.filePath = filePath;
    }

    public string FilePath => filePath;

    /// <summary>
    /// Loads configuration or creates a default configuration file on first use.
    /// </summary>
    /// <returns>The persisted configuration, or defaults when the file is absent or unreadable.</returns>
    public BranchPinsConfiguration Load()
    {
        if (!File.Exists(filePath))
        {
            BranchPinsConfiguration configuration = new BranchPinsConfiguration();
            Save(configuration);
            return configuration;
        }

        try
        {
            using FileStream stream = File.OpenRead(filePath);
            DataContractJsonSerializer serializer = new DataContractJsonSerializer(typeof(BranchPinsConfiguration));
            return serializer.ReadObject(stream) as BranchPinsConfiguration ?? new BranchPinsConfiguration();
        }
        catch (IOException)
        {
            return new BranchPinsConfiguration();
        }
        catch (SerializationException)
        {
            return new BranchPinsConfiguration();
        }
    }

    /// <summary>
    /// Persists the supplied configuration as the complete local configuration document.
    /// </summary>
    /// <param name="configuration">The complete configuration to serialize.</param>
    public void Save(BranchPinsConfiguration configuration)
    {
        string directory = Path.GetDirectoryName(filePath) ?? throw new InvalidOperationException("The configuration file must have a parent directory.");
        Directory.CreateDirectory(directory);
        using FileStream stream = File.Create(filePath);
        DataContractJsonSerializer serializer = new DataContractJsonSerializer(typeof(BranchPinsConfiguration));
        serializer.WriteObject(stream, configuration);
    }
}
