using Wayfinding.Infra;

namespace Wayfinding.Tests;

public class StatsReportTests
{
    [Fact]
    public void Unfed_planner_metrics_print_as_na()
    {
        var gate = new VersionGate();
        gate.Evaluate(new FakeProbe(), new WayfindingLog(new ListSink()));
        var report = StatsReport.Format(gate, new RenderGuard(new WayfindingLog(new ListSink())), 0, new PlanMetrics());

        Assert.Contains("[wayfinding] verbosity: 0", report, StringComparison.Ordinal);
        Assert.Contains("[wayfinding] MapLayer: enabled", report, StringComparison.Ordinal);
        Assert.Contains("[wayfinding] render guard: enabled", report, StringComparison.Ordinal);
        Assert.Contains("[wayfinding] plan ms: last n/a, avg n/a, p95 n/a (window 100)", report, StringComparison.Ordinal);
        Assert.Contains("[wayfinding] nodes expanded: n/a", report, StringComparison.Ordinal);
        Assert.Contains("[wayfinding] snapshot ms/frame: n/a", report, StringComparison.Ordinal);
        Assert.Contains("[wayfinding] height cache: n/a columns, n/a bytes", report, StringComparison.Ordinal);
    }

    [Fact]
    public void Fed_metrics_replace_na()
    {
        var metrics = new PlanMetrics();
        metrics.RecordPlan(10, 40);
        metrics.RecordPlan(30, 80);
        metrics.RecordSnapshotMs(1.25);
        metrics.SetHeightCache(12, 4096);

        var snapshot = metrics.Snapshot();
        Assert.Equal(30, snapshot.LastPlanMs);
        Assert.Equal(20, snapshot.AveragePlanMs);
        Assert.Equal(30, snapshot.P95PlanMs);
        Assert.Equal(80, snapshot.NodesExpanded);

        var gate = new VersionGate();
        var report = StatsReport.Format(gate, new RenderGuard(new WayfindingLog(new ListSink())), 2, metrics);
        Assert.Contains("[wayfinding] MapLayer: n/a", report, StringComparison.Ordinal);
        Assert.Contains("[wayfinding] verbosity: 2", report, StringComparison.Ordinal);
        Assert.Contains("last 30, avg 20, p95 30 (window 100)", report, StringComparison.Ordinal);
        Assert.Contains("[wayfinding] nodes expanded: 80", report, StringComparison.Ordinal);
        Assert.Contains("[wayfinding] snapshot ms/frame: 1.25", report, StringComparison.Ordinal);
        Assert.Contains("[wayfinding] height cache: 12 columns, 4096 bytes", report, StringComparison.Ordinal);
    }

    [Fact]
    public void Plan_window_keeps_the_last_100_and_reports_p95()
    {
        var metrics = new PlanMetrics();
        for (var i = 1; i <= 101; i++)
        {
            metrics.RecordPlan(i, i);
        }

        var snapshot = metrics.Snapshot();
        Assert.Equal(101, snapshot.LastPlanMs);
        Assert.Equal(51.5, snapshot.AveragePlanMs);
        Assert.Equal(96, snapshot.P95PlanMs);
        Assert.Equal(101, snapshot.NodesExpanded);
    }
}
