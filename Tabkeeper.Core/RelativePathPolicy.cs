using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Tabkeeper.Core;

/// <summary>
/// The single rule for which paths may be persisted or transferred: non-blank, relative, and unable
/// to escape the repository root. Shared by the saved-state store and the portable export format so
/// both reject the same unsafe input.
/// </summary>
internal static class RelativePathPolicy
{
    /// <summary>
    /// Determines whether a path is non-blank, relative, and contains no parent-directory segment.
    /// </summary>
    /// <param name="path">The candidate repository-relative path.</param>
    /// <returns><see langword="true"/> when the path is safe to persist.</returns>
    public static bool IsSafe(string path)
    {
        return !string.IsNullOrWhiteSpace(path)
            && !Path.IsPathRooted(path)
            && !path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Any(segment => segment == "..");
    }

    /// <summary>
    /// Keeps only safe paths, then de-duplicates and orders them case-insensitively so the persisted
    /// form is stable regardless of input order or casing.
    /// </summary>
    /// <param name="paths">Candidate repository-relative paths.</param>
    /// <returns>Sorted, distinct, safe repository-relative paths.</returns>
    public static IEnumerable<string> Normalize(IEnumerable<string> paths)
    {
        return paths
            .Where(IsSafe)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase);
    }
}
