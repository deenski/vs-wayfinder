namespace Wayfinding.Tests;

public class LayerSourceTests
{
    [Fact]
    public void Layer_uses_the_hover_reset_and_the_input_guard()
    {
        var layer = File.ReadAllText(Path.Combine(RepoRoot(), "Wayfinding", "Map", "WayfindingMapLayer.cs"));
        Assert.Contains("HoverLabelPolicy.ShouldReset", layer, StringComparison.Ordinal);
        Assert.Contains("HoverLabelPolicy.Show", layer, StringComparison.Ordinal);
        Assert.Contains("MapInputPolicy.AllowsInput(inputDisabled)", layer, StringComparison.Ordinal);
        Assert.Contains("LabelUpload.ShouldUpload", layer, StringComparison.Ordinal);
        Assert.DoesNotContain("snapToPlayer", layer, StringComparison.Ordinal);
        Assert.Contains("DialogType == EnumDialogType.HUD", layer, StringComparison.Ordinal);
        Assert.Contains("targets.Changed += OnTargetChanged", layer, StringComparison.Ordinal);
        Assert.DoesNotContain("mod.Target", layer, StringComparison.Ordinal);
    }

    [Fact]
    public void Segment_styles_name_every_kind_and_default_to_unknown()
    {
        var source = File.ReadAllText(Path.Combine(RepoRoot(), "Wayfinding", "Route", "SegmentStyles.cs"));
        Assert.Contains("SegmentKind.Walk => Known", source, StringComparison.Ordinal);
        Assert.Contains("SegmentKind.Swim => Known", source, StringComparison.Ordinal);
        Assert.Contains("SegmentKind.Build => Build", source, StringComparison.Ordinal);
        Assert.Contains("SegmentKind.Unknown => Unknown", source, StringComparison.Ordinal);
        Assert.Contains("_ => Unknown", source, StringComparison.Ordinal);
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Wayfinding.sln")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException("Could not find Wayfinding.sln above the test output.");
    }
}
