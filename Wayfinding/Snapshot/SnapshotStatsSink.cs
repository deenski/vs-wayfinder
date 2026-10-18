using Wayfinding.Pathing.Grid;

namespace Wayfinding.Snapshot;

/// <summary>
/// Remembers the last per-frame snapshot cost and optionally forwards it.
/// </summary>
/// <remarks>
/// <c>.nav stats</c> reads <see cref="LastMilliseconds"/>. KAN-18's <c>PlanMetrics</c> is not on main;
/// set <see cref="Forward"/> to an adapter that calls <c>PlanMetrics.RecordSnapshotMs</c> when it lands.
/// This type does not reference <c>PlanMetrics</c> or <c>WayfindingLog</c>.
/// </remarks>
public sealed class SnapshotStatsSink : ISnapshotFrameStats
{
    /// <summary>Optional next sink. Not used until KAN-18 is on main.</summary>
    public ISnapshotFrameStats? Forward { get; set; }

    /// <summary>Milliseconds of the most recent slice, or null before the first one.</summary>
    public double? LastMilliseconds { get; private set; }

    /// <inheritdoc />
    public void RecordSnapshotMs(double milliseconds)
    {
        LastMilliseconds = milliseconds;
        Forward?.RecordSnapshotMs(milliseconds);
    }
}
