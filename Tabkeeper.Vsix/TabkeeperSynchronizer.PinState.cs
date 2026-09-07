using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Tabkeeper.Core;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Threading;

namespace Tabkeeper.Vsix;

/// <summary>
/// Handles saving, restoring, transferring, and clearing branch-specific pin sets.
/// </summary>
internal sealed partial class TabkeeperSynchronizer
{
    /// <summary>
    /// Asynchronously clears the active branch's saved state and unpins documents in scope.
    /// </summary>
    public void ClearCurrentTabkeeper()
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        QueueClearCurrentTabkeeper();
    }

    /// <summary>
    /// Writes the active branch's saved pins to a portable JSON file.
    /// </summary>
    /// <param name="filePath">The destination export file path.</param>
    public void ExportCurrentPinSet(string filePath)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        GitRepository? repository = TryGetSolutionRepository();
        if (repository is null)
        {
            return;
        }

        SaveCurrentPins(repository);
        IReadOnlyList<string> pinnedPaths = stateStore.GetPinnedPaths(repository.RootPath, GetStateKey(repository));
        PinSetTransfer.Export(filePath, repository.BranchId, pinnedPaths);
        Log(ActivityLogLevel.Info, repository, repository.BranchId, $"Exported {pinnedPaths.Count} pinned tabs to {filePath}.");
    }

    /// <summary>
    /// Replaces the active branch's saved pins with a validated portable JSON file.
    /// </summary>
    /// <param name="filePath">The source export file path.</param>
    public void ImportIntoCurrentPinSet(string filePath)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        GitRepository? repository = TryGetSolutionRepository();
        if (repository is null || !IsManaged(repository))
        {
            return;
        }

        PinSetTransfer transfer = PinSetTransfer.Import(filePath);
        stateStore.SavePinnedPaths(repository.RootPath, GetStateKey(repository), transfer.PinnedPaths);
        Log(ActivityLogLevel.Info, repository, repository.BranchId, $"Imported {transfer.PinnedPaths.Count} pinned tabs from {filePath}.");
        SynchronizeNow();
    }

    /// <summary>
    /// Copies the active branch's pin set to another branch or matching shared-set key.
    /// </summary>
    /// <param name="destinationBranch">The destination local branch name.</param>
    public void CopyCurrentPinsToBranch(string destinationBranch)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        GitRepository? repository = TryGetSolutionRepository();
        if (repository is null || string.IsNullOrWhiteSpace(destinationBranch))
        {
            return;
        }

        SaveCurrentPins(repository);
        IReadOnlyList<string> currentPins = stateStore.GetPinnedPaths(repository.RootPath, GetStateKey(repository));
        stateStore.SavePinnedPaths(repository.RootPath, GetStateKey(destinationBranch), currentPins);
        Log(ActivityLogLevel.Info, repository, repository.BranchId, $"Copied {currentPins.Count} pinned tabs to {destinationBranch}.");
    }

    /// <summary>
    /// Deletes saved states for local branches that no longer exist.
    /// </summary>
    /// <returns>The number of obsolete branch-specific states removed.</returns>
    public int CleanUpMissingBranches()
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        GitRepository? repository = TryGetSolutionRepository();
        if (repository is null)
        {
            return 0;
        }

        int removedCount = stateStore.RemoveMissingBranchStates(repository.RootPath, GitBranchReader.GetLocalBranchNames(repository.GitDirectoryPath));
        Log(ActivityLogLevel.Info, repository, repository.BranchId, $"Removed {removedCount} obsolete branch pin sets.");
        return removedCount;
    }

    /// <summary>
    /// Captures open pinned document paths and persists them under the repository's effective branch or shared-set key.
    /// </summary>
    /// <param name="repository">The observed repository and outgoing branch identity.</param>
    /// <param name="savedMessage">The activity-log message written after a successful save.</param>
    private void SaveCurrentPins(GitRepository repository, string savedMessage = "Saved pinned tabs.")
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        if (!IsManaged(repository) || (IsDetachedHead(repository) && GetConfiguration().ClearsDetachedHeadPins))
        {
            // Disabled rules and clear-on-detached mode intentionally do not create or update a saved pin set.
            return;
        }

        IEnumerable<string> pinnedPaths = documentPins.GetOpenDocuments(GetScopeRoot(repository))
            .Where(frame => VisualStudioDocumentPins.IsPinned(frame.Frame))
            .Select(frame => VisualStudioDocumentPins.MakeRelativePath(repository.RootPath, frame.Path));
        stateStore.SavePinnedPaths(repository.RootPath, GetStateKey(repository), pinnedPaths);
        Log(ActivityLogLevel.Info, repository, repository.BranchId, savedMessage);
    }

    /// <summary>
    /// On the first observation of a branch that has no saved set, records the tabs the user already
    /// has pinned as that branch's initial set instead of unpinning them.
    /// </summary>
    /// <param name="repository">The observed repository and branch identity.</param>
    private void AdoptExistingPinsIfUnseen(GitRepository repository)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        if (stateStore.HasState(repository.RootPath, GetStateKey(repository)))
        {
            return;
        }

        SaveCurrentPins(repository, "Adopted the currently pinned tabs as this branch's initial set.");
    }

    /// <summary>
    /// Unpins current scoped documents, then opens and pins the valid paths saved for the destination branch.
    /// </summary>
    /// <param name="repository">The observed repository and destination branch identity.</param>
    /// <param name="isRetry">
    /// <see langword="true"/> for a follow-up attempt after a checkout that had not finished writing
    /// files; missing-file reporting is suppressed because the first attempt already logged them.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when every saved tab within the restore limit was pinned; otherwise
    /// <see langword="false"/>, indicating a retry may recover files a slow checkout wrote late.
    /// </returns>
    private bool ApplyTabkeeper(GitRepository repository, bool isRetry = false)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        if (!IsManaged(repository))
        {
            if (!isRetry)
            {
                Log(ActivityLogLevel.Info, repository, repository.BranchId, "Skipped synchronization because its matching rule is disabled.");
            }

            return true;
        }

        TabkeeperConfiguration configuration = GetConfiguration();
        string? activeDocumentPath = configuration.PreserveActiveDocument ? documentPins.GetActiveDocumentPath() : null;
        string scopeRoot = GetScopeRoot(repository);

        // Enumerate the open document frames once: unpin every in-scope frame now, and keep a
        // path-keyed lookup so the restore loop below does not re-enumerate per saved tab.
        Dictionary<string, Microsoft.VisualStudio.Shell.Interop.IVsWindowFrame> openInScope =
            new Dictionary<string, Microsoft.VisualStudio.Shell.Interop.IVsWindowFrame>(StringComparer.OrdinalIgnoreCase);
        foreach (OpenDocumentFrame document in documentPins.GetOpenDocuments(scopeRoot))
        {
            VisualStudioDocumentPins.SetPinned(document.Frame, false);
            openInScope[document.Path] = document.Frame;
        }

        if (IsDetachedHead(repository) && configuration.ClearsDetachedHeadPins)
        {
            return true;
        }

        // Filter absent files before enforcing the cap so missing files do not consume a restore slot.
        List<(string Relative, string Full)> restorablePaths = new List<(string, string)>();
        bool anyMissing = false;
        foreach (string relativePath in stateStore.GetPinnedPaths(repository.RootPath, GetStateKey(repository)))
        {
            string fullPath = Path.GetFullPath(Path.Combine(repository.RootPath, relativePath));
            if (!VisualStudioDocumentPins.IsPathInRoot(fullPath, scopeRoot) || !File.Exists(fullPath))
            {
                anyMissing = true;
                if (!isRetry)
                {
                    ReportMissingFile(relativePath);
                }

                continue;
            }

            restorablePaths.Add((relativePath, fullPath));
        }

        int maximumRestoredTabs = configuration.GetMaximumRestoredTabs();
        if (restorablePaths.Count > maximumRestoredTabs && !isRetry)
        {
            Log(ActivityLogLevel.Warn, repository, repository.BranchId, $"Restore limit ({maximumRestoredTabs}): skipped {restorablePaths.Count - maximumRestoredTabs} pinned tabs.");
        }

        int restoredCount = 0;
        bool anyOpenFailed = false;
        foreach ((string relativePath, string fullPath) in restorablePaths.Take(maximumRestoredTabs))
        {
            if (!openInScope.TryGetValue(fullPath, out Microsoft.VisualStudio.Shell.Interop.IVsWindowFrame? frame))
            {
                frame = documentPins.OpenDocument(fullPath);
            }

            if (frame is not null)
            {
                VisualStudioDocumentPins.SetPinned(frame, true);
                restoredCount++;
            }
            else
            {
                anyOpenFailed = true;
                if (!isRetry)
                {
                    Log(ActivityLogLevel.Warn, repository, repository.BranchId, $"Could not open pinned tab: {relativePath}");
                }
            }
        }

        documentPins.RestoreActiveDocument(activeDocumentPath);
        Log(ActivityLogLevel.Info, repository, repository.BranchId, $"Restored {restoredCount} pinned tabs.");

        // Files reported permanently missing do not warrant a retry; a failed open of an existing
        // file might, since a slow checkout can leave a file briefly locked or unreadable.
        return !anyOpenFailed && (isRetry || !anyMissing);
    }

    /// <summary>
    /// Queues clearing work behind pending branch synchronization so document-frame operations remain serialized.
    /// </summary>
    private void QueueClearCurrentTabkeeper()
    {
        if (disposed)
        {
            return;
        }

        JoinableTask clearTask = joinableTaskFactory.RunAsync(async delegate
        {
            await synchronizationGate.WaitAsync();
            try
            {
                await joinableTaskFactory.SwitchToMainThreadAsync();
                GitRepository? repository = TryGetSolutionRepository();
                if (repository is not null)
                {
                    stateStore.ClearPinnedPaths(repository.RootPath, GetStateKey(repository));
                    documentPins.UnpinAll(GetScopeRoot(repository));
                }
            }
            finally
            {
                synchronizationGate.Release();
            }
        });
    }

    /// <summary>
    /// Resolves the configured repository-wide or solution-directory document scope.
    /// </summary>
    /// <param name="repository">The active repository used when repository scope is selected.</param>
    /// <returns>The absolute directory root for document filtering.</returns>
    private string GetScopeRoot(GitRepository repository)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        if (!GetConfiguration().UsesSolutionDirectoryScope)
        {
            return repository.RootPath;
        }

        ErrorHandler.ThrowOnFailure(solution.GetSolutionInfo(out string solutionDirectory, out string solutionFile, out string userOptionsFile));
        return !string.IsNullOrEmpty(solutionDirectory) && VisualStudioDocumentPins.IsPathInRoot(solutionDirectory, repository.RootPath)
            ? solutionDirectory
            : repository.RootPath;
    }

    /// <summary>
    /// Determines whether the branch's most-specific matching rule permits synchronization.
    /// </summary>
    /// <param name="repository">The branch identity to evaluate.</param>
    /// <returns><see langword="true"/> when no disabled rule matches; otherwise <see langword="false"/>.</returns>
    private bool IsManaged(GitRepository repository)
    {
        BranchRuleConfiguration? rule = GetMatchingRule(repository.BranchId);
        return rule is null || rule.Enabled;
    }

    /// <summary>
    /// Gets the latest configuration so user edits take effect without restarting Visual Studio.
    /// </summary>
    /// <returns>The current configuration, or defaults when it cannot be read.</returns>
    private TabkeeperConfiguration GetConfiguration()
    {
        return configuration.Current;
    }

    /// <summary>
    /// Finds the longest matching configured branch glob.
    /// </summary>
    /// <param name="branchName">The branch name to evaluate.</param>
    /// <returns>The most-specific matching rule, or <see langword="null"/> when no rule applies.</returns>
    private BranchRuleConfiguration? GetMatchingRule(string branchName)
    {
        return GetConfiguration().Rules
            .Where(rule => !string.IsNullOrWhiteSpace(rule.Pattern) && new BranchRule(rule.Pattern, rule.PinSetName, rule.Enabled).IsMatch(branchName))
            .OrderByDescending(rule => rule.Pattern.Length)
            .FirstOrDefault();
    }

    /// <summary>
    /// Resolves the persisted pin-state key for an observed repository branch.
    /// </summary>
    /// <param name="repository">The repository whose branch key is required.</param>
    /// <returns>A branch key or a <c>set/</c>-prefixed shared-set key.</returns>
    private string GetStateKey(GitRepository repository)
    {
        return GetStateKey(repository.BranchId);
    }

    /// <summary>
    /// Resolves a branch name to its own state key or to an enabled rule's shared-set key.
    /// </summary>
    /// <param name="branchName">The branch name to resolve.</param>
    /// <returns>A branch key or a <c>set/</c>-prefixed shared-set key.</returns>
    private string GetStateKey(string branchName)
    {
        BranchRuleConfiguration? rule = GetMatchingRule(branchName);
        return rule is not null && rule.Enabled ? "set/" + rule.PinSetName : branchName;
    }

    /// <summary>
    /// Logs a missing saved path and optionally displays the configured warning dialog.
    /// </summary>
    /// <param name="relativePath">The repository-relative path that could not be restored.</param>
    private void ReportMissingFile(string relativePath)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        Log(ActivityLogLevel.Warn, currentRepository, currentRepository?.BranchId ?? "system", $"Missing: {relativePath}");
        if (GetConfiguration().ShowsMissingFileMessage)
        {
            documentPins.ShowMissingFileMessage(relativePath);
        }
    }

    /// <summary>
    /// Determines whether a repository identity represents a detached commit rather than a local branch.
    /// </summary>
    /// <param name="repository">The repository identity to inspect.</param>
    /// <returns><see langword="true"/> for identifiers prefixed with <c>detached/</c>.</returns>
    private static bool IsDetachedHead(GitRepository repository)
    {
        return repository.BranchId.StartsWith("detached/", StringComparison.Ordinal);
    }
}
