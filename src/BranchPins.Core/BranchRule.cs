using System;
using System.Text.RegularExpressions;

namespace BranchPins.Core;

/// <summary>
/// Maps matching branch names to a shared pin set or disables synchronization for them.
/// </summary>
public sealed class BranchRule
{
    /// <summary>
    /// Initializes a branch rule from configuration values.
    /// </summary>
    /// <param name="pattern">The case-insensitive branch glob pattern.</param>
    /// <param name="pinSetName">The shared pin-set name for matching branches.</param>
    /// <param name="enabled">Whether matching branches are synchronized.</param>
    public BranchRule(string pattern, string pinSetName, bool enabled)
    {
        Pattern = pattern;
        PinSetName = pinSetName;
        Enabled = enabled;
    }

    public string Pattern { get; }

    public string PinSetName { get; }

    public bool Enabled { get; }

    /// <summary>
    /// Tests a branch name against this rule's case-insensitive <c>*</c> and <c>?</c> glob pattern.
    /// </summary>
    /// <param name="branchName">The branch name to test.</param>
    /// <returns><see langword="true"/> when the branch name matches; otherwise <see langword="false"/>.</returns>
    public bool IsMatch(string branchName)
    {
        return Regex.IsMatch(branchName, "^" + Regex.Escape(Pattern).Replace("\\*", ".*").Replace("\\?", ".") + "$", RegexOptions.IgnoreCase);
    }
}
