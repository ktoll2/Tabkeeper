import * as assert from "assert";
import * as fs from "fs";
import * as path from "path";
import { exportPinSet, importPinSet } from "../../core/pinSetTransfer";
import { makeTempDir } from "../testUtil";

suite("pinSetTransfer", () => {
    let tempDir: { dir: string; cleanup: () => void };

    setup(() => {
        tempDir = makeTempDir("tabkeeper-pinset");
    });

    teardown(() => {
        tempDir.cleanup();
    });

    test("round-trips branch and paths through export and import", async () => {
        const filePath = path.join(tempDir.dir, "pins.tabkeeper.json");
        await exportPinSet(filePath, "feature/login", ["b.txt", "a.txt"]);

        const imported = await importPinSet(filePath);
        assert.strictEqual(imported.formatVersion, 1);
        assert.strictEqual(imported.sourceBranch, "feature/login");
        assert.deepStrictEqual(imported.pinnedPaths, ["a.txt", "b.txt"]);
    });

    test("export normalizes and validates paths the same way the state store does", async () => {
        const filePath = path.join(tempDir.dir, "pins.tabkeeper.json");
        await exportPinSet(filePath, "main", ["../escape.txt", "b.txt", "b.txt"]);

        const contents = JSON.parse(fs.readFileSync(filePath, "utf8"));
        assert.deepStrictEqual(contents.pinnedPaths, ["b.txt"]);
    });

    test("rejects a file with an unsupported format version", async () => {
        const filePath = path.join(tempDir.dir, "pins.tabkeeper.json");
        fs.writeFileSync(filePath, JSON.stringify({ formatVersion: 2, sourceBranch: "main", pinnedPaths: [] }), "utf8");

        await assert.rejects(() => importPinSet(filePath), /unsupported format/);
    });

    test("tolerates a document missing optional fields", async () => {
        const filePath = path.join(tempDir.dir, "pins.tabkeeper.json");
        fs.writeFileSync(filePath, JSON.stringify({ formatVersion: 1 }), "utf8");

        const imported = await importPinSet(filePath);
        assert.strictEqual(imported.sourceBranch, "");
        assert.deepStrictEqual(imported.pinnedPaths, []);
    });
});
