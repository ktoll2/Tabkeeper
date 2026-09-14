import * as vscode from "vscode";
import { ActivityLog } from "./core/activityLog";
import { tryLocateGitRepository } from "./core/gitRepository";
import { PinnedTabStateStore } from "./core/pinnedTabStateStore";
import { getConfiguration, setEnabled } from "./configuration";
import { getTabkeeperPaths } from "./paths";
import { TabkeeperSynchronizer } from "./synchronizer";

interface ManagedRepository {
    readonly folderPath: string;
    readonly repoRoot: string;
    readonly synchronizer: TabkeeperSynchronizer;
}

export function activate(context: vscode.ExtensionContext): void {
    const paths = getTabkeeperPaths(context);
    const stateStore = new PinnedTabStateStore(paths.stateFile);
    const activityLog = new ActivityLog(paths.activityLogFile);
    const statusBarItem = vscode.window.createStatusBarItem(vscode.StatusBarAlignment.Left, 0);
    statusBarItem.command = "tabkeeper.toggleEnabled";

    let managedRepositories: ManagedRepository[] = [];

    function rebuildSynchronizers(): void {
        for (const managed of managedRepositories) {
            managed.synchronizer.dispose();
        }

        managedRepositories = [];
        const seenRoots = new Set<string>();
        for (const folder of vscode.workspace.workspaceFolders ?? []) {
            const folderPath = folder.uri.fsPath;
            const repository = tryLocateGitRepository(folderPath);
            const rootKey = (repository?.rootPath ?? folderPath).toLowerCase();
            if (seenRoots.has(rootKey)) {
                // Another workspace folder already resolved to this same repository; one synchronizer suffices.
                continue;
            }

            seenRoots.add(rootKey);
            const synchronizer = new TabkeeperSynchronizer(folderPath, stateStore, activityLog);
            managedRepositories.push({ folderPath, repoRoot: repository?.rootPath ?? folderPath, synchronizer });
            synchronizer.start();
        }

        updateStatusBar();
    }

    function getCurrentSynchronizer(): TabkeeperSynchronizer | undefined {
        const activeUri = vscode.window.activeTextEditor?.document.uri;
        if (activeUri) {
            const folder = vscode.workspace.getWorkspaceFolder(activeUri);
            if (folder) {
                const match = managedRepositories.find((managed) => managed.folderPath === folder.uri.fsPath);
                if (match) {
                    return match.synchronizer;
                }
            }
        }

        return managedRepositories[0]?.synchronizer;
    }

    function updateStatusBar(): void {
        const enabled = getConfiguration().enabled;
        statusBarItem.text = enabled ? "$(pinned) Tabkeeper" : "$(pin) Tabkeeper (off)";
        statusBarItem.tooltip = enabled
            ? "Tabkeeper is synchronizing pinned tabs per branch. Click to disable."
            : "Tabkeeper is disabled. Click to enable.";
        if (managedRepositories.length > 0) {
            statusBarItem.show();
        } else {
            statusBarItem.hide();
        }
    }

    context.subscriptions.push(
        statusBarItem,
        vscode.workspace.onDidChangeWorkspaceFolders(rebuildSynchronizers),
        vscode.workspace.onDidChangeConfiguration((event) => {
            if (event.affectsConfiguration("tabkeeper")) {
                for (const managed of managedRepositories) {
                    managed.synchronizer.updateSettings();
                }

                updateStatusBar();
            }
        }),
        vscode.commands.registerCommand("tabkeeper.toggleEnabled", async () => {
            const enabled = !getConfiguration().enabled;
            await setEnabled(enabled);
            for (const managed of managedRepositories) {
                managed.synchronizer.updateSettings();
            }

            updateStatusBar();
        }),
        vscode.commands.registerCommand("tabkeeper.syncNow", () => {
            for (const managed of managedRepositories) {
                managed.synchronizer.synchronizeNow();
            }
        }),
        vscode.commands.registerCommand("tabkeeper.clearCurrentTabSet", () => {
            getCurrentSynchronizer()?.clearCurrentTabSet();
        }),
        vscode.commands.registerCommand("tabkeeper.exportPinSet", async () => {
            const synchronizer = getCurrentSynchronizer();
            if (!synchronizer) {
                return;
            }

            const destination = await vscode.window.showSaveDialog({
                filters: { "Tabkeeper export": ["tabkeeper.json"] },
                saveLabel: "Export",
            });
            if (destination) {
                await synchronizer.exportCurrentPinSet(destination.fsPath);
                void vscode.window.showInformationMessage(`Tabkeeper: exported pinned tabs to ${destination.fsPath}.`);
            }
        }),
        vscode.commands.registerCommand("tabkeeper.importPinSet", async () => {
            const synchronizer = getCurrentSynchronizer();
            if (!synchronizer) {
                return;
            }

            const [source] =
                (await vscode.window.showOpenDialog({
                    filters: { "Tabkeeper export": ["tabkeeper.json"] },
                    canSelectMany: false,
                    openLabel: "Import",
                })) ?? [];
            if (source) {
                await synchronizer.importIntoCurrentPinSet(source.fsPath);
                void vscode.window.showInformationMessage("Tabkeeper: imported pinned tabs for this branch.");
            }
        }),
        vscode.commands.registerCommand("tabkeeper.copyPinsToBranch", async () => {
            const synchronizer = getCurrentSynchronizer();
            if (!synchronizer) {
                return;
            }

            const destinationBranch = await vscode.window.showInputBox({
                title: "Copy Tab Set to Branch",
                prompt: "Destination local branch name",
            });
            if (destinationBranch?.trim()) {
                await synchronizer.copyCurrentPinsToBranch(destinationBranch.trim());
            }
        }),
        vscode.commands.registerCommand("tabkeeper.cleanUpMissingBranches", async () => {
            const synchronizer = getCurrentSynchronizer();
            if (!synchronizer) {
                return;
            }

            const removedCount = await synchronizer.cleanUpMissingBranches();
            void vscode.window.showInformationMessage(`Tabkeeper: removed ${removedCount} obsolete branch pin sets.`);
        }),
        vscode.commands.registerCommand("tabkeeper.openSavedState", async () => {
            await stateStore.ensureFileExists();
            await vscode.window.showTextDocument(vscode.Uri.file(stateStore.filePath));
        }),
        vscode.commands.registerCommand("tabkeeper.openActivityLog", async () => {
            await activityLog.ensureFileExists();
            await vscode.window.showTextDocument(vscode.Uri.file(activityLog.filePath));
        }),
        vscode.commands.registerCommand("tabkeeper.manageBranchRules", () => {
            void vscode.commands.executeCommand("workbench.action.openSettings", "tabkeeper.rules");
        }),
        new vscode.Disposable(() => {
            for (const managed of managedRepositories) {
                managed.synchronizer.dispose();
            }
        }),
    );

    rebuildSynchronizers();
}

export function deactivate(): void {}
