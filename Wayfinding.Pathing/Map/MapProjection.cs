namespace Wayfinding.Pathing.Map;

/// <summary>
/// View rectangle for one map element. Block bounds are the world X/Z window
/// (<c>GuiElementMap.CurrentBlockViewBounds</c>). Inner size is the element in pixels.
/// Origin is the element's screen position; pass 0 to get view-local pixels.
/// </summary>
public readonly struct MapViewRect
{
    public double BlockX1 { get; init; }
    public double BlockZ1 { get; init; }
    public double BlockX2 { get; init; }
    public double BlockZ2 { get; init; }
    public double InnerWidth { get; init; }
    public double InnerHeight { get; init; }
    public double OriginX { get; init; }
    public double OriginY { get; init; }

    public double BlockWidth => BlockX2 - BlockX1;
    public double BlockHeight => BlockZ2 - BlockZ1;
}

/// <summary>
/// Affine map from world X/Z onto a map element. This is the same transform as
/// <c>GuiElementMap.TranslateWorldPosToViewPos</c> plus the element origin.
/// A world-coordinate mesh would use <see cref="ViewTransform"/> as its model matrix.
/// </summary>
public static class MapProjection
{
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
    /// Screen position of a world point as <c>origin + world * scale</c>.
    /// Equivalent to translating by <c>-BlockX1/-BlockZ1</c>, scaling into the element, then adding the origin.
    /// </summary>
    public readonly struct ViewTransform
    {
        public float ScaleX { get; init; }
        public float ScaleY { get; init; }
        public float OriginX { get; init; }
        public float OriginY { get; init; }

        public void Apply(double worldX, double worldZ, out float screenX, out float screenY)
        {
            screenX = OriginX + (float)(worldX * ScaleX);
            screenY = OriginY + (float)(worldZ * ScaleY);
        }
    }

    public static bool TryViewTransform(MapViewRect view, out ViewTransform transform)
    {
        transform = default;
        if (!TryScales(view, out double scaleX, out double scaleY))
        {
            return false;
        }

        transform = new ViewTransform
        {
            ScaleX = (float)scaleX,
            ScaleY = (float)scaleY,
            OriginX = (float)(view.OriginX - view.BlockX1 * scaleX),
            OriginY = (float)(view.OriginY - view.BlockZ1 * scaleY),
        };
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
