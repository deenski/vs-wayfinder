using Vintagestory.API.MathTools;
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
    public static readonly SegmentStyle Known = new(new Vec4f(0.27f, 0.51f, 0.71f, 0.95f), 2.5, false);

    public static readonly SegmentStyle Unknown = new(new Vec4f(0.75f, 0.75f, 0.75f, 0.55f), 2.0, true);

    public static readonly SegmentStyle Build = new(new Vec4f(1f, 0.62f, 0.12f, 0.95f), 4.5, false);

    public static SegmentStyle For(SegmentKind kind)
    {
        return kind switch
        {
            SegmentKind.Build => Build,
            SegmentKind.Unknown => Unknown,
            _ => Known,
        };
    }
}
