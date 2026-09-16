import * as assert from "assert";
import * as fs from "fs";
import * as path from "path";
import { retryOnTransientError, withFileLock } from "../../core/fileLock";
import { makeTempDir } from "../testUtil";

function delay(milliseconds: number): Promise<void> {
    return new Promise((resolve) => setTimeout(resolve, milliseconds));
}

suite("fileLock", () => {
    let tempDir: { dir: string; cleanup: () => void };
    let lockPath: string;

    setup(() => {
        tempDir = makeTempDir("tabkeeper-lock");
        lockPath = path.join(tempDir.dir, "state.json.lock");
    });

    teardown(() => {
        tempDir.cleanup();
    });

    suite("withFileLock", () => {
        test("creates the lock file's directory on first use", async () => {
            const nestedLockPath = path.join(tempDir.dir, "nested", "sub", "state.json.lock");
            await withFileLock(nestedLockPath, async () => undefined);
            assert.strictEqual(fs.existsSync(path.dirname(nestedLockPath)), true);
        });

        test("removes the lock file once the caller that acquired it finishes", async () => {
            await withFileLock(lockPath, async () => undefined);
            assert.strictEqual(fs.existsSync(lockPath), false);
        });

        test("returns the operation's result", async () => {
            const result = await withFileLock(lockPath, async () => 42);
            assert.strictEqual(result, 42);
        });

        test("serializes overlapping operations so they never run concurrently", async () => {
            let concurrentCount = 0;
            let maxConcurrentCount = 0;
            const order: number[] = [];

            const run = (id: number) =>
                withFileLock(lockPath, async () => {
                    concurrentCount++;
                    maxConcurrentCount = Math.max(maxConcurrentCount, concurrentCount);
                    await delay(30);
                    order.push(id);
                    concurrentCount--;
                });

            await Promise.all([run(1), run(2), run(3)]);

            assert.strictEqual(maxConcurrentCount, 1);
            assert.deepStrictEqual([...order].sort((a, b) => a - b), [1, 2, 3]);
        });

        test(
            "a caller that times out waiting for the lock does not delete the lock still held by the owner " +
                "(regression: a timed-out caller must not release a lock it never acquired)",
            async () => {
                let lockExistedDuringTimedOutCall = false;

                const owner = withFileLock(lockPath, async () => {
                    await delay(500);
                });

                // Give the owner a moment to actually acquire the lock file first.
                await delay(50);

                const impatient = withFileLock(
                    lockPath,
                    async () => {
                        lockExistedDuringTimedOutCall = fs.existsSync(lockPath);
                    },
                    /* timeoutMs */ 120,
                    /* staleAfterMs */ 60_000,
                );

                await impatient;
                assert.strictEqual(lockExistedDuringTimedOutCall, true, "the owner's lock file must still exist while the impatient caller gave up");
                assert.strictEqual(fs.existsSync(lockPath), true, "the owner's lock file must survive the impatient caller's cleanup");

                await owner;
                assert.strictEqual(fs.existsSync(lockPath), false, "the owner still removes its own lock once it finishes");
            },
        );

        test("reclaims a stale lock left behind by a crashed process", async () => {
            fs.writeFileSync(lockPath, "", "utf8");
            const staleTime = new Date(Date.now() - 60_000);
            fs.utimesSync(lockPath, staleTime, staleTime);

            const result = await withFileLock(lockPath, async () => "acquired", /* timeoutMs */ 5_000, /* staleAfterMs */ 100);

            assert.strictEqual(result, "acquired");
            assert.strictEqual(fs.existsSync(lockPath), false);
        });

        test("still runs the operation, unsynchronized, when it cannot acquire before the timeout", async () => {
            const owner = withFileLock(lockPath, async () => {
                await delay(400);
            });
            await delay(50);

            let ran = false;
            await withFileLock(
                lockPath,
                async () => {
                    ran = true;
                },
                /* timeoutMs */ 100,
                /* staleAfterMs */ 60_000,
            );

            assert.strictEqual(ran, true);
            await owner;
        });
    });

    suite("retryOnTransientError", () => {
        test("retries on a transient error and returns the eventual success", async () => {
            let attempts = 0;
            const result = await retryOnTransientError(async () => {
                attempts++;
                if (attempts < 3) {
                    const error: NodeJS.ErrnoException = new Error("busy");
                    error.code = "EBUSY";
                    throw error;
                }

                return "done";
            });

            assert.strictEqual(result, "done");
            assert.strictEqual(attempts, 3);
        });

        test("rethrows immediately on a non-transient error without further attempts", async () => {
            let attempts = 0;
            await assert.rejects(
                () =>
                    retryOnTransientError(async () => {
                        attempts++;
                        const error: NodeJS.ErrnoException = new Error("not found");
                        error.code = "ENOENT";
                        throw error;
                    }),
                /not found/,
            );

            assert.strictEqual(attempts, 1);
        });

        test("rethrows a transient error once maxAttempts is exhausted", async () => {
            let attempts = 0;
            await assert.rejects(
                () =>
                    retryOnTransientError(async () => {
                        attempts++;
                        const error: NodeJS.ErrnoException = new Error("busy");
                        error.code = "EBUSY";
                        throw error;
                    }, 2),
                /busy/,
            );

            assert.strictEqual(attempts, 2);
        });
    });
});
