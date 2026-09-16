import * as assert from "assert";
import * as fs from "fs";
import * as path from "path";
import { ActivityLog, ActivityLogLevel, SYSTEM_CONTEXT } from "../../core/activityLog";
import { makeTempDir } from "../testUtil";

suite("ActivityLog", () => {
    let tempDir: { dir: string; cleanup: () => void };
    let logFilePath: string;
    let log: ActivityLog;

    setup(() => {
        tempDir = makeTempDir("tabkeeper-activitylog");
        logFilePath = path.join(tempDir.dir, "nested", "activity.log");
        log = new ActivityLog(logFilePath);
    });

    teardown(() => {
        tempDir.cleanup();
    });

    test("write creates the log directory and appends a formatted line", async () => {
        await log.write(ActivityLogLevel.Warn, { repository: "repo", solution: "sol", branch: "main" }, "Something happened.");

        const contents = fs.readFileSync(logFilePath, "utf8");
        assert.match(contents, /^\[\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d{3} [+-]\d{2}:\d{2}\] \[WARN\] \[repo\] \[sol\] \[main\] - Something happened\.\n$/);
    });

    test("write appends subsequent lines rather than overwriting", async () => {
        await log.write(ActivityLogLevel.Info, SYSTEM_CONTEXT, "first");
        await log.write(ActivityLogLevel.Info, SYSTEM_CONTEXT, "second");

        const lines = fs.readFileSync(logFilePath, "utf8").trim().split("\n");
        assert.strictEqual(lines.length, 2);
        assert.ok(lines[0].endsWith("- first"));
        assert.ok(lines[1].endsWith("- second"));
    });

    test("write collapses embedded newlines so one call is always one line", async () => {
        await log.write(ActivityLogLevel.Error, SYSTEM_CONTEXT, "line one\nline two\r\nline three");

        const lines = fs.readFileSync(logFilePath, "utf8").trim().split("\n");
        assert.strictEqual(lines.length, 1);
        assert.ok(lines[0].endsWith("- line one line two line three"));
    });

    test("ensureFileExists creates the file only when it is missing", async () => {
        assert.strictEqual(fs.existsSync(logFilePath), false);
        await log.ensureFileExists();
        assert.strictEqual(fs.existsSync(logFilePath), true);

        const firstContents = fs.readFileSync(logFilePath, "utf8");
        await log.ensureFileExists();
        assert.strictEqual(fs.readFileSync(logFilePath, "utf8"), firstContents);
    });

    test("filePath exposes the configured log file path", () => {
        assert.strictEqual(log.filePath, logFilePath);
    });
});
