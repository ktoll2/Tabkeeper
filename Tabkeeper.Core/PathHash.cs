using System.Globalization;

namespace Tabkeeper.Core;

/// <summary>
/// Produces process-independent hashes for naming system-wide synchronization primitives.
/// <see cref="string.GetHashCode()"/> is randomized per process on modern runtimes and cannot be used.
/// </summary>
internal static class PathHash
{
    /// <summary>
    /// Returns an eight-character hexadecimal FNV-1a hash of <paramref name="value"/>.
    /// </summary>
    /// <param name="value">The text to hash.</param>
    /// <returns>A stable eight-character hexadecimal hash.</returns>
    public static string Stable(string value)
    {
        uint hash = 2166136261;
        foreach (char character in value)
        {
            hash = (hash ^ character) * 16777619;
        }

        return hash.ToString("x8", CultureInfo.InvariantCulture);
    }
}
