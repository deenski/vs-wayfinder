using Wayfinding.Pathing.Grid;

namespace Wayfinding.Snapshot;

/// <summary>
/// Remembers the last per-frame snapshot cost and optionally forwards it.
/// </summary>
/// <remarks>
/// <see cref="LastMilliseconds"/> is the latest sample. <see cref="Forward"/> is
/// <c>PlanMetrics.RecordSnapshotMs</c>, which <c>.nav stats</c> prints as snapshot ms/frame.
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
