using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

namespace Tabkeeper.Core;

/// <summary>
/// All user-configurable Tabkeeper behavior, stored in local <c>config.json</c>.
/// </summary>
[DataContract]
public sealed class TabkeeperConfiguration
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

    /// <summary>
    /// Parses a JSON array of rule objects, returning an empty list for null, blank, or malformed input.
    /// Used when branch rules are stored as a single string setting.
    /// </summary>
    /// <param name="json">A JSON array such as <c>[{"pattern":"feature/*","pinSetName":"feat","enabled":true}]</c>.</param>
    /// <returns>The parsed rules, or an empty list.</returns>
    public static List<BranchRuleConfiguration> ParseList(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new List<BranchRuleConfiguration>();
        }

        try
        {
            using MemoryStream stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json));
            DataContractJsonSerializer serializer = new DataContractJsonSerializer(typeof(List<BranchRuleConfiguration>));
            return serializer.ReadObject(stream) as List<BranchRuleConfiguration> ?? new List<BranchRuleConfiguration>();
        }
        catch (Exception exception) when (exception is SerializationException
            || exception is System.Xml.XmlException
            || exception is FormatException)
        {
            return new List<BranchRuleConfiguration>();
        }
    }

    /// <summary>
    /// Serializes rules back to a compact JSON array string.
    /// </summary>
    /// <param name="rules">The rules to serialize.</param>
    /// <returns>A JSON array string; <c>[]</c> when there are none.</returns>
    public static string SerializeList(IEnumerable<BranchRuleConfiguration> rules)
    {
        List<BranchRuleConfiguration> list = new List<BranchRuleConfiguration>(rules);
        using MemoryStream stream = new MemoryStream();
        DataContractJsonSerializer serializer = new DataContractJsonSerializer(typeof(List<BranchRuleConfiguration>));
        serializer.WriteObject(stream, list);
        return System.Text.Encoding.UTF8.GetString(stream.ToArray());
    }
}

/// <summary>
/// Loads and saves a Tabkeeper configuration document as a local JSON file, and adapts it to
/// <see cref="ITabkeeperConfigurationProvider"/>.
/// </summary>
public sealed class TabkeeperConfigurationStore : ITabkeeperConfigurationProvider
{
    private readonly string filePath;
    private readonly object gate = new object();
    private TabkeeperConfiguration? cachedConfiguration;
    private (DateTime WriteTimeUtc, long Length) cachedSignature;
    private string? lastLoadError;

    /// <summary>
    /// Initializes a store for one local configuration file.
    /// </summary>
    /// <param name="filePath">The absolute configuration file path.</param>
    public TabkeeperConfigurationStore(string filePath)
    {
        this.filePath = filePath;
    }

    public string FilePath => filePath;

    /// <summary>
    /// Gets a message describing the most recent failure to parse the configuration file, or
    /// <see langword="null"/> when the file was last read successfully. When set, <see cref="Load"/>
    /// returns the last valid configuration, or defaults if none was ever read.
    /// </summary>
    public string? LastLoadError
    {
        get { lock (gate) { return lastLoadError; } }
    }

    /// <inheritdoc />
    public TabkeeperConfiguration Current => Load();

    /// <inheritdoc />
    public string? LastError => LastLoadError;

    /// <inheritdoc />
    public void SetEnabled(bool enabled)
    {
        TabkeeperConfiguration configuration = Load();
        configuration.Enabled = enabled;
        Save(configuration);
    }

    /// <summary>
    /// Loads configuration or creates a default configuration file on first use.
    /// </summary>
    /// <remarks>
    /// The parsed document is cached and only re-read when the file's last-write time or size changes, so
    /// callers may invoke this on every operation without repeated disk reads. A file that cannot be
    /// parsed does not overwrite a previously cached valid configuration; the failure is recorded in
    /// <see cref="LastLoadError"/> and cleared on the next successful read.
    /// </remarks>
    /// <returns>The persisted configuration, the last valid configuration, or defaults.</returns>
    public TabkeeperConfiguration Load()
    {
        lock (gate)
        {
            if (!File.Exists(filePath))
            {
                TabkeeperConfiguration configuration = new TabkeeperConfiguration();
                Save(configuration);
                return configuration;
            }

            (DateTime WriteTimeUtc, long Length) signature = GetFileSignature();
            if (cachedConfiguration is not null && lastLoadError is null && signature == cachedSignature)
            {
                return cachedConfiguration;
            }

            try
            {
                using FileStream stream = File.OpenRead(filePath);
                DataContractJsonSerializer serializer = new DataContractJsonSerializer(typeof(TabkeeperConfiguration));
                TabkeeperConfiguration configuration = serializer.ReadObject(stream) as TabkeeperConfiguration
                    ?? new TabkeeperConfiguration();
                cachedConfiguration = configuration;
                cachedSignature = signature;
                lastLoadError = null;
                return configuration;
            }
            catch (Exception exception) when (exception is IOException
                || exception is UnauthorizedAccessException
                || exception is SerializationException
                || exception is System.Xml.XmlException
                || exception is FormatException)
            {
                lastLoadError = $"'{filePath}' could not be read ({exception.Message}). " +
                    (cachedConfiguration is not null
                        ? "The last valid settings remain in effect until the file is fixed."
                        : "Default settings are in effect until the file is fixed.");

                // Do not bind the failure to the current signature so the next Load retries the file.
                cachedSignature = default;
                return cachedConfiguration ?? new TabkeeperConfiguration();
            }
        }
    }

    /// <summary>
    /// Persists the supplied configuration as the complete local configuration document.
    /// </summary>
    /// <param name="configuration">The complete configuration to serialize.</param>
    public void Save(TabkeeperConfiguration configuration)
    {
        lock (gate)
        {
            string directory = Path.GetDirectoryName(filePath) ?? throw new InvalidOperationException("The configuration file must have a parent directory.");
            Directory.CreateDirectory(directory);
            using (FileStream stream = File.Create(filePath))
            {
                DataContractJsonSerializer serializer = new DataContractJsonSerializer(typeof(TabkeeperConfiguration));
                serializer.WriteObject(stream, configuration);
            }

            cachedConfiguration = configuration;
            cachedSignature = GetFileSignature();
            lastLoadError = null;
        }
    }

    private (DateTime WriteTimeUtc, long Length) GetFileSignature()
    {
        try
        {
            FileInfo info = new FileInfo(filePath);
            return (info.LastWriteTimeUtc, info.Length);
        }
        catch (IOException)
        {
            return default;
        }
        catch (UnauthorizedAccessException)
        {
            return default;
        }
    }
}
