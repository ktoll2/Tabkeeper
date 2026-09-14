using Tabkeeper.Core;

namespace Tabkeeper.Core.Tests;

public sealed class ActivityLogTests : IDisposable
{
    private readonly string temporaryDirectory = Path.Combine(Path.GetTempPath(), "Tabkeeper.Tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void Write_UsesLineTemplateWithInstanceAndWorkspaceContext()
    {
        string logPath = Path.Combine(temporaryDirectory, "activity.log");
        ActivityLogContext context = new ActivityLogContext("MyApp", "MyApp.sln", "feature/login -> main");
        TabkeeperActivityLog activityLog = new TabkeeperActivityLog(logPath, "VS:18420/7eab71c2");

        activityLog.Write(ActivityLogLevel.Info, context, "Restored 4 pinned tabs.");

        activityLog.Dispose();
        string line = File.ReadAllText(logPath);
        Assert.Contains("[INFO] [VS:18420/7eab71c2] [MyApp] [MyApp.sln] [feature/login -> main] - Restored 4 pinned tabs.", line);
    }

    [Fact]
    public void Write_RendersDebugLevelLabel()
    {
        string logPath = Path.Combine(temporaryDirectory, "activity.log");
        TabkeeperActivityLog activityLog = new TabkeeperActivityLog(logPath, "VS:2/deadbeef");

        activityLog.Write(ActivityLogLevel.Debug, ActivityLogContext.System, "Recovered from a transient synchronization error: IOException: locked.");

        activityLog.Dispose();
        Assert.Contains("[DEBUG] [VS:2/deadbeef] [-] [-] [system] - Recovered from a transient synchronization error", File.ReadAllText(logPath));
    }

    [Fact]
    public void Write_AppendsEachLineAndCollapsesEmbeddedNewlines()
    {
        string logPath = Path.Combine(temporaryDirectory, "activity.log");
        TabkeeperActivityLog activityLog = new TabkeeperActivityLog(logPath, "VS:3/feedface");

        activityLog.Write(ActivityLogLevel.Info, ActivityLogContext.System, "First.");
        activityLog.Write(ActivityLogLevel.Info, ActivityLogContext.System, "Second\r\nstill second.");
        activityLog.Dispose();

        string[] lines = File.ReadAllLines(logPath);
        Assert.Equal(2, lines.Length);
        Assert.EndsWith(" - First.", lines[0]);
        Assert.EndsWith(" - Second still second.", lines[1]);
    }

    [Fact]
    public void Write_UsesSystemContextForSystemMessages()
    {
        string logPath = Path.Combine(temporaryDirectory, "activity.log");
        TabkeeperActivityLog activityLog = new TabkeeperActivityLog(logPath, "VS:1/abcd1234");
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
