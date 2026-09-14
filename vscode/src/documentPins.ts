import * as path from "path";
import * as vscode from "vscode";

/** One open tab paired with the normalized filesystem path of its resource. */
export interface OpenScopedTab {
    readonly tab: vscode.Tab;
    readonly uri: vscode.Uri;
    readonly fsPath: string;
}

/** Extracts the resource URI from a tab's input, when its kind carries one. */
export function getTabResourceUri(tab: vscode.Tab): vscode.Uri | undefined {
    const input = tab.input;
    if (
        input instanceof vscode.TabInputText ||
        input instanceof vscode.TabInputCustom ||
        input instanceof vscode.TabInputNotebook
    ) {
        return input.uri;
    }

    return undefined;
}

/** Enumerates open tabs across all groups whose resource is a descendant of a root directory. */
export function getOpenScopedTabs(rootPath: string): OpenScopedTab[] {
    const normalizedRoot = appendSeparator(path.resolve(rootPath));
    const results: OpenScopedTab[] = [];
    for (const group of vscode.window.tabGroups.all) {
        for (const tab of group.tabs) {
            const uri = getTabResourceUri(tab);
            if (uri?.scheme !== "file") {
                continue;
            }

            const fsPath = path.resolve(uri.fsPath);
            if (startsWithPath(fsPath, normalizedRoot)) {
                results.push({ tab, uri, fsPath });
            }
        }
    }

    return results;
}

/** Activates a tab's resource and sets its pinned state, tolerating tabs that no longer exist. */
export async function setTabPinned(tab: vscode.Tab, uri: vscode.Uri, pinned: boolean): Promise<void> {
    if (tab.isPinned === pinned) {
        return;
    }

    await vscode.window.showTextDocument(uri, {
        viewColumn: tab.group.viewColumn,
        preserveFocus: false,
        preview: false,
    });
    await vscode.commands.executeCommand(pinned ? "workbench.action.pinEditor" : "workbench.action.unpinEditor");
}

/** Unpins every currently open tab inside a root directory. */
export async function unpinAllInScope(rootPath: string): Promise<void> {
    for (const { tab, uri } of getOpenScopedTabs(rootPath)) {
        if (tab.isPinned) {
            await setTabPinned(tab, uri, false);
        }
    }
}

/**
 * Opens a file and pins it. Text documents open through the standard text editor; anything else
 * falls back to VS Code's default editor for the resource.
 */
export async function openAndPin(fullPath: string): Promise<boolean> {
    const uri = vscode.Uri.file(fullPath);
    try {
        const document = await vscode.workspace.openTextDocument(uri);
        await vscode.window.showTextDocument(document, { preview: false, preserveFocus: false });
    } catch {
        try {
            await vscode.commands.executeCommand("vscode.open", uri, { preview: false, preserveFocus: false });
        } catch {
            return false;
        }
    }

    await vscode.commands.executeCommand("workbench.action.pinEditor");
    return true;
}

/** Gets the resource of the tab active before a restore operation. */
export function getActiveResourceUri(): vscode.Uri | undefined {
    const activeTab = vscode.window.tabGroups.activeTabGroup.activeTab;
    return activeTab ? getTabResourceUri(activeTab) : undefined;
}

/** Restores focus to a previously active document when it still exists. */
export async function restoreActiveDocument(uri: vscode.Uri | undefined): Promise<void> {
    if (!uri) {
        return;
    }

    try {
        await vscode.window.showTextDocument(uri, { preview: false, preserveFocus: false });
    } catch {
        // The previously active document no longer exists or cannot be opened; nothing to restore.
    }
}

/** Converts an absolute descendant path into a repository-relative path for persistence. */
export function makeRelativePath(rootPath: string, fullPath: string): string {
    return path.relative(rootPath, fullPath).split(path.sep).join("/");
}

/** Determines whether an absolute path is a descendant of a root directory. */
export function isPathInRoot(fullPath: string, rootPath: string): boolean {
    return startsWithPath(path.resolve(fullPath), appendSeparator(path.resolve(rootPath)));
}

/** Compares normalized filesystem paths using platform-appropriate case sensitivity. */
export function pathsEqual(first: string, second: string): boolean {
    const a = path.resolve(first).replace(/[\\/]+$/, "");
    const b = path.resolve(second).replace(/[\\/]+$/, "");
    return process.platform === "win32" || process.platform === "darwin" ? a.toLowerCase() === b.toLowerCase() : a === b;
}

function appendSeparator(candidate: string): string {
    return candidate.endsWith(path.sep) ? candidate : candidate + path.sep;
}

function startsWithPath(candidate: string, normalizedRoot: string): boolean {
    const a = candidate + (candidate.endsWith(path.sep) ? "" : path.sep);
    return process.platform === "win32" || process.platform === "darwin"
        ? a.toLowerCase().startsWith(normalizedRoot.toLowerCase())
        : a.startsWith(normalizedRoot);
}
