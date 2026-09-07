using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

namespace Tabkeeper.Core;

/// <summary>
/// Persists branch-specific, repository-relative pinned document paths outside the repository.
/// </summary>
public sealed class PinnedTabStateStore
{
    private readonly string stateFilePath;
    private readonly string mutexName;

    /// <summary>
    /// Initializes a store for the user-local saved pin-state file.
    /// </summary>
    /// <param name="stateFilePath">The absolute state-file path.</param>
    public PinnedTabStateStore(string stateFilePath)
    {
        this.stateFilePath = stateFilePath;

        // Multiple Visual Studio instances share this one file. A named mutex serializes each
        // read-modify-write across processes so concurrent saves cannot drop one another's changes.
        mutexName = SharedFileAccess.MutexNameFor("PinnedTabState", stateFilePath);
    }

    public string StateFilePath => stateFilePath;

    /// <summary>
    /// Determines whether a saved pin set already exists for a repository and branch or shared-set key.
    /// </summary>
    /// <param name="repositoryRoot">The repository root used as the state partition key.</param>
    /// <param name="branchId">A branch, detached commit, or shared-set key.</param>
    /// <returns><see langword="true"/> when an entry exists, even if its pinned-path list is empty.</returns>
    public bool HasState(string repositoryRoot, string branchId)
    {
        StateDocument state = Load();
        string repositoryKey = NormalizeRepositoryPath(repositoryRoot);
        return state.Repositories.TryGetValue(repositoryKey, out RepositoryState? repository)
            && repository.Branches.ContainsKey(branchId);
    }

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
        List<string> safePaths = RelativePathPolicy.Normalize(relativePaths).ToList();

        Mutate(state =>
        {
            string repositoryKey = NormalizeRepositoryPath(repositoryRoot);
            if (!state.Repositories.TryGetValue(repositoryKey, out RepositoryState? repository))
            {
                repository = new RepositoryState();
                state.Repositories.Add(repositoryKey, repository);
            }

            // Persist only portable, repository-relative paths; never store user-specific absolute paths.
            repository.Branches[branchId] = new BranchState { PinnedPaths = safePaths };
            return true;
        });
    }

    /// <summary>
    /// Removes the saved pin list for one branch or shared-set key.
    /// </summary>
    /// <param name="repositoryRoot">The repository root used as the state partition key.</param>
    /// <param name="branchId">The branch, detached commit, or shared-set key to remove.</param>
    public void ClearPinnedPaths(string repositoryRoot, string branchId)
    {
        Mutate(state =>
        {
            string repositoryKey = NormalizeRepositoryPath(repositoryRoot);
            return state.Repositories.TryGetValue(repositoryKey, out RepositoryState? repository)
                && repository.Branches.Remove(branchId);
        });
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
        return Mutate(state =>
        {
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

            return removableKeys.Count;
        });
    }

    /// <summary>
    /// Runs a read-modify-write cycle on the state document under a cross-process lock, then
    /// persists the result. The state file is re-read inside the lock so concurrent Visual Studio
    /// instances cannot overwrite one another's saved sets.
    /// </summary>
    /// <typeparam name="T">The mutator's result type.</typeparam>
    /// <param name="mutator">Receives the current document and applies changes in place.</param>
    /// <returns>The mutator's result.</returns>
    private T Mutate<T>(Func<StateDocument, T> mutator)
    {
        using (SharedFileAccess.Lock(mutexName, TimeSpan.FromSeconds(10)))
        {
            StateDocument state = Load();
            T result = mutator(state);
            Save(state);
            return result;
        }
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
            return SharedFileAccess.Retry(() =>
            {
                using FileStream stream = new FileStream(stateFilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                return new DataContractJsonSerializer(typeof(StateDocument)).ReadObject(stream) as StateDocument
                    ?? new StateDocument();
            });
        }
        catch (IOException)
        {
            // The file stayed locked. Treat as empty rather than blocking Visual Studio; the caller's
            // change is re-derivable on the next synchronization.
            return new StateDocument();
        }
        catch (Exception exception) when (exception is SerializationException
            || exception is System.Xml.XmlException
            || exception is FormatException)
        {
            // A corrupt state file is treated as empty so a fresh set can be written over it.
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

        // A per-write temporary name keeps two instances from colliding on a shared ".tmp" file.
        string temporaryPath = stateFilePath + "." + Guid.NewGuid().ToString("N").Substring(0, 8) + ".tmp";
        try
        {
            using (FileStream stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                new DataContractJsonSerializer(typeof(StateDocument)).WriteObject(stream, state);
            }

            SharedFileAccess.Retry(() =>
            {
                if (File.Exists(stateFilePath))
                {
                    // File.Replace swaps the contents in one operation, so a concurrent reader never
                    // sees a missing or half-written file.
                    File.Replace(temporaryPath, stateFilePath, null);
                }
                else
                {
                    File.Move(temporaryPath, stateFilePath);
                }
            });
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                try
                {
                    File.Delete(temporaryPath);
                }
                catch (IOException)
                {
                    // A leftover temporary file is harmless and is overwritten on the next save.
                }
            }
        }
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
