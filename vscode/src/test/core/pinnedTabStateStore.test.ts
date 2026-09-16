import * as assert from "assert";
import * as path from "path";
import { PinnedTabStateStore } from "../../core/pinnedTabStateStore";
import { makeTempDir } from "../testUtil";

suite("PinnedTabStateStore", () => {
    let tempDir: { dir: string; cleanup: () => void };
    let store: PinnedTabStateStore;
    const repoRoot = "C:\\repos\\Example";

    setup(() => {
        tempDir = makeTempDir("tabkeeper-state");
        store = new PinnedTabStateStore(path.join(tempDir.dir, "state", "pinned-tabs.json"));
    });

    teardown(() => {
        tempDir.cleanup();
    });

    test("hasState and getPinnedPaths report nothing for an unknown branch", async () => {
        assert.strictEqual(await store.hasState(repoRoot, "main"), false);
        assert.deepStrictEqual(await store.getPinnedPaths(repoRoot, "main"), []);
    });

    test("savePinnedPaths persists and validates paths, retrievable by getPinnedPaths", async () => {
        await store.savePinnedPaths(repoRoot, "main", ["b.ts", "a.ts", "../escape.ts"]);

        assert.strictEqual(await store.hasState(repoRoot, "main"), true);
        assert.deepStrictEqual(await store.getPinnedPaths(repoRoot, "main"), ["a.ts", "b.ts"]);
    });

    test("keeps separate branches and separate repositories independent", async () => {
        await store.savePinnedPaths(repoRoot, "main", ["main.ts"]);
        await store.savePinnedPaths(repoRoot, "develop", ["develop.ts"]);
        await store.savePinnedPaths("C:\\repos\\Other", "main", ["other.ts"]);

        assert.deepStrictEqual(await store.getPinnedPaths(repoRoot, "main"), ["main.ts"]);
        assert.deepStrictEqual(await store.getPinnedPaths(repoRoot, "develop"), ["develop.ts"]);
        assert.deepStrictEqual(await store.getPinnedPaths("C:\\repos\\Other", "main"), ["other.ts"]);
    });

    test("repository partition keys are case-insensitive on Windows", async function () {
        if (process.platform !== "win32") {
            this.skip();
            return;
        }

        await store.savePinnedPaths(repoRoot, "main", ["main.ts"]);
        assert.deepStrictEqual(await store.getPinnedPaths(repoRoot.toUpperCase(), "main"), ["main.ts"]);
    });

    test("clearPinnedPaths removes a branch's saved set", async () => {
        await store.savePinnedPaths(repoRoot, "main", ["main.ts"]);
        await store.clearPinnedPaths(repoRoot, "main");

        assert.strictEqual(await store.hasState(repoRoot, "main"), false);
        assert.deepStrictEqual(await store.getPinnedPaths(repoRoot, "main"), []);
    });

    test("clearPinnedPaths on an unknown repository does not throw", async () => {
        await assert.doesNotReject(() => store.clearPinnedPaths(repoRoot, "main"));
    });

    test("removeMissingBranchStates deletes only ordinary branches absent from the existing set", async () => {
        await store.savePinnedPaths(repoRoot, "main", ["main.ts"]);
        await store.savePinnedPaths(repoRoot, "stale-branch", ["stale.ts"]);
        await store.savePinnedPaths(repoRoot, "set/shared", ["shared.ts"]);
        await store.savePinnedPaths(repoRoot, "detached/abc1234", ["detached.ts"]);

        const removedCount = await store.removeMissingBranchStates(repoRoot, new Set(["main"]));

        assert.strictEqual(removedCount, 1);
        assert.strictEqual(await store.hasState(repoRoot, "main"), true);
        assert.strictEqual(await store.hasState(repoRoot, "stale-branch"), false);
        assert.strictEqual(await store.hasState(repoRoot, "set/shared"), true);
        assert.strictEqual(await store.hasState(repoRoot, "detached/abc1234"), true);
    });

    test("ensureFileExists creates an empty state file only once", async () => {
        await store.ensureFileExists();
        assert.deepStrictEqual(await store.getPinnedPaths(repoRoot, "main"), []);

        await store.savePinnedPaths(repoRoot, "main", ["main.ts"]);
        await store.ensureFileExists();
        assert.deepStrictEqual(await store.getPinnedPaths(repoRoot, "main"), ["main.ts"]);
    });

    test("concurrent saves to the same store do not lose either write", async () => {
        await Promise.all([
            store.savePinnedPaths(repoRoot, "main", ["main.ts"]),
            store.savePinnedPaths(repoRoot, "develop", ["develop.ts"]),
        ]);

        assert.deepStrictEqual(await store.getPinnedPaths(repoRoot, "main"), ["main.ts"]);
        assert.deepStrictEqual(await store.getPinnedPaths(repoRoot, "develop"), ["develop.ts"]);
    });
});
