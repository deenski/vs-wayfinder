namespace Wayfinding.Pathing.Grid;

/// <summary>
/// Receives the main-thread milliseconds spent on the snapshot during one tick, so <c>.nav stats</c>
/// can show snapshot ms/frame.
/// </summary>
/// <remarks>
/// KAN-18's <c>PlanMetrics.RecordSnapshotMs(double)</c> is the consumer once that type is on main.
/// It is not on main (it lives on PR #5). This interface is the seam. This branch does not copy
/// <c>PlanMetrics</c> or <c>WayfindingLog</c>. A later change forwards
/// <see cref="RecordSnapshotMs"/> to <c>PlanMetrics.RecordSnapshotMs</c>.
/// </remarks>
public interface ISnapshotFrameStats
{
    /// <summary>Records the milliseconds the main thread spent inside the snapshot slice this tick.</summary>
    /// <param name="milliseconds">Elapsed time of the slice. Zero is a real measurement, not "unknown".</param>
    void RecordSnapshotMs(double milliseconds);
}
