import * as fs from "fs";
import * as path from "path";
import { retryOnTransientError, withFileLock } from "./fileLock";

export enum ActivityLogLevel {
    Info = "INFO",
    Warn = "WARN",
    Error = "ERROR",
    /** Diagnostic detail, such as a swallowed transient error. */
    Debug = "DEBUG",
}

/** Repository, workspace, and branch labels rendered on every log line. */
export interface ActivityLogContext {
    repository: string;
    solution: string;
    branch: string;
}

export const SYSTEM_CONTEXT: ActivityLogContext = { repository: "-", solution: "-", branch: "system" };

/**
 * Appends concise, local diagnostic records the user can open in the editor. Each write opens the
 * file, appends one line, and closes it, coordinated by a lock file so multiple Code windows can
 * share one log without interleaving or losing lines.
 */
export class ActivityLog {
    private readonly lockPath: string;

    constructor(private readonly logFilePath: string) {
        this.lockPath = logFilePath + ".lock";
    }

    public get filePath(): string {
        return this.logFilePath;
    }

    public async write(level: ActivityLogLevel, context: ActivityLogContext, message: string): Promise<void> {
        const timestamp = formatTimestamp(new Date());
        const cleanMessage = message.replace(/\r\n|\r|\n/g, " ");
        const line = `[${timestamp}] [${level}] [${context.repository}] [${context.solution}] [${context.branch}] - ${cleanMessage}\n`;
        await this.appendLine(line);
    }

    /** Creates the log lazily when a user opens it before another event has been recorded. */
    public async ensureFileExists(): Promise<void> {
        if (!fs.existsSync(this.logFilePath)) {
            await this.write(ActivityLogLevel.Info, SYSTEM_CONTEXT, "Activity log created.");
        }
    }

    private async appendLine(line: string): Promise<void> {
        try {
            await withFileLock(this.lockPath, async () => {
                await fs.promises.mkdir(path.dirname(this.logFilePath), { recursive: true });
                await retryOnTransientError(() => fs.promises.appendFile(this.logFilePath, line, "utf8"));
            });
        } catch {
            // Diagnostics must never destabilize the extension; a log line that cannot be written is dropped.
        }
    }
}

function formatTimestamp(date: Date): string {
    const pad = (value: number, length = 2): string => value.toString().padStart(length, "0");
    const offsetMinutes = -date.getTimezoneOffset();
    const offsetSign = offsetMinutes >= 0 ? "+" : "-";
    const absoluteOffset = Math.abs(offsetMinutes);
    return (
        `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())} ` +
        `${pad(date.getHours())}:${pad(date.getMinutes())}:${pad(date.getSeconds())}.${pad(date.getMilliseconds(), 3)} ` +
        `${offsetSign}${pad(Math.floor(absoluteOffset / 60))}:${pad(absoluteOffset % 60)}`
    );
}
