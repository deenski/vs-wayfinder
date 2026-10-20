using System.Diagnostics;

namespace Wayfinding.Pathing.Grid;

/// <summary>
/// Records the thread that constructed it and, in DEBUG builds, fails if a later call runs elsewhere.
/// </summary>
/// <remarks>
/// The game adapter creates one guard on the main thread when the snapshot builder is created, then
/// calls <see cref="AssertMainThread"/> before every game API call. Release builds compile the check
/// out. The pathing library itself never touches game objects; this guard is the assert the adapter uses.
/// </remarks>
public sealed class MainThreadGuard
{
    private readonly int _threadId;

    /// <summary>Captures <see cref="Environment.CurrentManagedThreadId"/>.</summary>
    public MainThreadGuard()
    {
        _threadId = Environment.CurrentManagedThreadId;
    }

    /// <summary>The managed thread id captured at construction. This is the main thread for a guard created there.</summary>
    public int ThreadId => _threadId;

    /// <summary>
    /// Fails when the current thread is not <see cref="ThreadId"/>.
    /// </summary>
    /// <remarks>
    /// DEBUG builds call <see cref="Debug.Assert(bool, string)"/> and then throw
    /// <see cref="InvalidOperationException"/>. The method is removed from non-DEBUG call sites.
    /// </remarks>
    /// <exception cref="InvalidOperationException">The current thread is not the thread that created this guard.</exception>
    [Conditional("DEBUG")]
    public void AssertMainThread()
    {
        if (Environment.CurrentManagedThreadId == _threadId)
        {
            return;
        }

        const string message = "Snapshot builder touched game objects off the main thread.";
        try
        {
            Debug.Assert(false, message);
        }
        catch (Exception)
        {
            // The test host installs a listener that throws from Debug.Assert.
            // Callers still see InvalidOperationException.
        }

        throw new InvalidOperationException(message);
    }
}
