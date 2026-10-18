using Wayfinding.Pathing.Grid;

namespace Wayfinding.Pathing.Tests;

public class BlockIdReaderTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void SiblingChunkTypes_ReadInEitherBindOrder(bool clientFirst)
    {
        var reader = new ReflectiveBlockIdReader();
        var client = new ClientChunkData { Solid = 10, Fluid = 20 };
        var server = new ServerChunkData { Solid = 30, Fluid = 40 };
        ChunkDataBase first = clientFirst ? client : server;
        ChunkDataBase second = clientFirst ? server : client;

        Assert.True(reader.TryRead(first, 3, 1, null, out int solid));
        Assert.Equal((clientFirst ? 10 : 30) + 3, solid);
        Assert.True(reader.TryRead(first, 3, 2, null, out int fluid));
        Assert.Equal(clientFirst ? 20 : 40, fluid);

        Assert.True(reader.TryRead(second, 1, 1, null, out int otherSolid));
        Assert.Equal((clientFirst ? 30 : 10) + 1, otherSolid);
        Assert.True(reader.TryRead(second, 1, 2, null, out int otherFluid));
        Assert.Equal(clientFirst ? 40 : 20, otherFluid);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void IndependentlyDeclaredChunkTypes_ReadInEitherBindOrder(bool leftFirst)
    {
        var reader = new ReflectiveBlockIdReader();
        var left = new LeftChunk { Id = 100 };
        var right = new RightChunk { Id = 200 };
        object first = leftFirst ? left : right;
        object second = leftFirst ? right : left;

        Assert.True(reader.TryRead(first, 4, 1, null, out int id));
        Assert.Equal(leftFirst ? 104 : 240, id);
        Assert.True(reader.TryRead(second, 4, 1, null, out int other));
        Assert.Equal(leftFirst ? 240 : 104, other);
    }

    [Fact]
    public void MissingMethod_IsUnknown_LogsOnce_AndDoesNotThrow()
    {
        var reader = new ReflectiveBlockIdReader();
        var logs = new List<string>();
        var data = new NoSuchMethod();

        Assert.False(reader.TryRead(data, 0, 1, logs.Add, out int id));
        Assert.Equal(0, id);
        Assert.False(reader.TryRead(data, 2, 2, logs.Add, out int again));
        Assert.Equal(0, again);
        Assert.Single(logs);
        Assert.Contains(typeof(NoSuchMethod).FullName!, logs[0], StringComparison.Ordinal);
    }

    [Fact]
    public void BindFailure_IsCached_LogsOnce_AndDoesNotThrow()
    {
        var reader = new ReflectiveBlockIdReader();
        var logs = new List<string>();
        var data = new WrongReturn();

        Assert.False(reader.TryRead(data, 0, 1, logs.Add, out _));
        Assert.False(reader.TryRead(data, 0, 1, logs.Add, out _));
        Assert.Single(logs);
    }

    [Fact]
    public void ThrowingRead_DoesNotEscape()
    {
        var reader = new ReflectiveBlockIdReader();
        var data = new ThrowingChunk();

        var read = () => reader.TryRead(data, 0, 1, null, out _);
        Assert.False(read());
        Assert.False(read());
    }

    public class ChunkDataBase
    {
        public int Solid { get; set; }

        public int Fluid { get; set; }

        public int GetBlockIdUnsafe(int index, int layer) => layer == 2 ? Fluid : Solid + index;
    }

    public sealed class ClientChunkData : ChunkDataBase;

    public sealed class ServerChunkData : ChunkDataBase;

    public sealed class LeftChunk
    {
        public int Id { get; set; }

        public int GetBlockIdUnsafe(int index, int layer) => Id + index;
    }

    public sealed class RightChunk
    {
        public int Id { get; set; }

        public int GetBlockIdUnsafe(int index, int layer) => Id + (index * 10);
    }

    public sealed class NoSuchMethod;

    public sealed class WrongReturn
    {
        public string GetBlockIdUnsafe(int index, int layer) => index + "/" + layer;
    }

    public sealed class ThrowingChunk
    {
        public int GetBlockIdUnsafe(int index, int layer) => throw new InvalidOperationException("read failed");
    }
}
