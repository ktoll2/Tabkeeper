using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BranchPins.Core;
using EnvDTE;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Threading;

namespace BranchPins.Vsix;

/// <summary>
/// Synchronizes open document pin states with the Git branch selected for the solution.
/// </summary>
internal sealed partial class BranchPinSynchronizer : IDisposable, IVsSolutionEvents
{
    private readonly JoinableTaskFactory joinableTaskFactory;
    private readonly IVsSolution solution;
    private readonly VisualStudioDocumentPins documentPins;
    private readonly PinnedTabStateStore stateStore;
    private readonly BranchPinsConfigurationStore configurationStore;
    private readonly BranchPinsActivityLog activityLog;
    private readonly SemaphoreSlim synchronizationGate = new SemaphoreSlim(1, 1);
    private readonly Timer pollingTimer;

    private FileSystemWatcher? headWatcher;
    private GitRepository? currentRepository;
    private uint solutionEventsCookie;
    private bool noRepositoryWarningLogged;
    private bool disposed;

    /// <summary>
    /// Initializes the coordinator that observes Git HEAD and applies branch-specific document pins.
    /// </summary>
    /// <param name="joinableTaskFactory">Schedules background observation and UI-thread transitions.</param>
    /// <param name="solution">Provides active-solution and lifecycle information.</param>
    /// <param name="uiShell">Provides document-window enumeration and shell UI services.</param>
    /// <param name="openDocument">Opens saved document paths in Visual Studio.</param>
    /// <param name="stateStore">Persists branch and shared pin sets.</param>
    /// <param name="configurationStore">Loads user-local behavior and rule configuration.</param>
    /// <param name="activityLog">Records synchronization activity.</param>
    /// <param name="dte">Restores the document active before a pin restore.</param>
    public BranchPinSynchronizer(
        JoinableTaskFactory joinableTaskFactory,
        IVsSolution solution,
        IVsUIShell uiShell,
        IVsUIShellOpenDocument openDocument,
        PinnedTabStateStore stateStore,
        BranchPinsConfigurationStore configurationStore,
        BranchPinsActivityLog activityLog,
        DTE dte)
    {
        this.joinableTaskFactory = joinableTaskFactory;
        this.solution = solution;
        documentPins = new VisualStudioDocumentPins(uiShell, openDocument, dte);
        this.stateStore = stateStore;
        this.configurationStore = configurationStore;
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
        Log(ActivityLogLevel.Info, TryGetSolutionRepository(), "system", "Branch Pins session started.");
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
    /// Toggles automatic synchronization in <c>config.json</c>.
    /// </summary>
    /// <returns>The newly persisted enabled state.</returns>
    public bool ToggleEnabled()
    {
        BranchPinsConfiguration configuration = GetConfiguration();
        configuration.Enabled = !configuration.Enabled;
        configurationStore.Save(configuration);
        UpdateSettings();
        return configuration.Enabled;
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
            catch
            {
                // Git may rewrite HEAD while a checkout is in progress. The watcher and polling fallback retry the observation.
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

        if (currentRepository is null || !VisualStudioDocumentPins.PathsEqual(currentRepository.RootPath, observedRepository.RootPath))
        {
            if (currentRepository is not null)
            {
                SaveCurrentPins(currentRepository);
            }

            currentRepository = observedRepository;
            WatchHeadFile(observedRepository);
            await DelayRestoreAsync();
            ApplyBranchPins(observedRepository);
            return;
        }

        if (!string.Equals(currentRepository.BranchId, observedRepository.BranchId, StringComparison.Ordinal))
        {
            Log(ActivityLogLevel.Info, observedRepository, currentRepository.BranchId + " -> " + observedRepository.BranchId, "Branch switch detected.");
            SaveCurrentPins(currentRepository);
            currentRepository = observedRepository;
            WatchHeadFile(observedRepository);
            await DelayRestoreAsync();
            ApplyBranchPins(observedRepository);
        }
        else if (forceApply)
        {
            SaveCurrentPins(currentRepository);
            await DelayRestoreAsync();
            ApplyBranchPins(currentRepository);
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
