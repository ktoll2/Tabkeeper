namespace BranchPins.Core;

/// <summary>
/// Identifies the Git repository and checked-out branch for a solution.
/// </summary>
public sealed class GitRepository
{
    /// <summary>
    /// Initializes a repository identity read from local Git metadata.
    /// </summary>
    /// <param name="rootPath">The working-tree root path.</param>
    /// <param name="gitDirectoryPath">The Git metadata directory.</param>
    /// <param name="branchId">The local branch name or detached commit identifier.</param>
    public GitRepository(string rootPath, string gitDirectoryPath, string branchId)
    {
        RootPath = rootPath;
        GitDirectoryPath = gitDirectoryPath;
        BranchId = branchId;
    }

    public string RootPath { get; }

    public string GitDirectoryPath { get; }

    public string BranchId { get; }
}
