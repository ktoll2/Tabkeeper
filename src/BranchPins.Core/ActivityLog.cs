using System;
using System.IO;
using Serilog;
using Serilog.Events;

namespace BranchPins.Core;

/// <summary>
/// Appends concise, local diagnostic records that users can open in Visual Studio.
/// </summary>
public sealed class BranchPinsActivityLog : IDisposable
{
    private readonly string filePath;
    private readonly string instanceIdentifier;
    private readonly ILogger logger;

    /// <summary>
    /// Initializes a shared Serilog file sink for one Visual Studio extension session.
    /// </summary>
    /// <param name="filePath">The user-local log file path.</param>
    /// <param name="instanceIdentifier">The <c>VS:&lt;pid&gt;/&lt;session&gt;</c> identifier for this extension load.</param>
    public BranchPinsActivityLog(string filePath, string instanceIdentifier)
    {
        this.filePath = filePath;
        this.instanceIdentifier = instanceIdentifier;
        string directory = Path.GetDirectoryName(filePath) ?? throw new InvalidOperationException("The activity log must have a parent directory.");
        Directory.CreateDirectory(directory);
        logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.File(
                filePath,
                outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz}] [{LogLevel}] [{Instance}] [{Repository}] [{Solution}] [{Branch}] - {Message:lj}{NewLine}{Exception}",
                // Multiple devenv.exe processes can share this user-level log file.
                shared: true)
            .CreateLogger();
    }

    public string FilePath => filePath;

    /// <summary>
    /// Writes one structured log event enriched with instance and workspace context.
    /// </summary>
    /// <param name="level">The operational severity of the event.</param>
    /// <param name="context">Repository, solution, and branch labels for the event.</param>
    /// <param name="message">The human-readable activity message.</param>
    public void Write(ActivityLogLevel level, ActivityLogContext context, string message)
    {
        ILogger contextualLogger = logger
            // Instance and workspace properties make concurrent Visual Studio sessions distinguishable in one shared log.
            .ForContext("LogLevel", ToDisplayLevel(level))
            .ForContext("Instance", instanceIdentifier)
            .ForContext("Repository", context.Repository)
            .ForContext("Solution", context.Solution)
            .ForContext("Branch", context.Branch);
        contextualLogger.Write(ToSerilogLevel(level), "{ActivityMessage}", message);
    }

    /// <summary>
    /// Creates the log lazily when a user opens it before another event has been recorded.
    /// </summary>
    public void EnsureFileExists()
    {
        if (!File.Exists(filePath))
        {
            Write(ActivityLogLevel.Info, ActivityLogContext.System, "Activity log created.");
        }
    }

    /// <summary>
    /// Flushes and disposes the underlying Serilog sink.
    /// </summary>
    public void Dispose()
    {
        (logger as IDisposable)?.Dispose();
    }

    /// <summary>
    /// Maps the extension's compact severity model to Serilog's event level.
    /// </summary>
    /// <param name="level">The extension activity severity.</param>
    /// <returns>The corresponding Serilog event level.</returns>
    private static LogEventLevel ToSerilogLevel(ActivityLogLevel level)
    {
        return level == ActivityLogLevel.Warn ? LogEventLevel.Warning : level == ActivityLogLevel.Error ? LogEventLevel.Error : LogEventLevel.Information;
    }

    /// <summary>
    /// Maps the extension's compact severity model to the fixed log-line display label.
    /// </summary>
    /// <param name="level">The extension activity severity.</param>
    /// <returns>The uppercase three-to-five character display label.</returns>
    private static string ToDisplayLevel(ActivityLogLevel level)
    {
        return level == ActivityLogLevel.Warn ? "WARN" : level == ActivityLogLevel.Error ? "ERROR" : "INFO";
    }
}

/// <summary>
/// Classifies activity log events by their operational severity.
/// </summary>
public enum ActivityLogLevel
{
    Info,
    Warn,
    Error,
}

/// <summary>
/// Provides the repository, solution, and branch labels rendered on every log line.
/// </summary>
public sealed class ActivityLogContext
{
    /// <summary>
    /// Gets context for events without a repository or solution, such as extension startup failures.
    /// </summary>
    public static readonly ActivityLogContext System = new ActivityLogContext("-", "-", "system");

    /// <summary>
    /// Initializes labels rendered in a structured activity-log line.
    /// </summary>
    /// <param name="repository">The display name of the Git repository, or <c>-</c>.</param>
    /// <param name="solution">The solution filename, or <c>-</c>.</param>
    /// <param name="branch">The branch, transition, or <c>system</c> label.</param>
    public ActivityLogContext(string repository, string solution, string branch)
    {
        Repository = string.IsNullOrWhiteSpace(repository) ? "-" : repository;
        Solution = string.IsNullOrWhiteSpace(solution) ? "-" : solution;
        Branch = string.IsNullOrWhiteSpace(branch) ? "system" : branch;
    }

    public string Repository { get; }

    public string Solution { get; }

    public string Branch { get; }
}
