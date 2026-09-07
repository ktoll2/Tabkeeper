using System;
using System.Linq;
using System.Reflection;
using Tabkeeper.Core;

namespace Tabkeeper.Vsix;

/// <summary>
/// Reads Tabkeeper configuration from the Visual Studio settings store, which is the backing store
/// for the unified <b>Tools &gt; Options / Settings</b> page declared in
/// <c>TabkeeperSettings.registration.json</c>. Visual Studio holds the values in memory, so
/// <see cref="Current"/> reads them on every call and the synchronizer's normal polling picks up
/// changes without an explicit subscription.
/// </summary>
/// <remarks>
/// <c>ISettingsManager</c> is reached through reflection rather than a compile-time reference: the
/// only NuGet package that carries it (<c>Microsoft.VisualStudio.Settings.15.0</c>) ships an x86
/// reference assembly that trips MSB3270 in an AnyCPU project, and Visual Studio supplies the real
/// assembly at runtime regardless. The surface used here is tiny and stable
/// (<c>TryGetValue&lt;T&gt;(string, out T)</c> and <c>SetValueAsync(string, object, bool)</c>).
/// </remarks>
internal sealed class UnifiedSettingsConfigurationProvider : ITabkeeperConfigurationProvider
{
    /// <summary>The full type name of the MEF export that backs the unified settings page.</summary>
    public const string SettingsManagerContract = "Microsoft.VisualStudio.Settings.ISettingsManager";

    private const string Prefix = "tabkeeper.";

    private readonly object settingsManager;
    private readonly MethodInfo tryGetValue;
    private readonly MethodInfo? setValueAsync;

    /// <summary>
    /// Initializes the provider over the Visual Studio settings manager instance obtained from MEF.
    /// </summary>
    /// <param name="settingsManager">The <c>ISettingsManager</c> instance (as <see cref="object"/>).</param>
    /// <exception cref="MissingMethodException">The instance does not expose the expected members.</exception>
    public UnifiedSettingsConfigurationProvider(object settingsManager)
    {
        this.settingsManager = settingsManager;
        Type type = settingsManager.GetType();

        tryGetValue = type.GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .FirstOrDefault(m => m.Name == "TryGetValue"
                && m.IsGenericMethodDefinition
                && m.GetParameters() is { Length: 2 } p
                && p[0].ParameterType == typeof(string)
                && p[1].ParameterType.IsByRef)
            ?? throw new MissingMethodException(type.FullName, "TryGetValue");

        setValueAsync = type.GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .FirstOrDefault(m => m.Name == "SetValueAsync"
                && m.GetParameters() is { Length: 3 } p
                && p[0].ParameterType == typeof(string));
    }

    /// <inheritdoc />
    public string? LastError { get; private set; }

    /// <inheritdoc />
    public TabkeeperConfiguration Current
    {
        get
        {
            LastError = null;
            TabkeeperConfiguration configuration = new TabkeeperConfiguration
            {
                Enabled = GetValue("enabled", true),
                PreserveActiveDocument = GetValue("preserveActiveDocument", true),
                MaximumRestoredTabs = GetValue("maximumRestoredTabs", 25),
                RestoreDelayMilliseconds = GetValue("restoreDelayMilliseconds", 500),
                PollingIntervalSeconds = GetValue("pollingIntervalSeconds", 2),
                MissingFileBehavior = GetValue("missingFileBehavior", "skip"),
                PinScope = GetValue("scope", "repository"),
                DetachedHeadBehavior = GetValue("detachedHeadBehavior", "keepCommitPins"),
            };

            configuration.Rules = BranchRuleConfiguration.ParseList(GetValue("rules", "[]"));
            return configuration;
        }
    }

    /// <inheritdoc />
    public void SetEnabled(bool enabled)
    {
        try
        {
            // ISettingsManager.SetValueAsync(string name, object value, bool isMachineLocal)
            setValueAsync?.Invoke(settingsManager, new object[] { Prefix + "enabled", enabled, false });
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LastError = $"Could not write setting '{Prefix}enabled': {exception.Message}";
        }
    }

    private T GetValue<T>(string name, T fallback)
    {
        try
        {
            object?[] args = { Prefix + name, null };
            object? result = tryGetValue.MakeGenericMethod(typeof(T)).Invoke(settingsManager, args);

            // GetValueResult.Success is the only non-failure value.
            if (string.Equals(result?.ToString(), "Success", StringComparison.Ordinal) && args[1] is T value)
            {
                return value;
            }

            return fallback;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // An unregistered or wrongly typed moniker must not break a synchronization cycle.
            LastError = $"Could not read setting '{Prefix + name}': {(exception.InnerException ?? exception).Message}";
            return fallback;
        }
    }
}
