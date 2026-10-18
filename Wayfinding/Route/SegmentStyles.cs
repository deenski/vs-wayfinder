using Vintagestory.API.MathTools;

namespace Wayfinding;

/// <summary>
/// Colors and stroke for each <see cref="RouteSegmentKind"/>. The draw loop
/// indexes this table; it does not special-case the straight line.
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

    public static SegmentStyle For(RouteSegmentKind kind)
    {
        return kind switch
        {
            RouteSegmentKind.Known => Known,
            RouteSegmentKind.Build => Build,
            _ => Unknown,
        };
    }
}
