using Vintagestory.API.MathTools;
using Wayfinding.Infra;
using Wayfinding.Pathing.Search;

namespace Wayfinding;

/// <summary>
/// Stroke for each <see cref="SegmentKind"/>. Walk and swim share Known.
/// The unknown tail, including a straight line that may cross solid blocks, uses Unknown.
/// </summary>
public readonly struct SegmentStyle
{
    public SegmentStyle(Vec4f color, double thickness, bool dashed)
    {
        Color = color;
        Thickness = thickness;
        Dashed = dashed;
    }

    public Vec4f Color { get; }

    public double Thickness { get; }

    public bool Dashed { get; }
}

public static class SegmentStyles
{
    public static SegmentStyle For(SegmentKind kind, WayfindingClientConfig config)
    {
        var known = new SegmentStyle(ColorOf(config.knownColor), config.knownThickness, false);
        var unknown = new SegmentStyle(ColorOf(config.unknownColor), config.unknownThickness, true);
        var build = new SegmentStyle(ColorOf(config.buildColor), config.buildThickness, false);
        return kind switch
        {
            SegmentKind.Walk => known,
            SegmentKind.Swim => known,
            SegmentKind.Build => build,
            SegmentKind.Unknown => unknown,
            _ => unknown,
        };
    }

    private static Vec4f ColorOf(string text)
    {
        if (!WayfindingConfig.TryColor(text, out var r, out var g, out var b, out var a))
        {
            WayfindingConfig.TryColor(WayfindingConfig.DefaultUnknownColor, out r, out g, out b, out a);
        }

        return new Vec4f(r, g, b, a);
    }
}
