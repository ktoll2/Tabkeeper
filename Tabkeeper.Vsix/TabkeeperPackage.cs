using System;
using System.ComponentModel.Composition;
using System.ComponentModel.Composition.Primitives;
using System.ComponentModel.Design;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Tabkeeper.Core;
using EnvDTE;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.ComponentModelHost;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.Win32;

namespace Tabkeeper.Vsix;

[PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
[ProvideAutoLoad(VSConstants.UICONTEXT.SolutionExists_string, PackageAutoLoadFlags.BackgroundLoad)]
[ProvideMenuResource("Menus.ctmenu", 1)]
// Registers the unified-settings manifest (Content, shipped in the package folder) that VS 2026's
// Settings UI reads. If it fails to load in VS, the file-backed fallback provider still works.
[ProvideSettingsManifest(PackageRelativeManifestFile = "TabkeeperSettings.registration.json")]
[Guid(PackageGuid)]
public sealed class TabkeeperPackage : AsyncPackage
{
    /// <summary>
    /// Identifies the Visual Studio package registration for Tabkeeper.
    /// </summary>
    public const string PackageGuid = "75ff4b05-e5bc-4143-a7ca-86e17d6aa50b";

    /// <summary>The loaded package instance, or <see langword="null"/> before load or after dispose.</summary>
    internal static TabkeeperPackage? Current { get; private set; }

    private TabkeeperSynchronizer? synchronizer;
    private IVsUIShellOpenDocument? openDocument;
    private PinnedTabStateStore? stateStore;
    private ITabkeeperConfigurationProvider? configuration;
    private TabkeeperActivityLog? activityLog;

    /// <summary>
    /// Acquires Visual Studio services and starts branch pin monitoring after solution load.
    /// </summary>
    protected override async Task InitializeAsync(CancellationToken cancellationToken, IProgress<ServiceProgressData> progress)
    {
        await JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);

        IVsSolution solution = await GetServiceAsync(typeof(SVsSolution)) as IVsSolution
            ?? throw new InvalidOperationException("The Visual Studio solution service is unavailable.");
        IVsUIShell uiShell = await GetServiceAsync(typeof(SVsUIShell)) as IVsUIShell
            ?? throw new InvalidOperationException("The Visual Studio shell service is unavailable.");
        openDocument = await GetServiceAsync(typeof(SVsUIShellOpenDocument)) as IVsUIShellOpenDocument
            ?? throw new InvalidOperationException("The Visual Studio document-opening service is unavailable.");
        DTE dte = await GetServiceAsync(typeof(SDTE)) as DTE
            ?? throw new InvalidOperationException("The Visual Studio automation service is unavailable.");

        stateStore = new PinnedTabStateStore(TabkeeperPaths.StateFile);

        // Prefer the Visual Studio settings store (the backing store for the unified Settings page).
        // Fall back to the local config.json file if the service cannot be resolved.
        configuration = await TryCreateUnifiedConfigurationAsync()
            ?? new TabkeeperConfigurationStore(TabkeeperPaths.ConfigFile);

        string sessionId = Guid.NewGuid().ToString("N").Substring(0, 8);
        string instanceIdentifier = $"VS:{System.Diagnostics.Process.GetCurrentProcess().Id}/{sessionId}";
        activityLog = new TabkeeperActivityLog(TabkeeperPaths.ActivityLogFile, instanceIdentifier);
        synchronizer = new TabkeeperSynchronizer(JoinableTaskFactory, solution, uiShell, openDocument, stateStore, configuration, activityLog, dte);
        synchronizer.Start();
        AddMenuCommands();
        Current = this;
    }

    /// <summary>
    /// Builds the settings-store-backed configuration provider, or returns <see langword="null"/> when
    /// the Visual Studio settings service cannot be resolved so the caller can fall back to the file.
    /// </summary>
    private async Task<ITabkeeperConfigurationProvider?> TryCreateUnifiedConfigurationAsync()
    {
        try
        {
            IComponentModel? componentModel = await GetServiceAsync(typeof(SComponentModel)) as IComponentModel;
            if (componentModel is null)
            {
                return null;
            }

            // Resolve ISettingsManager by MEF contract name so there is no compile-time reference to
            // Microsoft.VisualStudio.Settings.15.0 (its x86 reference assembly trips MSB3270 in an
            // AnyCPU project). Visual Studio provides the real assembly at runtime.
            ContractBasedImportDefinition import = new ContractBasedImportDefinition(
                UnifiedSettingsConfigurationProvider.SettingsManagerContract,
                UnifiedSettingsConfigurationProvider.SettingsManagerContract,
                requiredMetadata: null,
                ImportCardinality.ZeroOrOne,
                isRecomposable: false,
                isPrerequisite: false,
                CreationPolicy.Any);

            object? settingsManager = componentModel.DefaultExportProvider.GetExports(import)
                .Select(export => export.Value)
                .FirstOrDefault(value => value is not null);

            return settingsManager is null ? null : new UnifiedSettingsConfigurationProvider(settingsManager);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return null;
        }
    }

    /// <summary>
    /// Re-reads configuration and re-synchronizes when the Tools &gt; Options page saves changes,
    /// so edits take effect without a manual <b>Sync Tabkeeper Now</b>.
    /// </summary>
    internal static void ApplySettingsIfRunning()
    {
        TabkeeperPackage? package = Current;
        if (package?.synchronizer is null)
        {
            return;
        }

        ThreadHelper.JoinableTaskFactory.Run(async () =>
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            package.synchronizer?.UpdateSettings();
        });
    }

    protected override void Dispose(bool disposing)
    {
        if (!disposing)
        {
            base.Dispose(disposing);
            return;
        }

        ThreadHelper.ThrowIfNotOnUIThread();
        Current = null;
        synchronizer?.Dispose();
        activityLog?.Dispose();
        base.Dispose(disposing);
    }

    private void AddMenuCommands()
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        OleMenuCommandService commandService = GetService(typeof(IMenuCommandService)) as OleMenuCommandService
            ?? throw new InvalidOperationException("The Visual Studio menu command service is unavailable.");

        OleMenuCommand enableCommand = new OleMenuCommand(OnEnableTabkeeper, new CommandID(TabkeeperCommands.CommandSet, TabkeeperCommands.Enable));
        enableCommand.BeforeQueryStatus += OnEnableTabkeeperStatus;
        commandService.AddCommand(enableCommand);
        commandService.AddCommand(new MenuCommand(OnSynchronizeNow, new CommandID(TabkeeperCommands.CommandSet, TabkeeperCommands.SynchronizeNow)));
        commandService.AddCommand(new MenuCommand(OnClearCurrentTabkeeper, new CommandID(TabkeeperCommands.CommandSet, TabkeeperCommands.ClearCurrentTabkeeper)));
        commandService.AddCommand(new MenuCommand(OnOpenSavedPinState, new CommandID(TabkeeperCommands.CommandSet, TabkeeperCommands.OpenSavedPinState)));
        commandService.AddCommand(new MenuCommand(OnManageBranchRules, new CommandID(TabkeeperCommands.CommandSet, TabkeeperCommands.ManageBranchRules)));
        commandService.AddCommand(new MenuCommand(OnExportCurrentPinSet, new CommandID(TabkeeperCommands.CommandSet, TabkeeperCommands.ExportCurrentPinSet)));
        commandService.AddCommand(new MenuCommand(OnImportIntoCurrentPinSet, new CommandID(TabkeeperCommands.CommandSet, TabkeeperCommands.ImportIntoCurrentPinSet)));
        commandService.AddCommand(new MenuCommand(OnCopyCurrentPinsToBranch, new CommandID(TabkeeperCommands.CommandSet, TabkeeperCommands.CopyCurrentPinsToBranch)));
        commandService.AddCommand(new MenuCommand(OnCleanUpMissingBranches, new CommandID(TabkeeperCommands.CommandSet, TabkeeperCommands.CleanUpMissingBranches)));
        commandService.AddCommand(new MenuCommand(OnOpenActivityLog, new CommandID(TabkeeperCommands.CommandSet, TabkeeperCommands.OpenActivityLog)));
    }

    private void OnEnableTabkeeper(object sender, EventArgs e)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        synchronizer?.ToggleEnabled();
    }

    private void OnEnableTabkeeperStatus(object sender, EventArgs e)
    {
        if (sender is OleMenuCommand command && synchronizer is not null)
        {
            command.Checked = synchronizer.IsEnabled;
        }
    }

    private void OnSynchronizeNow(object sender, EventArgs e)
    {
        synchronizer?.SynchronizeNow();
    }

    private void OnClearCurrentTabkeeper(object sender, EventArgs e)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        synchronizer?.ClearCurrentTabkeeper();
    }

    private void OnOpenSavedPinState(object sender, EventArgs e)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        if (stateStore is null)
        {
            return;
        }

        stateStore.EnsureStateFileExists();
        OpenFile(stateStore.StateFilePath);
    }

    private void OnManageBranchRules(object sender, EventArgs e)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        if (configuration is TabkeeperConfigurationStore store)
        {
            // File-backed fallback: open config.json directly.
            OpenFile(store.FilePath);
        }
        else if (GetService(typeof(SDTE)) is DTE dte)
        {
            // Settings-backed: rules live in the "Branch rules (JSON)" setting on the options page.
            dte.ExecuteCommand("Tools.Options");
        }
    }

    private void OnExportCurrentPinSet(object sender, EventArgs e)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        SaveFileDialog dialog = new SaveFileDialog { Filter = "Tabkeeper export (*.tabkeeper.json)|*.tabkeeper.json", AddExtension = true };
        if (dialog.ShowDialog() == true)
        {
            synchronizer?.ExportCurrentPinSet(dialog.FileName);
        }
    }

    private void OnImportIntoCurrentPinSet(object sender, EventArgs e)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        OpenFileDialog dialog = new OpenFileDialog { Filter = "Tabkeeper export (*.tabkeeper.json)|*.tabkeeper.json" };
        if (dialog.ShowDialog() == true)
        {
            synchronizer?.ImportIntoCurrentPinSet(dialog.FileName);
        }
    }

    private void OnCopyCurrentPinsToBranch(object sender, EventArgs e)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        string? branchName = BranchNameDialog.Prompt();
        if (!string.IsNullOrWhiteSpace(branchName))
        {
            synchronizer?.CopyCurrentPinsToBranch(branchName!.Trim());
        }
    }

    private void OnCleanUpMissingBranches(object sender, EventArgs e)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        synchronizer?.CleanUpMissingBranches();
    }

    private void OnOpenActivityLog(object sender, EventArgs e)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        if (activityLog is not null)
        {
            activityLog.EnsureFileExists();
            OpenFile(activityLog.FilePath);
        }
    }

    private void OpenFile(string filePath)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        if (openDocument is null)
        {
            return;
        }

        Guid logicalView = VSConstants.LOGVIEWID_Primary;
        openDocument.OpenDocumentViaProject(
            filePath,
            ref logicalView,
            out Microsoft.VisualStudio.OLE.Interop.IServiceProvider serviceProvider,
            out IVsUIHierarchy hierarchy,
            out uint itemId,
            out IVsWindowFrame windowFrame);
    }

}
