namespace Wayfinding.Pathing.Grid;

/// <summary>
/// One tick of a snapshot build: advance, or drop the job when the slice throws.
/// </summary>
/// <remarks>
/// An exception from <see cref="TimeSlicedSnapshotFill.Advance"/> used to leave the cursor where it
/// was, so the next tick threw the same exception again. This step closes the chunk, returns the
/// exception once, and tells the caller to drop the job. The next <c>Begin</c> starts clean.
/// </remarks>
public static class SnapshotBuildStep
{
    /// <summary>
    /// Runs one slice.
    /// </summary>
    /// <param name="fill">The cursor.</param>
    /// <param name="terrain">The chunk source.</param>
    /// <param name="builder">The snapshot being written.</param>
    /// <param name="budget">Time allowed for this slice.</param>
    /// <param name="stats">Receives the elapsed milliseconds. May be null.</param>
    /// <param name="completed">The finished snapshot when the box is done. Null otherwise.</param>
    /// <param name="error">The exception that dropped the job, or null when the slice ran.</param>
    /// <returns>True when the caller should keep the job and tick again. False when the job is finished or dropped.</returns>
    public static bool TryAdvance(
        TimeSlicedSnapshotFill fill,
        ITerrainSlice terrain,
        BlockGridSnapshotBuilder builder,
        TimeSpan budget,
        ISnapshotFrameStats? stats,
        out BlockGridSnapshot? completed,
        out Exception? error)
    {
        ArgumentNullException.ThrowIfNull(fill);
        ArgumentNullException.ThrowIfNull(terrain);
        ArgumentNullException.ThrowIfNull(builder);
        completed = null;
        error = null;
        try
        {
            SnapshotSlice slice = fill.Advance(terrain, budget, stats);
            if (!slice.Completed)
            {
                return true;
            }

            completed = builder.Build();
            return false;
        }
        catch (Exception ex)
        {
            error = ex;
            try
            {
                terrain.CloseChunk();
            }
            catch (Exception)
            {
                // The slice already failed. Dropping the job matters more than a second fault.
            }

            return false;
        }
    }
}
