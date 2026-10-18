namespace Wayfinding.Pathing.Grid;

/// <summary>
/// An integer block coordinate in world space (the same X/Y/Z as the game's absolute block position,
/// Y up). Positions used as player positions refer to the cell the player's feet occupy.
/// </summary>
/// <param name="X">Block X (east is +X in Vintage Story).</param>
/// <param name="Y">Block Y (up).</param>
/// <param name="Z">Block Z (south is +Z in Vintage Story).</param>
public readonly record struct GridPos(int X, int Y, int Z)
{
    /// <summary>Returns this position moved by the given offsets.</summary>
    /// <param name="dx">X offset.</param>
    /// <param name="dy">Y offset.</param>
    /// <param name="dz">Z offset.</param>
    public GridPos Offset(int dx, int dy, int dz) => new(X + dx, Y + dy, Z + dz);

    /// <summary>The same column, one block up.</summary>
    public GridPos Up => new(X, Y + 1, Z);

    /// <summary>The same column, one block down.</summary>
    public GridPos Down => new(X, Y - 1, Z);

    /// <summary>Euclidean distance between the two positions in the horizontal (X/Z) plane, ignoring Y.</summary>
    /// <param name="a">First position.</param>
    /// <param name="b">Second position.</param>
    public static double HorizontalDistance(GridPos a, GridPos b)
    {
        double dx = (double)b.X - a.X;
        double dz = (double)b.Z - a.Z;
        return Math.Sqrt(dx * dx + dz * dz);
    }

    /// <inheritdoc />
    public override string ToString() => $"({X}, {Y}, {Z})";
}
