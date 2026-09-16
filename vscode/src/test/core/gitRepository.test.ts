import * as assert from "assert";
import * as fs from "fs";
import * as path from "path";
import { getLocalBranchNames, isDetachedHead, tryLocateGitRepository } from "../../core/gitRepository";
import { makeTempDir } from "../testUtil";

function writeHead(gitDir: string, contents: string): void {
    fs.mkdirSync(gitDir, { recursive: true });
    fs.writeFileSync(path.join(gitDir, "HEAD"), contents, "utf8");
}

suite("gitRepository", () => {
    let tempDir: { dir: string; cleanup: () => void };

    setup(() => {
        tempDir = makeTempDir("tabkeeper-git");
    });

    teardown(() => {
        tempDir.cleanup();
    });

    suite("tryLocateGitRepository", () => {
        test("finds a repository whose .git is a directory and reads its branch", () => {
            const repoRoot = path.join(tempDir.dir, "repo");
            writeHead(path.join(repoRoot, ".git"), "ref: refs/heads/main\n");

            const repository = tryLocateGitRepository(repoRoot);

            assert.strictEqual(repository?.rootPath, repoRoot);
            assert.strictEqual(repository?.branchId, "main");
            assert.strictEqual(isDetachedHead(repository!), false);
        });

        test("walks upward from a nested start path to find the repository root", () => {
            const repoRoot = path.join(tempDir.dir, "repo");
            writeHead(path.join(repoRoot, ".git"), "ref: refs/heads/develop\n");
            const nested = path.join(repoRoot, "src", "deep", "nested.ts");
            fs.mkdirSync(path.dirname(nested), { recursive: true });
            fs.writeFileSync(nested, "// placeholder", "utf8");

            const repository = tryLocateGitRepository(nested);

            assert.strictEqual(repository?.rootPath, repoRoot);
            assert.strictEqual(repository?.branchId, "develop");
        });

        test("reports a detached HEAD as detached/<commit>", () => {
            const repoRoot = path.join(tempDir.dir, "repo");
            const commit = "a".repeat(40);
            writeHead(path.join(repoRoot, ".git"), commit + "\n");

            const repository = tryLocateGitRepository(repoRoot);

            assert.strictEqual(repository?.branchId, `detached/${commit}`);
            assert.strictEqual(isDetachedHead(repository!), true);
        });

        test("resolves a linked worktree's gitdir pointer file", () => {
            const worktreeGitDir = path.join(tempDir.dir, "main-repo", ".git", "worktrees", "feature");
            writeHead(worktreeGitDir, "ref: refs/heads/feature\n");

            const worktreeRoot = path.join(tempDir.dir, "worktree");
            fs.mkdirSync(worktreeRoot, { recursive: true });
            fs.writeFileSync(path.join(worktreeRoot, ".git"), `gitdir: ${worktreeGitDir}\n`, "utf8");

            const repository = tryLocateGitRepository(worktreeRoot);

            assert.strictEqual(repository?.rootPath, worktreeRoot);
            assert.strictEqual(path.resolve(repository!.gitDirectoryPath), path.resolve(worktreeGitDir));
            assert.strictEqual(repository?.branchId, "feature");
        });

        test("returns undefined when no repository is found", () => {
            const orphanPath = path.join(tempDir.dir, "not-a-repo");
            fs.mkdirSync(orphanPath, { recursive: true });

            assert.strictEqual(tryLocateGitRepository(orphanPath), undefined);
        });

        test("returns undefined for an empty start path", () => {
            assert.strictEqual(tryLocateGitRepository(""), undefined);
        });

        test("keeps searching upward past a .git directory with no readable HEAD", () => {
            const outerRoot = path.join(tempDir.dir, "outer");
            writeHead(path.join(outerRoot, ".git"), "ref: refs/heads/outer-branch\n");

            const innerRepo = path.join(outerRoot, "inner");
            fs.mkdirSync(path.join(innerRepo, ".git"), { recursive: true });
            // Inner .git directory exists but has no HEAD file, so it cannot yield a branch id.

            const repository = tryLocateGitRepository(innerRepo);

            assert.strictEqual(repository?.rootPath, outerRoot);
            assert.strictEqual(repository?.branchId, "outer-branch");
        });
    });

    suite("getLocalBranchNames", () => {
        test("collects loose refs recursively and packed refs, excluding non-branch entries", () => {
            const gitDir = path.join(tempDir.dir, "repo", ".git");
            const refsHeads = path.join(gitDir, "refs", "heads");
            fs.mkdirSync(path.join(refsHeads, "feature"), { recursive: true });
            fs.writeFileSync(path.join(refsHeads, "main"), "0".repeat(40) + "\n", "utf8");
            fs.writeFileSync(path.join(refsHeads, "feature", "login"), "1".repeat(40) + "\n", "utf8");

            fs.writeFileSync(
                path.join(gitDir, "packed-refs"),
                ["# pack-refs with: peeled fully-peeled sorted", `${"2".repeat(40)} refs/heads/archived`, `${"3".repeat(40)} refs/tags/v1.0.0`].join(
                    "\n",
                ),
                "utf8",
            );

            const branchNames = getLocalBranchNames(gitDir);

            assert.deepStrictEqual([...branchNames].sort(), ["archived", "feature/login", "main"]);
        });

        test("resolves a linked worktree's shared common directory via commondir", () => {
            const mainGitDir = path.join(tempDir.dir, "main-repo", ".git");
            fs.mkdirSync(path.join(mainGitDir, "refs", "heads"), { recursive: true });
            fs.writeFileSync(path.join(mainGitDir, "refs", "heads", "main"), "0".repeat(40) + "\n", "utf8");

            const worktreeGitDir = path.join(mainGitDir, "worktrees", "feature");
            fs.mkdirSync(worktreeGitDir, { recursive: true });
            fs.writeFileSync(path.join(worktreeGitDir, "commondir"), "../..\n", "utf8");

            const branchNames = getLocalBranchNames(worktreeGitDir);

            assert.deepStrictEqual([...branchNames], ["main"]);
        });

        test("returns an empty set when no refs exist", () => {
            const gitDir = path.join(tempDir.dir, "repo", ".git");
            fs.mkdirSync(gitDir, { recursive: true });

            assert.deepStrictEqual(getLocalBranchNames(gitDir), new Set());
        });
    });
});
