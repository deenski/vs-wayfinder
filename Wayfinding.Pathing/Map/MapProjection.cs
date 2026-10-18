namespace Wayfinding.Pathing.Map;

/// <summary>
/// View rectangle for one map element. Block bounds are the world X/Z window.
/// Inner size is the element in pixels. Origin is the element's screen position;
/// pass 0 to get view-local pixels.
/// </summary>
public readonly struct MapViewRect
{
    /// <summary>West edge of the visible world window, in blocks.</summary>
    public double BlockX1 { get; init; }

    /// <summary>North edge of the visible world window, in blocks.</summary>
    public double BlockZ1 { get; init; }

    /// <summary>East edge of the visible world window, in blocks.</summary>
    public double BlockX2 { get; init; }

    /// <summary>South edge of the visible world window, in blocks.</summary>
    public double BlockZ2 { get; init; }

    /// <summary>Element width in pixels.</summary>
    public double InnerWidth { get; init; }

    /// <summary>Element height in pixels.</summary>
    public double InnerHeight { get; init; }

    /// <summary>Screen X of the element's top-left. Zero yields view-local pixels.</summary>
    public double OriginX { get; init; }

    /// <summary>Screen Y of the element's top-left. Zero yields view-local pixels.</summary>
    public double OriginY { get; init; }

    /// <summary>Width of the world window in blocks.</summary>
    public double BlockWidth => BlockX2 - BlockX1;

    /// <summary>Height of the world window in blocks.</summary>
    public double BlockHeight => BlockZ2 - BlockZ1;
}

/// <summary>
/// The one world-to-view transform. It matches <c>GuiElementMap.TranslateWorldPosToViewPos</c>
/// plus the element origin. Callers use this instead of the game method.
/// </summary>
public static class MapProjection
{
    /// <summary>Projects a world X/Z into the element. Returns false when the view has no area.</summary>
    public static bool TryWorldToView(MapViewRect view, double worldX, double worldZ, out float x, out float y)
    {
        x = 0;
        y = 0;
        if (!TryScales(view, out double scaleX, out double scaleY))
        {
            return false;
        }

        x = (float)(view.OriginX + (worldX - view.BlockX1) * scaleX);
        y = (float)(view.OriginY + (worldZ - view.BlockZ1) * scaleY);
        return true;
    }

    /// <summary>Inverse of <see cref="TryWorldToView"/>. Returns false when the view has no area.</summary>
    public static bool TryViewToWorld(MapViewRect view, float viewX, float viewY, out double worldX, out double worldZ)
    {
        worldX = 0;
        worldZ = 0;
        if (view.InnerWidth == 0 || view.InnerHeight == 0 || view.BlockWidth == 0 || view.BlockHeight == 0)
        {
            return false;
        }

        worldX = (viewX - view.OriginX) * view.BlockWidth / view.InnerWidth + view.BlockX1;
        worldZ = (viewY - view.OriginY) * view.BlockHeight / view.InnerHeight + view.BlockZ1;
        return true;
    }

    /// <summary>
    /// Port of <c>GuiElementMap.ClampButPreserveAngle</c>, including its edge test
    /// (<c>inner - 2</c>, not <c>inner - border</c>). A zero center is left unchanged.
    /// </summary>
    public static void ClampPreserveAngle(ref float x, ref float y, double innerWidth, double innerHeight, int border)
    {
        if (IsInsideClampRegion(x, y, innerWidth, innerHeight, border))
        {
            return;
        }

        double centerX = innerWidth / 2 - border;
        double centerY = innerHeight / 2 - border;
        if (Math.Abs(centerX) < 1e-6 || Math.Abs(centerY) < 1e-6)
        {
            return;
        }

        double relX = (x - centerX) / centerX;
        double relY = (y - centerY) / centerY;
        double factor = Math.Max(Math.Abs(relX), Math.Abs(relY));
        if (factor < 1e-6)
        {
            return;
        }

        x = (float)((x - centerX) / factor + centerX);
        y = (float)((y - centerY) / factor + centerY);
    }

    /// <summary>True when the game's clamp would leave the point where it is.</summary>
    public static bool IsInsideClampRegion(float x, float y, double innerWidth, double innerHeight, int border)
    {
        return x >= border && x <= innerWidth - 2 && y >= border && y <= innerHeight - 2;
    }

    static bool TryScales(MapViewRect view, out double scaleX, out double scaleY)
    {
        scaleX = 0;
        scaleY = 0;
        if (view.BlockWidth == 0 || view.BlockHeight == 0 || view.InnerWidth == 0 || view.InnerHeight == 0)
        {
            return false;
        }

        scaleX = view.InnerWidth / view.BlockWidth;
        scaleY = view.InnerHeight / view.BlockHeight;
        return true;
    }
}
