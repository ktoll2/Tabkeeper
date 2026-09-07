namespace Tabkeeper.Core;

/// <summary>
/// Supplies the current Tabkeeper configuration to the synchronizer, independent of where it is
/// stored. The file-backed <see cref="TabkeeperConfigurationStore"/> and the Visual Studio
/// settings-backed provider both implement this.
/// </summary>
public interface ITabkeeperConfigurationProvider
{
    /// <summary>The configuration in effect right now. Callers may read this on every operation.</summary>
    TabkeeperConfiguration Current { get; }

    /// <summary>
    /// A message describing the most recent failure to read configuration, or <see langword="null"/>
    /// when the last read succeeded.
    /// </summary>
    string? LastError { get; }

    /// <summary>
    /// Persists a new value for the "enabled" flag only, used by the <b>Enable Tabkeeper</b> command.
    /// </summary>
    /// <param name="enabled">The desired enabled state.</param>
    void SetEnabled(bool enabled);
}
