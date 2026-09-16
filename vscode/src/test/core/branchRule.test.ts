import * as assert from "assert";
import { BranchRuleConfiguration, branchRuleMatches, findMatchingRule } from "../../core/branchRule";

function rule(pattern: string, overrides: Partial<BranchRuleConfiguration> = {}): BranchRuleConfiguration {
    return { pattern, pinSetName: "", enabled: true, ...overrides };
}

suite("branchRule", () => {
    suite("branchRuleMatches", () => {
        test("matches an exact, case-insensitive branch name", () => {
            assert.strictEqual(branchRuleMatches(rule("Main"), "main"), true);
            assert.strictEqual(branchRuleMatches(rule("main"), "develop"), false);
        });

        test("* matches any run of characters", () => {
            assert.strictEqual(branchRuleMatches(rule("feature/*"), "feature/login"), true);
            assert.strictEqual(branchRuleMatches(rule("feature/*"), "feature/a/b"), true);
            assert.strictEqual(branchRuleMatches(rule("feature/*"), "bugfix/login"), false);
        });

        test("? matches exactly one character", () => {
            assert.strictEqual(branchRuleMatches(rule("hotfix-?"), "hotfix-1"), true);
            assert.strictEqual(branchRuleMatches(rule("hotfix-?"), "hotfix-12"), false);
        });

        test("escapes regex-special characters in the pattern literally", () => {
            assert.strictEqual(branchRuleMatches(rule("release/1.0"), "release/1.0"), true);
            assert.strictEqual(branchRuleMatches(rule("release/1.0"), "release/1x0"), false);
        });
    });

    suite("findMatchingRule", () => {
        test("returns undefined when nothing matches", () => {
            assert.strictEqual(findMatchingRule([rule("feature/*")], "main"), undefined);
        });

        test("skips rules with a blank pattern", () => {
            assert.strictEqual(findMatchingRule([rule("   ")], "main"), undefined);
        });

        test("picks the most specific (longest pattern) match", () => {
            const general = rule("feature/*", { pinSetName: "general" });
            const specific = rule("feature/login-*", { pinSetName: "specific" });
            const match = findMatchingRule([general, specific], "feature/login-page");
            assert.strictEqual(match?.pinSetName, "specific");
        });

        test("order in the list does not affect which match wins", () => {
            const general = rule("feature/*", { pinSetName: "general" });
            const specific = rule("feature/login-*", { pinSetName: "specific" });
            const match = findMatchingRule([specific, general], "feature/login-page");
            assert.strictEqual(match?.pinSetName, "specific");
        });
    });
});
