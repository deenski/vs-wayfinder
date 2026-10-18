using Wayfinding.Infra;

namespace Wayfinding.Tests;

public class VersionGateTests
{
    [Fact]
    public void Missing_member_disables_only_that_feature_and_notifies_once()
    {
        var sink = new ListSink();
        var log = new WayfindingLog(sink);
        var notice = new NoticeSpy();
        var gate = new VersionGate();
        var probe = new FakeProbe(FeatureCatalog.OwnWaypoints.Display);

        gate.Evaluate(probe, log);
        gate.Notify(notice);
        gate.Evaluate(probe, log);
        gate.Notify(notice);

        Assert.True(gate.IsEnabled(Feature.MapLayer));
        Assert.True(gate.IsEnabled(Feature.MapViewport));
        Assert.False(gate.IsEnabled(Feature.Waypoints));
        Assert.True(gate.IsEnabled(Feature.RemoteTracking));
        Assert.True(gate.IsEnabled(Feature.MinimapEdge));

        var call = Assert.Single(notice.Calls);
        Assert.Equal(Feature.Waypoints, call.Feature);
        Assert.Contains(FeatureCatalog.OwnWaypoints.Display, call.Message, StringComparison.Ordinal);

        var warnings = sink.Lines.Where(line => line.Level == LogLevel.Warning).ToArray();
        var warning = Assert.Single(warnings);
        Assert.StartsWith("[wayfinding] ", warning.Line, StringComparison.Ordinal);
        Assert.Contains("Waypoints disabled", warning.Line, StringComparison.Ordinal);
    }

    [Fact]
    public void Missing_generic_register_method_disables_only_map_layer()
    {
        var gate = Evaluate(FeatureCatalog.RegisterMapLayer.Display);

        Assert.False(gate.IsEnabled(Feature.MapLayer));
        Assert.True(gate.IsEnabled(Feature.MapViewport));
        Assert.True(gate.IsEnabled(Feature.Waypoints));
        Assert.True(gate.IsEnabled(Feature.RemoteTracking));
        Assert.True(gate.IsEnabled(Feature.MinimapEdge));
        Assert.Contains("RegisterMapLayer", gate.State(Feature.MapLayer).Missing, StringComparison.Ordinal);
    }

    [Fact]
    public void Missing_one_viewport_member_disables_only_map_viewport()
    {
        var gate = Evaluate(FeatureCatalog.IsDragingMap.Display);

        Assert.True(gate.IsEnabled(Feature.MapLayer));
        Assert.False(gate.IsEnabled(Feature.MapViewport));
        Assert.True(gate.IsEnabled(Feature.Waypoints));
        Assert.True(gate.IsEnabled(Feature.RemoteTracking));
        Assert.True(gate.IsEnabled(Feature.MinimapEdge));
    }

    [Fact]
    public void Present_members_enable_every_feature_without_a_notice()
    {
        var notice = new NoticeSpy();
        var gate = new VersionGate();
        gate.Evaluate(new FakeProbe(), new WayfindingLog(new ListSink()));
        gate.Notify(notice);

        Assert.All(FeatureCatalog.All, feature => Assert.True(gate.IsEnabled(feature)));
        Assert.Empty(notice.Calls);
    }

    [Fact]
    public void An_unknown_feature_stays_disabled_after_evaluation()
    {
        var gate = new VersionGate();
        gate.Evaluate(new CountingProbe(), new WayfindingLog(new ListSink()));

        Assert.True(gate.IsEvaluated);
        Assert.False(gate.IsEnabled((Feature)42));
        Assert.False(gate.State((Feature)42).Enabled);
    }

    [Fact]
    public void Features_stay_disabled_until_the_gate_runs()
    {
        var gate = new VersionGate();

        Assert.False(gate.IsEvaluated);
        Assert.All(FeatureCatalog.All, feature => Assert.False(gate.IsEnabled(feature)));
        Assert.False(gate.State(Feature.MapLayer).Enabled);
        Assert.False(gate.State((Feature)42).Enabled);
    }

    [Fact]
    public void Evaluation_stays_unpublished_until_every_feature_is_filled()
    {
        var gate = new VersionGate();
        var seen = new List<(bool Evaluated, bool Enabled)>();
        var probe = new ObservingProbe(gate, seen);

        gate.Evaluate(probe, new WayfindingLog(new ListSink()));

        Assert.NotEmpty(seen);
        Assert.All(seen, row =>
        {
            Assert.False(row.Evaluated);
            Assert.False(row.Enabled);
        });
    }

    [Fact]
    public void Evaluate_probes_each_requirement_once_and_logs_one_summary()
    {
        var sink = new ListSink();
        var log = new WayfindingLog(sink);
        var probe = new CountingProbe();
        var gate = new VersionGate();
        var expected = FeatureCatalog.All.Sum(feature => FeatureCatalog.Requirements(feature).Count);

        gate.Evaluate(probe, log);
        gate.Evaluate(probe, log);

        Assert.Equal(expected, probe.Calls);
        Assert.Equal(1, sink.Lines.Count(line => line.Line.Contains("version gate:", StringComparison.Ordinal)));
    }

    [Fact]
    public void Probe_exception_disables_only_the_feature_under_test()
    {
        var sink = new ListSink();
        var gate = new VersionGate();
        gate.Evaluate(new SelectiveThrowProbe(FeatureCatalog.GetAllTrackedPlayerPositions.Display), new WayfindingLog(sink));

        Assert.True(gate.IsEnabled(Feature.MapLayer));
        Assert.False(gate.IsEnabled(Feature.RemoteTracking));
        Assert.True(gate.IsEnabled(Feature.MinimapEdge));
        Assert.Contains(sink.Lines, line => line.Level == LogLevel.Error && line.Line.StartsWith("[wayfinding] ", StringComparison.Ordinal));
    }

    [Fact]
    public void Generic_method_definition_does_not_match_a_plain_method()
    {
        Assert.True(MemberMatcher.HasMember(
            typeof(SampleMap),
            new MemberRequirement("SampleMap", "RegisterMapLayer", MemberKind.GenericMethodDefinition)));
        Assert.False(MemberMatcher.HasMember(
            typeof(SampleMap),
            new MemberRequirement("SampleMap", "RegisterMapLayer", MemberKind.Method)));
        Assert.False(MemberMatcher.HasMember(
            typeof(PlainMap),
            new MemberRequirement("PlainMap", "RegisterMapLayer", MemberKind.GenericMethodDefinition)));
        Assert.True(MemberMatcher.HasMember(
            typeof(SampleMap),
            new MemberRequirement("SampleMap", "DialogType", MemberKind.Property)));
        Assert.False(MemberMatcher.HasMember(
            typeof(SampleMap),
            new MemberRequirement("SampleMap", "DialogType", MemberKind.Field)));
        Assert.True(MemberMatcher.HasMember(
            typeof(SampleEvents),
            new MemberRequirement("SampleEvents", "MouseDown", MemberKind.Event)));
        Assert.False(MemberMatcher.HasMember(
            typeof(SampleEvents),
            new MemberRequirement("SampleEvents", "MouseDown", MemberKind.Method)));
    }

    [Fact]
    public void Missing_mouse_down_disables_only_client_input()
    {
        var gate = Evaluate(FeatureCatalog.MouseDown.Display);

        Assert.False(gate.IsEnabled(Feature.ClientInput));
        Assert.True(gate.IsEnabled(Feature.MapLayer));
        Assert.True(gate.IsEnabled(Feature.MapViewport));
        Assert.True(gate.IsEnabled(Feature.Waypoints));
        Assert.True(gate.IsEnabled(Feature.RemoteTracking));
        Assert.True(gate.IsEnabled(Feature.MinimapEdge));
    }

    [Fact]
    public void Catalog_lists_the_members_the_map_layer_uses()
    {
        Assert.Equal(["MapLayers", "RegisterMapLayer"], Names(Feature.MapLayer));
        Assert.Equal(["CurrentBlockViewBounds", "IsDragingMap"], Names(Feature.MapViewport));
        Assert.Equal(["ownWaypoints", "mapLayers"], Names(Feature.Waypoints));
        Assert.Equal(
            ["GetAllTrackedPlayerPositions", "PlayerUid", "PosX", "PosZ", "Despawn", "AssociatedPlayer", "mapLayers"],
            Names(Feature.RemoteTracking));
        Assert.Equal(["worldMapDlg", "DialogType", "HUD"], Names(Feature.MinimapEdge));
        Assert.Equal(
            ["RegisterHotKey", "SetHotKeyHandler", "GetHotKeyByCode", "MouseDown", "Dialog"],
            Names(Feature.ClientInput));

        var names = FeatureCatalog.All.SelectMany(feature => Names(feature)).ToArray();
        Assert.DoesNotContain("ClampButPreserveAngle", names);
        Assert.DoesNotContain("GetPlayerPositionInformation", names);
    }

    [Fact]
    public void Missing_map_layer_list_disables_only_the_hit_test_features()
    {
        var gate = Evaluate(FeatureCatalog.MapElementLayers.Display);

        Assert.True(gate.IsEnabled(Feature.MapLayer));
        Assert.True(gate.IsEnabled(Feature.MapViewport));
        Assert.False(gate.IsEnabled(Feature.Waypoints));
        Assert.False(gate.IsEnabled(Feature.RemoteTracking));
        Assert.True(gate.IsEnabled(Feature.MinimapEdge));
    }

    [Fact]
    public void Missing_dialog_type_disables_only_the_minimap_edge()
    {
        var gate = Evaluate(FeatureCatalog.DialogType.Display);

        Assert.True(gate.IsEnabled(Feature.MapLayer));
        Assert.True(gate.IsEnabled(Feature.MapViewport));
        Assert.True(gate.IsEnabled(Feature.Waypoints));
        Assert.True(gate.IsEnabled(Feature.RemoteTracking));
        Assert.False(gate.IsEnabled(Feature.MinimapEdge));
    }

    private static string[] Names(Feature feature)
    {
        return FeatureCatalog.Requirements(feature).Select(requirement => requirement.MemberName).ToArray();
    }

    [Fact]
    public void NonPublic_field_still_matches()
    {
        Assert.True(MemberMatcher.HasMember(
            typeof(HiddenWaypoints),
            new MemberRequirement("HiddenWaypoints", "ownWaypoints", MemberKind.Field)));
        Assert.True(MemberMatcher.HasMember(
            typeof(DerivedWaypoints),
            new MemberRequirement("DerivedWaypoints", "ownWaypoints", MemberKind.Field)));
    }

    private static VersionGate Evaluate(params string[] missing)
    {
        var gate = new VersionGate();
        gate.Evaluate(new FakeProbe(missing), new WayfindingLog(new ListSink()));
        return gate;
    }

    private sealed class ObservingProbe : IMemberProbe
    {
        private readonly VersionGate gate;
        private readonly List<(bool Evaluated, bool Enabled)> seen;

        public ObservingProbe(VersionGate gate, List<(bool Evaluated, bool Enabled)> seen)
        {
            this.gate = gate;
            this.seen = seen;
        }

        public bool HasMember(MemberRequirement requirement)
        {
            foreach (var feature in FeatureCatalog.All)
            {
                seen.Add((gate.IsEvaluated, gate.IsEnabled(feature)));
            }

            return true;
        }
    }

    private sealed class CountingProbe : IMemberProbe
    {
        public int Calls { get; private set; }

        public bool HasMember(MemberRequirement requirement)
        {
            Calls++;
            return true;
        }
    }

    private sealed class SelectiveThrowProbe : IMemberProbe
    {
        private readonly string display;

        public SelectiveThrowProbe(string display)
        {
            this.display = display;
        }

        public bool HasMember(MemberRequirement requirement)
        {
            if (requirement.Display == display)
            {
                throw new InvalidOperationException("probe blew up");
            }

            return true;
        }
    }

    private interface SampleEvents
    {
        event Action MouseDown;
    }

    private sealed class SampleMap
    {
        public void RegisterMapLayer<T>(string code, double position)
        {
        }

        public int DialogType { get; set; }
    }

    private sealed class PlainMap
    {
        public void RegisterMapLayer(string code, double position)
        {
        }
    }

    private class HiddenWaypoints
    {
        private readonly List<int> ownWaypoints = new();

        public int Count => ownWaypoints.Count;
    }

    private sealed class DerivedWaypoints : HiddenWaypoints;
}
