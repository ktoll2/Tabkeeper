using System;
using System.IO;

namespace Tabkeeper.Core;

/// <summary>
/// Locates Git metadata without requiring a Git executable on PATH.
/// </summary>
public static class GitRepositoryLocator
{
    /// <summary>
    /// Finds the nearest Git repository above a solution or directory path and reads its current branch.
    /// </summary>
    /// <param name="startPath">A solution file, directory, or descendant path to search upward from.</param>
    /// <returns>A repository identity when readable Git metadata is found; otherwise <see langword="null"/>.</returns>
    public static GitRepository? TryLocate(string startPath)
    {
        if (string.IsNullOrWhiteSpace(startPath))
        {
            return null;
        }

        DirectoryInfo? directory = new DirectoryInfo(Path.GetFullPath(startPath));
        if (!directory.Exists)
        {
            directory = directory.Parent;
        }

        while (directory is not null)
        {
            string markerPath = Path.Combine(directory.FullName, ".git");
            string? gitDirectoryPath = TryResolveGitDirectory(markerPath, directory.FullName);
            if (gitDirectoryPath is not null)
            {
                string? branchId = TryReadBranchId(gitDirectoryPath);
                if (branchId is not null)
                {
                    return new GitRepository(directory.FullName, gitDirectoryPath, branchId);
                }
            }

            directory = directory.Parent;
        }

        return null;
    }

    /// <summary>
    /// Resolves a repository's <c>.git</c> directory marker or linked-worktree <c>gitdir:</c> pointer.
    /// </summary>
    /// <param name="markerPath">The candidate <c>.git</c> file or directory path.</param>
    /// <param name="repositoryRoot">The working-tree directory containing the marker.</param>
    /// <returns>The absolute Git metadata directory, or <see langword="null"/> when the marker is invalid.</returns>
    private static string? TryResolveGitDirectory(string markerPath, string repositoryRoot)
    {
        if (Directory.Exists(markerPath))
        {
            return markerPath;
        }

        if (!File.Exists(markerPath))
        {
            return null;
        }

        string markerContents;
        try
        {
            markerContents = File.ReadAllText(markerPath).Trim();
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }

        // A worktree uses a .git file that points at its separate Git metadata directory.
        const string gitDirPrefix = "gitdir: ";
        if (!markerContents.StartsWith(gitDirPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        string configuredPath = markerContents.Substring(gitDirPrefix.Length).Trim();
        if (configuredPath.Length == 0)
        {
            return null;
        }

        string gitDirectoryPath = Path.IsPathRooted(configuredPath)
            ? configuredPath
            : Path.Combine(repositoryRoot, configuredPath);
        return Directory.Exists(gitDirectoryPath) ? Path.GetFullPath(gitDirectoryPath) : null;
    }

    /// <summary>
    /// Reads the current local branch name or detached commit identifier from Git's HEAD file.
    /// </summary>
    /// <param name="gitDirectoryPath">The resolved Git metadata directory.</param>
    /// <returns>A branch or detached identifier, or <see langword="null"/> when HEAD cannot be read.</returns>
    private static string? TryReadBranchId(string gitDirectoryPath)
    {
        // Reading HEAD directly avoids requiring a Git executable or relying on PATH configuration.
        string headPath = Path.Combine(gitDirectoryPath, "HEAD");
        string headContents;
        try
        {
            headContents = File.ReadAllText(headPath).Trim();
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }

        const string localBranchPrefix = "ref: refs/heads/";
        if (headContents.StartsWith(localBranchPrefix, StringComparison.Ordinal))
        {
            string branchName = headContents.Substring(localBranchPrefix.Length);
            return branchName.Length == 0 ? null : branchName;
        }

        return IsCommitId(headContents) ? "detached/" + headContents : null;
    }

    /// <summary>
    /// Validates the hexadecimal commit identifier format accepted for detached HEAD state.
    /// </summary>
    /// <param name="value">The trimmed contents of the HEAD file.</param>
    /// <returns><see langword="true"/> for a seven-to-sixty-four character hexadecimal identifier.</returns>
    private static bool IsCommitId(string value)
    {
        if (value.Length < 7 || value.Length > 64)
        {
            return false;
        }

        foreach (char character in value)
        {
            bool isHexDigit = (character >= '0' && character <= '9') ||
                (character >= 'a' && character <= 'f') ||
                (character >= 'A' && character <= 'F');
            if (!isHexDigit)
            {
                return false;
            }
        }

        return true;
    }
}
