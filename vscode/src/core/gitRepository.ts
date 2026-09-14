import * as fs from "fs";
import * as path from "path";

/** Identifies the Git repository and checked-out branch for a workspace folder. */
export interface GitRepository {
    readonly rootPath: string;
    readonly gitDirectoryPath: string;
    /** A local branch name, or `detached/<commit>` while HEAD is detached. */
    readonly branchId: string;
}

const DETACHED_PREFIX = "detached/";
const LOCAL_BRANCH_HEAD_PREFIX = "ref: refs/heads/";
const LOCAL_BRANCH_REFS_PREFIX = "refs/heads/";

/** Determines whether a repository identity represents a detached commit rather than a local branch. */
export function isDetachedHead(repository: GitRepository): boolean {
    return repository.branchId.startsWith(DETACHED_PREFIX);
}

/**
 * Finds the nearest Git repository above a path and reads its current branch, without invoking the
 * Git executable.
 */
export function tryLocateGitRepository(startPath: string): GitRepository | undefined {
    if (!startPath) {
        return undefined;
    }

    let directory: string | undefined = fs.existsSync(startPath)
        ? path.resolve(startPath)
        : path.dirname(path.resolve(startPath));

    while (directory) {
        const markerPath = path.join(directory, ".git");
        const gitDirectoryPath = tryResolveGitDirectory(markerPath, directory);
        if (gitDirectoryPath) {
            const branchId = tryReadBranchId(gitDirectoryPath);
            if (branchId) {
                return { rootPath: directory, gitDirectoryPath, branchId };
            }
        }

        const parent = path.dirname(directory);
        directory = parent === directory ? undefined : parent;
    }

    return undefined;
}

/** Resolves a repository's `.git` directory marker or linked-worktree `gitdir:` pointer. */
function tryResolveGitDirectory(markerPath: string, repositoryRoot: string): string | undefined {
    let stat: fs.Stats;
    try {
        stat = fs.statSync(markerPath);
    } catch {
        return undefined;
    }

    if (stat.isDirectory()) {
        return markerPath;
    }

    if (!stat.isFile()) {
        return undefined;
    }

    let markerContents: string;
    try {
        markerContents = fs.readFileSync(markerPath, "utf8").trim();
    } catch {
        return undefined;
    }

    const gitDirPrefix = "gitdir: ";
    if (!markerContents.toLowerCase().startsWith(gitDirPrefix.toLowerCase())) {
        return undefined;
    }

    const configuredPath = markerContents.substring(gitDirPrefix.length).trim();
    if (configuredPath.length === 0) {
        return undefined;
    }

    const gitDirectoryPath = path.isAbsolute(configuredPath)
        ? configuredPath
        : path.join(repositoryRoot, configuredPath);
    return fs.existsSync(gitDirectoryPath) ? path.resolve(gitDirectoryPath) : undefined;
}

/** Reads the current local branch name or detached commit identifier from Git's HEAD file. */
function tryReadBranchId(gitDirectoryPath: string): string | undefined {
    const headPath = path.join(gitDirectoryPath, "HEAD");
    let headContents: string;
    try {
        headContents = fs.readFileSync(headPath, "utf8").trim();
    } catch {
        return undefined;
    }

    if (headContents.startsWith(LOCAL_BRANCH_HEAD_PREFIX)) {
        const branchName = headContents.substring(LOCAL_BRANCH_HEAD_PREFIX.length);
        return branchName.length === 0 ? undefined : branchName;
    }

    return isCommitId(headContents) ? DETACHED_PREFIX + headContents : undefined;
}

function isCommitId(value: string): boolean {
    return value.length >= 7 && value.length <= 64 && /^[0-9a-fA-F]+$/.test(value);
}

/** Returns local branch names from loose refs and packed refs, including linked-worktree metadata. */
export function getLocalBranchNames(gitDirectoryPath: string): Set<string> {
    const branchNames = new Set<string>();
    const commonDirectory = getCommonGitDirectory(gitDirectoryPath);
    const refsDirectory = path.join(commonDirectory, "refs", "heads");
    if (fs.existsSync(refsDirectory)) {
        addLooseRefs(refsDirectory, refsDirectory, branchNames);
    }

    const packedRefsPath = path.join(commonDirectory, "packed-refs");
    if (fs.existsSync(packedRefsPath)) {
        const lines = fs.readFileSync(packedRefsPath, "utf8").split(/\r?\n/);
        for (const line of lines) {
            const separatorIndex = line.indexOf(" ");
            if (separatorIndex > 0 && line.substring(separatorIndex + 1).startsWith(LOCAL_BRANCH_REFS_PREFIX)) {
                branchNames.add(line.substring(separatorIndex + 1 + LOCAL_BRANCH_REFS_PREFIX.length));
            }
        }
    }

    return branchNames;
}

/** Resolves the shared Git directory used by linked worktrees, when configured. */
function getCommonGitDirectory(gitDirectoryPath: string): string {
    const commonDirectoryFile = path.join(gitDirectoryPath, "commondir");
    if (!fs.existsSync(commonDirectoryFile)) {
        return gitDirectoryPath;
    }

    const configuredPath = fs.readFileSync(commonDirectoryFile, "utf8").trim();
    return path.resolve(path.join(gitDirectoryPath, configuredPath));
}

function addLooseRefs(rootDirectory: string, directory: string, branchNames: Set<string>): void {
    for (const entry of fs.readdirSync(directory, { withFileTypes: true })) {
        const entryPath = path.join(directory, entry.name);
        if (entry.isDirectory()) {
            addLooseRefs(rootDirectory, entryPath, branchNames);
        } else if (entry.isFile()) {
            const relative = path.relative(rootDirectory, entryPath).split(path.sep).join("/");
            branchNames.add(relative);
        }
    }
}
