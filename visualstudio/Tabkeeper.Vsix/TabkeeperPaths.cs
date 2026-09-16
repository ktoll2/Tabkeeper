using System;
using System.IO;

namespace Tabkeeper.Vsix;

/// <summary>
/// The fixed on-disk locations for Tabkeeper's user-local data (used only by the file-backed
/// configuration fallback and by the saved tab-state and activity-log files).
/// </summary>
internal static class TabkeeperPaths
{
    /// <summary><c>%LOCALAPPDATA%\Tabkeeper</c>.</summary>
    public static string Directory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Tabkeeper");

    /// <summary>The configuration and branch-rules file.</summary>
    public static string ConfigFile { get; } = Path.Combine(Directory, "config.json");

    /// <summary>The saved pin-state file.</summary>
    public static string StateFile { get; } = Path.Combine(Directory, "pinned-tabs.json");

    /// <summary>The activity log file.</summary>
    public static string ActivityLogFile { get; } = Path.Combine(Directory, "activity.log");
}
