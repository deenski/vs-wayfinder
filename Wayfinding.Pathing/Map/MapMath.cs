namespace Wayfinding.Pathing.Map;

/// <summary>Horizontal distance and the meter value shown next to an edge arrow.</summary>
public static class MapMath
{
    /// <summary>Euclidean distance in the horizontal plane.</summary>
    public static double HorizontalDistance(double x1, double z1, double x2, double z2)
    {
        double dx = x2 - x1;
        double dz = z2 - z1;
        return Math.Sqrt(dx * dx + dz * dz);
    }

    /// <summary>
    /// Nearest meter, ties away from zero. The result is within half a block of a
    /// non-negative distance, which satisfies the one-block acceptance check.
    /// </summary>
    public static int RoundedMeters(double distance)
    {
        if (double.IsNaN(distance) || distance <= 0)
        {
            return 0;
        }

        return (int)Math.Round(distance, MidpointRounding.AwayFromZero);
    }
}

/// <summary>The on-map distance label, for example "412 m".</summary>
public static class DistanceText
{
    /// <summary>Formats a non-negative meter count.</summary>
    public static string Format(int meters)
    {
        return meters.ToString(System.Globalization.CultureInfo.InvariantCulture) + " m";
    }
}

/// <summary>Square hit test used by map icons.</summary>
public static class MapHit
{
    /// <summary>Axis-aligned square. The edges are outside the icon, matching vanilla (<c>abs &lt; half</c>).</summary>
    public static bool InsideSquare(float iconX, float iconY, float mouseX, float mouseY, float halfExtent)
    {
        return MathF.Abs(iconX - mouseX) < halfExtent && MathF.Abs(iconY - mouseY) < halfExtent;
    }
}
