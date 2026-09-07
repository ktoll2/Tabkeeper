using System;
using System.IO;
using System.Threading;

namespace Tabkeeper.Core;

/// <summary>
/// Primitives for accessing a user-level file that several Visual Studio instances may touch at once:
/// a system-wide advisory lock and a short retry for transient sharing violations. The saved-state
/// store and the activity log both build on these.
/// </summary>
internal static class SharedFileAccess
{
    /// <summary>The default number of attempts before a transient I/O failure is allowed to surface.</summary>
    public const int DefaultMaxAttempts = 4;

    private const int RetryBackoffMilliseconds = 25;

    /// <summary>
    /// Builds a stable <c>Global\</c> mutex name for a file, so every process derives the same name
    /// from the same path. <see cref="string.GetHashCode()"/> cannot be used because it is randomized
    /// per process on modern runtimes.
    /// </summary>
    /// <param name="purpose">A short discriminator, for example <c>PinnedTabState</c> or <c>ActivityLog</c>.</param>
    /// <param name="filePath">The file the lock guards.</param>
    /// <returns>A system-wide mutex name.</returns>
    public static string MutexNameFor(string purpose, string filePath)
    {
        return @"Global\Tabkeeper." + purpose + "." + PathHash.Stable(Path.GetFullPath(filePath).ToUpperInvariant());
    }

    /// <summary>
    /// Acquires a named system-wide mutex, waiting up to <paramref name="timeout"/>. Disposing the
    /// result releases it. If the wait times out the scope is still returned (callers proceed
    /// unsynchronized rather than block Visual Studio); a mutex abandoned by a crashed process is
    /// treated as acquired because every writer here replaces files atomically.
    /// </summary>
    /// <param name="mutexName">The name from <see cref="MutexNameFor"/>.</param>
    /// <param name="timeout">The longest time to wait for the lock.</param>
    /// <returns>A scope that releases the lock when disposed.</returns>
    public static IDisposable Lock(string mutexName, TimeSpan timeout)
    {
        Mutex mutex = new Mutex(false, mutexName);
        bool acquired;
        try
        {
            acquired = mutex.WaitOne(timeout);
        }
        catch (AbandonedMutexException)
        {
            acquired = true;
        }
        catch
        {
            mutex.Dispose();
            throw;
        }

        return new MutexScope(mutex, acquired);
    }

    /// <summary>
    /// Runs <paramref name="operation"/>, retrying on <see cref="IOException"/> with a short linear
    /// back-off. Any other exception, and an <see cref="IOException"/> on the final attempt, surface
    /// to the caller.
    /// </summary>
    /// <typeparam name="T">The operation's result type.</typeparam>
    /// <param name="operation">The file operation to attempt.</param>
    /// <param name="maxAttempts">The number of attempts before an <see cref="IOException"/> is rethrown.</param>
    /// <returns>The operation's result.</returns>
    public static T Retry<T>(Func<T> operation, int maxAttempts = DefaultMaxAttempts)
    {
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                return operation();
            }
            catch (IOException) when (attempt < maxAttempts)
            {
                Thread.Sleep(RetryBackoffMilliseconds * attempt);
            }
        }
    }

    /// <summary>
    /// Runs <paramref name="operation"/> with the same retry policy as <see cref="Retry{T}"/>.
    /// </summary>
    /// <param name="operation">The file operation to attempt.</param>
    /// <param name="maxAttempts">The number of attempts before an <see cref="IOException"/> is rethrown.</param>
    public static void Retry(Action operation, int maxAttempts = DefaultMaxAttempts)
    {
        Retry<object?>(
            () =>
            {
                operation();
                return null;
            },
            maxAttempts);
    }

    private sealed class MutexScope : IDisposable
    {
        private readonly Mutex mutex;
        private readonly bool acquired;

        public MutexScope(Mutex mutex, bool acquired)
        {
            this.mutex = mutex;
            this.acquired = acquired;
        }

        public void Dispose()
        {
            if (acquired)
            {
                try
                {
                    mutex.ReleaseMutex();
                }
                catch (ApplicationException)
                {
                    // The mutex was not held by this thread; nothing to release.
                }
            }

            mutex.Dispose();
        }
    }
}
