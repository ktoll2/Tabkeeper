using BranchPins.Core;

namespace BranchPins.Core.Tests;

public sealed class ActivityLogTests : IDisposable
{
    private readonly string temporaryDirectory = Path.Combine(Path.GetTempPath(), "BranchPins.Tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void Write_UsesSerilogTemplateWithInstanceAndWorkspaceContext()
    {
        string logPath = Path.Combine(temporaryDirectory, "activity.log");
        ActivityLogContext context = new ActivityLogContext("MyApp", "MyApp.sln", "feature/login -> main");
        BranchPinsActivityLog activityLog = new BranchPinsActivityLog(logPath, "VS:18420/7eab71c2");

        activityLog.Write(ActivityLogLevel.Info, context, "Restored 4 pinned tabs.");

        activityLog.Dispose();
        string line = File.ReadAllText(logPath);
        Assert.Contains("[INFO] [VS:18420/7eab71c2] [MyApp] [MyApp.sln] [feature/login -> main] - Restored 4 pinned tabs.", line);
    }

    [Fact]
    public void Write_UsesSystemContextForSystemMessages()
    {
        string logPath = Path.Combine(temporaryDirectory, "activity.log");
        BranchPinsActivityLog activityLog = new BranchPinsActivityLog(logPath, "VS:1/abcd1234");
        activityLog.Write(ActivityLogLevel.Warn, ActivityLogContext.System, "No Git repository found.");

        activityLog.Dispose();
        string line = File.ReadAllText(logPath);
        Assert.Contains("[WARN] [VS:1/abcd1234] [-] [-] [system] - No Git repository found.", line);
    }

    public void Dispose()
    {
        if (Directory.Exists(temporaryDirectory))
        {
            Directory.Delete(temporaryDirectory, recursive: true);
        }
    }
}
