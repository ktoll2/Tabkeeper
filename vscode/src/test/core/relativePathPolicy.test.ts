import * as assert from "assert";
import { isSafeRelativePath, normalizeRelativePaths } from "../../core/relativePathPolicy";

suite("relativePathPolicy", () => {
    suite("isSafeRelativePath", () => {
        test("accepts a plain relative path", () => {
            assert.strictEqual(isSafeRelativePath("src/index.ts"), true);
        });

        test("rejects an empty or blank path", () => {
            assert.strictEqual(isSafeRelativePath(""), false);
            assert.strictEqual(isSafeRelativePath("   "), false);
        });

        test("rejects an absolute path", () => {
            assert.strictEqual(isSafeRelativePath("/etc/passwd"), false);
            assert.strictEqual(isSafeRelativePath("C:\\Windows\\System32"), false);
        });

        test("rejects a path that escapes the root via a .. segment", () => {
            assert.strictEqual(isSafeRelativePath("../outside.txt"), false);
            assert.strictEqual(isSafeRelativePath("a/../../outside.txt"), false);
            assert.strictEqual(isSafeRelativePath("a\\..\\..\\outside.txt"), false);
        });

        test("allows a filename that merely contains dots", () => {
            assert.strictEqual(isSafeRelativePath("a/..b.txt"), true);
            assert.strictEqual(isSafeRelativePath("a/b..c.txt"), true);
        });
    });

    suite("normalizeRelativePaths", () => {
        test("drops unsafe paths and keeps safe ones", () => {
            const result = normalizeRelativePaths(["a.txt", "../escape.txt", "b.txt"]);
            assert.deepStrictEqual(result, ["a.txt", "b.txt"]);
        });

        test("de-duplicates case-insensitively, keeping the first-seen casing", () => {
            const result = normalizeRelativePaths(["src/Foo.ts", "src/foo.ts", "src/FOO.ts"]);
            assert.deepStrictEqual(result, ["src/Foo.ts"]);
        });

        test("sorts the result case-insensitively", () => {
            const result = normalizeRelativePaths(["b.txt", "A.txt", "c.txt"]);
            assert.deepStrictEqual(result, ["A.txt", "b.txt", "c.txt"]);
        });
    });
});
