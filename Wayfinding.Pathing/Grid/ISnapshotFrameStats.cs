namespace Wayfinding.Pathing.Grid;

/// <summary>
/// Receives the main-thread milliseconds spent on the snapshot during one tick, so <c>.nav stats</c>
/// can show snapshot ms/frame.
/// </summary>
/// <remarks>
/// Pathing stays free of the mod. <c>PlanMetrics.RecordSnapshotMs</c> on main is the consumer:
/// the game adapter forwards each sample to it, and <c>.nav stats</c> prints
/// <c>snapshot ms/frame</c> from that value.
/// </remarks>
public interface ISnapshotFrameStats
{
    /// <summary>Records the milliseconds the main thread spent inside the snapshot slice this tick.</summary>
    /// <param name="milliseconds">Elapsed time of the slice. Zero is a real measurement, not "unknown".</param>
    void RecordSnapshotMs(double milliseconds);
}
