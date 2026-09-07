using System;

namespace Tabkeeper.Vsix;

/// <summary>
/// Defines command identifiers shared by the package implementation and VSCT menu resource.
/// </summary>
internal static class TabkeeperCommands
{
    /// <summary>
    /// Gets the command-group GUID, which must match the group declared in <c>Menus.vsct</c>.
    /// </summary>
    internal static readonly Guid CommandSet = new Guid("a7a42bc7-ea42-4f56-945d-dd2fa64bc3cb");

    internal const int Enable = 0x0100;
    internal const int SynchronizeNow = 0x0101;
    internal const int ClearCurrentTabkeeper = 0x0102;
    internal const int OpenSavedPinState = 0x0103;
    internal const int ManageBranchRules = 0x0104;
    internal const int ExportCurrentPinSet = 0x0105;
    internal const int ImportIntoCurrentPinSet = 0x0106;
    internal const int CopyCurrentPinsToBranch = 0x0107;
    internal const int CleanUpMissingBranches = 0x0108;
    internal const int OpenActivityLog = 0x0109;
}
