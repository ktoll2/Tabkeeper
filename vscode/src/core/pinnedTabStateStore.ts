import * as crypto from "crypto";
import * as fs from "fs";
import * as path from "path";
import { normalizeRelativePaths } from "./relativePathPolicy";
import { retryOnTransientError, withFileLock } from "./fileLock";

interface StateDocument {
    repositories: Record<string, RepositoryState>;
}

interface RepositoryState {
    branches: Record<string, BranchState>;
}

interface BranchState {
    pinnedPaths: string[];
}

/** Persists branch-specific, repository-relative pinned document paths outside the repository. */
export class PinnedTabStateStore {
    private readonly lockPath: string;

    constructor(private readonly stateFilePath: string) {
        this.lockPath = stateFilePath + ".lock";
    }

    public get filePath(): string {
        return this.stateFilePath;
    }

    /** Determines whether a saved pin set already exists for a repository and branch or shared-set key. */
    public async hasState(repositoryRoot: string, branchId: string): Promise<boolean> {
        const state = await this.load();
        const repository = state.repositories[normalizeRepositoryPath(repositoryRoot)];
        return repository !== undefined && Object.prototype.hasOwnProperty.call(repository.branches, branchId);
    }

    /** Returns a snapshot of saved relative paths for a repository and branch or shared-set key. */
    public async getPinnedPaths(repositoryRoot: string, branchId: string): Promise<string[]> {
        const state = await this.load();
        const repository = state.repositories[normalizeRepositoryPath(repositoryRoot)];
        return repository?.branches[branchId]?.pinnedPaths.slice() ?? [];
    }

    /** Saves one complete branch or shared-set pin list after validating relative paths. */
    public async savePinnedPaths(repositoryRoot: string, branchId: string, relativePaths: Iterable<string>): Promise<void> {
        const safePaths = normalizeRelativePaths(relativePaths);
        await this.mutate((state) => {
            const key = normalizeRepositoryPath(repositoryRoot);
            const repository = (state.repositories[key] ??= { branches: {} });
            repository.branches[branchId] = { pinnedPaths: safePaths };
        });
    }

    /** Removes the saved pin list for one branch or shared-set key. */
    public async clearPinnedPaths(repositoryRoot: string, branchId: string): Promise<void> {
        await this.mutate((state) => {
            const repository = state.repositories[normalizeRepositoryPath(repositoryRoot)];
            if (repository) {
                delete repository.branches[branchId];
            }
        });
    }

    /** Creates an empty state file when no saved pin state has been recorded yet. */
    public async ensureFileExists(): Promise<void> {
        if (!fs.existsSync(this.stateFilePath)) {
            await this.save({ repositories: {} });
        }
    }

    /** Removes ordinary branch states that no longer have a local Git branch and returns the count. */
    public async removeMissingBranchStates(repositoryRoot: string, existingBranches: ReadonlySet<string>): Promise<number> {
        return this.mutate((state) => {
            const repository = state.repositories[normalizeRepositoryPath(repositoryRoot)];
            if (!repository) {
                return 0;
            }

            const removableKeys = Object.keys(repository.branches).filter(
                (key) => !key.startsWith("set/") && !key.startsWith("detached/") && !existingBranches.has(key),
            );
            for (const key of removableKeys) {
                delete repository.branches[key];
            }

            return removableKeys.length;
        });
    }

    /**
     * Runs a read-modify-write cycle on the state document under a cross-process lock, then persists
     * the result. The state file is re-read inside the lock so concurrent Code windows on the same
     * repository cannot overwrite one another's saved sets.
     */
    private async mutate<T>(mutator: (state: StateDocument) => T): Promise<T> {
        return withFileLock(this.lockPath, async () => {
            const state = await this.load();
            const result = mutator(state);
            await this.save(state);
            return result;
        });
    }

    private async load(): Promise<StateDocument> {
        if (!fs.existsSync(this.stateFilePath)) {
            return { repositories: {} };
        }

        try {
            return await retryOnTransientError(async () => {
                const contents = await fs.promises.readFile(this.stateFilePath, "utf8");
                const parsed = JSON.parse(contents) as Partial<StateDocument>;
                return { repositories: parsed.repositories ?? {} };
            });
        } catch {
            // A momentarily locked or corrupt file is treated as empty rather than blocking the editor.
            return { repositories: {} };
        }
    }

    private async save(state: StateDocument): Promise<void> {
        const directory = path.dirname(this.stateFilePath);
        await fs.promises.mkdir(directory, { recursive: true });

        const temporaryPath = `${this.stateFilePath}.${crypto.randomBytes(4).toString("hex")}.tmp`;
        try {
            await fs.promises.writeFile(temporaryPath, JSON.stringify(state, null, 2), "utf8");
            await retryOnTransientError(() => fs.promises.rename(temporaryPath, this.stateFilePath));
        } finally {
            try {
                await fs.promises.unlink(temporaryPath);
            } catch {
                // Already renamed away; nothing to clean up.
            }
        }
    }
}

/** Produces the stable repository partition key used in the state document. */
function normalizeRepositoryPath(repositoryRoot: string): string {
    const resolved = path.resolve(repositoryRoot);
    return process.platform === "win32" ? resolved.toLowerCase() : resolved;
}
