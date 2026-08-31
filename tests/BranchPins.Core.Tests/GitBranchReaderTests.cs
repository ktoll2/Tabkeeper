using BranchPins.Core;

namespace BranchPins.Core.Tests;

public sealed class GitBranchReaderTests : IDisposable
{
    private readonly string temporaryDirectory = Path.Combine(Path.GetTempPath(), "BranchPins.Tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void GetLocalBranchNames_ReadsLooseAndPackedReferences()
    {
        string headsDirectory = Path.Combine(temporaryDirectory, "refs", "heads", "feature");
        Directory.CreateDirectory(headsDirectory);
        File.WriteAllText(Path.Combine(headsDirectory, "login"), "abc");
        File.WriteAllText(Path.Combine(temporaryDirectory, "packed-refs"), "123 refs/heads/main\n456 refs/tags/v1\n");

        ISet<string> branchNames = GitBranchReader.GetLocalBranchNames(temporaryDirectory);

        Assert.Contains("feature/login", branchNames);
        Assert.Contains("main", branchNames);
        Assert.DoesNotContain("v1", branchNames);
    }

    public void Dispose()
    {
        if (Directory.Exists(temporaryDirectory))
        {
            Directory.Delete(temporaryDirectory, recursive: true);
        }
    }
}
