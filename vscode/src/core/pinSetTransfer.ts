import * as fs from "fs";
import { normalizeRelativePaths } from "./relativePathPolicy";

/** Portable, validated representation of a saved pin set. */
export interface PinSetTransfer {
    formatVersion: number;
    sourceBranch: string;
    pinnedPaths: string[];
}

/** Exports validated repository-relative paths to a portable pin-set JSON document. */
export async function exportPinSet(filePath: string, sourceBranch: string, pinnedPaths: Iterable<string>): Promise<void> {
    const transfer: PinSetTransfer = {
        formatVersion: 1,
        sourceBranch,
        pinnedPaths: normalizeRelativePaths(pinnedPaths),
    };
    await fs.promises.writeFile(filePath, JSON.stringify(transfer, null, 2), "utf8");
}

/** Imports a supported pin-set document and removes unsafe or duplicate paths. */
export async function importPinSet(filePath: string): Promise<PinSetTransfer> {
    const contents = await fs.promises.readFile(filePath, "utf8");
    const parsed = JSON.parse(contents) as Partial<PinSetTransfer>;
    if (parsed.formatVersion !== 1) {
        throw new Error("The pin-set file uses an unsupported format.");
    }

    return {
        formatVersion: 1,
        sourceBranch: parsed.sourceBranch ?? "",
        pinnedPaths: normalizeRelativePaths(parsed.pinnedPaths ?? []),
    };
}
