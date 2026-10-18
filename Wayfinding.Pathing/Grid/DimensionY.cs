namespace Wayfinding.Pathing.Grid;

/// <summary>
/// Converts between in-dimension block Y and the game's internal Y.
/// </summary>
/// <remarks>
/// Vintage Story stores <c>BlockPos.InternalY</c> as <c>Y + dimension * 32768</c>
/// (<see cref="Boundary"/>, the game's <c>BlockPos.DimensionBoundary</c>).
/// <see cref="IBlockGrid"/> coordinates are always the in-dimension Y. The game adapter
/// converts when it calls the game, and converts back before writing a snapshot.
/// </remarks>
public static class DimensionY
{
    /// <summary>Blocks per dimension in the game's internal Y (<c>dim * 32768</c>).</summary>
    public const int Boundary = 32768;

    /// <summary>Converts an in-dimension Y to the game's internal Y.</summary>
    /// <param name="dimension">Game dimension id.</param>
    /// <param name="inDimensionY">Y inside that dimension, with no dimension offset.</param>
    /// <returns>The internal Y.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The internal Y does not fit in an <see cref="int"/>.</exception>
    public static int ToInternal(int dimension, int inDimensionY)
    {
        if (!TryToInternal(dimension, inDimensionY, out int internalY))
        {
            throw new ArgumentOutOfRangeException(nameof(inDimensionY), "The internal Y does not fit in an int.");
        }

        return internalY;
    }

    /// <summary>Converts an in-dimension Y to the game's internal Y.</summary>
    /// <param name="dimension">Game dimension id.</param>
    /// <param name="inDimensionY">Y inside that dimension, with no dimension offset.</param>
    /// <param name="internalY">The internal Y, or 0 when it does not fit in an <see cref="int"/>.</param>
    /// <returns>False when the internal Y does not fit in an <see cref="int"/>.</returns>
    public static bool TryToInternal(int dimension, int inDimensionY, out int internalY)
    {
        long value = (long)inDimensionY + (long)dimension * Boundary;
        if (value < int.MinValue || value > int.MaxValue)
        {
            internalY = 0;
            return false;
        }

        internalY = (int)value;
        return true;
    }

    /// <summary>Removes the dimension offset from an internal Y.</summary>
    /// <param name="dimension">Game dimension id.</param>
    /// <param name="internalY">The game's internal Y.</param>
    /// <returns>The in-dimension Y.</returns>
    public static int ToInDimension(int dimension, int internalY)
    {
        long value = (long)internalY - (long)dimension * Boundary;
        if (value < int.MinValue || value > int.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(internalY), "The in-dimension Y does not fit in an int.");
        }

        return (int)value;
    }

    /// <summary>Floor-divides <paramref name="value"/> by <paramref name="divisor"/>.</summary>
    /// <param name="value">The dividend.</param>
    /// <param name="divisor">The positive divisor.</param>
    /// <returns>The floor quotient.</returns>
    /// <remarks>
    /// Chunk coordinates use this. For non-negative block coordinates it matches the game's
    /// toward-zero <c>block / chunkSize</c>. Vintage Story world coordinates are non-negative.
    /// </remarks>
    public static int FloorDiv(int value, int divisor)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(divisor, 0);
        int quotient = value / divisor;
        int remainder = value % divisor;
        if (remainder < 0)
        {
            quotient--;
        }

        return quotient;
    }
}
