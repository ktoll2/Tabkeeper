import * as fs from "fs";
import * as path from "path";

const RETRY_BACKOFF_MILLISECONDS = 25;
const DEFAULT_MAX_ATTEMPTS = 4;

function delay(milliseconds: number): Promise<void> {
    return new Promise((resolve) => setTimeout(resolve, milliseconds));
}

/**
 * Runs an operation, retrying on a transient filesystem error with a short linear back-off. Any
 * other error, and a transient error on the final attempt, are rethrown.
 */
export async function retryOnTransientError<T>(
    operation: () => Promise<T>,
    maxAttempts: number = DEFAULT_MAX_ATTEMPTS,
): Promise<T> {
    for (let attempt = 1; ; attempt++) {
        try {
            return await operation();
        } catch (error) {
            if (attempt >= maxAttempts || !isTransientFileError(error)) {
                throw error;
            }

            await delay(RETRY_BACKOFF_MILLISECONDS * attempt);
        }
    }
}

function isTransientFileError(error: unknown): boolean {
    const code = (error as NodeJS.ErrnoException)?.code;
    return code === "EBUSY" || code === "EPERM" || code === "EACCES" || code === "EAGAIN";
}

/**
 * Acquires an exclusive lock backed by a sibling `.lock` file, so multiple Code windows sharing the
 * same saved-state file cannot interleave a read-modify-write cycle. Waits up to `timeoutMs`; if the
 * wait times out the caller proceeds unsynchronized rather than blocking indefinitely (a stale lock
 * left by a crashed process is reclaimed after `staleAfterMs`).
 */
export async function withFileLock<T>(
    lockPath: string,
    operation: () => Promise<T>,
    timeoutMs = 10_000,
    staleAfterMs = 30_000,
): Promise<T> {
    const deadline = Date.now() + timeoutMs;
    // The lock file's directory may not exist yet on a fresh install; create it once up front so
    // the very first acquisition attempt below does not fail with ENOENT.
    await fs.promises.mkdir(path.dirname(lockPath), { recursive: true });
    for (;;) {
        try {
            const handle = await fs.promises.open(lockPath, "wx");
            await handle.close();
            break;
        } catch (error) {
            if ((error as NodeJS.ErrnoException).code !== "EEXIST") {
                throw error;
            }

            if (isLockStale(lockPath, staleAfterMs)) {
                try {
                    await fs.promises.unlink(lockPath);
                    continue;
                } catch {
                    // Another process cleared it first; fall through to retry.
                }
            }

            if (Date.now() >= deadline) {
                break;
            }

            await delay(RETRY_BACKOFF_MILLISECONDS);
        }
    }

    try {
        return await operation();
    } finally {
        try {
            await fs.promises.unlink(lockPath);
        } catch {
            // Already removed; nothing to clean up.
        }
    }
}

function isLockStale(lockPath: string, staleAfterMs: number): boolean {
    try {
        const stat = fs.statSync(lockPath);
        return Date.now() - stat.mtimeMs > staleAfterMs;
    } catch {
        return true;
    }
}
