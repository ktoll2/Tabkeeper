import * as fs from "fs";
import * as os from "os";
import * as path from "path";

/** Creates a fresh temporary directory for a test and returns it alongside a cleanup function. */
export function makeTempDir(prefix: string): { dir: string; cleanup: () => void } {
    const dir = fs.mkdtempSync(path.join(os.tmpdir(), `${prefix}-`));
    return {
        dir,
        cleanup: () => fs.rmSync(dir, { recursive: true, force: true }),
    };
}
