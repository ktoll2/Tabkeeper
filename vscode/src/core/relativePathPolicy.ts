import * as path from "path";

/**
 * The single rule for which paths may be persisted or transferred: non-blank, relative, and unable
 * to escape the repository root. Shared by the saved-state store and the portable export format so
 * both reject the same unsafe input.
 */
export function isSafeRelativePath(candidate: string): boolean {
    if (!candidate || candidate.trim().length === 0) {
        return false;
    }

    if (path.isAbsolute(candidate)) {
        return false;
    }

    return !candidate.split(/[\\/]/).some((segment) => segment === "..");
}

/**
 * Keeps only safe paths, then de-duplicates and orders them case-insensitively so the persisted
 * form is stable regardless of input order or casing.
 */
export function normalizeRelativePaths(paths: Iterable<string>): string[] {
    const seen = new Map<string, string>();
    for (const candidate of paths) {
        if (!isSafeRelativePath(candidate)) {
            continue;
        }

        const key = candidate.toLowerCase();
        if (!seen.has(key)) {
            seen.set(key, candidate);
        }
    }

    return [...seen.values()].sort((a, b) => a.toLowerCase().localeCompare(b.toLowerCase()));
}
