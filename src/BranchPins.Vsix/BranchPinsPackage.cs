using System;
using System.ComponentModel.Design;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using BranchPins.Core;
using EnvDTE;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.Win32;

namespace BranchPins.Vsix;

[PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
[ProvideAutoLoad(VSConstants.UICONTEXT.SolutionExists_string, PackageAutoLoadFlags.BackgroundLoad)]
[ProvideMenuResource("Menus.ctmenu", 1)]
[Guid(PackageGuid)]
public sealed class BranchPinsPackage : AsyncPackage
{
    /// <summary>
    /// Identifies the Visual Studio package registration for Branch Pins.
    /// </summary>
    public const string PackageGuid = "75ff4b05-e5bc-4143-a7ca-86e17d6aa50b";

    private BranchPinSynchronizer? synchronizer;
    private IVsUIShellOpenDocument? openDocument;
    private PinnedTabStateStore? stateStore;
    private BranchPinsConfigurationStore? configurationStore;
    private BranchPinsActivityLog? activityLog;

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

        string stateFilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "BranchPins",
            "pinned-tabs.json");
        stateStore = new PinnedTabStateStore(stateFilePath);
        configurationStore = new BranchPinsConfigurationStore(Path.Combine(Path.GetDirectoryName(stateFilePath)!, "config.json"));
        configurationStore.Load();
        string sessionId = Guid.NewGuid().ToString("N").Substring(0, 8);
        string instanceIdentifier = $"VS:{System.Diagnostics.Process.GetCurrentProcess().Id}/{sessionId}";
        activityLog = new BranchPinsActivityLog(Path.Combine(Path.GetDirectoryName(stateFilePath)!, "activity.log"), instanceIdentifier);
        synchronizer = new BranchPinSynchronizer(JoinableTaskFactory, solution, uiShell, openDocument, stateStore, configurationStore, activityLog, dte);
        synchronizer.Start();
        AddMenuCommands();
    }

    protected override void Dispose(bool disposing)
    {
        if (!disposing)
        {
            base.Dispose(disposing);
            return;
        }

        ThreadHelper.ThrowIfNotOnUIThread();
        synchronizer?.Dispose();
        activityLog?.Dispose();
        base.Dispose(disposing);
    }

    private void AddMenuCommands()
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        OleMenuCommandService commandService = GetService(typeof(IMenuCommandService)) as OleMenuCommandService
            ?? throw new InvalidOperationException("The Visual Studio menu command service is unavailable.");

        OleMenuCommand enableCommand = new OleMenuCommand(OnEnableBranchPins, new CommandID(BranchPinsCommands.CommandSet, BranchPinsCommands.Enable));
        enableCommand.BeforeQueryStatus += OnEnableBranchPinsStatus;
        commandService.AddCommand(enableCommand);
        commandService.AddCommand(new MenuCommand(OnSynchronizeNow, new CommandID(BranchPinsCommands.CommandSet, BranchPinsCommands.SynchronizeNow)));
        commandService.AddCommand(new MenuCommand(OnClearCurrentBranchPins, new CommandID(BranchPinsCommands.CommandSet, BranchPinsCommands.ClearCurrentBranchPins)));
        commandService.AddCommand(new MenuCommand(OnOpenSavedPinState, new CommandID(BranchPinsCommands.CommandSet, BranchPinsCommands.OpenSavedPinState)));
        commandService.AddCommand(new MenuCommand(OnManageBranchRules, new CommandID(BranchPinsCommands.CommandSet, BranchPinsCommands.ManageBranchRules)));
        commandService.AddCommand(new MenuCommand(OnExportCurrentPinSet, new CommandID(BranchPinsCommands.CommandSet, BranchPinsCommands.ExportCurrentPinSet)));
        commandService.AddCommand(new MenuCommand(OnImportIntoCurrentPinSet, new CommandID(BranchPinsCommands.CommandSet, BranchPinsCommands.ImportIntoCurrentPinSet)));
        commandService.AddCommand(new MenuCommand(OnCopyCurrentPinsToBranch, new CommandID(BranchPinsCommands.CommandSet, BranchPinsCommands.CopyCurrentPinsToBranch)));
        commandService.AddCommand(new MenuCommand(OnCleanUpMissingBranches, new CommandID(BranchPinsCommands.CommandSet, BranchPinsCommands.CleanUpMissingBranches)));
        commandService.AddCommand(new MenuCommand(OnOpenActivityLog, new CommandID(BranchPinsCommands.CommandSet, BranchPinsCommands.OpenActivityLog)));
    }

    private void OnEnableBranchPins(object sender, EventArgs e)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        synchronizer?.ToggleEnabled();
    }

    private void OnEnableBranchPinsStatus(object sender, EventArgs e)
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

    private void OnClearCurrentBranchPins(object sender, EventArgs e)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        synchronizer?.ClearCurrentBranchPins();
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
        if (configurationStore is not null)
        {
            OpenFile(configurationStore.FilePath);
        }
    }

    private void OnExportCurrentPinSet(object sender, EventArgs e)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        SaveFileDialog dialog = new SaveFileDialog { Filter = "Branch Pins export (*.branchpins.json)|*.branchpins.json", AddExtension = true };
        if (dialog.ShowDialog() == true)
        {
            synchronizer?.ExportCurrentPinSet(dialog.FileName);
        }
    }

    private void OnImportIntoCurrentPinSet(object sender, EventArgs e)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        OpenFileDialog dialog = new OpenFileDialog { Filter = "Branch Pins export (*.branchpins.json)|*.branchpins.json" };
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
