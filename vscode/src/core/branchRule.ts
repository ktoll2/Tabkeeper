/** Configures a branch glob, its shared pin-set name, and whether synchronization is enabled. */
export interface BranchRuleConfiguration {
    pattern: string;
    pinSetName: string;
    enabled: boolean;
}

/** Tests a branch name against a rule's case-insensitive `*` and `?` glob pattern. */
export function branchRuleMatches(rule: BranchRuleConfiguration, branchName: string): boolean {
    const escaped = rule.pattern.replace(/[.+^${}()|[\]\\]/g, "\\$&").replace(/\*/g, ".*").replace(/\?/g, ".");
    return new RegExp("^" + escaped + "$", "i").test(branchName);
}

/** Finds the longest (most specific) matching configured branch glob. */
export function findMatchingRule(
    rules: readonly BranchRuleConfiguration[],
    branchName: string,
): BranchRuleConfiguration | undefined {
    let best: BranchRuleConfiguration | undefined;
    for (const rule of rules) {
        if (!rule.pattern || rule.pattern.trim().length === 0) {
            continue;
        }

        if (!branchRuleMatches(rule, branchName)) {
            continue;
        }

        if (!best || rule.pattern.length > best.pattern.length) {
            best = rule;
        }
    }

    return best;
}
