using Wayfinding.Pathing.Grid;
using Wayfinding.Pathing.Testing;

namespace Wayfinding.Pathing.Tests;

public class StandabilityTests
{
    // Single column, bottom-up: support, feet, head.
    private static StandKind Column(string bottomUp, int feetY = 1)
    {
        string[][] layers = bottomUp.Select(c => new[] { c.ToString() }).ToArray();
        return Standability.Classify(FakeGrid.FromLayers(layers), new GridPos(0, feetY, 0));
    }

    [Theory]
    [InlineData("#..", StandKind.Ground)]
    [InlineData("#,,", StandKind.Ground)]
    [InlineData("#.#", StandKind.None)]      // no headroom for a 1.85-tall player
    [InlineData("##.", StandKind.None)]      // feet inside a block
    [InlineData("...", StandKind.None)]      // nothing to stand on
    [InlineData("~..", StandKind.None)]      // feet above water: you'd be swimming one lower
    [InlineData("#~.", StandKind.Swim)]      // wading / surface swimming
    [InlineData("~~.", StandKind.Swim)]
    [InlineData("~~~", StandKind.None)]      // fully submerged: no underwater routing
    [InlineData("#L.", StandKind.Dangerous)]
    [InlineData("L..", StandKind.Dangerous)]
    [InlineData("#.!", StandKind.Dangerous)]
    [InlineData("?..", StandKind.Unknown)]
    [InlineData("#.?", StandKind.Unknown)]
    public void Classify(string column, StandKind expected, int feetY = 1) =>
        Assert.Equal(expected, Column(column, feetY));

    [Fact]
    public void Classify_TopOfGrid_IsUnknownNotGround()
    {
        // Head cell is outside the snapshot: must not be assumed to be air.
        Assert.Equal(StandKind.Unknown, Column("#.", 1));
    }
}
