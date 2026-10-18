namespace Wayfinding.Pathing.Map;

/// <summary>
/// View rectangle for one map element. Block bounds are the world X/Z window
/// (<c>GuiElementMap.CurrentBlockViewBounds</c>). Inner size is the element in pixels.
/// Origin is the element's screen position; pass 0 to get view-local pixels.
/// </summary>
public readonly struct MapViewRect
{
    /// <summary>Western block edge of the view.</summary>
    public double BlockX1 { get; init; }

    /// <summary>Northern block edge of the view.</summary>
    public double BlockZ1 { get; init; }

    /// <summary>Eastern block edge of the view.</summary>
    public double BlockX2 { get; init; }

    /// <summary>Southern block edge of the view.</summary>
    public double BlockZ2 { get; init; }

    /// <summary>Map element width in pixels.</summary>
    public double InnerWidth { get; init; }

    /// <summary>Map element height in pixels.</summary>
    public double InnerHeight { get; init; }

    /// <summary>Element origin X in screen pixels. Zero yields view-local X.</summary>
    public double OriginX { get; init; }

    /// <summary>Element origin Y in screen pixels. Zero yields view-local Y.</summary>
    public double OriginY { get; init; }

    /// <summary>View width in blocks (<see cref="BlockX2"/> minus <see cref="BlockX1"/>).</summary>
    public double BlockWidth => BlockX2 - BlockX1;

    /// <summary>View height in blocks (<see cref="BlockZ2"/> minus <see cref="BlockZ1"/>).</summary>
    public double BlockHeight => BlockZ2 - BlockZ1;
}

/// <summary>
/// Affine map from world X/Z onto a map element. This is the same transform as
/// <c>GuiElementMap.TranslateWorldPosToViewPos</c> plus the element origin.
/// A world-coordinate mesh would use <see cref="ViewTransform"/> as its model matrix.
/// </summary>
public static class MapProjection
{
    /// <summary>Projects a world X/Z into view pixels.</summary>
    /// <param name="view">Map element and the world window it shows.</param>
    /// <param name="worldX">World X.</param>
    /// <param name="worldZ">World Z.</param>
    /// <param name="x">View X. Zero when the view has no scale.</param>
    /// <param name="y">View Y. Zero when the view has no scale.</param>
    /// <returns>False when a block or pixel extent is zero.</returns>
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

    /// <summary>Projects a view pixel back to world X/Z.</summary>
    /// <param name="view">Map element and the world window it shows.</param>
    /// <param name="viewX">View X.</param>
    /// <param name="viewY">View Y.</param>
    /// <param name="worldX">World X. Zero when the view has no scale.</param>
    /// <param name="worldZ">World Z. Zero when the view has no scale.</param>
    /// <returns>False when a block or pixel extent is zero.</returns>
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
        /// <summary>Pixels per block on X.</summary>
        public float ScaleX { get; init; }

        /// <summary>Pixels per block on Z, applied to screen Y.</summary>
        public float ScaleY { get; init; }

        /// <summary>Screen X of world origin after the scale.</summary>
        public float OriginX { get; init; }

        /// <summary>Screen Y of world origin after the scale.</summary>
        public float OriginY { get; init; }

        /// <summary>Applies this transform to a world point.</summary>
        /// <param name="worldX">World X.</param>
        /// <param name="worldZ">World Z.</param>
        /// <param name="screenX">Screen X.</param>
        /// <param name="screenY">Screen Y.</param>
        public void Apply(double worldX, double worldZ, out float screenX, out float screenY)
        {
            screenX = OriginX + (float)(worldX * ScaleX);
            screenY = OriginY + (float)(worldZ * ScaleY);
        }
    }

    /// <summary>Builds the affine form of <see cref="TryWorldToView"/>.</summary>
    /// <param name="view">Map element and the world window it shows.</param>
    /// <param name="transform">The transform. Default when the view has no scale.</param>
    /// <returns>False when a block or pixel extent is zero.</returns>
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
    /// <param name="x">View X, updated in place when the point is outside the clamp region.</param>
    /// <param name="y">View Y, updated in place when the point is outside the clamp region.</param>
    /// <param name="innerWidth">Map element width in pixels.</param>
    /// <param name="innerHeight">Map element height in pixels.</param>
    /// <param name="border">Inset, in pixels, used for the near edges and the center.</param>
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

    /// <summary>True when the point is inside the region <see cref="ClampPreserveAngle"/> leaves alone.</summary>
    /// <param name="x">View X.</param>
    /// <param name="y">View Y.</param>
    /// <param name="innerWidth">Map element width in pixels.</param>
    /// <param name="innerHeight">Map element height in pixels.</param>
    /// <param name="border">Inset, in pixels, used for the near edges.</param>
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
