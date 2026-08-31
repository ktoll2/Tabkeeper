using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

namespace BranchPins.Core;

/// <summary>
/// Persists branch-specific, repository-relative pinned document paths outside the repository.
/// </summary>
public sealed class PinnedTabStateStore
{
    private readonly string stateFilePath;

    /// <summary>
    /// Initializes a store for the user-local saved pin-state file.
    /// </summary>
    /// <param name="stateFilePath">The absolute state-file path.</param>
    public PinnedTabStateStore(string stateFilePath)
    {
        this.stateFilePath = stateFilePath;
    }

    public string StateFilePath => stateFilePath;

    /// <summary>
    /// Returns a snapshot of saved relative paths for a repository and branch or shared-set key.
    /// </summary>
    /// <param name="repositoryRoot">The repository root used as the state partition key.</param>
    /// <param name="branchId">A branch, detached commit, or shared-set key.</param>
    /// <returns>A copy of saved repository-relative paths, or an empty list when no state exists.</returns>
    public IReadOnlyList<string> GetPinnedPaths(string repositoryRoot, string branchId)
    {
        StateDocument state = Load();
        string repositoryKey = NormalizeRepositoryPath(repositoryRoot);
        if (!state.Repositories.TryGetValue(repositoryKey, out RepositoryState? repository) ||
            !repository.Branches.TryGetValue(branchId, out BranchState? branch))
        {
            return Array.Empty<string>();
        }

        return branch.PinnedPaths.ToArray();
    }

    /// <summary>
    /// Saves one complete branch or shared-set pin list after validating relative paths.
    /// </summary>
    /// <param name="repositoryRoot">The repository root used as the state partition key.</param>
    /// <param name="branchId">A branch, detached commit, or shared-set key.</param>
    /// <param name="relativePaths">Candidate repository-relative paths to persist.</param>
    public void SavePinnedPaths(string repositoryRoot, string branchId, IEnumerable<string> relativePaths)
    {
        StateDocument state = Load();
        string repositoryKey = NormalizeRepositoryPath(repositoryRoot);
        if (!state.Repositories.TryGetValue(repositoryKey, out RepositoryState? repository))
        {
            repository = new RepositoryState();
            state.Repositories.Add(repositoryKey, repository);
        }

        repository.Branches[branchId] = new BranchState
        {
            // Persist only portable, repository-relative paths; never store user-specific absolute paths.
            PinnedPaths = relativePaths
                .Where(IsSafeRelativePath)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToList(),
        };

        Save(state);
    }

    /// <summary>
    /// Removes the saved pin list for one branch or shared-set key.
    /// </summary>
    /// <param name="repositoryRoot">The repository root used as the state partition key.</param>
    /// <param name="branchId">The branch, detached commit, or shared-set key to remove.</param>
    public void ClearPinnedPaths(string repositoryRoot, string branchId)
    {
        StateDocument state = Load();
        string repositoryKey = NormalizeRepositoryPath(repositoryRoot);
        if (state.Repositories.TryGetValue(repositoryKey, out RepositoryState? repository))
        {
            repository.Branches.Remove(branchId);
            Save(state);
        }
    }

    /// <summary>
    /// Creates an empty state file when no saved pin state has been recorded yet.
    /// </summary>
    public void EnsureStateFileExists()
    {
        if (!File.Exists(stateFilePath))
        {
            Save(new StateDocument());
        }
    }

    /// <summary>
    /// Removes ordinary branch states that no longer have a local Git branch and returns the count.
    /// </summary>
    /// <param name="repositoryRoot">The repository root whose branch states are evaluated.</param>
    /// <param name="existingBranches">Current local branch names read from Git metadata.</param>
    /// <returns>The number of obsolete branch-specific states removed.</returns>
    public int RemoveMissingBranchStates(string repositoryRoot, ISet<string> existingBranches)
    {
        StateDocument state = Load();
        string repositoryKey = NormalizeRepositoryPath(repositoryRoot);
        if (!state.Repositories.TryGetValue(repositoryKey, out RepositoryState? repository))
        {
            return 0;
        }

        List<string> removableKeys = repository.Branches.Keys
            // Shared rule sets and detached commit sets are not local branches, so manual cleanup must retain them.
            .Where(branchKey => !branchKey.StartsWith("set/", StringComparison.Ordinal) && !branchKey.StartsWith("detached/", StringComparison.Ordinal) && !existingBranches.Contains(branchKey))
            .ToList();
        foreach (string branchKey in removableKeys)
        {
            repository.Branches.Remove(branchKey);
        }

        if (removableKeys.Count > 0)
        {
            Save(state);
        }

        return removableKeys.Count;
    }

    /// <summary>
    /// Loads the complete state document, returning an empty document for a missing or unreadable file.
    /// </summary>
    /// <returns>The deserialized state document or a new empty state document.</returns>
    private StateDocument Load()
    {
        if (!File.Exists(stateFilePath))
        {
            return new StateDocument();
        }

        try
        {
            using FileStream stream = File.OpenRead(stateFilePath);
            DataContractJsonSerializer serializer = new DataContractJsonSerializer(typeof(StateDocument));
            return serializer.ReadObject(stream) as StateDocument ?? new StateDocument();
        }
        catch (IOException)
        {
            return new StateDocument();
        }
        catch (SerializationException)
        {
            return new StateDocument();
        }
    }

    /// <summary>
    /// Atomically replaces the local state document with a serialized snapshot.
    /// </summary>
    /// <param name="state">The complete state document to persist.</param>
    private void Save(StateDocument state)
    {
        string? directoryPath = Path.GetDirectoryName(stateFilePath);
        if (string.IsNullOrEmpty(directoryPath))
        {
            throw new InvalidOperationException("The state file must have a parent directory.");
        }

        Directory.CreateDirectory(directoryPath);
        string temporaryPath = stateFilePath + ".tmp";
        using (FileStream stream = File.Create(temporaryPath))
        {
            DataContractJsonSerializer serializer = new DataContractJsonSerializer(typeof(StateDocument));
            serializer.WriteObject(stream, state);
        }

        if (File.Exists(stateFilePath))
        {
            File.Delete(stateFilePath);
        }

        File.Move(temporaryPath, stateFilePath);
    }

    /// <summary>
    /// Produces the stable repository partition key used in the state document.
    /// </summary>
    /// <param name="repositoryRoot">The repository root path to normalize.</param>
    /// <returns>The fully qualified root path without trailing directory separators.</returns>
    private static string NormalizeRepositoryPath(string repositoryRoot)
    {
        return Path.GetFullPath(repositoryRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    /// <summary>
    /// Determines whether a path is nonblank, relative, and cannot traverse above its repository root.
    /// </summary>
    /// <param name="path">The candidate persisted path.</param>
    /// <returns><see langword="true"/> for a safe repository-relative path.</returns>
    private static bool IsSafeRelativePath(string path)
    {
        return !string.IsNullOrWhiteSpace(path) &&
            !Path.IsPathRooted(path) &&
            !path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Any(segment => segment == "..");
    }

    [DataContract]
    private sealed class StateDocument
    {
        [DataMember(Name = "repositories")]
        public Dictionary<string, RepositoryState> Repositories { get; set; } = new Dictionary<string, RepositoryState>(StringComparer.OrdinalIgnoreCase);
    }

    [DataContract]
    private sealed class RepositoryState
    {
        [DataMember(Name = "branches")]
        public Dictionary<string, BranchState> Branches { get; set; } = new Dictionary<string, BranchState>(StringComparer.Ordinal);
    }

    [DataContract]
    private sealed class BranchState
    {
        [DataMember(Name = "pinnedPaths")]
        public List<string> PinnedPaths { get; set; } = new List<string>();
    }
}
