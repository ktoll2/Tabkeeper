using BranchPins.Core;

namespace BranchPins.Core.Tests;

public sealed class GitRepositoryLocatorTests : IDisposable
{
    private readonly string temporaryDirectory = Path.Combine(Path.GetTempPath(), "BranchPins.Tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void TryLocate_ReturnsLocalBranchForRepositoryDirectory()
    {
        string repositoryRoot = CreateRepository("ref: refs/heads/feature/pinned-tabs\n");
        string nestedDirectory = Path.Combine(repositoryRoot, "src", "App");
        Directory.CreateDirectory(nestedDirectory);

        GitRepository? repository = GitRepositoryLocator.TryLocate(nestedDirectory);

        Assert.NotNull(repository);
        Assert.Equal(Path.GetFullPath(repositoryRoot), repository.RootPath);
        Assert.Equal("feature/pinned-tabs", repository.BranchId);
    }

    [Fact]
    public void TryLocate_ReturnsDetachedHeadIdentifier()
    {
        string repositoryRoot = CreateRepository("5eb7f4c0a8260f5018c5a39a2e452c65f87eb7d2\n");

        GitRepository? repository = GitRepositoryLocator.TryLocate(repositoryRoot);

        Assert.NotNull(repository);
        Assert.Equal("detached/5eb7f4c0a8260f5018c5a39a2e452c65f87eb7d2", repository.BranchId);
    }

    [Fact]
    public void TryLocate_ResolvesGitDirectoryFileForWorktree()
    {
        string repositoryRoot = Path.Combine(temporaryDirectory, "worktree");
        string metadataDirectory = Path.Combine(temporaryDirectory, "metadata");
        Directory.CreateDirectory(repositoryRoot);
        Directory.CreateDirectory(metadataDirectory);
        File.WriteAllText(Path.Combine(repositoryRoot, ".git"), "gitdir: ../metadata\n");
        File.WriteAllText(Path.Combine(metadataDirectory, "HEAD"), "ref: refs/heads/main\n");

        GitRepository? repository = GitRepositoryLocator.TryLocate(repositoryRoot);

        Assert.NotNull(repository);
        Assert.Equal(Path.GetFullPath(metadataDirectory), repository.GitDirectoryPath);
        Assert.Equal("main", repository.BranchId);
    }

    public void Dispose()
    {
        if (Directory.Exists(temporaryDirectory))
        {
            Directory.Delete(temporaryDirectory, recursive: true);
        }
    }

    private string CreateRepository(string headContents)
    {
        string repositoryRoot = Path.Combine(temporaryDirectory, "repository-" + Guid.NewGuid().ToString("N"));
        string gitDirectory = Path.Combine(repositoryRoot, ".git");
        Directory.CreateDirectory(gitDirectory);
        File.WriteAllText(Path.Combine(gitDirectory, "HEAD"), headContents);
        return repositoryRoot;
    }
}
