import * as vscode from "vscode";
import { BranchRuleConfiguration } from "./core/branchRule";

export type MissingFileBehavior = "skip" | "showMessage";
export type PinScope = "repository" | "workspaceFolder";
export type DetachedHeadBehavior = "keepCommitPins" | "clearPins";

/** All user-configurable Tabkeeper behavior, read live from VS Code settings. */
export interface TabkeeperConfiguration {
    enabled: boolean;
    pollingIntervalSeconds: number;
    maximumRestoredTabs: number;
    restoreDelayMilliseconds: number;
    preserveActiveDocument: boolean;
    missingFileBehavior: MissingFileBehavior;
    pinScope: PinScope;
    detachedHeadBehavior: DetachedHeadBehavior;
    rules: BranchRuleConfiguration[];

    readonly showsMissingFileMessage: boolean;
    readonly usesWorkspaceFolderScope: boolean;
    readonly clearsDetachedHeadPins: boolean;
}

function clamp(value: number, minimum: number, maximum: number): number {
    return value < minimum ? minimum : value > maximum ? maximum : value;
}

/** Reads the current Tabkeeper configuration from VS Code settings, clamped to supported ranges. */
export function getConfiguration(scope?: vscode.ConfigurationScope): TabkeeperConfiguration {
    const config = vscode.workspace.getConfiguration("tabkeeper", scope);
    const missingFileBehavior = config.get<MissingFileBehavior>("missingFileBehavior", "skip");
    const pinScope = config.get<PinScope>("pinScope", "repository");
    const detachedHeadBehavior = config.get<DetachedHeadBehavior>("detachedHeadBehavior", "keepCommitPins");
    const rawRules = config.get<Array<Partial<BranchRuleConfiguration>>>("rules", []);

    return {
        enabled: config.get<boolean>("enabled", true),
        pollingIntervalSeconds: clamp(config.get<number>("pollingIntervalSeconds", 2), 1, 60),
        maximumRestoredTabs: clamp(config.get<number>("maximumRestoredTabs", 25), 1, 100),
        restoreDelayMilliseconds: clamp(config.get<number>("restoreDelayMilliseconds", 500), 0, 5000),
        preserveActiveDocument: config.get<boolean>("preserveActiveDocument", true),
        missingFileBehavior,
        pinScope,
        detachedHeadBehavior,
        rules: rawRules
            .filter((rule): rule is BranchRuleConfiguration => typeof rule.pattern === "string" && rule.pattern.length > 0)
            .map((rule) => ({
                pattern: rule.pattern!,
                pinSetName: rule.pinSetName ?? "",
                enabled: rule.enabled ?? true,
            })),
        showsMissingFileMessage: missingFileBehavior === "showMessage",
        usesWorkspaceFolderScope: pinScope === "workspaceFolder",
        clearsDetachedHeadPins: detachedHeadBehavior === "clearPins",
    };
}

/** Persists a new value for the "enabled" flag only, used by the Toggle Enabled command. */
export async function setEnabled(enabled: boolean): Promise<void> {
    await vscode.workspace
        .getConfiguration("tabkeeper")
        .update("enabled", enabled, vscode.ConfigurationTarget.Global);
}
