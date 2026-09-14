import * as path from "path";
import * as vscode from "vscode";

/** The fixed on-disk locations for Tabkeeper's user-local data. */
export interface TabkeeperPaths {
    readonly stateFile: string;
    readonly activityLogFile: string;
}

/** Resolves Tabkeeper's data file paths under the extension's global storage directory. */
export function getTabkeeperPaths(context: vscode.ExtensionContext): TabkeeperPaths {
    const directory = context.globalStorageUri.fsPath;
    return {
        stateFile: path.join(directory, "pinned-tabs.json"),
        activityLogFile: path.join(directory, "activity.log"),
    };
}
