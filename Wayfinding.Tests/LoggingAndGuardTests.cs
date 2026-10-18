using Wayfinding.Infra;

namespace Wayfinding.Tests;

public class LoggingAndGuardTests
{
    [Fact]
    public void Lines_carry_the_prefix_once()
    {
        var sink = new ListSink();
        var log = new WayfindingLog(sink);

        log.Log(LogLevel.Notice, "version gate ready");
        log.Log(LogLevel.Warning, "[wayfinding] already tagged");

        Assert.Equal("[wayfinding] version gate ready", sink.Lines[0].Line);
        Assert.Equal("[wayfinding] already tagged", sink.Lines[1].Line);
        Assert.Equal("version gate ready", WayfindingLog.WithoutPrefix(sink.Lines[0].Line));
    }

    [Fact]
    public void LogOnce_writes_a_key_one_time()
    {
        var sink = new ListSink();
        var log = new WayfindingLog(sink);

        log.LogOnce("render-guard", LogLevel.Error, "first");
        log.LogOnce("render-guard", LogLevel.Error, "second");

        var line = Assert.Single(sink.Lines);
        Assert.Equal("[wayfinding] first", line.Line);
    }

    [Fact]
    public void LogOnce_from_parallel_threads_writes_exactly_once()
    {
        var sink = new ParallelSink();
        var log = new WayfindingLog(sink);
        const int threads = 32;
        using var start = new Barrier(threads);
        var workers = new Thread[threads];
        for (var i = 0; i < threads; i++)
        {
            workers[i] = new Thread(() =>
            {
                start.SignalAndWait();
                log.LogOnce("gate:parallel", LogLevel.Warning, "parallel");
            });
        }

        foreach (var worker in workers)
        {
            worker.Start();
        }

        foreach (var worker in workers)
        {
            Assert.True(worker.Join(TimeSpan.FromSeconds(10)));
        }

        Assert.Equal(1, sink.Count);
        Assert.Equal("[wayfinding] parallel", sink.OnlyLine);
    }

    [Fact]
    public void Debug_lines_follow_verbosity()
    {
        var sink = new ListSink();
        var log = new WayfindingLog(sink, verbosity: 0);

        log.Log(LogLevel.Debug, "hidden");
        log.Log(LogLevel.Warning, "shown");
        log.Verbosity = 1;
        log.Log(LogLevel.Debug, "visible");
        log.Verbosity = 9;
        log.Log(LogLevel.Trace, "trace");

        Assert.Equal(3, log.Verbosity);
        Assert.Equal(3, sink.Lines.Count);
        Assert.Equal("[wayfinding] shown", sink.Lines[0].Line);
        Assert.Equal("[wayfinding] visible", sink.Lines[1].Line);
        Assert.Equal("[wayfinding] trace", sink.Lines[2].Line);
    }

    [Fact]
    public void Render_exception_is_logged_once_and_leaves_other_layers_alone()
    {
        var sink = new ListSink();
        var log = new WayfindingLog(sink);
        var guard = new RenderGuard(log);
        var other = new RenderGuard(log);
        var otherHudStillDrawn = false;
        var callsAfterDisable = 0;

        guard.Draw(() => throw new InvalidOperationException("boom"));
        guard.Draw(() =>
        {
            callsAfterDisable++;
            throw new InvalidOperationException("boom again");
        });
        other.Draw(() => otherHudStillDrawn = true);

        Assert.False(guard.LayerEnabled);
        Assert.Equal(0, callsAfterDisable);
        Assert.True(other.LayerEnabled);
        Assert.True(otherHudStillDrawn);
        var line = Assert.Single(sink.Lines);
        Assert.Equal(LogLevel.Error, line.Level);
        Assert.StartsWith("[wayfinding] ", line.Line, StringComparison.Ordinal);
        Assert.Contains("boom", line.Line, StringComparison.Ordinal);
    }

    private sealed class ParallelSink : ILogSink
    {
        private readonly object gate = new();
        private int count;
        private string? line;

        public int Count
        {
            get
            {
                lock (gate)
                {
                    return count;
                }
            }
        }

        public string? OnlyLine
        {
            get
            {
                lock (gate)
                {
                    return line;
                }
            }
        }

        public void Write(LogLevel level, string written)
        {
            lock (gate)
            {
                count++;
                line = written;
            }
        }
    }
}
