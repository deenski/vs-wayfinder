using Wayfinding.Pathing.Grid;

namespace Wayfinding.Pathing.Tests;

public class ChunkReadProtocolTests
{
    [Fact]
    public void Open_IsUnpackThenLock_AndCloseReleases()
    {
        var log = new StepLog();
        var reader = new FakeReader(readable: true);
        Assert.True(ChunkReadProtocol.TryOpen(reader, log));
        Assert.Equal(1, reader.Locks);
        ChunkReadProtocol.Read(log);
        ChunkReadProtocol.Close(reader, log);

        Assert.Equal(
            [ChunkReadStep.UnpackReadOnly, ChunkReadStep.AcquireLock, ChunkReadStep.Read, ChunkReadStep.ReleaseLock],
            log.Steps);
        Assert.Equal(0, reader.Locks);
    }

    [Fact]
    public void UnconfirmedChunk_DoesNotTakeTheLock()
    {
        var log = new StepLog();
        var reader = new FakeReader(readable: true);
        Assert.False(ChunkReadProtocol.TryOpen(reader, log, confirmReadable: () => false));
        Assert.Equal([ChunkReadStep.UnpackReadOnly], log.Steps);
        Assert.Equal(0, reader.Locks);
    }

    [Fact]
    public void PackedChunk_DoesNotTakeTheLock()
    {
        var log = new StepLog();
        var reader = new FakeReader(readable: false);
        Assert.False(ChunkReadProtocol.TryOpen(reader, log));
        Assert.Equal([ChunkReadStep.UnpackReadOnly], log.Steps);
        Assert.Equal(0, reader.Locks);
    }

    [Fact]
    public void Close_RunsWhenTheBodyThrows()
    {
        var log = new StepLog();
        var reader = new FakeReader(readable: true);
        Assert.True(ChunkReadProtocol.TryOpen(reader, log));
        var thrown = Assert.Throws<InvalidOperationException>((Action)(() =>
        {
            try
            {
                ChunkReadProtocol.Read(log);
                throw new InvalidOperationException("read failed");
            }
            finally
            {
                ChunkReadProtocol.Close(reader, log);
            }
        }));

        Assert.Equal("read failed", thrown.Message);
        Assert.Equal(ChunkReadStep.ReleaseLock, log.Steps[^1]);
        Assert.Equal(0, reader.Locks);
    }

    private sealed class StepLog : IChunkReadLog
    {
        public List<ChunkReadStep> Steps { get; } = [];

        public void Record(ChunkReadStep step) => Steps.Add(step);
    }

    private sealed class FakeReader(bool readable) : IChunkReader
    {
        public int Locks { get; private set; }

        public bool UnpackReadOnly() => readable;

        public void AcquireLock() => Locks++;

        public void ReleaseLock() => Locks--;
    }
}
