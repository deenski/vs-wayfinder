using Wayfinding.Pathing.Map;

namespace Wayfinding.Pathing.Tests;

public class MapLabelPolicyTests
{
    [Fact]
    public void Hover_shows_only_while_a_target_exists_and_the_layer_is_enabled()
    {
        Assert.True(HoverLabelPolicy.Show(hasTarget: true, layerEnabled: true));
        Assert.False(HoverLabelPolicy.Show(hasTarget: false, layerEnabled: true));
        Assert.False(HoverLabelPolicy.Show(hasTarget: true, layerEnabled: false));
    }

    [Fact]
    public void Hover_resets_when_the_target_changes_clears_or_the_layer_disables()
    {
        Assert.False(HoverLabelPolicy.ShouldReset(seenRevision: 3, targetRevision: 3, hasTarget: true, layerEnabled: true));
        Assert.True(HoverLabelPolicy.ShouldReset(seenRevision: 3, targetRevision: 4, hasTarget: true, layerEnabled: true));
        Assert.True(HoverLabelPolicy.ShouldReset(seenRevision: 4, targetRevision: 4, hasTarget: false, layerEnabled: true));
        Assert.True(HoverLabelPolicy.ShouldReset(seenRevision: 4, targetRevision: 4, hasTarget: true, layerEnabled: false));
    }

    [Fact]
    public void A_disabled_input_guard_ignores_clicks()
    {
        Assert.True(MapInputPolicy.AllowsInput(inputDisabled: false));
        Assert.False(MapInputPolicy.AllowsInput(inputDisabled: true));
    }

    [Fact]
    public void Distance_texture_uploads_on_a_new_distance_after_the_interval()
    {
        Assert.True(LabelUpload.ShouldUpload(meters: 10, uploadedMeters: -1, nowMs: 0, uploadedAtMs: 0));
        Assert.False(LabelUpload.ShouldUpload(meters: 10, uploadedMeters: 10, nowMs: 1_000, uploadedAtMs: 0));
        Assert.False(LabelUpload.ShouldUpload(meters: 11, uploadedMeters: 10, nowMs: 100, uploadedAtMs: 0));
        Assert.True(LabelUpload.ShouldUpload(meters: 11, uploadedMeters: 10, nowMs: LabelUpload.MinIntervalMs, uploadedAtMs: 0));
    }
}
