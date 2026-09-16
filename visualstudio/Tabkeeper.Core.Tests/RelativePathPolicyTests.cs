using Tabkeeper.Core;

namespace Tabkeeper.Core.Tests;

public sealed class RelativePathPolicyTests
{
    [Theory]
    [InlineData("src/App/Program.cs", true)]
    [InlineData("Program.cs", true)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData("../secret.txt", false)]
    [InlineData("src/../../secret.txt", false)]
    [InlineData(@"C:\Windows\System32\hosts", false)]
    [InlineData("/etc/passwd", false)]
    public void IsSafe_AcceptsOnlyRelativeInRepositoryPaths(string path, bool expected)
    {
        Assert.Equal(expected, RelativePathPolicy.IsSafe(path));
    }

    [Fact]
    public void Normalize_DropsUnsafePathsAndReturnsDistinctOrderedResults()
    {
        string[] input =
        {
            "src/Zeta.cs",
            "src/alpha.cs",
            "src/Alpha.cs",
            "../escape.cs",
            "",
            @"D:\outside.cs",
        };

        Assert.Equal(new[] { "src/alpha.cs", "src/Zeta.cs" }, RelativePathPolicy.Normalize(input).ToArray());
    }
}
