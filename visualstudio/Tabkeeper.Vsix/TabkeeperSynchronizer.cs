using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Tabkeeper.Core;
using EnvDTE;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Threading;

namespace Tabkeeper.Vsix;

/// <summary>
/// Synchronizes open document pin states with the Git branch selected for the solution.
/// </summary>
internal sealed partial class TabkeeperSynchronizer : IDisposable, IVsSolutionEvents
{
    private readonly JoinableTaskFactory joinableTaskFactory;
    private readonly IVsSolution solution;
    private readonly VisualStudioDocumentPins documentPins;
    private readonly PinnedTabStateStore stateStore;
    private readonly ITabkeeperConfigurationProvider configuration;
    private readonly TabkeeperActivityLog activityLog;
    private readonly SemaphoreSlim synchronizationGate = new SemaphoreSlim(1, 1);
    private readonly Timer pollingTimer;

    private const int RestoreRetryLimit = 3;
    private const int RestoreRetryDelayMilliseconds = 1500;

    private FileSystemWatcher? headWatcher;
    private GitRepository? currentRepository;
    private uint solutionEventsCookie;
    private bool noRepositoryWarningLogged;
    private bool disposed;
    private Exception? lastSwallowedException;
    private string? loggedConfigurationError;

    /// <summary>
    /// Initializes the coordinator that observes Git HEAD and applies branch-specific document pins.
    /// </summary>
    /// <param name="joinableTaskFactory">Schedules background observation and UI-thread transitions.</param>
    /// <param name="solution">Provides active-solution and lifecycle information.</param>
    /// <param name="uiShell">Provides document-window enumeration and shell UI services.</param>
    /// <param name="openDocument">Opens saved document paths in Visual Studio.</param>
    /// <param name="stateStore">Persists branch and shared pin sets.</param>
    /// <param name="configuration">Supplies current behavior and rule configuration.</param>
    /// <param name="activityLog">Records synchronization activity.</param>
    /// <param name="dte">Restores the document active before a pin restore.</param>
    public TabkeeperSynchronizer(
        JoinableTaskFactory joinableTaskFactory,
        IVsSolution solution,
        IVsUIShell uiShell,
        IVsUIShellOpenDocument openDocument,
        PinnedTabStateStore stateStore,
        ITabkeeperConfigurationProvider configuration,
        TabkeeperActivityLog activityLog,
        DTE dte)
    {
        this.joinableTaskFactory = joinableTaskFactory;
        this.solution = solution;
        documentPins = new VisualStudioDocumentPins(uiShell, openDocument, dte);
        this.stateStore = stateStore;
        this.configuration = configuration;
        this.activityLog = activityLog;
        pollingTimer = new Timer(OnPollingTimerElapsed, null, Timeout.Infinite, Timeout.Infinite);
    }

    /// <summary>
    /// Starts Git HEAD monitoring and subscribes to solution lifecycle events.
    /// </summary>
    public void Start()
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        ErrorHandler.ThrowOnFailure(solution.AdviseSolutionEvents(this, out solutionEventsCookie));
        Log(ActivityLogLevel.Info, TryGetSolutionRepository(), "system", "Tabkeeper session started.");
        QueueSynchronization();
        UpdatePollingInterval();
    }

    /// <summary>
    /// Applies the current configuration's polling interval and synchronizes when enabled.
    /// </summary>
    public void UpdateSettings()
    {
        UpdatePollingInterval();
        if (GetConfiguration().Enabled)
        {
            SynchronizeNow();
        }
    }

    /// <summary>
    /// Queues a manual save-and-restore cycle for the active branch.
    /// </summary>
    public void SynchronizeNow()
    {
        QueueSynchronization(forceApply: true);
    }

    /// <summary>
    /// Toggles automatic synchronization and persists the new state.
    /// </summary>
    /// <returns>The newly persisted enabled state.</returns>
    public bool ToggleEnabled()
    {
        bool enabled = !configuration.Current.Enabled;
        configuration.SetEnabled(enabled);
        UpdateSettings();
        return enabled;
    }

    public bool IsEnabled => GetConfiguration().Enabled;

    /// <summary>
    /// Stops timers, filesystem watching, and solution event subscriptions.
    /// </summary>
    public void Dispose()
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        if (disposed)
        {
            return;
        }

        disposed = true;
        pollingTimer.Dispose();
        if (headWatcher is not null)
        {
            headWatcher.EnableRaisingEvents = false;
            headWatcher.Dispose();
        }

        if (solutionEventsCookie != 0)
        {
            solution.UnadviseSolutionEvents(solutionEventsCookie);
            solutionEventsCookie = 0;
        }

    }

    /// <summary>
    /// Queues an observation from the fallback polling timer.
    /// </summary>
    /// <param name="state">Unused timer state.</param>
    private void OnPollingTimerElapsed(object? state)
    {
        QueueSynchronization();
    }

    /// <summary>
    /// Queues an observation after Git rewrites, creates, or renames its HEAD metadata file.
    /// </summary>
    /// <param name="sender">The filesystem watcher that raised the event.</param>
    /// <param name="eventArgs">The filesystem change details.</param>
    private void OnHeadChanged(object sender, FileSystemEventArgs eventArgs)
    {
        QueueSynchronization();
    }

    /// <summary>
    /// Serializes an asynchronous repository observation and optional restore operation.
    /// </summary>
    /// <param name="forceApply">Whether to apply saved pins even when the observed branch has not changed.</param>
    private void QueueSynchronization(bool forceApply = false)
    {
        if (disposed)
        {
            return;
        }

        // HEAD file events can arrive in bursts during checkout; serialize them so an older transition cannot overwrite a newer pin set.
        JoinableTask synchronizationTask = joinableTaskFactory.RunAsync(async delegate
        {
            await synchronizationGate.WaitAsync();
            try
            {
                if (!disposed)
                {
                    await SynchronizeAsync(forceApply);
                }
            }
            catch (Exception exception)
            {
                // Git may rewrite HEAD while a checkout is in progress. The watcher and polling fallback
                // retry the observation; the swallowed error is logged at Debug on the next pass.
                lastSwallowedException = exception;
            }
            finally
            {
                synchronizationGate.Release();
            }
        });
    }

    /// <summary>
    /// Observes the active repository, saves outgoing pins, and restores incoming pins when required.
    /// </summary>
    /// <param name="forceApply">Whether to restore even without a branch transition.</param>
    /// <returns>A task that completes after all required UI-thread synchronization work has run.</returns>
    private async Task SynchronizeAsync(bool forceApply)
    {
        await joinableTaskFactory.SwitchToMainThreadAsync();
        LogPendingDiagnostics();

        GitRepository? observedRepository = TryGetSolutionRepository();
        if (observedRepository is null)
        {
            if (!noRepositoryWarningLogged)
            {
                Log(ActivityLogLevel.Warn, null, "system", "No Git repository found for the active solution.");
                noRepositoryWarningLogged = true;
            }

            return;
        }

        noRepositoryWarningLogged = false;
        if (!GetConfiguration().Enabled)
        {
            return;
        }

        bool repositoryChanged = currentRepository is null
            || !VisualStudioDocumentPins.PathsEqual(currentRepository.RootPath, observedRepository.RootPath);
        bool branchChanged = !repositoryChanged
            && !string.Equals(currentRepository!.BranchId, observedRepository.BranchId, StringComparison.Ordinal);
        if (!repositoryChanged && !branchChanged && !forceApply)
        {
            return;
        }

        bool firstObservation = currentRepository is null;
        if (branchChanged)
        {
            Log(ActivityLogLevel.Info, observedRepository, currentRepository!.BranchId + " -> " + observedRepository.BranchId, "Branch switch detected.");
        }

        if (currentRepository is not null)
        {
            SaveCurrentPins(currentRepository);
        }

        currentRepository = observedRepository;
        WatchHeadFile(observedRepository);
        if (firstObservation)
        {
            AdoptExistingPinsIfUnseen(observedRepository);
        }

        await DelayRestoreAsync();
        bool fullyRestored = ApplyTabkeeper(observedRepository);
        if (!fullyRestored && !forceApply)
        {
            // A checkout that is still writing files leaves saved tabs briefly missing; re-apply shortly.
            ScheduleRestoreRetry(observedRepository.BranchId, RestoreRetryLimit);
        }
    }

    /// <summary>
    /// Re-applies the saved pin set for a branch after a short delay, to recover tabs whose files a
    /// slow branch checkout had not finished writing when the first restore ran.
    /// </summary>
    /// <param name="branchId">The branch the retry must still be on to proceed.</param>
    /// <param name="attemptsRemaining">The number of retry attempts left before giving up.</param>
    private void ScheduleRestoreRetry(string branchId, int attemptsRemaining)
    {
        if (disposed || attemptsRemaining <= 0)
        {
            return;
        }

        JoinableTask retryTask = joinableTaskFactory.RunAsync(async delegate
        {
            await Task.Delay(RestoreRetryDelayMilliseconds);
            await synchronizationGate.WaitAsync();
            try
            {
                if (disposed)
                {
                    return;
                }

                await joinableTaskFactory.SwitchToMainThreadAsync();
                GitRepository? observedRepository = TryGetSolutionRepository();
                if (observedRepository is null
                    || currentRepository is null
                    || !VisualStudioDocumentPins.PathsEqual(currentRepository.RootPath, observedRepository.RootPath)
                    || !string.Equals(observedRepository.BranchId, branchId, StringComparison.Ordinal)
                    || !GetConfiguration().Enabled)
                {
                    // The solution, repository, or branch moved on; the newer observation owns the state now.
                    return;
                }

                if (!ApplyTabkeeper(observedRepository, isRetry: true))
                {
                    ScheduleRestoreRetry(branchId, attemptsRemaining - 1);
                }
            }
            catch (Exception exception)
            {
                lastSwallowedException = exception;
            }
            finally
            {
                synchronizationGate.Release();
            }
        });
    }

    /// <summary>
    /// Writes deferred diagnostics that could not be logged from a non-UI-thread context: a swallowed
    /// transient synchronization error and an unreadable configuration file.
    /// </summary>
    private void LogPendingDiagnostics()
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        Exception? swallowed = Interlocked.Exchange(ref lastSwallowedException, null);
        if (swallowed is not null)
        {
            Log(ActivityLogLevel.Debug, TryGetSolutionRepository(), "system",
                $"Recovered from a transient synchronization error: {swallowed.GetType().Name}: {swallowed.Message}");
        }

        string? configurationError = configuration.LastError;
        if (configurationError is null)
        {
            loggedConfigurationError = null;
        }
        else if (!string.Equals(configurationError, loggedConfigurationError, StringComparison.Ordinal))
        {
            loggedConfigurationError = configurationError;
            Log(ActivityLogLevel.Warn, TryGetSolutionRepository(), "system", configurationError);
        }
    }

    /// <summary>
    /// Locates the Git repository containing the active solution without invoking the Git executable.
    /// </summary>
    /// <returns>The current solution repository, or <see langword="null"/> when none can be read.</returns>
    private GitRepository? TryGetSolutionRepository()
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        ErrorHandler.ThrowOnFailure(solution.GetSolutionInfo(out string solutionDirectory, out string solutionFile, out string userOptionsFile));
        string candidatePath = !string.IsNullOrEmpty(solutionFile)
            ? Path.GetDirectoryName(solutionFile) ?? solutionDirectory
            : solutionDirectory;
        return GitRepositoryLocator.TryLocate(candidatePath);
    }

    /// <summary>
    /// Replaces the HEAD watcher when the active repository's Git metadata directory changes.
    /// </summary>
    /// <param name="repository">The repository whose HEAD file should be watched.</param>
    private void WatchHeadFile(GitRepository repository)
    {
        string headPath = Path.Combine(repository.GitDirectoryPath, "HEAD");
        if (headWatcher is not null && VisualStudioDocumentPins.PathsEqual(headWatcher.Path, repository.GitDirectoryPath))
        {
            return;
        }

        if (headWatcher is not null)
        {
            headWatcher.EnableRaisingEvents = false;
            headWatcher.Dispose();
        }

        headWatcher = new FileSystemWatcher(repository.GitDirectoryPath, Path.GetFileName(headPath))
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.CreationTime,
            EnableRaisingEvents = true,
        };
        headWatcher.Changed += OnHeadChanged;
        headWatcher.Created += OnHeadChanged;
        headWatcher.Renamed += OnHeadChanged;
    }

    /// <summary>
    /// Adds solution and repository labels before writing a structured activity-log record.
    /// </summary>
    /// <param name="level">The activity severity.</param>
    /// <param name="repository">The optional repository associated with the event.</param>
    /// <param name="branch">The branch or branch-transition display label.</param>
    /// <param name="message">The human-readable activity message.</param>
    private void Log(ActivityLogLevel level, GitRepository? repository, string branch, string message)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        ErrorHandler.ThrowOnFailure(solution.GetSolutionInfo(out string solutionDirectory, out string solutionFile, out string userOptionsFile));
        string repositoryName = repository is null ? "-" : new DirectoryInfo(repository.RootPath).Name;
        string solutionName = string.IsNullOrEmpty(solutionFile) ? "-" : Path.GetFileName(solutionFile);
        activityLog.Write(level, new ActivityLogContext(repositoryName, solutionName, branch), message);
    }

    /// <summary>
    /// Applies the current configured fallback polling interval to the monitoring timer.
    /// </summary>
    private void UpdatePollingInterval()
    {
        int intervalSeconds = GetConfiguration().GetPollingIntervalSeconds();
        pollingTimer.Change(TimeSpan.FromSeconds(intervalSeconds), TimeSpan.FromSeconds(intervalSeconds));
    }

    /// <summary>
    /// Waits for the configured checkout-settlement delay and returns execution to the UI thread.
    /// </summary>
    /// <returns>A task that completes after the configured delay and UI-thread switch.</returns>
    private async Task DelayRestoreAsync()
    {
        int delayMilliseconds = GetConfiguration().GetRestoreDelayMilliseconds();
        if (delayMilliseconds > 0)
        {
            await Task.Delay(delayMilliseconds);
            await joinableTaskFactory.SwitchToMainThreadAsync();
        }
    }

}
