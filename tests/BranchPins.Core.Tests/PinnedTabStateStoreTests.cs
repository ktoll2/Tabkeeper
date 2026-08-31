using BranchPins.Core;

namespace BranchPins.Core.Tests;

public sealed class PinnedTabStateStoreTests : IDisposable
{
    private readonly string temporaryDirectory = Path.Combine(Path.GetTempPath(), "BranchPins.Tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void SavePinnedPaths_PersistsSeparateSetsForEachBranch()
    {
        string statePath = Path.Combine(temporaryDirectory, "state.json");
        string repositoryRoot = Path.Combine(temporaryDirectory, "repository");
        PinnedTabStateStore stateStore = new PinnedTabStateStore(statePath);

        stateStore.SavePinnedPaths(repositoryRoot, "main", new[] { "Program.cs", "Services/Auth.cs" });
        stateStore.SavePinnedPaths(repositoryRoot, "feature/login", new[] { "Login.cs" });

        Assert.Equal(new[] { "Program.cs", "Services/Auth.cs" }, stateStore.GetPinnedPaths(repositoryRoot, "main"));
        Assert.Equal(new[] { "Login.cs" }, stateStore.GetPinnedPaths(repositoryRoot, "feature/login"));
    }

    [Fact]
    public void SavePinnedPaths_RemovesUnsafeAndDuplicatePaths()
    {
        string statePath = Path.Combine(temporaryDirectory, "state.json");
        string repositoryRoot = Path.Combine(temporaryDirectory, "repository");
        PinnedTabStateStore stateStore = new PinnedTabStateStore(statePath);

        stateStore.SavePinnedPaths(repositoryRoot, "main", new[] { "App.cs", "app.cs", "../secrets.txt", Path.GetTempFileName() });

        Assert.Equal(new[] { "App.cs" }, stateStore.GetPinnedPaths(repositoryRoot, "main"));
    }

    [Fact]
    public void ClearPinnedPaths_RemovesOnlyTheSelectedBranch()
    {
        string statePath = Path.Combine(temporaryDirectory, "state.json");
        string repositoryRoot = Path.Combine(temporaryDirectory, "repository");
        PinnedTabStateStore stateStore = new PinnedTabStateStore(statePath);
        stateStore.SavePinnedPaths(repositoryRoot, "main", new[] { "Program.cs" });
        stateStore.SavePinnedPaths(repositoryRoot, "feature/login", new[] { "Login.cs" });

        stateStore.ClearPinnedPaths(repositoryRoot, "main");

        Assert.Empty(stateStore.GetPinnedPaths(repositoryRoot, "main"));
        Assert.Equal(new[] { "Login.cs" }, stateStore.GetPinnedPaths(repositoryRoot, "feature/login"));
    }

    [Fact]
    public void RemoveMissingBranchStates_PreservesSharedAndExistingStates()
    {
        string statePath = Path.Combine(temporaryDirectory, "state.json");
        string repositoryRoot = Path.Combine(temporaryDirectory, "repository");
        PinnedTabStateStore stateStore = new PinnedTabStateStore(statePath);
        stateStore.SavePinnedPaths(repositoryRoot, "main", new[] { "Main.cs" });
        stateStore.SavePinnedPaths(repositoryRoot, "deleted", new[] { "Old.cs" });
        stateStore.SavePinnedPaths(repositoryRoot, "set/features", new[] { "Shared.cs" });

        int removedCount = stateStore.RemoveMissingBranchStates(repositoryRoot, new HashSet<string> { "main" });

        Assert.Equal(1, removedCount);
        Assert.Equal(new[] { "Main.cs" }, stateStore.GetPinnedPaths(repositoryRoot, "main"));
        Assert.Empty(stateStore.GetPinnedPaths(repositoryRoot, "deleted"));
        Assert.Equal(new[] { "Shared.cs" }, stateStore.GetPinnedPaths(repositoryRoot, "set/features"));
    }

    public void Dispose()
    {
        if (Directory.Exists(temporaryDirectory))
        {
            Directory.Delete(temporaryDirectory, recursive: true);
        }
    }
}
