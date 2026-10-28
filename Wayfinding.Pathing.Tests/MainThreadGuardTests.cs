using Wayfinding.Pathing.Grid;

namespace Wayfinding.Pathing.Tests;

public class MainThreadGuardTests
{
    [Fact]
    public void CreatingThread_IsRecorded_AndMayCall()
    {
        var guard = new MainThreadGuard();
        Assert.Equal(Environment.CurrentManagedThreadId, guard.ThreadId);
        guard.AssertMainThread();
    }

    [Fact]
    public void OtherThread_FailsInDebug()
    {
        var guard = new MainThreadGuard();
        Exception? caught = null;
        var thread = new Thread(() =>
        {
            try
            {
                guard.AssertMainThread();
            }
            catch (Exception ex)
            {
                caught = ex;
            }
        });
        thread.Start();
        thread.Join();

#if DEBUG
        var error = Assert.IsType<InvalidOperationException>(caught);
        Assert.Contains("main thread", error.Message, StringComparison.OrdinalIgnoreCase);
#else
        Assert.Null(caught);
#endif
    }
}
