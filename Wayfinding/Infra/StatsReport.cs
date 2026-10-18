using System;
using System.Collections.Generic;
using System.Globalization;

namespace Wayfinding.Infra;

public static class StatsReport
{
    public static string Format(VersionGate gate, RenderGuard guard, int verbosity, PlanMetrics metrics)
    {
        ArgumentNullException.ThrowIfNull(gate);
        ArgumentNullException.ThrowIfNull(guard);
        ArgumentNullException.ThrowIfNull(metrics);
        return string.Join("\n", Lines(gate, guard, verbosity, metrics));
    }

    public static IReadOnlyList<string> Lines(VersionGate gate, RenderGuard guard, int verbosity, PlanMetrics metrics)
    {
        ArgumentNullException.ThrowIfNull(gate);
        ArgumentNullException.ThrowIfNull(guard);
        ArgumentNullException.ThrowIfNull(metrics);

        var snapshot = metrics.Snapshot();
        var lines = new List<string>
        {
            Line("verbosity: " + WayfindingLog.ClampVerbosity(verbosity)),
        };

        foreach (var feature in FeatureCatalog.All)
        {
            if (!gate.IsEvaluated)
            {
                lines.Add(Line(feature + ": n/a"));
                continue;
            }

            var state = gate.State(feature);
            lines.Add(state.Enabled
                ? Line(feature + ": enabled")
                : Line(feature + ": disabled (" + state.Missing + ")"));
        }

        lines.Add(guard.LayerEnabled
            ? Line("render guard: enabled")
            : Line("render guard: disabled (" + guard.Fault + ")"));
        lines.Add(Line(
            "plan ms: last " + Num(snapshot.LastPlanMs)
            + ", avg " + Num(snapshot.AveragePlanMs)
            + ", p95 " + Num(snapshot.P95PlanMs)
            + " (window " + PlanMetrics.WindowSize + ")"));
        lines.Add(Line("nodes expanded: " + Int(snapshot.NodesExpanded)));
        lines.Add(Line("snapshot ms/frame: " + Num(snapshot.SnapshotMsPerFrame)));
        lines.Add(Line(
            "height cache: " + Int(snapshot.HeightCacheColumns)
            + " columns, " + Long(snapshot.HeightCacheBytes) + " bytes"));
        return lines;
    }

    private static string Line(string body)
    {
        return WayfindingLog.PrefixLine(body);
    }

    private static string Num(double? value)
    {
        return value.HasValue
            ? value.Value.ToString("0.###", CultureInfo.InvariantCulture)
            : "n/a";
    }

    private static string Int(int? value)
    {
        return value.HasValue ? value.Value.ToString(CultureInfo.InvariantCulture) : "n/a";
    }

    private static string Long(long? value)
    {
        return value.HasValue ? value.Value.ToString(CultureInfo.InvariantCulture) : "n/a";
    }
}
