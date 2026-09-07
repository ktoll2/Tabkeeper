using System;
using System.Collections.Generic;
using System.IO;

namespace Tabkeeper.Core;

/// <summary>
/// Reads local branch names directly from Git metadata for cleanup operations.
/// </summary>
public static class GitBranchReader
{
    /// <summary>
    /// Returns local branch names from loose refs and packed refs, including linked-worktree metadata.
    /// </summary>
    /// <param name="gitDirectoryPath">The Git metadata directory associated with the active worktree.</param>
    /// <returns>A set of local branch names suitable for stale-state cleanup.</returns>
    public static ISet<string> GetLocalBranchNames(string gitDirectoryPath)
    {
        HashSet<string> branchNames = new HashSet<string>(StringComparer.Ordinal);
        string commonDirectory = GetCommonGitDirectory(gitDirectoryPath);
        string refsDirectory = Path.Combine(commonDirectory, "refs", "heads");
        if (Directory.Exists(refsDirectory))
        {
            AddLooseRefs(refsDirectory, refsDirectory, branchNames);
        }

        string packedRefsPath = Path.Combine(commonDirectory, "packed-refs");
        if (File.Exists(packedRefsPath))
        {
            foreach (string line in File.ReadAllLines(packedRefsPath))
            {
                int separatorIndex = line.IndexOf(' ');
                const string prefix = "refs/heads/";
                if (separatorIndex > 0 && line.Substring(separatorIndex + 1).StartsWith(prefix, StringComparison.Ordinal))
                {
                    branchNames.Add(line.Substring(separatorIndex + 1 + prefix.Length));
                }
            }
        }

        return branchNames;
    }

    /// <summary>
    /// Resolves the shared Git directory used by linked worktrees, when configured.
    /// </summary>
    /// <param name="gitDirectoryPath">The worktree-specific Git metadata directory.</param>
    /// <returns>The Git directory that owns shared refs and packed refs.</returns>
    private static string GetCommonGitDirectory(string gitDirectoryPath)
    {
        string commonDirectoryFile = Path.Combine(gitDirectoryPath, "commondir");
        if (!File.Exists(commonDirectoryFile))
        {
            return gitDirectoryPath;
        }

        string configuredPath = File.ReadAllText(commonDirectoryFile).Trim();
        return Path.GetFullPath(Path.Combine(gitDirectoryPath, configuredPath));
    }

    /// <summary>
    /// Recursively adds local branch names represented by loose reference files.
    /// </summary>
    /// <param name="rootDirectory">The root <c>refs/heads</c> directory used to derive names.</param>
    /// <param name="directory">The current directory being traversed.</param>
    /// <param name="branchNames">The destination set of branch names.</param>
    private static void AddLooseRefs(string rootDirectory, string directory, ISet<string> branchNames)
    {
        foreach (string filePath in Directory.GetFiles(directory))
        {
            branchNames.Add(filePath.Substring(rootDirectory.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Replace(Path.DirectorySeparatorChar, '/'));
        }

        foreach (string childDirectory in Directory.GetDirectories(directory))
        {
            AddLooseRefs(rootDirectory, childDirectory, branchNames);
        }
    }
}
