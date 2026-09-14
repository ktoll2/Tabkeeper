import * as fs from "fs";
import * as path from "path";
import * as vscode from "vscode";
import { ActivityLog, ActivityLogContext, ActivityLogLevel } from "./core/activityLog";
import { findMatchingRule } from "./core/branchRule";
import { GitRepository, getLocalBranchNames, isDetachedHead, tryLocateGitRepository } from "./core/gitRepository";
import { PinnedTabStateStore } from "./core/pinnedTabStateStore";
import { exportPinSet, importPinSet } from "./core/pinSetTransfer";
import { getConfiguration, setEnabled, TabkeeperConfiguration } from "./configuration";
import {
    getActiveResourceUri,
    getOpenScopedTabs,
    isPathInRoot,
    makeRelativePath,
    openAndPin,
    pathsEqual,
    restoreActiveDocument,
    setTabPinned,
    unpinAllInScope,
} from "./documentPins";

const RESTORE_RETRY_LIMIT = 3;
const RESTORE_RETRY_DELAY_MILLISECONDS = 1500;

/** Synchronizes open document pin states with the Git branch selected for one repository. */
export class TabkeeperSynchronizer implements vscode.Disposable {
    private currentRepository: GitRepository | undefined;
    private headWatcher: fs.FSWatcher | undefined;
    private watchedGitDirectory: string | undefined;
    private pollingTimer: ReturnType<typeof setInterval> | undefined;
    private gate: Promise<void> = Promise.resolve();
    private disposed = false;
    private noRepositoryWarningLogged = false;
    private lastSwallowedError: unknown;

    constructor(
        private readonly repositoryStartPath: string,
        private readonly stateStore: PinnedTabStateStore,
        private readonly activityLog: ActivityLog,
    ) {}

    public start(): void {
        void this.log(ActivityLogLevel.Info, this.tryGetRepository(), "system", "Tabkeeper session started.");
        this.queueSynchronization();
        this.updatePollingInterval();
    }

    /** Applies the current configuration's polling interval and synchronizes when enabled. */
    public updateSettings(): void {
        this.updatePollingInterval();
        if (this.getConfig().enabled) {
            this.synchronizeNow();
        }
    }

    /** Queues a manual save-and-restore cycle for the active branch. */
    public synchronizeNow(): void {
        this.queueSynchronization(true);
    }

    public isEnabled(): boolean {
        return this.getConfig().enabled;
    }

    /** Toggles automatic synchronization and persists the new state. Returns the new enabled state. */
    public async toggleEnabled(): Promise<boolean> {
        const enabled = !this.getConfig().enabled;
        await setEnabled(enabled);
        this.updateSettings();
        return enabled;
    }

    public dispose(): void {
        if (this.disposed) {
            return;
        }

        this.disposed = true;
        if (this.pollingTimer) {
            clearInterval(this.pollingTimer);
        }

        this.headWatcher?.close();
        this.watchedGitDirectory = undefined;
    }

    /** Clears the active branch's saved state and unpins documents in scope. */
    public clearCurrentTabSet(): void {
        this.enqueue(async () => {
            const repository = this.tryGetRepository();
            if (!repository) {
                return;
            }

            await this.stateStore.clearPinnedPaths(repository.rootPath, this.getStateKey(repository.branchId));
            await unpinAllInScope(this.getScopeRoot(repository));
        });
    }

    /** Writes the active branch's saved pins to a portable JSON file. */
    public async exportCurrentPinSet(filePath: string): Promise<void> {
        await this.runExclusive(async () => {
            const repository = this.tryGetRepository();
            if (!repository) {
                return;
            }

            await this.saveCurrentPins(repository);
            const pinnedPaths = await this.stateStore.getPinnedPaths(repository.rootPath, this.getStateKey(repository.branchId));
            await exportPinSet(filePath, repository.branchId, pinnedPaths);
            await this.log(ActivityLogLevel.Info, repository, repository.branchId, `Exported ${pinnedPaths.length} pinned tabs to ${filePath}.`);
        });
    }

    /** Replaces the active branch's saved pins with a validated portable JSON file. */
    public async importIntoCurrentPinSet(filePath: string): Promise<void> {
        await this.runExclusive(async () => {
            const repository = this.tryGetRepository();
            if (!repository || !this.isManaged(repository)) {
                return;
            }

            const transfer = await importPinSet(filePath);
            await this.stateStore.savePinnedPaths(repository.rootPath, this.getStateKey(repository.branchId), transfer.pinnedPaths);
            await this.log(
                ActivityLogLevel.Info,
                repository,
                repository.branchId,
                `Imported ${transfer.pinnedPaths.length} pinned tabs from ${filePath}.`,
            );
        });
        this.synchronizeNow();
    }

    /** Copies the active branch's pin set to another branch or matching shared-set key. */
    public async copyCurrentPinsToBranch(destinationBranch: string): Promise<void> {
        await this.runExclusive(async () => {
            const repository = this.tryGetRepository();
            if (!repository || !destinationBranch.trim()) {
                return;
            }

            await this.saveCurrentPins(repository);
            const currentPins = await this.stateStore.getPinnedPaths(repository.rootPath, this.getStateKey(repository.branchId));
            await this.stateStore.savePinnedPaths(repository.rootPath, this.getStateKey(destinationBranch), currentPins);
            await this.log(ActivityLogLevel.Info, repository, repository.branchId, `Copied ${currentPins.length} pinned tabs to ${destinationBranch}.`);
        });
    }

    /** Deletes saved states for local branches that no longer exist. Returns the number removed. */
    public async cleanUpMissingBranches(): Promise<number> {
        return this.runExclusive(async () => {
            const repository = this.tryGetRepository();
            if (!repository) {
                return 0;
            }

            const removedCount = await this.stateStore.removeMissingBranchStates(
                repository.rootPath,
                getLocalBranchNames(repository.gitDirectoryPath),
            );
            await this.log(ActivityLogLevel.Info, repository, repository.branchId, `Removed ${removedCount} obsolete branch pin sets.`);
            return removedCount;
        });
    }

    private queueSynchronization(forceApply = false): void {
        this.enqueue(() => (this.disposed ? Promise.resolve() : this.synchronizeAsync(forceApply)));
    }

    /** Serializes an asynchronous operation behind the same gate as branch observation. */
    private enqueue(operation: () => Promise<void>): void {
        this.gate = this.gate
            .then(operation)
            .catch((error) => {
                // Git may rewrite HEAD mid-checkout, or a transient file error can occur; the watcher and
                // polling fallback retry, and the swallowed error is logged at Debug on the next pass.
                this.lastSwallowedError = error;
            });
    }

    /** Runs an operation behind the gate and returns its result to the caller. */
    private runExclusive<T>(operation: () => Promise<T>): Promise<T> {
        const resultPromise = this.gate.then(operation);
        this.gate = resultPromise.then(
            () => undefined,
            (error) => {
                this.lastSwallowedError = error;
            },
        );
        return resultPromise;
    }

    private async synchronizeAsync(forceApply: boolean): Promise<void> {
        await this.logPendingDiagnostics();

        const observedRepository = this.tryGetRepository();
        if (!observedRepository) {
            if (!this.noRepositoryWarningLogged) {
                await this.log(ActivityLogLevel.Warn, undefined, "system", "No Git repository found for the active workspace.");
                this.noRepositoryWarningLogged = true;
            }

            return;
        }

        this.noRepositoryWarningLogged = false;
        if (!this.getConfig().enabled) {
            return;
        }

        const repositoryChanged =
            !this.currentRepository || !pathsEqual(this.currentRepository.rootPath, observedRepository.rootPath);
        const branchChanged =
            !repositoryChanged && this.currentRepository!.branchId !== observedRepository.branchId;
        if (!repositoryChanged && !branchChanged && !forceApply) {
            return;
        }

        const firstObservation = !this.currentRepository;
        if (branchChanged) {
            await this.log(
                ActivityLogLevel.Info,
                observedRepository,
                `${this.currentRepository!.branchId} -> ${observedRepository.branchId}`,
                "Branch switch detected.",
            );
        }

        if (this.currentRepository) {
            await this.saveCurrentPins(this.currentRepository);
        }

        this.currentRepository = observedRepository;
        this.watchHeadFile(observedRepository);
        if (firstObservation) {
            await this.adoptExistingPinsIfUnseen(observedRepository);
        }

        await this.delayRestore();
        const fullyRestored = await this.applyTabkeeper(observedRepository);
        if (!fullyRestored && !forceApply) {
            this.scheduleRestoreRetry(observedRepository.branchId, RESTORE_RETRY_LIMIT);
        }
    }

    private scheduleRestoreRetry(branchId: string, attemptsRemaining: number): void {
        if (this.disposed || attemptsRemaining <= 0) {
            return;
        }

        setTimeout(() => {
            this.enqueue(async () => {
                if (this.disposed) {
                    return;
                }

                const observedRepository = this.tryGetRepository();
                if (
                    !observedRepository ||
                    !this.currentRepository ||
                    !pathsEqual(this.currentRepository.rootPath, observedRepository.rootPath) ||
                    observedRepository.branchId !== branchId ||
                    !this.getConfig().enabled
                ) {
                    // The workspace, repository, or branch moved on; the newer observation owns the state now.
                    return;
                }

                const fullyRestored = await this.applyTabkeeper(observedRepository, true);
                if (!fullyRestored) {
                    this.scheduleRestoreRetry(branchId, attemptsRemaining - 1);
                }
            });
        }, RESTORE_RETRY_DELAY_MILLISECONDS);
    }

    /** Captures open pinned document paths and persists them under the branch's effective state key. */
    private async saveCurrentPins(repository: GitRepository, savedMessage = "Saved pinned tabs."): Promise<void> {
        if (!this.isManaged(repository) || (isDetachedHead(repository) && this.getConfig().clearsDetachedHeadPins)) {
            return;
        }

        const scopeRoot = this.getScopeRoot(repository);
        const pinnedPaths = getOpenScopedTabs(scopeRoot)
            .filter(({ tab }) => tab.isPinned)
            .map(({ fsPath }) => makeRelativePath(repository.rootPath, fsPath));
        await this.stateStore.savePinnedPaths(repository.rootPath, this.getStateKey(repository.branchId), pinnedPaths);
        await this.log(ActivityLogLevel.Info, repository, repository.branchId, savedMessage);
    }

    /**
     * On the first observation of a branch that has no saved set, records the tabs already pinned as
     * that branch's initial set instead of unpinning them.
     */
    private async adoptExistingPinsIfUnseen(repository: GitRepository): Promise<void> {
        const hasState = await this.stateStore.hasState(repository.rootPath, this.getStateKey(repository.branchId));
        if (hasState) {
            return;
        }

        await this.saveCurrentPins(repository, "Adopted the currently pinned tabs as this branch's initial set.");
    }

    /**
     * Unpins current scoped tabs, then opens and pins the valid paths saved for the destination branch.
     * Returns true when every saved tab within the restore limit was pinned.
     */
    private async applyTabkeeper(repository: GitRepository, isRetry = false): Promise<boolean> {
        if (!this.isManaged(repository)) {
            if (!isRetry) {
                await this.log(ActivityLogLevel.Info, repository, repository.branchId, "Skipped synchronization because its matching rule is disabled.");
            }

            return true;
        }

        const configuration = this.getConfig();
        const activeUri = configuration.preserveActiveDocument ? getActiveResourceUri() : undefined;
        const scopeRoot = this.getScopeRoot(repository);

        const openInScope = getOpenScopedTabs(scopeRoot);
        for (const { tab, uri } of openInScope) {
            if (tab.isPinned) {
                await setTabPinned(tab, uri, false);
            }
        }

        if (isDetachedHead(repository) && configuration.clearsDetachedHeadPins) {
            return true;
        }

        const savedPaths = await this.stateStore.getPinnedPaths(repository.rootPath, this.getStateKey(repository.branchId));
        const restorablePaths: Array<{ relative: string; full: string }> = [];
        let anyMissing = false;
        for (const relativePath of savedPaths) {
            const fullPath = path.resolve(path.join(repository.rootPath, relativePath));
            if (!isPathInRoot(fullPath, scopeRoot) || !fs.existsSync(fullPath)) {
                anyMissing = true;
                if (!isRetry) {
                    await this.reportMissingFile(relativePath);
                }

                continue;
            }

            restorablePaths.push({ relative: relativePath, full: fullPath });
        }

        const maximumRestoredTabs = configuration.maximumRestoredTabs;
        if (restorablePaths.length > maximumRestoredTabs && !isRetry) {
            await this.log(
                ActivityLogLevel.Warn,
                repository,
                repository.branchId,
                `Restore limit (${maximumRestoredTabs}): skipped ${restorablePaths.length - maximumRestoredTabs} pinned tabs.`,
            );
        }

        let restoredCount = 0;
        let anyOpenFailed = false;
        for (const { relative, full } of restorablePaths.slice(0, maximumRestoredTabs)) {
            const existingTab = openInScope.find((entry) => pathsEqual(entry.fsPath, full));
            let opened: boolean;
            try {
                opened = existingTab ? await setTabPinned(existingTab.tab, existingTab.uri, true).then(() => true) : await openAndPin(full);
            } catch {
                opened = false;
            }

            if (opened) {
                restoredCount++;
            } else {
                anyOpenFailed = true;
                if (!isRetry) {
                    await this.log(ActivityLogLevel.Warn, repository, repository.branchId, `Could not open pinned tab: ${relative}`);
                }
            }
        }

        await restoreActiveDocument(activeUri);
        await this.log(ActivityLogLevel.Info, repository, repository.branchId, `Restored ${restoredCount} pinned tabs.`);

        // Files reported permanently missing do not warrant a retry; a failed open of an existing file
        // might, since a slow checkout can leave a file briefly locked or unreadable.
        return !anyOpenFailed && (isRetry || !anyMissing);
    }

    /** Resolves the configured repository-wide or workspace-folder document scope. */
    private getScopeRoot(repository: GitRepository): string {
        if (!this.getConfig().usesWorkspaceFolderScope) {
            return repository.rootPath;
        }

        return isPathInRoot(this.repositoryStartPath, repository.rootPath) ? this.repositoryStartPath : repository.rootPath;
    }

    /** Determines whether the branch's most-specific matching rule permits synchronization. */
    private isManaged(repository: GitRepository): boolean {
        const rule = findMatchingRule(this.getConfig().rules, repository.branchId);
        return !rule || rule.enabled;
    }

    /** Resolves a branch name to its own state key or to an enabled rule's shared-set key. */
    private getStateKey(branchName: string): string {
        const rule = findMatchingRule(this.getConfig().rules, branchName);
        return rule && rule.enabled ? "set/" + rule.pinSetName : branchName;
    }

    private async reportMissingFile(relativePath: string): Promise<void> {
        await this.log(
            ActivityLogLevel.Warn,
            this.currentRepository,
            this.currentRepository?.branchId ?? "system",
            `Missing: ${relativePath}`,
        );
        if (this.getConfig().showsMissingFileMessage) {
            void vscode.window.showWarningMessage(`Tabkeeper: the saved pinned file '${relativePath}' does not exist on this branch.`);
        }
    }

    private tryGetRepository(): GitRepository | undefined {
        return tryLocateGitRepository(this.repositoryStartPath);
    }

    private watchHeadFile(repository: GitRepository): void {
        if (this.watchedGitDirectory && pathsEqual(this.watchedGitDirectory, repository.gitDirectoryPath)) {
            return;
        }

        this.headWatcher?.close();
        this.watchedGitDirectory = repository.gitDirectoryPath;
        try {
            this.headWatcher = fs.watch(repository.gitDirectoryPath, (_eventType, filename) => {
                if (filename === "HEAD" || filename === null) {
                    this.queueSynchronization();
                }
            });
        } catch {
            // Some filesystems do not support watching; the polling timer still covers branch changes.
            this.headWatcher = undefined;
        }
    }

    private async log(level: ActivityLogLevel, repository: GitRepository | undefined, branch: string, message: string): Promise<void> {
        const context: ActivityLogContext = {
            repository: repository ? path.basename(repository.rootPath) : "-",
            solution: path.basename(this.repositoryStartPath),
            branch,
        };
        await this.activityLog.write(level, context, message);
    }

    private updatePollingInterval(): void {
        if (this.pollingTimer) {
            clearInterval(this.pollingTimer);
        }

        const intervalMs = this.getConfig().pollingIntervalSeconds * 1000;
        this.pollingTimer = setInterval(() => this.queueSynchronization(), intervalMs);
    }

    private async delayRestore(): Promise<void> {
        const delayMs = this.getConfig().restoreDelayMilliseconds;
        if (delayMs > 0) {
            await new Promise((resolve) => setTimeout(resolve, delayMs));
        }
    }

    private async logPendingDiagnostics(): Promise<void> {
        if (this.lastSwallowedError !== undefined) {
            const error = this.lastSwallowedError;
            this.lastSwallowedError = undefined;
            const description = error instanceof Error ? `${error.name}: ${error.message}` : String(error);
            await this.log(
                ActivityLogLevel.Debug,
                this.tryGetRepository(),
                "system",
                `Recovered from a transient synchronization error: ${description}`,
            );
        }
    }

    private getConfig(): TabkeeperConfiguration {
        return getConfiguration(vscode.Uri.file(this.repositoryStartPath));
    }
}
