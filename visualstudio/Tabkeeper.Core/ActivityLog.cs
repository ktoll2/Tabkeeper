using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace Tabkeeper.Core;

/// <summary>
/// Appends concise, local diagnostic records that users can open in Visual Studio. Each write opens
/// the file, appends one line, and closes it, coordinated by a system-wide mutex so that multiple
/// <c>devenv.exe</c> processes can share one user-level log without interleaving or losing lines.
/// </summary>
public sealed class TabkeeperActivityLog : IDisposable
{
    private static readonly Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    private readonly string filePath;
    private readonly string instanceIdentifier;
    private readonly string mutexName;

    /// <summary>
    /// Initializes the activity log for one Visual Studio extension session.
    /// </summary>
    /// <param name="filePath">The user-local log file path.</param>
    /// <param name="instanceIdentifier">The <c>VS:&lt;pid&gt;/&lt;session&gt;</c> identifier for this extension load.</param>
    public TabkeeperActivityLog(string filePath, string instanceIdentifier)
    {
        this.filePath = filePath;
        this.instanceIdentifier = instanceIdentifier;
        string directory = Path.GetDirectoryName(filePath) ?? throw new InvalidOperationException("The activity log must have a parent directory.");
        Directory.CreateDirectory(directory);
        mutexName = SharedFileAccess.MutexNameFor("ActivityLog", filePath);
    }

    public string FilePath => filePath;

    /// <summary>
    /// Appends one activity-log line enriched with instance and workspace context.
    /// </summary>
    /// <param name="level">The operational severity of the event.</param>
    /// <param name="context">Repository, solution, and branch labels for the event.</param>
    /// <param name="message">The human-readable activity message.</param>
    public void Write(ActivityLogLevel level, ActivityLogContext context, string message)
    {
        string line = string.Format(
            CultureInfo.InvariantCulture,
            "[{0:yyyy-MM-dd HH:mm:ss.fff zzz}] [{1}] [{2}] [{3}] [{4}] [{5}] - {6}",
            DateTimeOffset.Now,
            ToDisplayLevel(level),
            instanceIdentifier,
            context.Repository,
            context.Solution,
            context.Branch,
            (message ?? string.Empty).Replace("\r\n", " ").Replace("\r", " ").Replace("\n", " "));

        AppendLine(line);
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
    /// No-op. Every <see cref="Write"/> closes the file, so there is no buffered state to flush.
    /// </summary>
    public void Dispose()
    {
    }

    /// <summary>
    /// Appends one line under a cross-process mutex, retrying briefly if the file is momentarily locked.
    /// Diagnostics must never destabilize the extension, so an unrecoverable failure is dropped silently.
    /// </summary>
    /// <param name="line">The fully formatted log line, without a trailing newline.</param>
    private void AppendLine(string line)
    {
        try
        {
            using (SharedFileAccess.Lock(mutexName, TimeSpan.FromSeconds(5)))
            {
                SharedFileAccess.Retry(() =>
                {
                    using FileStream stream = new FileStream(filePath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
                    using StreamWriter writer = new StreamWriter(stream, Utf8NoBom);
                    writer.WriteLine(line);
                });
            }
        }
        catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
        {
            // Diagnostics must never destabilize the extension; a log line that cannot be written is dropped.
        }
    }

    /// <summary>
    /// Maps the extension's compact severity model to the fixed log-line display label.
    /// </summary>
    /// <param name="level">The extension activity severity.</param>
    /// <returns>The uppercase three-to-five character display label.</returns>
    private static string ToDisplayLevel(ActivityLogLevel level)
    {
        return level switch
        {
            ActivityLogLevel.Debug => "DEBUG",
            ActivityLogLevel.Warn => "WARN",
            ActivityLogLevel.Error => "ERROR",
            _ => "INFO",
        };
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

    /// <summary>Diagnostic detail, such as a swallowed transient error.</summary>
    Debug,
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
