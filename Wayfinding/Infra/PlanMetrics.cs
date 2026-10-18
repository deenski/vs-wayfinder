using System;

namespace Wayfinding.Infra;

public readonly record struct PlanSnapshot(
    double? LastPlanMs,
    double? AveragePlanMs,
    double? P95PlanMs,
    int? NodesExpanded,
    double? SnapshotMsPerFrame,
    int? HeightCacheColumns,
    long? HeightCacheBytes);

public sealed class PlanMetrics
{
    public const int WindowSize = 100;

    private readonly double[] samples = new double[WindowSize];
    private readonly object gate = new();
    private int count;
    private int next;
    private int? nodesExpanded;
    private double? snapshotMsPerFrame;
    private int? heightCacheColumns;
    private long? heightCacheBytes;

    public void RecordPlan(double milliseconds, int nodesExpanded)
    {
        lock (gate)
        {
            samples[next] = milliseconds;
            next = (next + 1) % WindowSize;
            if (count < WindowSize)
            {
                count++;
            }

            this.nodesExpanded = nodesExpanded;
        }
    }

    public void RecordSnapshotMs(double milliseconds)
    {
        lock (gate)
        {
            snapshotMsPerFrame = milliseconds;
        }
    }

    public void SetHeightCache(int columns, long sizeBytes)
    {
        lock (gate)
        {
            heightCacheColumns = columns;
            heightCacheBytes = sizeBytes;
        }
    }

    public PlanSnapshot Snapshot()
    {
        lock (gate)
        {
            if (count == 0)
            {
                return new PlanSnapshot(
                    null,
                    null,
                    null,
                    nodesExpanded,
                    snapshotMsPerFrame,
                    heightCacheColumns,
                    heightCacheBytes);
            }

            var lastIndex = (next - 1 + WindowSize) % WindowSize;
            double sum = 0;
            var ordered = new double[count];
            var start = count == WindowSize ? next : 0;
            for (var i = 0; i < count; i++)
            {
                var value = samples[(start + i) % WindowSize];
                ordered[i] = value;
                sum += value;
            }

            Array.Sort(ordered);
            var rank = (int)Math.Ceiling(0.95 * count) - 1;
            if (rank < 0)
            {
                rank = 0;
            }
            else if (rank >= count)
            {
                rank = count - 1;
            }

            return new PlanSnapshot(
                samples[lastIndex],
                sum / count,
                ordered[rank],
                nodesExpanded,
                snapshotMsPerFrame,
                heightCacheColumns,
                heightCacheBytes);
        }
    }
}
