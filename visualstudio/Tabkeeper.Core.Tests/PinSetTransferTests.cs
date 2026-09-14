using Tabkeeper.Core;

namespace Tabkeeper.Core.Tests;

public sealed class PinSetTransferTests : IDisposable
{
    private readonly string temporaryDirectory = Path.Combine(Path.GetTempPath(), "Tabkeeper.Tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void ExportAndImport_RoundTripsOnlySafeRepositoryRelativePaths()
    {
        string exportPath = Path.Combine(temporaryDirectory, "pins.tabkeeper.json");
        Directory.CreateDirectory(temporaryDirectory);

        PinSetTransfer.Export(exportPath, "feature/login", new[] { "App.cs", "app.cs", "../secret.txt", Path.Combine(Path.GetTempPath(), "outside.cs") });
        PinSetTransfer imported = PinSetTransfer.Import(exportPath);

        Assert.Equal("feature/login", imported.SourceBranch);
        Assert.Equal(new[] { "App.cs" }, imported.PinnedPaths);
    }

    public void Dispose()
    {
        if (Directory.Exists(temporaryDirectory))
        {
            Directory.Delete(temporaryDirectory, recursive: true);
        }
    }
}
