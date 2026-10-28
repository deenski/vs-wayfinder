using Wayfinding.Infra;
using Wayfinding.Pathing.Grid;

namespace Wayfinding.Snapshot;

/// <summary>
/// Forwards one frame's snapshot cost to <see cref="PlanMetrics.RecordSnapshotMs"/>.
/// </summary>
internal sealed class PlanMetricsFrameStats(PlanMetrics metrics) : ISnapshotFrameStats
{
    /// <inheritdoc />
    public void RecordSnapshotMs(double milliseconds) => metrics.RecordSnapshotMs(milliseconds);
}
